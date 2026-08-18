namespace OmenGamingHubUnlocker.Windows;

/// <summary>
/// Persists operation checkpoints separately from rollback state so crash recovery remains independently readable.
/// </summary>
public sealed class FileOperationJournalStore : IOperationJournalStore
{
    private const int CurrentSchemaVersion = 1;
    private static readonly UTF8Encoding Utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private readonly string _journalPath;
    private readonly string _currentUserSid;
    private readonly bool _hardenStorage;
    private readonly Func<DateTimeOffset> _utcNow;

    public FileOperationJournalStore(
        string? journalPath = null,
        string? currentUserSid = null,
        Func<DateTimeOffset>? utcNow = null)
    {
        _currentUserSid = string.IsNullOrWhiteSpace(currentUserSid)
            ? ResolveCurrentUserSid()
            : currentUserSid.Trim();
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        _hardenStorage = string.IsNullOrWhiteSpace(journalPath);

        _journalPath = !string.IsNullOrWhiteSpace(journalPath)
            ? journalPath
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "OmenGamingHubUnlocker",
                "operation.json");
    }

    public OperationJournalLoadResult Load()
    {
        try
        {
            if (!File.Exists(_journalPath))
                return OperationJournalLoadResult.Empty();

            var json = SecureStorage.ReadAllText(_journalPath);
            var document = JsonSerializer.Deserialize<OperationJournalDocument>(json, SerializerOptions);
            if (document?.Entry is null)
                return OperationJournalLoadResult.Failed("The operation journal is empty.");

            if (document.SchemaVersion is <= 0 or > CurrentSchemaVersion)
                return OperationJournalLoadResult.Failed("The operation journal was created by a newer app version.");

            if (!TryValidateEntry(document.Entry, out var validationError))
                return OperationJournalLoadResult.Failed(validationError);

            if (!document.Entry.OwnerUserSid.Equals(_currentUserSid, StringComparison.OrdinalIgnoreCase))
                return OperationJournalLoadResult.Failed("The operation journal belongs to another Windows user.");

            if (document.Entry.Phase == UnlockerOperationPhase.Completed)
            {
                TryDeleteCompletedJournal();
                return OperationJournalLoadResult.Empty();
            }

            return OperationJournalLoadResult.Loaded(document.Entry);
        }
        catch (Exception exception)
        {
            return OperationJournalLoadResult.Failed(exception.Message);
        }
    }

    public OperationJournalEntry Begin(
        UnlockerOperationKind operation,
        bool manageFirewall,
        bool manageHosts)
    {
        var existing = Load();
        if (!existing.Success)
            throw new InvalidOperationException(existing.Error);
        if (existing.Entry is not null)
            throw new InvalidOperationException("An unfinished operation journal must be recovered before a new operation begins.");

        var now = _utcNow();
        var entry = new OperationJournalEntry(
            Guid.NewGuid(),
            operation,
            UnlockerOperationPhase.Prepared,
            manageFirewall,
            manageHosts,
            _currentUserSid,
            now,
            now);
        Save(entry);
        return entry;
    }

    public void Advance(Guid operationId, UnlockerOperationPhase phase)
    {
        var entry = LoadRequired(operationId);
        Save(entry with { Phase = phase, UpdatedUtc = _utcNow() });
    }

    public void Complete(Guid operationId)
    {
        var entry = LoadRequired(operationId);
        Save(entry with { Phase = UnlockerOperationPhase.Completed, UpdatedUtc = _utcNow() });
        TryDeleteCompletedJournal();
    }

    private OperationJournalEntry LoadRequired(Guid operationId)
    {
        var result = Load();
        if (!result.Success || result.Entry is null)
            throw new InvalidOperationException(result.Error.Length > 0 ? result.Error : "The operation journal is missing.");
        if (result.Entry.OperationId != operationId)
            throw new InvalidOperationException("The operation journal identity changed unexpectedly.");

        return result.Entry;
    }

    private void Save(OperationJournalEntry entry)
    {
        var json = JsonSerializer.Serialize(
            new OperationJournalDocument { Entry = entry },
            SerializerOptions);
        SecureStorage.WriteAllTextAtomically(
            _journalPath,
            json,
            Utf8WithoutBom,
            _hardenStorage);
    }

    private void TryDeleteCompletedJournal()
    {
        try
        {
            SecureStorage.DeleteFile(_journalPath);
        }
        catch
        {
            // A durable Completed marker prevents false recovery even when antivirus delays deletion.
        }
    }

    private static bool TryValidateEntry(OperationJournalEntry entry, out string error)
    {
        if (entry.OperationId == Guid.Empty ||
            !Enum.IsDefined(entry.Operation) ||
            !Enum.IsDefined(entry.Phase) ||
            string.IsNullOrWhiteSpace(entry.OwnerUserSid) ||
            entry.StartedUtc == default ||
            entry.UpdatedUtc < entry.StartedUtc)
        {
            error = "The operation journal contains invalid checkpoint data.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static string ResolveCurrentUserSid()
    {
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        return identity.User?.Value ??
               throw new InvalidOperationException("The current Windows user SID is unavailable.");
    }

    private sealed class OperationJournalDocument
    {
        public int SchemaVersion { get; init; } = CurrentSchemaVersion;
        public OperationJournalEntry? Entry { get; init; }
    }
}
