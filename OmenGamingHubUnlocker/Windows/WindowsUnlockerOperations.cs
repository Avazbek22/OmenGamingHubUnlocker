namespace OmenGamingHubUnlocker.Windows;

/// <summary>
/// Production adapter that maps orchestration requests to concrete Windows managers.
/// </summary>
public sealed class WindowsUnlockerOperations : IUnlockerOperations
{
    public IReadOnlyList<ProcessItem> QueryTargetProcesses()
        => ProcessManager.QueryTargetProcesses(
            OmenTargets.ProcessNamePatterns,
            DiscoverFirewallTargets().AllExecutables,
            requireTrustedOmenIdentity: true);

    public IReadOnlyList<ServiceItem> QueryTargetServices()
        => ServiceManager.QueryServices(OmenTargets.ServicePatterns);

    public IReadOnlyList<TaskItem> QueryTargetTasks()
        => TaskSchedulerManager.QueryTasks(OmenTargets.TaskPatterns);

    public IReadOnlyList<RunEntry> QueryTargetRunEntries()
        => RegistryRunManager.QueryRunEntries(OmenTargets.RunEntryPatterns);

    public UserContextStatus InspectUserContext()
        => UserContextManager.Inspect();

    public FirewallProtectionStatus InspectFirewallProtection()
    {
        var targets = DiscoverFirewallTargets();
        return FirewallManager.InspectProtection(OmenTargets.FirewallRulePrefix, targets);
    }

    public HostsInspection InspectHosts()
        => HostsManager.Inspect(OmenTargets.HostsDomains, OmenTargets.HostsMarker);

    public FirewallTargetSet DiscoverFirewallTargets()
    {
        var baseTargets = FirewallManager.DiscoverTargets();
        var additionalExecutables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var discoveryErrors = baseTargets.ScanErrors.ToList();

        TryDiscoverServiceExecutables(additionalExecutables, discoveryErrors);
        TryDiscoverTaskExecutables(additionalExecutables, discoveryErrors);
        TryDiscoverRunEntryExecutables(additionalExecutables, discoveryErrors);

        return new FirewallTargetSet(
            baseTargets.Package,
            baseTargets.PackageSid,
            baseTargets.PackageSidError,
            baseTargets.PackageExecutables,
            baseTargets.ExternalExecutables
                .Concat(additionalExecutables)
                .ToHashSet(StringComparer.OrdinalIgnoreCase),
            baseTargets.PackageDirectoryReady,
            discoveryErrors);
    }

    public IReadOnlyList<string> DiscoverFirewallExecutables()
        => DiscoverFirewallTargets().AllExecutables
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public bool TryGetPrimaryPackage(out AppxPackageInfo? package, out string details)
        => AppxPackageManager.TryGetPrimaryPackage(OmenTargets.AppxFilters, out package, out details);

    public IReadOnlyList<(string Name, bool Success, string Details)> RunCapabilityChecks()
    {
        var checks = new List<(string Name, Func<(bool ok, string details)> Run)>
        {
            (Text.Get("engine.check.taskSchedulerCom"), TaskSchedulerManager.CheckCapability),
            (Text.Get("engine.check.firewallCom"), FirewallManager.CheckCapability),
            (Text.Get("engine.check.wmiServices"), ServiceManager.CheckCapability),
            (Text.Get("engine.check.hostsWriteAccess"), () => HostsManager.CheckWriteAccess(OmenTargets.HostsMarker)),
            (Text.Get("engine.check.powerShellAvailability"), PowerShellRunner.CheckAvailability),
            (Text.Get("engine.check.netshAvailability"), PowerShellRunner.CheckNetshAvailability),
            (Text.Get("engine.check.appxResetCapability"), AppxPackageManager.CheckResetCapability)
        };

        var userContext = UserContextManager.Inspect();
        checks.Add((
            Text.Get("engine.check.userContext"),
            () => (
                userContext.IsSafe,
                userContext.InspectionSucceeded
                    ? $"{userContext.ProcessIdentity} / {userContext.InteractiveIdentity}"
                    : userContext.Error)));

        return checks.Select(check =>
        {
            try
            {
                var result = check.Run();
                return (check.Name, result.ok, result.details);
            }
            catch (Exception exception)
            {
                return (check.Name, false, exception.Message);
            }
        }).ToList();
    }

    public IReadOnlyList<OperationLine> SetServiceStartModes(
        IEnumerable<ServiceStartModeTarget> targets,
        bool dryRun)
        => ServiceManager.ApplyStartModeTargets(targets, dryRun);

    public IReadOnlyList<OperationLine> StopServices(IEnumerable<ServiceRuntimeTarget> targets, bool dryRun)
        => ServiceManager.StopServices(targets, dryRun);

    public IReadOnlyList<OperationLine> StartServices(IEnumerable<ServiceRuntimeTarget> targets, bool dryRun)
        => ServiceManager.StartServices(targets, dryRun);

    public IReadOnlyList<OperationLine> SetTaskEnabledStates(
        IEnumerable<TaskEnableTarget> targets,
        bool dryRun)
        => TaskSchedulerManager.ApplyEnabledTargets(targets, dryRun);

    public IReadOnlyList<OperationLine> StopTasks(IEnumerable<TaskRuntimeTarget> targets, bool dryRun)
        => TaskSchedulerManager.StopTasks(targets, dryRun);

    public IReadOnlyList<OperationLine> StartTasks(IEnumerable<TaskRuntimeTarget> targets, bool dryRun)
        => TaskSchedulerManager.StartTasks(targets, dryRun);

    public IReadOnlyList<OperationLine> RemoveRunEntries(IEnumerable<RunEntry> entries, bool dryRun)
        => RegistryRunManager.RemoveEntries(entries, dryRun);

    public IReadOnlyList<OperationLine> RestoreRunEntries(IEnumerable<RunEntryBackup> entries, bool dryRun)
        => RegistryRunManager.RestoreEntries(entries, dryRun);

    public IReadOnlyList<OperationLine> TerminateTargetProcesses(bool dryRun)
        => ProcessManager.TerminateTargetProcesses(
            OmenTargets.ProcessNamePatterns,
            DiscoverFirewallTargets().AllExecutables,
            dryRun,
            requireTrustedOmenIdentity: true);

    public IReadOnlyList<OperationLine> ActivateFirewall(bool dryRun, bool removeStaleRules = false)
    {
        var targets = DiscoverFirewallTargets();
        return FirewallManager.ActivateFirewallBlock(
            OmenTargets.FirewallRulePrefix,
            dryRun,
            removeStaleRules,
            targets);
    }

    public IReadOnlyList<OperationLine> DisableFirewall(bool dryRun)
        => FirewallManager.DisableFirewallBlock(OmenTargets.FirewallRulePrefix, dryRun);

    public IReadOnlyList<OperationLine> ActivateHosts(bool dryRun)
        => HostsManager.ActivateHostsBlock(OmenTargets.HostsDomains, OmenTargets.HostsMarker, dryRun);

    public IReadOnlyList<OperationLine> DisableHosts(bool dryRun)
        => HostsManager.DisableHostsBlock(OmenTargets.HostsMarker, dryRun);

    public IReadOnlyList<OperationLine> ResetPackage(bool dryRun)
        => AppxPackageManager.ResetPackage(OmenTargets.AppxFilters, dryRun);

    private static void TryDiscoverServiceExecutables(
        HashSet<string> destination,
        List<string> discoveryErrors)
    {
        try
        {
            foreach (var service in ServiceManager.QueryServices(OmenTargets.ServicePatterns))
                TryAddExecutable(service.PathName, destination, service.Name, service.DisplayName);
        }
        catch (Exception exception)
        {
            discoveryErrors.Add($"Service executable discovery: {exception.Message}");
        }
    }

    private static void TryDiscoverTaskExecutables(
        HashSet<string> destination,
        List<string> discoveryErrors)
    {
        try
        {
            foreach (var task in TaskSchedulerManager.QueryTasks(OmenTargets.TaskPatterns))
            {
                foreach (var actionPath in task.ActionPaths)
                    TryAddExecutable(actionPath, destination, task.Path);
            }
        }
        catch (Exception exception)
        {
            discoveryErrors.Add($"Scheduled task executable discovery: {exception.Message}");
        }
    }

    private static void TryDiscoverRunEntryExecutables(
        HashSet<string> destination,
        List<string> discoveryErrors)
    {
        try
        {
            foreach (var entry in RegistryRunManager.QueryRunEntries(OmenTargets.RunEntryPatterns))
                TryAddExecutable(entry.Value, destination, entry.Name);
        }
        catch (Exception exception)
        {
            discoveryErrors.Add($"Run entry executable discovery: {exception.Message}");
        }
    }

    private static void TryAddExecutable(
        string commandLine,
        HashSet<string> destination,
        params string[] identities)
    {
        if (ExecutablePathResolver.TryResolveExistingExecutable(commandLine, out var executablePath) &&
            OmenExecutableTrust.IsTrustedOmenExecutable(executablePath, identities))
        {
            destination.Add(executablePath);
        }
    }
}
