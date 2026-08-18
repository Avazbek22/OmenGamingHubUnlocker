namespace OmenGamingHubUnlocker.Tests.Core;

public sealed class TaskStatePolicyTests
{
    [Theory]
    [InlineData(ScheduledTaskRuntimeState.Running, "Running", true)]
    [InlineData(ScheduledTaskRuntimeState.Running, "Ready", false)]
    [InlineData(ScheduledTaskRuntimeState.Ready, "Ready", true)]
    [InlineData(ScheduledTaskRuntimeState.Ready, "Running", false)]
    [InlineData(ScheduledTaskRuntimeState.Queued, "Ready", true)]
    [InlineData(ScheduledTaskRuntimeState.Queued, "Queued", false)]
    public void MatchesOriginalRuntimeState_ShouldEnforceOnlySafelyReproducibleStates(
        ScheduledTaskRuntimeState originalState,
        string currentState,
        bool expected)
    {
        var task = new TaskItem(@"\OMEN", true, currentState);
        var backup = new TaskBackup(@"\OMEN", true, OriginalRuntimeState: originalState);

        Assert.Equal(expected, TaskStatePolicy.MatchesOriginalRuntimeState(task, backup));
    }
}
