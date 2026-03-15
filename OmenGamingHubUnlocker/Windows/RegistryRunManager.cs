using Microsoft.Win32;
using OmenGamingHubUnlocker.Core;

namespace OmenGamingHubUnlocker.Windows;

public sealed record RunEntry(RegistryHive Hive, RegistryView View, string Name, string Value)
{
    public string Location => $"{FormatHive(Hive)}({View})\\{RunSubKey}";

    internal static string RunSubKey => @"Software\Microsoft\Windows\CurrentVersion\Run";

    private static string FormatHive(RegistryHive hive)
        => hive == RegistryHive.CurrentUser ? "HKCU" : "HKLM";
}

public static class RegistryRunManager
{
    public static List<RunEntry> QueryRunEntries(string[] patterns)
    {
        var list = new List<RunEntry>();

        list.AddRange(ReadFrom(RegistryHive.CurrentUser, RegistryView.Registry64, patterns));
        list.AddRange(ReadFrom(RegistryHive.CurrentUser, RegistryView.Registry32, patterns));
        list.AddRange(ReadFrom(RegistryHive.LocalMachine, RegistryView.Registry64, patterns));
        list.AddRange(ReadFrom(RegistryHive.LocalMachine, RegistryView.Registry32, patterns));

        return list
            .DistinctBy(ToIdentityKey, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static List<OperationLine> RemoveEntries(IEnumerable<RunEntry> entries, bool dryRun)
    {
        var targets = entries
            .DistinctBy(ToIdentityKey, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (targets.Count == 0)
        {
            return
            [
                new OperationLine { Level = "INFO", Text = "Registry Run: nothing to remove." }
            ];
        }

        var lines = new List<OperationLine>();

        foreach (var group in targets.GroupBy(x => (x.Hive, x.View)))
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(group.Key.Hive, group.Key.View);
                using var runKey = baseKey.OpenSubKey(RunEntry.RunSubKey, writable: true);

                if (runKey is null)
                {
                    foreach (var entry in group)
                    {
                        lines.Add(new OperationLine
                        {
                            Level = "WARN",
                            Text = $"Registry Run: key not found for {entry.Location} -> {entry.Name}"
                        });
                    }

                    continue;
                }

                foreach (var entry in group)
                {
                    if (dryRun)
                    {
                        lines.Add(new OperationLine
                        {
                            Level = "OK",
                            Text = $"Registry Run: would remove {entry.Location} -> {entry.Name}"
                        });
                        continue;
                    }

                    runKey.DeleteValue(entry.Name, throwOnMissingValue: false);
                    lines.Add(new OperationLine
                    {
                        Level = "OK",
                        Text = $"Registry Run: removed {entry.Location} -> {entry.Name}"
                    });
                }
            }
            catch (Exception ex)
            {
                lines.Add(new OperationLine
                {
                    Level = "WARN",
                    Text = $"Registry Run: failed in {FormatLocation(group.Key.Hive, group.Key.View)}: {ex.Message}"
                });
            }
        }

        return lines;
    }

    public static List<OperationLine> RestoreEntries(IEnumerable<RunEntryBackup> entries, bool dryRun)
    {
        var targets = entries
            .DistinctBy(ToIdentityKey, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (targets.Count == 0)
        {
            return
            [
                new OperationLine { Level = "INFO", Text = "Registry Run: no backup state found." }
            ];
        }

        var lines = new List<OperationLine>();

        foreach (var group in targets.GroupBy(x => (x.Hive, x.View)))
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(group.Key.Hive, group.Key.View);
                using var runKey = baseKey.CreateSubKey(RunEntry.RunSubKey, writable: true);

                if (runKey is null)
                {
                    foreach (var entry in group)
                    {
                        lines.Add(new OperationLine
                        {
                            Level = "ERR",
                            Text = $"Registry Run: failed to open {FormatLocation(entry.Hive, entry.View)} for restore."
                        });
                    }

                    continue;
                }

                foreach (var entry in group)
                {
                    if (dryRun)
                    {
                        lines.Add(new OperationLine
                        {
                            Level = "OK",
                            Text = $"Registry Run: would restore {FormatLocation(entry.Hive, entry.View)}\\{RunEntry.RunSubKey} -> {entry.Name}"
                        });
                        continue;
                    }

                    runKey.SetValue(entry.Name, entry.Value, RegistryValueKind.String);
                    lines.Add(new OperationLine
                    {
                        Level = "OK",
                        Text = $"Registry Run: restored {FormatLocation(entry.Hive, entry.View)}\\{RunEntry.RunSubKey} -> {entry.Name}"
                    });
                }
            }
            catch (Exception ex)
            {
                lines.Add(new OperationLine
                {
                    Level = "ERR",
                    Text = $"Registry Run: restore failed in {FormatLocation(group.Key.Hive, group.Key.View)}: {ex.Message}"
                });
            }
        }

        return lines;
    }

    private static IEnumerable<RunEntry> ReadFrom(RegistryHive hive, RegistryView view, string[] patterns)
    {
        var list = new List<RunEntry>();
        var matchAll = patterns.Length == 0;

        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var runKey = baseKey.OpenSubKey(RunEntry.RunSubKey, writable: false);
            if (runKey is null)
                return list;

            foreach (var name in runKey.GetValueNames())
            {
                var value = runKey.GetValue(name)?.ToString() ?? string.Empty;
                if (matchAll || MatchAny(name, value, patterns))
                    list.Add(new RunEntry(hive, view, name, value));
            }
        }
        catch
        {
            // Keep non-fatal.
        }

        return list;
    }

    private static string FormatLocation(RegistryHive hive, RegistryView view)
        => $"{(hive == RegistryHive.CurrentUser ? "HKCU" : "HKLM")}({view})";

    private static string ToIdentityKey(RunEntry entry)
        => $"{entry.Hive}|{entry.View}|{entry.Name}";

    private static string ToIdentityKey(RunEntryBackup entry)
        => $"{entry.Hive}|{entry.View}|{entry.Name}";

    private static bool MatchAny(string valueName, string valueData, string[] patterns)
        => patterns.Any(p => WildMatch(valueName, p) || WildMatch(valueData, p));

    private static bool WildMatch(string input, string pattern)
    {
        var regex = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".") + "$";

        return System.Text.RegularExpressions.Regex.IsMatch(input ?? string.Empty, regex, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }
}
