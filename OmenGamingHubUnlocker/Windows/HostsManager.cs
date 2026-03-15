using System.Text.RegularExpressions;

namespace OmenGamingHubUnlocker.Windows;

/// <summary>
/// Represents the block state of a single hosts entry.
/// </summary>
public sealed record HostsDomainState(string Domain, bool Blocked);

/// <summary>
/// Encapsulates hosts-file inspection and mutation.
/// </summary>
public static class HostsManager
{
    public static (bool ok, string details) CheckWriteAccess(string marker)
    {
        _ = marker;

        try
        {
            var hostsFilePath = WindowsPaths.HostsPath;
            if (!File.Exists(hostsFilePath))
                return (false, Text.Format("manager.hosts.fileNotFound", hostsFilePath));

            // Opening the file for read/write without touching the contents is enough to validate permissions.
            using var stream = new FileStream(hostsFilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
            return (true, Text.Get("manager.hosts.accessibleForReadWrite"));
        }
        catch (Exception exception)
        {
            return (false, exception.Message);
        }
    }

    public static List<HostsDomainState> GetDomainsStatus(IEnumerable<string> domains, string marker)
    {
        _ = marker;

        var domainStates = new List<HostsDomainState>();
        var hostsLines = TryReadHostsFile();

        foreach (var domain in domains)
        {
            var isBlocked = hostsLines.Any(line => IsDomainLine(line, domain));
            domainStates.Add(new HostsDomainState(domain, isBlocked));
        }

        return domainStates;
    }

    public static List<OperationLine> ActivateHostsBlock(string[] domains, string marker, bool dryRun)
    {
        var operationLines = new List<OperationLine>();
        var hostsFilePath = WindowsPaths.HostsPath;

        if (!File.Exists(hostsFilePath))
        {
            operationLines.Add(LocalizedLine.Warn("manager.hosts.fileNotFound", hostsFilePath));
            return operationLines;
        }

        string[] existingLines;
        try
        {
            existingLines = File.ReadAllLines(hostsFilePath);
        }
        catch (Exception exception)
        {
            operationLines.Add(LocalizedLine.Err("manager.hosts.cannotRead", exception.Message));
            return operationLines;
        }

        var linesToAppend = new List<string>();
        foreach (var domain in domains)
        {
            if (existingLines.Any(line => IsDomainLine(line, domain)))
                continue;

            linesToAppend.Add($"127.0.0.1\t{domain}\t{marker}");
        }

        if (linesToAppend.Count == 0)
        {
            operationLines.Add(LocalizedLine.Info("manager.hosts.nothingToAdd"));
            return operationLines;
        }

        foreach (var lineToAppend in linesToAppend)
        {
            if (dryRun)
            {
                operationLines.Add(LocalizedLine.Ok("manager.hosts.wouldAdd", lineToAppend));
                continue;
            }

            var appendSucceeded = TryAppendLineWithRetry(hostsFilePath, lineToAppend, out var appendError);
            operationLines.Add(appendSucceeded
                ? LocalizedLine.Ok("manager.hosts.added", lineToAppend)
                : LocalizedLine.Warn("manager.hosts.failedToAdd", appendError));
        }

        return operationLines;
    }

    public static List<OperationLine> DisableHostsBlock(string marker, bool dryRun)
    {
        var operationLines = new List<OperationLine>();
        var hostsFilePath = WindowsPaths.HostsPath;

        if (!File.Exists(hostsFilePath))
        {
            operationLines.Add(LocalizedLine.Warn("manager.hosts.fileNotFound", hostsFilePath));
            return operationLines;
        }

        string[] existingLines;
        try
        {
            existingLines = File.ReadAllLines(hostsFilePath);
        }
        catch (Exception exception)
        {
            operationLines.Add(LocalizedLine.Err("manager.hosts.cannotRead", exception.Message));
            return operationLines;
        }

        var remainingLines = existingLines
            .Where(line => !line.Contains(marker, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var removedLineCount = existingLines.Length - remainingLines.Length;

        if (removedLineCount <= 0)
        {
            operationLines.Add(LocalizedLine.Info("manager.hosts.noMarkerLines"));
            return operationLines;
        }

        if (dryRun)
        {
            operationLines.Add(LocalizedLine.Ok("manager.hosts.wouldRemove", removedLineCount, marker));
            return operationLines;
        }

        try
        {
            File.WriteAllLines(hostsFilePath, remainingLines, Encoding.ASCII);
            operationLines.Add(LocalizedLine.Ok("manager.hosts.removed", removedLineCount));
        }
        catch (Exception exception)
        {
            operationLines.Add(LocalizedLine.Err("manager.hosts.failedToWrite", exception.Message));
        }

        return operationLines;
    }

    private static string[] TryReadHostsFile()
    {
        try
        {
            return File.Exists(WindowsPaths.HostsPath)
                ? File.ReadAllLines(WindowsPaths.HostsPath)
                : Array.Empty<string>();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static bool IsDomainLine(string line, string domain)
    {
        var escapedDomain = Regex.Escape(domain);
        var matcher = new Regex(
            @"^\s*\d{1,3}(\.\d{1,3}){3}\s+" + escapedDomain + @"(\s|$)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        return matcher.IsMatch(line ?? string.Empty);
    }

    private static bool TryAppendLineWithRetry(string path, string line, out string error)
    {
        const int maxRetries = 3;
        const int retryDelayMs = 250;

        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                File.AppendAllText(path, Environment.NewLine + line, Encoding.ASCII);
                error = string.Empty;
                return true;
            }
            catch (IOException exception)
            {
                if (attempt < maxRetries)
                {
                    Thread.Sleep(retryDelayMs);
                    continue;
                }

                error = exception.Message;
                return false;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }

        error = "Unknown error";
        return false;
    }
}
