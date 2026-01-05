using System.Diagnostics;

namespace OmenGamingHubUnlocker.Windows;

public static class ProcessManager
{
    public static List<Process> FindMatchingProcesses(string[] patterns)
    {
        var list = new List<Process>();
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                var name = p.ProcessName;
                if (patterns.Any(pt => WildcardMatch(name, pt)))
                    list.Add(p);
            }
            catch { }
        }

        return list
            .GroupBy(p => p.Id)
            .Select(g => g.First())
            .OrderBy(p => p.ProcessName)
            .ToList();
    }

    public static List<string> TryKillMatchingProcesses(string[] patterns, bool dryRun)
    {
        var killed = new List<string>();
        var procs = FindMatchingProcesses(patterns);

        foreach (var p in procs)
        {
            var label = $"{p.ProcessName} (PID {p.Id})";
            if (dryRun)
            {
                killed.Add(label);
                continue;
            }

            try
            {
                p.Kill(entireProcessTree: true);
                killed.Add(label);
            }
            catch
            {
                // aggressive flow should not crash
            }
        }

        return killed;
    }

    private static bool WildcardMatch(string input, string pattern)
    {
        input ??= "";
        pattern ??= "";

        var regex = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".") + "$";

        return System.Text.RegularExpressions.Regex.IsMatch(input, regex, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }
}