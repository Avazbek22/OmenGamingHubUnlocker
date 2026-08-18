namespace OmenGamingHubUnlocker.Core;

/// <summary>
/// Isolates orchestration from Windows APIs so operation ordering and failures can be tested deterministically.
/// </summary>
public interface IUnlockerOperations
{
    IReadOnlyList<ProcessItem> QueryTargetProcesses();
    IReadOnlyList<ServiceItem> QueryTargetServices();
    IReadOnlyList<TaskItem> QueryTargetTasks();
    IReadOnlyList<RunEntry> QueryTargetRunEntries();
    UserContextStatus InspectUserContext();
    FirewallProtectionStatus InspectFirewallProtection();
    HostsInspection InspectHosts();
    FirewallTargetSet DiscoverFirewallTargets();
    IReadOnlyList<string> DiscoverFirewallExecutables();
    bool TryGetPrimaryPackage(out AppxPackageInfo? package, out string details);
    IReadOnlyList<(string Name, bool Success, string Details)> RunCapabilityChecks();

    IReadOnlyList<OperationLine> SetServiceStartModes(IEnumerable<ServiceStartModeTarget> targets, bool dryRun);
    IReadOnlyList<OperationLine> StopServices(IEnumerable<ServiceRuntimeTarget> targets, bool dryRun);
    IReadOnlyList<OperationLine> StartServices(IEnumerable<ServiceRuntimeTarget> targets, bool dryRun);
    IReadOnlyList<OperationLine> SetTaskEnabledStates(IEnumerable<TaskEnableTarget> targets, bool dryRun);
    IReadOnlyList<OperationLine> StopTasks(IEnumerable<TaskRuntimeTarget> targets, bool dryRun);
    IReadOnlyList<OperationLine> StartTasks(IEnumerable<TaskRuntimeTarget> targets, bool dryRun);
    IReadOnlyList<OperationLine> RemoveRunEntries(IEnumerable<RunEntry> entries, bool dryRun);
    IReadOnlyList<OperationLine> RestoreRunEntries(IEnumerable<RunEntryBackup> entries, bool dryRun);
    IReadOnlyList<OperationLine> TerminateTargetProcesses(bool dryRun);
    IReadOnlyList<OperationLine> ActivateFirewall(bool dryRun, bool removeStaleRules = false);
    IReadOnlyList<OperationLine> DisableFirewall(bool dryRun);
    IReadOnlyList<OperationLine> ActivateHosts(bool dryRun);
    IReadOnlyList<OperationLine> DisableHosts(bool dryRun);
    IReadOnlyList<OperationLine> ResetPackage(bool dryRun);
}

/// <summary>
/// Abstracts bounded waits so stabilization can run instantly in tests.
/// </summary>
public interface IOperationDelay
{
    void Wait(TimeSpan delay);
}

/// <summary>
/// Serializes machine-wide mutations across processes and Windows sessions.
/// </summary>
public interface IOperationLock
{
    bool TryAcquire(out IDisposable? lease, out string failureDetails);
}

public sealed class ThreadOperationDelay : IOperationDelay
{
    public void Wait(TimeSpan delay) => Thread.Sleep(delay);
}

/// <summary>
/// Defines the rollback persistence behavior required by the engine.
/// </summary>
public interface IUnlockerStateStore
{
    StateLoadResult LoadState();

    void PersistBackups(
        IEnumerable<ServiceBackup> serviceBackups,
        IEnumerable<TaskBackup> taskBackups,
        IEnumerable<RunEntryBackup> runEntryBackups);

    bool TryClear(out string failureDetails);
}

/// <summary>
/// Persists the current mutation phase so an interrupted operation can recover network isolation first.
/// </summary>
public interface IOperationJournalStore
{
    OperationJournalLoadResult Load();
    OperationJournalEntry Begin(
        UnlockerOperationKind operation,
        bool manageFirewall,
        bool manageHosts);
    void Advance(Guid operationId, UnlockerOperationPhase phase);
    void Complete(Guid operationId);
}

public sealed record StateLoadResult(UnlockerState State, bool Success, string Error)
{
    public static StateLoadResult Loaded(UnlockerState state) => new(state, true, string.Empty);
    public static StateLoadResult Failed(string error) => new(new UnlockerState(), false, error);
}
