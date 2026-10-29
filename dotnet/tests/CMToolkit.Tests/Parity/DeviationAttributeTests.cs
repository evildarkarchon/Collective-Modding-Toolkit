namespace CMToolkit.Tests.Parity;

/// <summary>
/// The test-side marker for a Deviation recorded during the port (ADR-0006). It is built with <c>new</c> here rather
/// than applied, so this synthetic one isn't mistaken for a real deviation when filtering on the trait.
/// </summary>
public sealed class DeviationAttributeTests
{
    [Fact]
    public void It_surfaces_the_bug_number_as_a_deviation_trait_so_a_fixer_can_filter_on_it()
    {
        var attribute = new DeviationAttribute(1, "Synthetic: the port fixes it.");

        Assert.Equal([new KeyValuePair<string, string>("Deviation", "1")], attribute.GetTraits());
        Assert.Equal(1, attribute.Bug);
        Assert.Equal("Synthetic: the port fixes it.", attribute.Reason);
    }

    [Theory]
    [InlineData(0, "x")]
    [InlineData(-1, "x")]
    [InlineData(1, "")]
    [InlineData(1, "  ")]
    public void A_malformed_deviation_is_rejected(int bug, string reason)
    {
        // ThrowsAny: a bad bug number is ArgumentOutOfRangeException, a blank reason a plain ArgumentException.
        Assert.ThrowsAny<ArgumentException>(() => new DeviationAttribute(bug, reason));
    }
}
