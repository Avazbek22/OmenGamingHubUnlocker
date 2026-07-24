namespace OmenGamingHubUnlocker.Tests.UI;

public sealed class ConsoleTableTests
{
    private static readonly string[] PrimitiveRows = ["plain row"];

    public ConsoleTableTests()
    {
        Text.Initialize(CreateLocalization(AppLanguage.English));
    }

    [Fact]
    public void PrintStatusTable_ShouldReturnFalse_WhenThereAreNoRows()
    {
        using var capture = new ConsoleOutputCapture();

        var printed = ConsoleTable.PrintStatusTable(Array.Empty<StatusSnapshot>());

        Assert.False(printed);
        Assert.Contains("No status table data.", capture.GetOutput());
    }

    [Fact]
    public void PrintStatusTable_ShouldRenderHeadersWithoutResultColumn_WhenRequested()
    {
        using var capture = new ConsoleOutputCapture();

        var printed = ConsoleTable.PrintStatusTable(
            new[] { new StatusSnapshot { Area = "Services", Item = "Svc", Current = "Manual" } },
            showResultColumn: false);

        Assert.True(printed);
        var output = capture.GetOutput();
        Assert.Contains("Area", output);
        Assert.Contains("Item", output);
        Assert.Contains("Current", output);
        Assert.DoesNotContain("Result", output);
    }

    [Fact]
    public void PrintStatusTable_ShouldRenderPredictiveResult_ForEnabledTask()
    {
        using var capture = new ConsoleOutputCapture();

        ConsoleTable.PrintStatusTable(
            new[] { new StatusSnapshot { Area = "Tasks", Item = "Task1", Current = "Enabled" } },
            intent: ConsoleTable.StatusIntent.AfterActivate,
            showResultColumn: true,
            predictive: true);

        Assert.Contains("Will disable", capture.GetOutput());
    }

    [Fact]
    public void PrintStatusTable_ShouldPredictProcessTermination()
    {
        using var capture = new ConsoleOutputCapture();

        ConsoleTable.PrintStatusTable(
            new[] { new StatusSnapshot { Area = "Processes", Item = "Omen", Current = "Running" } },
            intent: ConsoleTable.StatusIntent.AfterActivate,
            showResultColumn: true,
            predictive: true);

        var output = capture.GetOutput();
        Assert.Contains("Will terminate", output);
        Assert.DoesNotContain("Will check", output);
    }

    [Fact]
    public void PrintStatusTable_ShouldPredictCombinedServiceChanges()
    {
        using var capture = new ConsoleOutputCapture();

        ConsoleTable.PrintStatusTable(
            new[] { new StatusSnapshot { Area = "Services", Item = "HPOmenCap", Current = "Auto, Running" } },
            intent: ConsoleTable.StatusIntent.AfterActivate,
            showResultColumn: true,
            predictive: true);

        Assert.Contains("Will set startup to Manual and stop", capture.GetOutput());
    }

    [Fact]
    public void PrintStatusTable_ShouldPredictCombinedTaskChanges()
    {
        using var capture = new ConsoleOutputCapture();

        ConsoleTable.PrintStatusTable(
            new[] { new StatusSnapshot { Area = "Tasks", Item = "OmenTask", Current = "Enabled, Running" } },
            intent: ConsoleTable.StatusIntent.AfterActivate,
            showResultColumn: true,
            predictive: true);

        Assert.Contains("Will disable and stop", capture.GetOutput());
    }

    [Fact]
    public void PrintStatusTable_ShouldReportWhenNoChangeIsNeeded()
    {
        using var capture = new ConsoleOutputCapture();

        ConsoleTable.PrintStatusTable(
            new[] { new StatusSnapshot { Area = "Services", Item = "HPOmenCap", Current = "Manual, Stopped" } },
            intent: ConsoleTable.StatusIntent.AfterActivate,
            showResultColumn: true,
            predictive: true);

        Assert.Contains("No changes needed", capture.GetOutput());
    }

    [Fact]
    public void PrintStatusTable_ShouldRenderNaturalRussianCompositeStates()
    {
        Text.Initialize(CreateLocalization(AppLanguage.Russian));
        try
        {
            using var capture = new ConsoleOutputCapture();

            ConsoleTable.PrintStatusTable(
                new[] { new StatusSnapshot { Area = "Services", Item = "HPOmenCap", Current = "Auto, Running" } },
                intent: ConsoleTable.StatusIntent.AfterActivate,
                showResultColumn: true,
                predictive: true);

            var output = capture.GetOutput();
            Assert.Contains("Автоматически, Запущена", output);
            Assert.Contains("Тип запуска: вручную; будет остановлена", output);
        }
        finally
        {
            Text.Initialize(CreateLocalization(AppLanguage.English));
        }
    }

    [Fact]
    public void PrintStatusTable_ShouldUseGrammaticallyMatchingRussianForms()
    {
        Text.Initialize(CreateLocalization(AppLanguage.Russian));
        try
        {
            using var capture = new ConsoleOutputCapture();

            ConsoleTable.PrintStatusTable(
                new[]
                {
                    new StatusSnapshot { Area = "Processes", Item = "Omen", Current = "Running" },
                    new StatusSnapshot { Area = "Tasks", Item = "OmenTask", Current = "Enabled, Running" },
                    new StatusSnapshot { Area = "hosts", Item = "hpbp.io", Current = "Not blocked" }
                },
                intent: ConsoleTable.StatusIntent.AfterActivate,
                showResultColumn: true,
                predictive: true);

            var output = capture.GetOutput();
            Assert.Contains("Будет завершен", output);
            Assert.Contains("Включена, Выполняется", output);
            Assert.Contains("Будет отключена и остановлена", output);
            Assert.Contains("Не заблокирован", output);
            Assert.Contains("Будет заблокирован", output);
        }
        finally
        {
            Text.Initialize(CreateLocalization(AppLanguage.English));
        }
    }

    [Fact]
    public void PrintStatusTable_ShouldEvaluateDisabledTaskAsOk_AfterActivate()
    {
        using var capture = new ConsoleOutputCapture();

        ConsoleTable.PrintStatusTable(
            new[] { new StatusSnapshot { Area = "Tasks", Item = "Task1", Current = "Disabled" } },
            intent: ConsoleTable.StatusIntent.AfterActivate,
            showResultColumn: true,
            predictive: false);

        Assert.Contains("OK", capture.GetOutput());
    }

    [Fact]
    public void PrintStatusTable_ShouldTrustExplicitResult_InsteadOfGuessingFromIntent()
    {
        using var capture = new ConsoleOutputCapture();

        ConsoleTable.PrintStatusTable(
            new[] { new StatusSnapshot { Area = "Services", Item = "Svc", Current = "Automatic, Running", Result = "OK" } },
            intent: ConsoleTable.StatusIntent.AfterDisable,
            showResultColumn: true,
            predictive: false);

        Assert.Contains("OK", capture.GetOutput());
    }

    [Fact]
    public void PrintStatusTable_ShouldRenderExplicitError()
    {
        using var capture = new ConsoleOutputCapture();

        ConsoleTable.PrintStatusTable(
            new[] { new StatusSnapshot { Area = "Firewall", Item = "Rules", Current = "1", Result = "ERR" } },
            intent: ConsoleTable.StatusIntent.AfterDisable,
            showResultColumn: true,
            predictive: false);

        Assert.Contains("ERR", capture.GetOutput());
    }

    [Fact]
    public void PrintStatusTable_ShouldFallbackToToString_ForPrimitiveRows()
    {
        using var capture = new ConsoleOutputCapture();

        var printed = ConsoleTable.PrintStatusTable(PrimitiveRows);

        Assert.True(printed);
        Assert.Contains("plain row", capture.GetOutput());
    }

    private static LocalizationService CreateLocalization(AppLanguage language)
        => new(new TestLanguagePreferenceStore(), language);

    private sealed class TestLanguagePreferenceStore : ILanguagePreferenceStore
    {
        public AppLanguage? Load() => null;
        public void Save(AppLanguage language) { }
    }
}
