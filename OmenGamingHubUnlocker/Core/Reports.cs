namespace OmenGamingHubUnlocker.Core;

/// <summary>
/// Represents a single status row rendered in the console snapshot table.
/// </summary>
public sealed class StatusSnapshot
{
    public string Area { get; init; } = string.Empty;
    public string Item { get; init; } = string.Empty;
    public string Current { get; init; } = string.Empty;
    public string Expected { get; init; } = string.Empty;
    public string Result { get; init; } = string.Empty;
}

/// <summary>
/// Represents a single execution log line emitted during an operation.
/// </summary>
public sealed class OperationLine
{
    public string Level { get; init; } = "INFO";
    public string Text { get; init; } = string.Empty;
}

/// <summary>
/// Describes the current machine state from the unlocker's perspective.
/// </summary>
public sealed class StatusReport
{
    public List<StatusSnapshot> Snapshots { get; } = [];
    public List<string> RunningProcesses { get; } = [];

    public int ServicesMatched { get; set; }
    public int TasksMatched { get; set; }
    public int RunEntriesMatched { get; set; }
    public int FirewallRulesFound { get; set; }

    public string ToPrettyText()
    {
        return Text.Format("reports.servicesMatched", ServicesMatched) + "\n" +
               Text.Format("reports.tasksMatched", TasksMatched) + "\n" +
               Text.Format("reports.runEntriesMatched", RunEntriesMatched) + "\n" +
               Text.Format("reports.firewallRules", OmenTargets.FirewallRulePrefix, FirewallRulesFound) + "\n" +
               Text.Format("reports.runningProcesses", RunningProcesses.Count) + "\n";
    }
}

/// <summary>
/// Captures the result of a mutating or dry-run operation.
/// </summary>
public sealed class OperationReport
{
    public bool Success { get; set; }
    public string Title { get; set; } = string.Empty;
    public List<OperationLine> Lines { get; } = [];
    public List<StatusSnapshot> SnapshotsAfter { get; } = [];

    public static OperationReport Ok(string title) => new() { Success = true, Title = title };
    public static OperationReport Fail(string title) => new() { Success = false, Title = title };
}
