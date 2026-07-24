namespace OmenGamingHubUnlocker.Windows;

/// <summary>
/// Immutable process metadata used after native process handles have been released.
/// </summary>
public sealed record ProcessItem(int Id, string Name, string ExecutablePath)
{
    public string Label => $"{Name} (PID {Id})";
}

/// <summary>
/// Provides process discovery and termination helpers for OMEN-related executables.
/// </summary>
public static class ProcessManager
{
    public static List<ProcessItem> QueryTargetProcesses(
        IEnumerable<string> namePatterns,
        IEnumerable<string> trustedExecutablePaths)
    {
        var normalizedPaths = trustedExecutablePaths
            .Select(TryNormalizePath)
            .Where(path => path is not null)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var targetProcesses = new List<ProcessItem>();
        var currentProcessId = Environment.ProcessId;

        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (process.Id == currentProcessId)
                        continue;

                    var executablePath = TryGetExecutablePath(process);
                    var normalizedExecutablePath = TryNormalizePath(executablePath);
                    var matchesKnownName = namePatterns.Any(pattern => WildcardMatcher.IsMatch(process.ProcessName, pattern));
                    var matchesDiscoveredPath = normalizedExecutablePath is not null && normalizedPaths.Contains(normalizedExecutablePath);

                    if (matchesKnownName || matchesDiscoveredPath)
                        targetProcesses.Add(new ProcessItem(process.Id, process.ProcessName, executablePath));
                }
                catch
                {
                    // A process can exit between enumeration and inspection.
                }
            }
        }

        return targetProcesses
            .DistinctBy(process => process.Id)
            .OrderBy(process => process.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(process => process.Id)
            .ToList();
    }

    public static List<OperationLine> TerminateTargetProcesses(
        IEnumerable<string> namePatterns,
        IEnumerable<string> trustedExecutablePaths,
        bool dryRun)
    {
        var lines = new List<OperationLine>();
        var targetProcesses = QueryTargetProcesses(namePatterns, trustedExecutablePaths);

        if (targetProcesses.Count == 0)
        {
            lines.Add(LocalizedLine.Info("manager.processes.noneRunning"));
            return lines;
        }

        foreach (var target in targetProcesses)
        {
            if (dryRun)
            {
                lines.Add(LocalizedLine.Ok("manager.processes.wouldTerminate", target.Label));
                continue;
            }

            try
            {
                using var process = Process.GetProcessById(target.Id);
                process.Kill(entireProcessTree: true);

                if (!process.WaitForExit(10_000))
                {
                    lines.Add(LocalizedLine.Err("manager.processes.didNotExit", target.Label));
                    continue;
                }

                lines.Add(LocalizedLine.Ok("manager.processes.terminated", target.Label));
            }
            catch (ArgumentException)
            {
                // Exiting before termination is already the desired state.
                lines.Add(LocalizedLine.Ok("manager.processes.alreadyExited", target.Label));
            }
            catch (Exception exception)
            {
                lines.Add(LocalizedLine.Err("manager.processes.failedToTerminate", target.Label, exception.Message));
            }
        }

        return lines;
    }

    private static string TryGetExecutablePath(Process process)
    {
        try
        {
            return process.MainModule?.FileName ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string? TryNormalizePath(string? path)
    {
        try
        {
            return string.IsNullOrWhiteSpace(path) ? null : Path.GetFullPath(path);
        }
        catch
        {
            return null;
        }
    }
}
