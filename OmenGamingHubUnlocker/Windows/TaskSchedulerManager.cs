namespace OmenGamingHubUnlocker.Windows;

/// <summary>
/// Snapshot of a scheduled task that the unlocker may inspect or modify.
/// </summary>
public sealed record TaskItem(string Path, bool Enabled);

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
                return (false, "Schedule.Service COM not available.");

            dynamic taskScheduler = Activator.CreateInstance(schedulerType)!;
            taskScheduler.Connect();
            return (true, "COM connect ok.");
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
                return matchingTasks;

            dynamic taskScheduler = Activator.CreateInstance(schedulerType)!;
            taskScheduler.Connect();

            dynamic rootFolder = taskScheduler.GetFolder("\\");
            EnumerateFolder(rootFolder, patterns, matchingTasks, matchEverything);
        }
        catch
        {
            // Querying tasks is best-effort because some machines can have custom scheduler ACLs.
        }

        return matchingTasks
            .DistinctBy(task => task.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static List<OperationLine> ApplyEnabledTargets(IEnumerable<TaskEnableTarget> targets, bool dryRun)
    {
        var requestedTargets = targets
            .DistinctBy(target => target.Path, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(target => NormalizeTaskPath(target.Path), target => target.Enabled, StringComparer.OrdinalIgnoreCase);

        if (requestedTargets.Count == 0)
        {
            return
            [
                new OperationLine { Level = "INFO", Text = "Tasks: nothing to change." }
            ];
        }

        var currentTasks = QueryTasks(Array.Empty<string>())
            .ToDictionary(task => NormalizeTaskPath(task.Path), task => task, StringComparer.OrdinalIgnoreCase);

        var operationLines = new List<OperationLine>();

        foreach (var (normalizedTaskPath, desiredEnabled) in requestedTargets.OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (!currentTasks.TryGetValue(normalizedTaskPath, out var currentTask))
            {
                operationLines.Add(new OperationLine { Level = "WARN", Text = $"Tasks: {normalizedTaskPath} was not found." });
                continue;
            }

            if (currentTask.Enabled == desiredEnabled)
            {
                operationLines.Add(new OperationLine
                {
                    Level = "INFO",
                    Text = $"Tasks: {currentTask.Path} already {(desiredEnabled ? "enabled" : "disabled")}."
                });
                continue;
            }

            if (dryRun)
            {
                operationLines.Add(new OperationLine
                {
                    Level = "OK",
                    Text = $"Tasks: would set {(desiredEnabled ? "Enabled" : "Disabled")} -> {currentTask.Path}"
                });
                continue;
            }

            try
            {
                SetEnabledViaCom(currentTask.Path, desiredEnabled);
                operationLines.Add(new OperationLine
                {
                    Level = "OK",
                    Text = $"Tasks: set {(desiredEnabled ? "Enabled" : "Disabled")} -> {currentTask.Path}"
                });
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

                operationLines.Add(new OperationLine
                {
                    Level = fallbackApplied ? "WARN" : "ERR",
                    Text = fallbackApplied
                        ? $"Tasks: COM failed for {currentTask.Path}, schtasks.exe fallback applied."
                        : $"Tasks: failed for {currentTask.Path}. COM error: {exception.Message}. schtasks.exe error: {fallbackError}"
                });
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

            if (matchEverything || patterns.Any(pattern => WildcardMatch(taskName, pattern) || WildcardMatch(taskPath, pattern)))
                destination.Add(new TaskItem(taskPath, isEnabled));
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
                            ?? throw new InvalidOperationException("Schedule.Service COM not available.");

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

    private static string NormalizeTaskPath(string taskPath)
        => taskPath.StartsWith("\\", StringComparison.Ordinal) ? taskPath : "\\" + taskPath;

    private static bool WildcardMatch(string input, string pattern)
    {
        var regex = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".") + "$";

        return System.Text.RegularExpressions.Regex.IsMatch(
            input ?? string.Empty,
            regex,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }
}
