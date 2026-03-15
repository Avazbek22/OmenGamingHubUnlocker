namespace OmenGamingHubUnlocker.Windows;

/// <summary>
/// Represents the minimal AppX metadata needed for reset and executable discovery.
/// </summary>
public sealed record AppxPackageInfo(
    string Name,
    string PackageFamilyName,
    string PackageFullName,
    string InstallLocation);

/// <summary>
/// Encapsulates all AppX package discovery and reset logic behind a simple API.
/// </summary>
public static class AppxPackageManager
{
    public static (bool ok, string details) CheckResetCapability()
    {
        const string script = """
$ErrorActionPreference = 'Stop'
Get-Command Reset-AppxPackage | Out-Null
Write-Output 'Reset-AppxPackage is available.'
""";

        var commandSucceeded = TryRunPowerShell(script, out var standardOutput, out var standardError, 20_000);
        return commandSucceeded
            ? (true, standardOutput.Trim())
            : (false, string.IsNullOrWhiteSpace(standardError) ? "Reset-AppxPackage is not available." : standardError.Trim());
    }

    /// <summary>
    /// Queries installed AppX packages and returns only the entries that match OMEN-related filters.
    /// </summary>
    public static List<AppxPackageInfo> QueryPackages(string[] filters)
    {
        const string script = """
$ErrorActionPreference = 'Stop'
Get-AppxPackage |
    Select-Object Name, PackageFamilyName, PackageFullName, InstallLocation |
    ConvertTo-Json -Compress
""";

        var commandSucceeded = TryRunPowerShell(script, out var standardOutput, out _, 30_000);
        if (!commandSucceeded || string.IsNullOrWhiteSpace(standardOutput))
            return [];

        try
        {
            var packages = DeserializePackages(standardOutput);

            return packages
                .Where(package => MatchesAnyFilter(package, filters))
                .Where(package => !string.IsNullOrWhiteSpace(package.InstallLocation))
                .DistinctBy(package => package.PackageFullName)
                .OrderByDescending(IsPrimaryPackage)
                .ThenBy(package => package.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch
        {
            return [];
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

    /// <summary>
    /// Executes the same AppX reset operation that Windows Settings exposes for Store apps.
    /// </summary>
    public static List<OperationLine> ResetPackage(string[] filters, bool dryRun)
    {
        var lines = new List<OperationLine>();

        if (!TryGetPrimaryPackage(filters, out var package, out var packageDescription) || package is null)
        {
            lines.Add(new OperationLine { Level = "ERR", Text = $"Reset: {packageDescription}" });
            return lines;
        }

        lines.Add(new OperationLine { Level = "INFO", Text = $"Reset target: {packageDescription}" });

        if (dryRun)
        {
            lines.Add(new OperationLine
            {
                Level = "OK",
                Text = $"Reset: would run Windows app reset for {package.PackageFullName}"
            });
            return lines;
        }

        var escapedPackageName = EscapeSingleQuotedString(package.PackageFullName);
        var script = $"""
$ErrorActionPreference = 'Stop'
Reset-AppxPackage -Package '{escapedPackageName}' -Confirm:$false | Out-Null
Write-Output 'Reset completed.'
""";

        var commandSucceeded = TryRunPowerShell(script, out var standardOutput, out var standardError, 120_000);
        lines.Add(new OperationLine
        {
            Level = commandSucceeded ? "OK" : "ERR",
            Text = commandSucceeded
                ? $"Reset: Windows app reset completed for {package.Name}."
                : $"Reset: failed for {package.Name}. {FirstNonEmpty(standardError, standardOutput, "Unknown error.")}"
        });

        return lines;
    }

    private static bool TryRunPowerShell(string script, out string standardOutput, out string standardError, int timeoutMs)
    {
        var encodedScript = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        return PowerShellRunner.TryRun(
            "powershell.exe",
            $"-NoProfile -ExecutionPolicy Bypass -EncodedCommand {encodedScript}",
            out standardOutput,
            out standardError,
            timeoutMs);
    }

    private static List<AppxPackageInfo> DeserializePackages(string json)
    {
        using var document = JsonDocument.Parse(json);

        return document.RootElement.ValueKind switch
        {
            JsonValueKind.Array => document.RootElement.EnumerateArray().Select(MapPackageInfo).ToList(),
            JsonValueKind.Object => [MapPackageInfo(document.RootElement)],
            _ => []
        };
    }

    private static AppxPackageInfo MapPackageInfo(JsonElement element)
    {
        return new AppxPackageInfo(
            GetStringProperty(element, "Name"),
            GetStringProperty(element, "PackageFamilyName"),
            GetStringProperty(element, "PackageFullName"),
            GetStringProperty(element, "InstallLocation"));
    }

    private static string GetStringProperty(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
            return string.Empty;

        return property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? string.Empty
            : property.ToString();
    }

    private static bool MatchesAnyFilter(AppxPackageInfo package, string[] filters)
    {
        return filters.Any(filter =>
            WildcardMatch(package.Name, filter) ||
            WildcardMatch(package.PackageFamilyName, filter) ||
            WildcardMatch(package.PackageFullName, filter));
    }

    private static bool IsPrimaryPackage(AppxPackageInfo package)
        => package.Name.Equals(OmenTargets.PrimaryAppxPackageName, StringComparison.OrdinalIgnoreCase);

    private static string EscapeSingleQuotedString(string value)
        => (value ?? string.Empty).Replace("'", "''");

    private static string FirstNonEmpty(params string[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

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
