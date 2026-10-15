using Xunit.v3;

namespace CMToolkit.Tests.Parity;

/// <summary>
/// Marks a test that asserts the port's fix of a reference bug instead of the Reference Implementation's behaviour: a
/// documented Deviation from Behaviour Parity (ADR-0006). Put it beside the test's <c>Trait("Parity", …)</c>, which
/// stays, with the <c>bug</c> issue number and one sentence saying what the port does instead. A deliberate change
/// that isn't a bug fix (#69) uses its own issue number, and its sentence says it is deliberate.
/// </summary>
/// <remarks>
/// <para>
/// The coverage check reads these as text, like the parity traits, and lists them on every run. So write the bug as an
/// integer literal and the reason as a regular (not verbatim or raw) string literal, and never spell an applied
/// attribute inside a string or a doc comment: it would be listed as a real deviation.
/// </para>
/// <para>
/// It is also an xUnit trait, <c>Deviation=&lt;bug&gt;</c>, so <c>dotnet test --filter "Deviation=77"</c> runs the tests
/// that bug's fix changed.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true)]
public sealed class DeviationAttribute : Attribute, ITraitAttribute
{
    /// <summary>Declares that the test asserts the fix for <paramref name="bug"/>.</summary>
    /// <param name="bug">
    /// The <c>bug</c> issue number on the fork, for example <c>77</c> for #77, or a deliberate change's own issue number.
    /// </param>
    /// <param name="reason">What the port does instead of the reference, in one sentence.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="bug"/> isn't positive.</exception>
    /// <exception cref="ArgumentException"><paramref name="reason"/> is blank.</exception>
    public DeviationAttribute(int bug, string reason)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bug);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        Bug = bug;
        Reason = reason;
    }

    /// <summary>The <c>bug</c> issue number.</summary>
    public int Bug { get; }

    /// <summary>What the port does instead of the reference.</summary>
    public string Reason { get; }

    /// <summary>The single trait <c>Deviation=&lt;bug&gt;</c>.</summary>
    public IReadOnlyCollection<KeyValuePair<string, string>> GetTraits()
        => [new("Deviation", Bug.ToString(System.Globalization.CultureInfo.InvariantCulture))];
}
