namespace OmenGamingHubUnlocker.Tests.Windows;

public sealed class TaskItemTests
{
    [Theory]
    [InlineData("Running", true)]
    [InlineData("Queued", true)]
    [InlineData("Ready", false)]
    [InlineData("Disabled", false)]
    [InlineData("Unknown", false)]
    public void RequiresStop_ShouldIncludeRunningAndQueuedStates(string state, bool expected)
    {
        var task = new TaskItem(@"\OmenTask", false, state);

        Assert.Equal(expected, task.RequiresStop);
    }

    [Fact]
    public void ActionPaths_ShouldReflectActionsChangedByARecordClone()
    {
        var original = new TaskItem(@"\OmenTask", true, "Ready", [@"C:\HP\v1.exe"]);

        var updated = original with { Actions = [@"C:\HP\v2.exe"] };

        Assert.Equal([@"C:\HP\v2.exe"], updated.ActionPaths);
    }
}
