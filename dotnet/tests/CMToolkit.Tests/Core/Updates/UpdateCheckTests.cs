using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using CMToolkit.Core.Settings;
using CMToolkit.Core.Updates;
using CMToolkit.Tests.Parity;
using CMToolkit.Tests.Support;

namespace CMToolkit.Tests.Core.Updates;

/// <summary>
/// The update check's requests and the network stack's timeouts: what the <c>update/*</c> scenarios' logical
/// resources can't show (the URLs, the User-Agent, the early end of the Nexus read, per-read timeouts). The network
/// is never reached: requests go to an in-memory handler or a loopback listener.
/// </summary>
public sealed class UpdateCheckTests
{
    private const string NexusPage =
        "<html>\n<head>\n    <meta property=\"twitter:label1\" content=\"Version\" />\n"
        + "    <meta property=\"twitter:data1\" content=\"0.7.0\" />\n";

    /// <summary>
    /// A running version the canned 0.7.0 release is an update for, so these tests don't hinge on the app's own
    /// version, which is 0.7.0 itself since the Cutover.
    /// </summary>
    private const string OlderThanTheCannedRelease = "0.6.2";

    [Fact]
    [Trait("Parity", "NET-1")]
    public async Task The_nexus_check_gets_the_mod_page_and_stops_reading_at_the_version_line()
    {
        // The version line, then a megabyte the reference's streamed read (stream=True, then break) never touches.
        var body = new CountingStream(Encoding.UTF8.GetBytes(NexusPage + new string('x', 1 << 20)));
        var handler = new StubHandler(_ => Ok(new StreamContent(body), "text/html; charset=utf-8"));

        var version = await new UpdateCheck(handler, new RecordingLogger(), OlderThanTheCannedRelease).CheckNexusAsync(TestContext.Current.CancellationToken);

        Assert.Equal("0.7.0", version);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(new Uri("https://www.nexusmods.com/fallout4/mods/87907"), request.RequestUri);
        Assert.Equal("*/*", request.Headers.Accept.ToString());
        Assert.True(body.BytesRead < 4096, $"Read {body.BytesRead} bytes of the page.");
    }

    [Fact]
    [Trait("Parity", "NET-2")]
    [Trait("Parity", "B-16"), Deviation(32, "The update check and the GitHub link both point at evildarkarchon/Collective-Modding-Toolkit, which publishes the C# releases.")]
    public async Task The_github_check_asks_the_repository_that_publishes_the_releases_with_the_api_headers()
    {
        var handler = new StubHandler(_ => Ok(new StringContent("{\"tag_name\": \"0.7.0\"}"), "application/json; charset=utf-8"));

        var version = await new UpdateCheck(handler, new RecordingLogger(), OlderThanTheCannedRelease).CheckGitHubAsync(TestContext.Current.CancellationToken);

        Assert.Equal("0.7.0", version);
        var request = Assert.Single(handler.Requests);
        // B-16: the reference asked the original author's (wxMichael's) releases while its banner linked to RowanSkie's
        // fork. The port asks the fork that publishes its releases (ADR-0005), the one its links open (bug #32, fixed
        // per ADR-0006).
        Assert.Equal(new Uri("https://api.github.com/repos/evildarkarchon/Collective-Modding-Toolkit/releases/latest"), request.RequestUri);
        Assert.Equal("application/vnd.github+json", string.Join(", ", request.Headers.GetValues("Accept")));
        Assert.Equal("2022-11-28", string.Join(", ", request.Headers.GetValues("X-GitHub-Api-Version")));
    }

    [Fact]
    [Trait("Parity", "T-14")]
    public async Task Both_requests_carry_a_user_agent_which_httpclient_does_not_send_by_default()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        await new UpdateCheck(handler, new RecordingLogger()).RunAsync(UpdateSource.Both, TestContext.Current.CancellationToken);

        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, r => Assert.Equal($"cm-toolkit/{CMToolkit.Core.AppInfo.Version}", r.Headers.UserAgent.ToString()));
    }

    [Fact]
    [Trait("Parity", "NET-1"), Deviation(81, "The port logs any update-check failure and treats it as no update, so startup continues.")]
    public async Task A_charset_parameter_without_a_value_is_logged_as_a_failed_check()
    {
        // requests stores a bare "charset" as True, and get_encoding_from_headers calls True.strip(). The reference's
        // AttributeError escaped the check and failed startup; the port logs it as a failed check (bug #81, fixed per
        // ADR-0006).
        var handler = new StubHandler(_ => Ok(new StringContent(NexusPage), "text/html; charset"));
        var logger = new RecordingLogger();

        var version = await new UpdateCheck(handler, logger).CheckNexusAsync(TestContext.Current.CancellationToken);

        Assert.Null(version);
        var failure = Assert.Single(logger.Records, r => r.Message == "Update Check : Nexus Mods : Failed");
        Assert.Equal(Microsoft.Extensions.Logging.LogLevel.Error, failure.Level);
        Assert.NotNull(failure.Exception);
    }

    [Fact]
    [Trait("Parity", "UPD-4"), Deviation(81, "The port logs any update-check failure and treats it as no update, so startup continues.")]
    public async Task With_both_sources_a_failed_nexus_check_still_lets_github_be_checked()
    {
        // The Nexus page has no Content-Type (the reference's TypeError ended the whole check there); GitHub answers.
        var handler = new StubHandler(request => request.RequestUri!.Host == "www.nexusmods.com"
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Encoding.UTF8.GetBytes(NexusPage)) }
            : Ok(new StringContent("{\"tag_name\": \"0.7.0\"}"), "application/json; charset=utf-8"));
        var logger = new RecordingLogger();

        var result = await new UpdateCheck(handler, logger, OlderThanTheCannedRelease).RunAsync(UpdateSource.Both, TestContext.Current.CancellationToken);

        Assert.Equal(new UpdateCheckResult(null, "0.7.0"), result);
        Assert.Contains(logger.Records, r => r.Message == "Update Check : Nexus Mods : Failed" && r.Exception is not null);
    }

    [Fact]
    [Trait("Parity", "UPD-3")]
    public async Task The_check_compares_against_the_running_version_it_is_given_else_the_apps_own()
    {
        var handler = new StubHandler(_ => Ok(new StringContent("{\"tag_name\": \"0.7.0\"}"), "application/json; charset=utf-8"));

        // The update/* goldens were recorded with the reference running 0.6.2-dev, so their replays pass that version.
        var asBaseline = await new UpdateCheck(handler, new RecordingLogger(), ParityScenario.RecordedAppVersion)
            .CheckGitHubAsync(TestContext.Current.CancellationToken);
        var asApp = await new UpdateCheck(handler, new RecordingLogger())
            .CheckGitHubAsync(TestContext.Current.CancellationToken);

        Assert.Equal("0.7.0", asBaseline);
        // The app runs 0.7.0, so a 0.7.0 release is not an update.
        Assert.Null(asApp);
    }

    [Theory]
    [Trait("Parity", "UPD-3"), Deviation(71, "A deliberate change at the Cutover, not a reference bug: the version is 0.7.0, the first C# release, where the reference ran as 0.6.2-dev.")]
    [InlineData("0.7.1", "0.7.1")]
    [InlineData("0.7.0", null)]
    [InlineData("0.6.2", null)]
    public async Task Against_the_apps_own_version_only_a_release_past_0_7_0_is_an_update(string tag, string? expected)
    {
        // utils.py: Version(latest) > Version(str(APP_VERSION)). The app runs 0.7.0 since the Cutover (#71).
        var handler = new StubHandler(_ => Ok(new StringContent($"{{\"tag_name\": \"{tag}\"}}"), "application/json; charset=utf-8"));

        var version = await new UpdateCheck(handler, new RecordingLogger()).CheckGitHubAsync(TestContext.Current.CancellationToken);

        Assert.Equal(expected, version);
    }

    [Fact]
    public async Task A_cancellation_the_caller_asks_for_is_not_logged_as_a_failed_check()
    {
        using var cancel = new CancellationTokenSource();
        await cancel.CancelAsync();
        var logger = new RecordingLogger();
        var handler = new StubHandler(_ => throw new TaskCanceledException());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new UpdateCheck(handler, logger).CheckGitHubAsync(cancel.Token));
        Assert.DoesNotContain(logger.Records, r => r.Message.EndsWith("Failed", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Parity", "T-14")]
    public async Task The_timeout_is_per_read_so_a_slow_steady_response_outlasting_it_still_arrives()
    {
        // Thirty 100 ms gaps: three seconds in all, against a 2 s per-read timeout that a total timeout would blow. The
        // 20x margin between a gap and the timeout, and the writes paced by Thread.Sleep on a dedicated thread rather
        // than Task.Delay on the pool, keep other tests that tie up pool threads from stretching a gap into a timeout.
        var body = new string('a', 30);
        await using var server = LoopbackServer.Start((stream, stopping) => Task.Factory.StartNew(
            () =>
            {
                WriteNow(stream, $"HTTP/1.1 200 OK\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
                foreach (var c in body)
                {
                    stopping.ThrowIfCancellationRequested();
                    Thread.Sleep(100);
                    WriteNow(stream, c.ToString());
                }
            },
            stopping,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default));
        using var client = new HttpClient(UpdateCheck.CreateHandler(TimeSpan.FromSeconds(2))) { Timeout = Timeout.InfiniteTimeSpan };

        var watch = Stopwatch.StartNew();
        var text = await client.GetStringAsync(server.Url, TestContext.Current.CancellationToken).WithTimeout();

        Assert.Equal(body, text);
        Assert.True(watch.Elapsed > TimeSpan.FromSeconds(2), $"The response took only {watch.Elapsed}.");
    }

    [Fact]
    [Trait("Parity", "T-14")]
    public async Task A_read_that_stalls_past_the_timeout_fails_the_request()
    {
        await using var server = LoopbackServer.Start(async (stream, stopping) =>
        {
            await Write(stream, "HTTP/1.1 200 OK\r\nContent-Length: 10\r\n\r\nabc");
            // Then nothing: the connection stays open, silent, until the test ends.
            await Task.Delay(Timeout.Infinite, stopping);
        });
        using var client = new HttpClient(UpdateCheck.CreateHandler(TimeSpan.FromMilliseconds(300))) { Timeout = Timeout.InfiniteTimeSpan };

        var watch = Stopwatch.StartNew();
        var ex = await Assert.ThrowsAnyAsync<Exception>(() => client.GetStringAsync(server.Url, TestContext.Current.CancellationToken).WithTimeout());

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), $"The stall was noticed only after {watch.Elapsed}.");
        Assert.True(Chain(ex).Any(e => e is TimeoutException), ex.ToString());
    }

    [Fact]
    [Trait("Parity", "T-14")]
    public void The_check_has_no_total_timeout_and_connects_within_the_read_timeout()
    {
        using var handler = UpdateCheck.CreateHandler();

        Assert.Equal(TimeSpan.FromSeconds(5), UpdateCheck.ReadTimeout);
        Assert.Equal(UpdateCheck.ReadTimeout, handler.ConnectTimeout);
        Assert.NotNull(handler.ConnectCallback);
        Assert.Equal(DecompressionMethods.GZip | DecompressionMethods.Deflate, handler.AutomaticDecompression);
    }

    private static IEnumerable<Exception> Chain(Exception ex)
    {
        for (Exception? e = ex; e is not null; e = e.InnerException)
        {
            yield return e;
        }
    }

    private static async Task Write(Stream stream, string text)
    {
        await stream.WriteAsync(Encoding.ASCII.GetBytes(text));
        await stream.FlushAsync();
    }

    /// <summary>Writes and flushes <paramref name="text"/> synchronously, on the calling thread.</summary>
    private static void WriteNow(Stream stream, string text)
    {
        stream.Write(Encoding.ASCII.GetBytes(text));
        stream.Flush();
    }

    private static HttpResponseMessage Ok(HttpContent content, string contentType)
    {
        content.Headers.Remove("Content-Type");
        content.Headers.TryAddWithoutValidation("Content-Type", contentType);
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    /// <summary>An in-memory handler: answers every request with <paramref name="respond"/> and keeps the requests.</summary>
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(respond(request));
        }
    }

    /// <summary>A read-only stream that counts the bytes read from it.</summary>
    private sealed class CountingStream(byte[] data) : MemoryStream(data, writable: false)
    {
        public long BytesRead { get; private set; }

        public override bool CanSeek => false;

        public override int Read(byte[] buffer, int offset, int count) => Count(base.Read(buffer, offset, count));

        public override int Read(Span<byte> buffer) => Count(base.Read(buffer));

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => Count(await base.ReadAsync(buffer, cancellationToken));

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => Count(await base.ReadAsync(buffer, offset, count, cancellationToken));

        private int Count(int read)
        {
            BytesRead += read;
            return read;
        }
    }

    /// <summary>
    /// A one-connection HTTP server on 127.0.0.1: reads the request head, then hands the connection to a script.
    /// </summary>
    private sealed class LoopbackServer : IAsyncDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _stopping = new();
        private readonly Task _serving;

        private LoopbackServer(TcpListener listener, Func<NetworkStream, CancellationToken, Task> script)
        {
            _listener = listener;
            var token = _stopping.Token;
            _serving = Task.Run(async () =>
            {
                using var client = await listener.AcceptTcpClientAsync(token);
                var stream = client.GetStream();
                var head = new StringBuilder();
                var buffer = new byte[1];
                while (!head.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal)
                       && await stream.ReadAsync(buffer, token) > 0)
                {
                    head.Append((char)buffer[0]);
                }

                await script(stream, token);
            });
        }

        public Uri Url => new($"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/");

        public static LoopbackServer Start(Func<NetworkStream, CancellationToken, Task> script)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return new LoopbackServer(listener, script);
        }

        public async ValueTask DisposeAsync()
        {
            await _stopping.CancelAsync();
            try
            {
                await _serving;
            }
            catch (Exception ex) when (ex is OperationCanceledException or IOException or SocketException)
            {
                // Stopped mid-script, or the client had already dropped the connection: the test is over either way.
            }

            _listener.Stop();
            _listener.Dispose();
            _stopping.Dispose();
        }
    }
}
