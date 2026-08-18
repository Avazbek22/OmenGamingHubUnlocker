namespace OmenGamingHubUnlocker.Windows;

/// <summary>
/// Immutable process metadata used after native process handles have been released.
/// </summary>
public sealed record ProcessItem(
    int Id,
    string Name,
    string ExecutablePath,
    DateTime? StartTimeUtc = null)
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
        IEnumerable<string> trustedExecutablePaths,
        bool requireTrustedOmenIdentity = false)
    {
        var patterns = namePatterns.ToArray();
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
                    var matchesKnownName = patterns.Any(pattern => WildcardMatcher.IsMatch(process.ProcessName, pattern));
                    var matchesDiscoveredPath = normalizedExecutablePath is not null && normalizedPaths.Contains(normalizedExecutablePath);
                    var trustedNameMatch = matchesKnownName &&
                                           (!requireTrustedOmenIdentity ||
                                            OmenExecutableTrust.IsTrustedOmenExecutable(
                                                executablePath,
                                                process.ProcessName));

                    if (trustedNameMatch || matchesDiscoveredPath)
                    {
                        targetProcesses.Add(new ProcessItem(
                            process.Id,
                            process.ProcessName,
                            executablePath,
                            TryGetStartTimeUtc(process)));
                    }
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
        bool dryRun,
        bool requireTrustedOmenIdentity = false)
    {
        var lines = new List<OperationLine>();
        var targetProcesses = QueryTargetProcesses(
            namePatterns,
            trustedExecutablePaths,
            requireTrustedOmenIdentity);

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
                if (!MatchesSnapshot(process, target))
                {
                    lines.Add(LocalizedLine.Warn("manager.processes.identityChanged", target.Label));
                    continue;
                }

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

    private static DateTime? TryGetStartTimeUtc(Process process)
    {
        try
        {
            return process.StartTime.ToUniversalTime();
        }
        catch
        {
            return null;
        }
    }

    private static bool MatchesSnapshot(Process process, ProcessItem target)
    {
        try
        {
            if (!process.ProcessName.Equals(target.Name, StringComparison.OrdinalIgnoreCase))
                return false;

            var currentStartTime = TryGetStartTimeUtc(process);
            if (target.StartTimeUtc.HasValue && currentStartTime != target.StartTimeUtc)
                return false;

            var expectedPath = TryNormalizePath(target.ExecutablePath);
            return expectedPath is null ||
                   string.Equals(
                       TryNormalizePath(TryGetExecutablePath(process)),
                       expectedPath,
                       StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
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
