namespace OmenGamingHubUnlocker.Tests.Integration;

public sealed class RecoveryAndPersistenceE2ETests
{
    private const string UserSid = "S-1-5-21-1000";

    [Theory]
    [InlineData(UnlockerOperationPhase.Prepared)]
    [InlineData(UnlockerOperationPhase.ApplyingNetworkIsolation)]
    [InlineData(UnlockerOperationPhase.ApplyingStartupConstraints)]
    [InlineData(UnlockerOperationPhase.ResettingPackage)]
    [InlineData(UnlockerOperationPhase.ReapplyingAfterReset)]
    [InlineData(UnlockerOperationPhase.RestoringStartup)]
    [InlineData(UnlockerOperationPhase.RemovingNetworkIsolation)]
    [InlineData(UnlockerOperationPhase.Verifying)]
    public void ANewProcess_ShouldRecoverEveryInterruptedCheckpointBeforeMutatingStartup(
        UnlockerOperationPhase interruptedPhase)
    {
        using var directory = new TemporaryDirectory();
        var journalPath = Path.Combine(directory.Path, "operation.json");
        var crashedProcess = E2EHostRunner.Run(
            "journal-crash",
            journalPath,
            UserSid,
            interruptedPhase.ToString());

        Assert.Equal(137, crashedProcess.ExitCode);
        Assert.True(Guid.TryParse(crashedProcess.StandardOutput, out _));
        Assert.Equal(string.Empty, crashedProcess.StandardError);

        var operations = new FakeUnlockerOperations();
        var engine = new UnlockerEngine(
            operations,
            new InMemoryStateStore(),
            new RecordingDelay(),
            new RecordingOperationLock(),
            new FileOperationJournalStore(journalPath, UserSid));

        var report = engine.Activate(UnlockerOptions.ForActivate());

        Assert.True(report.Success, string.Join(Environment.NewLine, report.Lines.Select(line => line.Text)));
        Assert.False(File.Exists(journalPath));
        Assert.True(
            operations.Calls.IndexOf("ActivateFirewall") < operations.Calls.IndexOf("SetServiceModes"),
            string.Join(", ", operations.Calls));
    }

    [Fact]
    public void ResetThenDisable_ShouldRestoreThePostUpdateComponentGenerationAcrossEngineInstances()
    {
        using var directory = new TemporaryDirectory();
        var statePath = Path.Combine(directory.Path, "state.json");
        var journalPath = Path.Combine(directory.Path, "operation.json");
        var operations = CreateVersionOnePlatform();
        operations.OnReset = UpgradePlatformToVersionTwo;
        var stateStore = new UnlockerStateStore(statePath, UserSid);
        var journalStore = new FileOperationJournalStore(journalPath, UserSid);

        var resetReport = new UnlockerEngine(
                operations,
                stateStore,
                new RecordingDelay(),
                new RecordingOperationLock(),
                journalStore)
            .ResetAndReapply(UnlockerOptions.ForResetAndReapply());

        Assert.True(resetReport.Success, FormatReport(resetReport));
        var persistedState = stateStore.LoadState();
        Assert.True(persistedState.Success, persistedState.Error);
        Assert.Equal(@"C:\HP\Omen\v2\service.exe", Assert.Single(persistedState.State.Services).PathName);
        Assert.Equal([@"C:\HP\Omen\v2\task.exe"], Assert.Single(persistedState.State.Tasks).ActionPaths);
        Assert.Equal(@"C:\HP\Omen\v2\startup.exe", Assert.Single(persistedState.State.RunEntries).Value);

        var disableReport = new UnlockerEngine(
                operations,
                new UnlockerStateStore(statePath, UserSid),
                new RecordingDelay(),
                new RecordingOperationLock(),
                new FileOperationJournalStore(journalPath, UserSid))
            .Disable(UnlockerOptions.ForDisable());

        Assert.True(disableReport.Success, FormatReport(disableReport));
        var service = Assert.Single(operations.Services);
        var task = Assert.Single(operations.Tasks);
        Assert.Equal("Automatic", service.StartMode);
        Assert.Equal("Running", service.State);
        Assert.Equal(@"C:\HP\Omen\v2\service.exe", service.PathName);
        Assert.True(task.Enabled);
        Assert.Equal("Running", task.State);
        Assert.Equal([@"C:\HP\Omen\v2\task.exe"], task.ActionPaths);
        Assert.Equal(@"C:\HP\Omen\v2\startup.exe", Assert.Single(operations.RunEntries).Value);
        Assert.False(File.Exists(statePath));
        Assert.False(File.Exists(journalPath));
    }

    [Fact]
    public async Task ConcurrentProcesses_ShouldMergeRollbackStateWithoutLostUpdates()
    {
        using var directory = new TemporaryDirectory();
        var statePath = Path.Combine(directory.Path, "state.json");
        var identities = Enumerable.Range(1, 12)
            .Select(index => index.ToString("D2", CultureInfo.InvariantCulture))
            .ToArray();

        var processResults = await Task.WhenAll(identities.Select(identity => Task.Run(() =>
            E2EHostRunner.Run("state-merge", statePath, UserSid, identity))));

        Assert.All(processResults, result =>
        {
            Assert.Equal(0, result.ExitCode);
            Assert.Equal(string.Empty, result.StandardError);
        });

        var loaded = new UnlockerStateStore(statePath, UserSid).LoadState();
        Assert.True(loaded.Success, loaded.Error);
        Assert.Equal(identities.Length, loaded.State.Services.Count);
        Assert.Equal(identities.Length, loaded.State.Tasks.Count);
        Assert.Equal(identities.Length, loaded.State.RunEntries.Count);
        Assert.All(identities, identity =>
        {
            Assert.Contains(loaded.State.Services, service => service.Name == $"service-{identity}");
            Assert.Contains(loaded.State.Tasks, task => task.Path == $@"\task-{identity}");
            Assert.Contains(loaded.State.RunEntries, entry => entry.Name == $"run-{identity}");
        });
    }

    [Fact]
    public void CorruptJournal_ShouldAbortBeforeAnyMachineMutation()
    {
        using var directory = new TemporaryDirectory();
        var journalPath = Path.Combine(directory.Path, "operation.json");
        File.WriteAllText(journalPath, "{\"SchemaVersion\":1,\"Entry\":");
        var operations = new FakeUnlockerOperations();
        var engine = new UnlockerEngine(
            operations,
            new InMemoryStateStore(),
            new RecordingDelay(),
            new RecordingOperationLock(),
            new FileOperationJournalStore(journalPath, UserSid));

        var report = engine.Activate(UnlockerOptions.ForActivate());

        Assert.False(report.Success);
        Assert.DoesNotContain("ActivateFirewall", operations.Calls);
        Assert.DoesNotContain("ActivateHosts", operations.Calls);
        Assert.DoesNotContain("SetServiceModes", operations.Calls);
        Assert.DoesNotContain("SetTaskStates", operations.Calls);
        Assert.True(File.Exists(journalPath));
    }

    private static FakeUnlockerOperations CreateVersionOnePlatform()
    {
        var operations = new FakeUnlockerOperations();
        operations.Executables.Clear();
        operations.Executables.Add(@"C:\HP\Omen\v1\app.exe");
        operations.Services.Add(new ServiceItem(
            "HPOmenCap",
            "HP OMEN Service",
            "Automatic",
            "Running",
            @"C:\HP\Omen\v1\service.exe"));
        operations.Tasks.Add(new TaskItem(
            @"\OmenTask",
            true,
            "Running",
            [@"C:\HP\Omen\v1\task.exe"]));
        operations.RunEntries.Add(new RunEntry(
            RegistryHive.CurrentUser,
            RegistryView.Registry64,
            "OmenStartup",
            @"C:\HP\Omen\v1\startup.exe"));
        return operations;
    }

    private static void UpgradePlatformToVersionTwo(FakeUnlockerOperations operations)
    {
        operations.Executables.Clear();
        operations.Executables.Add(@"C:\HP\Omen\v2\app.exe");
        operations.Package = new AppxPackageInfo(
            OmenTargets.PrimaryAppxPackageName,
            "AD2F1837.OMENCommandCenter_test",
            "AD2F1837.OMENCommandCenter_2.0.0.0_x64__test",
            @"C:\HP\Omen\v2");
        operations.Services.Clear();
        operations.Services.Add(new ServiceItem(
            "HPOmenCap",
            "HP OMEN Service",
            "Automatic",
            "Running",
            @"C:\HP\Omen\v2\service.exe"));
        operations.Tasks.Clear();
        operations.Tasks.Add(new TaskItem(
            @"\OmenTask",
            true,
            "Running",
            [@"C:\HP\Omen\v2\task.exe"]));
        operations.RunEntries.Clear();
        operations.RunEntries.Add(new RunEntry(
            RegistryHive.CurrentUser,
            RegistryView.Registry64,
            "OmenStartup",
            @"C:\HP\Omen\v2\startup.exe"));
    }

    private static string FormatReport(OperationReport report)
        => string.Join(Environment.NewLine, report.Lines.Select(line => $"{line.Level}: {line.Text}"));
}
