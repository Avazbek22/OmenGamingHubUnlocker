using OmenGamingHubUnlocker.Core;

namespace OmenGamingHubUnlocker.Windows;

public sealed record TaskItem(string Path, bool Enabled);

public static class TaskSchedulerManager
{
    public static (bool ok, string details) CheckCapability()
    {
        try
        {
            var t = Type.GetTypeFromProgID("Schedule.Service");
            if (t is null) return (false, "Schedule.Service COM not available.");

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

        try
        {
            var tsType = Type.GetTypeFromProgID("Schedule.Service");
            if (tsType is null) return list;

            dynamic ts = Activator.CreateInstance(tsType)!;
            ts.Connect();

            dynamic root = ts.GetFolder("\\");
            EnumerateFolder(root, patterns, list);
        }
        catch
        {
            // keep non-fatal
        }

        return list;
    }

    public static List<OperationLine> SetTasksEnabled(string[] patterns, bool enabled, bool dryRun)
    {
        var lines = new List<OperationLine>();
        var tasks = QueryTasks(patterns);

        if (tasks.Count == 0)
        {
            lines.Add(new OperationLine { Level = "INFO", Text = "Tasks: no matching tasks found." });
            return lines;
        }

        foreach (var t in tasks.OrderBy(x => x.Path))
        {
            if (t.Enabled == enabled)
                continue;

            if (dryRun)
            {
                lines.Add(new OperationLine
                {
                    Level = "OK",
                    Text = $"Tasks: would set {(enabled ? "Enabled" : "Disabled")} -> {t.Path}"
                });
                continue;
            }

            // Primary: COM
            try
            {
                SetEnabledViaCom(t.Path, enabled);
                lines.Add(new OperationLine { Level = "OK", Text = $"Tasks: set {(enabled ? "Enabled" : "Disabled")} -> {t.Path}" });
            }
            catch (Exception ex)
            {
                // Fallback: schtasks.exe
                var flag = enabled ? "/ENABLE" : "/DISABLE";
                var ok = PowerShellRunner.TryRun("schtasks.exe", $"/Change /TN \"{t.Path}\" {flag}", out _, out var err);

                lines.Add(new OperationLine
                {
                    Level = ok ? "WARN" : "ERR",
                    Text = ok
                        ? $"Tasks: COM failed for {t.Path}, schtasks.exe fallback applied."
                        : $"Tasks: failed for {t.Path}. COM error: {ex.Message}. schtasks.exe error: {err}"
                });
            }
        }

        return lines;
    }

    private static void EnumerateFolder(dynamic folder, string[] patterns, List<TaskItem> list)
    {
        dynamic tasks = folder.GetTasks(1);
        int count = tasks.Count;

        for (int i = 1; i <= count; i++)
        {
            dynamic task = tasks.Item(i);
            string name = task.Name;
            string path = task.Path;
            bool enabled = task.Enabled;

            if (patterns.Any(p => WildMatch(name, p) || WildMatch(path, p)))
                list.Add(new TaskItem(path, enabled));
        }

        dynamic subs = folder.GetFolders(0);
        int subCount = subs.Count;

        for (int i = 1; i <= subCount; i++)
        {
            dynamic sub = subs.Item(i);
            EnumerateFolder(sub, patterns, list);
        }
    }

    private static void SetEnabledViaCom(string taskPath, bool enabled)
    {
        var tsType = Type.GetTypeFromProgID("Schedule.Service")
                     ?? throw new InvalidOperationException("Schedule.Service COM not available.");

        dynamic ts = Activator.CreateInstance(tsType)!;
        ts.Connect();

        var normalized = taskPath.StartsWith("\\") ? taskPath : "\\" + taskPath;

        var lastSlash = normalized.LastIndexOf('\\');
        var folderPath = lastSlash <= 0 ? "\\" : normalized[..lastSlash];
        var taskName = normalized[(lastSlash + 1)..];

        dynamic folder = ts.GetFolder(folderPath);
        dynamic task = folder.GetTask(taskName);
        task.Enabled = enabled;
    }

    private static bool WildMatch(string input, string pattern)
    {
        var regex = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".") + "$";

        return System.Text.RegularExpressions.Regex.IsMatch(input ?? "", regex, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }
}
