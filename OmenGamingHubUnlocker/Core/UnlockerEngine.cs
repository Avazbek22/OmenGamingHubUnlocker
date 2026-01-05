using OmenGamingHubUnlocker.Windows;

namespace OmenGamingHubUnlocker.Core;

public sealed class UnlockerEngine
{
    public StatusReport GetStatusReport()
    {
        var report = new StatusReport();

        // Processes
        foreach (var p in ProcessManager.FindMatchingProcesses(OmenTargets.ProcessNamePatterns))
            report.RunningProcesses.Add($"{p.ProcessName} (PID {p.Id})");

        // Services
        var services = ServiceManager.QueryServices(OmenTargets.ServicePatterns);
        report.ServicesMatched = services.Count;

        foreach (var s in services.OrderBy(x => x.Name))
        {
            var expected = "Manual (tamed)";
            var current = s.StartMode;

            var ok = current.Equals("Manual", StringComparison.OrdinalIgnoreCase);
            report.Snapshots.Add(new StatusSnapshot
            {
                Area = "Services",
                Item = $"{s.Name}",
                Current = current,
                Expected = expected,
                Result = ok ? "OK" : "WARN"
            });
        }

        // Tasks
        var tasks = TaskSchedulerManager.QueryTasks(OmenTargets.TaskPatterns);
        report.TasksMatched = tasks.Count;

        foreach (var t in tasks.OrderBy(x => x.Path))
        {
            // In "tamed" mode tasks are Disabled
            var ok = !t.Enabled;
            report.Snapshots.Add(new StatusSnapshot
            {
                Area = "Tasks",
                Item = t.Path,
                Current = t.Enabled ? "Enabled" : "Disabled",
                Expected = "Disabled (tamed)",
                Result = ok ? "OK" : "WARN"
            });
        }

        // Run entries
        var runEntries = RegistryRunManager.QueryRunEntries(OmenTargets.RunEntryPatterns);
        report.RunEntriesMatched = runEntries.Count;

        foreach (var r in runEntries.OrderBy(x => x.Location).ThenBy(x => x.Name))
        {
            // In "tamed" mode run entries should NOT exist
            report.Snapshots.Add(new StatusSnapshot
            {
                Area = "Autostart (Run)",
                Item = $"{r.Location} :: {r.Name}",
                Current = "Present",
                Expected = "Removed (tamed)",
                Result = "WARN"
            });
        }

        // Firewall rules
        report.FirewallRulesFound = FirewallManager.CountRulesByPrefix(OmenTargets.FirewallRulePrefix);
        report.Snapshots.Add(new StatusSnapshot
        {
            Area = "Firewall",
            Item = $"Rules: {OmenTargets.FirewallRulePrefix} - *",
            Current = report.FirewallRulesFound.ToString(),
            Expected = ">= 1 (when activated)",
            Result = report.FirewallRulesFound > 0 ? "OK" : "INFO"
        });

        // Hosts
        var hostsStatus = HostsManager.GetDomainsStatus(OmenTargets.HostsDomains, OmenTargets.HostsMarker);
        foreach (var d in hostsStatus)
        {
            report.Snapshots.Add(new StatusSnapshot
            {
                Area = "hosts",
                Item = d.Domain,
                Current = d.Blocked ? "Blocked" : "Not blocked",
                Expected = "Blocked (when activated)",
                Result = d.Blocked ? "OK" : "INFO"
            });
        }

        return report;
    }

    public OperationReport RunDryRunDeep()
    {
        var rep = OperationReport.Ok("Dry run completed (no changes applied).");

        var checks = new List<(string Name, Func<(bool ok, string details)> Fn)>
        {
            ("Task Scheduler COM", TaskSchedulerManager.CheckCapability),
            ("Firewall COM (HNetCfg)", FirewallManager.CheckCapability),
            ("WMI Services (Win32_Service)", ServiceManager.CheckCapability),
            ("hosts write access", () => HostsManager.CheckWriteAccess(OmenTargets.HostsMarker)),
            ("PowerShell availability", PowerShellRunner.CheckAvailability),
            ("netsh availability", PowerShellRunner.CheckNetshAvailability),
        };

        foreach (var (name, fn) in checks)
        {
            try
            {
                var (ok, details) = fn();
                rep.Lines.Add(new OperationLine
                {
                    Level = ok ? "OK" : "WARN",
                    Text = $"{name}: {(ok ? "OK" : "NOT OK")} — {details}"
                });
            }
            catch (Exception ex)
            {
                rep.Lines.Add(new OperationLine { Level = "WARN", Text = $"{name}: check failed — {ex.Message}" });
            }
        }

        // Discovery: what we can block in Firewall
        try
        {
            var exes = FirewallManager.DiscoverCandidateExecutables();
            rep.Lines.Add(new OperationLine { Level = "INFO", Text = $"Executable discovery: {exes.Count} candidate .exe file(s) found." });

            foreach (var e in exes.Take(25))
                rep.Lines.Add(new OperationLine { Level = "INFO", Text = $"  - {e}" });

            if (exes.Count > 25)
                rep.Lines.Add(new OperationLine { Level = "INFO", Text = $"  ... +{exes.Count - 25} more" });
        }
        catch (Exception ex)
        {
            rep.Lines.Add(new OperationLine { Level = "WARN", Text = $"Executable discovery failed: {ex.Message}" });
        }

        // Snapshots (status view)
        var status = GetStatusReport();
        rep.SnapshotsAfter.AddRange(status.Snapshots);

        rep.Success = true;
        return rep;
    }

    public OperationReport Activate(UnlockerOptions options)
    {
        var rep = OperationReport.Ok("Activation completed.");

        ExecuteAggressiveFlow(
            rep,
            options,
            "Activate scripts",
            applyServices: true,
            disableTasks: true,
            removeRunEntries: true,
            firewallMode: FirewallApplyMode.Activate,
            hostsMode: HostsApplyMode.Activate);

        rep.SnapshotsAfter.AddRange(GetStatusReport().Snapshots);
        rep.Success = rep.Lines.All(l => l.Level != "ERR");
        if (!rep.Success) rep.Title = "Activation finished with errors.";
        return rep;
    }

    public OperationReport Disable(UnlockerOptions options)
    {
        var rep = OperationReport.Ok("Disable completed.");

        ExecuteAggressiveFlow(
            rep,
            options,
            "Disable scripts",
            applyServices: false,
            disableTasks: false,
            removeRunEntries: false, // cannot safely restore without backups
            firewallMode: FirewallApplyMode.Disable,
            hostsMode: HostsApplyMode.Disable);

        rep.Lines.Add(new OperationLine
        {
            Level = "WARN",
            Text = "Note: Autostart (Run) entries were removed during activation and cannot be restored without backups. " +
                   "If you need OMEN to autostart again, reinstall/repair OMEN Gaming Hub or enable startup from its settings (if available)."
        });

        rep.SnapshotsAfter.AddRange(GetStatusReport().Snapshots);
        rep.Success = rep.Lines.All(l => l.Level != "ERR");
        if (!rep.Success) rep.Title = "Disable finished with errors.";
        return rep;
    }

    private static void ExecuteAggressiveFlow(
        OperationReport rep,
        UnlockerOptions options,
        string title,
        bool applyServices,
        bool disableTasks,
        bool removeRunEntries,
        FirewallApplyMode firewallMode,
        HostsApplyMode hostsMode)
    {
        rep.Lines.Add(new OperationLine { Level = "INFO", Text = $"{title}: started." });

        // 1) Kill processes (optional)
        if (options.TryKillProcesses)
        {
            try
            {
                var killed = ProcessManager.TryKillMatchingProcesses(OmenTargets.ProcessNamePatterns, options.DryRun);
                rep.Lines.Add(new OperationLine
                {
                    Level = "INFO",
                    Text = options.DryRun
                        ? $"Dry run: would terminate {killed.Count} process(es)."
                        : $"Terminated {killed.Count} process(es)."
                });

                foreach (var k in killed.Take(12))
                    rep.Lines.Add(new OperationLine { Level = "INFO", Text = $"  - {k}" });

                if (killed.Count > 12)
                    rep.Lines.Add(new OperationLine { Level = "INFO", Text = $"  ... +{killed.Count - 12} more" });
            }
            catch (Exception ex)
            {
                rep.Lines.Add(new OperationLine { Level = "WARN", Text = $"Process termination step failed: {ex.Message}" });
            }
        }

        // 2) Services
        try
        {
            var res = applyServices
                ? ServiceManager.SetServicesStartMode(OmenTargets.ServicePatterns, "Manual", options.DryRun)
                : ServiceManager.SetServicesStartMode(OmenTargets.ServicePatterns, "Automatic", options.DryRun);

            rep.Lines.AddRange(res);
        }
        catch (Exception ex)
        {
            rep.Lines.Add(new OperationLine { Level = "ERR", Text = $"Services step failed: {ex.Message}" });
        }

        // 3) Tasks
        try
        {
            var res = disableTasks
                ? TaskSchedulerManager.SetTasksEnabled(OmenTargets.TaskPatterns, enabled: false, options.DryRun)
                : TaskSchedulerManager.SetTasksEnabled(OmenTargets.TaskPatterns, enabled: true, options.DryRun);

            rep.Lines.AddRange(res);
        }
        catch (Exception ex)
        {
            rep.Lines.Add(new OperationLine { Level = "ERR", Text = $"Tasks step failed: {ex.Message}" });
        }

        // 4) Run entries (activate only)
        if (removeRunEntries)
        {
            try
            {
                var res = RegistryRunManager.RemoveRunEntries(OmenTargets.RunEntryPatterns, options.DryRun);
                rep.Lines.AddRange(res);
            }
            catch (Exception ex)
            {
                rep.Lines.Add(new OperationLine { Level = "ERR", Text = $"Registry Run step failed: {ex.Message}" });
            }
        }

        // 5) Firewall
        if (options.ManageFirewall)
        {
            try
            {
                var res = firewallMode == FirewallApplyMode.Activate
                    ? FirewallManager.ActivateFirewallBlock(OmenTargets.FirewallRulePrefix, options.DryRun)
                    : FirewallManager.DisableFirewallBlock(OmenTargets.FirewallRulePrefix, options.DryRun);

                rep.Lines.AddRange(res);
            }
            catch (Exception ex)
            {
                rep.Lines.Add(new OperationLine { Level = "ERR", Text = $"Firewall step failed: {ex.Message}" });
            }
        }
        else
        {
            rep.Lines.Add(new OperationLine { Level = "INFO", Text = "Firewall step skipped by options." });
        }

        // 6) hosts
        if (options.ManageHosts)
        {
            try
            {
                var res = hostsMode == HostsApplyMode.Activate
                    ? HostsManager.ActivateHostsBlock(OmenTargets.HostsDomains, OmenTargets.HostsMarker, options.DryRun)
                    : HostsManager.DisableHostsBlock(OmenTargets.HostsMarker, options.DryRun);

                rep.Lines.AddRange(res);
            }
            catch (Exception ex)
            {
                rep.Lines.Add(new OperationLine { Level = "ERR", Text = $"hosts step failed: {ex.Message}" });
            }
        }
        else
        {
            rep.Lines.Add(new OperationLine { Level = "INFO", Text = "hosts step skipped by options." });
        }

        rep.Lines.Add(new OperationLine { Level = "INFO", Text = $"{title}: finished." });
    }

    private enum FirewallApplyMode { Activate, Disable }
    private enum HostsApplyMode { Activate, Disable }
}
