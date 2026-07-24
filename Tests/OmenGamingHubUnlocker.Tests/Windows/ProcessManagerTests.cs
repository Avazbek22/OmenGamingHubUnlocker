namespace OmenGamingHubUnlocker.Tests.Windows;

public sealed class ProcessManagerTests
{
    [Fact]
    public void QueryTargetProcesses_ShouldNeverReturnCurrentTestHostProcess()
    {
        var currentProcess = Process.GetCurrentProcess();

        var matches = ProcessManager.QueryTargetProcesses([$"*{currentProcess.ProcessName}*"], []);

        Assert.DoesNotContain(matches, process => process.Id == currentProcess.Id);
    }

    [Fact]
    public void QueryTargetProcesses_ShouldFindSpawnedChildProcess()
    {
        using var childProcess = ChildProcessScope.StartUniqueNamedWaitProcess();

        var matches = ProcessManager.QueryTargetProcesses([$"*{childProcess.ExpectedProcessName}*"], []);

        Assert.Contains(matches, process => process.Id == childProcess.Process.Id);
    }

    [Fact]
    public void TerminateTargetProcesses_WithDryRun_ShouldNotTerminateChildProcess()
    {
        using var childProcess = ChildProcessScope.StartUniqueNamedWaitProcess();

        var lines = ProcessManager.TerminateTargetProcesses(
            [$"*{childProcess.ExpectedProcessName}*"],
            [],
            dryRun: true);

        Assert.Contains(
            lines,
            line => line.Text.Contains(
                childProcess.Process.Id.ToString(CultureInfo.InvariantCulture),
                StringComparison.Ordinal));
        Assert.False(childProcess.Process.HasExited);
    }

    [Fact]
    public void TerminateTargetProcesses_ShouldTerminateSpawnedChildProcess()
    {
        using var childProcess = ChildProcessScope.StartUniqueNamedWaitProcess();

        var lines = ProcessManager.TerminateTargetProcesses(
            [$"*{childProcess.ExpectedProcessName}*"],
            [],
            dryRun: false);
        childProcess.Process.WaitForExit(5_000);

        Assert.Contains(
            lines,
            line => line.Text.Contains(
                childProcess.Process.Id.ToString(CultureInfo.InvariantCulture),
                StringComparison.Ordinal));
        Assert.DoesNotContain(lines, line => line.Level == "ERR");
        Assert.True(childProcess.Process.HasExited);
    }
}
