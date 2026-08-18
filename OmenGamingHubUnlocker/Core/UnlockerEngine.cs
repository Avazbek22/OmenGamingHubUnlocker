namespace OmenGamingHubUnlocker.Core;

/// <summary>
/// Coordinates transactional OMEN operations and verifies the resulting machine state.
/// </summary>
public sealed class UnlockerEngine
{
    private const int StabilizationAttempts = 8;
    private const int PostResetDiscoveryAttempts = 15;
    private const int RequiredStableSnapshots = 2;
    private static readonly TimeSpan StabilizationDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan PostResetDiscoveryDelay = TimeSpan.FromSeconds(2);

    private readonly IUnlockerOperations _operations;
    private readonly IUnlockerStateStore _stateStore;
    private readonly IOperationDelay _delay;
    private readonly UnlockerStatusService _statusService;

    public UnlockerEngine()
        : this(new WindowsUnlockerOperations(), new UnlockerStateStore(), new ThreadOperationDelay())
    {
    }

    public UnlockerEngine(
        IUnlockerOperations operations,
        IUnlockerStateStore stateStore,
        IOperationDelay? delay = null)
    {
        _operations = operations ?? throw new ArgumentNullException(nameof(operations));
        _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
        _delay = delay ?? new ThreadOperationDelay();
        _statusService = new UnlockerStatusService(_operations);
    }

    /// <summary>
    /// Builds a status report that distinguishes a healthy state from failed discovery.
    /// </summary>
    public StatusReport GetStatusReport()
        => _statusService.BuildTamedStatus();

    /// <summary>
    /// Performs capability and discovery checks without changing Windows.
    /// </summary>
    public OperationReport RunDryRunDeep()
    {
        var report = OperationReport.Ok(Text.Get("engine.title.dryRunCompleted"));

        foreach (var check in SafeQuery(
                     _operations.RunCapabilityChecks,
                     [],
                     report,
                     "engine.capabilityDiscoveryFailed"))
        {
            report.Lines.Add(new OperationLine
            {
                Level = check.Success ? "OK" : "WARN",
                Text = Text.Format(
                    "engine.check.result",
                    check.Name,
                    check.Success ? Text.Get("engine.check.okLabel") : Text.Get("engine.check.notOkLabel"),
                    check.Details)
            });
        }

        if (_operations.TryGetPrimaryPackage(out _, out var packageDetails))
            report.Lines.Add(LocalizedLine.Ok("engine.appxTarget", packageDetails));
        else
            report.Lines.Add(LocalizedLine.Warn("engine.appxTarget", packageDetails));

        var plan = CollectActivationPlan(report);
        report.Lines.Add(LocalizedLine.Info(
            "engine.activationPlanDetailed",
            plan.ServicesToConfigure.Count,
            plan.ServicesToStop.Count,
            plan.TasksToDisable.Count,
            plan.TasksToStop.Count,
            plan.RunEntriesToRemove.Count));

        var stateResult = _stateStore.LoadState();
        if (stateResult.Success)
        {
            report.Lines.Add(LocalizedLine.Info(
                "engine.rollbackBackup",
                stateResult.State.Services.Count,
                stateResult.State.Tasks.Count,
                stateResult.State.RunEntries.Count));
        }
        else
        {
            report.Lines.Add(LocalizedLine.Warn("engine.stateBackupUnreadable", stateResult.Error));
        }

        var executables = SafeQuery(
            _operations.DiscoverFirewallExecutables,
            [],
            report,
            "engine.executableDiscoveryFailed");
        report.Lines.Add(LocalizedLine.Info("engine.executableDiscoveryFound", executables.Count));

        foreach (var executable in executables.Take(25))
            report.Lines.Add(new OperationLine { Level = "INFO", Text = $"  - {executable}" });

        if (executables.Count > 25)
            report.Lines.Add(LocalizedLine.Info("common.moreItems", executables.Count - 25));

        report.SnapshotsAfter.AddRange(GetStatusReport().Snapshots);
        report.Success = report.Lines.All(line => line.Level != "ERR");
        return report;
    }

    public OperationReport Activate(UnlockerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var report = OperationReport.Ok(Text.Get("engine.title.activationCompleted"));
        if (!ValidateUserContext(report, options))
        {
            CompleteReport(report, Text.Get("engine.title.activationFailed"));
            return report;
        }

        var activationResult = ApplyTamedState(report, options, saveRollback: true);

        if (activationResult == ApplyTamedStateResult.Applied && !options.DryRun)
            StabilizeTamedState(report, options);

        CompleteTamedReport(report, options, Text.Get("engine.title.activationFailed"));
        return report;
    }

    public OperationReport ResetAndReapply(
        UnlockerOptions options,
        IProgress<string>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        var report = OperationReport.Ok(Text.Get("engine.title.resetCompleted"));
        report.Lines.Add(LocalizedLine.Info("engine.resetStarted"));
        progress?.Report(Text.Get("activity.reset.preparing"));
        if (!ValidateUserContext(report, options))
        {
            CompleteReport(report, Text.Get("engine.title.resetFailed"));
            return report;
        }

        if (ApplyTamedState(report, options, saveRollback: true) != ApplyTamedStateResult.Applied)
        {
            CompleteTamedReport(report, options, Text.Get("engine.title.resetFailed"));
            return report;
        }

        progress?.Report(Text.Get("activity.reset.executing"));
        AddOperationLines(report, () => _operations.ResetPackage(options.DryRun), "engine.resetUnexpectedFailure");

        if (!options.DryRun)
        {
            report.Lines.Add(LocalizedLine.Info("engine.postResetDiscovery"));
            WaitForStablePostResetTargets(progress);
        }

        // Reset can recreate package registrations and background activity, so all targets are rediscovered.
        progress?.Report(Text.Get("activity.reset.reapplying"));
        var reapplicationResult = ApplyTamedState(
            report,
            options,
            saveRollback: true,
            NetworkVerificationMode.Retryable);

        if (reapplicationResult != ApplyTamedStateResult.Failed && !options.DryRun)
            StabilizeTamedState(report, options, progress);

        progress?.Report(Text.Get("activity.reset.verifying"));
        CompleteTamedReport(report, options, Text.Get("engine.title.resetFailed"));
        return report;
    }

    public OperationReport Disable(UnlockerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var report = OperationReport.Ok(Text.Get("engine.title.disableCompleted"));
        if (!ValidateUserContext(report, options))
        {
            CompleteReport(report, Text.Get("engine.title.disableFailed"));
            return report;
        }

        var stateResult = _stateStore.LoadState();
        if (!stateResult.Success)
        {
            report.Lines.Add(LocalizedLine.Err("engine.stateBackupUnreadable", stateResult.Error));
            CompleteReport(report, Text.Get("engine.title.disableFailed"));
            return report;
        }

        var state = stateResult.State;
        RestoreStartupState(report, state, options);

        if (!options.DryRun)
            VerifyRestoredStartupState(report, state);

        if (!HasErrors(report))
        {
            if (options.ManageHosts)
                AddOperationLines(report, () => _operations.DisableHosts(options.DryRun), "engine.hostsStepFailed");

            if (options.ManageFirewall)
                AddOperationLines(report, () => _operations.DisableFirewall(options.DryRun), "engine.firewallStepFailed");
        }
        else
        {
            report.Lines.Add(LocalizedLine.Warn("engine.networkProtectionKeptAfterRestoreFailure"));
        }

        if (!options.DryRun)
            VerifyDisabledNetworkState(report, options);

        CompleteDisableReport(report, state, options, Text.Get("engine.title.disableFailed"));

        if (!options.DryRun && report.Success)
        {
            if (_stateStore.TryClear(out var clearError))
                report.Lines.Add(LocalizedLine.Info("engine.stateBackupCleared"));
            else
            {
                report.Lines.Add(LocalizedLine.Err("engine.stateBackupClearFailed", clearError));
                report.Success = false;
                report.Title = Text.Get("engine.title.disableFailed");
            }
        }

        return report;
    }

    private ApplyTamedStateResult ApplyTamedState(
        OperationReport report,
        UnlockerOptions options,
        bool saveRollback,
        NetworkVerificationMode networkVerificationMode = NetworkVerificationMode.Required,
        bool removeStaleFirewallRules = false)
    {
        var errorsBeforeDiscovery = CountErrors(report);
        var plan = CollectActivationPlan(report);
        if (CountErrors(report) > errorsBeforeDiscovery)
            return ApplyTamedStateResult.Failed;

        if (saveRollback && !SaveActivationBackups(plan, options, report))
            return ApplyTamedStateResult.Failed;

        // Network isolation is intentionally first so later reset or process races cannot call home.
        var errorsBeforeNetworkMutation = CountErrors(report);
        var retryNetworkFailures = networkVerificationMode == NetworkVerificationMode.Retryable;
        var networkMutationSucceeded = true;
        if (options.ManageFirewall)
        {
            var activateFirewall = () =>
                _operations.ActivateFirewall(options.DryRun, removeStaleFirewallRules);
            if (retryNetworkFailures)
            {
                networkMutationSucceeded &= AddRetryableOperationLines(
                    report,
                    activateFirewall);
            }
            else
            {
                AddOperationLines(report, activateFirewall, "engine.firewallStepFailed");
            }
        }

        if (options.ManageHosts)
        {
            var activateHosts = () => _operations.ActivateHosts(options.DryRun);
            if (retryNetworkFailures)
                networkMutationSucceeded &= AddRetryableOperationLines(report, activateHosts);
            else
                AddOperationLines(report, activateHosts, "engine.hostsStepFailed");
        }

        if (CountErrors(report) > errorsBeforeNetworkMutation)
            return ApplyTamedStateResult.Failed;

        if (!networkMutationSucceeded)
            return ApplyTamedStateResult.NetworkIncomplete;

        if (!options.DryRun && !VerifyNetworkProtectionBeforeMutation(
                report,
                options,
                reportIncomplete: networkVerificationMode == NetworkVerificationMode.Required))
        {
            return networkVerificationMode == NetworkVerificationMode.Required
                ? ApplyTamedStateResult.Failed
                : ApplyTamedStateResult.NetworkIncomplete;
        }

        AddOperationLines(
            report,
            () => _operations.SetServiceStartModes(
                plan.ServicesToConfigure.Select(service => new ServiceStartModeTarget(service.Name, "Manual")),
                options.DryRun),
            "engine.servicesStepFailed");

        AddOperationLines(
            report,
            () => _operations.SetTaskEnabledStates(
                plan.TasksToDisable.Select(task => new TaskEnableTarget(task.Path, false)),
                options.DryRun),
            "engine.tasksStepFailed");

        AddOperationLines(
            report,
            () => _operations.StopTasks(plan.TasksToStop.Select(task => task.Path), options.DryRun),
            "engine.tasksStopFailed");

        AddOperationLines(
            report,
            () => _operations.StopServices(plan.ServicesToStop.Select(service => service.Name), options.DryRun),
            "engine.servicesStopFailed");

        if (options.TryKillProcesses)
        {
            AddOperationLines(
                report,
                () => _operations.TerminateTargetProcesses(options.DryRun),
                "engine.processTerminationFailed");
        }

        AddOperationLines(
            report,
            () => _operations.RemoveRunEntries(plan.RunEntriesToRemove, options.DryRun),
            "engine.registryRunStepFailed");

        return CountErrors(report) == errorsBeforeNetworkMutation
            ? ApplyTamedStateResult.Applied
            : ApplyTamedStateResult.Failed;
    }

    private void StabilizeTamedState(
        OperationReport report,
        UnlockerOptions options,
        IProgress<string>? progress = null)
    {
        var consecutiveStableSnapshots = 0;

        for (var attempt = 1; attempt <= StabilizationAttempts; attempt++)
        {
            progress?.Report(Text.Format(
                "activity.reset.stabilizing",
                attempt,
                StabilizationAttempts));
            _delay.Wait(StabilizationDelay);
            var plan = CollectActivationPlan(report);
            var processes = SafeQuery(
                _operations.QueryTargetProcesses,
                [],
                report,
                "engine.processDiscoveryFailed");
            var firewallComplete = !options.ManageFirewall ||
                                   SafeQueryFirewall(report, reportFailure: false).IsComplete;
            var hostsComplete = !options.ManageHosts ||
                                SafeQueryHosts(report, reportFailure: false).AllBlocked;
            var startupStable =
                plan.ServicesToConfigure.Count == 0 &&
                plan.ServicesToStop.Count == 0 &&
                plan.TasksToDisable.Count == 0 &&
                plan.TasksToStop.Count == 0 &&
                plan.RunEntriesToRemove.Count == 0 &&
                processes.Count == 0;

            if (startupStable && firewallComplete && hostsComplete)
            {
                consecutiveStableSnapshots++;
                report.Lines.Add(LocalizedLine.Info(
                    "engine.stabilizationStableSnapshot",
                    attempt,
                    consecutiveStableSnapshots,
                    RequiredStableSnapshots));

                if (consecutiveStableSnapshots >= RequiredStableSnapshots)
                {
                    if (FinalizeFirewallReconciliation(report, options))
                        return;

                    consecutiveStableSnapshots = 0;
                }

                continue;
            }

            consecutiveStableSnapshots = 0;
            report.Lines.Add(LocalizedLine.Info(
                "engine.stabilizationSweepFound",
                attempt,
                processes.Count,
                plan.ServicesToConfigure.Count + plan.ServicesToStop.Count,
                plan.TasksToDisable.Count + plan.TasksToStop.Count,
                plan.RunEntriesToRemove.Count));

            var applyResult = ApplyTamedState(
                report,
                options,
                saveRollback: true,
                NetworkVerificationMode.Retryable);
            if (applyResult == ApplyTamedStateResult.Failed)
                return;
        }

        report.Lines.Add(LocalizedLine.Err("engine.stabilizationFailed", StabilizationAttempts));
    }

    private bool FinalizeFirewallReconciliation(OperationReport report, UnlockerOptions options)
    {
        if (!options.ManageFirewall)
            return true;

        var cleanupSucceeded = AddRetryableOperationLines(
            report,
            () => _operations.ActivateFirewall(options.DryRun, removeStaleRules: true));
        if (!cleanupSucceeded)
            return false;

        var status = SafeQueryFirewall(report, reportFailure: false);
        return status.IsComplete && status.StaleExecutableRules.Count == 0;
    }

    private bool WaitForStablePostResetTargets(IProgress<string>? progress)
    {
        FirewallTargetSet? previousTargets = null;
        var consecutiveStableSnapshots = 0;

        for (var attempt = 1; attempt <= PostResetDiscoveryAttempts; attempt++)
        {
            progress?.Report(Text.Format(
                "activity.reset.waitingForFiles",
                attempt,
                PostResetDiscoveryAttempts,
                previousTargets?.AllExecutables.Count ?? 0));
            _delay.Wait(PostResetDiscoveryDelay);

            FirewallTargetSet targets;
            try
            {
                targets = _operations.DiscoverFirewallTargets();
            }
            catch
            {
                previousTargets = null;
                consecutiveStableSnapshots = 0;
                continue;
            }

            var ready = targets.Package is not null &&
                        targets.PackageDirectoryReady &&
                        targets.DiscoveryComplete &&
                        targets.PackageExecutables.Count > 0;
            var unchanged = ready && previousTargets is not null &&
                            HaveSameFirewallTargets(previousTargets, targets);
            consecutiveStableSnapshots = unchanged
                ? consecutiveStableSnapshots + 1
                : ready ? 1 : 0;
            previousTargets = targets;

            progress?.Report(Text.Format(
                "activity.reset.filesFound",
                targets.AllExecutables.Count,
                consecutiveStableSnapshots,
                RequiredStableSnapshots));

            if (consecutiveStableSnapshots >= RequiredStableSnapshots)
                return true;
        }

        return false;
    }

    private static bool HaveSameFirewallTargets(
        FirewallTargetSet previous,
        FirewallTargetSet current)
        => string.Equals(
               previous.Package?.PackageFullName,
               current.Package?.PackageFullName,
               StringComparison.OrdinalIgnoreCase) &&
           previous.AllExecutables.SetEquals(current.AllExecutables);

    private ActivationPlan CollectActivationPlan(OperationReport report)
    {
        var services = SafeQuery(
                _operations.QueryTargetServices,
                [],
                report,
                "engine.serviceDiscoveryFailed")
            .DistinctBy(service => service.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var tasks = SafeQuery(
                _operations.QueryTargetTasks,
                [],
                report,
                "engine.taskDiscoveryFailed")
            .DistinctBy(task => task.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var runEntries = SafeQuery(
                _operations.QueryTargetRunEntries,
                [],
                report,
                "engine.registryDiscoveryFailed")
            .DistinctBy(
                entry => $"{entry.Hive}|{entry.View}|{entry.Name}",
                StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new ActivationPlan(
            services.Where(service => !ServiceStatePolicy.IsManual(service)).ToList(),
            services.Where(service => !ServiceStatePolicy.IsStopped(service)).ToList(),
            tasks.Where(task => task.Enabled).ToList(),
            tasks.Where(task => task.RequiresStop).ToList(),
            runEntries);
    }

    private bool SaveActivationBackups(
        ActivationPlan plan,
        UnlockerOptions options,
        OperationReport report)
    {
        if (options.DryRun)
        {
            report.Lines.Add(LocalizedLine.Info("engine.stateBackupSkipped"));
            return true;
        }

        try
        {
            var services = plan.ServicesToConfigure
                .Concat(plan.ServicesToStop)
                .DistinctBy(service => service.Name, StringComparer.OrdinalIgnoreCase)
                .Select(service => new ServiceBackup(
                    service.Name,
                    service.StartMode,
                    ServiceStatePolicy.IsRunning(service),
                    service.DelayedAutoStart));
            var tasks = plan.TasksToDisable
                .Concat(plan.TasksToStop)
                .DistinctBy(task => task.Path, StringComparer.OrdinalIgnoreCase)
                .Select(task => new TaskBackup(task.Path, task.Enabled));
            var runEntries = plan.RunEntriesToRemove.Select(entry =>
                new RunEntryBackup(entry.Hive, entry.View, entry.Name, entry.Value, entry.ValueKind));

            _stateStore.PersistBackups(services, tasks, runEntries);
            report.Lines.Add(LocalizedLine.Info(
                "engine.stateBackupSaved",
                plan.ServicesToConfigure.Concat(plan.ServicesToStop)
                    .DistinctBy(service => service.Name, StringComparer.OrdinalIgnoreCase)
                    .Count(),
                plan.TasksToDisable.Concat(plan.TasksToStop)
                    .DistinctBy(task => task.Path, StringComparer.OrdinalIgnoreCase)
                    .Count(),
                plan.RunEntriesToRemove.Count));
            return true;
        }
        catch (Exception exception)
        {
            report.Lines.Add(LocalizedLine.Err("engine.stateBackupFailed", exception.Message));
            return false;
        }
    }

    private bool VerifyNetworkProtectionBeforeMutation(
        OperationReport report,
        UnlockerOptions options,
        bool reportIncomplete)
    {
        var isComplete = true;

        if (options.ManageFirewall)
        {
            var firewall = SafeQueryFirewall(report, reportIncomplete);
            if (!firewall.IsComplete)
            {
                if (reportIncomplete && firewall.QuerySucceeded)
                    report.Lines.Add(LocalizedLine.Err("engine.verificationFirewallIncomplete"));

                isComplete = false;
            }
        }

        if (options.ManageHosts)
        {
            var hosts = SafeQueryHosts(report, reportIncomplete);
            if (!hosts.AllBlocked)
            {
                if (reportIncomplete && hosts.Success)
                    report.Lines.Add(LocalizedLine.Err("engine.verificationHostsIncomplete"));

                isComplete = false;
            }
        }

        return isComplete;
    }

    private void RestoreStartupState(
        OperationReport report,
        UnlockerState state,
        UnlockerOptions options)
    {
        AddOperationLines(
            report,
            () => _operations.RestoreRunEntries(state.RunEntries, options.DryRun),
            "engine.registryRestoreFailed");

        AddOperationLines(
            report,
            () => _operations.SetTaskEnabledStates(
                state.Tasks.Select(task => new TaskEnableTarget(task.Path, task.OriginalEnabled)),
                options.DryRun),
            "engine.tasksRestoreFailed");

        AddOperationLines(
            report,
            () => _operations.SetServiceStartModes(
                state.Services.Select(service =>
                    new ServiceStartModeTarget(
                        service.Name,
                        service.OriginalStartMode,
                        service.OriginalDelayedAutoStart)),
                options.DryRun),
            "engine.servicesRestoreFailed");

        AddOperationLines(
            report,
            () => _operations.StopServices(
                state.Services.Where(service => !service.OriginalRunning).Select(service => service.Name),
                options.DryRun),
            "engine.servicesStopFailed");

        AddOperationLines(
            report,
            () => _operations.StartServices(
                state.Services.Where(service => service.OriginalRunning).Select(service => service.Name),
                options.DryRun),
            "engine.servicesStartFailed");
    }

    private void CompleteTamedReport(
        OperationReport report,
        UnlockerOptions options,
        string errorTitle)
    {
        if (!options.DryRun)
            VerifyTamedState(report, options);

        CompleteReport(report, errorTitle);
    }

    private void CompleteReport(OperationReport report, string errorTitle)
    {
        report.SnapshotsAfter.Clear();
        report.SnapshotsAfter.AddRange(GetStatusReport().Snapshots);
        report.Success = !HasErrors(report);

        if (!report.Success)
            report.Title = errorTitle;
    }

    private void CompleteDisableReport(
        OperationReport report,
        UnlockerState state,
        UnlockerOptions options,
        string errorTitle)
    {
        report.SnapshotsAfter.Clear();
        report.SnapshotsAfter.AddRange(_statusService.BuildDisableStatus(state, options));

        report.Success = !HasErrors(report) &&
                         report.SnapshotsAfter.All(snapshot => snapshot.Result != "ERR");
        if (!report.Success)
            report.Title = errorTitle;
    }

    private void VerifyTamedState(OperationReport report, UnlockerOptions options)
    {
        var processes = SafeQuery(
            _operations.QueryTargetProcesses,
            [],
            report,
            "engine.processDiscoveryFailed");
        var services = SafeQuery(
            _operations.QueryTargetServices,
            [],
            report,
            "engine.serviceDiscoveryFailed");
        var tasks = SafeQuery(
            _operations.QueryTargetTasks,
            [],
            report,
            "engine.taskDiscoveryFailed");
        var runEntries = SafeQuery(
            _operations.QueryTargetRunEntries,
            [],
            report,
            "engine.registryDiscoveryFailed");

        if (processes.Count > 0)
            report.Lines.Add(LocalizedLine.Err("engine.verificationProcessesRunning", processes.Count));

        var invalidServices = services.Count(service =>
            !ServiceStatePolicy.IsManual(service) ||
            !ServiceStatePolicy.IsStopped(service));
        if (invalidServices > 0)
            report.Lines.Add(LocalizedLine.Err("engine.verificationServicesInvalid", invalidServices));

        var invalidTasks = tasks.Count(task => task.Enabled || task.RequiresStop);
        if (invalidTasks > 0)
            report.Lines.Add(LocalizedLine.Err("engine.verificationTasksInvalid", invalidTasks));

        if (runEntries.Count > 0)
            report.Lines.Add(LocalizedLine.Err("engine.verificationRunEntriesPresent", runEntries.Count));

        if (options.ManageFirewall)
        {
            var firewall = SafeQueryFirewall(report);
            if (firewall.QuerySucceeded && !firewall.IsComplete)
                AddErrorOnce(report, "engine.verificationFirewallIncomplete");
        }

        if (options.ManageHosts)
        {
            var hosts = SafeQueryHosts(report);
            if (hosts.Success && !hosts.AllBlocked)
                AddErrorOnce(report, "engine.verificationHostsIncomplete");
        }

        if (!HasErrors(report))
            report.Lines.Add(LocalizedLine.Ok("engine.verificationPassed"));
    }

    private void VerifyRestoredStartupState(OperationReport report, UnlockerState state)
    {
        var services = SafeQuery(
                _operations.QueryTargetServices,
                [],
                report,
                "engine.serviceDiscoveryFailed")
            .ToDictionary(service => service.Name, StringComparer.OrdinalIgnoreCase);
        var tasks = SafeQuery(
                _operations.QueryTargetTasks,
                [],
                report,
                "engine.taskDiscoveryFailed")
            .ToDictionary(task => task.Path, StringComparer.OrdinalIgnoreCase);
        var runEntries = SafeQuery(
                _operations.QueryTargetRunEntries,
                [],
                report,
                "engine.registryDiscoveryFailed")
            .ToDictionary(
                entry => $"{entry.Hive}|{entry.View}|{entry.Name}",
                StringComparer.OrdinalIgnoreCase);

        foreach (var backup in state.Services)
        {
            if (!services.TryGetValue(backup.Name, out var service))
            {
                report.Lines.Add(LocalizedLine.Warn("engine.restoreServiceNoLongerExists", backup.Name));
                continue;
            }

            if (!ServiceStatePolicy.MatchesBackup(service, backup))
                report.Lines.Add(LocalizedLine.Err("engine.restoreServiceMismatch", backup.Name));

            if (!ServiceStatePolicy.MatchesOriginalRunningState(service, backup))
                report.Lines.Add(LocalizedLine.Err("engine.restoreServiceRunningStateMismatch", backup.Name));
        }

        foreach (var backup in state.Tasks)
        {
            if (!tasks.TryGetValue(backup.Path, out var task))
            {
                report.Lines.Add(LocalizedLine.Warn("engine.restoreTaskNoLongerExists", backup.Path));
                continue;
            }

            if (task.Enabled != backup.OriginalEnabled)
                report.Lines.Add(LocalizedLine.Err("engine.restoreTaskMismatch", backup.Path));
        }

        foreach (var backup in state.RunEntries)
        {
            var key = $"{backup.Hive}|{backup.View}|{backup.Name}";
            if (!runEntries.TryGetValue(key, out var entry) ||
                !entry.Value.Equals(backup.Value, StringComparison.Ordinal) ||
                entry.ValueKind != backup.ValueKind)
            {
                report.Lines.Add(LocalizedLine.Err("engine.restoreRunEntryMismatch", backup.Name));
            }
        }
    }

    private void VerifyDisabledNetworkState(OperationReport report, UnlockerOptions options)
    {
        if (options.ManageFirewall)
        {
            var firewall = SafeQueryFirewall(report);
            if (firewall.QuerySucceeded && firewall.RuleCount > 0)
                report.Lines.Add(LocalizedLine.Err("engine.disableFirewallRulesRemain", firewall.RuleCount));
        }

        if (options.ManageHosts)
        {
            var hosts = SafeQueryHosts(report);
            if (hosts.Success && hosts.ManagedLineCount > 0)
                report.Lines.Add(LocalizedLine.Err("engine.disableHostsEntriesRemain", hosts.ManagedLineCount));
        }
    }

    private bool ValidateUserContext(OperationReport report, UnlockerOptions options)
    {
        try
        {
            var context = _operations.InspectUserContext();
            if (context.IsSafe)
            {
                report.Lines.Add(LocalizedLine.Info(
                    "engine.userContextVerified",
                    context.ProcessIdentity));
                return true;
            }

            var details = context.InspectionSucceeded
                ? Text.Format(
                    "engine.userContextMismatchDetails",
                    context.ProcessIdentity,
                    context.InteractiveIdentity)
                : context.Error;

            report.Lines.Add(options.DryRun
                ? LocalizedLine.Warn("engine.userContextUnsafe", details)
                : LocalizedLine.Err("engine.userContextUnsafe", details));
            return options.DryRun;
        }
        catch (Exception exception)
        {
            report.Lines.Add(options.DryRun
                ? LocalizedLine.Warn("engine.userContextUnsafe", exception.Message)
                : LocalizedLine.Err("engine.userContextUnsafe", exception.Message));
            return options.DryRun;
        }
    }

    private FirewallProtectionStatus SafeQueryFirewall(
        OperationReport report,
        bool reportFailure = true)
    {
        try
        {
            var status = _operations.InspectFirewallProtection();
            if (reportFailure && !status.QuerySucceeded)
                report.Lines.Add(LocalizedLine.Err("engine.firewallInspectionFailed", status.Error));

            return status;
        }
        catch (Exception exception)
        {
            if (reportFailure)
                report.Lines.Add(LocalizedLine.Err("engine.firewallInspectionFailed", exception.Message));

            return EmptyFirewallStatus(exception.Message);
        }
    }

    private HostsInspection SafeQueryHosts(
        OperationReport report,
        bool reportFailure = true)
    {
        try
        {
            var status = _operations.InspectHosts();
            if (reportFailure && !status.Success)
                report.Lines.Add(LocalizedLine.Err("engine.hostsInspectionFailed", status.Error));

            return status;
        }
        catch (Exception exception)
        {
            if (reportFailure)
                report.Lines.Add(LocalizedLine.Err("engine.hostsInspectionFailed", exception.Message));

            return new HostsInspection(false, [], 0, exception.Message);
        }
    }

    private static void AddErrorOnce(OperationReport report, string localizationKey)
    {
        var message = Text.Get(localizationKey);
        if (report.Lines.Any(line =>
                line.Level.Equals("ERR", StringComparison.OrdinalIgnoreCase) &&
                line.Text.Equals(message, StringComparison.Ordinal)))
        {
            return;
        }

        report.Lines.Add(new OperationLine { Level = "ERR", Text = message });
    }

    private static void AddOperationLines(
        OperationReport report,
        Func<IReadOnlyList<OperationLine>> operation,
        string failureKey)
    {
        try
        {
            report.Lines.AddRange(operation());
        }
        catch (Exception exception)
        {
            report.Lines.Add(LocalizedLine.Err(failureKey, exception.Message));
        }
    }

    private static bool AddRetryableOperationLines(
        OperationReport report,
        Func<IReadOnlyList<OperationLine>> operation)
    {
        try
        {
            var operationLines = operation();
            report.Lines.AddRange(operationLines.Where(line =>
                !line.Level.Equals("ERR", StringComparison.OrdinalIgnoreCase)));

            var failures = operationLines
                .Where(line => line.Level.Equals("ERR", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (failures.Count == 0)
                return true;

            report.Lines.Add(LocalizedLine.Info(
                "engine.retryableNetworkStep",
                failures.Count,
                failures[0].Text));
            return false;
        }
        catch (Exception exception)
        {
            report.Lines.Add(LocalizedLine.Info(
                "engine.retryableNetworkStep",
                1,
                exception.Message));
            return false;
        }
    }

    private static IReadOnlyList<T> SafeQuery<T>(
        Func<IReadOnlyList<T>> query,
        IReadOnlyList<T> fallback,
        OperationReport report,
        string failureKey)
    {
        try
        {
            return query();
        }
        catch (Exception exception)
        {
            report.Lines.Add(LocalizedLine.Err(failureKey, exception.Message));
            return fallback;
        }
    }

    private static bool HasErrors(OperationReport report)
        => report.Lines.Any(line => line.Level.Equals("ERR", StringComparison.OrdinalIgnoreCase));

    private static int CountErrors(OperationReport report)
        => report.Lines.Count(line => line.Level.Equals("ERR", StringComparison.OrdinalIgnoreCase));

    private static FirewallProtectionStatus EmptyFirewallStatus(string error)
        => new(
            false,
            new FirewallTargetSet(null, string.Empty, string.Empty, new HashSet<string>(), new HashSet<string>()),
            [],
            [],
            [],
            false,
            error);

    private sealed record ActivationPlan(
        IReadOnlyList<ServiceItem> ServicesToConfigure,
        IReadOnlyList<ServiceItem> ServicesToStop,
        IReadOnlyList<TaskItem> TasksToDisable,
        IReadOnlyList<TaskItem> TasksToStop,
        IReadOnlyList<RunEntry> RunEntriesToRemove);

    private enum ApplyTamedStateResult
    {
        Applied,
        NetworkIncomplete,
        Failed
    }

    private enum NetworkVerificationMode
    {
        Required,
        Retryable
    }
}
