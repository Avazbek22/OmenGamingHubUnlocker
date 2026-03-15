using System.Text.Json;
using OmenGamingHubUnlocker.Core;

namespace OmenGamingHubUnlocker.Windows;

public sealed record AppxPackageInfo(
    string Name,
    string PackageFamilyName,
    string PackageFullName,
    string InstallLocation);

public static class AppxPackageManager
{
    public static (bool ok, string details) CheckResetCapability()
    {
        const string script = """
$ErrorActionPreference = 'Stop'
Get-Command Reset-AppxPackage | Out-Null
Write-Output 'Reset-AppxPackage is available.'
""";

        var ok = TryRunPowerShell(script, out var stdout, out var stderr, 20_000);
        return ok
            ? (true, stdout.Trim())
            : (false, string.IsNullOrWhiteSpace(stderr) ? "Reset-AppxPackage is not available." : stderr.Trim());
    }

    public static List<AppxPackageInfo> QueryPackages(string[] filters)
    {
        const string script = """
$ErrorActionPreference = 'Stop'
Get-AppxPackage |
    Select-Object Name, PackageFamilyName, PackageFullName, InstallLocation |
    ConvertTo-Json -Compress
""";

        var ok = TryRunPowerShell(script, out var stdout, out var stderr, 30_000);
        if (!ok || string.IsNullOrWhiteSpace(stdout))
            return new List<AppxPackageInfo>();

        try
        {
            var packages = DeserializePackages(stdout);

            return packages
                .Where(p => MatchesAny(p, filters))
                .Where(p => !string.IsNullOrWhiteSpace(p.InstallLocation))
                .DistinctBy(p => p.PackageFullName)
                .OrderByDescending(p => IsPrimaryPackage(p))
                .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch
        {
            return new List<AppxPackageInfo>();
        }
    }

    public static bool TryGetPrimaryPackage(string[] filters, out AppxPackageInfo? package, out string details)
    {
        var packages = QueryPackages(filters);
        package = packages.FirstOrDefault();

        if (package is null)
        {
            details = "OMEN AppX package was not found.";
            return false;
        }

        details = $"{package.Name} ({package.PackageFullName})";
        return true;
    }

    public static List<OperationLine> ResetPackage(string[] filters, bool dryRun)
    {
        var lines = new List<OperationLine>();

        if (!TryGetPrimaryPackage(filters, out var package, out var details) || package is null)
        {
            lines.Add(new OperationLine { Level = "ERR", Text = $"Reset: {details}" });
            return lines;
        }

        lines.Add(new OperationLine { Level = "INFO", Text = $"Reset target: {details}" });

        if (dryRun)
        {
            lines.Add(new OperationLine
            {
                Level = "OK",
                Text = $"Reset: would run Windows app reset for {package.PackageFullName}"
            });
            return lines;
        }

        var escapedFullName = EscapeSingleQuotedPowerShellString(package.PackageFullName);
        var script = $"""
$ErrorActionPreference = 'Stop'
Reset-AppxPackage -Package '{escapedFullName}' -Confirm:$false | Out-Null
Write-Output 'Reset completed.'
""";

        var ok = TryRunPowerShell(script, out var stdout, out var stderr, 120_000);
        lines.Add(new OperationLine
        {
            Level = ok ? "OK" : "ERR",
            Text = ok
                ? $"Reset: Windows app reset completed for {package.Name}."
                : $"Reset: failed for {package.Name}. {FirstNonEmpty(stderr, stdout, "Unknown error.")}"
        });

        return lines;
    }

    private static bool TryRunPowerShell(string script, out string stdout, out string stderr, int timeoutMs)
    {
        var encoded = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(script));
        return PowerShellRunner.TryRun(
            "powershell.exe",
            $"-NoProfile -ExecutionPolicy Bypass -EncodedCommand {encoded}",
            out stdout,
            out stderr,
            timeoutMs);
    }

    private static List<AppxPackageInfo> DeserializePackages(string json)
    {
        using var document = JsonDocument.Parse(json);

        if (document.RootElement.ValueKind == JsonValueKind.Array)
            return document.RootElement.EnumerateArray().Select(ToPackageInfo).ToList();

        if (document.RootElement.ValueKind == JsonValueKind.Object)
            return new List<AppxPackageInfo> { ToPackageInfo(document.RootElement) };

        return new List<AppxPackageInfo>();
    }

    private static AppxPackageInfo ToPackageInfo(JsonElement element)
    {
        return new AppxPackageInfo(
            GetString(element, "Name"),
            GetString(element, "PackageFamilyName"),
            GetString(element, "PackageFullName"),
            GetString(element, "InstallLocation"));
    }

    private static string GetString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var prop))
            return string.Empty;

        return prop.ValueKind == JsonValueKind.String ? prop.GetString() ?? string.Empty : prop.ToString();
    }

    private static bool MatchesAny(AppxPackageInfo package, string[] filters)
    {
        return filters.Any(pattern =>
            WildMatch(package.Name, pattern) ||
            WildMatch(package.PackageFamilyName, pattern) ||
            WildMatch(package.PackageFullName, pattern));
    }

    private static bool IsPrimaryPackage(AppxPackageInfo package)
        => package.Name.Equals(OmenTargets.PrimaryAppxPackageName, StringComparison.OrdinalIgnoreCase);

    private static string EscapeSingleQuotedPowerShellString(string value)
        => (value ?? string.Empty).Replace("'", "''");

    private static string FirstNonEmpty(params string[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? string.Empty;

    private static bool WildMatch(string input, string pattern)
    {
        var regex = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".") + "$";

        return System.Text.RegularExpressions.Regex.IsMatch(input ?? string.Empty, regex, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }
}
