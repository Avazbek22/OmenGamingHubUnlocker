namespace OmenGamingHubUnlocker.Tests.Core;

public sealed class WildcardMatcherTests
{
    [Theory]
    [InlineData("OmenCommandCenterBackground", "*omen*", true)]
    [InlineData("OverlayHelper", "OverlayHelper", true)]
    [InlineData("OverlayHelper.exe", "OverlayHelper", false)]
    [InlineData("HPSupportAssistant", "*OMEN*", false)]
    [InlineData("", "*", true)]
    public void IsMatch_ShouldApplyAnchoredCaseInsensitiveWildcards(
        string value,
        string pattern,
        bool expected)
    {
        Assert.Equal(expected, WildcardMatcher.IsMatch(value, pattern));
    }

    [Fact]
    public void IsMatch_ShouldTreatRegexCharactersAsLiterals()
    {
        Assert.True(WildcardMatcher.IsMatch("HP.Omen[1]", "HP.Omen[1]"));
        Assert.False(WildcardMatcher.IsMatch("HPxOmen1", "HP.Omen[1]"));
    }
}
