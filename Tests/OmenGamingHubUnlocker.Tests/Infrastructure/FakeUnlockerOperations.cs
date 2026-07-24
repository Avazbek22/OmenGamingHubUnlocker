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

    public AppxPackageInfo? Package { get; set; } = new(
        OmenTargets.PrimaryAppxPackageName,
        "AD2F1837.OMENCommandCenter_test",
        "AD2F1837.OMENCommandCenter_1.0.0.0_x64__test",
        @"C:\Program Files\WindowsApps\Omen\v1");

    public FirewallProtectionStatus Firewall { get; set; }
    public HostsInspection Hosts { get; set; }
    public Action<FakeUnlockerOperations>? OnReset { get; set; }
    public bool FailFirewallActivation { get; set; }
    public bool FailReset { get; set; }
    public bool ThrowOnServiceQuery { get; set; }
    public bool KeepProcessesRunning { get; set; }
    public bool FailServiceRestore { get; set; }
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
        return Firewall;
    }

    public HostsInspection InspectHosts()
    {
        Calls.Add("InspectHosts");
        return Hosts;
    }

    public IReadOnlyList<string> DiscoverFirewallExecutables()
    {
        Calls.Add("DiscoverExecutables");
        return Executables.ToList();
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

        foreach (var target in targets)
        {
            var index = Services.FindIndex(service =>
                service.Name.Equals(target.Name, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
                continue;

            if (FailServiceRestore && !target.DesiredStartMode.Equals("Manual", StringComparison.OrdinalIgnoreCase))
                continue;

            Services[index] = Services[index] with
            {
                StartMode = target.DesiredStartMode,
                DelayedAutoStart = target.DelayedAutoStart
            };
        }

        return [Ok("Service modes set")];
    }

    public IReadOnlyList<OperationLine> StopServices(IEnumerable<string> serviceNames, bool dryRun)
    {
        Calls.Add("StopServices");
        if (!dryRun)
            SetServiceStates(serviceNames, "Stopped");

        return [Ok("Services stopped")];
    }

    public IReadOnlyList<OperationLine> StartServices(IEnumerable<string> serviceNames, bool dryRun)
    {
        Calls.Add("StartServices");
        if (!dryRun)
            SetServiceStates(serviceNames, "Running");

        return [Ok("Services started")];
    }

    public IReadOnlyList<OperationLine> SetTaskEnabledStates(
        IEnumerable<TaskEnableTarget> targets,
        bool dryRun)
    {
        Calls.Add("SetTaskStates");
        if (dryRun)
            return [Ok("Would set task states")];

        foreach (var target in targets)
        {
            var index = Tasks.FindIndex(task =>
                task.Path.Equals(target.Path, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
                Tasks[index] = Tasks[index] with { Enabled = target.Enabled };
        }

        return [Ok("Task states set")];
    }

    public IReadOnlyList<OperationLine> StopTasks(IEnumerable<string> taskPaths, bool dryRun)
    {
        Calls.Add("StopTasks");
        if (dryRun)
            return [Ok("Would stop tasks")];

        foreach (var path in taskPaths)
        {
            var index = Tasks.FindIndex(task =>
                task.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
                Tasks[index] = Tasks[index] with { State = "Ready" };
        }

        return [Ok("Tasks stopped")];
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
        if (!dryRun)
        {
            foreach (var entry in entries)
            {
                var restored = new RunEntry(entry.Hive, entry.View, entry.Name, entry.Value);
                RunEntries.RemoveAll(current => RunEntryIdentity(current).Equals(
                    RunEntryIdentity(restored),
                    StringComparison.OrdinalIgnoreCase));
                RunEntries.Add(restored);
            }
        }

        return [Ok("Run entries restored")];
    }

    public IReadOnlyList<OperationLine> TerminateTargetProcesses(bool dryRun)
    {
        Calls.Add("TerminateProcesses");
        if (!dryRun && !KeepProcessesRunning)
            Processes.Clear();

        return [Ok("Processes terminated")];
    }

    public IReadOnlyList<OperationLine> ActivateFirewall(bool dryRun)
    {
        Calls.Add("ActivateFirewall");
        if (dryRun)
            return [Ok("Would activate firewall")];

        if (FailFirewallActivation)
        {
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
        var targets = new FirewallTargetSet(
            Package,
            PackageSid,
            string.Empty,
            Executables.ToHashSet(StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));
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

    private static HostsInspection BuildHostsInspection(bool allBlocked, int managedLineCount)
        => new(
            true,
            OmenTargets.HostsDomains
                .Select(domain => new HostsDomainState(domain, allBlocked))
                .ToList(),
            managedLineCount,
            string.Empty);

    private void SetServiceStates(IEnumerable<string> serviceNames, string state)
    {
        var names = serviceNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < Services.Count; index++)
        {
            if (names.Contains(Services[index].Name))
                Services[index] = Services[index] with { State = state };
        }
    }

    private static OperationLine Ok(string text) => new() { Level = "OK", Text = text };
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

        Merge(
            State.Services,
            serviceBackups,
            backup => backup.Name);
        Merge(
            State.Tasks,
            taskBackups,
            backup => backup.Path);
        Merge(
            State.RunEntries,
            runEntryBackups,
            backup => $"{backup.Hive}|{backup.View}|{backup.Name}");
    }

    public bool TryClear(out string failureDetails)
    {
        ClearCalled = true;
        failureDetails = ClearSucceeds ? string.Empty : "clear failed";
        return ClearSucceeds;
    }

    private static void Merge<T>(
        ICollection<T> destination,
        IEnumerable<T> source,
        Func<T, string> identity)
    {
        var known = destination.Select(identity).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var item in source)
        {
            if (known.Add(identity(item)))
                destination.Add(item);
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
