using OmenGamingHubUnlocker.Core;

namespace OmenGamingHubUnlocker.Windows;

public sealed record TaskItem(string Path, bool Enabled);
public sealed record TaskEnableTarget(string Path, bool Enabled);

public static class TaskSchedulerManager
{
    public static (bool ok, string details) CheckCapability()
    {
        try
        {
            var t = Type.GetTypeFromProgID("Schedule.Service");
            if (t is null)
                return (false, "Schedule.Service COM not available.");

            dynamic ts = Activator.CreateInstance(t)!;
            ts.Connect();
            return (true, "COM connect ok.");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public static List<TaskItem> QueryTasks(string[] patterns)
    {
        var list = new List<TaskItem>();
        var matchAll = patterns.Length == 0;

        try
        {
            var tsType = Type.GetTypeFromProgID("Schedule.Service");
            if (tsType is null)
                return list;

            dynamic ts = Activator.CreateInstance(tsType)!;
            ts.Connect();

            dynamic root = ts.GetFolder("\\");
            EnumerateFolder(root, patterns, list, matchAll);
        }
        catch
        {
            // Keep non-fatal.
        }

        return list
            .DistinctBy(x => x.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static List<OperationLine> ApplyEnabledTargets(IEnumerable<TaskEnableTarget> targets, bool dryRun)
    {
        var targetMap = targets
            .DistinctBy(x => x.Path, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => NormalizePath(x.Path), x => x.Enabled, StringComparer.OrdinalIgnoreCase);

        if (targetMap.Count == 0)
        {
            return
            [
                new OperationLine { Level = "INFO", Text = "Tasks: nothing to change." }
            ];
        }

        var currentTasks = QueryTasks(Array.Empty<string>())
            .ToDictionary(x => NormalizePath(x.Path), x => x, StringComparer.OrdinalIgnoreCase);

        var lines = new List<OperationLine>();

        foreach (var (normalizedPath, desiredEnabled) in targetMap.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (!currentTasks.TryGetValue(normalizedPath, out var task))
            {
                lines.Add(new OperationLine { Level = "WARN", Text = $"Tasks: {normalizedPath} was not found." });
                continue;
            }

            if (task.Enabled == desiredEnabled)
            {
                lines.Add(new OperationLine
                {
                    Level = "INFO",
                    Text = $"Tasks: {task.Path} already {(desiredEnabled ? "enabled" : "disabled")}."
                });
                continue;
            }

            if (dryRun)
            {
                lines.Add(new OperationLine
                {
                    Level = "OK",
                    Text = $"Tasks: would set {(desiredEnabled ? "Enabled" : "Disabled")} -> {task.Path}"
                });
                continue;
            }

            try
            {
                SetEnabledViaCom(task.Path, desiredEnabled);
                lines.Add(new OperationLine
                {
                    Level = "OK",
                    Text = $"Tasks: set {(desiredEnabled ? "Enabled" : "Disabled")} -> {task.Path}"
                });
            }
            catch (Exception ex)
            {
                var flag = desiredEnabled ? "/ENABLE" : "/DISABLE";
                var ok = PowerShellRunner.TryRun("schtasks.exe", $"/Change /TN \"{task.Path}\" {flag}", out _, out var err, 20_000);

                lines.Add(new OperationLine
                {
                    Level = ok ? "WARN" : "ERR",
                    Text = ok
                        ? $"Tasks: COM failed for {task.Path}, schtasks.exe fallback applied."
                        : $"Tasks: failed for {task.Path}. COM error: {ex.Message}. schtasks.exe error: {err}"
                });
            }
        }

        return lines;
    }

    private static void EnumerateFolder(dynamic folder, string[] patterns, List<TaskItem> list, bool matchAll)
    {
        dynamic tasks = folder.GetTasks(1);
        int count = tasks.Count;

        for (var i = 1; i <= count; i++)
        {
            dynamic task = tasks.Item(i);
            string name = task.Name;
            string path = task.Path;
            bool enabled = task.Enabled;

            if (matchAll || patterns.Any(p => WildMatch(name, p) || WildMatch(path, p)))
                list.Add(new TaskItem(path, enabled));
        }

        dynamic subs = folder.GetFolders(0);
        int subCount = subs.Count;

        for (var i = 1; i <= subCount; i++)
        {
            dynamic sub = subs.Item(i);
            EnumerateFolder(sub, patterns, list, matchAll);
        }
    }

    private static void SetEnabledViaCom(string taskPath, bool enabled)
    {
        var tsType = Type.GetTypeFromProgID("Schedule.Service")
                     ?? throw new InvalidOperationException("Schedule.Service COM not available.");

        dynamic ts = Activator.CreateInstance(tsType)!;
        ts.Connect();

        var normalized = NormalizePath(taskPath);

        var lastSlash = normalized.LastIndexOf('\\');
        var folderPath = lastSlash <= 0 ? "\\" : normalized[..lastSlash];
        var taskName = normalized[(lastSlash + 1)..];

        dynamic folder = ts.GetFolder(folderPath);
        dynamic task = folder.GetTask(taskName);
        task.Enabled = enabled;
    }

    private static string NormalizePath(string taskPath)
        => taskPath.StartsWith("\\", StringComparison.Ordinal) ? taskPath : "\\" + taskPath;

    private static bool WildMatch(string input, string pattern)
    {
        var regex = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".") + "$";

        return System.Text.RegularExpressions.Regex.IsMatch(input ?? string.Empty, regex, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }
}
