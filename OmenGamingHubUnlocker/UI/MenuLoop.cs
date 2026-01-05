using System.Reflection;
using System.Threading;
using OmenGamingHubUnlocker.App;
using OmenGamingHubUnlocker.Core;

namespace OmenGamingHubUnlocker.UI;

public sealed class MenuLoop(AppInfo appInfo, UnlockerEngine engine)
{
    private const string RepoUrl = "https://github.com/Avazbek22/OmenGamingHubUnlocker";
    private const string DefaultFirewallPrefix = "Tame-OMEN";

    private enum EngineActionKind
    {
        Status,
        DryRun,
        Activate,
        Disable
    }

    public void Run()
    {
        while (true)
        {
            Console.Clear();
            RenderMainMenuHeader();

            Console.WriteLine("[1] Check status");
            Console.WriteLine("[2] Dry run");
            Console.WriteLine("[3] Activate scripts");
            Console.WriteLine("[4] Disable scripts");
            Console.WriteLine("[5] About");
            Console.WriteLine("[6] Exit");
            Console.WriteLine();

            var choice = ConsoleHelpers.ReadMenuChoice();

            switch (choice)
            {
                case "1":
                    ShowStatus();
                    break;
                case "2":
                    ShowDryRun();
                    break;
                case "3":
                    RunActivate();
                    break;
                case "4":
                    RunDisable();
                    break;
                case "5":
                    ShowAbout();
                    break;
                case "6":
                    return;
                default:
                    ConsoleHelpers.WriteWarning("Invalid choice.");
                    ConsoleHelpers.Pause();
                    break;
            }
        }
    }

    // ============================================================
    //  HEADERS
    // ============================================================

    private void RenderMainMenuHeader()
    {
        ConsoleHelpers.WriteHeader(AppInfo.AppName);

        // OS/Runtime must be white, and only shown in main menu
        ConsoleHelpers.WithColor(ConsoleColor.White, () =>
        {
            Console.WriteLine($"OS: {appInfo.OsDisplayName}");
            Console.WriteLine($"Runtime: {appInfo.FrameworkDescription}");
        });

        if (appInfo.IsAdministrator)
            ConsoleHelpers.WithColor(ConsoleColor.Green, () => Console.WriteLine("Admin: Yes"));
        else
            ConsoleHelpers.WithColor(ConsoleColor.Yellow, () => Console.WriteLine("Admin: No (some actions will be blocked)"));

        Console.WriteLine();
    }

    private static void RenderScreenHeader(string title)
    {
        ConsoleHelpers.WriteMiniHeader(AppInfo.AppName);
        if (!string.IsNullOrWhiteSpace(title))
            ConsoleHelpers.WriteSection(title);
    }

    // ============================================================
    //  MENU ACTIONS
    // ============================================================

    private void ShowStatus()
    {
        Console.Clear();
        RenderScreenHeader("Status");

        var report = InvokeEngineReport(
            EngineActionKind.Status,
            "GetStatusReport",
            "GetStatus",
            "StatusReport",
            "Status",
            "CheckStatus"
        );

        if (report is null)
        {
            ConsoleHelpers.WriteError("Status: engine method not found.");
            ConsoleHelpers.Pause();
            return;
        }

        var hadTable = PrintReport(
            reportObj: report,
            intent: ConsoleTable.StatusIntent.Neutral,
            showResultColumn: false,
            predictive: false);

        if (!hadTable)
            PrintCurrentStatusSnapshot(ConsoleTable.StatusIntent.Neutral, showResultColumn: false, predictive: false);

        ConsoleHelpers.Pause();
    }

    private void ShowDryRun()
    {
        Console.Clear();
        RenderScreenHeader("Dry run");

        ConsoleHelpers.WriteBullets("What will be checked", new[]
        {
            "Detect HP/OMEN services and their startup type",
            "Detect HP/OMEN scheduled tasks and their state",
            "Detect HP/OMEN autostart entries (Run keys)",
            "Check firewall capability and existing rules",
            "Check hosts-file capability and current block entries",
            "Predict what would change if you activate scripts",
        });

        if (!ConsoleHelpers.ConfirmEnterOrEscape("Press Enter to start dry run or Esc to cancel..."))
            return;

        Console.Clear();
        RenderScreenHeader("Dry run");

        var report = InvokeEngineReport(
            EngineActionKind.DryRun,
            "DryRun",
            "RunDryRun",
            "Analyze",
            "Check",
            "Preview"
        );

        // Fallback: run Activate in dry-run mode if dedicated DryRun is not available
        report ??= InvokeEngineReport(
            EngineActionKind.DryRun,
            "Activate",
            "ActivateScripts",
            "Apply",
            "Run",
            "Enable"
        );

        if (report is null)
        {
            ConsoleHelpers.WriteError("Dry run: engine method not found.");
            ConsoleHelpers.Pause();
            return;
        }

        var hadTable = PrintReport(
            reportObj: report,
            // Dry run predicts activation effects ("Will ...")
            intent: ConsoleTable.StatusIntent.AfterActivate,
            showResultColumn: true,
            predictive: true);

        if (!hadTable)
            PrintCurrentStatusSnapshot(ConsoleTable.StatusIntent.AfterActivate, showResultColumn: true, predictive: true);

        ConsoleHelpers.Pause();
    }

    private void RunActivate()
    {
        Console.Clear();
        RenderScreenHeader("Activation");

        ConsoleHelpers.WriteBullets("What will happen", new[]
        {
            "Stop OMEN from starting automatically with Windows (services / tasks / Run entries)",
            "Optionally block OMEN executables from going online (Windows Firewall)",
            "Optionally block known OMEN endpoints via hosts file",
            "Show a detailed result summary at the end"
        });

        ConsoleHelpers.WriteHint("Tip: close OMEN Gaming Hub before running activation (recommended).");
        ConsoleHelpers.WriteHint("Nothing is uninstalled. This tool only changes startup / tasks / firewall / hosts settings.");

        if (!ConsoleHelpers.ConfirmEnterOrEscape("Press Enter to start activation or Esc to cancel..."))
            return;

        Console.Clear();
        RenderScreenHeader("Activation");

        var report = InvokeEngineReport(
            EngineActionKind.Activate,
            "Activate",
            "ActivateScripts",
            "Apply",
            "RunActivate",
            "Enable",
            "Run"
        );

        if (report is null)
        {
            ConsoleHelpers.WriteError("Activation: engine method not found.");
            ConsoleHelpers.Pause();
            return;
        }

        var hadTable = PrintReport(
            reportObj: report,
            intent: ConsoleTable.StatusIntent.AfterActivate,
            showResultColumn: true,
            predictive: false);

        if (!hadTable)
            PrintCurrentStatusSnapshot(ConsoleTable.StatusIntent.AfterActivate, showResultColumn: true, predictive: false);

        ConsoleHelpers.Pause();
    }

    private void RunDisable()
    {
        Console.Clear();
        RenderScreenHeader("Disable");

        ConsoleHelpers.WriteBullets("What will happen", new[]
        {
            "Remove firewall block rules created by this tool (if any)",
            "Remove hosts entries created by this tool (if any)",
            "Try to re-enable HP/OMEN scheduled tasks and services (best-effort)",
            "Show a detailed result summary at the end"
        });

        ConsoleHelpers.WriteHint("Note: removed Run autostart entries cannot be restored without backups (by design).");

        if (!ConsoleHelpers.ConfirmEnterOrEscape("Press Enter to start disable or Esc to cancel..."))
            return;

        Console.Clear();
        RenderScreenHeader("Disable");

        var report = InvokeEngineReport(
            EngineActionKind.Disable,
            "Disable",
            "DisableScripts",
            "Deactivate",
            "Rollback",
            "Revert",
            "RunDisable"
        );

        if (report is null)
        {
            ConsoleHelpers.WriteError("Disable: engine method not found.");
            ConsoleHelpers.Pause();
            return;
        }

        var hadTable = PrintReport(
            reportObj: report,
            intent: ConsoleTable.StatusIntent.AfterDisable,
            showResultColumn: true,
            predictive: false);

        if (!hadTable)
            PrintCurrentStatusSnapshot(ConsoleTable.StatusIntent.AfterDisable, showResultColumn: true, predictive: false);

        ConsoleHelpers.Pause();
    }

    private void ShowAbout()
    {
        Console.Clear();
        RenderScreenHeader("About");

        ConsoleHelpers.PrintLinesAnimated(new[]
        {
            "OmenGamingHubUnlocker is a small helper tool for HP OMEN laptops/desktops.",
            "",
            "Purpose:",
            " - Keep OMEN Gaming Hub installed, but prevent it from auto-starting and running unwanted background activity.",
            "",
            "What it can do:",
            " - Tame auto-start behavior (services / scheduled tasks / Run entries)",
            " - Block OMEN executables outbound traffic via Windows Firewall (optional)",
            " - Block known OMEN endpoints via hosts file (optional)",
            "",
            "Usage idea:",
            " - Boot Windows",
            " - Turn on VPN (if needed)",
            " - Launch OMEN Gaming Hub manually",
            "",
            "Author:",
            " - Avazbek22",
            "",
            "GitHub:",
            $" - {RepoUrl}",
            "",
            "License:",
            " - MIT"
        });

        ConsoleHelpers.Pause();
    }

    // ============================================================
    //  REPORT PRINTING
    // ============================================================

    private bool PrintReport(
        object reportObj,
        ConsoleTable.StatusIntent intent,
        bool showResultColumn,
        bool predictive)
    {
        var (success, title) = ReadReportHeader(reportObj);

        if (success) ConsoleHelpers.WriteSuccess(title);
        else ConsoleHelpers.WriteError(title);

        Console.WriteLine(new string('-', Math.Max(10, title.Length)));

        var opLines = ReadOperationLines(reportObj);
        if (opLines is not null && opLines.Count > 0)
        {
            Console.WriteLine();
            ConsoleHelpers.PrintOperationLinesAnimated(opLines);
        }

        var plainLines = ReadPlainLines(reportObj);
        if (plainLines is not null && plainLines.Count > 0)
        {
            Console.WriteLine();
            ConsoleHelpers.PrintLinesAnimated(plainLines);
        }

        return TryPrintSnapshotsFromReport(reportObj, intent, showResultColumn, predictive);
    }

    private bool TryPrintSnapshotsFromReport(
        object reportObj,
        ConsoleTable.StatusIntent intent,
        bool showResultColumn,
        bool predictive)
    {
        try
        {
            var t = reportObj.GetType();

            var p = t.GetProperty("Snapshots", BindingFlags.Public | BindingFlags.Instance)
                    ?? t.GetProperty("Status", BindingFlags.Public | BindingFlags.Instance)
                    ?? t.GetProperty("Items", BindingFlags.Public | BindingFlags.Instance);

            var snaps = p?.GetValue(reportObj);
            if (snaps is null)
                return false;

            ConsoleHelpers.WriteSection("Snapshot");
            return ConsoleTable.PrintStatusTable(
                snapshots: snaps,
                intent: intent,
                showResultColumn: showResultColumn,
                predictive: predictive);
        }
        catch
        {
            return false;
        }
    }

    private void PrintCurrentStatusSnapshot(
        ConsoleTable.StatusIntent intent,
        bool showResultColumn,
        bool predictive)
    {
        var statusReport = InvokeEngineReport(
            EngineActionKind.Status,
            "GetStatusReport",
            "GetStatus",
            "StatusReport",
            "Status",
            "CheckStatus"
        );

        if (statusReport is null)
            return;

        try
        {
            var t = statusReport.GetType();

            var p = t.GetProperty("Snapshots", BindingFlags.Public | BindingFlags.Instance)
                    ?? t.GetProperty("Status", BindingFlags.Public | BindingFlags.Instance)
                    ?? t.GetProperty("Items", BindingFlags.Public | BindingFlags.Instance);

            var snaps = p?.GetValue(statusReport);
            if (snaps is null)
                return;

            ConsoleHelpers.WriteSection("Current status snapshot");
            ConsoleTable.PrintStatusTable(
                snapshots: snaps,
                intent: intent,
                showResultColumn: showResultColumn,
                predictive: predictive);
        }
        catch
        {
            // ignore
        }
    }

    private static (bool success, string title) ReadReportHeader(object reportObj)
    {
        var t = reportObj.GetType();

        var titleProp = t.GetProperty("Title", BindingFlags.Public | BindingFlags.Instance);
        var successProp = t.GetProperty("Success", BindingFlags.Public | BindingFlags.Instance);

        var title = titleProp?.GetValue(reportObj) as string ?? "Done";
        var success = successProp?.GetValue(reportObj) as bool? ?? true;

        return (success, title);
    }

    private static List<OperationLine>? ReadOperationLines(object reportObj)
    {
        var t = reportObj.GetType();

        var p = t.GetProperty("Lines", BindingFlags.Public | BindingFlags.Instance)
                ?? t.GetProperty("OperationLines", BindingFlags.Public | BindingFlags.Instance);

        var value = p?.GetValue(reportObj);
        if (value is null) return null;

        return value is IEnumerable<OperationLine> typed ? typed.ToList() : null;
    }

    private static List<string>? ReadPlainLines(object reportObj)
    {
        var t = reportObj.GetType();

        var p = t.GetProperty("TextLines", BindingFlags.Public | BindingFlags.Instance)
                ?? t.GetProperty("Messages", BindingFlags.Public | BindingFlags.Instance);

        var value = p?.GetValue(reportObj);
        if (value is null) return null;

        return value is IEnumerable<string> lines ? lines.ToList() : null;
    }

    // ============================================================
    //  SMART ENGINE INVOKER
    // ============================================================

    private object? InvokeEngineReport(EngineActionKind kind, params string[] methodNames)
    {
        foreach (var name in methodNames)
        {
            var methods = engine.GetType()
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => m.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                .OrderBy(m => m.GetParameters().Length)
                .ToList();

            foreach (var mi in methods)
            {
                if (!TryBuildArgs(kind, mi, out var args))
                    continue;

                try
                {
                    var res = mi.Invoke(engine, args);
                    if (res is not null)
                        return res;
                }
                catch (TargetInvocationException tie)
                {
                    ConsoleHelpers.WriteError($"Engine error: {tie.InnerException?.Message ?? tie.Message}");
                    return null;
                }
                catch (Exception ex)
                {
                    ConsoleHelpers.WriteError($"Engine error: {ex.Message}");
                    return null;
                }
            }
        }

        return null;
    }

    private bool TryBuildArgs(EngineActionKind kind, MethodInfo mi, out object?[] args)
    {
        var ps = mi.GetParameters();
        args = new object?[ps.Length];

        for (var i = 0; i < ps.Length; i++)
        {
            var p = ps[i];
            var pt = p.ParameterType;

            if (pt == typeof(UnlockerOptions))
            {
                args[i] = CreateDefaultOptions(kind);
                continue;
            }

            if (pt == typeof(bool))
            {
                var isDry = p.Name?.Contains("dry", StringComparison.OrdinalIgnoreCase) == true;
                args[i] = isDry ? (kind == EngineActionKind.DryRun) : false;
                continue;
            }

            if (pt == typeof(string))
            {
                if (p.Name?.Contains("prefix", StringComparison.OrdinalIgnoreCase) == true)
                    args[i] = DefaultFirewallPrefix;
                else
                    args[i] = string.Empty;

                continue;
            }

            if (pt == typeof(CancellationToken))
            {
                args[i] = CancellationToken.None;
                continue;
            }

            if (pt.IsValueType)
            {
                args[i] = Activator.CreateInstance(pt);
                continue;
            }

            args[i] = null;
        }

        return true;
    }

    private static UnlockerOptions CreateDefaultOptions(EngineActionKind kind)
    {
        var opt = new UnlockerOptions();

        TrySet(opt, "DryRun", kind == EngineActionKind.DryRun);
        TrySet(opt, "ManageFirewall", true);
        TrySet(opt, "ManageHosts", true);
        TrySet(opt, "FirewallRulePrefix", DefaultFirewallPrefix);

        return opt;
    }

    private static void TrySet<T>(object target, string propName, T value)
    {
        try
        {
            var p = target.GetType().GetProperty(propName, BindingFlags.Public | BindingFlags.Instance);
            if (p is null || !p.CanWrite) return;

            if (p.PropertyType.IsAssignableFrom(typeof(T)))
            {
                p.SetValue(target, value);
                return;
            }

            var converted = Convert.ChangeType(value, p.PropertyType);
            p.SetValue(target, converted);
        }
        catch
        {
            // ignore
        }
    }
}
