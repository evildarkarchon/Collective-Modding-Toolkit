using System.Net;
using System.Text;
using CMToolkit.Tests.Support;

namespace CMToolkit.Tests.Parity;

/// <summary>The fake HTTP handler: canned responses by logical resource, failures included.</summary>
public sealed class ParityHttpHandlerTests : IDisposable
{
    private readonly TempDirectory _http = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _http.Dispose();

    private void Can(string resource, string metaJson, byte[]? body = null)
    {
        _http.WriteBytes(resource + ".response.json", Encoding.UTF8.GetBytes(metaJson));
        if (body is not null)
        {
            _http.WriteBytes(resource + ".body", body);
        }
    }

    private HttpClient Client(out ParityHttpHandler handler)
    {
        handler = new ParityHttpHandler(_http.Path);
        return new HttpClient(handler);
    }

    [Theory]
    [InlineData("https://www.nexusmods.com/fallout4/mods/87907", "nexus-page")]
    [InlineData("https://api.github.com/repos/wxMichael/Collective-Modding-Toolkit/releases/latest", "github-latest-release")]
    [InlineData("https://api.github.com/repos/evildarkarchon/Collective-Modding-Toolkit/releases/latest", "github-latest-release")]
    [InlineData(
        "https://github.com/wxMichael/Collective-Modding-Toolkit/releases/download/delta-patches/NG-to-OG-Fallout4.exe.xdelta",
        "delta/NG-to-OG-Fallout4.exe.xdelta")]
    [InlineData("https://cdn.example.invalid/any/where/OG-to-NG-steam_api64.dll.xdelta", "delta/OG-to-NG-steam_api64.dll.xdelta")]
    [InlineData("https://www.nexusmods.com/fallout4/mods/1", null)]
    [InlineData("https://example.invalid/", null)]
    public void Urls_map_to_logical_resources(string url, string? expected)
    {
        Assert.Equal(expected, ParityHttpHandler.LogicalResource(new Uri(url)));
    }

    [Fact]
    public async Task A_canned_response_is_served_with_its_status_headers_and_body_and_the_request_is_recorded()
    {
        Can("github-latest-release", """{"status": 200, "headers": {"Content-Type": "application/json", "X-RateLimit-Remaining": "59"}}""", """{"tag_name": "v0.7.0"}"""u8.ToArray());
        using var client = Client(out var handler);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/wxMichael/Collective-Modding-Toolkit/releases/latest");
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");

        using var response = await client.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("59", response.Headers.GetValues("X-RateLimit-Remaining").Single());
        Assert.Equal("""{"tag_name": "v0.7.0"}""", await response.Content.ReadAsStringAsync(Ct));
        var recorded = Assert.Single(handler.Requests);
        Assert.Equal("github-latest-release", recorded.Resource);
        Assert.Equal("2022-11-28", recorded.Headers["X-GitHub-Api-Version"]);
    }

    [Fact]
    public async Task A_body_has_a_content_length_only_when_the_scenario_declares_one()
    {
        Can("delta/a.xdelta", """{"status": 200}""", [1, 2, 3]);
        Can("delta/b.xdelta", """{"status": 200, "headers": {"Content-Length": "3"}}""", [1, 2, 3]);
        using var client = Client(out _);

        using var without = await client.GetAsync("https://x.invalid/a.xdelta", HttpCompletionOption.ResponseHeadersRead, Ct);
        using var with = await client.GetAsync("https://x.invalid/b.xdelta", HttpCompletionOption.ResponseHeadersRead, Ct);

        Assert.Null(without.Content.Headers.ContentLength);
        Assert.Equal(3, with.Content.Headers.ContentLength);
        Assert.Equal([1, 2, 3], await without.Content.ReadAsByteArrayAsync(Ct));
    }

    [Fact]
    public async Task Error_statuses_are_served_like_any_other_response()
    {
        Can("nexus-page", """{"status": 503}""", "down"u8.ToArray());
        using var client = Client(out _);

        using var response = await client.GetAsync("https://www.nexusmods.com/fallout4/mods/87907", Ct);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("down", await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task A_canned_timeout_surfaces_as_httpclient_reports_one()
    {
        Can("nexus-page", """{"failure": "timeout"}""");
        using var client = Client(out _);

        var ex = await Assert.ThrowsAsync<TaskCanceledException>(() => client.GetAsync("https://www.nexusmods.com/fallout4/mods/87907", Ct));

        Assert.IsType<TimeoutException>(ex.InnerException);
    }

    [Fact]
    public async Task A_canned_connection_failure_is_an_http_request_exception()
    {
        Can("nexus-page", """{"failure": "connection"}""");
        using var client = Client(out _);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync("https://www.nexusmods.com/fallout4/mods/87907", Ct));

        Assert.Equal(HttpRequestError.ConnectionError, ex.HttpRequestError);
    }

    [Fact]
    public async Task An_unscripted_request_fails_instead_of_reaching_the_network()
    {
        using var client = Client(out _);

        var unknownResource = await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetAsync("https://example.invalid/", Ct));
        var noResponse = await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetAsync("https://www.nexusmods.com/fallout4/mods/87907", Ct));

        Assert.Contains("isn't a logical resource", unknownResource.Message);
        Assert.Contains("Unscripted HTTP request", noResponse.Message);
    }
}
