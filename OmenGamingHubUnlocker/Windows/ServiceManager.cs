using System.Management;
namespace OmenGamingHubUnlocker.Windows;

/// <summary>
/// Snapshot of a Windows service that the unlocker may inspect or modify.
/// </summary>
public sealed record ServiceItem(
    string Name,
    string DisplayName,
    string StartMode,
    string State = "Unknown",
    string PathName = "",
    bool DelayedAutoStart = false);

/// <summary>
/// Describes the desired startup mode for a specific service.
/// </summary>
public sealed record ServiceStartModeTarget(
    string Name,
    string DesiredStartMode,
    bool DelayedAutoStart = false);

/// <summary>
/// Encapsulates WMI-based service discovery and startup mode changes.
/// </summary>
public static class ServiceManager
{
    public static (bool ok, string details) CheckCapability()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_Service");
            _ = searcher.Get().Count;
            return (true, Text.Get("manager.services.capabilityOk"));
        }
        catch (Exception exception)
        {
            return (false, exception.Message);
        }
    }

    public static List<ServiceItem> QueryServices(string[] patterns)
    {
        var matchingServices = new List<ServiceItem>();
        var matchEverything = patterns.Length == 0;

        using var searcher = new ManagementObjectSearcher(
            "SELECT Name, DisplayName, StartMode, State, PathName, DelayedAutoStart FROM Win32_Service");
        foreach (ManagementObject serviceObject in searcher.Get())
        {
            var serviceName = (string)(serviceObject["Name"] ?? string.Empty);
            var serviceDisplayName = (string)(serviceObject["DisplayName"] ?? string.Empty);
            var serviceStartMode = (string)(serviceObject["StartMode"] ?? string.Empty);
            var serviceState = (string)(serviceObject["State"] ?? string.Empty);
            var servicePath = (string)(serviceObject["PathName"] ?? string.Empty);
            var delayedAutoStart = serviceObject["DelayedAutoStart"] is bool delayed && delayed;

            if (matchEverything ||
                patterns.Any(pattern =>
                    WildcardMatcher.IsMatch(serviceName, pattern) ||
                    WildcardMatcher.IsMatch(serviceDisplayName, pattern) ||
                    WildcardMatcher.IsMatch(servicePath, pattern)))
            {
                matchingServices.Add(new ServiceItem(
                    serviceName,
                    serviceDisplayName,
                    serviceStartMode,
                    serviceState,
                    servicePath,
                    delayedAutoStart));
            }
        }

        return matchingServices;
    }

    public static List<OperationLine> StopServices(IEnumerable<string> serviceNames, bool dryRun)
        => ChangeServiceRunningState(serviceNames, desiredRunning: false, dryRun);

    public static List<OperationLine> StartServices(IEnumerable<string> serviceNames, bool dryRun)
        => ChangeServiceRunningState(serviceNames, desiredRunning: true, dryRun);

    public static List<OperationLine> ApplyStartModeTargets(IEnumerable<ServiceStartModeTarget> targets, bool dryRun)
    {
        var requestedTargets = targets
            .DistinctBy(target => target.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(target => target.Name, StringComparer.OrdinalIgnoreCase);

        if (requestedTargets.Count == 0)
        {
            return
            [
                LocalizedLine.Info("manager.services.nothingToChange")
            ];
        }

        var currentServices = QueryServices(Array.Empty<string>())
            .Where(service => requestedTargets.ContainsKey(service.Name))
            .ToDictionary(service => service.Name, service => service, StringComparer.OrdinalIgnoreCase);

        var operationLines = new List<OperationLine>();

        foreach (var (serviceName, target) in requestedTargets.OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase))
        {
            var desiredStartMode = NormalizeStartMode(target.DesiredStartMode);
            if (!currentServices.TryGetValue(serviceName, out var currentService))
            {
                operationLines.Add(LocalizedLine.Warn("manager.services.notFound", serviceName));
                continue;
            }

            if (StartModeMatches(currentService, target))
            {
                operationLines.Add(LocalizedLine.Info("manager.services.alreadySet", serviceName, FormatTarget(target)));
                continue;
            }

            if (dryRun)
            {
                operationLines.Add(LocalizedLine.Ok(
                    "manager.services.wouldSet",
                    serviceName,
                    FormatTarget(target),
                    currentService.StartMode));
                continue;
            }

            try
            {
                using var serviceObject = new ManagementObject(
                    $"Win32_Service.Name='{EscapeWmiKey(serviceName)}'");
                var wmiResult = serviceObject.InvokeMethod("ChangeStartMode", new object[] { desiredStartMode });
                var wmiReturnCode = ConvertToWmiReturnCode(wmiResult);

                if (wmiReturnCode == 0)
                {
                    if (ApplyDelayedAutoStartIfNeeded(target, out var delayedError))
                    {
                        operationLines.Add(LocalizedLine.Ok("manager.services.set", serviceName, FormatTarget(target)));
                        continue;
                    }

                    operationLines.Add(LocalizedLine.Err(
                        "manager.services.failedWithException",
                        serviceName,
                        "Delayed-auto configuration failed.",
                        delayedError));
                    continue;
                }

                var fallbackApplied = TryApplyWithSc(target, out var fallbackError);
                operationLines.Add(fallbackApplied
                    ? LocalizedLine.Warn("manager.services.wmiFallbackApplied", wmiReturnCode, serviceName)
                    : LocalizedLine.Err("manager.services.failedWithReturnCode", serviceName, wmiReturnCode, fallbackError));
            }
            catch (Exception exception)
            {
                var fallbackApplied = TryApplyWithSc(target, out var fallbackError);
                operationLines.Add(fallbackApplied
                    ? LocalizedLine.Warn("manager.services.exceptionFallbackApplied", serviceName)
                    : LocalizedLine.Err("manager.services.failedWithException", serviceName, exception.Message, fallbackError));
            }
        }

        return operationLines;
    }

    private static bool TryApplyWithSc(ServiceStartModeTarget target, out string error)
    {
        var normalizedMode = NormalizeStartMode(target.DesiredStartMode);
        var scMode = normalizedMode switch
        {
            "Manual" => "demand",
            "Automatic" when target.DelayedAutoStart => "delayed-auto",
            "Automatic" => "auto",
            "Disabled" => "disabled",
            _ => throw new ArgumentOutOfRangeException(nameof(target), target.DesiredStartMode, "Unsupported service mode.")
        };

        return PowerShellRunner.TryRun(
            "sc.exe",
            $"config \"{target.Name}\" start= {scMode}",
            out _,
            out error,
            20_000);
    }

    public static string NormalizeStartMode(string startMode)
        => startMode.Trim() switch
        {
            var mode when mode.Equals("Auto", StringComparison.OrdinalIgnoreCase) => "Automatic",
            var mode when mode.Equals("Automatic", StringComparison.OrdinalIgnoreCase) => "Automatic",
            var mode when mode.Equals("Delayed", StringComparison.OrdinalIgnoreCase) => "Automatic",
            var mode when mode.Equals("Delayed-Auto", StringComparison.OrdinalIgnoreCase) => "Automatic",
            var mode when mode.Equals("Manual", StringComparison.OrdinalIgnoreCase) => "Manual",
            var mode when mode.Equals("Demand", StringComparison.OrdinalIgnoreCase) => "Manual",
            var mode when mode.Equals("Disabled", StringComparison.OrdinalIgnoreCase) => "Disabled",
            _ => startMode.Trim()
        };

    private static bool StartModeMatches(ServiceItem currentService, ServiceStartModeTarget target)
    {
        var currentMode = NormalizeStartMode(currentService.StartMode);
        var desiredMode = NormalizeStartMode(target.DesiredStartMode);
        return currentMode.Equals(desiredMode, StringComparison.OrdinalIgnoreCase) &&
               (!desiredMode.Equals("Automatic", StringComparison.OrdinalIgnoreCase) ||
                currentService.DelayedAutoStart == target.DelayedAutoStart);
    }

    private static bool ApplyDelayedAutoStartIfNeeded(ServiceStartModeTarget target, out string error)
    {
        if (!NormalizeStartMode(target.DesiredStartMode).Equals("Automatic", StringComparison.OrdinalIgnoreCase))
        {
            error = string.Empty;
            return true;
        }

        return TryApplyWithSc(target, out error);
    }

    private static string FormatTarget(ServiceStartModeTarget target)
        => NormalizeStartMode(target.DesiredStartMode) == "Automatic" && target.DelayedAutoStart
            ? "Automatic (Delayed Start)"
            : NormalizeStartMode(target.DesiredStartMode);

    private static string EscapeWmiKey(string value)
        => value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("'", "\\'", StringComparison.Ordinal);

    private static uint ConvertToWmiReturnCode(object? result)
    {
        try
        {
            return result is null
                ? uint.MaxValue
                : Convert.ToUInt32(result, System.Globalization.CultureInfo.InvariantCulture);
        }
        catch
        {
            return uint.MaxValue;
        }
    }

    private static List<OperationLine> ChangeServiceRunningState(
        IEnumerable<string> serviceNames,
        bool desiredRunning,
        bool dryRun)
    {
        var requestedNames = serviceNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (requestedNames.Count == 0)
            return [LocalizedLine.Info("manager.services.nothingToChange")];

        var currentServices = QueryServices([])
            .ToDictionary(service => service.Name, StringComparer.OrdinalIgnoreCase);
        var lines = new List<OperationLine>();

        foreach (var serviceName in requestedNames)
        {
            if (!currentServices.TryGetValue(serviceName, out var service))
            {
                lines.Add(LocalizedLine.Err("manager.services.notFound", serviceName));
                continue;
            }

            var alreadyInDesiredState = desiredRunning
                ? service.State.Equals("Running", StringComparison.OrdinalIgnoreCase)
                : service.State.Equals("Stopped", StringComparison.OrdinalIgnoreCase);
            if (alreadyInDesiredState)
            {
                lines.Add(LocalizedLine.Info(
                    desiredRunning ? "manager.services.alreadyRunning" : "manager.services.alreadyStopped",
                    serviceName));
                continue;
            }

            if (dryRun)
            {
                lines.Add(LocalizedLine.Ok(
                    desiredRunning ? "manager.services.wouldStart" : "manager.services.wouldStop",
                    serviceName));
                continue;
            }

            var command = desiredRunning ? "start" : "stop";
            var succeeded = PowerShellRunner.TryRun(
                "sc.exe",
                $"{command} \"{serviceName}\"",
                out _,
                out var error,
                20_000);

            if (!succeeded)
            {
                lines.Add(LocalizedLine.Err(
                    desiredRunning ? "manager.services.failedToStart" : "manager.services.failedToStop",
                    serviceName,
                    error));
                continue;
            }

            var reachedState = WaitForServiceState(serviceName, desiredRunning, TimeSpan.FromSeconds(15));
            lines.Add(reachedState
                ? LocalizedLine.Ok(
                    desiredRunning ? "manager.services.started" : "manager.services.stopped",
                    serviceName)
                : LocalizedLine.Err(
                    desiredRunning ? "manager.services.startTimeout" : "manager.services.stopTimeout",
                    serviceName));
        }

        return lines;
    }

    private static bool WaitForServiceState(string serviceName, bool desiredRunning, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        do
        {
            var service = QueryServices([])
                .FirstOrDefault(item => item.Name.Equals(serviceName, StringComparison.OrdinalIgnoreCase));
            var reachedDesiredState = desiredRunning
                ? service?.State.Equals("Running", StringComparison.OrdinalIgnoreCase) == true
                : service?.State.Equals("Stopped", StringComparison.OrdinalIgnoreCase) == true;

            if (reachedDesiredState)
                return true;

            Thread.Sleep(250);
        }
        while (DateTime.UtcNow < deadline);

        return false;
    }
}
