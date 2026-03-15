namespace OmenGamingHubUnlocker.Core;

/// <summary>
/// Coordinates every read/write operation needed to inspect, tame, restore, or reset OMEN Gaming Hub.
/// </summary>
public sealed class UnlockerEngine
{
    private const int ActivationStabilizationAttempts = 2;
    private const int ActivationStabilizationDelayMs = 3_000;

    private readonly UnlockerStateStore _stateStore = new();

    /// <summary>
    /// Builds a neutral snapshot of the current system state.
    /// </summary>
    public StatusReport GetStatusReport()
    {
        var report = new StatusReport();

        foreach (var process in ProcessManager.FindMatchingProcesses(OmenTargets.ProcessNamePatterns))
        {
            var label = $"{process.ProcessName} (PID {process.Id})";
            report.RunningProcesses.Add(label);
            report.Snapshots.Add(new StatusSnapshot
            {
                Area = "Processes",
                Item = label,
                Current = "Running",
                Expected = "Not running while tamed",
                Result = "WARN"
            });
        }

        var services = ServiceManager.QueryServices(OmenTargets.ServicePatterns);
        report.ServicesMatched = services.Count;

        foreach (var service in services.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
        {
            var ok = service.StartMode.Equals("Manual", StringComparison.OrdinalIgnoreCase);
            report.Snapshots.Add(new StatusSnapshot
            {
                Area = "Services",
                Item = service.Name,
                Current = service.StartMode,
                Expected = "Manual (tamed)",
                Result = ok ? "OK" : "WARN"
            });
        }

        var tasks = TaskSchedulerManager.QueryTasks(OmenTargets.TaskPatterns);
        report.TasksMatched = tasks.Count;

        foreach (var task in tasks.OrderBy(x => x.Path, StringComparer.OrdinalIgnoreCase))
        {
            var ok = !task.Enabled;
            report.Snapshots.Add(new StatusSnapshot
            {
                Area = "Tasks",
                Item = task.Path,
                Current = task.Enabled ? "Enabled" : "Disabled",
                Expected = "Disabled (tamed)",
                Result = ok ? "OK" : "WARN"
            });
        }

        var runEntries = RegistryRunManager.QueryRunEntries(OmenTargets.RunEntryPatterns);
        report.RunEntriesMatched = runEntries.Count;

        foreach (var entry in runEntries.OrderBy(x => x.Location, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
        {
            report.Snapshots.Add(new StatusSnapshot
            {
                Area = "Autostart (Run)",
                Item = $"{entry.Location} :: {entry.Name}",
                Current = "Present",
                Expected = "Removed (tamed)",
                Result = "WARN"
            });
        }

        report.FirewallRulesFound = FirewallManager.CountRulesByPrefix(OmenTargets.FirewallRulePrefix);
        report.Snapshots.Add(new StatusSnapshot
        {
            Area = "Firewall",
            Item = Text.Format("status.firewallRulesItem", OmenTargets.FirewallRulePrefix),
            Current = report.FirewallRulesFound.ToString(),
            Expected = ">= 1 (when activated)",
            Result = report.FirewallRulesFound > 0 ? "OK" : "INFO"
        });

        var hostsStatus = HostsManager.GetDomainsStatus(OmenTargets.HostsDomains, OmenTargets.HostsMarker);
        foreach (var domain in hostsStatus)
        {
            report.Snapshots.Add(new StatusSnapshot
            {
                Area = "hosts",
                Item = domain.Domain,
                Current = domain.Blocked ? "Blocked" : "Not blocked",
                Expected = "Blocked (when activated)",
                Result = domain.Blocked ? "OK" : "INFO"
            });
        }

        return report;
    }

    /// <summary>
    /// Performs a non-mutating capability check and preview of what activation would touch.
    /// </summary>
    public OperationReport RunDryRunDeep()
    {
        var report = OperationReport.Ok(Text.Get("engine.title.dryRunCompleted"));

        var checks = new List<(string Name, Func<(bool ok, string details)> Fn)>
        {
            (Text.Get("engine.check.taskSchedulerCom"), TaskSchedulerManager.CheckCapability),
            (Text.Get("engine.check.firewallCom"), FirewallManager.CheckCapability),
            (Text.Get("engine.check.wmiServices"), ServiceManager.CheckCapability),
            (Text.Get("engine.check.hostsWriteAccess"), () => HostsManager.CheckWriteAccess(OmenTargets.HostsMarker)),
            (Text.Get("engine.check.powerShellAvailability"), PowerShellRunner.CheckAvailability),
            (Text.Get("engine.check.netshAvailability"), PowerShellRunner.CheckNetshAvailability),
            (Text.Get("engine.check.appxResetCapability"), AppxPackageManager.CheckResetCapability)
        };

        foreach (var (name, fn) in checks)
        {
            try
            {
                var (ok, details) = fn();
                report.Lines.Add(new OperationLine
                {
                    Level = ok ? "OK" : "WARN",
                    Text = Text.Format("engine.check.result", name, ok ? Text.Get("engine.check.okLabel") : Text.Get("engine.check.notOkLabel"), details)
                });
            }
            catch (Exception ex)
            {
                report.Lines.Add(LocalizedLine.Warn("engine.check.failed", name, ex.Message));
            }
        }

        if (AppxPackageManager.TryGetPrimaryPackage(OmenTargets.AppxFilters, out _, out var packageDetails))
            report.Lines.Add(LocalizedLine.Ok("engine.appxTarget", packageDetails));
        else
            report.Lines.Add(LocalizedLine.Warn("engine.appxTarget", packageDetails));

        var plan = CollectActivationPlan();
        report.Lines.Add(new OperationLine
        {
            Level = "INFO",
            Text = Text.Format("engine.activationPlan", plan.ServicesToManual.Count, plan.TasksToDisable.Count, plan.RunEntriesToRemove.Count)
        });

        var state = _stateStore.Load();
        report.Lines.Add(new OperationLine
        {
            Level = "INFO",
            Text = Text.Format("engine.rollbackBackup", state.Services.Count, state.Tasks.Count, state.RunEntries.Count)
        });

        try
        {
            var exes = FirewallManager.DiscoverCandidateExecutables();
            report.Lines.Add(LocalizedLine.Info("engine.executableDiscoveryFound", exes.Count));

            foreach (var exe in exes.Take(25))
                report.Lines.Add(new OperationLine { Level = "INFO", Text = $"  - {exe}" });

            if (exes.Count > 25)
                report.Lines.Add(LocalizedLine.Info("common.moreItems", exes.Count - 25));
        }
        catch (Exception ex)
        {
            report.Lines.Add(LocalizedLine.Warn("engine.executableDiscoveryFailed", ex.Message));
        }

        report.SnapshotsAfter.AddRange(GetStatusReport().Snapshots);
        return report;
    }

    /// <summary>
    /// Applies the standard tame configuration.
    /// </summary>
    public OperationReport Activate(UnlockerOptions options)
    {
        var report = OperationReport.Ok(Text.Get("engine.title.activationCompleted"));
        RunActivationFlow(report, options, Text.Get("engine.flow.activateScripts"), includeProcessTermination: true);
        RunActivationStabilizationSweeps(report, options, Text.Get("engine.flow.activationStabilization"), killProcesses: options.TryKillProcesses);
        CompleteReport(report, Text.Get("engine.title.activationFailed"));
        return report;
    }

    /// <summary>
    /// Restores the last known pre-activation state from the persisted backup.
    /// </summary>
    public OperationReport Disable(UnlockerOptions options)
    {
        var report = OperationReport.Ok(Text.Get("engine.title.disableCompleted"));
        RunDisableFlow(report, options, Text.Get("engine.flow.disableScripts"));
        CompleteReport(report, Text.Get("engine.title.disableFailed"));

        if (!options.DryRun && report.Success)
        {
            _stateStore.Clear();
            report.Lines.Add(LocalizedLine.Info("engine.stateBackupCleared"));
        }

        return report;
    }

    /// <summary>
    /// Executes a full Windows app reset and immediately re-applies tame mode afterwards.
    /// </summary>
    public OperationReport ResetAndReapply(UnlockerOptions options)
    {
        var report = OperationReport.Ok(Text.Get("engine.title.resetCompleted"));

        report.Lines.Add(LocalizedLine.Info("engine.resetStarted"));

        AddProcessTerminationLines(report, options);

        try
        {
            report.Lines.AddRange(AppxPackageManager.ResetPackage(OmenTargets.AppxFilters, options.DryRun));
        }
        catch (Exception ex)
        {
            report.Lines.Add(LocalizedLine.Err("engine.resetUnexpectedFailure", ex.Message));
        }

        WaitForResetSideEffectsToSettle(report, options);
        RunActivationFlow(report, options, Text.Get("engine.flow.refreshAfterReset"), includeProcessTermination: false);
        RunActivationStabilizationSweeps(report, options, Text.Get("engine.flow.postResetStabilization"), killProcesses: true);
        CompleteReport(report, Text.Get("engine.title.resetFailed"));
        return report;
    }

    private void RunActivationFlow(OperationReport report, UnlockerOptions options, string title, bool includeProcessTermination)
    {
        report.Lines.Add(LocalizedLine.Info("common.started", title));

        var activationPlan = CollectActivationPlan();
        SaveActivationBackups(activationPlan, options, report);

        if (includeProcessTermination)
            AddProcessTerminationLines(report, options);

        ApplyActivationPlan(report, activationPlan, options);

        ApplyFirewall(report, options, activate: true);
        ApplyHosts(report, options, activate: true);

        report.Lines.Add(LocalizedLine.Info("common.finished", title));
    }

    private void RunDisableFlow(OperationReport report, UnlockerOptions options, string title)
    {
        report.Lines.Add(LocalizedLine.Info("common.started", title));

        ApplyFirewall(report, options, activate: false);
        ApplyHosts(report, options, activate: false);

        var state = _stateStore.Load();

        try
        {
            report.Lines.AddRange(RegistryRunManager.RestoreEntries(state.RunEntries, options.DryRun));
        }
        catch (Exception ex)
        {
            report.Lines.Add(LocalizedLine.Err("engine.registryRestoreFailed", ex.Message));
        }

        try
        {
            var taskTargets = state.Tasks.Select(x => new TaskEnableTarget(x.Path, x.OriginalEnabled));
            report.Lines.AddRange(TaskSchedulerManager.ApplyEnabledTargets(taskTargets, options.DryRun));
        }
        catch (Exception ex)
        {
            report.Lines.Add(LocalizedLine.Err("engine.tasksRestoreFailed", ex.Message));
        }

        try
        {
            var serviceTargets = state.Services.Select(x => new ServiceStartModeTarget(x.Name, x.OriginalStartMode));
            report.Lines.AddRange(ServiceManager.ApplyStartModeTargets(serviceTargets, options.DryRun));
        }
        catch (Exception ex)
        {
            report.Lines.Add(LocalizedLine.Err("engine.servicesRestoreFailed", ex.Message));
        }

        report.Lines.Add(LocalizedLine.Info("common.finished", title));
    }

    private void SaveActivationBackups(ActivationPlan activationPlan, UnlockerOptions options, OperationReport report)
    {
        if (options.DryRun)
        {
            report.Lines.Add(LocalizedLine.Info("engine.stateBackupSkipped"));
            return;
        }

        try
        {
            var serviceBackups = activationPlan.ServicesToManual.Select(x => new ServiceBackup(x.Name, x.StartMode));
            var taskBackups = activationPlan.TasksToDisable.Select(x => new TaskBackup(x.Path, x.Enabled));
            var runEntryBackups = activationPlan.RunEntriesToRemove.Select(x => new RunEntryBackup(x.Hive, x.View, x.Name, x.Value));

            _stateStore.PersistBackups(serviceBackups, taskBackups, runEntryBackups);

            report.Lines.Add(new OperationLine
            {
                Level = "INFO",
                Text = Text.Format("engine.stateBackupSaved", activationPlan.ServicesToManual.Count, activationPlan.TasksToDisable.Count, activationPlan.RunEntriesToRemove.Count)
            });
        }
        catch (Exception ex)
        {
            report.Lines.Add(LocalizedLine.Err("engine.stateBackupFailed", ex.Message));
        }
    }

    private void RunActivationStabilizationSweeps(OperationReport report, UnlockerOptions options, string title, bool killProcesses)
    {
        if (options.DryRun)
        {
            report.Lines.Add(LocalizedLine.Info("common.skippedInDryRun", title));
            return;
        }

        for (var attempt = 1; attempt <= ActivationStabilizationAttempts; attempt++)
        {
            Thread.Sleep(ActivationStabilizationDelayMs);

            var runningProcesses = ProcessManager.FindMatchingProcesses(OmenTargets.ProcessNamePatterns);
            var activationPlan = CollectActivationPlan();

            var hasPendingChanges =
                runningProcesses.Count > 0 ||
                activationPlan.ServicesToManual.Count > 0 ||
                activationPlan.TasksToDisable.Count > 0 ||
                activationPlan.RunEntriesToRemove.Count > 0;

            if (!hasPendingChanges)
            {
                report.Lines.Add(new OperationLine
                {
                    Level = "INFO",
                    Text = Text.Format("engine.stabilizationSystemStable", title, attempt - 1)
                });
                return;
            }

            report.Lines.Add(new OperationLine
            {
                Level = "INFO",
                Text = Text.Format("engine.stabilizationSweepFound", title, attempt, runningProcesses.Count, activationPlan.ServicesToManual.Count, activationPlan.TasksToDisable.Count, activationPlan.RunEntriesToRemove.Count)
            });

            SaveActivationBackups(activationPlan, options, report);

            if (killProcesses)
                AddProcessTerminationLines(report, options);

            ApplyActivationPlan(report, activationPlan, options);
        }
    }

    private static void WaitForResetSideEffectsToSettle(OperationReport report, UnlockerOptions options)
    {
        if (options.DryRun)
        {
            report.Lines.Add(LocalizedLine.Info("engine.postResetSettleSkipped"));
            return;
        }

        report.Lines.Add(new OperationLine
        {
            Level = "INFO",
            Text = Text.Format("engine.waitingPostResetSettle", ActivationStabilizationDelayMs / 1000)
        });

        Thread.Sleep(ActivationStabilizationDelayMs);
    }

    private static void ApplyActivationPlan(OperationReport report, ActivationPlan activationPlan, UnlockerOptions options)
    {
        try
        {
            var serviceTargets = activationPlan.ServicesToManual
                .Select(x => new ServiceStartModeTarget(x.Name, "Manual"));

            report.Lines.AddRange(ServiceManager.ApplyStartModeTargets(serviceTargets, options.DryRun));
        }
        catch (Exception ex)
        {
            report.Lines.Add(LocalizedLine.Err("engine.servicesStepFailed", ex.Message));
        }

        try
        {
            var taskTargets = activationPlan.TasksToDisable
                .Select(x => new TaskEnableTarget(x.Path, false));

            report.Lines.AddRange(TaskSchedulerManager.ApplyEnabledTargets(taskTargets, options.DryRun));
        }
        catch (Exception ex)
        {
            report.Lines.Add(LocalizedLine.Err("engine.tasksStepFailed", ex.Message));
        }

        try
        {
            report.Lines.AddRange(RegistryRunManager.RemoveEntries(activationPlan.RunEntriesToRemove, options.DryRun));
        }
        catch (Exception ex)
        {
            report.Lines.Add(LocalizedLine.Err("engine.registryRunStepFailed", ex.Message));
        }
    }

    private static void AddProcessTerminationLines(OperationReport report, UnlockerOptions options)
    {
        if (!options.TryKillProcesses)
            return;

        try
        {
            var killed = ProcessManager.TryKillMatchingProcesses(OmenTargets.ProcessNamePatterns, options.DryRun);
            report.Lines.Add(new OperationLine
            {
                Level = "INFO",
                Text = options.DryRun
                    ? Text.Format("engine.processTerminationDryRun", killed.Count)
                    : Text.Format("engine.processTerminationDone", killed.Count)
            });

            foreach (var item in killed.Take(12))
                report.Lines.Add(new OperationLine { Level = "INFO", Text = $"  - {item}" });

            if (killed.Count > 12)
                report.Lines.Add(LocalizedLine.Info("common.moreItems", killed.Count - 12));
        }
        catch (Exception ex)
        {
            report.Lines.Add(LocalizedLine.Warn("engine.processTerminationFailed", ex.Message));
        }
    }

    private static void ApplyFirewall(OperationReport report, UnlockerOptions options, bool activate)
    {
        if (!options.ManageFirewall)
        {
            report.Lines.Add(LocalizedLine.Info("engine.firewallStepSkipped"));
            return;
        }

        try
        {
            var lines = activate
                ? FirewallManager.ActivateFirewallBlock(OmenTargets.FirewallRulePrefix, options.DryRun)
                : FirewallManager.DisableFirewallBlock(OmenTargets.FirewallRulePrefix, options.DryRun);

            report.Lines.AddRange(lines);
        }
        catch (Exception ex)
        {
            report.Lines.Add(LocalizedLine.Err("engine.firewallStepFailed", ex.Message));
        }
    }

    private static void ApplyHosts(OperationReport report, UnlockerOptions options, bool activate)
    {
        if (!options.ManageHosts)
        {
            report.Lines.Add(LocalizedLine.Info("engine.hostsStepSkipped"));
            return;
        }

        try
        {
            var lines = activate
                ? HostsManager.ActivateHostsBlock(OmenTargets.HostsDomains, OmenTargets.HostsMarker, options.DryRun)
                : HostsManager.DisableHostsBlock(OmenTargets.HostsMarker, options.DryRun);

            report.Lines.AddRange(lines);
        }
        catch (Exception ex)
        {
            report.Lines.Add(LocalizedLine.Err("engine.hostsStepFailed", ex.Message));
        }
    }

    private void CompleteReport(OperationReport report, string errorTitle)
    {
        report.SnapshotsAfter.Clear();
        report.SnapshotsAfter.AddRange(GetStatusReport().Snapshots);

        report.Success = report.Lines.All(x => x.Level != "ERR");
        if (!report.Success)
            report.Title = errorTitle;
    }

    private static ActivationPlan CollectActivationPlan()
    {
        var services = ServiceManager.QueryServices(OmenTargets.ServicePatterns)
            .DistinctBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .Where(x => !x.StartMode.Equals("Manual", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var tasks = TaskSchedulerManager.QueryTasks(OmenTargets.TaskPatterns)
            .DistinctBy(x => x.Path, StringComparer.OrdinalIgnoreCase)
            .Where(x => x.Enabled)
            .ToList();

        var runEntries = RegistryRunManager.QueryRunEntries(OmenTargets.RunEntryPatterns)
            .DistinctBy(x => $"{x.Hive}|{x.View}|{x.Name}", StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new ActivationPlan(services, tasks, runEntries);
    }

    private sealed record ActivationPlan(
        List<ServiceItem> ServicesToManual,
        List<TaskItem> TasksToDisable,
        List<RunEntry> RunEntriesToRemove);
}
