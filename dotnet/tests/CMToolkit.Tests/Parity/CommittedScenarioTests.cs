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
        var replayed = ReplayedScenarioIds(typeof(CommittedScenarioTests).Assembly.GetTypes());

        Assert.DoesNotContain(ParityScenarios.All(), s => !replayed.Contains(s.Id));
    }

    [Fact]
    public void Member_data_without_a_theory_replays_nothing()
    {
        Assert.Empty(ReplayedScenarioIds([typeof(OrphanedMemberData)]));
    }

    /// <summary>
    /// The IDs of the scenarios that theories on <paramref name="types"/> replay. Every <c>[MemberData]</c> whose member
    /// yields <see cref="TheoryData{T}"/> of <see cref="ParityScenario"/> is a scenario source, but only if xUnit
    /// actually runs its rows: a <c>[MemberData]</c> left on a method whose theory attribute was removed, or a
    /// skipped/explicit theory, data source or row, would otherwise count as proof while replaying nothing.
    /// </summary>
    private static HashSet<string> ReplayedScenarioIds(IEnumerable<Type> types)
        => types
            .SelectMany(type => type.GetMethods())
            .Where(IsActiveTheory)
            .SelectMany(method => method.GetCustomAttributes<MemberDataAttribute>()
                .Where(data => data.Skip is null)
                .Select(data => (data.MemberType ?? method.DeclaringType!).GetProperty(data.MemberName, BindingFlags.Public | BindingFlags.Static)))
            .Where(property => property?.PropertyType == typeof(TheoryData<ParityScenario>))
            .SelectMany(property => (TheoryData<ParityScenario>)property!.GetValue(null)!)
            .Where(row => row.Skip is null)
            .Select(row => row.Data.Id)
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// Whether <paramref name="method"/> is a theory xUnit runs by default: it has <c>[Theory]</c> (or a subclass such
    /// as <c>[AvaloniaTheory]</c>), with no <c>Skip</c> and not <c>Explicit</c>. <c>SkipWhen</c>/<c>SkipUnless</c> only
    /// apply alongside <c>Skip</c>, so any <c>Skip</c> at all counts as skipped.
    /// </summary>
    private static bool IsActiveTheory(MethodInfo method)
        => method.GetCustomAttribute<FactAttribute>() is TheoryAttribute { Skip: null, Explicit: false };

    /// <summary>
    /// A theory whose attribute was deleted but whose <c>[MemberData]</c> stayed. xUnit doesn't discover the method, so
    /// it never runs; it only exists for <see cref="Member_data_without_a_theory_replays_nothing"/> to scan.
    /// </summary>
    public static class OrphanedMemberData
    {
        public static TheoryData<ParityScenario> Scenarios => new(new ParityScenario("unused", "fixture/orphaned"));

        // xUnit1008 rejects exactly this at build time; the fixture needs the mistake to exist so the runtime filter,
        // which also has to catch skipped and explicit theories the analyzer doesn't, can be shown to drop it.
#pragma warning disable xUnit1008
        [MemberData(nameof(Scenarios))]
        public static void Replay(ParityScenario scenario) => _ = scenario;
#pragma warning restore xUnit1008
    }
}
