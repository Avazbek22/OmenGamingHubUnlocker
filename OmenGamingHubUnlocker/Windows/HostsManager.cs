using System.Text;
using System.Text.RegularExpressions;
using OmenGamingHubUnlocker.Core;

namespace OmenGamingHubUnlocker.Windows;

public sealed record HostsDomainState(string Domain, bool Blocked);

public static class HostsManager
{
    public static (bool ok, string details) CheckWriteAccess(string marker)
    {
        try
        {
            var path = WindowsPaths.HostsPath;
            if (!File.Exists(path))
                return (false, $"hosts file not found: {path}");

            // try open for read/write (no changes)
            using var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
            return (true, "hosts file is accessible for read/write.");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public static List<HostsDomainState> GetDomainsStatus(IEnumerable<string> domains, string marker)
    {
        var list = new List<HostsDomainState>();

        string[] lines;
        try
        {
            lines = File.Exists(WindowsPaths.HostsPath) ? File.ReadAllLines(WindowsPaths.HostsPath) : Array.Empty<string>();
        }
        catch
        {
            lines = Array.Empty<string>();
        }

        foreach (var d in domains)
        {
            var blocked = lines.Any(l => IsDomainLine(l, d));
            list.Add(new HostsDomainState(d, blocked));
        }

        return list;
    }

    public static List<OperationLine> ActivateHostsBlock(string[] domains, string marker, bool dryRun)
    {
        var lines = new List<OperationLine>();
        var hostsPath = WindowsPaths.HostsPath;

        if (!File.Exists(hostsPath))
        {
            lines.Add(new OperationLine { Level = "WARN", Text = $"hosts: file not found: {hostsPath}" });
            return lines;
        }

        string[] content;
        try { content = File.ReadAllLines(hostsPath); }
        catch (Exception ex)
        {
            lines.Add(new OperationLine { Level = "ERR", Text = $"hosts: cannot read: {ex.Message}" });
            return lines;
        }

        var toAdd = new List<string>();
        foreach (var d in domains)
        {
            if (content.Any(l => IsDomainLine(l, d)))
                continue;

            toAdd.Add($"127.0.0.1\t{d}\t{marker}");
        }

        if (toAdd.Count == 0)
        {
            lines.Add(new OperationLine { Level = "INFO", Text = "hosts: nothing to add (already present)." });
            return lines;
        }

        foreach (var l in toAdd)
        {
            if (dryRun)
            {
                lines.Add(new OperationLine { Level = "OK", Text = $"hosts: would add: {l}" });
                continue;
            }

            var ok = TryAppendLineWithRetry(hostsPath, l, out var err);
            lines.Add(new OperationLine
            {
                Level = ok ? "OK" : "WARN",
                Text = ok ? $"hosts: added: {l}" : $"hosts: failed to add line: {err}"
            });
        }

        return lines;
    }

    public static List<OperationLine> DisableHostsBlock(string marker, bool dryRun)
    {
        var lines = new List<OperationLine>();
        var hostsPath = WindowsPaths.HostsPath;

        if (!File.Exists(hostsPath))
        {
            lines.Add(new OperationLine { Level = "WARN", Text = $"hosts: file not found: {hostsPath}" });
            return lines;
        }

        string[] content;
        try { content = File.ReadAllLines(hostsPath); }
        catch (Exception ex)
        {
            lines.Add(new OperationLine { Level = "ERR", Text = $"hosts: cannot read: {ex.Message}" });
            return lines;
        }

        var filtered = content.Where(l => !l.Contains(marker, StringComparison.OrdinalIgnoreCase)).ToArray();
        var removedCount = content.Length - filtered.Length;

        if (removedCount <= 0)
        {
            lines.Add(new OperationLine { Level = "INFO", Text = "hosts: no marker lines found to remove." });
            return lines;
        }

        if (dryRun)
        {
            lines.Add(new OperationLine { Level = "OK", Text = $"hosts: would remove {removedCount} line(s) with marker '{marker}'." });
            return lines;
        }

        try
        {
            File.WriteAllLines(hostsPath, filtered, Encoding.ASCII);
            lines.Add(new OperationLine { Level = "OK", Text = $"hosts: removed {removedCount} line(s)." });
        }
        catch (Exception ex)
        {
            lines.Add(new OperationLine { Level = "ERR", Text = $"hosts: failed to write: {ex.Message}" });
        }

        return lines;
    }

    private static bool IsDomainLine(string line, string domain)
    {
        var escaped = Regex.Escape(domain);
        var rx = new Regex(@"^\s*\d{1,3}(\.\d{1,3}){3}\s+" + escaped + @"(\s|$)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return rx.IsMatch(line ?? "");
    }

    private static bool TryAppendLineWithRetry(string path, string line, out string error)
    {
        const int maxRetries = 3;
        const int delayMs = 250;

        for (int i = 1; i <= maxRetries; i++)
        {
            try
            {
                File.AppendAllText(path, Environment.NewLine + line, Encoding.ASCII);
                error = "";
                return true;
            }
            catch (IOException ex)
            {
                if (i < maxRetries)
                {
                    Thread.Sleep(delayMs);
                    continue;
                }

                error = ex.Message;
                return false;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        error = "Unknown error";
        return false;
    }
}
