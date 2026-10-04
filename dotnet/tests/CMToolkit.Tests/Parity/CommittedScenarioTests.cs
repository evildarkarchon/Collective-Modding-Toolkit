using System.Reflection;

namespace CMToolkit.Tests.Parity;

/// <summary>
/// Checks over the committed scenarios in <c>parity/scenarios/</c>. A scenario only proves its IDs if a test replays it,
/// so a scenario that no theory picks up, or that was never recorded, is an error rather than a silent gap.
/// </summary>
public sealed class CommittedScenarioTests
{
    [Fact]
    public void There_are_committed_scenarios()
    {
        Assert.NotEmpty(ParityScenarios.All());
    }

    [Fact]
    public void Every_committed_scenario_parses_and_is_recorded()
    {
        var problems = new List<string>();
        foreach (var scenario in ParityScenarios.All())
        {
            try
            {
                _ = scenario.Manifest;
                _ = scenario.LoadGolden();
            }
            catch (Exception ex)
            {
                problems.Add($"{scenario.Id}: {ex.Message}");
            }
        }

        Assert.Empty(problems);
    }

    [Fact]
    public void Every_committed_scenario_is_replayed_by_a_theory()
    {
        // Every [MemberData] in this assembly whose member yields TheoryData<ParityScenario> is a scenario source.
        var replayed = typeof(CommittedScenarioTests).Assembly.GetTypes()
            .SelectMany(type => type.GetMethods())
            .SelectMany(method => method.GetCustomAttributes<MemberDataAttribute>()
                .Select(data => (data.MemberType ?? method.DeclaringType!).GetProperty(data.MemberName, BindingFlags.Public | BindingFlags.Static)))
            .Where(property => property?.PropertyType == typeof(TheoryData<ParityScenario>))
            .SelectMany(property => (TheoryData<ParityScenario>)property!.GetValue(null)!)
            .Select(row => row.Data.Id)
            .ToHashSet(StringComparer.Ordinal);

        Assert.DoesNotContain(ParityScenarios.All(), s => !replayed.Contains(s.Id));
    }
}
