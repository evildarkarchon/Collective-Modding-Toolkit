using CMToolkit.App.Parity;
using CMToolkit.Tests.Parity;

namespace CMToolkit.Tests.App;

/// <summary>
/// The exe's canned HTTP (used when it runs on a scenario, for screenshots and the smoke test) against the tests'
/// <see cref="ParityHttpHandler"/>: on every committed scenario's <c>http/</c> folder, each resource must come back
/// the same, status, headers, body and failure alike, so the exe never answers differently from the goldens' replay.
/// </summary>
public sealed class ScenarioHttpHandlerTests
{
    private static readonly Dictionary<string, Uri> UrlOf = new()
    {
        // The URLs the update check asks for.
        ["nexus-page"] = new Uri(CMToolkit.Core.Updates.UpdateCheck.NexusPageUrl),
        ["github-latest-release"] = new Uri(CMToolkit.Core.Updates.UpdateCheck.GitHubLatestReleaseUrl),
    };

    public static TheoryData<string> HttpResources =>
    [
        .. ParityScenarios.All()
            .Where(s => Directory.Exists(Path.Combine(s.Directory, "http")))
            .SelectMany(s => Directory.GetFiles(Path.Combine(s.Directory, "http"), "*.response.json")
                .Select(f => $"{s.Id}|{Path.GetFileName(f)[..^".response.json".Length]}")),
    ];

    [Theory]
    [MemberData(nameof(HttpResources))]
    public async Task It_serves_like_the_test_handler(string scenarioAndResource)
    {
        var (id, resource) = (scenarioAndResource.Split('|')[0], scenarioAndResource.Split('|')[1]);
        var http = Path.Combine(ParityScenarios.All().Single(s => s.Id == id).Directory, "http");
        var url = UrlOf.GetValueOrDefault(resource) ?? new Uri($"https://example.invalid/{resource}");

        var expected = await Serve(new ParityHttpHandler(http), url);
        var actual = await Serve(new ScenarioHttpHandler(http), url);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task A_request_the_scenario_has_no_response_for_fails_instead_of_going_online()
    {
        using var client = new HttpClient(new ScenarioHttpHandler(Path.Combine(ParityPaths.Scenarios, "shell", "main-window", "http")));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.GetAsync(UrlOf["nexus-page"], TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Logical_resources_follow_the_test_handler()
    {
        foreach (var url in new[]
                 {
                     "https://www.nexusmods.com/fallout4/mods/87907", "https://WWW.NEXUSMODS.COM/Fallout4/Mods/87907/",
                     "https://www.nexusmods.com/fallout4/mods/1", "https://api.github.com/repos/a/b/releases/latest",
                     "https://api.github.com/repos/a/releases/latest", "https://example.com/x/y%20z.xdelta",
                     "https://example.com/x.txt",
                 })
        {
            Assert.Equal(ParityHttpHandler.LogicalResource(new Uri(url)), ScenarioHttpHandler.LogicalResource(new Uri(url)));
        }
    }

    /// <summary>What a GET of <paramref name="url"/> yields: status, sorted headers and body as text, or the failure.</summary>
    private static async Task<string> Serve(HttpMessageHandler handler, Uri url)
    {
        using var client = new HttpClient(handler);
        try
        {
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);
            var headers = response.Headers.NonValidated.Concat(response.Content.Headers.NonValidated)
                .Select(h => $"{h.Key}: {string.Join(", ", h.Value)}")
                .Order(StringComparer.Ordinal);
            var body = Convert.ToBase64String(await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
            return $"{(int)response.StatusCode}\n{string.Join("\n", headers)}\n{body}";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return $"failure: {ex.GetType().Name} {(ex as HttpRequestException)?.HttpRequestError}";
        }
    }
}
