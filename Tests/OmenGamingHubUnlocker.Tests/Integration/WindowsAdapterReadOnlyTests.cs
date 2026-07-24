namespace OmenGamingHubUnlocker.Tests.Integration;

/// <summary>
/// Exercises the real read-only adapters without changing services, tasks, firewall, registry, or hosts.
/// </summary>
public sealed class WindowsAdapterReadOnlyTests
{
    [Fact]
    public void UserContextInspection_ShouldIdentifyTheInteractiveDesktopOwner()
    {
        var context = UserContextManager.Inspect();

        Assert.True(context.InspectionSucceeded, context.Error);
        Assert.NotEmpty(context.ProcessIdentity);
        Assert.NotEmpty(context.InteractiveIdentity);
        Assert.True(context.IsSafe);
    }

    [Fact]
    public void FirewallInspection_ShouldReturnStructuredState()
    {
        var status = FirewallManager.InspectProtection(OmenTargets.FirewallRulePrefix);

        Assert.True(status.QuerySucceeded, status.Error);
        Assert.NotNull(status.Targets);
        Assert.NotNull(status.Rules);
        Assert.NotNull(status.MissingExecutableRules);
        Assert.NotNull(status.StaleExecutableRules);
    }

    [Fact]
    public void InstalledOmenPackage_ShouldHaveAResolvableStableSid()
    {
        var found = AppxPackageManager.TryGetPrimaryPackage(
            OmenTargets.AppxFilters,
            out var package,
            out _);

        if (!found || package is null)
            return;

        var resolved = AppContainerSidResolver.TryResolve(
            package.PackageFamilyName,
            out var sid,
            out var error);

        Assert.True(resolved, error);
        Assert.StartsWith("S-1-15-2-", sid, StringComparison.Ordinal);
    }

    [Fact]
    public void RealEngineDryRun_ShouldCompleteEndToEndWithoutChangingWindows()
    {
        var engine = new UnlockerEngine();

        var report = engine.RunDryRunDeep();

        Assert.NotEmpty(report.Lines);
        Assert.NotEmpty(report.SnapshotsAfter);
        Assert.Contains(report.Lines, line =>
            line.Text.Contains("AppX", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(report.SnapshotsAfter, snapshot => snapshot.Area == "Firewall");
        Assert.DoesNotContain(report.Lines, line =>
            line.Text.StartsWith("engine.", StringComparison.Ordinal) ||
            line.Text.StartsWith("manager.", StringComparison.Ordinal));
    }
}
