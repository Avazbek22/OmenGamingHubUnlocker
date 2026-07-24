namespace OmenGamingHubUnlocker.Windows;

/// <summary>
/// Stores the original service, task, and Run-entry state so the app can restore what it changed.
/// </summary>
public sealed record ServiceBackup(
    string Name,
    string OriginalStartMode,
    bool OriginalRunning = false,
    bool OriginalDelayedAutoStart = false);

/// <summary>
/// Stores the original enabled flag of a scheduled task.
/// </summary>
public sealed record TaskBackup(string Path, bool OriginalEnabled);

/// <summary>
/// Stores the original value of a Run entry together with its registry location.
/// </summary>
public sealed record RunEntryBackup(
    RegistryHive Hive,
    RegistryView View,
    string Name,
    string Value,
    RegistryValueKind ValueKind = RegistryValueKind.String);

/// <summary>
/// Serializable container for every persisted rollback artifact.
/// </summary>
public sealed class UnlockerState
{
    public int SchemaVersion { get; set; } = UnlockerStateStore.CurrentSchemaVersion;
    public List<ServiceBackup> Services { get; init; } = [];
    public List<TaskBackup> Tasks { get; init; } = [];
    public List<RunEntryBackup> RunEntries { get; init; } = [];
}

/// <summary>
/// Persists and merges rollback data inside ProgramData so it survives multiple runs.
/// </summary>
public sealed class UnlockerStateStore : IUnlockerStateStore
{
    public const int CurrentSchemaVersion = 4;
    private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan LockRetryDelay = TimeSpan.FromMilliseconds(50);

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _stateFilePath;
    private readonly string _lockFilePath;

    public UnlockerStateStore(string? stateFilePath = null)
    {
        if (!string.IsNullOrWhiteSpace(stateFilePath))
        {
            _stateFilePath = stateFilePath;
            _lockFilePath = stateFilePath + ".lock";
            return;
        }

        var stateDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "OmenGamingHubUnlocker");

        _stateFilePath = Path.Combine(stateDirectory, "state.json");
        _lockFilePath = _stateFilePath + ".lock";
    }

    public StateLoadResult LoadState()
    {
        try
        {
            using var stateLock = AcquireStateLock();
            return LoadStateWithoutLock();
        }
        catch (Exception exception)
        {
            return StateLoadResult.Failed(exception.Message);
        }
    }

    private StateLoadResult LoadStateWithoutLock()
    {
        try
        {
            if (!File.Exists(_stateFilePath))
                return StateLoadResult.Loaded(new UnlockerState());

            var json = File.ReadAllText(_stateFilePath);
            var state = JsonSerializer.Deserialize<UnlockerState>(json, SerializerOptions);
            if (state is null)
                return StateLoadResult.Failed("The rollback state file is empty.");

            if (state.SchemaVersion > CurrentSchemaVersion)
            {
                return StateLoadResult.Failed(
                    $"Rollback schema {state.SchemaVersion} is newer than supported schema {CurrentSchemaVersion}.");
            }

            state.SchemaVersion = CurrentSchemaVersion;
            return StateLoadResult.Loaded(state);
        }
        catch (Exception exception)
        {
            return StateLoadResult.Failed(exception.Message);
        }
    }

    public void PersistBackups(
        IEnumerable<ServiceBackup> serviceBackups,
        IEnumerable<TaskBackup> taskBackups,
        IEnumerable<RunEntryBackup> runEntryBackups)
    {
        using var stateLock = AcquireStateLock();
        var loadResult = LoadStateWithoutLock();
        if (!loadResult.Success)
            throw new InvalidOperationException($"Cannot read the existing rollback state: {loadResult.Error}");

        var currentState = loadResult.State;

        MergeServices(currentState.Services, serviceBackups);
        MergeTasks(currentState.Tasks, taskBackups);
        MergeRunEntries(currentState.RunEntries, runEntryBackups);

        Save(currentState);
    }

    public bool TryClear(out string failureDetails)
    {
        try
        {
            using var stateLock = AcquireStateLock();
            if (File.Exists(_stateFilePath))
                File.Delete(_stateFilePath);

            failureDetails = string.Empty;
            return true;
        }
        catch (Exception exception)
        {
            failureDetails = exception.Message;
            return false;
        }
    }

    private FileStream AcquireStateLock()
    {
        var stateDirectory = Path.GetDirectoryName(_lockFilePath);
        if (string.IsNullOrWhiteSpace(stateDirectory))
            throw new InvalidOperationException("State lock directory path is invalid.");

        Directory.CreateDirectory(stateDirectory);
        var stopwatch = Stopwatch.StartNew();

        while (true)
        {
            try
            {
                return new FileStream(
                    _lockFilePath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None);
            }
            catch (IOException) when (stopwatch.Elapsed < LockTimeout)
            {
                Thread.Sleep(LockRetryDelay);
            }

            if (stopwatch.Elapsed >= LockTimeout)
                throw new TimeoutException("Timed out waiting for exclusive access to the rollback state.");
        }
    }

    private void Save(UnlockerState state)
    {
        var stateDirectory = Path.GetDirectoryName(_stateFilePath);
        if (string.IsNullOrWhiteSpace(stateDirectory))
            throw new InvalidOperationException("State directory path is invalid.");

        Directory.CreateDirectory(stateDirectory);

        var json = JsonSerializer.Serialize(state, SerializerOptions);
        var temporaryPath = Path.Combine(
            stateDirectory,
            $".{Path.GetFileName(_stateFilePath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            File.WriteAllText(temporaryPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            if (File.Exists(_stateFilePath))
            {
                File.Replace(
                    temporaryPath,
                    _stateFilePath,
                    destinationBackupFileName: null,
                    ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temporaryPath, _stateFilePath);
            }
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
            catch
            {
                // A stale temporary file does not invalidate the committed state file.
            }
        }
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
