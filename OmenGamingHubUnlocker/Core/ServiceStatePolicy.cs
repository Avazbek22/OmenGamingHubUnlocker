namespace OmenGamingHubUnlocker.Core;

/// <summary>
/// Centralizes service-mode equivalence across WMI, sc.exe, verification, and rollback.
/// </summary>
public static class ServiceStatePolicy
{
    public static bool IsManual(ServiceItem service)
        => ServiceManager.NormalizeStartMode(service.StartMode)
            .Equals("Manual", StringComparison.OrdinalIgnoreCase);

    public static bool IsRunning(ServiceItem service)
        => service.State.Equals("Running", StringComparison.OrdinalIgnoreCase);

    public static bool IsStopped(ServiceItem service)
        => service.State.Equals("Stopped", StringComparison.OrdinalIgnoreCase);

    public static bool MatchesOriginalRunningState(ServiceItem service, ServiceBackup backup)
        => backup.OriginalRunning ? IsRunning(service) : IsStopped(service);

    public static bool MatchesBackup(ServiceItem service, ServiceBackup backup)
    {
        var currentMode = ServiceManager.NormalizeStartMode(service.StartMode);
        var expectedMode = ServiceManager.NormalizeStartMode(backup.OriginalStartMode);
        return currentMode.Equals(expectedMode, StringComparison.OrdinalIgnoreCase) &&
               (!expectedMode.Equals("Automatic", StringComparison.OrdinalIgnoreCase) ||
                service.DelayedAutoStart == backup.OriginalDelayedAutoStart);
    }
}
