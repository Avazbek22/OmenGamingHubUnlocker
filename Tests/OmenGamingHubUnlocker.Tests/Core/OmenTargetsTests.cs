namespace OmenGamingHubUnlocker.Tests.Core;

public sealed class OmenTargetsTests
{
    [Fact]
    public void HostsDomains_ShouldContainKnownEndpointsWithoutDuplicates()
    {
        Assert.NotEmpty(OmenTargets.HostsDomains);
        Assert.Equal(OmenTargets.HostsDomains.Length, OmenTargets.HostsDomains.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Contains("hpbp.io", OmenTargets.HostsDomains);
    }

    [Fact]
    public void ServicePatterns_ShouldContainOmenAndHpPatterns()
    {
        Assert.Contains("*OMEN*", OmenTargets.ServicePatterns);
        Assert.Contains("*HP Gaming*", OmenTargets.ServicePatterns);
        Assert.NotEmpty(OmenTargets.ServicePatterns);
    }

    [Fact]
    public void TaskPatterns_ShouldContainOverlayRelatedPatterns()
    {
        Assert.Contains("*Omen*", OmenTargets.TaskPatterns);
        Assert.Contains("*HP.OMEN*", OmenTargets.TaskPatterns);
        Assert.NotEmpty(OmenTargets.TaskPatterns);
    }

    [Fact]
    public void Constants_ShouldExposeStableMarkers()
    {
        Assert.Equal("Tame-OMEN", OmenTargets.FirewallRulePrefix);
        Assert.Equal("# OmenGamingHubUnlocker", OmenTargets.HostsMarker);
        Assert.False(string.IsNullOrWhiteSpace(OmenTargets.PrimaryAppxPackageName));
    }
}
