using OmenGamingHubUnlocker.Core;

namespace OmenGamingHubUnlocker.Windows;

public static class FirewallManager
{
    public static (bool ok, string details) CheckCapability()
    {
        try
        {
            var t = Type.GetTypeFromProgID("HNetCfg.FwPolicy2");
            if (t is null)
                return (false, "HNetCfg.FwPolicy2 COM not available.");

            dynamic policy = Activator.CreateInstance(t)!;
            _ = policy.Rules;
            return (true, "COM access ok.");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public static int CountRulesByPrefix(string prefix)
    {
        try
        {
            var policyType = Type.GetTypeFromProgID("HNetCfg.FwPolicy2");
            if (policyType is null)
                return 0;

            dynamic policy = Activator.CreateInstance(policyType)!;
            dynamic rules = policy.Rules;

            var count = 0;
            foreach (dynamic rule in (System.Collections.IEnumerable)rules)
            {
                string name = rule.Name;
                if (name.StartsWith(prefix + " - ", StringComparison.OrdinalIgnoreCase))
                    count++;
            }

            return count;
        }
        catch
        {
            return 0;
        }
    }

    public static HashSet<string> DiscoverCandidateExecutables()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var locations = AppxPackageManager
                .QueryPackages(OmenTargets.AppxFilters)
                .Select(x => x.InstallLocation)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase);

            foreach (var location in locations)
                TryScanExe(location, set);
        }
        catch
        {
            // Keep non-fatal and continue with classic locations.
        }

        var pf = WindowsPaths.ProgramFiles;
        var pf86 = WindowsPaths.ProgramFilesX86;

        foreach (var rel in OmenTargets.ExtraExeDirsRelative)
        {
            TryScanExe(Path.Combine(pf, rel), set);

            if (!string.Equals(pf, pf86, StringComparison.OrdinalIgnoreCase))
                TryScanExe(Path.Combine(pf86, rel), set);
        }

        return set;
    }

    public static List<OperationLine> ActivateFirewallBlock(string prefix, bool dryRun)
    {
        var lines = new List<OperationLine>();

        lines.AddRange(RemoveByPrefix(prefix, dryRun));

        var exes = DiscoverCandidateExecutables();
        if (exes.Count == 0)
        {
            lines.Add(new OperationLine { Level = "WARN", Text = "Firewall: no executables discovered. No rules created." });
            return lines;
        }

        foreach (var exe in exes.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            var file = Path.GetFileName(exe);
            var ruleName = $"{prefix} - {file}";

            if (dryRun)
            {
                lines.Add(new OperationLine { Level = "OK", Text = $"Firewall: would block outbound: {file}" });
                continue;
            }

            var created = TryAddOutboundBlockRuleCom(ruleName, exe, out var comError);
            if (created)
            {
                lines.Add(new OperationLine { Level = "OK", Text = $"Firewall: created block rule: {ruleName}" });
                continue;
            }

            var psOk = TryAddOutboundBlockRulePowerShell(ruleName, exe, out var psError);
            lines.Add(new OperationLine
            {
                Level = psOk ? "WARN" : "ERR",
                Text = psOk
                    ? $"Firewall: COM failed, PowerShell fallback applied for {file}."
                    : $"Firewall: failed to create rule for {file}. COM error: {comError}. PS error: {psError}"
            });
        }

        return lines;
    }

    public static List<OperationLine> DisableFirewallBlock(string prefix, bool dryRun)
        => RemoveByPrefix(prefix, dryRun);

    private static List<OperationLine> RemoveByPrefix(string prefix, bool dryRun)
    {
        var lines = new List<OperationLine>();

        try
        {
            var policyType = Type.GetTypeFromProgID("HNetCfg.FwPolicy2");
            if (policyType is not null)
            {
                dynamic policy = Activator.CreateInstance(policyType)!;
                dynamic rules = policy.Rules;

                var names = new List<string>();
                foreach (dynamic rule in (System.Collections.IEnumerable)rules)
                {
                    string name = rule.Name;
                    if (name.StartsWith(prefix + " - ", StringComparison.OrdinalIgnoreCase))
                        names.Add(name);
                }

                if (names.Count == 0)
                {
                    lines.Add(new OperationLine { Level = "INFO", Text = $"Firewall: no rules found with prefix '{prefix} - '." });
                    return lines;
                }

                foreach (var name in names)
                {
                    if (dryRun)
                    {
                        lines.Add(new OperationLine { Level = "OK", Text = $"Firewall: would remove rule: {name}" });
                        continue;
                    }

                    try
                    {
                        rules.Remove(name);
                        lines.Add(new OperationLine { Level = "OK", Text = $"Firewall: removed rule: {name}" });
                    }
                    catch (Exception ex)
                    {
                        lines.Add(new OperationLine { Level = "WARN", Text = $"Firewall: failed to remove {name}: {ex.Message}" });
                    }
                }

                return lines;
            }
        }
        catch
        {
            // Fall through to PowerShell fallback.
        }

        if (dryRun)
        {
            lines.Add(new OperationLine { Level = "OK", Text = $"Firewall: would remove rules by PowerShell filter: {prefix} - *" });
            return lines;
        }

        var cmd = $"Get-NetFirewallRule -DisplayName \"{prefix} - *\" -ErrorAction SilentlyContinue | Remove-NetFirewallRule -ErrorAction SilentlyContinue";
        var ok = PowerShellRunner.TryRun("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -Command \"{cmd}\"", out _, out var error, 30_000);

        lines.Add(new OperationLine
        {
            Level = ok ? "OK" : "ERR",
            Text = ok
                ? $"Firewall: rules removed via PowerShell filter: {prefix} - *"
                : $"Firewall: PowerShell fallback failed: {error}"
        });

        return lines;
    }

    private static bool TryAddOutboundBlockRuleCom(string ruleName, string exePath, out string error)
    {
        try
        {
            var ruleType = Type.GetTypeFromProgID("HNetCfg.FWRule");
            var policyType = Type.GetTypeFromProgID("HNetCfg.FwPolicy2");

            if (ruleType is null || policyType is null)
            {
                error = "Firewall COM types not available.";
                return false;
            }

            dynamic rule = Activator.CreateInstance(ruleType)!;

            rule.Name = ruleName;
            rule.Description = "OmenGamingHubUnlocker";
            rule.ApplicationName = exePath;
            rule.Action = 0;
            rule.Direction = 2;
            rule.Enabled = true;
            rule.InterfaceTypes = "All";
            rule.Profiles = int.MaxValue;

            dynamic policy = Activator.CreateInstance(policyType)!;
            dynamic rules = policy.Rules;
            rules.Add(rule);

            error = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static bool TryAddOutboundBlockRulePowerShell(string ruleName, string exePath, out string error)
    {
        var cmd = $"New-NetFirewallRule -DisplayName \"{ruleName}\" -Direction Outbound -Program \"{exePath}\" -Action Block -Profile Any -Enabled True -ErrorAction Stop | Out-Null";
        var ok = PowerShellRunner.TryRun("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -Command \"{cmd}\"", out _, out var err, 30_000);
        error = err;
        return ok;
    }

    private static void TryScanExe(string dir, HashSet<string> set)
    {
        try
        {
            if (!Directory.Exists(dir))
                return;

            foreach (var exe in Directory.EnumerateFiles(dir, "*.exe", SearchOption.AllDirectories))
                set.Add(exe);
        }
        catch
        {
            // Keep non-fatal.
        }
    }
}
