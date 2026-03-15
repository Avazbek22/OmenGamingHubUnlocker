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

            Console.WriteLine($"[1] {Text.Get("menu.checkStatus")}");
            Console.WriteLine($"[2] {Text.Get("menu.dryRun")}");
            Console.WriteLine($"[3] {Text.Get("menu.activateScripts")}");
            Console.WriteLine($"[4] {Text.Get("menu.disableScripts")}");
            Console.WriteLine($"[5] {Text.Get("menu.resetAndActivate")}");
            Console.WriteLine($"[6] {Text.Get("menu.help")}");
            Console.WriteLine($"[7] {Text.Get("menu.about")}");
            Console.WriteLine($"[8] {Text.Get("menu.changeLanguage")}");
            Console.WriteLine($"[9] {Text.Get("menu.exit")}");
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
                    Text.ToggleLanguage();
                    break;
                case "9":
                    return;
                default:
                    ConsoleHelpers.WriteWarning(Text.Get("common.invalidChoice"));
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
            Console.WriteLine($"{Text.Get("header.os")}: {appInfo.OsDisplayName}");
            Console.WriteLine($"{Text.Get("header.runtime")}: {appInfo.FrameworkDescription}");
        });

        if (appInfo.IsAdministrator)
        {
            ConsoleHelpers.WithColor(ConsoleColor.Green, () => Console.WriteLine(Text.Get("header.adminYes")));
        }
        else
        {
            ConsoleHelpers.WithColor(ConsoleColor.Yellow, () => Console.WriteLine(Text.Get("header.adminNo")));
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
        RenderScreenHeader(Text.Get("screen.status"));

        var statusReport = engine.GetStatusReport();
        PrintStatusReport(statusReport, ConsoleTable.StatusIntent.Neutral, showResultColumn: false, predictive: false);
        ConsoleHelpers.Pause();
    }

    private void ShowDryRun()
    {
        ConsoleHelpers.TryClearScreen();
        RenderScreenHeader(Text.Get("screen.dryRun"));

        ConsoleHelpers.WriteBullets(Text.Get("ui.dryRun.whatWillBeChecked"), new[]
        {
            Text.Get("ui.dryRun.check1"),
            Text.Get("ui.dryRun.check2"),
            Text.Get("ui.dryRun.check3"),
            Text.Get("ui.dryRun.check4"),
            Text.Get("ui.dryRun.check5"),
            Text.Get("ui.dryRun.check6"),
            Text.Get("ui.dryRun.check7")
        });

        if (!ConsoleHelpers.ConfirmEnterOrEscape(Text.Get("ui.dryRun.confirm")))
            return;

        ConsoleHelpers.TryClearScreen();
        RenderScreenHeader(Text.Get("screen.dryRun"));

        var operationReport = engine.RunDryRunDeep();
        PrintOperationReport(operationReport, ConsoleTable.StatusIntent.AfterActivate, showResultColumn: true, predictive: true);
        ConsoleHelpers.Pause();
    }

    private void RunActivate()
    {
        ConsoleHelpers.TryClearScreen();
        RenderScreenHeader(Text.Get("screen.activation"));

        ConsoleHelpers.WriteBullets(Text.Get("ui.activate.whatWillHappen"), new[]
        {
            Text.Get("ui.activate.step1"),
            Text.Get("ui.activate.step2"),
            Text.Get("ui.activate.step3"),
            Text.Get("ui.activate.step4")
        });

        ConsoleHelpers.WriteHint(Text.Get("ui.activate.tipCloseOmen"));
        ConsoleHelpers.WriteHint(Text.Get("ui.activate.tipNoUninstall"));

        if (!ConsoleHelpers.ConfirmEnterOrEscape(Text.Get("ui.activate.confirm")))
            return;

        ConsoleHelpers.TryClearScreen();
        RenderScreenHeader(Text.Get("screen.activation"));

        var operationReport = engine.Activate(UnlockerOptions.ForActivate());
        PrintOperationReport(operationReport, ConsoleTable.StatusIntent.AfterActivate, showResultColumn: true, predictive: false);
        ConsoleHelpers.Pause();
    }

    private void RunDisable()
    {
        ConsoleHelpers.TryClearScreen();
        RenderScreenHeader(Text.Get("screen.disable"));

        ConsoleHelpers.WriteBullets(Text.Get("ui.disable.whatWillHappen"), new[]
        {
            Text.Get("ui.disable.step1"),
            Text.Get("ui.disable.step2"),
            Text.Get("ui.disable.step3"),
            Text.Get("ui.disable.step4")
        });

        if (!ConsoleHelpers.ConfirmEnterOrEscape(Text.Get("ui.disable.confirm")))
            return;

        ConsoleHelpers.TryClearScreen();
        RenderScreenHeader(Text.Get("screen.disable"));

        var operationReport = engine.Disable(UnlockerOptions.ForDisable());
        PrintOperationReport(operationReport, ConsoleTable.StatusIntent.AfterDisable, showResultColumn: true, predictive: false);
        ConsoleHelpers.Pause();
    }

    private void RunResetAndReapply()
    {
        ConsoleHelpers.TryClearScreen();
        RenderScreenHeader(Text.Get("screen.resetOmenApp"));

        ConsoleHelpers.WriteBullets(Text.Get("ui.reset.whatWillHappen"), new[]
        {
            Text.Get("ui.reset.step1"),
            Text.Get("ui.reset.step2"),
            Text.Get("ui.reset.step3"),
            Text.Get("ui.reset.step4")
        });

        ConsoleHelpers.WriteWarning(Text.Get("ui.reset.warning"));
        ConsoleHelpers.WriteHint(Text.Get("ui.reset.hint"));

        if (!ConsoleHelpers.ConfirmEnterOrEscape(Text.Get("ui.reset.confirm")))
            return;

        ConsoleHelpers.TryClearScreen();
        RenderScreenHeader(Text.Get("screen.resetOmenApp"));

        var operationReport = engine.ResetAndReapply(UnlockerOptions.ForResetAndReapply());
        PrintOperationReport(operationReport, ConsoleTable.StatusIntent.AfterActivate, showResultColumn: true, predictive: false);
        ConsoleHelpers.Pause();
    }

    private void ShowAbout()
        => ShowDocumentationScreen(Text.Get("screen.about"), DocumentationDocument.About);

    private void ShowHelp()
        => ShowDocumentationScreen(Text.Get("screen.help"), DocumentationDocument.Help);

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
        ConsoleHelpers.WriteSuccess(Text.Get("common.statusCollected"));
        Console.WriteLine(new string('-', 16));

        ConsoleHelpers.WriteSection(Text.Get("common.snapshot"));
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

        ConsoleHelpers.WriteSection(Text.Get("common.snapshot"));
        ConsoleTable.PrintStatusTable(operationReport.SnapshotsAfter, tableIntent, showResultColumn, predictive);
    }
}
