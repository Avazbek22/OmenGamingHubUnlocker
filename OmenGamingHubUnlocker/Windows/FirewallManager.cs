using System.Text;
using OmenGamingHubUnlocker.Core;

namespace OmenGamingHubUnlocker.Windows;

public static class FirewallManager
{
    public static (bool ok, string details) CheckCapability()
    {
        try
        {
            var t = Type.GetTypeFromProgID("HNetCfg.FwPolicy2");
            if (t is null) return (false, "HNetCfg.FwPolicy2 COM not available.");

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
            if (policyType is null) return 0;

            dynamic policy = Activator.CreateInstance(policyType)!;
            dynamic rules = policy.Rules;

            int count = 0;
            foreach (dynamic r in (System.Collections.IEnumerable)rules)
            {
                string name = r.Name;
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

    /// <summary>
    /// Finds candidate executables:
    /// - AppX OMEN packages via PowerShell Get-AppxPackage (robust, no SDK contracts needed)
    /// - Classic Program Files locations (fallback/additional)
    /// </summary>
    public static HashSet<string> DiscoverCandidateExecutables()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1) AppX packages via PowerShell (no WinRT dependency)
        try
        {
            var locations = TryGetAppxInstallLocationsViaPowerShell();
            foreach (var loc in locations)
                TryScanExe(loc, set);
        }
        catch
        {
            // keep non-fatal; continue with classic dirs
        }

        // 2) Classic dirs (fallback / additional)
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

        // remove old prefix rules
        lines.AddRange(RemoveByPrefix(prefix, dryRun));

        // create new rules
        var exes = DiscoverCandidateExecutables();
        if (exes.Count == 0)
        {
            lines.Add(new OperationLine { Level = "WARN", Text = "Firewall: no executables discovered. No rules created." });
            return lines;
        }

        foreach (var exe in exes.OrderBy(x => x))
        {
            var file = Path.GetFileName(exe);
            var ruleName = $"{prefix} - {file}";

            if (dryRun)
            {
                lines.Add(new OperationLine { Level = "OK", Text = $"Firewall: would block outbound: {file}" });
                continue;
            }

            var created = TryAddOutboundBlockRuleCom(ruleName, exe, out var err);
            if (created)
            {
                lines.Add(new OperationLine { Level = "OK", Text = $"Firewall: created block rule: {ruleName}" });
                continue;
            }

            // Fallback: PowerShell New-NetFirewallRule
            var psOk = TryAddOutboundBlockRulePowerShell(ruleName, exe, out var psErr);

            lines.Add(new OperationLine
            {
                Level = psOk ? "WARN" : "ERR",
                Text = psOk
                    ? $"Firewall: COM failed, PowerShell fallback applied for {file}."
                    : $"Firewall: failed to create rule for {file}. COM error: {err}. PS error: {psErr}"
            });
        }

        return lines;
    }

    public static List<OperationLine> DisableFirewallBlock(string prefix, bool dryRun)
        => RemoveByPrefix(prefix, dryRun);

    private static List<OperationLine> RemoveByPrefix(string prefix, bool dryRun)
    {
        var lines = new List<OperationLine>();

        // Try COM removal first (iterate all rules, remove those with prefix)
        try
        {
            var policyType = Type.GetTypeFromProgID("HNetCfg.FwPolicy2");
            if (policyType is not null)
            {
                dynamic policy = Activator.CreateInstance(policyType)!;
                dynamic rules = policy.Rules;

                var names = new List<string>();
                foreach (dynamic r in (System.Collections.IEnumerable)rules)
                {
                    string name = r.Name;
                    if (name.StartsWith(prefix + " - ", StringComparison.OrdinalIgnoreCase))
                        names.Add(name);
                }

                if (names.Count == 0)
                {
                    lines.Add(new OperationLine { Level = "INFO", Text = $"Firewall: no rules found with prefix '{prefix} - '." });
                    return lines;
                }

                foreach (var n in names)
                {
                    if (dryRun)
                    {
                        lines.Add(new OperationLine { Level = "OK", Text = $"Firewall: would remove rule: {n}" });
                        continue;
                    }

                    try
                    {
                        rules.Remove(n);
                        lines.Add(new OperationLine { Level = "OK", Text = $"Firewall: removed rule: {n}" });
                    }
                    catch (Exception ex)
                    {
                        lines.Add(new OperationLine { Level = "WARN", Text = $"Firewall: failed to remove {n}: {ex.Message}" });
                    }
                }

                return lines;
            }
        }
        catch
        {
            // fallback below
        }

        // Fallback: PowerShell remove by DisplayName
        if (dryRun)
        {
            lines.Add(new OperationLine { Level = "OK", Text = $"Firewall: would remove rules by PowerShell filter: {prefix} - *" });
            return lines;
        }

        var cmd = $"Get-NetFirewallRule -DisplayName \"{prefix} - *\" -ErrorAction SilentlyContinue | Remove-NetFirewallRule -ErrorAction SilentlyContinue";
        var ok = PowerShellRunner.TryRun("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -Command \"{cmd}\"", out _, out var err2);

        lines.Add(new OperationLine
        {
            Level = ok ? "OK" : "ERR",
            Text = ok
                ? $"Firewall: rules removed via PowerShell filter: {prefix} - *"
                : $"Firewall: PowerShell fallback failed: {err2}"
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

            rule.Name = ruleName; // Display name in UI
            rule.Description = "OmenGamingHubUnlocker";
            rule.ApplicationName = exePath;

            rule.Action = 0;      // BLOCK
            rule.Direction = 2;   // OUT
            rule.Enabled = true;
            rule.InterfaceTypes = "All";
            rule.Profiles = int.MaxValue;

            dynamic policy = Activator.CreateInstance(policyType)!;
            dynamic rules = policy.Rules;
            rules.Add(rule);

            error = "";
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
        var ok = PowerShellRunner.TryRun("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -Command \"{cmd}\"", out _, out var err);
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
            // keep non-fatal
        }
    }

    private static List<string> TryGetAppxInstallLocationsViaPowerShell()
    {
        // Output lines:
        // Name|Family|InstallLocation|DisplayName
        const string ps = """
$ErrorActionPreference = 'SilentlyContinue'
Get-AppxPackage | ForEach-Object {
  $n = $_.Name
  $f = $_.PackageFamilyName
  $l = $_.InstallLocation
  $d = $_.DisplayName
  if ($null -ne $l -and $l -ne '') { "$n|$f|$l|$d" }
}
""";

        var ok = TryRunPowerShellEncoded(ps, out var stdout, out _);
        if (!ok || string.IsNullOrWhiteSpace(stdout))
            return new List<string>();

        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in stdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split('|');
            if (parts.Length < 3)
                continue;

            var name = parts[0].Trim();
            var family = parts[1].Trim();
            var loc = parts[2].Trim();
            var display = parts.Length >= 4 ? parts[3].Trim() : "";

            if (string.IsNullOrWhiteSpace(loc) || !Directory.Exists(loc))
                continue;

            var match = OmenTargets.AppxFilters.Any(p =>
                WildMatch(name, p) ||
                WildMatch(family, p) ||
                WildMatch(display, p));

            if (!match)
                continue;

            result.Add(loc);
        }

        return result.ToList();
    }

    private static bool TryRunPowerShellEncoded(string script, out string stdout, out string stderr)
    {
        var bytes = Encoding.Unicode.GetBytes(script);
        var b64 = Convert.ToBase64String(bytes);

        return PowerShellRunner.TryRun(
            "powershell.exe",
            $"-NoProfile -ExecutionPolicy Bypass -EncodedCommand {b64}",
            out stdout,
            out stderr);
    }

    private static bool WildMatch(string input, string pattern)
    {
        var regex = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".") + "$";

        return System.Text.RegularExpressions.Regex.IsMatch(input ?? "", regex, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }
}
