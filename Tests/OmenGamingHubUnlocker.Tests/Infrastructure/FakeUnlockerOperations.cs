namespace OmenGamingHubUnlocker.Tests.Infrastructure;

internal sealed class FakeUnlockerOperations : IUnlockerOperations
{
    private const string PackageSid = "S-1-15-2-100";

    public List<ProcessItem> Processes { get; } = [];
    public List<ServiceItem> Services { get; } = [];
    public List<TaskItem> Tasks { get; } = [];
    public List<RunEntry> RunEntries { get; } = [];
    public List<string> Executables { get; } = [@"C:\Program Files\WindowsApps\Omen\v1\Omen.exe"];
    public List<string> Calls { get; } = [];
    public List<bool> FirewallCleanupRequests { get; } = [];
    public Queue<Action<FakeUnlockerOperations>> TargetDiscoverySteps { get; } = [];
    public Queue<Action<FakeUnlockerOperations>> FirewallInspectionSteps { get; } = [];
    public Queue<Action<FakeUnlockerOperations>> TaskQuerySteps { get; } = [];
    public Queue<Action<FakeUnlockerOperations>> ProcessQuerySteps { get; } = [];

    public AppxPackageInfo? Package { get; set; } = new(
        OmenTargets.PrimaryAppxPackageName,
        "AD2F1837.OMENCommandCenter_test",
        "AD2F1837.OMENCommandCenter_1.0.0.0_x64__test",
        @"C:\Program Files\WindowsApps\Omen\v1");

    public FirewallProtectionStatus Firewall { get; set; }
    public HostsInspection Hosts { get; set; }
    public Action<FakeUnlockerOperations>? OnReset { get; set; }
    public bool FailFirewallActivation { get; set; }
    public int FirewallActivationFailuresRemaining { get; set; }
    public int FirewallCleanupFailuresRemaining { get; set; }
    public bool FailReset { get; set; }
    public bool ThrowOnServiceQuery { get; set; }
    public bool KeepProcessesRunning { get; set; }
    public bool FailServiceRestore { get; set; }
    public bool PackageDirectoryReady { get; set; } = true;
    public IReadOnlyList<string> FirewallDiscoveryErrors { get; set; } = [];
    public UserContextStatus UserContext { get; set; } =
        new(true, @"TEST\User", @"TEST\User", string.Empty);

    public FakeUnlockerOperations()
    {
        Firewall = BuildFirewallStatus(isComplete: false);
        Hosts = BuildHostsInspection(allBlocked: false, managedLineCount: 0);
    }

    public IReadOnlyList<ProcessItem> QueryTargetProcesses()
    {
        Calls.Add("QueryProcesses");
        if (ProcessQuerySteps.TryDequeue(out var queryStep))
            queryStep(this);

        return Processes.ToList();
    }

    public IReadOnlyList<ServiceItem> QueryTargetServices()
    {
        Calls.Add("QueryServices");
        if (ThrowOnServiceQuery)
            throw new InvalidOperationException("service query failed");

        return Services.ToList();
    }

    public IReadOnlyList<TaskItem> QueryTargetTasks()
    {
        Calls.Add("QueryTasks");
        if (TaskQuerySteps.TryDequeue(out var queryStep))
            queryStep(this);

        return Tasks.ToList();
    }

    public IReadOnlyList<RunEntry> QueryTargetRunEntries()
    {
        Calls.Add("QueryRunEntries");
        return RunEntries.ToList();
    }

    public UserContextStatus InspectUserContext()
    {
        Calls.Add("InspectUserContext");
        return UserContext;
    }

    public FirewallProtectionStatus InspectFirewallProtection()
    {
        Calls.Add("InspectFirewall");
        if (FirewallInspectionSteps.TryDequeue(out var inspectionStep))
            inspectionStep(this);

        return Firewall;
    }

    public HostsInspection InspectHosts()
    {
        Calls.Add("InspectHosts");
        return Hosts;
    }

    public FirewallTargetSet DiscoverFirewallTargets()
    {
        Calls.Add("DiscoverFirewallTargets");
        if (TargetDiscoverySteps.TryDequeue(out var discoveryStep))
            discoveryStep(this);

        return BuildFirewallTargets();
    }

    public IReadOnlyList<string> DiscoverFirewallExecutables()
    {
        Calls.Add("DiscoverExecutables");
        return DiscoverFirewallTargets().AllExecutables
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public bool TryGetPrimaryPackage(out AppxPackageInfo? package, out string details)
    {
        package = Package;
        details = package?.PackageFullName ?? "not found";
        return package is not null;
    }

    public IReadOnlyList<(string Name, bool Success, string Details)> RunCapabilityChecks()
        => [("Fake platform", true, "Available")];

    public IReadOnlyList<OperationLine> SetServiceStartModes(
        IEnumerable<ServiceStartModeTarget> targets,
        bool dryRun)
    {
        Calls.Add("SetServiceModes");
        if (dryRun)
            return [Ok("Would set service modes")];

        var lines = new List<OperationLine>();
        foreach (var target in targets)
        {
            var index = Services.FindIndex(service =>
                service.Name.Equals(target.Name, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                lines.Add(Warn("Service no longer exists"));
                continue;
            }

            if (!TargetIdentityMatcher.ServiceMatches(Services[index], target.ExpectedPathName))
            {
                lines.Add(Error("Service identity changed"));
                continue;
            }

            if (FailServiceRestore && !target.DesiredStartMode.Equals("Manual", StringComparison.OrdinalIgnoreCase))
                continue;

            Services[index] = Services[index] with
            {
                StartMode = target.DesiredStartMode,
                DelayedAutoStart = target.DelayedAutoStart
            };
            lines.Add(Ok("Service mode set"));
        }

        return lines.Count == 0 ? [Ok("No service modes changed")] : lines;
    }

    public IReadOnlyList<OperationLine> StopServices(
        IEnumerable<ServiceRuntimeTarget> targets,
        bool dryRun)
    {
        Calls.Add("StopServices");
        return ChangeServiceStates(targets, "Stopped", dryRun);
    }

    public IReadOnlyList<OperationLine> StartServices(
        IEnumerable<ServiceRuntimeTarget> targets,
        bool dryRun)
    {
        Calls.Add("StartServices");
        return ChangeServiceStates(targets, "Running", dryRun);
    }

    public IReadOnlyList<OperationLine> SetTaskEnabledStates(
        IEnumerable<TaskEnableTarget> targets,
        bool dryRun)
    {
        Calls.Add("SetTaskStates");
        if (dryRun)
            return [Ok("Would set task states")];

        var lines = new List<OperationLine>();
        foreach (var target in targets)
        {
            var index = Tasks.FindIndex(task =>
                task.Path.Equals(target.Path, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                lines.Add(Warn("Task no longer exists"));
                continue;
            }

            if (!TargetIdentityMatcher.TaskMatches(Tasks[index], target.ExpectedActions))
            {
                lines.Add(Error("Task identity changed"));
                continue;
            }

            Tasks[index] = Tasks[index] with { Enabled = target.Enabled };
            lines.Add(Ok("Task state set"));
        }

        return lines.Count == 0 ? [Ok("No task states changed")] : lines;
    }

    public IReadOnlyList<OperationLine> StopTasks(
        IEnumerable<TaskRuntimeTarget> targets,
        bool dryRun)
    {
        Calls.Add("StopTasks");
        return ChangeTaskStates(targets, "Ready", dryRun);
    }

    public IReadOnlyList<OperationLine> StartTasks(
        IEnumerable<TaskRuntimeTarget> targets,
        bool dryRun)
    {
        Calls.Add("StartTasks");
        return ChangeTaskStates(targets, "Running", dryRun);
    }

    public IReadOnlyList<OperationLine> RemoveRunEntries(IEnumerable<RunEntry> entries, bool dryRun)
    {
        Calls.Add("RemoveRunEntries");
        if (!dryRun)
        {
            var identities = entries.Select(RunEntryIdentity).ToHashSet(StringComparer.OrdinalIgnoreCase);
            RunEntries.RemoveAll(entry => identities.Contains(RunEntryIdentity(entry)));
        }

        return [Ok("Run entries removed")];
    }

    public IReadOnlyList<OperationLine> RestoreRunEntries(
        IEnumerable<RunEntryBackup> entries,
        bool dryRun)
    {
        Calls.Add("RestoreRunEntries");
        var lines = new List<OperationLine>();
        foreach (var entry in entries)
        {
            var identity = $"{entry.Hive}|{entry.View}|{entry.Name}";
            var existing = RunEntries.FirstOrDefault(current =>
                RunEntryIdentity(current).Equals(identity, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                lines.Add(existing.Value.Equals(entry.Value, StringComparison.Ordinal) &&
                          existing.ValueKind == entry.ValueKind
                    ? Ok("Run entry already restored")
                    : Error("Run entry restore conflict"));
                continue;
            }

            if (!dryRun)
                RunEntries.Add(new RunEntry(entry.Hive, entry.View, entry.Name, entry.Value, entry.ValueKind));

            lines.Add(Ok(dryRun ? "Would restore Run entry" : "Run entry restored"));
        }

        return lines.Count == 0 ? [Ok("No Run entries to restore")] : lines;
    }

    public IReadOnlyList<OperationLine> TerminateTargetProcesses(bool dryRun)
    {
        Calls.Add("TerminateProcesses");
        if (!dryRun && !KeepProcessesRunning)
            Processes.Clear();

        return [Ok("Processes terminated")];
    }

    public IReadOnlyList<OperationLine> ActivateFirewall(bool dryRun, bool removeStaleRules = false)
    {
        Calls.Add("ActivateFirewall");
        FirewallCleanupRequests.Add(removeStaleRules);
        if (dryRun)
            return [Ok("Would activate firewall")];

        var cleanupFailed = removeStaleRules && FirewallCleanupFailuresRemaining > 0;
        if (FailFirewallActivation || FirewallActivationFailuresRemaining > 0 || cleanupFailed)
        {
            if (FirewallActivationFailuresRemaining > 0)
                FirewallActivationFailuresRemaining--;
            if (cleanupFailed)
                FirewallCleanupFailuresRemaining--;

            Firewall = BuildFirewallStatus(isComplete: false);
            return [Error("Firewall activation failed")];
        }

        Firewall = BuildFirewallStatus(isComplete: true);
        return [Ok("Firewall activated")];
    }

    public IReadOnlyList<OperationLine> DisableFirewall(bool dryRun)
    {
        Calls.Add("DisableFirewall");
        if (!dryRun)
            Firewall = BuildFirewallStatus(isComplete: false, noManagedRules: true);

        return [Ok("Firewall disabled")];
    }

    public IReadOnlyList<OperationLine> ActivateHosts(bool dryRun)
    {
        Calls.Add("ActivateHosts");
        if (!dryRun)
            Hosts = BuildHostsInspection(allBlocked: true, managedLineCount: OmenTargets.HostsDomains.Length);

        return [Ok("hosts activated")];
    }

    public IReadOnlyList<OperationLine> DisableHosts(bool dryRun)
    {
        Calls.Add("DisableHosts");
        if (!dryRun)
            Hosts = BuildHostsInspection(allBlocked: false, managedLineCount: 0);

        return [Ok("hosts disabled")];
    }

    public IReadOnlyList<OperationLine> ResetPackage(bool dryRun)
    {
        Calls.Add("ResetPackage");
        if (dryRun)
            return [Ok("Would reset package")];

        if (FailReset)
            return [Error("Package reset failed")];

        OnReset?.Invoke(this);
        return [Ok("Package reset")];
    }

    public FirewallProtectionStatus BuildFirewallStatus(bool isComplete, bool noManagedRules = false)
    {
        var targets = BuildFirewallTargets();
        var rules = noManagedRules
            ? []
            : isComplete
                ? Executables.Select(executable => new FirewallRuleInfo(
                    $"rule-{Path.GetFileName(executable)}",
                    true,
                    true,
                    true,
                    executable,
                    string.Empty)).Append(new FirewallRuleInfo(
                    "package-rule",
                    true,
                    true,
                    true,
                    string.Empty,
                    PackageSid)).ToList()
                : [];

        return new FirewallProtectionStatus(
            true,
            targets,
            rules,
            isComplete || noManagedRules ? [] : Executables.ToList(),
            [],
            isComplete && !noManagedRules,
            string.Empty);
    }

    public FirewallTargetSet BuildFirewallTargets(
        bool? packageDirectoryReady = null,
        IReadOnlyList<string>? discoveryErrors = null)
        => new(
            Package,
            PackageSid,
            string.Empty,
            Executables.ToHashSet(StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            packageDirectoryReady ?? PackageDirectoryReady,
            discoveryErrors ?? FirewallDiscoveryErrors);

    private static HostsInspection BuildHostsInspection(bool allBlocked, int managedLineCount)
        => new(
            true,
            OmenTargets.HostsDomains
                .Select(domain => new HostsDomainState(domain, allBlocked))
                .ToList(),
            managedLineCount,
            string.Empty);

    private List<OperationLine> ChangeServiceStates(
        IEnumerable<ServiceRuntimeTarget> targets,
        string state,
        bool dryRun)
    {
        var lines = new List<OperationLine>();
        foreach (var target in targets)
        {
            var index = Services.FindIndex(service =>
                service.Name.Equals(target.Name, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                lines.Add(Warn("Service no longer exists"));
                continue;
            }

            if (!TargetIdentityMatcher.ServiceMatches(Services[index], target.ExpectedPathName))
            {
                lines.Add(Error("Service identity changed"));
                continue;
            }

            if (!dryRun)
                Services[index] = Services[index] with { State = state };

            lines.Add(Ok("Service state changed"));
        }

        return lines.Count == 0 ? [Ok("No service states changed")] : lines;
    }

    private List<OperationLine> ChangeTaskStates(
        IEnumerable<TaskRuntimeTarget> targets,
        string state,
        bool dryRun)
    {
        var lines = new List<OperationLine>();
        foreach (var target in targets)
        {
            var index = Tasks.FindIndex(task =>
                task.Path.Equals(target.Path, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                lines.Add(Warn("Task no longer exists"));
                continue;
            }

            if (!TargetIdentityMatcher.TaskMatches(Tasks[index], target.ExpectedActions))
            {
                lines.Add(Error("Task identity changed"));
                continue;
            }

            if (!dryRun)
                Tasks[index] = Tasks[index] with { State = state };

            lines.Add(Ok("Task runtime state changed"));
        }

        return lines.Count == 0 ? [Ok("No task runtime states changed")] : lines;
    }

    private static OperationLine Ok(string text) => new() { Level = "OK", Text = text };
    private static OperationLine Warn(string text) => new() { Level = "WARN", Text = text };
    private static OperationLine Error(string text) => new() { Level = "ERR", Text = text };

    private static string RunEntryIdentity(RunEntry entry)
        => $"{entry.Hive}|{entry.View}|{entry.Name}";
}

internal sealed class InMemoryStateStore : IUnlockerStateStore
{
    public UnlockerState State { get; set; } = new();
    public bool LoadSucceeds { get; set; } = true;
    public bool ClearSucceeds { get; set; } = true;
    public bool ClearCalled { get; private set; }
    public bool ThrowOnPersist { get; set; }

    public StateLoadResult LoadState()
        => LoadSucceeds
            ? StateLoadResult.Loaded(State)
            : StateLoadResult.Failed("corrupt state");

    public void PersistBackups(
        IEnumerable<ServiceBackup> serviceBackups,
        IEnumerable<TaskBackup> taskBackups,
        IEnumerable<RunEntryBackup> runEntryBackups)
    {
        if (ThrowOnPersist)
            throw new IOException("state backup is unavailable");

        State.ActivationRecorded = true;

        MergeServices(State.Services, serviceBackups);
        MergeTasks(State.Tasks, taskBackups);
        MergeRunEntries(State.RunEntries, runEntryBackups);
    }

    public bool TryClear(out string failureDetails)
    {
        ClearCalled = true;
        failureDetails = ClearSucceeds ? string.Empty : "clear failed";
        return ClearSucceeds;
    }

    private static void MergeRunEntries(
        List<RunEntryBackup> destination,
        IEnumerable<RunEntryBackup> source)
    {
        static string Identity(RunEntryBackup backup)
            => $"{backup.Hive}|{backup.View}|{backup.Name}";

        foreach (var item in source)
        {
            var index = destination.FindIndex(existing =>
                Identity(existing).Equals(Identity(item), StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                destination.Add(item);
                continue;
            }

            if (!destination[index].Value.Equals(item.Value, StringComparison.Ordinal) ||
                destination[index].ValueKind != item.ValueKind)
            {
                destination[index] = item;
            }
        }
    }

    private static void MergeServices(
        List<ServiceBackup> destination,
        IEnumerable<ServiceBackup> source)
    {
        foreach (var item in source)
        {
            var index = destination.FindIndex(existing =>
                existing.Name.Equals(item.Name, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
                destination.Add(item);
            else if (string.IsNullOrWhiteSpace(destination[index].PathName))
                destination[index] = destination[index] with { PathName = item.PathName };
            else if (!TargetIdentityMatcher.ServiceIdentityEquals(destination[index].PathName, item.PathName))
                destination[index] = item;
        }
    }

    private static void MergeTasks(
        List<TaskBackup> destination,
        IEnumerable<TaskBackup> source)
    {
        foreach (var item in source)
        {
            var index = destination.FindIndex(existing =>
                existing.Path.Equals(item.Path, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
                destination.Add(item);
            else if (destination[index].Actions is null)
                destination[index] = destination[index] with { Actions = item.Actions };
            else if (destination[index].Actions is not null &&
                     item.Actions is not null &&
                     !TargetIdentityMatcher.TaskIdentityEquals(destination[index].Actions, item.Actions))
                destination[index] = item;
        }
    }
}

internal sealed class RecordingDelay : IOperationDelay
{
    public int WaitCount { get; private set; }

    public void Wait(TimeSpan delay)
    {
        Assert.True(delay > TimeSpan.Zero);
        WaitCount++;
    }
}

internal sealed class RecordingOperationLock : IOperationLock
{
    public bool Available { get; set; } = true;
    public int AcquireCount { get; private set; }
    public int ReleaseCount { get; private set; }

    public bool TryAcquire(out IDisposable? lease, out string failureDetails)
    {
        AcquireCount++;
        if (!Available)
        {
            lease = null;
            failureDetails = "operation already running";
            return false;
        }

        lease = new ActionScope(() => ReleaseCount++);
        failureDetails = string.Empty;
        return true;
    }

    private sealed class ActionScope(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;

        public void Dispose()
            => Interlocked.Exchange(ref _dispose, null)?.Invoke();
    }
}

internal sealed class RecordingOperationJournalStore : IOperationJournalStore
{
    public OperationJournalEntry? Entry { get; set; }
    public List<string> Calls { get; } = [];
    public UnlockerOperationPhase? FailAdvanceAt { get; set; }
    public bool FailLoad { get; set; }
    public bool FailBegin { get; set; }
    public bool FailComplete { get; set; }

    public OperationJournalLoadResult Load()
    {
        Calls.Add("Load");
        return FailLoad
            ? OperationJournalLoadResult.Failed("journal load failed")
            : Entry is null
                ? OperationJournalLoadResult.Empty()
                : OperationJournalLoadResult.Loaded(Entry);
    }

    public OperationJournalEntry Begin(
        UnlockerOperationKind operation,
        bool manageFirewall,
        bool manageHosts)
    {
        Calls.Add($"Begin:{operation}");
        if (FailBegin)
            throw new IOException("journal begin failed");

        var now = DateTimeOffset.UtcNow;
        Entry = new OperationJournalEntry(
            Guid.NewGuid(),
            operation,
            UnlockerOperationPhase.Prepared,
            manageFirewall,
            manageHosts,
            "S-1-5-21-TEST",
            now,
            now);
        return Entry;
    }

    public void Advance(Guid operationId, UnlockerOperationPhase phase)
    {
        Calls.Add($"Advance:{phase}");
        if (FailAdvanceAt == phase)
            throw new IOException("journal checkpoint failed");
        if (Entry is null || Entry.OperationId != operationId)
            throw new InvalidOperationException("journal identity mismatch");

        Entry = Entry with { Phase = phase, UpdatedUtc = DateTimeOffset.UtcNow };
    }

    public void Complete(Guid operationId)
    {
        Calls.Add("Complete");
        if (FailComplete)
            throw new IOException("journal completion failed");
        if (Entry is null || Entry.OperationId != operationId)
            throw new InvalidOperationException("journal identity mismatch");

        Entry = null;
    }
}
