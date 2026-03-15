using System.Text.Json;
using Microsoft.Win32;

namespace OmenGamingHubUnlocker.Windows;

public sealed record ServiceBackup(string Name, string OriginalStartMode);
public sealed record TaskBackup(string Path, bool OriginalEnabled);
public sealed record RunEntryBackup(RegistryHive Hive, RegistryView View, string Name, string Value);

public sealed class UnlockerState
{
    public List<ServiceBackup> Services { get; init; } = [];
    public List<TaskBackup> Tasks { get; init; } = [];
    public List<RunEntryBackup> RunEntries { get; init; } = [];
}

public sealed class UnlockerStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _statePath;

    public UnlockerStateStore()
    {
        var stateDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "OmenGamingHubUnlocker");

        _statePath = Path.Combine(stateDir, "state.json");
    }

    public UnlockerState Load()
    {
        try
        {
            if (!File.Exists(_statePath))
                return new UnlockerState();

            var json = File.ReadAllText(_statePath);
            return JsonSerializer.Deserialize<UnlockerState>(json, JsonOptions) ?? new UnlockerState();
        }
        catch
        {
            return new UnlockerState();
        }
    }

    public void PersistBackups(
        IEnumerable<ServiceBackup> serviceBackups,
        IEnumerable<TaskBackup> taskBackups,
        IEnumerable<RunEntryBackup> runEntryBackups)
    {
        var state = Load();

        MergeServices(state.Services, serviceBackups);
        MergeTasks(state.Tasks, taskBackups);
        MergeRunEntries(state.RunEntries, runEntryBackups);

        Save(state);
    }

    public void Clear()
    {
        try
        {
            if (File.Exists(_statePath))
                File.Delete(_statePath);
        }
        catch
        {
            // Best-effort cleanup only.
        }
    }

    private void Save(UnlockerState state)
    {
        var directory = Path.GetDirectoryName(_statePath);
        if (string.IsNullOrWhiteSpace(directory))
            throw new InvalidOperationException("State directory path is invalid.");

        Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(state, JsonOptions);
        File.WriteAllText(_statePath, json);
    }

    private static void MergeServices(List<ServiceBackup> existing, IEnumerable<ServiceBackup> incoming)
    {
        var known = new HashSet<string>(existing.Select(x => x.Name), StringComparer.OrdinalIgnoreCase);
        foreach (var item in incoming.DistinctBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (known.Add(item.Name))
                existing.Add(item);
        }
    }

    private static void MergeTasks(List<TaskBackup> existing, IEnumerable<TaskBackup> incoming)
    {
        var known = new HashSet<string>(existing.Select(x => x.Path), StringComparer.OrdinalIgnoreCase);
        foreach (var item in incoming.DistinctBy(x => x.Path, StringComparer.OrdinalIgnoreCase))
        {
            if (known.Add(item.Path))
                existing.Add(item);
        }
    }

    private static void MergeRunEntries(List<RunEntryBackup> existing, IEnumerable<RunEntryBackup> incoming)
    {
        var known = new HashSet<string>(
            existing.Select(ToRunEntryKey),
            StringComparer.OrdinalIgnoreCase);

        foreach (var item in incoming.DistinctBy(ToRunEntryKey, StringComparer.OrdinalIgnoreCase))
        {
            if (known.Add(ToRunEntryKey(item)))
                existing.Add(item);
        }
    }

    private static string ToRunEntryKey(RunEntryBackup backup)
        => $"{backup.Hive}|{backup.View}|{backup.Name}";
}
