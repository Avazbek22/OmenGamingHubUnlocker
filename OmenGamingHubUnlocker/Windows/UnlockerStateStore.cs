namespace OmenGamingHubUnlocker.Windows;

/// <summary>
/// Stores the original service, task, and Run-entry state so the app can restore what it changed.
/// </summary>
public sealed record ServiceBackup(
    string Name,
    string OriginalStartMode,
    bool OriginalRunning = false,
    bool OriginalDelayedAutoStart = false,
    string PathName = "");

/// <summary>
/// Stores the original enabled and runtime state of a scheduled task.
/// </summary>
public sealed record TaskBackup(
    string Path,
    bool OriginalEnabled,
    bool OriginalRunning = false,
    ScheduledTaskRuntimeState OriginalRuntimeState = ScheduledTaskRuntimeState.Unknown,
    IReadOnlyList<string>? Actions = null)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public ScheduledTaskRuntimeState EffectiveOriginalRuntimeState =>
        OriginalRuntimeState != ScheduledTaskRuntimeState.Unknown
            ? OriginalRuntimeState
            : OriginalRunning
                ? ScheduledTaskRuntimeState.Running
                : ScheduledTaskRuntimeState.Ready;

    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyList<string> ActionPaths => Actions ?? [];
}

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
    public string OwnerUserSid { get; set; } = string.Empty;
    public bool ActivationRecorded { get; set; }
    public List<ServiceBackup> Services { get; init; } = [];
    public List<TaskBackup> Tasks { get; init; } = [];
    public List<RunEntryBackup> RunEntries { get; init; } = [];

    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasRollbackRecord =>
        ActivationRecorded || Services.Count > 0 || Tasks.Count > 0 || RunEntries.Count > 0;
}

/// <summary>
/// Persists and merges rollback data inside ProgramData so it survives multiple runs.
/// </summary>
public sealed class UnlockerStateStore : IUnlockerStateStore
{
    public const int CurrentSchemaVersion = 6;
    private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan LockRetryDelay = TimeSpan.FromMilliseconds(50);

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _stateFilePath;
    private readonly string _lockFilePath;
    private readonly string _currentUserSid;
    private readonly bool _hardenStorage;

    public UnlockerStateStore(string? stateFilePath = null, string? currentUserSid = null)
    {
        _currentUserSid = string.IsNullOrWhiteSpace(currentUserSid)
            ? ResolveCurrentUserSid()
            : currentUserSid.Trim();
        _hardenStorage = string.IsNullOrWhiteSpace(stateFilePath);

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
            {
                return StateLoadResult.Loaded(new UnlockerState
                {
                    OwnerUserSid = _currentUserSid
                });
            }

            var json = SecureStorage.ReadAllText(_stateFilePath);
            var state = JsonSerializer.Deserialize<UnlockerState>(json, SerializerOptions);
            if (state is null)
                return StateLoadResult.Failed("The rollback state file is empty.");

            if (state.SchemaVersion > CurrentSchemaVersion)
            {
                return StateLoadResult.Failed(
                    $"Rollback schema {state.SchemaVersion} is newer than supported schema {CurrentSchemaVersion}.");
            }

            if (string.IsNullOrWhiteSpace(state.OwnerUserSid) && state.HasRollbackRecord)
            {
                var fileOwnerSid = SecureStorage.TryGetOwnerSid(_stateFilePath);
                if (!string.Equals(fileOwnerSid, _currentUserSid, StringComparison.OrdinalIgnoreCase))
                {
                    return StateLoadResult.Failed(
                        "Legacy rollback state has no verifiable Windows user owner and cannot be adopted safely.");
                }
            }
            else if (!string.IsNullOrWhiteSpace(state.OwnerUserSid) &&
                     !state.OwnerUserSid.Equals(_currentUserSid, StringComparison.OrdinalIgnoreCase))
            {
                return StateLoadResult.Failed(
                    $"The rollback state belongs to Windows user {state.OwnerUserSid}, not {_currentUserSid}.");
            }

            // Legacy state did not contain an owner SID or an explicit activation marker.
            state.ActivationRecorded |= state.Services.Count > 0 ||
                                        state.Tasks.Count > 0 ||
                                        state.RunEntries.Count > 0;
            state.OwnerUserSid = _currentUserSid;

            for (var index = 0; index < state.Tasks.Count; index++)
            {
                var task = state.Tasks[index];
                if (task.OriginalRuntimeState == ScheduledTaskRuntimeState.Unknown)
                {
                    state.Tasks[index] = task with
                    {
                        OriginalRuntimeState = task.OriginalRunning
                            ? ScheduledTaskRuntimeState.Running
                            : ScheduledTaskRuntimeState.Ready
                    };
                }
            }

            if (!TryValidateState(state, out var validationError))
                return StateLoadResult.Failed(validationError);

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
        currentState.OwnerUserSid = _currentUserSid;
        currentState.ActivationRecorded = true;

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
            var loadResult = LoadStateWithoutLock();
            if (!loadResult.Success)
            {
                failureDetails = loadResult.Error;
                return false;
            }

            SecureStorage.DeleteFile(_stateFilePath);

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

        SecureStorage.EnsureDirectory(stateDirectory, _hardenStorage);
        var stopwatch = Stopwatch.StartNew();

        while (true)
        {
            try
            {
                return SecureStorage.OpenExclusiveLock(_lockFilePath);
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
        var json = JsonSerializer.Serialize(state, SerializerOptions);
        SecureStorage.WriteAllTextAtomically(
            _stateFilePath,
            json,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            _hardenStorage);
    }

    private static void MergeServices(List<ServiceBackup> existingBackups, IEnumerable<ServiceBackup> newBackups)
    {
        foreach (var backup in newBackups.DistinctBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
        {
            var existingIndex = existingBackups.FindIndex(existing =>
                existing.Name.Equals(backup.Name, StringComparison.OrdinalIgnoreCase));
            if (existingIndex < 0)
            {
                existingBackups.Add(backup);
                continue;
            }

            var existing = existingBackups[existingIndex];
            if (string.IsNullOrWhiteSpace(existing.PathName))
            {
                existingBackups[existingIndex] = existing with { PathName = backup.PathName };
                continue;
            }

            if (!TargetIdentityMatcher.ServiceIdentityEquals(existing.PathName, backup.PathName))
                existingBackups[existingIndex] = backup;
        }
    }

    private static void MergeTasks(List<TaskBackup> existingBackups, IEnumerable<TaskBackup> newBackups)
    {
        foreach (var backup in newBackups.DistinctBy(item => item.Path, StringComparer.OrdinalIgnoreCase))
        {
            var existingIndex = existingBackups.FindIndex(existing =>
                existing.Path.Equals(backup.Path, StringComparison.OrdinalIgnoreCase));
            if (existingIndex < 0)
            {
                existingBackups.Add(backup);
                continue;
            }

            var existing = existingBackups[existingIndex];
            if (existing.Actions is null)
            {
                existingBackups[existingIndex] = existing with { Actions = backup.Actions };
                continue;
            }

            if (backup.Actions is null)
                continue;

            if (!TargetIdentityMatcher.TaskIdentityEquals(existing.Actions, backup.Actions))
                existingBackups[existingIndex] = backup;
        }
    }

    private static void MergeRunEntries(List<RunEntryBackup> existingBackups, IEnumerable<RunEntryBackup> newBackups)
    {
        foreach (var backup in newBackups.DistinctBy(BuildRunEntryIdentity, StringComparer.OrdinalIgnoreCase))
        {
            var existingIndex = existingBackups.FindIndex(existing =>
                BuildRunEntryIdentity(existing).Equals(
                    BuildRunEntryIdentity(backup),
                    StringComparison.OrdinalIgnoreCase));
            if (existingIndex < 0)
            {
                existingBackups.Add(backup);
                continue;
            }

            var existing = existingBackups[existingIndex];
            if (!existing.Value.Equals(backup.Value, StringComparison.Ordinal) ||
                existing.ValueKind != backup.ValueKind)
            {
                existingBackups[existingIndex] = backup;
            }
        }
    }

    private static bool TryValidateState(UnlockerState state, out string error)
    {
        if (state.Services.Any(service =>
                string.IsNullOrWhiteSpace(service.Name) ||
                !IsSupportedServiceMode(service.OriginalStartMode)))
        {
            error = "Rollback state contains an invalid service backup.";
            return false;
        }

        if (HasDuplicate(state.Services, service => service.Name) ||
            state.Tasks.Any(task =>
                string.IsNullOrWhiteSpace(task.Path) ||
                !Enum.IsDefined(task.OriginalRuntimeState) ||
                task.Actions?.Any(string.IsNullOrWhiteSpace) == true) ||
            HasDuplicate(state.Tasks, task => task.Path))
        {
            error = "Rollback state contains an invalid scheduled-task backup.";
            return false;
        }

        if (state.RunEntries.Any(entry =>
                (entry.Hive is not RegistryHive.CurrentUser and not RegistryHive.LocalMachine) ||
                (entry.View is not RegistryView.Registry32 and not RegistryView.Registry64) ||
                string.IsNullOrWhiteSpace(entry.Name) ||
                (entry.ValueKind is not RegistryValueKind.String and not RegistryValueKind.ExpandString)) ||
            HasDuplicate(state.RunEntries, BuildRunEntryIdentity))
        {
            error = "Rollback state contains an invalid Run-entry backup.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static bool IsSupportedServiceMode(string mode)
        => ServiceManager.NormalizeStartMode(mode) is "Automatic" or "Manual" or "Disabled";

    private static bool HasDuplicate<T>(IEnumerable<T> values, Func<T, string> identity)
    {
        var identities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return values.Any(value => !identities.Add(identity(value)));
    }

    private static string BuildRunEntryIdentity(RunEntryBackup backup)
        => $"{backup.Hive}|{backup.View}|{backup.Name}";

    private static string ResolveCurrentUserSid()
    {
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        return identity.User?.Value ??
               throw new InvalidOperationException("The current Windows user SID is unavailable.");
    }
}
