namespace OmenGamingHubUnlocker.Core;

/// <summary>
/// Builds user-facing status snapshots without mixing presentation state into operation orchestration.
/// </summary>
public sealed class UnlockerStatusService(IUnlockerOperations operations)
{
    public StatusReport BuildTamedStatus()
    {
        var report = new StatusReport();

        AddProcessStatus(report);
        AddServiceStatus(report);
        AddTaskStatus(report);
        AddRunEntryStatus(report);
        AddFirewallStatus(report);
        AddHostsStatus(report);

        return report;
    }

    public IReadOnlyList<StatusSnapshot> BuildDisableStatus(
        UnlockerState state,
        UnlockerOptions options)
    {
        var snapshots = new List<StatusSnapshot>();
        AddRestoredServices(snapshots, state.Services);
        AddRestoredTasks(snapshots, state.Tasks);
        AddRestoredRunEntries(snapshots, state.RunEntries);

        if (options.ManageFirewall)
            AddDisabledFirewall(snapshots);

        if (options.ManageHosts)
            AddDisabledHosts(snapshots);

        return snapshots;
    }

    private void AddProcessStatus(StatusReport report)
    {
        try
        {
            foreach (var process in operations.QueryTargetProcesses())
            {
                report.RunningProcesses.Add(process.Label);
                report.Snapshots.Add(new StatusSnapshot
                {
                    Area = "Processes",
                    Item = process.Label,
                    Current = "Running",
                    Expected = "Not running while tamed",
                    Result = "WARN"
                });
            }
        }
        catch (Exception exception)
        {
            AddStatusError(report.Snapshots, "Processes", exception.Message);
        }
    }

    private void AddServiceStatus(StatusReport report)
    {
        try
        {
            var services = operations.QueryTargetServices();
            report.ServicesMatched = services.Count;

            foreach (var service in services.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
            {
                var isTamed = ServiceStatePolicy.IsManual(service) &&
                              ServiceStatePolicy.IsStopped(service);
                report.Snapshots.Add(new StatusSnapshot
                {
                    Area = "Services",
                    Item = service.Name,
                    Current = $"{service.StartMode}, {service.State}",
                    Expected = "Manual, Stopped",
                    Result = isTamed ? "OK" : "WARN"
                });
            }
        }
        catch (Exception exception)
        {
            AddStatusError(report.Snapshots, "Services", exception.Message);
        }
    }

    private void AddTaskStatus(StatusReport report)
    {
        try
        {
            var tasks = operations.QueryTargetTasks();
            report.TasksMatched = tasks.Count;

            foreach (var task in tasks.OrderBy(item => item.Path, StringComparer.OrdinalIgnoreCase))
            {
                var isTamed = !task.Enabled && !task.RequiresStop;
                report.Snapshots.Add(new StatusSnapshot
                {
                    Area = "Tasks",
                    Item = task.Path,
                    Current = $"{(task.Enabled ? "Enabled" : "Disabled")}, {task.State}",
                    Expected = "Disabled, Not running",
                    Result = isTamed ? "OK" : "WARN"
                });
            }
        }
        catch (Exception exception)
        {
            AddStatusError(report.Snapshots, "Tasks", exception.Message);
        }
    }

    private void AddRunEntryStatus(StatusReport report)
    {
        try
        {
            var entries = operations.QueryTargetRunEntries();
            report.RunEntriesMatched = entries.Count;

            foreach (var entry in entries
                         .OrderBy(item => item.Location, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
            {
                report.Snapshots.Add(new StatusSnapshot
                {
                    Area = "Autostart (Run)",
                    Item = $"{entry.Location} :: {entry.Name}",
                    Current = "Present",
                    Expected = "Removed",
                    Result = "WARN"
                });
            }
        }
        catch (Exception exception)
        {
            AddStatusError(report.Snapshots, "Autostart (Run)", exception.Message);
        }
    }

    private void AddFirewallStatus(StatusReport report)
    {
        try
        {
            var status = operations.InspectFirewallProtection();
            report.FirewallRulesFound = status.RuleCount;
            report.Snapshots.Add(new StatusSnapshot
            {
                Area = "Firewall",
                Item = Text.Format("status.firewallRulesItem", OmenTargets.FirewallRulePrefix),
                Current = status.QuerySucceeded
                    ? Text.Format(
                        "status.firewallDetailed",
                        status.RuleCount,
                        status.MissingExecutableRules.Count,
                        status.StaleExecutableRules.Count,
                        status.PackageRulePresent ? Text.Get("state.true") : Text.Get("state.false"))
                    : status.Error,
                Expected = "Current package and executable paths blocked",
                Result = !status.QuerySucceeded ? "ERR" : status.IsComplete ? "OK" : "WARN"
            });
        }
        catch (Exception exception)
        {
            AddStatusError(report.Snapshots, "Firewall", exception.Message);
        }
    }

    private void AddHostsStatus(StatusReport report)
    {
        try
        {
            var status = operations.InspectHosts();
            if (!status.Success)
            {
                AddStatusError(report.Snapshots, "hosts", status.Error);
                return;
            }

            foreach (var domain in status.Domains)
            {
                report.Snapshots.Add(new StatusSnapshot
                {
                    Area = "hosts",
                    Item = domain.Domain,
                    Current = domain.Blocked ? "Blocked" : "Not blocked",
                    Expected = "Blocked",
                    Result = domain.Blocked ? "OK" : "WARN"
                });
            }
        }
        catch (Exception exception)
        {
            AddStatusError(report.Snapshots, "hosts", exception.Message);
        }
    }

    private void AddRestoredServices(
        List<StatusSnapshot> snapshots,
        IReadOnlyCollection<ServiceBackup> backups)
    {
        try
        {
            var services = operations.QueryTargetServices()
                .ToDictionary(service => service.Name, StringComparer.OrdinalIgnoreCase);
            foreach (var backup in backups)
            {
                var exists = services.TryGetValue(backup.Name, out var service);
                var restored = exists &&
                               ServiceStatePolicy.MatchesBackup(service!, backup) &&
                               ServiceStatePolicy.MatchesOriginalRunningState(service!, backup);
                snapshots.Add(new StatusSnapshot
                {
                    Area = "Services",
                    Item = backup.Name,
                    Current = exists
                        ? $"{service!.StartMode}, {service.State}"
                        : Text.Get("state.notInstalled"),
                    Expected = $"{backup.OriginalStartMode}, {(backup.OriginalRunning ? "Running" : "Stopped")}",
                    Result = !exists ? "INFO" : restored ? "OK" : "ERR"
                });
            }
        }
        catch (Exception exception)
        {
            AddStatusError(snapshots, "Services", exception.Message);
        }
    }

    private void AddRestoredTasks(
        List<StatusSnapshot> snapshots,
        IReadOnlyCollection<TaskBackup> backups)
    {
        try
        {
            var tasks = operations.QueryTargetTasks()
                .ToDictionary(task => task.Path, StringComparer.OrdinalIgnoreCase);
            foreach (var backup in backups)
            {
                var exists = tasks.TryGetValue(backup.Path, out var task);
                snapshots.Add(new StatusSnapshot
                {
                    Area = "Tasks",
                    Item = backup.Path,
                    Current = exists
                        ? task!.Enabled ? "Enabled" : "Disabled"
                        : Text.Get("state.notInstalled"),
                    Expected = backup.OriginalEnabled ? "Enabled" : "Disabled",
                    Result = !exists ? "INFO" : task!.Enabled == backup.OriginalEnabled ? "OK" : "ERR"
                });
            }
        }
        catch (Exception exception)
        {
            AddStatusError(snapshots, "Tasks", exception.Message);
        }
    }

    private void AddRestoredRunEntries(
        List<StatusSnapshot> snapshots,
        IReadOnlyCollection<RunEntryBackup> backups)
    {
        try
        {
            var entries = operations.QueryTargetRunEntries()
                .ToDictionary(
                    entry => $"{entry.Hive}|{entry.View}|{entry.Name}",
                    StringComparer.OrdinalIgnoreCase);
            foreach (var backup in backups)
            {
                var key = $"{backup.Hive}|{backup.View}|{backup.Name}";
                var restored = entries.TryGetValue(key, out var entry) &&
                               entry.Value.Equals(backup.Value, StringComparison.Ordinal) &&
                               entry.ValueKind == backup.ValueKind;
                snapshots.Add(new StatusSnapshot
                {
                    Area = "Autostart (Run)",
                    Item = backup.Name,
                    Current = restored ? "Present" : "Missing",
                    Expected = "Present",
                    Result = restored ? "OK" : "ERR"
                });
            }
        }
        catch (Exception exception)
        {
            AddStatusError(snapshots, "Autostart (Run)", exception.Message);
        }
    }

    private void AddDisabledFirewall(List<StatusSnapshot> snapshots)
    {
        try
        {
            var firewall = operations.InspectFirewallProtection();
            snapshots.Add(new StatusSnapshot
            {
                Area = "Firewall",
                Item = Text.Format("status.firewallRulesItem", OmenTargets.FirewallRulePrefix),
                Current = firewall.RuleCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Expected = "0",
                Result = firewall.QuerySucceeded && firewall.RuleCount == 0 ? "OK" : "ERR"
            });
        }
        catch (Exception exception)
        {
            AddStatusError(snapshots, "Firewall", exception.Message);
        }
    }

    private void AddDisabledHosts(List<StatusSnapshot> snapshots)
    {
        try
        {
            var hosts = operations.InspectHosts();
            snapshots.Add(new StatusSnapshot
            {
                Area = "hosts",
                Item = OmenTargets.HostsMarker,
                Current = hosts.ManagedLineCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Expected = "0",
                Result = hosts.Success && hosts.ManagedLineCount == 0 ? "OK" : "ERR"
            });
        }
        catch (Exception exception)
        {
            AddStatusError(snapshots, "hosts", exception.Message);
        }
    }

    private static void AddStatusError(
        List<StatusSnapshot> snapshots,
        string area,
        string error)
    {
        snapshots.Add(new StatusSnapshot
        {
            Area = area,
            Item = Text.Get("status.inspectionFailed"),
            Current = error,
            Expected = Text.Get("status.inspectionAvailable"),
            Result = "ERR"
        });
    }
}
