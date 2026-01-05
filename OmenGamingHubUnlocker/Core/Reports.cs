namespace OmenGamingHubUnlocker.Core;

public sealed class StatusSnapshot
{
    public string Area { get; init; } = "";
    public string Item { get; init; } = "";
    public string Current { get; init; } = "";
    public string Expected { get; init; } = "";
    public string Result { get; init; } = ""; // OK/WARN/INFO
}

public sealed class OperationLine
{
    public string Level { get; init; } = "INFO"; // OK/WARN/ERR/INFO
    public string Text { get; init; } = "";
}

public sealed class StatusReport
{
    public List<StatusSnapshot> Snapshots { get; } = [];

    public int ServicesMatched { get; set; }
    public int TasksMatched { get; set; }
    public int RunEntriesMatched { get; set; }
    public int FirewallRulesFound { get; set; }

    public List<string> RunningProcesses { get; } = [];

    public string ToPrettyText()
    {
        return $"Services matched: {ServicesMatched}\n" +
               $"Tasks matched: {TasksMatched}\n" +
               $"Run entries matched: {RunEntriesMatched}\n" +
               $"Firewall rules ({OmenTargets.FirewallRulePrefix}): {FirewallRulesFound}\n" +
               $"Running OMEN-related processes: {RunningProcesses.Count}\n";
    }
}

public sealed class OperationReport
{
    public bool Success { get; set; }
    public string Title { get; set; } = "";
    public List<OperationLine> Lines { get; } = [];
    public List<StatusSnapshot> SnapshotsAfter { get; } = [];

    public static OperationReport Ok(string title) => new() { Success = true, Title = title };
    public static OperationReport Fail(string title) => new() { Success = false, Title = title };
}