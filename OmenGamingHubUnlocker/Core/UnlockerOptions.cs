namespace OmenGamingHubUnlocker.Core;

/// <summary>
/// Describes which taming actions are allowed for a specific execution flow.
/// </summary>
public sealed class UnlockerOptions
{
    /// <summary>
    /// Runs discovery and reporting without mutating the system.
    /// </summary>
    public bool DryRun { get; init; }

    /// <summary>
    /// Enables firewall rule management.
    /// </summary>
    public bool ManageFirewall { get; init; } = true;

    /// <summary>
    /// Enables hosts file management.
    /// </summary>
    public bool ManageHosts { get; init; } = true;

    /// <summary>
    /// Allows the engine to stop OMEN-related processes before applying changes.
    /// </summary>
    public bool TryKillProcesses { get; init; } = true;

    public static UnlockerOptions ForDryRun()
        => new() { DryRun = true, ManageFirewall = true, ManageHosts = true, TryKillProcesses = false };

    public static UnlockerOptions ForActivate()
        => new() { DryRun = false, ManageFirewall = true, ManageHosts = true, TryKillProcesses = true };

    public static UnlockerOptions ForDisable()
        => new() { DryRun = false, ManageFirewall = true, ManageHosts = true, TryKillProcesses = false };

    public static UnlockerOptions ForResetAndReapply()
        => new() { DryRun = false, ManageFirewall = true, ManageHosts = true, TryKillProcesses = true };
}
