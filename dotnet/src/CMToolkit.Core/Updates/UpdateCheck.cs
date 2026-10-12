using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using CMToolkit.Core.Settings;
using Microsoft.Extensions.Logging;

namespace CMToolkit.Core.Updates;

/// <summary>
/// What the update check found (UPD-1): each source's newer version, as the source wrote it less the surrounding
/// whitespace and leading <c>v</c> PEP 440 allows (bug #83), or null.
/// </summary>
/// <param name="NexusVersion">The Nexus Mods version when it is newer than the running one.</param>
/// <param name="GitHubVersion">The GitHub release tag when it is newer than the running one.</param>
public sealed record UpdateCheckResult(string? NexusVersion, string? GitHubVersion)
{
    /// <summary>No update from either source, or no check at all.</summary>
    public static UpdateCheckResult None { get; } = new(null, null);

    /// <summary>Whether either source has a newer version, so the banner shows (UPD-2).</summary>
    public bool UpdateAvailable => NexusVersion is not null || GitHubVersion is not null;
}

/// <summary>
/// A response the reference's update check didn't catch, so it escaped <c>CMChecker.__init__</c> and startup failed
/// (SHELL-13): an unexpected response, such as a page with no <c>Content-Type</c> or a release without a
/// <c>tag_name</c>. The port's check catches it like any other failure, logs it and reports no update (bug #81, fixed
/// per ADR-0006); it is thrown only to carry the Python error into the log.
/// </summary>
/// <param name="pythonError">The Python exception the reference raised, for the log.</param>
public sealed class UpdateCheckCrashException(string pythonError)
    : Exception($"The update check failed with an unhandled error: {pythonError}");

/// <summary>
/// The update check (<c>utils.check_for_update_nexus</c> / <c>check_for_update_github</c>, UPD-1..UPD-4): the Nexus
/// Mods page scrape (NET-1) and the GitHub latest-release API (NET-2), each compared to the running version with
/// PEP 440 (<see cref="Pep440Version"/>), with any failure of either check logged and treated as no update (UPD-4;
/// the reference let some escape, bug #81).
/// </summary>
/// <remarks>
/// Async, unlike most of Core, because it is HTTP (the Core seams decision, issue #7); the App awaits it before the
/// main window shows. Log lines are the reference's, written through <see cref="PlainLog"/>.
/// </remarks>
public sealed class UpdateCheck
{
    /// <summary><c>NEXUS_LINK</c>, the page the Nexus check scrapes.</summary>
    public const string NexusPageUrl = "https://www.nexusmods.com/fallout4/mods/87907";

    /// <summary>
    /// The release the GitHub check asks for: the latest of the fork that publishes the C# releases (ADR-0005), the
    /// repository the banner's link opens. The reference asked the original author's (wxMichael's) releases while its
    /// link and version were RowanSkie's fork, so it never saw a C# release (B-16, bug #32, fixed per ADR-0006).
    /// </summary>
    public const string GitHubLatestReleaseUrl = "https://api.github.com/repos/evildarkarchon/Collective-Modding-Toolkit/releases/latest";

    /// <summary>The line before the version on the Nexus page, matched after stripping leading whitespace.</summary>
    private const string VersionLabelLine = "<meta property=\"twitter:label1\" content=\"Version\"";

    private readonly HttpClient _client;
    private readonly ILogger _logger;

    /// <param name="handler">
    /// The HTTP stack: <see cref="CreateHandler"/> for the real network, or a canned one in tests and on a Parity
    /// Scenario's machine. Not disposed by the check.
    /// </param>
    /// <param name="logger">The app log.</param>
    public UpdateCheck(HttpMessageHandler handler, ILogger logger)
    {
        // No total timeout: the handler's per-read timeout is the only one (T-14).
        _client = new HttpClient(handler, disposeHandler: false) { Timeout = Timeout.InfiniteTimeSpan };
        _logger = logger;
    }

    /// <summary>
    /// <c>timeout=5</c>: how long one connect, read or write may take. Not a limit on the whole request (T-14).
    /// </summary>
    public static TimeSpan ReadTimeout { get; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Sent with both requests. requests sent <c>python-requests/&lt;version&gt;</c>; GitHub's API refuses a request
    /// without one, and <see cref="HttpClient"/> sends none by default (T-14).
    /// </summary>
    public static string UserAgent => $"cm-toolkit/{AppInfo.Version}";

    /// <summary>
    /// The real network stack: connects within <paramref name="readTimeout"/>, then gives every read and write on the
    /// connection that long (<see cref="ReadTimeoutStream"/>), decompresses gzip and deflate (which requests asks
    /// for), and follows redirects.
    /// </summary>
    /// <param name="readTimeout">The per-operation timeout; <see cref="ReadTimeout"/> by default.</param>
    public static SocketsHttpHandler CreateHandler(TimeSpan? readTimeout = null)
    {
        var timeout = readTimeout ?? ReadTimeout;
        return new SocketsHttpHandler
        {
            ConnectTimeout = timeout,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            ConnectCallback = async (context, cancellationToken) =>
            {
                // The standard connect (dual-mode socket, every address DNS gives), with the connection's stream
                // wrapped so each socket operation is timed on its own, as a Python socket's timeout is.
                var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                try
                {
                    await socket.ConnectAsync(context.DnsEndPoint, cancellationToken).ConfigureAwait(false);
                    var stream = new NetworkStream(socket, ownsSocket: true)
                    {
                        ReadTimeout = (int)timeout.TotalMilliseconds,
                        WriteTimeout = (int)timeout.TotalMilliseconds,
                    };
                    return new ReadTimeoutStream(stream, timeout);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            },
        };
    }

    /// <summary>
    /// <c>CMChecker.check_for_updates</c>' part before the banner (UPD-1): <c>none</c> checks nothing; <c>nexus</c>
    /// and <c>github</c> check one source; <c>both</c> checks Nexus Mods, then GitHub. A failed Nexus check doesn't
    /// stop the GitHub one.
    /// </summary>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public async Task<UpdateCheckResult> RunAsync(UpdateSource source, CancellationToken cancellationToken = default)
    {
        if (source == UpdateSource.None)
        {
            return UpdateCheckResult.None;
        }

        var nexus = source is UpdateSource.Nexus or UpdateSource.Both
            ? await CheckNexusAsync(cancellationToken).ConfigureAwait(false)
            : null;
        var github = source is UpdateSource.GitHub or UpdateSource.Both
            ? await CheckGitHubAsync(cancellationToken).ConfigureAwait(false)
            : null;
        return new UpdateCheckResult(nexus, github);
    }

    /// <summary>
    /// <c>check_for_update_nexus</c> (NET-1): streams the mod page and reads the version from the line after the
    /// <c>twitter:label1</c> Version tag, as <c>line.rsplit('"', 2)[1]</c>. A non-200 status is no update; a page
    /// without the tag is logged as a failure; any other failure (a request error, an invalid version, a line without a
    /// quote, or a page with no usable encoding, which the reference didn't survive, bug #81) is logged with its
    /// exception. Reading stops at the version line.
    /// </summary>
    /// <returns>
    /// The version as the page wrote it (less the padding and leading <c>v</c>, <see cref="WithoutPrefix"/>) when it is
    /// newer than the running one; otherwise null.
    /// </returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public async Task<string?> CheckNexusAsync(CancellationToken cancellationToken = default)
    {
        _logger.Write(LogLevel.Debug, "Update Check : Nexus Mods");
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, NexusPageUrl);
            request.Headers.UserAgent.ParseAdd(UserAgent);
            // requests' session default, which the page is served against.
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));
            using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.OK)
            {
                var versionLine = await FindVersionLineAsync(response, cancellationToken).ConfigureAwait(false);
                if (versionLine is null)
                {
                    _logger.Write(LogLevel.Error, "Update Check : Nexus Mods : Failed");
                    return null;
                }

                if (versionLine.Count < 2)
                {
                    throw new PythonIndexError();
                }

                var latest = versionLine[1];
                if (Pep440Version.IsNewerThanRunning(latest))
                {
                    var shown = WithoutPrefix(latest);
                    _logger.Write(LogLevel.Information, $"Update Check : Nexus Mods : Update Available : v{shown}");
                    return shown;
                }
            }
        }
        catch (Exception ex) when (IsCheckFailure(ex, cancellationToken))
        {
            // The reference caught only RequestException, InvalidVersion and IndexError; anything else escaped
            // CMChecker.__init__ and failed startup. Any failure is no update here (bug #81).
            _logger.Write(LogLevel.Error, "Update Check : Nexus Mods : Failed", ex);
            return null;
        }

        _logger.Write(LogLevel.Debug, "Update Check : Nexus Mods : No Updates");
        return null;
    }

    /// <summary>
    /// <c>check_for_update_github</c> (NET-2, B-16): the latest release's <c>tag_name</c>, through <c>str()</c>. A
    /// non-200 status is no update; any failure (a request error, a body that isn't JSON, a tag that isn't PEP 440, or
    /// JSON that isn't an object with a <c>tag_name</c>, which the reference didn't survive, bug #81) is logged with
    /// its exception.
    /// </summary>
    /// <returns>
    /// The tag as GitHub wrote it (less the padding and leading <c>v</c>, <see cref="WithoutPrefix"/>) when it is newer
    /// than the running version; otherwise null.
    /// </returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public async Task<string?> CheckGitHubAsync(CancellationToken cancellationToken = default)
    {
        _logger.Write(LogLevel.Debug, "Update Check : GitHub");
        byte[] content;
        HttpResponseMessage response;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, GitHubLatestReleaseUrl);
            request.Headers.UserAgent.ParseAdd(UserAgent);
            request.Headers.TryAddWithoutValidation("Accept", "application/vnd.github+json");
            request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
            // Not streamed: requests reads the whole body inside get(), so its read errors are request errors.
            response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            try
            {
                content = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                response.Dispose();
                throw;
            }
        }
        catch (Exception ex) when (IsCheckFailure(ex, cancellationToken))
        {
            _logger.Write(LogLevel.Error, "Update Check : GitHub : Failed", ex);
            return null;
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.OK)
            {
                try
                {
                    var release = RequestsText.Json(content, RequestsText.EncodingFromHeaders(response));
                    var latest = TagName(release).Str();
                    if (Pep440Version.IsNewerThanRunning(latest))
                    {
                        var shown = WithoutPrefix(latest);
                        _logger.Write(LogLevel.Information, $"Update Check : GitHub : Update Available : v{shown}");
                        return shown;
                    }
                }
                catch (Exception ex) when (IsCheckFailure(ex, cancellationToken))
                {
                    // The reference caught only JSONDecodeError and InvalidVersion here, so a body that wasn't an
                    // object with a tag_name escaped and failed startup. Any failure is no update here (bug #81).
                    _logger.Write(LogLevel.Error, "Update Check : GitHub : Failed", ex);
                    return null;
                }
            }
        }

        _logger.Write(LogLevel.Debug, "Update Check : GitHub : No Updates");
        return null;
    }

    /// <summary><c>release_data["tag_name"]</c>.</summary>
    private static PythonValue TagName(PythonValue release)
    {
        if (release is not PythonDict dict)
        {
            throw new UpdateCheckCrashException($"TypeError: {release.TypeName} indices must be integers or slices, not str");
        }

        foreach (var (key, value) in dict.Items)
        {
            if (key == "tag_name")
            {
                return value;
            }
        }

        throw new UpdateCheckCrashException("KeyError: 'tag_name'");
    }

    /// <summary>
    /// Iterates the page's lines (<c>iter_lines(decode_unicode=True)</c>) up to the line after the Version tag, and
    /// returns it split as <c>rsplit('"', 2)</c>; null if the page ends first.
    /// </summary>
    private static async Task<IReadOnlyList<string>?> FindVersionLineAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var encoding = RequestsText.EncodingFromHeaders(response);
        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
        {
            var buffer = new byte[RequestsText.ChunkSize];

            async ValueTask<byte[]> ReadChunk(CancellationToken token)
            {
                // raw.read(512): fills the chunk unless the body ends.
                var filled = 0;
                while (filled < buffer.Length)
                {
                    var read = await stream.ReadAsync(buffer.AsMemory(filled), token).ConfigureAwait(false);
                    if (read == 0)
                    {
                        break;
                    }

                    filled += read;
                }

                return buffer[..filled];
            }

            if (encoding is null)
            {
                // With no encoding requests yields the lines as bytes, and bytes.startswith(str) raises TypeError at
                // the first line. An empty body has no lines, so it ends as a page without the tag.
                return (await ReadChunk(cancellationToken).ConfigureAwait(false)).Length > 0
                    ? throw new UpdateCheckCrashException("TypeError: startswith first arg must be bytes or a tuple of bytes, not str")
                    : null;
            }

            var decoder = PythonCodec.Lookup(encoding)?.CreateDecoder(strict: false)
                          ?? throw new UpdateCheckCrashException($"LookupError: unknown encoding: {encoding}");
            var useNext = false;
            await foreach (var line in RequestsText.IterLinesAsync(ReadChunk, decoder, cancellationToken).ConfigureAwait(false))
            {
                if (useNext)
                {
                    return RSplitQuote(line);
                }

                if (LStrip(line).StartsWith(VersionLabelLine, StringComparison.Ordinal))
                {
                    useNext = true;
                }
            }

            return null;
        }
    }

    /// <summary><c>line.rsplit('"', 2)</c>.</summary>
    private static List<string> RSplitQuote(string line)
    {
        var last = line.LastIndexOf('"');
        if (last < 0)
        {
            return [line];
        }

        var previous = last == 0 ? -1 : line.LastIndexOf('"', last - 1);
        return previous < 0
            ? [line[..last], line[(last + 1)..]]
            : [line[..previous], line[(previous + 1)..last], line[(last + 1)..]];
    }

    /// <summary>
    /// A newer version as the banner and the log show it after their own <c>v</c>: without the surrounding whitespace
    /// and the one leading <c>v</c> or <c>V</c> that PEP 440 lets the source write. The reference showed the text as
    /// written, so a <c>v0.7.0</c> tag read <c>vv0.7.0</c> (bug #83, fixed per ADR-0006). The rest of the spelling
    /// is kept.
    /// </summary>
    /// <param name="latest">A version <see cref="Pep440Version"/> accepted.</param>
    internal static string WithoutPrefix(string latest)
    {
        // Version._regex's \s, which in Python is str.isspace() (U+001C..U+001F included): str.strip()'s set.
        var stripped = PythonSemantics.PythonText.Strip(latest);
        return stripped.Length > 0 && stripped[0] is 'v' or 'V' ? stripped[1..] : stripped;
    }

    /// <summary>The no-argument <c>str.lstrip()</c>.</summary>
    private static string LStrip(string value)
    {
        var start = 0;
        while (start < value.Length && PythonSemantics.PythonText.IsSpace(value[start]))
        {
            start++;
        }

        return value[start..];
    }

    /// <summary>
    /// Whether <paramref name="ex"/> fails a check, to be logged as no update: anything, from a request error or a
    /// timeout to a response the reference didn't survive (bug #81), except a cancellation the caller asked for.
    /// </summary>
    private static bool IsCheckFailure(Exception ex, CancellationToken cancellationToken) =>
        ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested;

    /// <summary>Python's <c>IndexError</c> from <c>version_line[1]</c>, which the Nexus check catches.</summary>
    private sealed class PythonIndexError() : Exception("list index out of range");
}
