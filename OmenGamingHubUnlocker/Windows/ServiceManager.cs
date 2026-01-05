using System.Management;
using OmenGamingHubUnlocker.Core;

namespace OmenGamingHubUnlocker.Windows;

public sealed record ServiceItem(string Name, string DisplayName, string StartMode);

public static class ServiceManager
{
    public static (bool ok, string details) CheckCapability()
    {
        try
        {
            using var s = new ManagementObjectSearcher("SELECT Name FROM Win32_Service");
            _ = s.Get().Count;
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

        using var searcher = new ManagementObjectSearcher("SELECT Name, DisplayName, StartMode FROM Win32_Service");
        foreach (ManagementObject mo in searcher.Get())
        {
            var name = (string)(mo["Name"] ?? "");
            var displayName = (string)(mo["DisplayName"] ?? "");
            var startMode = (string)(mo["StartMode"] ?? "");

            if (patterns.Any(p => WildMatch(name, p) || WildMatch(displayName, p)))
                list.Add(new ServiceItem(name, displayName, startMode));
        }

        return list;
    }

    public static List<OperationLine> SetServicesStartMode(string[] patterns, string startMode, bool dryRun)
    {
        var lines = new List<OperationLine>();

        var services = QueryServices(patterns);
        if (services.Count == 0)
        {
            lines.Add(new OperationLine { Level = "INFO", Text = "Services: no matching services found." });
            return lines;
        }

        foreach (var s in services.OrderBy(x => x.Name).DistinctBy(x => x.Name))
        {
            if (s.StartMode.Equals(startMode, StringComparison.OrdinalIgnoreCase))
                continue;

            if (dryRun)
            {
                lines.Add(new OperationLine { Level = "OK", Text = $"Services: would set {s.Name} -> {startMode} (was {s.StartMode})" });
                continue;
            }

            try
            {
                // Primary: WMI ChangeStartMode
                using var mo = new ManagementObject($"Win32_Service.Name='{s.Name}'");
                mo.InvokeMethod("ChangeStartMode", new object[] { startMode });

                lines.Add(new OperationLine { Level = "OK", Text = $"Services: set {s.Name} -> {startMode}" });
            }
            catch (Exception ex)
            {
                // Fallback: sc.exe
                var scMode = startMode.Equals("Manual", StringComparison.OrdinalIgnoreCase) ? "demand" :
                             startMode.Equals("Automatic", StringComparison.OrdinalIgnoreCase) ? "auto" :
                             startMode.Equals("Disabled", StringComparison.OrdinalIgnoreCase) ? "disabled" : "demand";

                var ok = PowerShellRunner.TryRun("sc.exe", $"config \"{s.Name}\" start= {scMode}", out _, out var err);

                lines.Add(new OperationLine
                {
                    Level = ok ? "WARN" : "ERR",
                    Text = ok
                        ? $"Services: WMI failed for {s.Name}, sc.exe fallback applied."
                        : $"Services: failed to set {s.Name}. WMI error: {ex.Message}. sc.exe error: {err}"
                });
            }
        }

        return lines;
    }

    private static bool WildMatch(string input, string pattern)
    {
        var regex = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".") + "$";

        return System.Text.RegularExpressions.Regex.IsMatch(input ?? "", regex, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }
}
