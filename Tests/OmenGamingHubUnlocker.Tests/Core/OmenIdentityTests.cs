namespace OmenGamingHubUnlocker.Tests.Core;

public sealed class OmenIdentityTests
{
    [Theory]
    [InlineData("OmenInstallMonitor")]
    [InlineData("HPOmenCap")]
    [InlineData("HP.OMEN.Background")]
    [InlineData(@"C:\Program Files\HP\OMEN Gaming Hub\Omen.exe")]
    [InlineData(@"C:\Windows\System32\DriverStore\FileRepository\hpomencustomcap\OmenCap.exe")]
    public void IsLikelyOmenReference_ShouldAcceptKnownNamingForms(string value)
    {
        Assert.True(OmenIdentity.IsLikelyOmenReference(value));
    }

    [Theory]
    [InlineData("WomenService")]
    [InlineData("MomentService")]
    [InlineData("SomeName")]
    [InlineData("")]
    public void IsLikelyOmenReference_ShouldRejectIncidentalSubstrings(string value)
    {
        Assert.False(OmenIdentity.IsLikelyOmenReference(value));
    }
}
