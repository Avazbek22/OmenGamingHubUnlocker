namespace OmenGamingHubUnlocker.Tests.Windows;

public sealed class FirewallProtectionStatusTests
{
    private const string CurrentExecutable = @"C:\WindowsApps\Omen\v2\Omen.exe";
    private const string PackageSid = "S-1-15-2-100";

    [Fact]
    public void IsComplete_ShouldRequireCurrentExecutableAndPackageRules()
    {
        var targets = CreateTargets();
        var status = new FirewallProtectionStatus(
            true,
            targets,
            [],
            [CurrentExecutable],
            [],
            false,
            string.Empty);

        Assert.False(status.IsComplete);
    }

    [Fact]
    public void IsComplete_ShouldRejectFailedInspection()
    {
        var status = new FirewallProtectionStatus(
            false,
            CreateTargets(),
            [],
            [],
            [],
            true,
            "access denied");

        Assert.False(status.IsComplete);
    }

    [Fact]
    public void IsComplete_ShouldAcceptFullyCoveredCurrentTargets()
    {
        var status = new FirewallProtectionStatus(
            true,
            CreateTargets(),
            [
                new FirewallRuleInfo("program", true, true, true, CurrentExecutable, string.Empty),
                new FirewallRuleInfo("package", true, true, true, string.Empty, PackageSid)
            ],
            [],
            [],
            true,
            string.Empty);

        Assert.True(status.IsComplete);
        Assert.True(status.IsReconciled);
    }

    [Fact]
    public void IsComplete_ShouldAllowExecutableFallback_WhenPackageSidIsUnavailable()
    {
        var targets = CreateTargets(packageSid: string.Empty);
        var status = new FirewallProtectionStatus(
            true,
            targets,
            [new FirewallRuleInfo("program", true, true, true, CurrentExecutable, string.Empty)],
            [],
            [],
            false,
            string.Empty);

        Assert.True(status.IsComplete);
        Assert.False(status.PackageRuleRequired);
    }

    [Fact]
    public void IsComplete_ShouldRejectPackageMetadataWithoutAUsableProtectionIdentity()
    {
        var targets = new FirewallTargetSet(
            new AppxPackageInfo("Omen", "Omen_family", "Omen_2", @"C:\WindowsApps\Omen\v2"),
            string.Empty,
            "SID resolution failed",
            new HashSet<string>(),
            new HashSet<string>());
        var status = new FirewallProtectionStatus(
            true,
            targets,
            [],
            [],
            [],
            false,
            string.Empty);

        Assert.False(status.IsComplete);
    }

    [Fact]
    public void IsComplete_ShouldRejectPartialExecutableDiscovery()
    {
        var targets = CreateTargets() with
        {
            PackageDirectoryReady = false,
            DiscoveryErrors = ["access denied"]
        };
        var status = new FirewallProtectionStatus(
            true,
            targets,
            [
                new FirewallRuleInfo("program", true, true, true, CurrentExecutable, string.Empty),
                new FirewallRuleInfo("package", true, true, true, string.Empty, PackageSid)
            ],
            [],
            [],
            true,
            string.Empty);

        Assert.False(targets.DiscoveryComplete);
        Assert.False(status.IsComplete);
    }

    [Fact]
    public void IsComplete_ShouldRejectRulesWhenFirewallEnforcementIsInactive()
    {
        var status = new FirewallProtectionStatus(
            true,
            CreateTargets(),
            [],
            [],
            [],
            true,
            "Windows Firewall is disabled",
            EnforcementActive: false,
            EnforcementDetails: "Public");

        Assert.False(status.IsComplete);
    }

    [Fact]
    public void IsReconciled_ShouldRejectObsoleteRulesWithoutWeakeningActiveProtection()
    {
        var status = new FirewallProtectionStatus(
            true,
            CreateTargets(),
            [
                new FirewallRuleInfo("program", true, true, true, CurrentExecutable, string.Empty),
                new FirewallRuleInfo("package", true, true, true, string.Empty, PackageSid),
                new FirewallRuleInfo("old", true, true, true, @"C:\WindowsApps\Omen\v1\Omen.exe", string.Empty)
            ],
            [],
            [@"C:\WindowsApps\Omen\v1\Omen.exe"],
            true,
            string.Empty);

        Assert.True(status.IsComplete);
        Assert.False(status.IsReconciled);
    }

    [Fact]
    public void DerivedTargetCollections_ShouldReflectRecordCloneChanges()
    {
        var original = CreateTargets();

        var updated = original with
        {
            ExternalExecutables = new HashSet<string>([@"C:\HP\OmenHelper.exe"]),
            DiscoveryErrors = ["scan failed"]
        };

        Assert.Contains(@"C:\HP\OmenHelper.exe", updated.AllExecutables);
        Assert.Equal(["scan failed"], updated.ScanErrors);
        Assert.False(updated.DiscoveryComplete);
    }

    private static FirewallTargetSet CreateTargets(string packageSid = PackageSid)
        => new(
            new AppxPackageInfo("Omen", "Omen_family", "Omen_2", @"C:\WindowsApps\Omen\v2"),
            packageSid,
            string.Empty,
            new HashSet<string>([CurrentExecutable], StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));
}
