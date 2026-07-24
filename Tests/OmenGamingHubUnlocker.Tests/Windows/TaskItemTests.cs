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
}
