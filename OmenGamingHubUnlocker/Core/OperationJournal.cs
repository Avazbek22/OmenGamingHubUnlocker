namespace OmenGamingHubUnlocker.Core;

public enum UnlockerOperationKind
{
    Activate,
    ResetAndReapply,
    Disable
}

public enum UnlockerOperationPhase
{
    Prepared,
    ApplyingNetworkIsolation,
    ApplyingStartupConstraints,
    ResettingPackage,
    ReapplyingAfterReset,
    RestoringStartup,
    RemovingNetworkIsolation,
    Verifying,
    Completed
}

/// <summary>
/// Durable checkpoint for a machine-wide operation that may be interrupted by a crash or reboot.
/// </summary>
public sealed record OperationJournalEntry(
    Guid OperationId,
    UnlockerOperationKind Operation,
    UnlockerOperationPhase Phase,
    bool ManageFirewall,
    bool ManageHosts,
    string OwnerUserSid,
    DateTimeOffset StartedUtc,
    DateTimeOffset UpdatedUtc);

public sealed record OperationJournalLoadResult(
    OperationJournalEntry? Entry,
    bool Success,
    string Error)
{
    public static OperationJournalLoadResult Empty() => new(null, true, string.Empty);
    public static OperationJournalLoadResult Loaded(OperationJournalEntry entry) => new(entry, true, string.Empty);
    public static OperationJournalLoadResult Failed(string error) => new(null, false, error);
}

/// <summary>
/// Keeps injected engine tests isolated unless they explicitly opt into journal behavior.
/// </summary>
internal sealed class NoOpOperationJournalStore : IOperationJournalStore
{
    public OperationJournalLoadResult Load() => OperationJournalLoadResult.Empty();

    public OperationJournalEntry Begin(
        UnlockerOperationKind operation,
        bool manageFirewall,
        bool manageHosts)
        => new(
            Guid.NewGuid(),
            operation,
            UnlockerOperationPhase.Prepared,
            manageFirewall,
            manageHosts,
            string.Empty,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);

    public void Advance(Guid operationId, UnlockerOperationPhase phase)
    {
    }

    public void Complete(Guid operationId)
    {
    }
}
