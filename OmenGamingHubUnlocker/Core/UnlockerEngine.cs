using OmenGamingHubUnlocker.Windows;

namespace OmenGamingHubUnlocker.Core;

public sealed class UnlockerEngine
{
    private const int ActivationStabilizationAttempts = 2;
    private const int ActivationStabilizationDelayMs = 3_000;

    private readonly UnlockerStateStore _stateStore = new();

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
            Item = $"Rules: {OmenTargets.FirewallRulePrefix} - *",
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

    public OperationReport RunDryRunDeep()
    {
        var report = OperationReport.Ok("Dry run completed (no changes applied).");

        var checks = new List<(string Name, Func<(bool ok, string details)> Fn)>
        {
            ("Task Scheduler COM", TaskSchedulerManager.CheckCapability),
            ("Firewall COM (HNetCfg)", FirewallManager.CheckCapability),
            ("WMI Services (Win32_Service)", ServiceManager.CheckCapability),
            ("hosts write access", () => HostsManager.CheckWriteAccess(OmenTargets.HostsMarker)),
            ("PowerShell availability", PowerShellRunner.CheckAvailability),
            ("netsh availability", PowerShellRunner.CheckNetshAvailability),
            ("AppX reset capability", AppxPackageManager.CheckResetCapability)
        };

        foreach (var (name, fn) in checks)
        {
            try
            {
                var (ok, details) = fn();
                report.Lines.Add(new OperationLine
                {
                    Level = ok ? "OK" : "WARN",
                    Text = $"{name}: {(ok ? "OK" : "NOT OK")} - {details}"
                });
            }
            catch (Exception ex)
            {
                report.Lines.Add(new OperationLine { Level = "WARN", Text = $"{name}: check failed - {ex.Message}" });
            }
        }

        if (AppxPackageManager.TryGetPrimaryPackage(OmenTargets.AppxFilters, out _, out var packageDetails))
            report.Lines.Add(new OperationLine { Level = "OK", Text = $"AppX target: {packageDetails}" });
        else
            report.Lines.Add(new OperationLine { Level = "WARN", Text = $"AppX target: {packageDetails}" });

        var plan = BuildActivationPlan();
        report.Lines.Add(new OperationLine
        {
            Level = "INFO",
            Text = $"Activation plan: {plan.ServicesToManual.Count} service(s), {plan.TasksToDisable.Count} task(s), {plan.RunEntriesToRemove.Count} Run entries."
        });

        var state = _stateStore.Load();
        report.Lines.Add(new OperationLine
        {
            Level = "INFO",
            Text = $"Rollback backup: {state.Services.Count} service(s), {state.Tasks.Count} task(s), {state.RunEntries.Count} Run entries."
        });

        try
        {
            var exes = FirewallManager.DiscoverCandidateExecutables();
            report.Lines.Add(new OperationLine { Level = "INFO", Text = $"Executable discovery: {exes.Count} candidate .exe file(s) found." });

            foreach (var exe in exes.Take(25))
                report.Lines.Add(new OperationLine { Level = "INFO", Text = $"  - {exe}" });

            if (exes.Count > 25)
                report.Lines.Add(new OperationLine { Level = "INFO", Text = $"  ... +{exes.Count - 25} more" });
        }
        catch (Exception ex)
        {
            report.Lines.Add(new OperationLine { Level = "WARN", Text = $"Executable discovery failed: {ex.Message}" });
        }

        report.SnapshotsAfter.AddRange(GetStatusReport().Snapshots);
        return report;
    }

    public OperationReport Activate(UnlockerOptions options)
    {
        var report = OperationReport.Ok("Activation completed.");
        ExecuteActivationFlow(report, options, "Activate scripts", includeProcessTermination: true);
        RunActivationStabilization(report, options, "Activation stabilization", killProcesses: options.TryKillProcesses);
        FinalizeReport(report, "Activation finished with errors.");
        return report;
    }

    public OperationReport Disable(UnlockerOptions options)
    {
        var report = OperationReport.Ok("Disable completed.");
        ExecuteDisableFlow(report, options, "Disable scripts");
        FinalizeReport(report, "Disable finished with errors.");

        if (!options.DryRun && report.Success)
        {
            _stateStore.Clear();
            report.Lines.Add(new OperationLine { Level = "INFO", Text = "State backup: cleared after successful restore." });
        }

        return report;
    }

    public OperationReport ResetAndReapply(UnlockerOptions options)
    {
        var report = OperationReport.Ok("Reset and reapply completed.");

        report.Lines.Add(new OperationLine { Level = "INFO", Text = "Reset and reapply: started." });

        AddProcessTerminationLines(report, options);

        try
        {
            report.Lines.AddRange(AppxPackageManager.ResetPackage(OmenTargets.AppxFilters, options.DryRun));
        }
        catch (Exception ex)
        {
            report.Lines.Add(new OperationLine { Level = "ERR", Text = $"Reset: unexpected failure - {ex.Message}" });
        }

        WaitForPostResetSettle(report, options);
        ExecuteActivationFlow(report, options, "Refresh taming after reset", includeProcessTermination: false);
        RunActivationStabilization(report, options, "Post-reset stabilization", killProcesses: true);
        FinalizeReport(report, "Reset and reapply finished with errors.");
        return report;
    }

    private void ExecuteActivationFlow(OperationReport report, UnlockerOptions options, string title, bool includeProcessTermination)
    {
        report.Lines.Add(new OperationLine { Level = "INFO", Text = $"{title}: started." });

        var plan = BuildActivationPlan();
        PersistActivationBackups(plan, options, report);

        if (includeProcessTermination)
            AddProcessTerminationLines(report, options);

        ApplyActivationPlan(report, plan, options);

        ApplyFirewall(report, options, activate: true);
        ApplyHosts(report, options, activate: true);

        report.Lines.Add(new OperationLine { Level = "INFO", Text = $"{title}: finished." });
    }

    private void ExecuteDisableFlow(OperationReport report, UnlockerOptions options, string title)
    {
        report.Lines.Add(new OperationLine { Level = "INFO", Text = $"{title}: started." });

        ApplyFirewall(report, options, activate: false);
        ApplyHosts(report, options, activate: false);

        var state = _stateStore.Load();

        try
        {
            report.Lines.AddRange(RegistryRunManager.RestoreEntries(state.RunEntries, options.DryRun));
        }
        catch (Exception ex)
        {
            report.Lines.Add(new OperationLine { Level = "ERR", Text = $"Registry Run restore failed: {ex.Message}" });
        }

        try
        {
            var taskTargets = state.Tasks.Select(x => new TaskEnableTarget(x.Path, x.OriginalEnabled));
            report.Lines.AddRange(TaskSchedulerManager.ApplyEnabledTargets(taskTargets, options.DryRun));
        }
        catch (Exception ex)
        {
            report.Lines.Add(new OperationLine { Level = "ERR", Text = $"Tasks restore failed: {ex.Message}" });
        }

        try
        {
            var serviceTargets = state.Services.Select(x => new ServiceStartModeTarget(x.Name, x.OriginalStartMode));
            report.Lines.AddRange(ServiceManager.ApplyStartModeTargets(serviceTargets, options.DryRun));
        }
        catch (Exception ex)
        {
            report.Lines.Add(new OperationLine { Level = "ERR", Text = $"Services restore failed: {ex.Message}" });
        }

        report.Lines.Add(new OperationLine { Level = "INFO", Text = $"{title}: finished." });
    }

    private void PersistActivationBackups(ActivationPlan plan, UnlockerOptions options, OperationReport report)
    {
        if (options.DryRun)
        {
            report.Lines.Add(new OperationLine { Level = "INFO", Text = "State backup: skipped in dry run." });
            return;
        }

        try
        {
            var serviceBackups = plan.ServicesToManual.Select(x => new ServiceBackup(x.Name, x.StartMode));
            var taskBackups = plan.TasksToDisable.Select(x => new TaskBackup(x.Path, x.Enabled));
            var runEntryBackups = plan.RunEntriesToRemove.Select(x => new RunEntryBackup(x.Hive, x.View, x.Name, x.Value));

            _stateStore.PersistBackups(serviceBackups, taskBackups, runEntryBackups);

            report.Lines.Add(new OperationLine
            {
                Level = "INFO",
                Text = $"State backup: saved {plan.ServicesToManual.Count} service(s), {plan.TasksToDisable.Count} task(s), {plan.RunEntriesToRemove.Count} Run entries."
            });
        }
        catch (Exception ex)
        {
            report.Lines.Add(new OperationLine { Level = "ERR", Text = $"State backup failed: {ex.Message}" });
        }
    }

    private void RunActivationStabilization(OperationReport report, UnlockerOptions options, string title, bool killProcesses)
    {
        if (options.DryRun)
        {
            report.Lines.Add(new OperationLine { Level = "INFO", Text = $"{title}: skipped in dry run." });
            return;
        }

        for (var attempt = 1; attempt <= ActivationStabilizationAttempts; attempt++)
        {
            Thread.Sleep(ActivationStabilizationDelayMs);

            var runningProcesses = ProcessManager.FindMatchingProcesses(OmenTargets.ProcessNamePatterns);
            var plan = BuildActivationPlan();

            var hasPendingChanges =
                runningProcesses.Count > 0 ||
                plan.ServicesToManual.Count > 0 ||
                plan.TasksToDisable.Count > 0 ||
                plan.RunEntriesToRemove.Count > 0;

            if (!hasPendingChanges)
            {
                report.Lines.Add(new OperationLine
                {
                    Level = "INFO",
                    Text = $"{title}: system is stable after sweep {attempt - 1}."
                });
                return;
            }

            report.Lines.Add(new OperationLine
            {
                Level = "INFO",
                Text = $"{title}: sweep {attempt} found {runningProcesses.Count} process(es), {plan.ServicesToManual.Count} service change(s), {plan.TasksToDisable.Count} task change(s), {plan.RunEntriesToRemove.Count} Run entry change(s)."
            });

            PersistActivationBackups(plan, options, report);

            if (killProcesses)
                AddProcessTerminationLines(report, options);

            ApplyActivationPlan(report, plan, options);
        }
    }

    private static void WaitForPostResetSettle(OperationReport report, UnlockerOptions options)
    {
        if (options.DryRun)
        {
            report.Lines.Add(new OperationLine { Level = "INFO", Text = "Post-reset settle wait: skipped in dry run." });
            return;
        }

        report.Lines.Add(new OperationLine
        {
            Level = "INFO",
            Text = $"Waiting {ActivationStabilizationDelayMs / 1000} second(s) for OMEN post-reset registration to settle."
        });

        Thread.Sleep(ActivationStabilizationDelayMs);
    }

    private static void ApplyActivationPlan(OperationReport report, ActivationPlan plan, UnlockerOptions options)
    {
        try
        {
            var serviceTargets = plan.ServicesToManual
                .Select(x => new ServiceStartModeTarget(x.Name, "Manual"));

            report.Lines.AddRange(ServiceManager.ApplyStartModeTargets(serviceTargets, options.DryRun));
        }
        catch (Exception ex)
        {
            report.Lines.Add(new OperationLine { Level = "ERR", Text = $"Services step failed: {ex.Message}" });
        }

        try
        {
            var taskTargets = plan.TasksToDisable
                .Select(x => new TaskEnableTarget(x.Path, false));

            report.Lines.AddRange(TaskSchedulerManager.ApplyEnabledTargets(taskTargets, options.DryRun));
        }
        catch (Exception ex)
        {
            report.Lines.Add(new OperationLine { Level = "ERR", Text = $"Tasks step failed: {ex.Message}" });
        }

        try
        {
            report.Lines.AddRange(RegistryRunManager.RemoveEntries(plan.RunEntriesToRemove, options.DryRun));
        }
        catch (Exception ex)
        {
            report.Lines.Add(new OperationLine { Level = "ERR", Text = $"Registry Run step failed: {ex.Message}" });
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
                    ? $"Dry run: would terminate {killed.Count} process(es)."
                    : $"Terminated {killed.Count} process(es)."
            });

            foreach (var item in killed.Take(12))
                report.Lines.Add(new OperationLine { Level = "INFO", Text = $"  - {item}" });

            if (killed.Count > 12)
                report.Lines.Add(new OperationLine { Level = "INFO", Text = $"  ... +{killed.Count - 12} more" });
        }
        catch (Exception ex)
        {
            report.Lines.Add(new OperationLine { Level = "WARN", Text = $"Process termination step failed: {ex.Message}" });
        }
    }

    private static void ApplyFirewall(OperationReport report, UnlockerOptions options, bool activate)
    {
        if (!options.ManageFirewall)
        {
            report.Lines.Add(new OperationLine { Level = "INFO", Text = "Firewall step skipped by options." });
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
            report.Lines.Add(new OperationLine { Level = "ERR", Text = $"Firewall step failed: {ex.Message}" });
        }
    }

    private static void ApplyHosts(OperationReport report, UnlockerOptions options, bool activate)
    {
        if (!options.ManageHosts)
        {
            report.Lines.Add(new OperationLine { Level = "INFO", Text = "hosts step skipped by options." });
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
            report.Lines.Add(new OperationLine { Level = "ERR", Text = $"hosts step failed: {ex.Message}" });
        }
    }

    private void FinalizeReport(OperationReport report, string errorTitle)
    {
        report.SnapshotsAfter.Clear();
        report.SnapshotsAfter.AddRange(GetStatusReport().Snapshots);

        report.Success = report.Lines.All(x => x.Level != "ERR");
        if (!report.Success)
            report.Title = errorTitle;
    }

    private static ActivationPlan BuildActivationPlan()
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
