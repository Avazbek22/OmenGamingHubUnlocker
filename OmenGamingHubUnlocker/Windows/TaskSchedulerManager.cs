namespace OmenGamingHubUnlocker.Windows;

public enum ScheduledTaskRuntimeState
{
    Unknown,
    Disabled,
    Queued,
    Ready,
    Running
}

/// <summary>
/// Snapshot of a scheduled task that the unlocker may inspect or modify.
/// </summary>
public sealed record TaskItem(
    string Path,
    bool Enabled,
    string State = "Unknown",
    IReadOnlyList<string>? Actions = null)
{
    public IReadOnlyList<string> ActionPaths => Actions ?? [];
    public ScheduledTaskRuntimeState RuntimeState => ParseRuntimeState(State);
    public bool IsRunning => RuntimeState == ScheduledTaskRuntimeState.Running;
    public bool RequiresStop => RuntimeState is ScheduledTaskRuntimeState.Running or ScheduledTaskRuntimeState.Queued;

    public static ScheduledTaskRuntimeState ParseRuntimeState(string? state)
        => state?.Trim().ToUpperInvariant() switch
        {
            "DISABLED" => ScheduledTaskRuntimeState.Disabled,
            "QUEUED" => ScheduledTaskRuntimeState.Queued,
            "READY" => ScheduledTaskRuntimeState.Ready,
            "RUNNING" => ScheduledTaskRuntimeState.Running,
            _ => ScheduledTaskRuntimeState.Unknown
        };
}

/// <summary>
/// Describes the desired enabled flag for a specific scheduled task.
/// </summary>
public sealed record TaskEnableTarget(
    string Path,
    bool Enabled,
    IReadOnlyList<string>? ExpectedActions = null)
{
    public IReadOnlyList<string> ExpectedActionPaths => ExpectedActions ?? [];
}

/// <summary>
/// Identifies the exact scheduled task whose runtime state may be changed.
/// </summary>
public sealed record TaskRuntimeTarget(
    string Path,
    IReadOnlyList<string>? ExpectedActions = null)
{
    public IReadOnlyList<string> ExpectedActionPaths => ExpectedActions ?? [];
}

/// <summary>
/// Runs Task Scheduler COM discovery out of process and uses bounded schtasks.exe mutations.
/// </summary>
public static class TaskSchedulerManager
{
    private const int DiscoveryTimeoutMilliseconds = 30_000;
    private const int CommandTimeoutMilliseconds = 20_000;

    public static (bool ok, string details) CheckCapability()
        => TryQueryAllTasks(out _, out var error)
            ? (true, Text.Get("manager.taskScheduler.capabilityOk"))
            : (false, error);

    public static List<TaskItem> QueryTasks(string[] patterns)
    {
        if (!TryQueryAllTasks(out var tasks, out var error))
            throw new InvalidOperationException($"Task Scheduler discovery failed: {error}");

        if (patterns.Length == 0)
            return tasks;

        return tasks
            .Where(task =>
                patterns.Any(pattern =>
                    WildcardMatcher.IsMatch(Path.GetFileName(task.Path), pattern) ||
                    WildcardMatcher.IsMatch(task.Path, pattern) ||
                    task.ActionPaths.Any(action => WildcardMatcher.IsMatch(action, pattern))) &&
                OmenIdentity.IsLikelyOmenReference([task.Path, .. task.ActionPaths]))
            .ToList();
    }

    public static List<OperationLine> ApplyEnabledTargets(
        IEnumerable<TaskEnableTarget> targets,
        bool dryRun)
    {
        var requestedTargets = targets
            .Where(target => !string.IsNullOrWhiteSpace(target.Path))
            .Select(target => target with { Path = NormalizeTaskPath(target.Path) })
            .DistinctBy(target => target.Path, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(target => target.Path, StringComparer.OrdinalIgnoreCase);

        if (requestedTargets.Count == 0)
            return [LocalizedLine.Info("manager.tasks.nothingToChange")];

        var currentTasks = QueryTasks([])
            .ToDictionary(task => NormalizeTaskPath(task.Path), StringComparer.OrdinalIgnoreCase);
        var lines = new List<OperationLine>();

        foreach (var (taskPath, target) in requestedTargets.OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (!currentTasks.TryGetValue(taskPath, out var currentTask))
            {
                lines.Add(LocalizedLine.Warn("manager.tasks.notFound", taskPath));
                continue;
            }

            if (!TargetIdentityMatcher.TaskMatches(currentTask, target.ExpectedActions))
            {
                lines.Add(LocalizedLine.Err("manager.tasks.identityChanged", taskPath));
                continue;
            }

            if (currentTask.Enabled == target.Enabled)
            {
                lines.Add(LocalizedLine.Info(
                    "manager.tasks.alreadyState",
                    currentTask.Path,
                    target.Enabled ? Text.Get("state.enabled") : Text.Get("state.disabled")));
                continue;
            }

            if (dryRun)
            {
                lines.Add(LocalizedLine.Ok(
                    "manager.tasks.wouldSetState",
                    target.Enabled ? Text.Get("state.enabled") : Text.Get("state.disabled"),
                    currentTask.Path));
                continue;
            }

            var stateFlag = target.Enabled ? "/ENABLE" : "/DISABLE";
            if (TryRunSchtasks(["/Change", "/TN", currentTask.Path, stateFlag], out var error))
            {
                lines.Add(LocalizedLine.Ok(
                    "manager.tasks.setState",
                    target.Enabled ? Text.Get("state.enabled") : Text.Get("state.disabled"),
                    currentTask.Path));
                continue;
            }

            lines.Add(TaskReachedEnabledState(target)
                ? LocalizedLine.Info(
                    "manager.tasks.alreadyState",
                    currentTask.Path,
                    target.Enabled ? Text.Get("state.enabled") : Text.Get("state.disabled"))
                : LocalizedLine.Err("manager.tasks.commandFailed", currentTask.Path, error));
        }

        return lines;
    }

    public static List<OperationLine> StopTasks(
        IEnumerable<TaskRuntimeTarget> targets,
        bool dryRun)
        => ChangeTaskRuntimeState(targets, desiredRunning: false, dryRun);

    public static List<OperationLine> StartTasks(
        IEnumerable<TaskRuntimeTarget> targets,
        bool dryRun)
        => ChangeTaskRuntimeState(targets, desiredRunning: true, dryRun);

    internal static List<TaskItem> DeserializeTasks(string json)
    {
        using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "[]" : json);
        var elements = document.RootElement.ValueKind switch
        {
            JsonValueKind.Array => document.RootElement.EnumerateArray().ToList(),
            JsonValueKind.Object => [document.RootElement],
            _ => []
        };

        return elements.Select(element => new TaskItem(
            NormalizeTaskPath(ReadString(element, "Path")),
            ReadBoolean(element, "Enabled"),
            MapTaskState(ReadInteger(element, "State")),
            ReadStringArray(element, "Actions"))).ToList();
    }

    private static List<OperationLine> ChangeTaskRuntimeState(
        IEnumerable<TaskRuntimeTarget> targets,
        bool desiredRunning,
        bool dryRun)
    {
        var requestedTargets = targets
            .Where(target => !string.IsNullOrWhiteSpace(target.Path))
            .Select(target => target with { Path = NormalizeTaskPath(target.Path) })
            .DistinctBy(target => target.Path, StringComparer.OrdinalIgnoreCase)
            .OrderBy(target => target.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (requestedTargets.Count == 0)
        {
            return
            [
                LocalizedLine.Info(desiredRunning
                    ? "manager.tasks.nothingToStart"
                    : "manager.tasks.nothingToStop")
            ];
        }

        var currentTasks = QueryTasks([])
            .ToDictionary(task => NormalizeTaskPath(task.Path), StringComparer.OrdinalIgnoreCase);
        var lines = new List<OperationLine>();

        foreach (var target in requestedTargets)
        {
            if (!currentTasks.TryGetValue(target.Path, out var currentTask))
            {
                lines.Add(LocalizedLine.Warn("manager.tasks.notFound", target.Path));
                continue;
            }

            if (!TargetIdentityMatcher.TaskMatches(currentTask, target.ExpectedActions))
            {
                lines.Add(LocalizedLine.Err("manager.tasks.identityChanged", target.Path));
                continue;
            }

            var alreadyInDesiredState = desiredRunning
                ? currentTask.IsRunning
                : !currentTask.RequiresStop;
            if (alreadyInDesiredState)
            {
                lines.Add(LocalizedLine.Info(
                    desiredRunning ? "manager.tasks.alreadyRunning" : "manager.tasks.alreadyStopped",
                    target.Path));
                continue;
            }

            if (dryRun)
            {
                lines.Add(LocalizedLine.Ok(
                    desiredRunning ? "manager.tasks.wouldStart" : "manager.tasks.wouldStop",
                    target.Path));
                continue;
            }

            var command = desiredRunning ? "/Run" : "/End";
            if (TryRunSchtasks([command, "/TN", target.Path], out var error))
            {
                lines.Add(LocalizedLine.Ok(
                    desiredRunning ? "manager.tasks.started" : "manager.tasks.stopped",
                    target.Path));
                continue;
            }

            lines.Add(TaskReachedRuntimeState(target, desiredRunning)
                ? LocalizedLine.Info(
                    desiredRunning ? "manager.tasks.alreadyRunning" : "manager.tasks.alreadyStopped",
                    target.Path)
                : LocalizedLine.Err(
                    desiredRunning ? "manager.tasks.failedToStartCommand" : "manager.tasks.failedToStopCommand",
                    target.Path,
                    error));
        }

        return lines;
    }

    private static bool TryQueryAllTasks(out List<TaskItem> tasks, out string error)
    {
        const string script = """
$ErrorActionPreference = 'Stop'
$script:tasks = [System.Collections.Generic.List[object]]::new()

function Read-Folder([object]$folder) {
    $registeredTasks = $folder.GetTasks(1)
    for ($taskIndex = 1; $taskIndex -le $registeredTasks.Count; $taskIndex++) {
        $task = $registeredTasks.Item($taskIndex)
        $actions = [System.Collections.Generic.List[string]]::new()

        try {
            $definitionActions = $task.Definition.Actions
            for ($actionIndex = 1; $actionIndex -le $definitionActions.Count; $actionIndex++) {
                $action = $definitionActions.Item($actionIndex)
                try {
                    $path = [string]$action.Path
                    if (-not [string]::IsNullOrWhiteSpace($path)) {
                        $actions.Add($path)
                    }
                } catch {
                    # COM-handler and message actions do not expose an executable path.
                }
            }
        } catch {
            # A task without readable actions can still be identified by its registered path.
        }

        $script:tasks.Add([PSCustomObject]@{
            Path = [string]$task.Path
            Enabled = [bool]$task.Enabled
            State = [int]$task.State
            Actions = [string[]]$actions.ToArray()
        })
    }

    $subFolders = $folder.GetFolders(0)
    for ($folderIndex = 1; $folderIndex -le $subFolders.Count; $folderIndex++) {
        Read-Folder $subFolders.Item($folderIndex)
    }
}

$scheduler = New-Object -ComObject 'Schedule.Service'
$scheduler.Connect()
Read-Folder $scheduler.GetFolder('\')
ConvertTo-Json -InputObject $script:tasks.ToArray() -Compress -Depth 4
""";

        if (!PowerShellRunner.TryRunScript(
                script,
                out var output,
                out error,
                DiscoveryTimeoutMilliseconds))
        {
            tasks = [];
            return false;
        }

        try
        {
            tasks = DeserializeTasks(output)
                .DistinctBy(task => task.Path, StringComparer.OrdinalIgnoreCase)
                .ToList();
            error = string.Empty;
            return true;
        }
        catch (Exception exception)
        {
            tasks = [];
            error = exception.Message;
            return false;
        }
    }

    private static bool TryRunSchtasks(IEnumerable<string> arguments, out string error)
        => PowerShellRunner.TryRun(
            WindowsPaths.GetSystemExecutable("schtasks.exe"),
            arguments,
            out _,
            out error,
            CommandTimeoutMilliseconds);

    private static bool TaskReachedEnabledState(TaskEnableTarget target)
        => TryQueryAllTasks(out var tasks, out _) &&
           tasks.FirstOrDefault(task =>
               task.Path.Equals(target.Path, StringComparison.OrdinalIgnoreCase)) is { } task &&
           TargetIdentityMatcher.TaskMatches(task, target.ExpectedActions) &&
           task.Enabled == target.Enabled;

    private static bool TaskReachedRuntimeState(TaskRuntimeTarget target, bool desiredRunning)
        => TryQueryAllTasks(out var tasks, out _) &&
           tasks.FirstOrDefault(task =>
               task.Path.Equals(target.Path, StringComparison.OrdinalIgnoreCase)) is { } task &&
           TargetIdentityMatcher.TaskMatches(task, target.ExpectedActions) &&
           (desiredRunning ? task.IsRunning : !task.RequiresStop);

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

    private static string ReadString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var property)
            ? property.GetString() ?? string.Empty
            : string.Empty;

    private static bool ReadBoolean(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var property) &&
           property.ValueKind is JsonValueKind.True or JsonValueKind.False &&
           property.GetBoolean();

    private static int ReadInteger(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var property) && property.TryGetInt32(out var value)
            ? value
            : 0;

    private static List<string> ReadStringArray(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
            return [];

        if (property.ValueKind == JsonValueKind.String)
        {
            var value = property.GetString();
            return string.IsNullOrWhiteSpace(value) ? [] : [value];
        }

        if (property.ValueKind != JsonValueKind.Array)
            return [];

        return property.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString() ?? string.Empty)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToList();
    }
}
