using OmenGamingHubUnlocker.App;
using OmenGamingHubUnlocker.Core;

namespace OmenGamingHubUnlocker.UI;

public sealed class MenuLoop(AppInfo appInfo, UnlockerEngine engine)
{
    private const string RepoUrl = "https://github.com/Avazbek22/OmenGamingHubUnlocker";

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
            Console.WriteLine("[5] Reset OMEN app and reapply taming");
            Console.WriteLine("[6] About");
            Console.WriteLine("[7] Exit");
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
                    RunResetAndReapply();
                    break;
                case "6":
                    ShowAbout();
                    break;
                case "7":
                    return;
                default:
                    ConsoleHelpers.WriteWarning("Invalid choice.");
                    ConsoleHelpers.Pause();
                    break;
            }
        }
    }

    private void RenderMainMenuHeader()
    {
        ConsoleHelpers.WriteHeader(AppInfo.AppName);

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

    private void ShowStatus()
    {
        Console.Clear();
        RenderScreenHeader("Status");

        var report = engine.GetStatusReport();
        PrintStatusReport(report, ConsoleTable.StatusIntent.Neutral, showResultColumn: false, predictive: false);
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
            "Check AppX reset capability and OMEN package discovery",
            "Predict what would change if you activate scripts"
        });

        if (!ConsoleHelpers.ConfirmEnterOrEscape("Press Enter to start dry run or Esc to cancel..."))
            return;

        Console.Clear();
        RenderScreenHeader("Dry run");

        var report = engine.RunDryRunDeep();
        PrintOperationReport(report, ConsoleTable.StatusIntent.AfterActivate, showResultColumn: true, predictive: true);
        ConsoleHelpers.Pause();
    }

    private void RunActivate()
    {
        Console.Clear();
        RenderScreenHeader("Activation");

        ConsoleHelpers.WriteBullets("What will happen", new[]
        {
            "Stop OMEN from starting automatically with Windows (services / tasks / Run entries)",
            "Block OMEN executables from going online (Windows Firewall)",
            "Block known OMEN endpoints via hosts file",
            "Save rollback state before changing services / tasks / Run entries"
        });

        ConsoleHelpers.WriteHint("Tip: close OMEN Gaming Hub before running activation (recommended).");
        ConsoleHelpers.WriteHint("Nothing is uninstalled. This tool only changes startup, firewall and hosts settings.");

        if (!ConsoleHelpers.ConfirmEnterOrEscape("Press Enter to start activation or Esc to cancel..."))
            return;

        Console.Clear();
        RenderScreenHeader("Activation");

        var report = engine.Activate(UnlockerOptions.ForActivate());
        PrintOperationReport(report, ConsoleTable.StatusIntent.AfterActivate, showResultColumn: true, predictive: false);
        ConsoleHelpers.Pause();
    }

    private void RunDisable()
    {
        Console.Clear();
        RenderScreenHeader("Disable");

        ConsoleHelpers.WriteBullets("What will happen", new[]
        {
            "Remove firewall block rules created by this tool",
            "Remove hosts entries created by this tool",
            "Restore services, tasks and Run entries from saved backup state",
            "Clear saved rollback state after a successful restore"
        });

        if (!ConsoleHelpers.ConfirmEnterOrEscape("Press Enter to start disable or Esc to cancel..."))
            return;

        Console.Clear();
        RenderScreenHeader("Disable");

        var report = engine.Disable(UnlockerOptions.ForDisable());
        PrintOperationReport(report, ConsoleTable.StatusIntent.AfterDisable, showResultColumn: true, predictive: false);
        ConsoleHelpers.Pause();
    }

    private void RunResetAndReapply()
    {
        Console.Clear();
        RenderScreenHeader("Reset OMEN app");

        ConsoleHelpers.WriteBullets("What will happen", new[]
        {
            "Terminate OMEN-related processes before reset",
            "Run the Windows AppX reset for the installed OMEN package",
            "Immediately refresh firewall and hosts blocks after reset",
            "Re-apply service, task and Run-entry taming so the updated app stays constrained"
        });

        ConsoleHelpers.WriteWarning("This is equivalent to the Windows app reset for OMEN and will clear the app's stored data.");
        ConsoleHelpers.WriteHint("Use this when OMEN updated itself, broke your current bypass, or got stuck after an update.");

        if (!ConsoleHelpers.ConfirmEnterOrEscape("Press Enter to start reset and reapply or Esc to cancel..."))
            return;

        Console.Clear();
        RenderScreenHeader("Reset OMEN app");

        var report = engine.ResetAndReapply(UnlockerOptions.ForResetAndReapply());
        PrintOperationReport(report, ConsoleTable.StatusIntent.AfterActivate, showResultColumn: true, predictive: false);
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
            " - Block OMEN executables outbound traffic via Windows Firewall",
            " - Block known OMEN endpoints via hosts file",
            " - Reset the OMEN AppX package and immediately re-apply taming",
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

    private static void PrintStatusReport(
        StatusReport report,
        ConsoleTable.StatusIntent intent,
        bool showResultColumn,
        bool predictive)
    {
        ConsoleHelpers.WriteSuccess("Status collected.");
        Console.WriteLine(new string('-', 16));

        ConsoleHelpers.WriteSection("Snapshot");
        ConsoleTable.PrintStatusTable(report.Snapshots, intent, showResultColumn, predictive);
    }

    private static void PrintOperationReport(
        OperationReport report,
        ConsoleTable.StatusIntent intent,
        bool showResultColumn,
        bool predictive)
    {
        if (report.Success)
            ConsoleHelpers.WriteSuccess(report.Title);
        else
            ConsoleHelpers.WriteError(report.Title);

        Console.WriteLine(new string('-', Math.Max(10, report.Title.Length)));

        if (report.Lines.Count > 0)
        {
            Console.WriteLine();
            ConsoleHelpers.PrintOperationLinesAnimated(report.Lines);
        }

        ConsoleHelpers.WriteSection("Snapshot");
        ConsoleTable.PrintStatusTable(report.SnapshotsAfter, intent, showResultColumn, predictive);
    }
}
