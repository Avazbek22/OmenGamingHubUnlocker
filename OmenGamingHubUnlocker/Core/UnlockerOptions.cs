namespace OmenGamingHubUnlocker.Core;

public sealed class UnlockerOptions
{
    public bool DryRun { get; init; }
    public bool ManageFirewall { get; init; } = true;
    public bool ManageHosts { get; init; } = true;

    // Aggressive: try to terminate OMEN-related processes
    public bool TryKillProcesses { get; init; } = true;

    public static UnlockerOptions ForDryRun()
        => new UnlockerOptions { DryRun = true, ManageFirewall = true, ManageHosts = true, TryKillProcesses = false };

    public static UnlockerOptions ForActivate()
        => new UnlockerOptions { DryRun = false, ManageFirewall = true, ManageHosts = true, TryKillProcesses = true };

    public static UnlockerOptions ForDisable()
        => new UnlockerOptions { DryRun = false, ManageFirewall = true, ManageHosts = true, TryKillProcesses = false };

    public static UnlockerOptions ForResetAndReapply()
        => new UnlockerOptions { DryRun = false, ManageFirewall = true, ManageHosts = true, TryKillProcesses = true };
}
