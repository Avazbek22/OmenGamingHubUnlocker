namespace OmenGamingHubUnlocker.Tests.Windows;

public sealed class FileOperationJournalStoreTests
{
    private const string UserSid = "S-1-5-21-1000";

    [Fact]
    public void Journal_ShouldPersistEveryPhaseAndDisappearAfterCompletion()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "operation.json");
        var timestamps = new Queue<DateTimeOffset>(
        [
            DateTimeOffset.Parse("2026-01-01T00:00:00Z", CultureInfo.InvariantCulture),
            DateTimeOffset.Parse("2026-01-01T00:00:01Z", CultureInfo.InvariantCulture),
            DateTimeOffset.Parse("2026-01-01T00:00:02Z", CultureInfo.InvariantCulture)
        ]);
        var store = new FileOperationJournalStore(path, UserSid, timestamps.Dequeue);

        var entry = store.Begin(UnlockerOperationKind.ResetAndReapply, true, true);
        store.Advance(entry.OperationId, UnlockerOperationPhase.ResettingPackage);

        var loaded = store.Load();
        Assert.True(loaded.Success, loaded.Error);
        Assert.Equal(UnlockerOperationPhase.ResettingPackage, loaded.Entry?.Phase);
        Assert.True(File.Exists(path));

        store.Complete(entry.OperationId);

        Assert.Null(store.Load().Entry);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Journal_ShouldRejectAnotherUsersCheckpoint()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "operation.json");
        var ownerStore = new FileOperationJournalStore(path, UserSid);
        _ = ownerStore.Begin(UnlockerOperationKind.Activate, true, true);

        var result = new FileOperationJournalStore(path, "S-1-5-21-2000").Load();

        Assert.False(result.Success);
        Assert.Contains("another Windows user", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Journal_ShouldFailClosedForTruncatedJson()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "operation.json");
        File.WriteAllText(path, "{\"SchemaVersion\":1,\"Entry\":");

        var result = new FileOperationJournalStore(path, UserSid).Load();

        Assert.False(result.Success);
        Assert.Null(result.Entry);
    }

    [Fact]
    public void Journal_ShouldRejectAnUnexpectedOperationIdentity()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "operation.json");
        var store = new FileOperationJournalStore(path, UserSid);
        _ = store.Begin(UnlockerOperationKind.Disable, true, true);

        Assert.Throws<InvalidOperationException>(() =>
            store.Advance(Guid.NewGuid(), UnlockerOperationPhase.RestoringStartup));
    }

    [Theory]
    [InlineData(0, 0, 1)]
    [InlineData(1, 999, 1)]
    [InlineData(1, 0, 999)]
    public void Journal_ShouldFailClosedForInvalidCheckpointData(
        int schemaVersion,
        int operation,
        int phase)
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "operation.json");
        File.WriteAllText(
            path,
            $$"""
            {
              "SchemaVersion": {{schemaVersion}},
              "Entry": {
                "OperationId": "11111111-1111-1111-1111-111111111111",
                "Operation": {{operation}},
                "Phase": {{phase}},
                "ManageFirewall": true,
                "ManageHosts": true,
                "OwnerUserSid": "{{UserSid}}",
                "StartedUtc": "2026-01-01T00:00:00Z",
                "UpdatedUtc": "2026-01-01T00:00:01Z"
              }
            }
            """);

        var result = new FileOperationJournalStore(path, UserSid).Load();

        Assert.False(result.Success);
        Assert.Null(result.Entry);
    }

    [Fact]
    public void Journal_ShouldTreatADurableCompletedMarkerAsEmptyAndCleanItUp()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "operation.json");
        File.WriteAllText(
            path,
            $$"""
            {
              "SchemaVersion": 1,
              "Entry": {
                "OperationId": "11111111-1111-1111-1111-111111111111",
                "Operation": 0,
                "Phase": {{(int)UnlockerOperationPhase.Completed}},
                "ManageFirewall": true,
                "ManageHosts": true,
                "OwnerUserSid": "{{UserSid}}",
                "StartedUtc": "2026-01-01T00:00:00Z",
                "UpdatedUtc": "2026-01-01T00:00:01Z"
              }
            }
            """);

        var result = new FileOperationJournalStore(path, UserSid).Load();

        Assert.True(result.Success, result.Error);
        Assert.Null(result.Entry);
        Assert.False(File.Exists(path));
    }
}
