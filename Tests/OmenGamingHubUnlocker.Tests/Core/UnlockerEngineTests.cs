namespace OmenGamingHubUnlocker.Tests.Core;

public sealed class UnlockerEngineTests
{
    [Fact]
    public void Activate_ShouldIsolateNetworkBeforeStoppingStartupComponents()
    {
        var (engine, operations, _, _) = CreateEngineWithActiveOmen();

        var report = engine.Activate(UnlockerOptions.ForActivate());

        Assert.True(report.Success);
        AssertBefore(operations.Calls, "ActivateFirewall", "SetServiceModes");
        AssertBefore(operations.Calls, "ActivateFirewall", "SetTaskStates");
        AssertBefore(operations.Calls, "ActivateFirewall", "TerminateProcesses");
        Assert.Empty(operations.Processes);
        Assert.All(operations.Services, service =>
        {
            Assert.Equal("Manual", service.StartMode);
            Assert.Equal("Stopped", service.State);
        });
        Assert.All(operations.Tasks, task =>
        {
            Assert.False(task.Enabled);
            Assert.False(task.IsRunning);
        });
        Assert.Empty(operations.RunEntries);
        Assert.True(operations.Firewall.IsComplete);
        Assert.True(operations.Hosts.AllBlocked);
    }

    [Fact]
    public void Activate_ShouldPersistOriginalRunningAndStartupState()
    {
        var (engine, _, stateStore, _) = CreateEngineWithActiveOmen();

        var report = engine.Activate(UnlockerOptions.ForActivate());

        Assert.True(report.Success);
        var service = Assert.Single(stateStore.State.Services);
        Assert.Equal("Automatic", service.OriginalStartMode);
        Assert.True(service.OriginalRunning);
        Assert.True(Assert.Single(stateStore.State.Tasks).OriginalEnabled);
        Assert.Single(stateStore.State.RunEntries);
    }

    [Fact]
    public void Activate_ShouldPersistDelayedAutoStart()
    {
        var (engine, operations, stateStore, _) = CreateEngineWithActiveOmen();
        operations.Services[0] = operations.Services[0] with
        {
            StartMode = "Auto",
            DelayedAutoStart = true
        };

        var report = engine.Activate(UnlockerOptions.ForActivate());

        Assert.True(report.Success);
        var service = Assert.Single(stateStore.State.Services);
        Assert.Equal("Auto", service.OriginalStartMode);
        Assert.True(service.OriginalDelayedAutoStart);
    }

    [Fact]
    public void Activate_ShouldStopServiceThatIsStillTransitioning()
    {
        var (engine, operations, _, _) = CreateEngineWithActiveOmen();
        operations.Services[0] = operations.Services[0] with { State = "Stop Pending" };

        var report = engine.Activate(UnlockerOptions.ForActivate());

        Assert.True(report.Success);
        Assert.Contains("StopServices", operations.Calls);
        Assert.Equal("Stopped", Assert.Single(operations.Services).State);
    }

    [Fact]
    public void Activate_ShouldStopAQueuedScheduledTask()
    {
        var (engine, operations, _, _) = CreateEngineWithActiveOmen();
        operations.Tasks[0] = operations.Tasks[0] with { Enabled = false, State = "Queued" };

        var report = engine.Activate(UnlockerOptions.ForActivate());

        Assert.True(report.Success);
        Assert.Contains("StopTasks", operations.Calls);
        Assert.False(Assert.Single(operations.Tasks).RequiresStop);
    }

    [Fact]
    public void Activate_ShouldFail_WhenFirewallCannotBeVerified()
    {
        var (engine, operations, _, _) = CreateEngineWithActiveOmen();
        operations.FailFirewallActivation = true;

        var report = engine.Activate(UnlockerOptions.ForActivate());

        Assert.False(report.Success);
        Assert.Contains(report.Lines, line =>
            line.Level == "ERR" &&
            line.Text.Contains("firewall", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("SetServiceModes", operations.Calls);
        Assert.DoesNotContain("SetTaskStates", operations.Calls);
        Assert.DoesNotContain("TerminateProcesses", operations.Calls);
    }

    [Fact]
    public void Activate_ShouldFail_WhenTargetProcessKeepsRespawning()
    {
        var (engine, operations, _, delay) = CreateEngineWithActiveOmen();
        operations.KeepProcessesRunning = true;

        var report = engine.Activate(UnlockerOptions.ForActivate());

        Assert.False(report.Success);
        Assert.Equal(8, delay.WaitCount);
        Assert.Contains(report.Lines, line =>
            line.Level == "ERR" &&
            line.Text.Contains("stable", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Activate_ShouldRequireTwoConsecutiveStableSnapshots()
    {
        var (engine, _, _, delay) = CreateEngineWithActiveOmen();

        var report = engine.Activate(UnlockerOptions.ForActivate());

        Assert.True(report.Success);
        Assert.Equal(2, delay.WaitCount);
    }

    [Fact]
    public void Activate_ShouldFail_WhenDiscoveryThrows()
    {
        var (engine, operations, _, _) = CreateEngineWithActiveOmen();
        operations.ThrowOnServiceQuery = true;

        var report = engine.Activate(UnlockerOptions.ForActivate());

        Assert.False(report.Success);
        Assert.Contains(report.Lines, line =>
            line.Level == "ERR" &&
            line.Text.Contains("service", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("ActivateFirewall", operations.Calls);
        Assert.DoesNotContain("SetTaskStates", operations.Calls);
        Assert.DoesNotContain("TerminateProcesses", operations.Calls);
    }

    [Fact]
    public void Activate_ShouldAbortBeforeMutation_WhenRollbackStateCannotBeSaved()
    {
        var (engine, operations, stateStore, _) = CreateEngineWithActiveOmen();
        stateStore.ThrowOnPersist = true;

        var report = engine.Activate(UnlockerOptions.ForActivate());

        Assert.False(report.Success);
        Assert.Contains(report.Lines, line =>
            line.Level == "ERR" &&
            line.Text.Contains("backup", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("ActivateFirewall", operations.Calls);
        Assert.DoesNotContain("SetServiceModes", operations.Calls);
        Assert.DoesNotContain("SetTaskStates", operations.Calls);
        Assert.DoesNotContain("TerminateProcesses", operations.Calls);
    }

    [Fact]
    public void Activate_ShouldAbortBeforeMutation_WhenElevationUsesAnotherAccount()
    {
        var (engine, operations, _, _) = CreateEngineWithActiveOmen();
        operations.UserContext = new UserContextStatus(
            true,
            @"PC\Administrator",
            @"PC\StandardUser",
            string.Empty);

        var report = engine.Activate(UnlockerOptions.ForActivate());

        Assert.False(report.Success);
        Assert.DoesNotContain("ActivateFirewall", operations.Calls);
        Assert.DoesNotContain("SetServiceModes", operations.Calls);
        Assert.NotEmpty(operations.Processes);
    }

    [Fact]
    public void Reset_ShouldRunOnlyAfterVerifiedNetworkIsolation()
    {
        var (engine, operations, _, _) = CreateEngineWithActiveOmen();

        var report = engine.ResetAndReapply(UnlockerOptions.ForResetAndReapply());

        Assert.True(report.Success);
        AssertBefore(operations.Calls, "ActivateFirewall", "ResetPackage");
        Assert.True(operations.Calls.Count(call => call == "ActivateFirewall") >= 2);
    }

    [Fact]
    public void Reset_ShouldAbort_WhenPreResetIsolationFails()
    {
        var (engine, operations, _, _) = CreateEngineWithActiveOmen();
        operations.FailFirewallActivation = true;

        var report = engine.ResetAndReapply(UnlockerOptions.ForResetAndReapply());

        Assert.False(report.Success);
        Assert.DoesNotContain("ResetPackage", operations.Calls);
    }

    [Fact]
    public void Reset_ShouldRediscoverAndConstrainComponentsCreatedByNewVersion()
    {
        var (engine, operations, _, _) = CreateEngineWithActiveOmen();
        operations.OnReset = platform =>
        {
            platform.Package = platform.Package! with
            {
                PackageFullName = "AD2F1837.OMENCommandCenter_2.0.0.0_x64__test",
                InstallLocation = @"C:\Program Files\WindowsApps\Omen\v2"
            };
            platform.Executables.Clear();
            platform.Executables.Add(@"C:\Program Files\WindowsApps\Omen\v2\NewBackground.exe");
            platform.Processes.Add(new ProcessItem(20, "NewBackground", platform.Executables[0]));
            platform.Services.Add(new ServiceItem("NewOmenService", "New OMEN Service", "Automatic", "Running"));
            platform.Tasks.Add(new TaskItem(@"\NewOmenTask", true, "Running"));
        };

        var report = engine.ResetAndReapply(UnlockerOptions.ForResetAndReapply());

        Assert.True(report.Success);
        Assert.Empty(operations.Processes);
        Assert.All(operations.Services, service => Assert.Equal("Manual", service.StartMode));
        Assert.All(operations.Tasks, task => Assert.False(task.Enabled));
        Assert.Contains(
            @"C:\Program Files\WindowsApps\Omen\v2\NewBackground.exe",
            operations.Firewall.Targets.AllExecutables);
    }

    [Fact]
    public void Reset_ShouldRemainProtected_WhenPackageResetFails()
    {
        var (engine, operations, _, _) = CreateEngineWithActiveOmen();
        operations.FailReset = true;

        var report = engine.ResetAndReapply(UnlockerOptions.ForResetAndReapply());

        Assert.False(report.Success);
        Assert.True(operations.Firewall.IsComplete);
        Assert.True(operations.Hosts.AllBlocked);
    }

    [Fact]
    public void Disable_ShouldRestoreExactStateBeforeRemovingNetworkProtection()
    {
        var operations = CreateTamedOperations();
        var stateStore = new InMemoryStateStore
        {
            State = new UnlockerState
            {
                Services = [new ServiceBackup("HPOmenCap", "Automatic", true)],
                Tasks = [new TaskBackup(@"\OmenTask", true)],
                RunEntries =
                [
                    new RunEntryBackup(
                        RegistryHive.CurrentUser,
                        RegistryView.Registry64,
                        "OmenBackground",
                        "Omen.exe")
                ]
            }
        };
        var engine = new UnlockerEngine(operations, stateStore, new RecordingDelay());

        var report = engine.Disable(UnlockerOptions.ForDisable());

        Assert.True(report.Success);
        AssertBefore(operations.Calls, "SetServiceModes", "DisableFirewall");
        AssertBefore(operations.Calls, "RestoreRunEntries", "DisableFirewall");
        Assert.Equal("Automatic", Assert.Single(operations.Services).StartMode);
        Assert.Equal("Running", Assert.Single(operations.Services).State);
        Assert.True(Assert.Single(operations.Tasks).Enabled);
        Assert.Single(operations.RunEntries);
        Assert.Equal(0, operations.Firewall.RuleCount);
        Assert.Equal(0, operations.Hosts.ManagedLineCount);
        Assert.True(stateStore.ClearCalled);
        Assert.DoesNotContain(report.SnapshotsAfter, snapshot => snapshot.Result == "WARN");
        Assert.DoesNotContain(report.SnapshotsAfter, snapshot => snapshot.Result == "ERR");
    }

    [Fact]
    public void Disable_ShouldKeepNetworkProtectionAndBackup_WhenRestoreFails()
    {
        var operations = CreateTamedOperations();
        operations.FailServiceRestore = true;
        var stateStore = new InMemoryStateStore
        {
            State = new UnlockerState
            {
                Services = [new ServiceBackup("HPOmenCap", "Automatic", true)]
            }
        };
        var engine = new UnlockerEngine(operations, stateStore, new RecordingDelay());

        var report = engine.Disable(UnlockerOptions.ForDisable());

        Assert.False(report.Success);
        Assert.DoesNotContain("DisableFirewall", operations.Calls);
        Assert.DoesNotContain("DisableHosts", operations.Calls);
        Assert.True(operations.Firewall.IsComplete);
        Assert.True(operations.Hosts.AllBlocked);
        Assert.False(stateStore.ClearCalled);
    }

    [Fact]
    public void Disable_ShouldAbortSafely_WhenRollbackStateIsCorrupt()
    {
        var operations = CreateTamedOperations();
        var stateStore = new InMemoryStateStore { LoadSucceeds = false };
        var engine = new UnlockerEngine(operations, stateStore, new RecordingDelay());

        var report = engine.Disable(UnlockerOptions.ForDisable());

        Assert.False(report.Success);
        Assert.DoesNotContain("DisableFirewall", operations.Calls);
        Assert.False(stateStore.ClearCalled);
    }

    [Fact]
    public void DryRun_ShouldNotMutateDiscoveredState()
    {
        var (engine, operations, stateStore, _) = CreateEngineWithActiveOmen();
        var originalService = operations.Services.Single();

        var report = engine.Activate(UnlockerOptions.ForDryRun());

        Assert.True(report.Success);
        Assert.Equal(originalService, operations.Services.Single());
        Assert.NotEmpty(operations.Processes);
        Assert.NotEmpty(operations.RunEntries);
        Assert.Empty(stateStore.State.Services);
    }

    [Fact]
    public void Status_ShouldWarnForRulesThatCoverOnlyAnOldVersion()
    {
        var operations = CreateTamedOperations();
        var targets = operations.Firewall.Targets;
        operations.Firewall = new FirewallProtectionStatus(
            true,
            targets,
            [
                new FirewallRuleInfo(
                    "old",
                    true,
                    true,
                    true,
                    @"C:\Program Files\WindowsApps\Omen\v0\Omen.exe",
                    string.Empty)
            ],
            targets.AllExecutables.ToList(),
            [@"C:\Program Files\WindowsApps\Omen\v0\Omen.exe"],
            false,
            string.Empty);
        var engine = new UnlockerEngine(operations, new InMemoryStateStore(), new RecordingDelay());

        var report = engine.GetStatusReport();

        var firewall = Assert.Single(report.Snapshots, snapshot => snapshot.Area == "Firewall");
        Assert.Equal("WARN", firewall.Result);
        Assert.Contains("missing=1", firewall.Current);
        Assert.Contains("stale=1", firewall.Current);
    }

    private static (
        UnlockerEngine Engine,
        FakeUnlockerOperations Operations,
        InMemoryStateStore StateStore,
        RecordingDelay Delay) CreateEngineWithActiveOmen()
    {
        var operations = new FakeUnlockerOperations();
        operations.Processes.Add(new ProcessItem(10, "OmenCommandCenterBackground", operations.Executables[0]));
        operations.Services.Add(new ServiceItem("HPOmenCap", "HP Omen HSA Service", "Automatic", "Running"));
        operations.Tasks.Add(new TaskItem(@"\OmenTask", true, "Running"));
        operations.RunEntries.Add(new RunEntry(
            RegistryHive.CurrentUser,
            RegistryView.Registry64,
            "OmenBackground",
            "Omen.exe"));

        var stateStore = new InMemoryStateStore();
        var delay = new RecordingDelay();
        return (new UnlockerEngine(operations, stateStore, delay), operations, stateStore, delay);
    }

    private static FakeUnlockerOperations CreateTamedOperations()
    {
        var operations = new FakeUnlockerOperations();
        operations.Services.Add(new ServiceItem("HPOmenCap", "HP Omen HSA Service", "Manual", "Stopped"));
        operations.Tasks.Add(new TaskItem(@"\OmenTask", false, "Ready"));
        operations.Firewall = operations.BuildFirewallStatus(isComplete: true);
        operations.ActivateHosts(dryRun: false);
        operations.Calls.Clear();
        return operations;
    }

    private static void AssertBefore(IReadOnlyList<string> calls, string first, string second)
    {
        var recordedCalls = calls.ToList();
        var firstIndex = recordedCalls.IndexOf(first);
        var secondIndex = recordedCalls.IndexOf(second);
        Assert.True(firstIndex >= 0, $"Call '{first}' was not recorded.");
        Assert.True(secondIndex >= 0, $"Call '{second}' was not recorded.");
        Assert.True(firstIndex < secondIndex, $"Expected '{first}' before '{second}'.");
    }
}
