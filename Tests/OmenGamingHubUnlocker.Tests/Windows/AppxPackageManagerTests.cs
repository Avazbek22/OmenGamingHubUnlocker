namespace OmenGamingHubUnlocker.Tests.Windows;

public sealed class AppxPackageManagerTests
{
    [Fact]
    public void CheckResetCapability_ShouldReturnDetails()
    {
        var (_, details) = AppxPackageManager.CheckResetCapability();

        Assert.False(string.IsNullOrWhiteSpace(details));
    }

    [Fact]
    public void QueryPackages_ShouldReturnDistinctPackageFullNames()
    {
        var packages = AppxPackageManager.QueryPackages(OmenTargets.AppxFilters);

        Assert.Equal(
            packages.Count,
            packages.Select(package => package.PackageFullName).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void TryGetPrimaryPackage_ShouldReturnConsistentDetails()
    {
        var found = AppxPackageManager.TryGetPrimaryPackage(OmenTargets.AppxFilters, out var package, out var details);

        Assert.False(string.IsNullOrWhiteSpace(details));

        if (found)
        {
            Assert.NotNull(package);
            Assert.Contains(package!.PackageFullName, details, StringComparison.Ordinal);
        }
        else
        {
            Assert.Null(package);
        }
    }

    [Fact]
    public void ResetPackage_DryRun_ShouldProduceOperationLines()
    {
        var lines = AppxPackageManager.ResetPackage(OmenTargets.AppxFilters, dryRun: true);

        Assert.NotEmpty(lines);
        Assert.All(lines, line => Assert.False(string.IsNullOrWhiteSpace(line.Text)));
    }
}
