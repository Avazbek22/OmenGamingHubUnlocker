namespace OmenGamingHubUnlocker.Core;

/// <summary>
/// Defines which scheduled-task runtime states can be restored and verified safely.
/// </summary>
public static class TaskStatePolicy
{
    public static bool MatchesOriginalRuntimeState(TaskItem task, TaskBackup backup)
        => backup.EffectiveOriginalRuntimeState == ScheduledTaskRuntimeState.Running
            ? task.IsRunning
            : !task.RequiresStop;

    public static string FormatExpectedRuntimeState(TaskBackup backup)
        => backup.EffectiveOriginalRuntimeState == ScheduledTaskRuntimeState.Running
            ? "Running"
            : "Not running";
}
