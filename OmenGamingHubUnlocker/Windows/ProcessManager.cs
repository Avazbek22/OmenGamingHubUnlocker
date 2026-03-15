using System.Diagnostics;

namespace OmenGamingHubUnlocker.Windows;

/// <summary>
/// Provides process discovery and termination helpers for OMEN-related executables.
/// </summary>
public static class ProcessManager
{
    public static List<Process> FindMatchingProcesses(string[] patterns)
    {
        var matchingProcesses = new List<Process>();
        var currentProcessId = Environment.ProcessId;

        foreach (var process in Process.GetProcesses())
        {
            try
            {
                // Never report or terminate the unlocker process itself even if its name matches an OMEN pattern.
                if (process.Id == currentProcessId)
                    continue;

                if (patterns.Any(pattern => WildcardMatch(process.ProcessName, pattern)))
                    matchingProcesses.Add(process);
            }
            catch
            {
                // Some system processes deny metadata access; ignoring them keeps discovery best-effort.
            }
        }

        return matchingProcesses
            .GroupBy(process => process.Id)
            .Select(group => group.First())
            .OrderBy(process => process.ProcessName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static List<string> TryKillMatchingProcesses(string[] patterns, bool dryRun)
    {
        var affectedProcesses = new List<string>();
        var matchingProcesses = FindMatchingProcesses(patterns);

        foreach (var process in matchingProcesses)
        {
            var processLabel = $"{process.ProcessName} (PID {process.Id})";
            if (dryRun)
            {
                affectedProcesses.Add(processLabel);
                continue;
            }

            try
            {
                process.Kill(entireProcessTree: true);
                affectedProcesses.Add(processLabel);
            }
            catch
            {
                // Process termination is best-effort; the engine reports counts, not per-process failures.
            }
        }

        return affectedProcesses;
    }

    private static bool WildcardMatch(string input, string pattern)
    {
        var safeInput = input ?? string.Empty;
        var safePattern = pattern ?? string.Empty;

        var regex = "^" + System.Text.RegularExpressions.Regex.Escape(safePattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".") + "$";

        return System.Text.RegularExpressions.Regex.IsMatch(
            safeInput,
            regex,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }
}
