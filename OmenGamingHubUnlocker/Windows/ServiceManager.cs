using System.Management;
using OmenGamingHubUnlocker.Core;

namespace OmenGamingHubUnlocker.Windows;

public sealed record ServiceItem(string Name, string DisplayName, string StartMode);
public sealed record ServiceStartModeTarget(string Name, string DesiredStartMode);

public static class ServiceManager
{
    public static (bool ok, string details) CheckCapability()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_Service");
            _ = searcher.Get().Count;
            return (true, "WMI query ok.");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public static List<ServiceItem> QueryServices(string[] patterns)
    {
        var list = new List<ServiceItem>();
        var matchAll = patterns.Length == 0;

        using var searcher = new ManagementObjectSearcher("SELECT Name, DisplayName, StartMode FROM Win32_Service");
        foreach (ManagementObject mo in searcher.Get())
        {
            var name = (string)(mo["Name"] ?? string.Empty);
            var displayName = (string)(mo["DisplayName"] ?? string.Empty);
            var startMode = (string)(mo["StartMode"] ?? string.Empty);

            if (matchAll || patterns.Any(p => WildMatch(name, p) || WildMatch(displayName, p)))
                list.Add(new ServiceItem(name, displayName, startMode));
        }

        return list;
    }

    public static List<OperationLine> ApplyStartModeTargets(IEnumerable<ServiceStartModeTarget> targets, bool dryRun)
    {
        var targetMap = targets
            .DistinctBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Name, x => x.DesiredStartMode, StringComparer.OrdinalIgnoreCase);

        if (targetMap.Count == 0)
        {
            return
            [
                new OperationLine { Level = "INFO", Text = "Services: nothing to change." }
            ];
        }

        var currentServices = QueryServices(Array.Empty<string>())
            .Where(s => targetMap.ContainsKey(s.Name))
            .ToDictionary(s => s.Name, s => s, StringComparer.OrdinalIgnoreCase);

        var lines = new List<OperationLine>();

        foreach (var (name, desiredMode) in targetMap.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (!currentServices.TryGetValue(name, out var service))
            {
                lines.Add(new OperationLine { Level = "WARN", Text = $"Services: {name} was not found." });
                continue;
            }

            if (service.StartMode.Equals(desiredMode, StringComparison.OrdinalIgnoreCase))
            {
                lines.Add(new OperationLine { Level = "INFO", Text = $"Services: {name} already set to {desiredMode}." });
                continue;
            }

            if (dryRun)
            {
                lines.Add(new OperationLine
                {
                    Level = "OK",
                    Text = $"Services: would set {name} -> {desiredMode} (was {service.StartMode})"
                });
                continue;
            }

            try
            {
                using var mo = new ManagementObject($"Win32_Service.Name='{name}'");
                var result = mo.InvokeMethod("ChangeStartMode", new object[] { desiredMode });
                var code = ConvertToWmiReturnCode(result);

                if (code == 0)
                {
                    lines.Add(new OperationLine { Level = "OK", Text = $"Services: set {name} -> {desiredMode}" });
                    continue;
                }

                var fallback = TryApplyWithSc(name, desiredMode, out var fallbackError);
                lines.Add(new OperationLine
                {
                    Level = fallback ? "WARN" : "ERR",
                    Text = fallback
                        ? $"Services: WMI returned {code} for {name}, sc.exe fallback applied."
                        : $"Services: failed to set {name}. WMI returned {code}. sc.exe error: {fallbackError}"
                });
            }
            catch (Exception ex)
            {
                var fallback = TryApplyWithSc(name, desiredMode, out var fallbackError);
                lines.Add(new OperationLine
                {
                    Level = fallback ? "WARN" : "ERR",
                    Text = fallback
                        ? $"Services: WMI failed for {name}, sc.exe fallback applied."
                        : $"Services: failed to set {name}. WMI error: {ex.Message}. sc.exe error: {fallbackError}"
                });
            }
        }

        return lines;
    }

    private static bool TryApplyWithSc(string serviceName, string desiredMode, out string error)
    {
        var scMode = desiredMode.Equals("Manual", StringComparison.OrdinalIgnoreCase) ? "demand" :
                     desiredMode.Equals("Automatic", StringComparison.OrdinalIgnoreCase) ? "auto" :
                     desiredMode.Equals("Disabled", StringComparison.OrdinalIgnoreCase) ? "disabled" : "demand";

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

    private static bool WildMatch(string input, string pattern)
    {
        var regex = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".") + "$";

        return System.Text.RegularExpressions.Regex.IsMatch(input ?? string.Empty, regex, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }
}
