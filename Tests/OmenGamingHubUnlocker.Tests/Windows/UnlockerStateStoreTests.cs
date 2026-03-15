namespace OmenGamingHubUnlocker.Tests.Windows;

public sealed class UnlockerStateStoreTests
{
    [Fact]
    public void Load_ShouldReturnEmptyState_WhenFileDoesNotExist()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var store = new UnlockerStateStore(System.IO.Path.Combine(temporaryDirectory.Path, "state.json"));

        var state = store.Load();

        Assert.Empty(state.Services);
        Assert.Empty(state.Tasks);
        Assert.Empty(state.RunEntries);
    }

    [Fact]
    public void PersistBackups_ShouldSaveAndLoadAllKindsOfEntries()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var store = new UnlockerStateStore(System.IO.Path.Combine(temporaryDirectory.Path, "state.json"));

        store.PersistBackups(
            [new ServiceBackup("svc", "Manual")],
            [new TaskBackup(@"\task", true)],
            [new RunEntryBackup(RegistryHive.CurrentUser, RegistryView.Registry64, "entry", "value")]);

        var state = store.Load();

        Assert.Single(state.Services);
        Assert.Single(state.Tasks);
        Assert.Single(state.RunEntries);
        Assert.Equal("svc", state.Services[0].Name);
        Assert.Equal(@"\task", state.Tasks[0].Path);
        Assert.Equal("entry", state.RunEntries[0].Name);
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

        var state = store.Load();

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

        store.Clear();

        Assert.False(File.Exists(stateFilePath));
    }

    [Fact]
    public void Load_ShouldReturnEmptyState_WhenFileContainsInvalidJson()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var stateFilePath = System.IO.Path.Combine(temporaryDirectory.Path, "state.json");
        File.WriteAllText(stateFilePath, "{ invalid json");

        var store = new UnlockerStateStore(stateFilePath);

        var state = store.Load();

        Assert.Empty(state.Services);
        Assert.Empty(state.Tasks);
        Assert.Empty(state.RunEntries);
    }
}
