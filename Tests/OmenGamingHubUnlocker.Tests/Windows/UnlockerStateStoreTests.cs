namespace OmenGamingHubUnlocker.Tests.Windows;

public sealed class UnlockerStateStoreTests
{
    [Fact]
    public void Load_ShouldReturnEmptyState_WhenFileDoesNotExist()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var store = new UnlockerStateStore(System.IO.Path.Combine(temporaryDirectory.Path, "state.json"));

        var state = store.LoadState().State;

        Assert.Empty(state.Services);
        Assert.Empty(state.Tasks);
        Assert.Empty(state.RunEntries);
        Assert.Equal(UnlockerStateStore.CurrentSchemaVersion, state.SchemaVersion);
    }

    [Fact]
    public void PersistBackups_ShouldSaveAndLoadAllKindsOfEntries()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var store = new UnlockerStateStore(System.IO.Path.Combine(temporaryDirectory.Path, "state.json"));

        store.PersistBackups(
            [new ServiceBackup("svc", "Manual", true)],
            [new TaskBackup(@"\task", true)],
            [
                new RunEntryBackup(
                    RegistryHive.CurrentUser,
                    RegistryView.Registry64,
                    "entry",
                    "%LOCALAPPDATA%\\Omen.exe",
                    RegistryValueKind.ExpandString)
            ]);

        var state = store.LoadState().State;

        Assert.Single(state.Services);
        Assert.Single(state.Tasks);
        Assert.Single(state.RunEntries);
        Assert.Equal("svc", state.Services[0].Name);
        Assert.True(state.Services[0].OriginalRunning);
        Assert.Equal(@"\task", state.Tasks[0].Path);
        Assert.Equal("entry", state.RunEntries[0].Name);
        Assert.Equal("%LOCALAPPDATA%\\Omen.exe", state.RunEntries[0].Value);
        Assert.Equal(RegistryValueKind.ExpandString, state.RunEntries[0].ValueKind);
    }

    [Fact]
    public void PersistBackups_ShouldMergeWithoutDuplicatingExistingEntries()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var store = new UnlockerStateStore(System.IO.Path.Combine(temporaryDirectory.Path, "state.json"));

        store.PersistBackups(
            [new ServiceBackup("svc", "Manual")],
            [new TaskBackup(@"\task", true)],
            [new RunEntryBackup(RegistryHive.CurrentUser, RegistryView.Registry64, "entry", "value")]);

        store.PersistBackups(
            [new ServiceBackup("svc", "Automatic"), new ServiceBackup("svc2", "Manual")],
            [new TaskBackup(@"\task", false), new TaskBackup(@"\task2", false)],
            [new RunEntryBackup(RegistryHive.CurrentUser, RegistryView.Registry64, "entry", "new"), new RunEntryBackup(RegistryHive.LocalMachine, RegistryView.Registry32, "entry2", "value2")]);

        var state = store.LoadState().State;

        Assert.Equal(2, state.Services.Count);
        Assert.Equal(2, state.Tasks.Count);
        Assert.Equal(2, state.RunEntries.Count);
    }

    [Fact]
    public void Clear_ShouldDeleteExistingStateFile()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var stateFilePath = System.IO.Path.Combine(temporaryDirectory.Path, "state.json");
        var store = new UnlockerStateStore(stateFilePath);

        store.PersistBackups(
            [new ServiceBackup("svc", "Manual")],
            [],
            []);

        Assert.True(File.Exists(stateFilePath));

        Assert.True(store.TryClear(out var failureDetails), failureDetails);

        Assert.False(File.Exists(stateFilePath));
    }

    [Fact]
    public void LoadState_ShouldReturnFailure_WhenFileContainsInvalidJson()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var stateFilePath = System.IO.Path.Combine(temporaryDirectory.Path, "state.json");
        File.WriteAllText(stateFilePath, "{ invalid json");

        var store = new UnlockerStateStore(stateFilePath);

        var result = store.LoadState();

        Assert.False(result.Success);
        Assert.NotEmpty(result.Error);
    }

    [Fact]
    public void LoadState_ShouldUpgradeAnOlderSupportedSchema()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var stateFilePath = Path.Combine(temporaryDirectory.Path, "state.json");
        File.WriteAllText(
            stateFilePath,
            """{"SchemaVersion":3,"Services":[],"Tasks":[],"RunEntries":[]}""");
        var store = new UnlockerStateStore(stateFilePath);

        var result = store.LoadState();

        Assert.True(result.Success);
        Assert.Equal(UnlockerStateStore.CurrentSchemaVersion, result.State.SchemaVersion);
    }

    [Fact]
    public void PersistBackups_ShouldKeepFirstObservedState()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var store = new UnlockerStateStore(Path.Combine(temporaryDirectory.Path, "state.json"));

        store.PersistBackups([new ServiceBackup("svc", "Automatic", true)], [], []);
        store.PersistBackups([new ServiceBackup("svc", "Manual", false)], [], []);

        var backup = Assert.Single(store.LoadState().State.Services);
        Assert.Equal("Automatic", backup.OriginalStartMode);
        Assert.True(backup.OriginalRunning);
    }

    [Fact]
    public void PersistBackups_ShouldNotLeaveTemporaryFiles()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var statePath = Path.Combine(temporaryDirectory.Path, "state.json");
        var store = new UnlockerStateStore(statePath);

        store.PersistBackups([new ServiceBackup("svc", "Manual")], [], []);

        Assert.True(File.Exists(statePath));
        Assert.DoesNotContain(
            Directory.EnumerateFiles(temporaryDirectory.Path),
            path => Path.GetFileName(path).StartsWith(".state.json.", StringComparison.Ordinal));
    }

    [Fact]
    public void PersistBackups_ShouldNotLoseConcurrentUpdates()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var statePath = Path.Combine(temporaryDirectory.Path, "state.json");
        var stores = Enumerable.Range(0, 12)
            .Select(_ => new UnlockerStateStore(statePath))
            .ToList();

        Parallel.ForEach(
            stores.Select((store, index) => (store, index)),
            item => item.store.PersistBackups(
                [new ServiceBackup($"svc-{item.index}", "Manual")],
                [],
                []));

        var state = stores[0].LoadState().State;
        Assert.Equal(stores.Count, state.Services.Count);
        Assert.Equal(
            stores.Count,
            state.Services.Select(service => service.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }
}
