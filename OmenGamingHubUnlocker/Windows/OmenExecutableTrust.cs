namespace OmenGamingHubUnlocker.Windows;

/// <summary>
/// Prevents broad OMEN patterns from turning shared Windows executables into firewall or termination targets.
/// </summary>
public static class OmenExecutableTrust
{
    private static readonly HashSet<string> SharedHostNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "cmd.exe",
        "conhost.exe",
        "dllhost.exe",
        "explorer.exe",
        "msedgewebview2.exe",
        "powershell.exe",
        "pwsh.exe",
        "regsvr32.exe",
        "rundll32.exe",
        "schtasks.exe",
        "services.exe",
        "svchost.exe"
    };

    public static bool IsTrustedOmenExecutable(string? executablePath, params string?[] identities)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
            return false;

        try
        {
            var normalizedPath = Path.GetFullPath(executablePath);
            if (SharedHostNames.Contains(Path.GetFileName(normalizedPath)))
                return false;

            if (OmenIdentity.IsLikelyOmenReference(normalizedPath) && File.Exists(normalizedPath))
                return true;

            if (!OmenIdentity.IsLikelyOmenReference(identities) || !File.Exists(normalizedPath))
                return false;

            var versionInfo = FileVersionInfo.GetVersionInfo(normalizedPath);
            return IsHpMetadata(versionInfo.CompanyName) ||
                   OmenIdentity.IsLikelyOmenReference(
                       versionInfo.ProductName,
                       versionInfo.FileDescription,
                       versionInfo.InternalName,
                       versionInfo.OriginalFilename);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsHpMetadata(string? companyName)
        => !string.IsNullOrWhiteSpace(companyName) &&
           (companyName.Contains("HP Inc", StringComparison.OrdinalIgnoreCase) ||
            companyName.Contains("Hewlett-Packard", StringComparison.OrdinalIgnoreCase));
}
