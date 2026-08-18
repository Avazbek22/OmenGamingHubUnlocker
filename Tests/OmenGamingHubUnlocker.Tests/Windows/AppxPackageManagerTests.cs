namespace OmenGamingHubUnlocker.Tests.Windows;

public sealed class AppxPackageManagerTests
{
    [Fact]
    public void TrySelectPrimaryPackage_ShouldPreferOnlyExactPackageIdentity()
    {
        var expected = CreatePackage(OmenTargets.PrimaryAppxPackageName, "current");
        var related = CreatePackage("ThirdParty.OMENGamingTools", "related");

        var selected = AppxPackageManager.TrySelectPrimaryPackage(
            [related, expected],
            out var package,
            out _);

        Assert.True(selected);
        Assert.Equal(expected, package);
    }

    [Fact]
    public void TrySelectPrimaryPackage_ShouldRejectBroadMatchWithoutExactIdentity()
    {
        var selected = AppxPackageManager.TrySelectPrimaryPackage(
            [CreatePackage("ThirdParty.OMENGamingTools", "related")],
            out var package,
            out var details);

        Assert.False(selected);
        Assert.Null(package);
        Assert.NotEmpty(details);
    }

    [Fact]
    public void TrySelectPrimaryPackage_ShouldRejectMultipleExactRegistrations()
    {
        var selected = AppxPackageManager.TrySelectPrimaryPackage(
            [
                CreatePackage(OmenTargets.PrimaryAppxPackageName, "one"),
                CreatePackage(OmenTargets.PrimaryAppxPackageName, "two")
            ],
            out var package,
            out var details);

        Assert.False(selected);
        Assert.Null(package);
        Assert.NotEmpty(details);
    }

    private static AppxPackageInfo CreatePackage(string name, string suffix)
        => new(name, $"{name}_{suffix}", $"{name}_1.0.0.0_x64__{suffix}", $@"C:\WindowsApps\{suffix}");
}
