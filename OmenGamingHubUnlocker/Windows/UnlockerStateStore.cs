using System.Text.Json;
using Microsoft.Win32;

namespace OmenGamingHubUnlocker.Windows;

/// <summary>
/// Stores the original service, task, and Run-entry state so the app can restore what it changed.
/// </summary>
public sealed record ServiceBackup(string Name, string OriginalStartMode);

/// <summary>
/// Stores the original enabled flag of a scheduled task.
/// </summary>
public sealed record TaskBackup(string Path, bool OriginalEnabled);

/// <summary>
/// Stores the original value of a Run entry together with its registry location.
/// </summary>
public sealed record RunEntryBackup(RegistryHive Hive, RegistryView View, string Name, string Value);

/// <summary>
/// Serializable container for every persisted rollback artifact.
/// </summary>
public sealed class UnlockerState
{
    public List<ServiceBackup> Services { get; init; } = [];
    public List<TaskBackup> Tasks { get; init; } = [];
    public List<RunEntryBackup> RunEntries { get; init; } = [];
}

/// <summary>
/// Persists and merges rollback data inside ProgramData so it survives multiple runs.
/// </summary>
public sealed class UnlockerStateStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _stateFilePath;

    public UnlockerStateStore()
    {
        var stateDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "OmenGamingHubUnlocker");

        _stateFilePath = Path.Combine(stateDirectory, "state.json");
    }

    public UnlockerState Load()
    {
        try
        {
            if (!File.Exists(_stateFilePath))
                return new UnlockerState();

            var json = File.ReadAllText(_stateFilePath);
            return JsonSerializer.Deserialize<UnlockerState>(json, SerializerOptions) ?? new UnlockerState();
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
        var currentState = Load();

        MergeServices(currentState.Services, serviceBackups);
        MergeTasks(currentState.Tasks, taskBackups);
        MergeRunEntries(currentState.RunEntries, runEntryBackups);

        Save(currentState);
    }

    public void Clear()
    {
        try
        {
            if (File.Exists(_stateFilePath))
                File.Delete(_stateFilePath);
        }
        catch
        {
            // Failed cleanup should never block a successful disable flow.
        }
    }

    private void Save(UnlockerState state)
    {
        var stateDirectory = Path.GetDirectoryName(_stateFilePath);
        if (string.IsNullOrWhiteSpace(stateDirectory))
            throw new InvalidOperationException("State directory path is invalid.");

        Directory.CreateDirectory(stateDirectory);

        var json = JsonSerializer.Serialize(state, SerializerOptions);
        File.WriteAllText(_stateFilePath, json);
    }

    private static void MergeServices(List<ServiceBackup> existingBackups, IEnumerable<ServiceBackup> newBackups)
    {
        var knownServiceNames = new HashSet<string>(
            existingBackups.Select(backup => backup.Name),
            StringComparer.OrdinalIgnoreCase);

        foreach (var backup in newBackups.DistinctBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (knownServiceNames.Add(backup.Name))
                existingBackups.Add(backup);
        }
    }

    private static void MergeTasks(List<TaskBackup> existingBackups, IEnumerable<TaskBackup> newBackups)
    {
        var knownTaskPaths = new HashSet<string>(
            existingBackups.Select(backup => backup.Path),
            StringComparer.OrdinalIgnoreCase);

        foreach (var backup in newBackups.DistinctBy(item => item.Path, StringComparer.OrdinalIgnoreCase))
        {
            if (knownTaskPaths.Add(backup.Path))
                existingBackups.Add(backup);
        }
    }

    private static void MergeRunEntries(List<RunEntryBackup> existingBackups, IEnumerable<RunEntryBackup> newBackups)
    {
        var knownEntryKeys = new HashSet<string>(
            existingBackups.Select(BuildRunEntryIdentity),
            StringComparer.OrdinalIgnoreCase);

        foreach (var backup in newBackups.DistinctBy(BuildRunEntryIdentity, StringComparer.OrdinalIgnoreCase))
        {
            if (knownEntryKeys.Add(BuildRunEntryIdentity(backup)))
                existingBackups.Add(backup);
        }
    }

    private static string BuildRunEntryIdentity(RunEntryBackup backup)
        => $"{backup.Hive}|{backup.View}|{backup.Name}";
}
