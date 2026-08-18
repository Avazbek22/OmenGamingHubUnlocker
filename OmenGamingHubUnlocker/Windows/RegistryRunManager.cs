namespace OmenGamingHubUnlocker.Windows;

/// <summary>
/// Snapshot of a Run key entry together with its registry location.
/// </summary>
public sealed record RunEntry(
    RegistryHive Hive,
    RegistryView View,
    string Name,
    string Value,
    RegistryValueKind ValueKind = RegistryValueKind.String)
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
        var errors = new List<string>();

        matchingEntries.AddRange(ReadFrom(RegistryHive.CurrentUser, RegistryView.Registry64, patterns, errors));
        matchingEntries.AddRange(ReadFrom(RegistryHive.CurrentUser, RegistryView.Registry32, patterns, errors));
        matchingEntries.AddRange(ReadFrom(RegistryHive.LocalMachine, RegistryView.Registry64, patterns, errors));
        matchingEntries.AddRange(ReadFrom(RegistryHive.LocalMachine, RegistryView.Registry32, patterns, errors));

        if (errors.Count > 0)
            throw new InvalidOperationException(string.Join("; ", errors));

        return matchingEntries
            .DistinctBy(BuildIdentityKey, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static List<OperationLine> RemoveEntries(IEnumerable<RunEntry> entries, bool dryRun)
        => RemoveEntriesAtSubKey(entries, dryRun, RunEntry.RunSubKey);

    internal static List<OperationLine> RemoveEntriesAtSubKey(
        IEnumerable<RunEntry> entries,
        bool dryRun,
        string runSubKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runSubKey);
        var targetEntries = entries
            .DistinctBy(BuildIdentityKey, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (targetEntries.Count == 0)
        {
            return
            [
                LocalizedLine.Info("manager.registry.nothingToRemove")
            ];
        }

        var operationLines = new List<OperationLine>();

        foreach (var entryGroup in targetEntries.GroupBy(entry => (entry.Hive, entry.View)))
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(entryGroup.Key.Hive, entryGroup.Key.View);
                using var runKey = baseKey.OpenSubKey(runSubKey, writable: !dryRun);

                if (runKey is null)
                {
                    foreach (var entry in entryGroup)
                    {
                        operationLines.Add(LocalizedLine.Info(
                            "manager.registry.alreadyAbsent",
                            entry.Location,
                            entry.Name));
                    }

                    continue;
                }

                foreach (var entry in entryGroup)
                {
                    var valueExists = runKey.GetValueNames()
                        .Contains(entry.Name, StringComparer.OrdinalIgnoreCase);
                    if (!valueExists)
                    {
                        operationLines.Add(LocalizedLine.Info(
                            "manager.registry.alreadyAbsent",
                            entry.Location,
                            entry.Name));
                        continue;
                    }

                    var currentValue = runKey.GetValue(
                            entry.Name,
                            defaultValue: string.Empty,
                            RegistryValueOptions.DoNotExpandEnvironmentNames)
                        ?.ToString() ?? string.Empty;
                    var currentKind = runKey.GetValueKind(entry.Name);
                    if (!currentValue.Equals(entry.Value, StringComparison.Ordinal) ||
                        currentKind != entry.ValueKind)
                    {
                        operationLines.Add(LocalizedLine.Err(
                            "manager.registry.removeConflict",
                            entry.Location,
                            entry.Name));
                        continue;
                    }

                    if (dryRun)
                    {
                        operationLines.Add(new OperationLine
                        {
                            Level = "OK",
                            Text = Text.Format("manager.registry.wouldRemove", entry.Location, entry.Name)
                        });
                        continue;
                    }

                    runKey.DeleteValue(entry.Name, throwOnMissingValue: false);
                    operationLines.Add(new OperationLine
                    {
                        Level = "OK",
                        Text = Text.Format("manager.registry.removed", entry.Location, entry.Name)
                    });
                }
            }
            catch (Exception exception)
            {
                operationLines.Add(new OperationLine
                {
                    Level = "ERR",
                    Text = Text.Format("manager.registry.failedIn", FormatLocation(entryGroup.Key.Hive, entryGroup.Key.View), exception.Message)
                });
            }
        }

        return operationLines;
    }

    public static List<OperationLine> RestoreEntries(IEnumerable<RunEntryBackup> entries, bool dryRun)
        => RestoreEntriesAtSubKey(entries, dryRun, RunEntry.RunSubKey);

    internal static List<OperationLine> RestoreEntriesAtSubKey(
        IEnumerable<RunEntryBackup> entries,
        bool dryRun,
        string runSubKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runSubKey);
        var targetEntries = entries
            .DistinctBy(BuildIdentityKey, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (targetEntries.Count == 0)
        {
            return
            [
                LocalizedLine.Info("manager.registry.noBackupState")
            ];
        }

        var operationLines = new List<OperationLine>();

        foreach (var entryGroup in targetEntries.GroupBy(entry => (entry.Hive, entry.View)))
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(entryGroup.Key.Hive, entryGroup.Key.View);
                using var runKey = dryRun
                    ? baseKey.OpenSubKey(runSubKey, writable: false)
                    : baseKey.CreateSubKey(runSubKey, writable: true);

                if (runKey is null)
                {
                    foreach (var entry in entryGroup)
                    {
                        operationLines.Add(dryRun
                            ? LocalizedLine.Ok(
                                "manager.registry.wouldRestore",
                                FormatLocation(entry.Hive, entry.View),
                                runSubKey,
                                entry.Name)
                            : new OperationLine
                            {
                                Level = "ERR",
                                Text = Text.Format(
                                    "manager.registry.failedToOpenForRestore",
                                    FormatLocation(entry.Hive, entry.View))
                            });
                    }

                    continue;
                }

                foreach (var entry in entryGroup)
                {
                    var valueExists = runKey.GetValueNames().Contains(entry.Name, StringComparer.OrdinalIgnoreCase);
                    if (valueExists)
                    {
                        var currentValue = runKey.GetValue(
                                entry.Name,
                                defaultValue: string.Empty,
                                RegistryValueOptions.DoNotExpandEnvironmentNames)
                            ?.ToString() ?? string.Empty;
                        var currentKind = runKey.GetValueKind(entry.Name);

                        if (currentValue.Equals(entry.Value, StringComparison.Ordinal) &&
                            currentKind == entry.ValueKind)
                        {
                            operationLines.Add(LocalizedLine.Info(
                                "manager.registry.alreadyRestored",
                                FormatLocation(entry.Hive, entry.View),
                                entry.Name));
                        }
                        else
                        {
                            operationLines.Add(LocalizedLine.Err(
                                "manager.registry.restoreConflict",
                                FormatLocation(entry.Hive, entry.View),
                                entry.Name));
                        }

                        continue;
                    }

                    if (dryRun)
                    {
                        operationLines.Add(new OperationLine
                        {
                            Level = "OK",
                            Text = Text.Format("manager.registry.wouldRestore", FormatLocation(entry.Hive, entry.View), runSubKey, entry.Name)
                        });
                        continue;
                    }

                    runKey.SetValue(entry.Name, entry.Value, entry.ValueKind);
                    operationLines.Add(new OperationLine
                    {
                        Level = "OK",
                        Text = Text.Format("manager.registry.restored", FormatLocation(entry.Hive, entry.View), runSubKey, entry.Name)
                    });
                }
            }
            catch (Exception exception)
            {
                operationLines.Add(new OperationLine
                {
                    Level = "ERR",
                    Text = Text.Format("manager.registry.restoreFailedIn", FormatLocation(entryGroup.Key.Hive, entryGroup.Key.View), exception.Message)
                });
            }
        }

        return operationLines;
    }

    private static List<RunEntry> ReadFrom(
        RegistryHive hive,
        RegistryView view,
        string[] patterns,
        List<string> errors)
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
                var valueKind = runKey.GetValueKind(valueName);
                var valueData = runKey.GetValue(
                        valueName,
                        defaultValue: string.Empty,
                        RegistryValueOptions.DoNotExpandEnvironmentNames)
                    ?.ToString() ?? string.Empty;
                if (!matchEverything &&
                    (!MatchesAnyPattern(valueName, valueData, patterns) ||
                     !OmenIdentity.IsLikelyOmenReference(valueName, valueData)))
                    continue;

                if (valueKind is not RegistryValueKind.String and not RegistryValueKind.ExpandString)
                {
                    errors.Add(
                        $"{FormatLocation(hive, view)}\\{valueName}: unsupported Run value type {valueKind}.");
                    continue;
                }

                matchingEntries.Add(new RunEntry(hive, view, valueName, valueData, valueKind));
            }
        }
        catch (Exception exception)
        {
            errors.Add($"{FormatLocation(hive, view)}: {exception.Message}");
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
        => patterns.Any(pattern =>
            WildcardMatcher.IsMatch(valueName, pattern) ||
            WildcardMatcher.IsMatch(valueData, pattern));
}
