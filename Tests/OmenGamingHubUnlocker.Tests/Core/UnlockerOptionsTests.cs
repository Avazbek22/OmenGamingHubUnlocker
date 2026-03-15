namespace OmenGamingHubUnlocker.Tests.Core;

public sealed class UnlockerOptionsTests
{
    [Fact]
    public void ForDryRun_ShouldEnableDryRunAndSkipProcessTermination()
    {
        var options = UnlockerOptions.ForDryRun();

        Assert.True(options.DryRun);
        Assert.False(options.TryKillProcesses);
        Assert.True(options.ManageFirewall);
        Assert.True(options.ManageHosts);
    }

    [Fact]
    public void ForActivate_ShouldEnableLiveModeAndProcessTermination()
    {
        var options = UnlockerOptions.ForActivate();

        Assert.False(options.DryRun);
        Assert.True(options.TryKillProcesses);
        Assert.True(options.ManageFirewall);
        Assert.True(options.ManageHosts);
    }

    [Fact]
    public void ForDisable_ShouldEnableLiveModeWithoutProcessTermination()
    {
        var options = UnlockerOptions.ForDisable();

        Assert.False(options.DryRun);
        Assert.False(options.TryKillProcesses);
        Assert.True(options.ManageFirewall);
        Assert.True(options.ManageHosts);
    }

    [Fact]
    public void ForResetAndReapply_ShouldEnableLiveModeAndProcessTermination()
    {
        var options = UnlockerOptions.ForResetAndReapply();

        Assert.False(options.DryRun);
        Assert.True(options.TryKillProcesses);
        Assert.True(options.ManageFirewall);
        Assert.True(options.ManageHosts);
    }
}
