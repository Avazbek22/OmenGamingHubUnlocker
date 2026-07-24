namespace OmenGamingHubUnlocker.Tests.Core;

public sealed class OmenTargetsTests
{
    [Fact]
    public void ServicePatterns_ShouldNotTargetSupportAssistant()
    {
        Assert.DoesNotContain(
            OmenTargets.ServicePatterns,
            pattern => pattern.Contains("SupportAssistant", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(@"HP\OmenInstallMonitor")]
    [InlineData(@"HP\Overlay")]
    public void ExternalExecutableDirectories_ShouldIncludeCurrentBackgroundComponents(string directory)
    {
        Assert.Contains(directory, OmenTargets.ExtraExeDirsRelative, StringComparer.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("OverlayHelper")]
    [InlineData("OmenCommandCenterBackground")]
    [InlineData("HP.Omen.Application.Background.Tasks.Container")]
    public void ProcessPatterns_ShouldMatchKnownBackgroundComponents(string processName)
    {
        Assert.Contains(
            OmenTargets.ProcessNamePatterns,
            pattern => WildcardMatcher.IsMatch(processName, pattern));
    }

    [Fact]
    public void TargetCollections_ShouldNotContainDuplicates()
    {
        AssertDistinct(OmenTargets.HostsDomains);
        AssertDistinct(OmenTargets.ServicePatterns);
        AssertDistinct(OmenTargets.TaskPatterns);
        AssertDistinct(OmenTargets.RunEntryPatterns);
        AssertDistinct(OmenTargets.ProcessNamePatterns);
        AssertDistinct(OmenTargets.ExtraExeDirsRelative);
    }

    private static void AssertDistinct(IEnumerable<string> values)
    {
        var items = values.ToList();
        Assert.Equal(items.Count, items.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }
}
