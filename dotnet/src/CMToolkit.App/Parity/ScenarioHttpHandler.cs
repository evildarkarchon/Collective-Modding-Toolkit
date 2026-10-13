using System.Net;
using System.Text.Json;

namespace CMToolkit.App.Parity;

/// <summary>
/// A Parity Scenario's canned HTTP for the published exe: when the screenshot driver or the smoke test runs the exe on
/// a scenario (<see cref="ScenarioHostEnvironment.ScenarioVariable"/>), the update check is served from the scenario's
/// <c>http/</c> folder and never reaches the network. It follows the tests' <c>ParityHttpHandler</c> rules (responses
/// keyed by logical resource; <c>{"status", "headers"}</c> or <c>{"failure": "timeout" | "connection"}</c>, plus an
/// optional <c>.body</c>), and a test checks the two agree on every committed scenario.
/// </summary>
/// <remarks>
/// A request the scenario has no response for throws <see cref="InvalidOperationException"/> instead of quietly going
/// online. The update check logs it as a failed check (<c>Update Check : ... : Failed</c> with the exception in
/// <c>cm-toolkit.log</c>), since it treats any failure as no update (bug #81). JSON is read with
/// <see cref="JsonDocument"/>, which is trim- and AOT-safe.
/// </remarks>
public sealed class ScenarioHttpHandler : HttpMessageHandler
{
    private readonly string _httpDirectory;

    /// <param name="httpDirectory">The scenario's <c>http/</c> folder; it need not exist.</param>
    public ScenarioHttpHandler(string httpDirectory)
    {
        _httpDirectory = httpDirectory;
    }

    /// <summary>
    /// The handler for the scenario named by <see cref="ScenarioHostEnvironment.ScenarioVariable"/> (the <c>http/</c>
    /// folder beside its <c>scenario.json</c>), or null when the exe isn't running on a scenario.
    /// </summary>
    public static ScenarioHttpHandler? FromEnvironmentVariables()
    {
        var scenario = Environment.GetEnvironmentVariable(ScenarioHostEnvironment.ScenarioVariable);
        var root = Environment.GetEnvironmentVariable(ScenarioHostEnvironment.RootVariable);
        if (string.IsNullOrEmpty(scenario) || string.IsNullOrEmpty(root))
        {
            return null;
        }

        return new ScenarioHttpHandler(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(scenario))!, "http"));
    }

    /// <summary>
    /// The logical resource a URL stands for: <c>nexus-page</c> (NET-1), <c>github-latest-release</c> for any
    /// <c>api.github.com/repos/&lt;owner&gt;/&lt;repo&gt;/releases/latest</c> (NET-2), <c>delta/&lt;file&gt;</c> for
    /// any URL ending in an <c>.xdelta</c> file (NET-3); otherwise null.
    /// </summary>
    public static string? LogicalResource(Uri url)
    {
        var path = url.AbsolutePath.TrimEnd('/');
        if (url.Host.Equals("www.nexusmods.com", StringComparison.OrdinalIgnoreCase)
            && path.Equals("/fallout4/mods/87907", StringComparison.OrdinalIgnoreCase))
        {
            return "nexus-page";
        }

        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (url.Host.Equals("api.github.com", StringComparison.OrdinalIgnoreCase)
            && segments is ["repos", _, _, "releases", "latest"])
        {
            return "github-latest-release";
        }

        if (segments.Length > 0 && segments[^1].EndsWith(".xdelta", StringComparison.OrdinalIgnoreCase))
        {
            return "delta/" + Uri.UnescapeDataString(segments[^1]);
        }

        return null;
    }

    /// <inheritdoc/>
    /// <exception cref="InvalidOperationException">The URL isn't a logical resource, or the scenario has no response for it.</exception>
    /// <exception cref="TaskCanceledException">The canned failure is <c>timeout</c>.</exception>
    /// <exception cref="HttpRequestException">The canned failure is <c>connection</c>.</exception>
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = request.RequestUri ?? throw new InvalidOperationException("The request has no URL.");
        var resource = LogicalResource(url)
                       ?? throw new InvalidOperationException($"{url} isn't a logical resource the parity harness knows.");
        var basePath = Path.Combine(_httpDirectory, resource.Replace('/', Path.DirectorySeparatorChar));
        var metaPath = basePath + ".response.json";
        if (!File.Exists(metaPath))
        {
            throw new InvalidOperationException($"Unscripted HTTP request: the scenario has no {resource}.response.json for {url}.");
        }

        using var meta = JsonDocument.Parse(await File.ReadAllTextAsync(metaPath, cancellationToken));
        if (meta.RootElement.TryGetProperty("failure", out var failure))
        {
            throw failure.GetString() switch
            {
                "timeout" => new TaskCanceledException($"Canned timeout for {resource}.", new TimeoutException()),
                "connection" => new HttpRequestException(HttpRequestError.ConnectionError, $"Canned connection failure for {resource}."),
                var other => new InvalidDataException($"{metaPath}: unknown failure '{other}'; use timeout or connection."),
            };
        }

        var status = meta.RootElement.TryGetProperty("status", out var code) ? code.GetInt32() : 200;
        var bodyPath = basePath + ".body";
        var body = File.Exists(bodyPath) ? await File.ReadAllBytesAsync(bodyPath, cancellationToken) : [];
        // Not seekable, so the content has a Content-Length only when the scenario's headers declare one.
        var content = new StreamContent(new NonSeekableStream(body));
        var response = new HttpResponseMessage((HttpStatusCode)status) { Content = content, RequestMessage = request };
        if (meta.RootElement.TryGetProperty("headers", out var headers))
        {
            foreach (var header in headers.EnumerateObject())
            {
                var value = header.Value.GetString() ?? string.Empty;
                if (!response.Headers.TryAddWithoutValidation(header.Name, value))
                {
                    content.Headers.TryAddWithoutValidation(header.Name, value);
                }
            }
        }

        return response;
    }

    /// <summary>A read-only, forward-only stream over a byte array.</summary>
    private sealed class NonSeekableStream(byte[] data) : MemoryStream(data, writable: false)
    {
        public override bool CanSeek => false;
    }
}
