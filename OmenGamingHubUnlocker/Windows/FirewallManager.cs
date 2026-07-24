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
    IReadOnlySet<string> ExternalExecutables)
{
    public IReadOnlySet<string> AllExecutables { get; } = PackageExecutables
        .Concat(ExternalExecutables)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);
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
    string Error)
{
    public int RuleCount => Rules.Count;
    public bool PackageRuleRequired => !string.IsNullOrWhiteSpace(Targets.PackageSid);
    public bool HasProtectionIdentity => Targets.AllExecutables.Count > 0 || PackageRuleRequired;

    public bool IsComplete =>
        QuerySucceeded &&
        HasProtectionIdentity &&
        MissingExecutableRules.Count == 0 &&
        (!PackageRuleRequired || PackageRulePresent);
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
        return query.Success
            ? (true, Text.Get("manager.firewall.capabilityOk"))
            : (false, query.Error);
    }

    public static int CountRulesByPrefix(string prefix)
        => QueryManagedRules(prefix).Rules.Count;

    public static HashSet<string> DiscoverCandidateExecutables()
        => DiscoverTargets().AllExecutables.ToHashSet(StringComparer.OrdinalIgnoreCase);

    public static FirewallTargetSet DiscoverTargets()
    {
        var packageExecutables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var externalExecutables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var package = AppxPackageManager.QueryPackages(OmenTargets.AppxFilters).FirstOrDefault();

        if (package is not null)
            TryScanExecutables(package.InstallLocation, packageExecutables);

        foreach (var relativeDirectory in OmenTargets.ExtraExeDirsRelative)
        {
            TryScanExecutables(Path.Combine(WindowsPaths.ProgramFiles, relativeDirectory), externalExecutables);

            if (!string.Equals(WindowsPaths.ProgramFiles, WindowsPaths.ProgramFilesX86, StringComparison.OrdinalIgnoreCase))
                TryScanExecutables(Path.Combine(WindowsPaths.ProgramFilesX86, relativeDirectory), externalExecutables);
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
            externalExecutables);
    }

    public static FirewallProtectionStatus InspectProtection(string prefix)
    {
        var targets = DiscoverTargets();
        var query = QueryManagedRules(prefix);

        if (!query.Success)
        {
            return new FirewallProtectionStatus(
                false,
                targets,
                query.Rules,
                targets.AllExecutables.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList(),
                [],
                false,
                query.Error);
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
            string.Empty);
    }

    public static List<OperationLine> ActivateFirewallBlock(string prefix, bool dryRun)
    {
        var lines = new List<OperationLine>();
        var targets = DiscoverTargets();

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

        // Keep the stable package rule active while obsolete executable rules are replaced.
        lines.AddRange(RemoveRulesByPrefix(
            prefix,
            dryRun,
            new HashSet<string>([packageRuleName], StringComparer.OrdinalIgnoreCase)));

        foreach (var executablePath in targets.AllExecutables.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var ruleName = BuildProgramRuleName(prefix, executablePath);
            if (dryRun)
            {
                lines.Add(LocalizedLine.Ok("manager.firewall.wouldBlockOutbound", executablePath));
                continue;
            }

            if (TryAddProgramRuleCom(ruleName, executablePath, out var comError) ||
                TryAddProgramRulePowerShell(ruleName, executablePath, out var powerShellError))
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

        if (!dryRun)
            AppendActivationVerification(lines, prefix);

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
        if (TryAddPackageRuleCom(ruleName, packageSid, out var comError) ||
            TryAddPackageRulePowerShell(ruleName, packageSid, out var powerShellError))
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
        try
        {
            var firewallPolicyType = Type.GetTypeFromProgID("HNetCfg.FwPolicy2")
                                     ?? throw new InvalidOperationException(Text.Get("manager.firewall.capabilityNotAvailable"));
            dynamic firewallPolicy = Activator.CreateInstance(firewallPolicyType)!;
            dynamic firewallRules = firewallPolicy.Rules;
            firewallRules.Remove(ruleName);
            return [LocalizedLine.Ok("manager.firewall.removedRule", ruleName)];
        }
        catch (Exception comException)
        {
            var escapedName = EscapePowerShellLiteral(ruleName);
            var script = $"""
$ErrorActionPreference = 'Stop'
Get-NetFirewallRule -DisplayName '{escapedName}' -ErrorAction SilentlyContinue |
    Remove-NetFirewallRule -ErrorAction Stop
""";

            var succeeded = PowerShellRunner.TryRunScript(script, out _, out var error, 30_000);
            return succeeded
                ? [LocalizedLine.Ok("manager.firewall.removedRule", ruleName)]
                : [LocalizedLine.Err("manager.firewall.failedToRemoveRule", ruleName, $"{comException.Message}; {error}")];
        }
    }

    private static void AppendActivationVerification(List<OperationLine> lines, string prefix)
    {
        var status = InspectProtection(prefix);
        if (!status.QuerySucceeded)
        {
            lines.Add(LocalizedLine.Err("manager.firewall.verificationFailed", status.Error));
            return;
        }

        if (status.MissingExecutableRules.Count > 0)
        {
            lines.Add(LocalizedLine.Err(
                "manager.firewall.missingProgramRules",
                status.MissingExecutableRules.Count));
        }

        if (status.PackageRuleRequired && !status.PackageRulePresent)
            lines.Add(LocalizedLine.Err("manager.firewall.packageRuleMissing"));

        if (status.IsComplete)
            lines.Add(LocalizedLine.Ok("manager.firewall.verificationPassed", status.RuleCount));
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
        try
        {
            var firewallPolicyType = Type.GetTypeFromProgID("HNetCfg.FwPolicy2");
            if (firewallPolicyType is null)
                return FirewallRuleQuery.Failed(Text.Get("manager.firewall.capabilityNotAvailable"));

            dynamic firewallPolicy = Activator.CreateInstance(firewallPolicyType)!;
            dynamic firewallRules = firewallPolicy.Rules;
            var rules = new List<FirewallRuleInfo>();

            foreach (dynamic firewallRule in (System.Collections.IEnumerable)firewallRules)
            {
                string name = firewallRule.Name;
                if (!name.StartsWith(prefix + " - ", StringComparison.OrdinalIgnoreCase))
                    continue;

                rules.Add(new FirewallRuleInfo(
                    name,
                    ReadDynamic(() => (bool)firewallRule.Enabled, false),
                    ReadDynamic(() => (int)firewallRule.Direction, 0) == 2,
                    ReadDynamic(() => (int)firewallRule.Action, 1) == 0,
                    ReadDynamic(() => (string)firewallRule.ApplicationName, string.Empty) ?? string.Empty,
                    ReadDynamic(() => (string)firewallRule.LocalAppPackageId, string.Empty) ?? string.Empty));
            }

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

    private static T ReadDynamic<T>(Func<T> reader, T fallback)
    {
        try
        {
            return reader();
        }
        catch
        {
            return fallback;
        }
    }

    private static bool TryAddProgramRuleCom(string ruleName, string executablePath, out string error)
        => TryAddRuleCom(ruleName, rule => rule.ApplicationName = executablePath, out error);

    private static bool TryAddPackageRuleCom(string ruleName, string packageSid, out string error)
        => TryAddRuleCom(ruleName, rule => rule.LocalAppPackageId = packageSid, out error);

    private static bool TryAddRuleCom(string ruleName, Action<dynamic> configureIdentity, out string error)
    {
        try
        {
            var firewallRuleType = Type.GetTypeFromProgID("HNetCfg.FWRule");
            var firewallPolicyType = Type.GetTypeFromProgID("HNetCfg.FwPolicy2");
            if (firewallRuleType is null || firewallPolicyType is null)
            {
                error = Text.Get("manager.firewall.comTypesUnavailable");
                return false;
            }

            dynamic firewallRule = Activator.CreateInstance(firewallRuleType)!;
            firewallRule.Name = ruleName;
            firewallRule.Description = "OmenGamingHubUnlocker";
            firewallRule.Action = 0;
            firewallRule.Direction = 2;
            firewallRule.Enabled = true;
            firewallRule.InterfaceTypes = "All";
            firewallRule.Profiles = int.MaxValue;
            configureIdentity(firewallRule);

            dynamic firewallPolicy = Activator.CreateInstance(firewallPolicyType)!;
            dynamic firewallRules = firewallPolicy.Rules;
            firewallRules.Add(firewallRule);

            error = string.Empty;
            return true;
        }
        catch (Exception exception)
        {
            error = exception.Message;
            return false;
        }
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

    private static void TryScanExecutables(string directoryPath, HashSet<string> destination)
    {
        try
        {
            if (!Directory.Exists(directoryPath))
                return;

            foreach (var executablePath in Directory.EnumerateFiles(directoryPath, "*.exe", SearchOption.AllDirectories))
                destination.Add(Path.GetFullPath(executablePath));
        }
        catch
        {
            // Other locations remain useful when a directory has a transient ACL or update race.
        }
    }

    private sealed record FirewallRuleQuery(bool Success, IReadOnlyList<FirewallRuleInfo> Rules, string Error)
    {
        public static FirewallRuleQuery Succeeded(IReadOnlyList<FirewallRuleInfo> rules)
            => new(true, rules, string.Empty);

        public static FirewallRuleQuery Failed(string error)
            => new(false, [], error);
    }

    private static bool HasAnyTarget(this FirewallTargetSet targets)
        => !string.IsNullOrWhiteSpace(targets.PackageSid) || targets.AllExecutables.Count > 0;
}
