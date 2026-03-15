namespace OmenGamingHubUnlocker.Tests.Windows;

public sealed class ProcessManagerTests
{
    [Fact]
    public void FindMatchingProcesses_ShouldNeverReturnCurrentTestHostProcess()
    {
        var currentProcess = Process.GetCurrentProcess();

        var matches = ProcessManager.FindMatchingProcesses([$"*{currentProcess.ProcessName}*"]);

        Assert.DoesNotContain(matches, process => process.Id == currentProcess.Id);
    }

    [Fact]
    public void FindMatchingProcesses_ShouldFindSpawnedChildProcess()
    {
        using var childProcess = ChildProcessScope.StartUniqueNamedWaitProcess();

        var matches = ProcessManager.FindMatchingProcesses([$"*{childProcess.ExpectedProcessName}*"]);

        Assert.Contains(matches, process => process.Id == childProcess.Process.Id);
    }

    [Fact]
    public void TryKillMatchingProcesses_WithDryRun_ShouldNotTerminateChildProcess()
    {
        using var childProcess = ChildProcessScope.StartUniqueNamedWaitProcess();

        var killed = ProcessManager.TryKillMatchingProcesses([$"*{childProcess.ExpectedProcessName}*"], dryRun: true);

        Assert.Contains(killed, label => label.Contains(childProcess.Process.Id.ToString(), StringComparison.Ordinal));
        Assert.False(childProcess.Process.HasExited);
    }

    [Fact]
    public void TryKillMatchingProcesses_ShouldTerminateSpawnedChildProcess()
    {
        using var childProcess = ChildProcessScope.StartUniqueNamedWaitProcess();

        var killed = ProcessManager.TryKillMatchingProcesses([$"*{childProcess.ExpectedProcessName}*"], dryRun: false);
        childProcess.Process.WaitForExit(5_000);

        Assert.Contains(killed, label => label.Contains(childProcess.Process.Id.ToString(), StringComparison.Ordinal));
        Assert.True(childProcess.Process.HasExited);
    }
}
