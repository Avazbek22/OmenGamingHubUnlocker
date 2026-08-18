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
        Assert.True(state.ActivationRecorded);
        Assert.NotEmpty(state.OwnerUserSid);
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
        Assert.Equal("new", state.RunEntries.Single(entry => entry.Name == "entry").Value);
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

    [Fact]
    public void State_ShouldNotBeReadableOrClearableByAnotherUserSid()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var statePath = Path.Combine(temporaryDirectory.Path, "state.json");
        var ownerStore = new UnlockerStateStore(statePath, "S-1-5-21-1000");
        var otherUserStore = new UnlockerStateStore(statePath, "S-1-5-21-2000");
        ownerStore.PersistBackups([new ServiceBackup("svc", "Automatic")], [], []);

        var loadResult = otherUserStore.LoadState();
        var cleared = otherUserStore.TryClear(out var clearError);

        Assert.False(loadResult.Success);
        Assert.Contains("S-1-5-21-1000", loadResult.Error, StringComparison.Ordinal);
        Assert.False(cleared);
        Assert.NotEmpty(clearError);
        Assert.True(File.Exists(statePath));
    }

    [Fact]
    public void PersistBackups_ShouldPreserveOriginalTaskRunningState()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var store = new UnlockerStateStore(
            Path.Combine(temporaryDirectory.Path, "state.json"),
            "S-1-5-21-1000");

        store.PersistBackups([], [new TaskBackup(@"\OmenTask", true, true)], []);

        var task = Assert.Single(store.LoadState().State.Tasks);
        Assert.True(task.OriginalEnabled);
        Assert.True(task.OriginalRunning);
    }

    [Fact]
    public void PersistBackups_ShouldKeepTheFirstStateForTheSameComponentIdentity()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var store = new UnlockerStateStore(Path.Combine(temporaryDirectory.Path, "state.json"));

        store.PersistBackups(
            [new ServiceBackup("svc", "Automatic", true, PathName: @"C:\HP\Omen.exe")],
            [new TaskBackup(@"\task", true, true, ScheduledTaskRuntimeState.Running, [@"C:\HP\Task.exe"])],
            []);
        store.PersistBackups(
            [new ServiceBackup("svc", "Manual", false, PathName: @"C:\HP\Omen.exe")],
            [new TaskBackup(@"\task", false, false, ScheduledTaskRuntimeState.Ready, [@"C:\HP\Task.exe"])],
            []);

        var state = store.LoadState().State;
        Assert.Equal("Automatic", Assert.Single(state.Services).OriginalStartMode);
        Assert.True(Assert.Single(state.Tasks).OriginalEnabled);
    }

    [Fact]
    public void PersistBackups_ShouldReplaceStateWhenAnUpdateReusesTheLogicalName()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var store = new UnlockerStateStore(Path.Combine(temporaryDirectory.Path, "state.json"));

        store.PersistBackups(
            [new ServiceBackup("svc", "Automatic", true, PathName: @"C:\HP\v1\Omen.exe")],
            [new TaskBackup(@"\task", true, true, ScheduledTaskRuntimeState.Running, [@"C:\HP\v1\Task.exe"])],
            []);
        store.PersistBackups(
            [new ServiceBackup("svc", "Manual", false, PathName: @"C:\HP\v2\Omen.exe")],
            [new TaskBackup(@"\task", false, false, ScheduledTaskRuntimeState.Ready, [@"C:\HP\v2\Task.exe"])],
            []);

        var state = store.LoadState().State;
        var service = Assert.Single(state.Services);
        var task = Assert.Single(state.Tasks);
        Assert.Equal(@"C:\HP\v2\Omen.exe", service.PathName);
        Assert.Equal("Manual", service.OriginalStartMode);
        Assert.Equal([@"C:\HP\v2\Task.exe"], task.ActionPaths);
        Assert.False(task.OriginalEnabled);
    }

    [Fact]
    public void LoadState_ShouldRejectOwnerlessLegacyRollbackWhenFileOwnershipCannotBeVerified()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var statePath = Path.Combine(temporaryDirectory.Path, "state.json");
        File.WriteAllText(
            statePath,
            """{"SchemaVersion":3,"Services":[{"Name":"svc","OriginalStartMode":"Auto"}],"Tasks":[],"RunEntries":[]}""");

        var result = new UnlockerStateStore(statePath, "S-1-5-21-NOT-THE-FILE-OWNER").LoadState();

        Assert.False(result.Success);
        Assert.Contains("owner", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PersistBackups_ShouldPreserveTheExactTaskRuntimeState()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var store = new UnlockerStateStore(Path.Combine(temporaryDirectory.Path, "state.json"));

        store.PersistBackups(
            [],
            [new TaskBackup(@"\OmenTask", false, false, ScheduledTaskRuntimeState.Queued, [])],
            []);

        var task = Assert.Single(store.LoadState().State.Tasks);
        Assert.Equal(ScheduledTaskRuntimeState.Queued, task.EffectiveOriginalRuntimeState);
    }

    [Fact]
    public void PersistBackups_ShouldReplaceRunEntryWhenAnUpdateReusesItsRegistryIdentity()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var store = new UnlockerStateStore(Path.Combine(temporaryDirectory.Path, "state.json"));

        store.PersistBackups(
            [],
            [],
            [new RunEntryBackup(RegistryHive.CurrentUser, RegistryView.Registry64, "OMEN", @"C:\HP\v1.exe")]);
        store.PersistBackups(
            [],
            [],
            [new RunEntryBackup(RegistryHive.CurrentUser, RegistryView.Registry64, "OMEN", @"C:\HP\v2.exe")]);

        Assert.Equal(@"C:\HP\v2.exe", Assert.Single(store.LoadState().State.RunEntries).Value);
    }

    [Theory]
    [InlineData("{\"SchemaVersion\":6,\"OwnerUserSid\":\"S-1-5-21-1000\",\"Services\":[{\"Name\":\"\",\"OriginalStartMode\":\"Auto\"}],\"Tasks\":[],\"RunEntries\":[]}")]
    [InlineData("{\"SchemaVersion\":6,\"OwnerUserSid\":\"S-1-5-21-1000\",\"Services\":[],\"Tasks\":[{\"Path\":\"\\\\OMEN\",\"OriginalEnabled\":true,\"OriginalRuntimeState\":999}],\"RunEntries\":[]}")]
    [InlineData("{\"SchemaVersion\":6,\"OwnerUserSid\":\"S-1-5-21-1000\",\"Services\":[],\"Tasks\":[],\"RunEntries\":[{\"Hive\":2147483650,\"View\":256,\"Name\":\"OMEN\",\"Value\":\"x\",\"ValueKind\":4}]}")]
    public void LoadState_ShouldFailClosedForSemanticallyInvalidRollbackData(string json)
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var statePath = Path.Combine(temporaryDirectory.Path, "state.json");
        File.WriteAllText(statePath, json);

        var result = new UnlockerStateStore(statePath, "S-1-5-21-1000").LoadState();

        Assert.False(result.Success);
        Assert.NotEmpty(result.Error);
    }
}
