namespace OmenGamingHubUnlocker.Windows;

/// <summary>
/// Production adapter that maps orchestration requests to concrete Windows managers.
/// </summary>
public sealed class WindowsUnlockerOperations : IUnlockerOperations
{
    public IReadOnlyList<ProcessItem> QueryTargetProcesses()
        => ProcessManager.QueryTargetProcesses(
            OmenTargets.ProcessNamePatterns,
            FirewallManager.DiscoverCandidateExecutables());

    public IReadOnlyList<ServiceItem> QueryTargetServices()
        => ServiceManager.QueryServices(OmenTargets.ServicePatterns);

    public IReadOnlyList<TaskItem> QueryTargetTasks()
        => TaskSchedulerManager.QueryTasks(OmenTargets.TaskPatterns);

    public IReadOnlyList<RunEntry> QueryTargetRunEntries()
        => RegistryRunManager.QueryRunEntries(OmenTargets.RunEntryPatterns);

    public UserContextStatus InspectUserContext()
        => UserContextManager.Inspect();

    public FirewallProtectionStatus InspectFirewallProtection()
        => FirewallManager.InspectProtection(OmenTargets.FirewallRulePrefix);

    public HostsInspection InspectHosts()
        => HostsManager.Inspect(OmenTargets.HostsDomains, OmenTargets.HostsMarker);

    public IReadOnlyList<string> DiscoverFirewallExecutables()
        => FirewallManager.DiscoverCandidateExecutables()
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

    public IReadOnlyList<OperationLine> StopServices(IEnumerable<string> serviceNames, bool dryRun)
        => ServiceManager.StopServices(serviceNames, dryRun);

    public IReadOnlyList<OperationLine> StartServices(IEnumerable<string> serviceNames, bool dryRun)
        => ServiceManager.StartServices(serviceNames, dryRun);

    public IReadOnlyList<OperationLine> SetTaskEnabledStates(
        IEnumerable<TaskEnableTarget> targets,
        bool dryRun)
        => TaskSchedulerManager.ApplyEnabledTargets(targets, dryRun);

    public IReadOnlyList<OperationLine> StopTasks(IEnumerable<string> taskPaths, bool dryRun)
        => TaskSchedulerManager.StopTasks(taskPaths, dryRun);

    public IReadOnlyList<OperationLine> RemoveRunEntries(IEnumerable<RunEntry> entries, bool dryRun)
        => RegistryRunManager.RemoveEntries(entries, dryRun);

    public IReadOnlyList<OperationLine> RestoreRunEntries(IEnumerable<RunEntryBackup> entries, bool dryRun)
        => RegistryRunManager.RestoreEntries(entries, dryRun);

    public IReadOnlyList<OperationLine> TerminateTargetProcesses(bool dryRun)
        => ProcessManager.TerminateTargetProcesses(
            OmenTargets.ProcessNamePatterns,
            FirewallManager.DiscoverCandidateExecutables(),
            dryRun);

    public IReadOnlyList<OperationLine> ActivateFirewall(bool dryRun)
        => FirewallManager.ActivateFirewallBlock(OmenTargets.FirewallRulePrefix, dryRun);

    public IReadOnlyList<OperationLine> DisableFirewall(bool dryRun)
        => FirewallManager.DisableFirewallBlock(OmenTargets.FirewallRulePrefix, dryRun);

    public IReadOnlyList<OperationLine> ActivateHosts(bool dryRun)
        => HostsManager.ActivateHostsBlock(OmenTargets.HostsDomains, OmenTargets.HostsMarker, dryRun);

    public IReadOnlyList<OperationLine> DisableHosts(bool dryRun)
        => HostsManager.DisableHostsBlock(OmenTargets.HostsMarker, dryRun);

    public IReadOnlyList<OperationLine> ResetPackage(bool dryRun)
        => AppxPackageManager.ResetPackage(OmenTargets.AppxFilters, dryRun);
}
