using Microsoft.Win32;
using OmenGamingHubUnlocker.Core;

namespace OmenGamingHubUnlocker.Windows;

/// <summary>
/// Snapshot of a Run key entry together with its registry location.
/// </summary>
public sealed record RunEntry(RegistryHive Hive, RegistryView View, string Name, string Value)
{
    public string Location => $"{FormatHive(Hive)}({View})\\{RunSubKey}";

    internal static string RunSubKey => @"Software\Microsoft\Windows\CurrentVersion\Run";

    private static string FormatHive(RegistryHive hive)
        => hive == RegistryHive.CurrentUser ? "HKCU" : "HKLM";
}

/// <summary>
/// Encapsulates Run key discovery, removal, and restore logic.
/// </summary>
public static class RegistryRunManager
{
    public static List<RunEntry> QueryRunEntries(string[] patterns)
    {
        var matchingEntries = new List<RunEntry>();

        matchingEntries.AddRange(ReadFrom(RegistryHive.CurrentUser, RegistryView.Registry64, patterns));
        matchingEntries.AddRange(ReadFrom(RegistryHive.CurrentUser, RegistryView.Registry32, patterns));
        matchingEntries.AddRange(ReadFrom(RegistryHive.LocalMachine, RegistryView.Registry64, patterns));
        matchingEntries.AddRange(ReadFrom(RegistryHive.LocalMachine, RegistryView.Registry32, patterns));

        return matchingEntries
            .DistinctBy(BuildIdentityKey, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static List<OperationLine> RemoveEntries(IEnumerable<RunEntry> entries, bool dryRun)
    {
        var targetEntries = entries
            .DistinctBy(BuildIdentityKey, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (targetEntries.Count == 0)
        {
            return
            [
                new OperationLine { Level = "INFO", Text = "Registry Run: nothing to remove." }
            ];
        }

        var operationLines = new List<OperationLine>();

        foreach (var entryGroup in targetEntries.GroupBy(entry => (entry.Hive, entry.View)))
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(entryGroup.Key.Hive, entryGroup.Key.View);
                using var runKey = baseKey.OpenSubKey(RunEntry.RunSubKey, writable: true);

                if (runKey is null)
                {
                    foreach (var entry in entryGroup)
                    {
                        operationLines.Add(new OperationLine
                        {
                            Level = "WARN",
                            Text = $"Registry Run: key not found for {entry.Location} -> {entry.Name}"
                        });
                    }

                    continue;
                }

                foreach (var entry in entryGroup)
                {
                    if (dryRun)
                    {
                        operationLines.Add(new OperationLine
                        {
                            Level = "OK",
                            Text = $"Registry Run: would remove {entry.Location} -> {entry.Name}"
                        });
                        continue;
                    }

                    runKey.DeleteValue(entry.Name, throwOnMissingValue: false);
                    operationLines.Add(new OperationLine
                    {
                        Level = "OK",
                        Text = $"Registry Run: removed {entry.Location} -> {entry.Name}"
                    });
                }
            }
            catch (Exception exception)
            {
                operationLines.Add(new OperationLine
                {
                    Level = "WARN",
                    Text = $"Registry Run: failed in {FormatLocation(entryGroup.Key.Hive, entryGroup.Key.View)}: {exception.Message}"
                });
            }
        }

        return operationLines;
    }

    public static List<OperationLine> RestoreEntries(IEnumerable<RunEntryBackup> entries, bool dryRun)
    {
        var targetEntries = entries
            .DistinctBy(BuildIdentityKey, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (targetEntries.Count == 0)
        {
            return
            [
                new OperationLine { Level = "INFO", Text = "Registry Run: no backup state found." }
            ];
        }

        var operationLines = new List<OperationLine>();

        foreach (var entryGroup in targetEntries.GroupBy(entry => (entry.Hive, entry.View)))
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(entryGroup.Key.Hive, entryGroup.Key.View);
                using var runKey = baseKey.CreateSubKey(RunEntry.RunSubKey, writable: true);

                if (runKey is null)
                {
                    foreach (var entry in entryGroup)
                    {
                        operationLines.Add(new OperationLine
                        {
                            Level = "ERR",
                            Text = $"Registry Run: failed to open {FormatLocation(entry.Hive, entry.View)} for restore."
                        });
                    }

                    continue;
                }

                foreach (var entry in entryGroup)
                {
                    if (dryRun)
                    {
                        operationLines.Add(new OperationLine
                        {
                            Level = "OK",
                            Text = $"Registry Run: would restore {FormatLocation(entry.Hive, entry.View)}\\{RunEntry.RunSubKey} -> {entry.Name}"
                        });
                        continue;
                    }

                    runKey.SetValue(entry.Name, entry.Value, RegistryValueKind.String);
                    operationLines.Add(new OperationLine
                    {
                        Level = "OK",
                        Text = $"Registry Run: restored {FormatLocation(entry.Hive, entry.View)}\\{RunEntry.RunSubKey} -> {entry.Name}"
                    });
                }
            }
            catch (Exception exception)
            {
                operationLines.Add(new OperationLine
                {
                    Level = "ERR",
                    Text = $"Registry Run: restore failed in {FormatLocation(entryGroup.Key.Hive, entryGroup.Key.View)}: {exception.Message}"
                });
            }
        }

        return operationLines;
    }

    private static IEnumerable<RunEntry> ReadFrom(RegistryHive hive, RegistryView view, string[] patterns)
    {
        var matchingEntries = new List<RunEntry>();
        var matchEverything = patterns.Length == 0;

        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var runKey = baseKey.OpenSubKey(RunEntry.RunSubKey, writable: false);
            if (runKey is null)
                return matchingEntries;

            foreach (var valueName in runKey.GetValueNames())
            {
                var valueData = runKey.GetValue(valueName)?.ToString() ?? string.Empty;
                if (matchEverything || MatchesAnyPattern(valueName, valueData, patterns))
                    matchingEntries.Add(new RunEntry(hive, view, valueName, valueData));
            }
        }
        catch
        {
            // A blocked hive should not stop the rest of the discovery pass.
        }

        return matchingEntries;
    }

    private static string FormatLocation(RegistryHive hive, RegistryView view)
        => $"{(hive == RegistryHive.CurrentUser ? "HKCU" : "HKLM")}({view})";

    private static string BuildIdentityKey(RunEntry entry)
        => $"{entry.Hive}|{entry.View}|{entry.Name}";

    private static string BuildIdentityKey(RunEntryBackup entry)
        => $"{entry.Hive}|{entry.View}|{entry.Name}";

    private static bool MatchesAnyPattern(string valueName, string valueData, string[] patterns)
        => patterns.Any(pattern => WildcardMatch(valueName, pattern) || WildcardMatch(valueData, pattern));

    private static bool WildcardMatch(string input, string pattern)
    {
        var regex = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".") + "$";

        return System.Text.RegularExpressions.Regex.IsMatch(
            input ?? string.Empty,
            regex,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }
}
