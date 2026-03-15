namespace OmenGamingHubUnlocker.Tests.Integration;

public sealed class UnlockerEngineDryRunTests
{
    private static readonly UnlockerOptions DryRunOptions = new()
    {
        DryRun = true,
        ManageFirewall = true,
        ManageHosts = true,
        TryKillProcesses = false
    };

    [Fact]
    public void GetStatusReport_ShouldReturnNonNegativeCounters()
    {
        var engine = new UnlockerEngine();

        var report = engine.GetStatusReport();

        Assert.NotNull(report);
        Assert.True(report.ServicesMatched >= 0);
        Assert.True(report.TasksMatched >= 0);
        Assert.True(report.RunEntriesMatched >= 0);
        Assert.True(report.FirewallRulesFound >= 0);
        Assert.NotNull(report.Snapshots);
    }

    [Fact]
    public void RunDryRunDeep_ShouldReturnLinesAndSnapshots()
    {
        var engine = new UnlockerEngine();

        var report = engine.RunDryRunDeep();

        Assert.True(report.Success);
        Assert.NotEmpty(report.Lines);
        Assert.NotEmpty(report.SnapshotsAfter);
    }

    [Fact]
    public void Activate_WithDryRun_ShouldReturnSnapshot()
    {
        var engine = new UnlockerEngine();

        var report = engine.Activate(DryRunOptions);

        Assert.NotEmpty(report.Lines);
        Assert.NotEmpty(report.SnapshotsAfter);
    }

    [Fact]
    public void ResetAndReapply_WithDryRun_ShouldReturnSnapshot()
    {
        var engine = new UnlockerEngine();

        var report = engine.ResetAndReapply(DryRunOptions);

        Assert.NotEmpty(report.Lines);
        Assert.NotEmpty(report.SnapshotsAfter);
    }

    [Fact]
    public void Disable_WithDryRun_ShouldReturnSnapshot()
    {
        var engine = new UnlockerEngine();

        var report = engine.Disable(DryRunOptions);

        Assert.NotEmpty(report.Lines);
        Assert.NotEmpty(report.SnapshotsAfter);
    }
}
