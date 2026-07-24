namespace OmenGamingHubUnlocker.Windows;

/// <summary>
/// Snapshot of a scheduled task that the unlocker may inspect or modify.
/// </summary>
public sealed record TaskItem(
    string Path,
    bool Enabled,
    string State = "Unknown",
    IReadOnlyList<string>? Actions = null)
{
    public IReadOnlyList<string> ActionPaths { get; } = Actions ?? [];
    public bool IsRunning => State.Equals("Running", StringComparison.OrdinalIgnoreCase);
    public bool RequiresStop =>
        IsRunning ||
        State.Equals("Queued", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Describes the desired enabled flag for a scheduled task.
/// </summary>
public sealed record TaskEnableTarget(string Path, bool Enabled);

/// <summary>
/// Encapsulates COM-based Task Scheduler discovery and state changes.
/// </summary>
public static class TaskSchedulerManager
{
    public static (bool ok, string details) CheckCapability()
    {
        try
        {
            var schedulerType = Type.GetTypeFromProgID("Schedule.Service");
            if (schedulerType is null)
                return (false, Text.Get("manager.taskScheduler.capabilityNotAvailable"));

            dynamic taskScheduler = Activator.CreateInstance(schedulerType)!;
            taskScheduler.Connect();
            return (true, Text.Get("manager.taskScheduler.capabilityOk"));
        }
        catch (Exception exception)
        {
            return (false, exception.Message);
        }
    }

    public static List<TaskItem> QueryTasks(string[] patterns)
    {
        var matchingTasks = new List<TaskItem>();
        var matchEverything = patterns.Length == 0;

        try
        {
            var schedulerType = Type.GetTypeFromProgID("Schedule.Service");
            if (schedulerType is null)
                throw new InvalidOperationException(Text.Get("manager.taskScheduler.capabilityNotAvailable"));

            dynamic taskScheduler = Activator.CreateInstance(schedulerType)!;
            taskScheduler.Connect();

            dynamic rootFolder = taskScheduler.GetFolder("\\");
            EnumerateFolder(rootFolder, patterns, matchingTasks, matchEverything);
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                $"Task Scheduler discovery failed: {exception.Message}",
                exception);
        }

        return matchingTasks
            .DistinctBy(task => task.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static List<OperationLine> ApplyEnabledTargets(IEnumerable<TaskEnableTarget> targets, bool dryRun)
    {
        var requestedTargets = targets
            .Select(target => target with { Path = NormalizeTaskPath(target.Path) })
            .DistinctBy(target => target.Path, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(target => target.Path, target => target.Enabled, StringComparer.OrdinalIgnoreCase);

        if (requestedTargets.Count == 0)
        {
            return
            [
                LocalizedLine.Info("manager.tasks.nothingToChange")
            ];
        }

        var currentTasks = QueryTasks(Array.Empty<string>())
            .ToDictionary(task => NormalizeTaskPath(task.Path), task => task, StringComparer.OrdinalIgnoreCase);

        var operationLines = new List<OperationLine>();

        foreach (var (normalizedTaskPath, desiredEnabled) in requestedTargets.OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (!currentTasks.TryGetValue(normalizedTaskPath, out var currentTask))
            {
                operationLines.Add(LocalizedLine.Warn("manager.tasks.notFound", normalizedTaskPath));
                continue;
            }

            if (currentTask.Enabled == desiredEnabled)
            {
                operationLines.Add(LocalizedLine.Info(
                    "manager.tasks.alreadyState",
                    currentTask.Path,
                    desiredEnabled ? Text.Get("state.enabled") : Text.Get("state.disabled")));
                continue;
            }

            if (dryRun)
            {
                operationLines.Add(LocalizedLine.Ok(
                    "manager.tasks.wouldSetState",
                    desiredEnabled ? Text.Get("state.enabled") : Text.Get("state.disabled"),
                    currentTask.Path));
                continue;
            }

            try
            {
                SetEnabledViaCom(currentTask.Path, desiredEnabled);
                operationLines.Add(LocalizedLine.Ok(
                    "manager.tasks.setState",
                    desiredEnabled ? Text.Get("state.enabled") : Text.Get("state.disabled"),
                    currentTask.Path));
            }
            catch (Exception exception)
            {
                var schtasksFlag = desiredEnabled ? "/ENABLE" : "/DISABLE";
                var fallbackApplied = PowerShellRunner.TryRun(
                    "schtasks.exe",
                    $"/Change /TN \"{currentTask.Path}\" {schtasksFlag}",
                    out _,
                    out var fallbackError,
                    20_000);

                operationLines.Add(fallbackApplied
                    ? LocalizedLine.Warn("manager.tasks.fallbackApplied", currentTask.Path)
                    : LocalizedLine.Err("manager.tasks.failed", currentTask.Path, exception.Message, fallbackError));
            }
        }

        return operationLines;
    }

    public static List<OperationLine> StopTasks(IEnumerable<string> taskPaths, bool dryRun)
    {
        var requestedPaths = taskPaths
            .Select(NormalizeTaskPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (requestedPaths.Count == 0)
            return [LocalizedLine.Info("manager.tasks.nothingToStop")];

        var currentTasks = QueryTasks([])
            .ToDictionary(task => NormalizeTaskPath(task.Path), StringComparer.OrdinalIgnoreCase);
        var operationLines = new List<OperationLine>();

        foreach (var taskPath in requestedPaths)
        {
            if (!currentTasks.TryGetValue(taskPath, out var currentTask))
            {
                operationLines.Add(LocalizedLine.Err("manager.tasks.notFound", taskPath));
                continue;
            }

            if (!currentTask.RequiresStop)
            {
                operationLines.Add(LocalizedLine.Info("manager.tasks.alreadyStopped", taskPath));
                continue;
            }

            if (dryRun)
            {
                operationLines.Add(LocalizedLine.Ok("manager.tasks.wouldStop", taskPath));
                continue;
            }

            try
            {
                StopViaCom(taskPath);
                operationLines.Add(LocalizedLine.Ok("manager.tasks.stopped", taskPath));
            }
            catch (Exception exception)
            {
                var fallbackApplied = PowerShellRunner.TryRun(
                    "schtasks.exe",
                    $"/End /TN \"{taskPath}\"",
                    out _,
                    out var fallbackError,
                    20_000);

                operationLines.Add(fallbackApplied
                    ? LocalizedLine.Warn("manager.tasks.stopFallbackApplied", taskPath)
                    : LocalizedLine.Err("manager.tasks.failedToStop", taskPath, exception.Message, fallbackError));
            }
        }

        return operationLines;
    }

    private static void EnumerateFolder(dynamic folder, string[] patterns, List<TaskItem> destination, bool matchEverything)
    {
        dynamic tasks = folder.GetTasks(1);
        int taskCount = tasks.Count;

        for (var index = 1; index <= taskCount; index++)
        {
            dynamic task = tasks.Item(index);
            string taskName = task.Name;
            string taskPath = task.Path;
            bool isEnabled = task.Enabled;
            string taskState = MapTaskState((int)task.State);
            IReadOnlyList<string> actionPaths = ReadActionPaths(task);

            if (matchEverything ||
                patterns.Any(pattern =>
                    WildcardMatcher.IsMatch(taskName, pattern) ||
                    WildcardMatcher.IsMatch(taskPath, pattern) ||
                    actionPaths.Any(action => WildcardMatcher.IsMatch(action, pattern))))
            {
                destination.Add(new TaskItem(taskPath, isEnabled, taskState, actionPaths));
            }
        }

        dynamic subFolders = folder.GetFolders(0);
        int subFolderCount = subFolders.Count;

        for (var index = 1; index <= subFolderCount; index++)
        {
            dynamic subFolder = subFolders.Item(index);
            EnumerateFolder(subFolder, patterns, destination, matchEverything);
        }
    }

    private static void SetEnabledViaCom(string taskPath, bool enabled)
    {
        var schedulerType = Type.GetTypeFromProgID("Schedule.Service")
                            ?? throw new InvalidOperationException(Text.Get("manager.taskScheduler.capabilityNotAvailable"));

        dynamic taskScheduler = Activator.CreateInstance(schedulerType)!;
        taskScheduler.Connect();

        var normalizedTaskPath = NormalizeTaskPath(taskPath);
        var lastPathSeparator = normalizedTaskPath.LastIndexOf('\\');
        var folderPath = lastPathSeparator <= 0 ? "\\" : normalizedTaskPath[..lastPathSeparator];
        var taskName = normalizedTaskPath[(lastPathSeparator + 1)..];

        dynamic folder = taskScheduler.GetFolder(folderPath);
        dynamic task = folder.GetTask(taskName);
        task.Enabled = enabled;
    }

    private static void StopViaCom(string taskPath)
    {
        var schedulerType = Type.GetTypeFromProgID("Schedule.Service")
                            ?? throw new InvalidOperationException(Text.Get("manager.taskScheduler.capabilityNotAvailable"));

        dynamic taskScheduler = Activator.CreateInstance(schedulerType)!;
        taskScheduler.Connect();

        var normalizedTaskPath = NormalizeTaskPath(taskPath);
        var lastPathSeparator = normalizedTaskPath.LastIndexOf('\\');
        var folderPath = lastPathSeparator <= 0 ? "\\" : normalizedTaskPath[..lastPathSeparator];
        var taskName = normalizedTaskPath[(lastPathSeparator + 1)..];

        dynamic folder = taskScheduler.GetFolder(folderPath);
        dynamic task = folder.GetTask(taskName);
        task.Stop(0);
    }

    private static List<string> ReadActionPaths(dynamic task)
    {
        try
        {
            dynamic actions = task.Definition.Actions;
            var result = new List<string>();
            int actionCount = actions.Count;

            for (var index = 1; index <= actionCount; index++)
            {
                dynamic action = actions.Item(index);
                try
                {
                    string path = action.Path;
                    if (!string.IsNullOrWhiteSpace(path))
                        result.Add(path);
                }
                catch
                {
                    // Non-executable actions do not expose a Path property.
                }
            }

            return result;
        }
        catch
        {
            return [];
        }
    }

    private static string MapTaskState(int taskState)
        => taskState switch
        {
            1 => "Disabled",
            2 => "Queued",
            3 => "Ready",
            4 => "Running",
            _ => "Unknown"
        };

    private static string NormalizeTaskPath(string taskPath)
        => taskPath.StartsWith('\\') ? taskPath : "\\" + taskPath;

}
