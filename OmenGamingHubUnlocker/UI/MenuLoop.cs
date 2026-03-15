namespace OmenGamingHubUnlocker.UI;

/// <summary>
/// Owns the interactive console workflow and delegates the real work to the engine.
/// </summary>
public sealed class MenuLoop(AppInfo appInfo, UnlockerEngine engine)
{
    public void Run()
    {
        while (true)
        {
            ConsoleHelpers.TryClearScreen();
            RenderMainMenuHeader();

            Console.WriteLine("[1] Check status");
            Console.WriteLine("[2] Dry run");
            Console.WriteLine("[3] Activate scripts");
            Console.WriteLine("[4] Disable scripts");
            Console.WriteLine("[5] Reset Omen Gaming Hub & Activate scripts");
            Console.WriteLine("[6] Help");
            Console.WriteLine("[7] About");
            Console.WriteLine("[8] Exit");
            Console.WriteLine();

            var selectedAction = ConsoleHelpers.ReadMenuChoice();

            switch (selectedAction)
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
                    ShowHelp();
                    break;
                case "7":
                    ShowAbout();
                    break;
                case "8":
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
        ConsoleHelpers.WriteHeader(AppInfo.AppDisplayName);

        ConsoleHelpers.WithColor(ConsoleColor.White, () =>
        {
            Console.WriteLine($"OS: {appInfo.OsDisplayName}");
            Console.WriteLine($"Runtime: {appInfo.FrameworkDescription}");
        });

        if (appInfo.IsAdministrator)
        {
            ConsoleHelpers.WithColor(ConsoleColor.Green, () => Console.WriteLine("Admin: Yes"));
        }
        else
        {
            ConsoleHelpers.WithColor(ConsoleColor.Yellow, () => Console.WriteLine("Admin: No (some actions will be blocked)"));
        }

        Console.WriteLine();
    }

    private static void RenderScreenHeader(string title)
    {
        ConsoleHelpers.WriteMiniHeader(AppInfo.AppDisplayName);

        if (!string.IsNullOrWhiteSpace(title))
            ConsoleHelpers.WriteSection(title);
    }

    private void ShowStatus()
    {
        ConsoleHelpers.TryClearScreen();
        RenderScreenHeader("Status");

        var statusReport = engine.GetStatusReport();
        PrintStatusReport(statusReport, ConsoleTable.StatusIntent.Neutral, showResultColumn: false, predictive: false);
        ConsoleHelpers.Pause();
    }

    private void ShowDryRun()
    {
        ConsoleHelpers.TryClearScreen();
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

        ConsoleHelpers.TryClearScreen();
        RenderScreenHeader("Dry run");

        var operationReport = engine.RunDryRunDeep();
        PrintOperationReport(operationReport, ConsoleTable.StatusIntent.AfterActivate, showResultColumn: true, predictive: true);
        ConsoleHelpers.Pause();
    }

    private void RunActivate()
    {
        ConsoleHelpers.TryClearScreen();
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

        ConsoleHelpers.TryClearScreen();
        RenderScreenHeader("Activation");

        var operationReport = engine.Activate(UnlockerOptions.ForActivate());
        PrintOperationReport(operationReport, ConsoleTable.StatusIntent.AfterActivate, showResultColumn: true, predictive: false);
        ConsoleHelpers.Pause();
    }

    private void RunDisable()
    {
        ConsoleHelpers.TryClearScreen();
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

        ConsoleHelpers.TryClearScreen();
        RenderScreenHeader("Disable");

        var operationReport = engine.Disable(UnlockerOptions.ForDisable());
        PrintOperationReport(operationReport, ConsoleTable.StatusIntent.AfterDisable, showResultColumn: true, predictive: false);
        ConsoleHelpers.Pause();
    }

    private void RunResetAndReapply()
    {
        ConsoleHelpers.TryClearScreen();
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

        ConsoleHelpers.TryClearScreen();
        RenderScreenHeader("Reset OMEN app");

        var operationReport = engine.ResetAndReapply(UnlockerOptions.ForResetAndReapply());
        PrintOperationReport(operationReport, ConsoleTable.StatusIntent.AfterActivate, showResultColumn: true, predictive: false);
        ConsoleHelpers.Pause();
    }

    private void ShowAbout()
        => ShowDocumentationScreen("About", DocumentationDocument.About);

    private void ShowHelp()
        => ShowDocumentationScreen("Help", DocumentationDocument.Help);

    private static void ShowDocumentationScreen(string title, DocumentationDocument document)
    {
        ConsoleHelpers.TryClearScreen();
        ConsoleHelpers.WriteMiniHeader(AppInfo.AppName);

        if (!string.IsNullOrWhiteSpace(title))
            ConsoleHelpers.WriteSection(title);

        ConsoleHelpers.PrintLinesAnimated(DocumentationProvider.GetLines(document));

        ConsoleHelpers.Pause();
    }

    private static void PrintStatusReport(
        StatusReport statusReport,
        ConsoleTable.StatusIntent tableIntent,
        bool showResultColumn,
        bool predictive)
    {
        ConsoleHelpers.WriteSuccess("Status collected.");
        Console.WriteLine(new string('-', 16));

        ConsoleHelpers.WriteSection("Snapshot");
        ConsoleTable.PrintStatusTable(statusReport.Snapshots, tableIntent, showResultColumn, predictive);
    }

    private static void PrintOperationReport(
        OperationReport operationReport,
        ConsoleTable.StatusIntent tableIntent,
        bool showResultColumn,
        bool predictive)
    {
        if (operationReport.Success)
        {
            ConsoleHelpers.WriteSuccess(operationReport.Title);
        }
        else
        {
            ConsoleHelpers.WriteError(operationReport.Title);
        }

        Console.WriteLine(new string('-', Math.Max(10, operationReport.Title.Length)));

        if (operationReport.Lines.Count > 0)
        {
            Console.WriteLine();
            ConsoleHelpers.PrintOperationLinesAnimated(operationReport.Lines);
        }

        ConsoleHelpers.WriteSection("Snapshot");
        ConsoleTable.PrintStatusTable(operationReport.SnapshotsAfter, tableIntent, showResultColumn, predictive);
    }
}
