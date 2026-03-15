using System.Management;
namespace OmenGamingHubUnlocker.Windows;

/// <summary>
/// Snapshot of a Windows service that the unlocker may inspect or modify.
/// </summary>
public sealed record ServiceItem(string Name, string DisplayName, string StartMode);

/// <summary>
/// Describes the desired startup mode for a specific service.
/// </summary>
public sealed record ServiceStartModeTarget(string Name, string DesiredStartMode);

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

        using var searcher = new ManagementObjectSearcher("SELECT Name, DisplayName, StartMode FROM Win32_Service");
        foreach (ManagementObject serviceObject in searcher.Get())
        {
            var serviceName = (string)(serviceObject["Name"] ?? string.Empty);
            var serviceDisplayName = (string)(serviceObject["DisplayName"] ?? string.Empty);
            var serviceStartMode = (string)(serviceObject["StartMode"] ?? string.Empty);

            if (matchEverything || patterns.Any(pattern => WildcardMatch(serviceName, pattern) || WildcardMatch(serviceDisplayName, pattern)))
                matchingServices.Add(new ServiceItem(serviceName, serviceDisplayName, serviceStartMode));
        }

        return matchingServices;
    }

    public static List<OperationLine> ApplyStartModeTargets(IEnumerable<ServiceStartModeTarget> targets, bool dryRun)
    {
        var requestedTargets = targets
            .DistinctBy(target => target.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(target => target.Name, target => target.DesiredStartMode, StringComparer.OrdinalIgnoreCase);

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

        foreach (var (serviceName, desiredStartMode) in requestedTargets.OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (!currentServices.TryGetValue(serviceName, out var currentService))
            {
                operationLines.Add(LocalizedLine.Warn("manager.services.notFound", serviceName));
                continue;
            }

            if (currentService.StartMode.Equals(desiredStartMode, StringComparison.OrdinalIgnoreCase))
            {
                operationLines.Add(LocalizedLine.Info("manager.services.alreadySet", serviceName, desiredStartMode));
                continue;
            }

            if (dryRun)
            {
                operationLines.Add(LocalizedLine.Ok("manager.services.wouldSet", serviceName, desiredStartMode, currentService.StartMode));
                continue;
            }

            try
            {
                using var serviceObject = new ManagementObject($"Win32_Service.Name='{serviceName}'");
                var wmiResult = serviceObject.InvokeMethod("ChangeStartMode", new object[] { desiredStartMode });
                var wmiReturnCode = ConvertToWmiReturnCode(wmiResult);

                if (wmiReturnCode == 0)
                {
                    operationLines.Add(LocalizedLine.Ok("manager.services.set", serviceName, desiredStartMode));
                    continue;
                }

                var fallbackApplied = TryApplyWithSc(serviceName, desiredStartMode, out var fallbackError);
                operationLines.Add(fallbackApplied
                    ? LocalizedLine.Warn("manager.services.wmiFallbackApplied", wmiReturnCode, serviceName)
                    : LocalizedLine.Err("manager.services.failedWithReturnCode", serviceName, wmiReturnCode, fallbackError));
            }
            catch (Exception exception)
            {
                var fallbackApplied = TryApplyWithSc(serviceName, desiredStartMode, out var fallbackError);
                operationLines.Add(fallbackApplied
                    ? LocalizedLine.Warn("manager.services.exceptionFallbackApplied", serviceName)
                    : LocalizedLine.Err("manager.services.failedWithException", serviceName, exception.Message, fallbackError));
            }
        }

        return operationLines;
    }

    private static bool TryApplyWithSc(string serviceName, string desiredStartMode, out string error)
    {
        var scMode = desiredStartMode.Equals("Manual", StringComparison.OrdinalIgnoreCase) ? "demand" :
                     desiredStartMode.Equals("Automatic", StringComparison.OrdinalIgnoreCase) ? "auto" :
                     desiredStartMode.Equals("Disabled", StringComparison.OrdinalIgnoreCase) ? "disabled" : "demand";

        return PowerShellRunner.TryRun("sc.exe", $"config \"{serviceName}\" start= {scMode}", out _, out error, 20_000);
    }

    private static uint ConvertToWmiReturnCode(object? result)
    {
        try
        {
            return result is null ? uint.MaxValue : Convert.ToUInt32(result);
        }
        catch
        {
            return uint.MaxValue;
        }
    }

    private static bool WildcardMatch(string input, string pattern)
    {
        var regex = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".") + "$";

        return System.Text.RegularExpressions.Regex.IsMatch(
            input ?? string.Empty,
            regex,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }
}
