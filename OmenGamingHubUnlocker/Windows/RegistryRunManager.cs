using Microsoft.Win32;
using OmenGamingHubUnlocker.Core;

namespace OmenGamingHubUnlocker.Windows;

public sealed record RunEntry(string Location, string Name, string Value);

public static class RegistryRunManager
{
    private const string RunSubKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static List<RunEntry> QueryRunEntries(string[] patterns)
    {
        var list = new List<RunEntry>();

        list.AddRange(ReadFrom(RegistryHive.CurrentUser, RegistryView.Registry64, "HKCU(Registry64)", patterns));
        list.AddRange(ReadFrom(RegistryHive.CurrentUser, RegistryView.Registry32, "HKCU(Registry32)", patterns));
        list.AddRange(ReadFrom(RegistryHive.LocalMachine, RegistryView.Registry64, "HKLM(Registry64)", patterns));
        list.AddRange(ReadFrom(RegistryHive.LocalMachine, RegistryView.Registry32, "HKLM(Registry32)", patterns));

        return list;
    }

    public static List<OperationLine> RemoveRunEntries(string[] patterns, bool dryRun)
    {
        var lines = new List<OperationLine>();

        lines.AddRange(RemoveFrom(RegistryHive.CurrentUser, RegistryView.Registry64, "HKCU(Registry64)", patterns, dryRun));
        lines.AddRange(RemoveFrom(RegistryHive.CurrentUser, RegistryView.Registry32, "HKCU(Registry32)", patterns, dryRun));
        lines.AddRange(RemoveFrom(RegistryHive.LocalMachine, RegistryView.Registry64, "HKLM(Registry64)", patterns, dryRun));
        lines.AddRange(RemoveFrom(RegistryHive.LocalMachine, RegistryView.Registry32, "HKLM(Registry32)", patterns, dryRun));

        if (lines.Count == 0)
            lines.Add(new OperationLine { Level = "INFO", Text = "Registry Run: no matching entries found." });

        return lines;
    }

    private static IEnumerable<RunEntry> ReadFrom(RegistryHive hive, RegistryView view, string loc, string[] patterns)
    {
        var list = new List<RunEntry>();

        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var runKey = baseKey.OpenSubKey(RunSubKey, writable: false);
            if (runKey is null) return list;

            foreach (var name in runKey.GetValueNames())
            {
                var val = runKey.GetValue(name)?.ToString() ?? "";
                if (MatchAny(name, val, patterns))
                    list.Add(new RunEntry($"{loc}\\{RunSubKey}", name, val));
            }
        }
        catch
        {
            // keep non-fatal
        }

        return list;
    }

    private static IEnumerable<OperationLine> RemoveFrom(RegistryHive hive, RegistryView view, string loc, string[] patterns, bool dryRun)
    {
        var lines = new List<OperationLine>();

        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var runKey = baseKey.OpenSubKey(RunSubKey, writable: true);
            if (runKey is null) return lines;

            foreach (var name in runKey.GetValueNames())
            {
                var val = runKey.GetValue(name)?.ToString() ?? "";
                if (!MatchAny(name, val, patterns))
                    continue;

                if (dryRun)
                {
                    lines.Add(new OperationLine { Level = "OK", Text = $"Registry Run: would remove {loc}\\{RunSubKey} -> {name}" });
                    continue;
                }

                runKey.DeleteValue(name, throwOnMissingValue: false);
                lines.Add(new OperationLine { Level = "OK", Text = $"Registry Run: removed {loc}\\{RunSubKey} -> {name}" });
            }
        }
        catch (Exception ex)
        {
            lines.Add(new OperationLine { Level = "WARN", Text = $"Registry Run: failed in {loc} ({view}): {ex.Message}" });
        }

        return lines;
    }

    private static bool MatchAny(string valueName, string valueData, string[] patterns)
        => patterns.Any(p => WildMatch(valueName, p) || WildMatch(valueData, p));

    private static bool WildMatch(string input, string pattern)
    {
        var regex = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".") + "$";

        return System.Text.RegularExpressions.Regex.IsMatch(input ?? "", regex, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }
}
