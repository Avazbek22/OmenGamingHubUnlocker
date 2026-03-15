namespace OmenGamingHubUnlocker.Windows;

/// <summary>
/// Encapsulates firewall rule discovery and block-rule management for OMEN binaries.
/// </summary>
public static class FirewallManager
{
    public static (bool ok, string details) CheckCapability()
    {
        try
        {
            var firewallPolicyType = Type.GetTypeFromProgID("HNetCfg.FwPolicy2");
            if (firewallPolicyType is null)
                return (false, Text.Get("manager.firewall.capabilityNotAvailable"));

            dynamic firewallPolicy = Activator.CreateInstance(firewallPolicyType)!;
            _ = firewallPolicy.Rules;
            return (true, Text.Get("manager.firewall.capabilityOk"));
        }
        catch (Exception exception)
        {
            return (false, exception.Message);
        }
    }

    public static int CountRulesByPrefix(string prefix)
    {
        try
        {
            var firewallPolicyType = Type.GetTypeFromProgID("HNetCfg.FwPolicy2");
            if (firewallPolicyType is null)
                return 0;

            dynamic firewallPolicy = Activator.CreateInstance(firewallPolicyType)!;
            dynamic firewallRules = firewallPolicy.Rules;

            var matchingRuleCount = 0;
            foreach (dynamic firewallRule in (System.Collections.IEnumerable)firewallRules)
            {
                string ruleName = firewallRule.Name;
                if (ruleName.StartsWith(prefix + " - ", StringComparison.OrdinalIgnoreCase))
                    matchingRuleCount++;
            }

            return matchingRuleCount;
        }
        catch
        {
            return 0;
        }
    }

    public static HashSet<string> DiscoverCandidateExecutables()
    {
        var discoveredExecutables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var installLocations = AppxPackageManager
                .QueryPackages(OmenTargets.AppxFilters)
                .Select(package => package.InstallLocation)
                .Where(location => !string.IsNullOrWhiteSpace(location))
                .Distinct(StringComparer.OrdinalIgnoreCase);

            foreach (var installLocation in installLocations)
                TryScanExecutables(installLocation, discoveredExecutables);
        }
        catch
        {
            // AppX discovery is optional; classic locations are still scanned below.
        }

        var programFilesPath = WindowsPaths.ProgramFiles;
        var programFilesX86Path = WindowsPaths.ProgramFilesX86;

        foreach (var relativeDirectory in OmenTargets.ExtraExeDirsRelative)
        {
            TryScanExecutables(Path.Combine(programFilesPath, relativeDirectory), discoveredExecutables);

            if (!string.Equals(programFilesPath, programFilesX86Path, StringComparison.OrdinalIgnoreCase))
                TryScanExecutables(Path.Combine(programFilesX86Path, relativeDirectory), discoveredExecutables);
        }

        return discoveredExecutables;
    }

    public static List<OperationLine> ActivateFirewallBlock(string prefix, bool dryRun)
    {
        var operationLines = new List<OperationLine>();

        operationLines.AddRange(RemoveRulesByPrefix(prefix, dryRun));

        var candidateExecutables = DiscoverCandidateExecutables();
        if (candidateExecutables.Count == 0)
        {
            operationLines.Add(LocalizedLine.Warn("manager.firewall.noExecutables"));
            return operationLines;
        }

        foreach (var executablePath in candidateExecutables.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var executableName = Path.GetFileName(executablePath);
            var ruleName = $"{prefix} - {executableName}";

            if (dryRun)
            {
                operationLines.Add(LocalizedLine.Ok("manager.firewall.wouldBlockOutbound", executableName));
                continue;
            }

            var comRuleCreated = TryAddOutboundBlockRuleCom(ruleName, executablePath, out var comError);
            if (comRuleCreated)
            {
                operationLines.Add(LocalizedLine.Ok("manager.firewall.createdBlockRule", ruleName));
                continue;
            }

            var powerShellRuleCreated = TryAddOutboundBlockRulePowerShell(ruleName, executablePath, out var powerShellError);
            operationLines.Add(powerShellRuleCreated
                ? LocalizedLine.Warn("manager.firewall.comFailedPowerShellApplied", executableName)
                : LocalizedLine.Err("manager.firewall.failedToCreateRule", executableName, comError, powerShellError));
        }

        return operationLines;
    }

    public static List<OperationLine> DisableFirewallBlock(string prefix, bool dryRun)
        => RemoveRulesByPrefix(prefix, dryRun);

    private static List<OperationLine> RemoveRulesByPrefix(string prefix, bool dryRun)
    {
        var operationLines = new List<OperationLine>();

        try
        {
            var firewallPolicyType = Type.GetTypeFromProgID("HNetCfg.FwPolicy2");
            if (firewallPolicyType is not null)
            {
                dynamic firewallPolicy = Activator.CreateInstance(firewallPolicyType)!;
                dynamic firewallRules = firewallPolicy.Rules;

                var matchingRuleNames = new List<string>();
                foreach (dynamic firewallRule in (System.Collections.IEnumerable)firewallRules)
                {
                    string ruleName = firewallRule.Name;
                    if (ruleName.StartsWith(prefix + " - ", StringComparison.OrdinalIgnoreCase))
                        matchingRuleNames.Add(ruleName);
                }

                if (matchingRuleNames.Count == 0)
                {
                    operationLines.Add(LocalizedLine.Info("manager.firewall.noRulesFound", prefix));
                    return operationLines;
                }

                foreach (var ruleName in matchingRuleNames)
                {
                    if (dryRun)
                    {
                        operationLines.Add(LocalizedLine.Ok("manager.firewall.wouldRemoveRule", ruleName));
                        continue;
                    }

                    try
                    {
                        firewallRules.Remove(ruleName);
                        operationLines.Add(LocalizedLine.Ok("manager.firewall.removedRule", ruleName));
                    }
                    catch (Exception exception)
                    {
                        operationLines.Add(LocalizedLine.Warn("manager.firewall.failedToRemoveRule", ruleName, exception.Message));
                    }
                }

                return operationLines;
            }
        }
        catch
        {
            // COM removal failed; the PowerShell fallback below covers the same intent.
        }

        if (dryRun)
        {
            operationLines.Add(LocalizedLine.Ok("manager.firewall.wouldRemoveByFilter", prefix));
            return operationLines;
        }

        var powerShellCommand = $"Get-NetFirewallRule -DisplayName \"{prefix} - *\" -ErrorAction SilentlyContinue | Remove-NetFirewallRule -ErrorAction SilentlyContinue";
        var commandSucceeded = PowerShellRunner.TryRun(
            "powershell.exe",
            $"-NoProfile -ExecutionPolicy Bypass -Command \"{powerShellCommand}\"",
            out _,
            out var commandError,
            30_000);

        operationLines.Add(commandSucceeded
            ? LocalizedLine.Ok("manager.firewall.removedByFilter", prefix)
            : LocalizedLine.Err("manager.firewall.powerShellFallbackFailed", commandError));

        return operationLines;
    }

    private static bool TryAddOutboundBlockRuleCom(string ruleName, string executablePath, out string error)
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
            firewallRule.ApplicationName = executablePath;
            firewallRule.Action = 0;
            firewallRule.Direction = 2;
            firewallRule.Enabled = true;
            firewallRule.InterfaceTypes = "All";
            firewallRule.Profiles = int.MaxValue;

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

    private static bool TryAddOutboundBlockRulePowerShell(string ruleName, string executablePath, out string error)
    {
        var powerShellCommand =
            $"New-NetFirewallRule -DisplayName \"{ruleName}\" -Direction Outbound -Program \"{executablePath}\" -Action Block -Profile Any -Enabled True -ErrorAction Stop | Out-Null";

        var commandSucceeded = PowerShellRunner.TryRun(
            "powershell.exe",
            $"-NoProfile -ExecutionPolicy Bypass -Command \"{powerShellCommand}\"",
            out _,
            out var commandError,
            30_000);

        error = commandError;
        return commandSucceeded;
    }

    private static void TryScanExecutables(string directoryPath, HashSet<string> destination)
    {
        try
        {
            if (!Directory.Exists(directoryPath))
                return;

            foreach (var executablePath in Directory.EnumerateFiles(directoryPath, "*.exe", SearchOption.AllDirectories))
                destination.Add(executablePath);
        }
        catch
        {
            // Discovery must continue even if one directory becomes inaccessible during enumeration.
        }
    }
}
