namespace OmenGamingHubUnlocker.Tests.Windows;

public sealed class AppContainerSidResolverTests
{
    [Fact]
    public void TryResolve_ShouldDeriveStableSidFromPackageFamilyName()
    {
        var resolved = AppContainerSidResolver.TryResolve(
            "AD2F1837.OMENCommandCenter_v10z8vjag6ke6",
            out var sid,
            out var error);

        Assert.True(resolved, error);
        Assert.StartsWith("S-1-15-2-", sid, StringComparison.Ordinal);
    }

    [Fact]
    public void TryResolve_ShouldRejectEmptyPackageFamilyName()
    {
        var resolved = AppContainerSidResolver.TryResolve(string.Empty, out var sid, out var error);

        Assert.False(resolved);
        Assert.Empty(sid);
        Assert.NotEmpty(error);
    }
}
