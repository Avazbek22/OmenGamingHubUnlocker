using System.Security.Cryptography;

namespace OmenGamingHubUnlocker.Windows;

public sealed record FirewallRuleInfo(
    string Name,
    bool Enabled,
    bool IsOutbound,
    bool IsBlock,
    string ProgramPath,
    string PackageSid);

public sealed record FirewallTargetSet(
    AppxPackageInfo? Package,
    string PackageSid,
    string PackageSidError,
    IReadOnlySet<string> PackageExecutables,
    IReadOnlySet<string> ExternalExecutables,
    bool PackageDirectoryReady = true,
    IReadOnlyList<string>? DiscoveryErrors = null)
{
    public IReadOnlySet<string> AllExecutables => PackageExecutables
        .Concat(ExternalExecutables)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<string> ScanErrors => DiscoveryErrors ?? [];

    public bool DiscoveryComplete =>
        (Package is null || PackageDirectoryReady) &&
        ScanErrors.Count == 0;
}

/// <summary>
/// Describes whether managed firewall rules protect the current OGH package rather than an obsolete version path.
/// </summary>
public sealed record FirewallProtectionStatus(
    bool QuerySucceeded,
    FirewallTargetSet Targets,
    IReadOnlyList<FirewallRuleInfo> Rules,
    IReadOnlyList<string> MissingExecutableRules,
    IReadOnlyList<string> StaleExecutableRules,
    bool PackageRulePresent,
    string Error,
    bool EnforcementActive = true,
    string EnforcementDetails = "")
{
    public int RuleCount => Rules.Count;
    public bool PackageRuleRequired => !string.IsNullOrWhiteSpace(Targets.PackageSid);
    public bool HasProtectionIdentity => Targets.AllExecutables.Count > 0 || PackageRuleRequired;

    public bool IsComplete =>
        QuerySucceeded &&
        EnforcementActive &&
        Targets.DiscoveryComplete &&
        HasProtectionIdentity &&
        MissingExecutableRules.Count == 0 &&
        (!PackageRuleRequired || PackageRulePresent);

    public bool IsReconciled => IsComplete && StaleExecutableRules.Count == 0;
}

/// <summary>
/// Creates version-independent package rules plus explicit rules for every current OGH executable.
/// </summary>
public static class FirewallManager
{
    private const string PackageRuleSuffix = "Package - OMEN Gaming Hub";

    public static (bool ok, string details) CheckCapability()
    {
        var query = QueryManagedRules(OmenTargets.FirewallRulePrefix);
        if (!query.Success)
            return (false, query.Error);

        var enforcement = InspectEnforcement();
        return enforcement.InspectionSucceeded && enforcement.Active
            ? (true, Text.Get("manager.firewall.capabilityOk"))
            : (false, enforcement.Details);
    }

    public static int CountRulesByPrefix(string prefix)
        => QueryManagedRules(prefix).Rules.Count;

    public static FirewallTargetSet DiscoverTargets(
        IEnumerable<string>? additionalExecutablePaths = null,
        IEnumerable<string>? upstreamDiscoveryErrors = null)
    {
        var packageExecutables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var externalExecutables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var discoveryErrors = upstreamDiscoveryErrors?.ToList() ?? [];
        var packageCandidates = AppxPackageManager.QueryPackages(OmenTargets.AppxFilters);
        AppxPackageManager.TrySelectPrimaryPackage(packageCandidates, out var package, out var packageDetails);
        if (package is null && packageCandidates.Count > 0)
            discoveryErrors.Add(packageDetails);

        var packageDirectoryReady = true;

        if (package is not null)
        {
            packageDirectoryReady = ScanExecutables(
                package.InstallLocation,
                packageExecutables,
                discoveryErrors,
                requireDirectory: true);
        }

        foreach (var relativeDirectory in OmenTargets.ExtraExeDirsRelative)
        {
            ScanExecutables(
                Path.Combine(WindowsPaths.ProgramFiles, relativeDirectory),
                externalExecutables,
                discoveryErrors,
                requireDirectory: false);

            if (!string.Equals(WindowsPaths.ProgramFiles, WindowsPaths.ProgramFilesX86, StringComparison.OrdinalIgnoreCase))
            {
                ScanExecutables(
                    Path.Combine(WindowsPaths.ProgramFilesX86, relativeDirectory),
                    externalExecutables,
                    discoveryErrors,
                    requireDirectory: false);
            }
        }

        foreach (var executablePath in additionalExecutablePaths ?? [])
        {
            var normalizedPath = NormalizePath(executablePath);
            if (normalizedPath is not null &&
                normalizedPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                File.Exists(normalizedPath))
            {
                externalExecutables.Add(normalizedPath);
            }
        }

        var packageSid = string.Empty;
        var packageSidError = string.Empty;
        if (package is not null)
            AppContainerSidResolver.TryResolve(package.PackageFamilyName, out packageSid, out packageSidError);

        return new FirewallTargetSet(
            package,
            packageSid,
            packageSidError,
            packageExecutables,
            externalExecutables,
            packageDirectoryReady,
            discoveryErrors);
    }

    public static FirewallProtectionStatus InspectProtection(
        string prefix,
        FirewallTargetSet? discoveredTargets = null)
    {
        var targets = discoveredTargets ?? DiscoverTargets();
        var query = QueryManagedRules(prefix);
        var enforcement = InspectEnforcement();

        if (!query.Success || !enforcement.InspectionSucceeded)
        {
            return new FirewallProtectionStatus(
                false,
                targets,
                query.Rules,
                targets.AllExecutables.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList(),
                [],
                false,
                query.Success ? enforcement.Details : query.Error,
                enforcement.Active,
                enforcement.Details);
        }

        var activeRules = query.Rules
            .Where(rule => rule.Enabled && rule.IsOutbound && rule.IsBlock)
            .ToList();
        var coveredPrograms = activeRules
            .Select(rule => NormalizePath(rule.ProgramPath))
            .OfType<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var expectedPrograms = targets.AllExecutables
            .Select(NormalizePath)
            .OfType<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missingPrograms = expectedPrograms
            .Except(coveredPrograms, StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var stalePrograms = coveredPrograms
            .Except(expectedPrograms, StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var packageRulePresent = !string.IsNullOrWhiteSpace(targets.PackageSid) &&
                                 activeRules.Any(rule =>
                                     rule.PackageSid.Equals(targets.PackageSid, StringComparison.OrdinalIgnoreCase));

        return new FirewallProtectionStatus(
            true,
            targets,
            query.Rules,
            missingPrograms,
            stalePrograms,
            packageRulePresent,
            enforcement.Active ? string.Empty : enforcement.Details,
            enforcement.Active,
            enforcement.Details);
    }

    public static List<OperationLine> ActivateFirewallBlock(
        string prefix,
        bool dryRun,
        bool removeStaleRules = false,
        FirewallTargetSet? discoveredTargets = null)
    {
        var lines = new List<OperationLine>();
        var targets = discoveredTargets ?? DiscoverTargets();

        if (!targets.HasAnyTarget())
        {
            lines.Add(LocalizedLine.Err("manager.firewall.noExecutables"));
            return lines;
        }

        var packageRuleName = $"{prefix} - {PackageRuleSuffix}";
        if (!string.IsNullOrWhiteSpace(targets.PackageSid))
        {
            lines.AddRange(EnsurePackageRule(prefix, packageRuleName, targets.PackageSid, dryRun));
        }
        else if (targets.Package is not null)
        {
            lines.Add(LocalizedLine.Warn(
                "manager.firewall.packageSidUnavailable",
                targets.Package.PackageFamilyName,
                targets.PackageSidError));
        }

        var query = QueryManagedRules(prefix);
        if (!query.Success)
        {
            lines.Add(LocalizedLine.Err("manager.firewall.verificationFailed", query.Error));
            return lines;
        }

        var desiredRuleNames = new HashSet<string>([packageRuleName], StringComparer.OrdinalIgnoreCase);

        foreach (var executablePath in targets.AllExecutables.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var ruleName = BuildProgramRuleName(prefix, executablePath);
            desiredRuleNames.Add(ruleName);

            if (HasCurrentProgramRule(query.Rules, ruleName, executablePath))
                continue;

            if (dryRun)
            {
                lines.Add(LocalizedLine.Ok("manager.firewall.wouldBlockOutbound", executablePath));
                continue;
            }

            if (query.Rules.Any(rule => rule.Name.Equals(ruleName, StringComparison.OrdinalIgnoreCase)))
                lines.AddRange(RemoveRulesByExactName(ruleName));

            if (TryAddProgramRulePowerShell(ruleName, executablePath, out var powerShellError) ||
                TryAddProgramRuleCom(ruleName, executablePath, out var comError))
            {
                lines.Add(LocalizedLine.Ok("manager.firewall.createdBlockRule", ruleName));
                continue;
            }

            lines.Add(LocalizedLine.Err(
                "manager.firewall.failedToCreateRule",
                executablePath,
                comError,
                powerShellError));
        }

        // Existing rules remain active until target discovery is stable, preventing an update-time protection gap.
        if (removeStaleRules && targets.DiscoveryComplete)
            lines.AddRange(RemoveRulesByPrefix(prefix, dryRun, desiredRuleNames));
        else if (removeStaleRules)
            lines.Add(LocalizedLine.Info("manager.firewall.staleCleanupDeferred"));

        if (!dryRun)
            AppendActivationVerification(lines, prefix, targets);

        return lines;
    }

    public static List<OperationLine> DisableFirewallBlock(string prefix, bool dryRun)
    {
        var lines = RemoveRulesByPrefix(
            prefix,
            dryRun,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        if (dryRun)
            return lines;

        var query = QueryManagedRules(prefix);
        if (!query.Success)
            lines.Add(LocalizedLine.Err("manager.firewall.verificationFailed", query.Error));
        else if (query.Rules.Count > 0)
            lines.Add(LocalizedLine.Err("manager.firewall.rulesRemain", query.Rules.Count));

        return lines;
    }

    private static List<OperationLine> EnsurePackageRule(
        string prefix,
        string ruleName,
        string packageSid,
        bool dryRun)
    {
        var query = QueryManagedRules(prefix);
        if (!query.Success)
            return [LocalizedLine.Err("manager.firewall.verificationFailed", query.Error)];

        var matchingRule = query.Rules.FirstOrDefault(rule =>
            rule.Name.Equals(ruleName, StringComparison.OrdinalIgnoreCase) &&
            rule.Enabled &&
            rule.IsOutbound &&
            rule.IsBlock &&
            rule.PackageSid.Equals(packageSid, StringComparison.OrdinalIgnoreCase));

        if (matchingRule is not null)
            return [LocalizedLine.Info("manager.firewall.packageRuleAlreadyCurrent", ruleName)];

        if (dryRun)
            return [LocalizedLine.Ok("manager.firewall.wouldCreatePackageRule", ruleName)];

        var lines = RemoveRulesByExactName(ruleName);
        if (TryAddPackageRulePowerShell(ruleName, packageSid, out var powerShellError) ||
            TryAddPackageRuleCom(ruleName, packageSid, out var comError))
        {
            lines.Add(LocalizedLine.Ok("manager.firewall.createdPackageRule", ruleName));
            return lines;
        }

        lines.Add(LocalizedLine.Err(
            "manager.firewall.failedToCreatePackageRule",
            comError,
            powerShellError));
        return lines;
    }

    private static List<OperationLine> RemoveRulesByPrefix(
        string prefix,
        bool dryRun,
        HashSet<string> preservedRuleNames)
    {
        var query = QueryManagedRules(prefix);
        if (!query.Success)
            return [LocalizedLine.Err("manager.firewall.verificationFailed", query.Error)];

        var ruleNames = query.Rules
            .Select(rule => rule.Name)
            .Where(name => !preservedRuleNames.Contains(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (ruleNames.Count == 0)
            return [LocalizedLine.Info("manager.firewall.noRulesFound", prefix)];

        if (dryRun)
            return ruleNames.Select(name => LocalizedLine.Ok("manager.firewall.wouldRemoveRule", name)).ToList();

        var lines = new List<OperationLine>();
        foreach (var ruleName in ruleNames)
            lines.AddRange(RemoveRulesByExactName(ruleName));

        return lines;
    }

    private static List<OperationLine> RemoveRulesByExactName(string ruleName)
    {
        var escapedName = EscapePowerShellLiteral(ruleName);
        var netSecurityScript = $"""
$ErrorActionPreference = 'Stop'
Get-NetFirewallRule -DisplayName '{escapedName}' -ErrorAction SilentlyContinue |
    Remove-NetFirewallRule -ErrorAction Stop
""";

        if (PowerShellRunner.TryRunScript(netSecurityScript, out _, out var netSecurityError, 30_000))
            return [LocalizedLine.Ok("manager.firewall.removedRule", ruleName)];

        var comScript = $"""
$ErrorActionPreference = 'Stop'
$policy = New-Object -ComObject 'HNetCfg.FwPolicy2'
$policy.Rules.Remove('{escapedName}')
""";
        var comSucceeded = PowerShellRunner.TryRunScript(comScript, out _, out var comError, 30_000);
        return comSucceeded
            ? [LocalizedLine.Ok("manager.firewall.removedRule", ruleName)]
            : [LocalizedLine.Err(
                "manager.firewall.failedToRemoveRule",
                ruleName,
                $"NetSecurity: {netSecurityError}; COM: {comError}")];
    }

    private static void AppendActivationVerification(
        List<OperationLine> lines,
        string prefix,
        FirewallTargetSet targets)
    {
        var status = InspectProtection(prefix, targets);
        if (!status.QuerySucceeded)
        {
            lines.Add(LocalizedLine.Err("manager.firewall.verificationFailed", status.Error));
            return;
        }

        if (!status.Targets.DiscoveryComplete)
        {
            lines.Add(LocalizedLine.Info(
                "manager.firewall.discoveryIncomplete",
                status.Targets.ScanErrors.Count));
        }

        if (!status.EnforcementActive)
            lines.Add(LocalizedLine.Err("manager.firewall.enforcementInactive", status.EnforcementDetails));

        if (status.MissingExecutableRules.Count > 0)
        {
            lines.Add(LocalizedLine.Info(
                "manager.firewall.missingProgramRules",
                status.MissingExecutableRules.Count));
        }

        if (status.PackageRuleRequired && !status.PackageRulePresent)
            lines.Add(LocalizedLine.Err("manager.firewall.packageRuleMissing"));

        if (status.IsComplete)
            lines.Add(LocalizedLine.Ok("manager.firewall.verificationPassed", status.RuleCount));
    }

    private static bool HasCurrentProgramRule(
        IEnumerable<FirewallRuleInfo> rules,
        string ruleName,
        string executablePath)
    {
        var normalizedExecutablePath = NormalizePath(executablePath);
        return normalizedExecutablePath is not null && rules.Any(rule =>
            rule.Name.Equals(ruleName, StringComparison.OrdinalIgnoreCase) &&
            rule.Enabled &&
            rule.IsOutbound &&
            rule.IsBlock &&
            string.Equals(
                NormalizePath(rule.ProgramPath),
                normalizedExecutablePath,
                StringComparison.OrdinalIgnoreCase));
    }

    private static FirewallRuleQuery QueryManagedRules(string prefix)
    {
        var powerShellQuery = QueryManagedRulesPowerShell(prefix);
        if (powerShellQuery.Success)
            return powerShellQuery;

        var comQuery = QueryManagedRulesCom(prefix);
        if (comQuery.Success)
            return comQuery;

        return FirewallRuleQuery.Failed(
            $"PowerShell: {powerShellQuery.Error}; COM: {comQuery.Error}");
    }

    private static FirewallRuleQuery QueryManagedRulesPowerShell(string prefix)
    {
        var escapedPattern = EscapePowerShellLiteral(prefix + " - *");
        var script = $$"""
$ErrorActionPreference = 'Stop'
$result = @(
    Get-NetFirewallRule -DisplayName '{{escapedPattern}}' -ErrorAction SilentlyContinue |
        ForEach-Object {
            $filter = $_ | Get-NetFirewallApplicationFilter -ErrorAction Stop
            $program = [string]$filter.Program
            $package = [string]$filter.Package
            [PSCustomObject]@{
                Name = [string]$_.DisplayName
                Enabled = [bool]($_.Enabled -eq 'True')
                IsOutbound = [bool]($_.Direction -eq 'Outbound')
                IsBlock = [bool]($_.Action -eq 'Block')
                ProgramPath = if ($program -eq 'Any') { '' } else { $program }
                PackageSid = if ($package -eq 'Any') { '' } else { $package }
            }
        }
)
ConvertTo-Json -InputObject $result -Compress
""";

        if (!PowerShellRunner.TryRunScript(script, out var output, out var error, 30_000))
            return FirewallRuleQuery.Failed(error);

        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(output) ? "[]" : output);
            var elements = document.RootElement.ValueKind switch
            {
                JsonValueKind.Array => document.RootElement.EnumerateArray().ToList(),
                JsonValueKind.Object => [document.RootElement],
                _ => []
            };
            var rules = elements.Select(element => new FirewallRuleInfo(
                ReadJsonString(element, "Name"),
                ReadJsonBoolean(element, "Enabled"),
                ReadJsonBoolean(element, "IsOutbound"),
                ReadJsonBoolean(element, "IsBlock"),
                ReadJsonString(element, "ProgramPath"),
                ReadJsonString(element, "PackageSid"))).ToList();

            return FirewallRuleQuery.Succeeded(rules);
        }
        catch (Exception exception)
        {
            return FirewallRuleQuery.Failed(exception.Message);
        }
    }

    private static FirewallRuleQuery QueryManagedRulesCom(string prefix)
    {
        var escapedPrefix = EscapePowerShellLiteral(prefix + " - ");
        var script = $$"""
$ErrorActionPreference = 'Stop'
$policy = New-Object -ComObject 'HNetCfg.FwPolicy2'
$result = @(
    foreach ($rule in $policy.Rules) {
        $name = [string]$rule.Name
        if (-not $name.StartsWith('{{escapedPrefix}}', [StringComparison]::OrdinalIgnoreCase)) {
            continue
        }

        [PSCustomObject]@{
            Name = $name
            Enabled = [bool]$rule.Enabled
            IsOutbound = [bool]([int]$rule.Direction -eq 2)
            IsBlock = [bool]([int]$rule.Action -eq 0)
            ProgramPath = [string]$rule.ApplicationName
            PackageSid = [string]$rule.LocalAppPackageId
        }
    }
)
ConvertTo-Json -InputObject $result -Compress
""";

        if (!PowerShellRunner.TryRunScript(script, out var output, out var error, 30_000))
            return FirewallRuleQuery.Failed(error);

        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(output) ? "[]" : output);
            var elements = document.RootElement.ValueKind switch
            {
                JsonValueKind.Array => document.RootElement.EnumerateArray().ToList(),
                JsonValueKind.Object => [document.RootElement],
                _ => []
            };
            var rules = elements.Select(element => new FirewallRuleInfo(
                ReadJsonString(element, "Name"),
                ReadJsonBoolean(element, "Enabled"),
                ReadJsonBoolean(element, "IsOutbound"),
                ReadJsonBoolean(element, "IsBlock"),
                ReadJsonString(element, "ProgramPath"),
                ReadJsonString(element, "PackageSid"))).ToList();

            return FirewallRuleQuery.Succeeded(rules);
        }
        catch (Exception exception)
        {
            return FirewallRuleQuery.Failed(exception.Message);
        }
    }

    private static string ReadJsonString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var property)
            ? property.GetString() ?? string.Empty
            : string.Empty;

    private static bool ReadJsonBoolean(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var property) &&
           property.ValueKind is JsonValueKind.True or JsonValueKind.False &&
           property.GetBoolean();

    private static bool TryAddProgramRuleCom(string ruleName, string executablePath, out string error)
        => TryAddRuleCom(ruleName, "ApplicationName", executablePath, out error);

    private static bool TryAddPackageRuleCom(string ruleName, string packageSid, out string error)
        => TryAddRuleCom(ruleName, "LocalAppPackageId", packageSid, out error);

    private static bool TryAddRuleCom(
        string ruleName,
        string identityProperty,
        string identityValue,
        out string error)
    {
        var escapedRuleName = EscapePowerShellLiteral(ruleName);
        var escapedIdentity = EscapePowerShellLiteral(identityValue);
        var script = $$"""
$ErrorActionPreference = 'Stop'
$rule = New-Object -ComObject 'HNetCfg.FWRule'
$rule.Name = '{{escapedRuleName}}'
$rule.Description = 'OmenGamingHubUnlocker'
$rule.Action = 0
$rule.Direction = 2
$rule.Enabled = $true
$rule.InterfaceTypes = 'All'
$rule.Profiles = [int]::MaxValue
$rule.{{identityProperty}} = '{{escapedIdentity}}'
$policy = New-Object -ComObject 'HNetCfg.FwPolicy2'
$policy.Rules.Add($rule)
""";

        return PowerShellRunner.TryRunScript(script, out _, out error, 30_000);
    }

    private static bool TryAddProgramRulePowerShell(string ruleName, string executablePath, out string error)
    {
        var script = $"""
$ErrorActionPreference = 'Stop'
New-NetFirewallRule -DisplayName '{EscapePowerShellLiteral(ruleName)}' `
    -Direction Outbound `
    -Program '{EscapePowerShellLiteral(executablePath)}' `
    -Action Block `
    -Profile Any `
    -Enabled True `
    -ErrorAction Stop | Out-Null
""";

        return PowerShellRunner.TryRunScript(script, out _, out error, 30_000);
    }

    private static bool TryAddPackageRulePowerShell(string ruleName, string packageSid, out string error)
    {
        var script = $"""
$ErrorActionPreference = 'Stop'
New-NetFirewallRule -DisplayName '{EscapePowerShellLiteral(ruleName)}' `
    -Direction Outbound `
    -Package '{EscapePowerShellLiteral(packageSid)}' `
    -Action Block `
    -Profile Any `
    -Enabled True `
    -ErrorAction Stop | Out-Null
""";

        return PowerShellRunner.TryRunScript(script, out _, out error, 30_000);
    }

    private static string BuildProgramRuleName(string prefix, string executablePath)
    {
        var normalizedPath = NormalizePath(executablePath) ?? executablePath;
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedPath)))[..10];
        return $"{prefix} - Program - {Path.GetFileName(executablePath)} - {hash}";
    }

    private static string EscapePowerShellLiteral(string value)
        => value.Replace("'", "''", StringComparison.Ordinal);

    private static string? NormalizePath(string? path)
    {
        try
        {
            return string.IsNullOrWhiteSpace(path) ? null : Path.GetFullPath(path);
        }
        catch
        {
            return null;
        }
    }

    private static bool ScanExecutables(
        string directoryPath,
        HashSet<string> destination,
        List<string> discoveryErrors,
        bool requireDirectory)
    {
        try
        {
            if (!Directory.Exists(directoryPath))
                return !requireDirectory;

            foreach (var executablePath in Directory.EnumerateFiles(directoryPath, "*.exe", SearchOption.AllDirectories))
                destination.Add(Path.GetFullPath(executablePath));

            return true;
        }
        catch (Exception exception)
        {
            discoveryErrors.Add($"{directoryPath}: {exception.Message}");
            return false;
        }
    }

    private static FirewallEnforcementStatus InspectEnforcement()
    {
        const string script = """
$ErrorActionPreference = 'Stop'
$policy = New-Object -ComObject 'HNetCfg.FwPolicy2'
[PSCustomObject]@{
    CurrentProfiles = [int]$policy.CurrentProfileTypes
    DomainEnabled = [bool]$policy.FirewallEnabled(1)
    PrivateEnabled = [bool]$policy.FirewallEnabled(2)
    PublicEnabled = [bool]$policy.FirewallEnabled(4)
} | ConvertTo-Json -Compress
""";

        if (!PowerShellRunner.TryRunScript(script, out var output, out var error, 20_000))
            return FirewallEnforcementStatus.Failed(error);

        try
        {
            using var document = JsonDocument.Parse(output);
            var root = document.RootElement;
            var currentProfiles = root.TryGetProperty("CurrentProfiles", out var currentProfilesProperty) &&
                                  currentProfilesProperty.TryGetInt32(out var profileValue)
                ? profileValue
                : 0;
            if (currentProfiles == 0)
                return FirewallEnforcementStatus.Failed(Text.Get("manager.firewall.noCurrentProfile"));

            var disabledProfiles = new List<string>();
            foreach (var (profileFlag, profileName, enabledProperty) in new[]
                     {
                         (1, "Domain", "DomainEnabled"),
                         (2, "Private", "PrivateEnabled"),
                         (4, "Public", "PublicEnabled")
                     })
            {
                if ((currentProfiles & profileFlag) != 0 && !ReadJsonBoolean(root, enabledProperty))
                    disabledProfiles.Add(profileName);
            }

            return disabledProfiles.Count == 0
                ? FirewallEnforcementStatus.Enabled()
                : FirewallEnforcementStatus.Disabled(Text.Format(
                    "manager.firewall.disabledProfiles",
                    string.Join(", ", disabledProfiles)));
        }
        catch (Exception exception)
        {
            return FirewallEnforcementStatus.Failed(exception.Message);
        }
    }

    private sealed record FirewallRuleQuery(bool Success, IReadOnlyList<FirewallRuleInfo> Rules, string Error)
    {
        public static FirewallRuleQuery Succeeded(IReadOnlyList<FirewallRuleInfo> rules)
            => new(true, rules, string.Empty);

        public static FirewallRuleQuery Failed(string error)
            => new(false, [], error);
    }

    private sealed record FirewallEnforcementStatus(
        bool InspectionSucceeded,
        bool Active,
        string Details)
    {
        public static FirewallEnforcementStatus Enabled()
            => new(true, true, Text.Get("manager.firewall.enforcementEnabled"));

        public static FirewallEnforcementStatus Disabled(string details)
            => new(true, false, details);

        public static FirewallEnforcementStatus Failed(string details)
            => new(false, false, details);
    }

    private static bool HasAnyTarget(this FirewallTargetSet targets)
        => !string.IsNullOrWhiteSpace(targets.PackageSid) || targets.AllExecutables.Count > 0;
}
