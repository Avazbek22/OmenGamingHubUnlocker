namespace OmenGamingHubUnlocker.Tests.Windows;

public sealed class TaskSchedulerManagerTests
{
    [Fact]
    public void DeserializeTasks_ShouldMapEveryNativeRuntimeStateAndNormalizePaths()
    {
        const string json = """
        [
          {"Path":"OMEN-Disabled","Enabled":false,"State":1,"Actions":[]},
          {"Path":"\\OMEN-Queued","Enabled":true,"State":2,"Actions":["queued.exe"]},
          {"Path":"\\OMEN-Ready","Enabled":true,"State":3,"Actions":["ready.exe"]},
          {"Path":"\\OMEN-Running","Enabled":true,"State":4,"Actions":["running.exe"]},
          {"Path":"\\OMEN-Unknown","Enabled":true,"State":99,"Actions":[]}
        ]
        """;

        var tasks = TaskSchedulerManager.DeserializeTasks(json);

        Assert.Equal(5, tasks.Count);
        Assert.Equal(@"\OMEN-Disabled", tasks[0].Path);
        Assert.Equal(ScheduledTaskRuntimeState.Disabled, tasks[0].RuntimeState);
        Assert.Equal(ScheduledTaskRuntimeState.Queued, tasks[1].RuntimeState);
        Assert.True(tasks[1].RequiresStop);
        Assert.Equal(ScheduledTaskRuntimeState.Ready, tasks[2].RuntimeState);
        Assert.Equal(ScheduledTaskRuntimeState.Running, tasks[3].RuntimeState);
        Assert.True(tasks[3].IsRunning);
        Assert.Equal(ScheduledTaskRuntimeState.Unknown, tasks[4].RuntimeState);
    }

    [Fact]
    public void DeserializeTasks_ShouldAcceptPowerShellSingletonObjectsAndScalarActions()
    {
        const string json = """
        {"Path":"\\OMEN","Enabled":true,"State":3,"Actions":"C:\\HP\\Omen.exe"}
        """;

        var task = Assert.Single(TaskSchedulerManager.DeserializeTasks(json));

        Assert.Equal(@"\OMEN", task.Path);
        Assert.Equal([@"C:\HP\Omen.exe"], task.ActionPaths);
    }
}
