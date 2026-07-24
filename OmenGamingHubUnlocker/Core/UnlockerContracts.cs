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
    IReadOnlyList<string> DiscoverFirewallExecutables();
    bool TryGetPrimaryPackage(out AppxPackageInfo? package, out string details);
    IReadOnlyList<(string Name, bool Success, string Details)> RunCapabilityChecks();

    IReadOnlyList<OperationLine> SetServiceStartModes(IEnumerable<ServiceStartModeTarget> targets, bool dryRun);
    IReadOnlyList<OperationLine> StopServices(IEnumerable<string> serviceNames, bool dryRun);
    IReadOnlyList<OperationLine> StartServices(IEnumerable<string> serviceNames, bool dryRun);
    IReadOnlyList<OperationLine> SetTaskEnabledStates(IEnumerable<TaskEnableTarget> targets, bool dryRun);
    IReadOnlyList<OperationLine> StopTasks(IEnumerable<string> taskPaths, bool dryRun);
    IReadOnlyList<OperationLine> RemoveRunEntries(IEnumerable<RunEntry> entries, bool dryRun);
    IReadOnlyList<OperationLine> RestoreRunEntries(IEnumerable<RunEntryBackup> entries, bool dryRun);
    IReadOnlyList<OperationLine> TerminateTargetProcesses(bool dryRun);
    IReadOnlyList<OperationLine> ActivateFirewall(bool dryRun);
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

public sealed record StateLoadResult(UnlockerState State, bool Success, string Error)
{
    public static StateLoadResult Loaded(UnlockerState state) => new(state, true, string.Empty);
    public static StateLoadResult Failed(string error) => new(new UnlockerState(), false, error);
}
