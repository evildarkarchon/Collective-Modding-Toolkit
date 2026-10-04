using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CMToolkit.Tests.Parity;

/// <summary>
/// A fake <see cref="HttpMessageHandler"/> that serves a scenario's <c>http/</c> folder of canned responses, failures
/// included. Responses are keyed by <b>logical resource</b>, not URL, because where the C# app fetches Delta Patches
/// from is a deployment detail (issue #16): see <see cref="LogicalResource(Uri)"/>.
/// </summary>
/// <remarks>
/// Each resource is <c>http/&lt;resource&gt;.response.json</c>, plus an optional <c>http/&lt;resource&gt;.body</c> with the
/// raw body bytes. The JSON is either <c>{"status": 200, "headers": {...}}</c> or <c>{"failure": "timeout"}</c> /
/// <c>{"failure": "connection"}</c>. A request with no canned response fails the test rather than going to the network.
/// </remarks>
public sealed class ParityHttpHandler : HttpMessageHandler
{
    private static readonly JsonSerializerOptions MetaOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    private readonly string _httpDirectory;
    private readonly List<ParityHttpRequest> _requests = [];

    /// <summary>A handler serving <paramref name="httpDirectory"/>, a scenario's <c>http/</c> folder.</summary>
    public ParityHttpHandler(string httpDirectory)
    {
        _httpDirectory = httpDirectory;
    }

    /// <summary>Every request made through this handler, in order.</summary>
    public IReadOnlyList<ParityHttpRequest> Requests => _requests;

    /// <summary>
    /// The logical resource a URL stands for, or <see langword="null"/> if it isn't one the app fetches. The Python
    /// driver maps <c>requests.get</c> URLs the same way:
    /// <list type="bullet">
    ///   <item><c>nexus-page</c>: the Nexus Mods mod page (NET-1).</item>
    ///   <item><c>github-latest-release</c>: any <c>api.github.com/repos/&lt;owner&gt;/&lt;repo&gt;/releases/latest</c> (NET-2).</item>
    ///   <item><c>delta/&lt;file&gt;</c>: any URL whose last segment is an <c>.xdelta</c> file, such as
    ///   <c>delta/NG-to-OG-Fallout4.exe.xdelta</c> (NET-3).</item>
    /// </list>
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
    /// <exception cref="TaskCanceledException">The canned failure is <c>timeout</c>; this is how <see cref="HttpClient"/> reports one.</exception>
    /// <exception cref="HttpRequestException">The canned failure is <c>connection</c>.</exception>
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = request.RequestUri ?? throw new InvalidOperationException("The request has no URL.");
        var resource = LogicalResource(url)
                       ?? throw new InvalidOperationException($"{url} isn't a logical resource the parity harness knows.");
        _requests.Add(new ParityHttpRequest(request.Method, url, resource, request.Headers.ToDictionary(h => h.Key, h => string.Join(", ", h.Value))));

        var basePath = Path.Combine(_httpDirectory, resource.Replace('/', Path.DirectorySeparatorChar));
        var metaPath = basePath + ".response.json";
        if (!File.Exists(metaPath))
        {
            throw new InvalidOperationException($"Unscripted HTTP request: the scenario has no {resource}.response.json for {url}.");
        }

        var meta = JsonSerializer.Deserialize<CannedResponse>(await File.ReadAllTextAsync(metaPath, cancellationToken), MetaOptions)
                   ?? throw new InvalidDataException($"{metaPath} is null.");

        switch (meta.Failure)
        {
            case null:
                break;
            case "timeout":
                throw new TaskCanceledException($"Canned timeout for {resource}.", new TimeoutException());
            case "connection":
                throw new HttpRequestException(HttpRequestError.ConnectionError, $"Canned connection failure for {resource}.");
            default:
                throw new InvalidDataException($"{metaPath}: unknown failure '{meta.Failure}'; use timeout or connection.");
        }

        var bodyPath = basePath + ".body";
        var body = File.Exists(bodyPath) ? await File.ReadAllBytesAsync(bodyPath, cancellationToken) : [];
        var headers = meta.Headers ?? new Dictionary<string, string>();

        // A seekable stream would make HttpContent compute a Content-Length on its own. The reference's B-10 path
        // depends on a response with no Content-Length, so the body only has one when the scenario declares it.
        var content = new StreamContent(new NonSeekableStream(body));
        var response = new HttpResponseMessage((HttpStatusCode)(meta.Status ?? 200))
        {
            Content = content,
            RequestMessage = request,
        };
        foreach (var (name, value) in headers)
        {
            if (!response.Headers.TryAddWithoutValidation(name, value))
            {
                content.Headers.TryAddWithoutValidation(name, value);
            }
        }

        return response;
    }

    private sealed record CannedResponse(int? Status = null, Dictionary<string, string>? Headers = null, string? Failure = null);

    /// <summary>A read-only, forward-only stream over a byte array.</summary>
    private sealed class NonSeekableStream(byte[] data) : MemoryStream(data, writable: false)
    {
        public override bool CanSeek => false;
    }
}

/// <summary>One request a <see cref="ParityHttpHandler"/> served.</summary>
public sealed record ParityHttpRequest(HttpMethod Method, Uri Url, string Resource, IReadOnlyDictionary<string, string> Headers);
