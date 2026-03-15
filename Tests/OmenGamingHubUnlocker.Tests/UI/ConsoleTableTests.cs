namespace OmenGamingHubUnlocker.Tests.UI;

public sealed class ConsoleTableTests
{
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
    public void PrintStatusTable_ShouldEvaluateManualServiceAsWarn_AfterDisable()
    {
        using var capture = new ConsoleOutputCapture();

        ConsoleTable.PrintStatusTable(
            new[] { new StatusSnapshot { Area = "Services", Item = "Svc", Current = "Manual" } },
            intent: ConsoleTable.StatusIntent.AfterDisable,
            showResultColumn: true,
            predictive: false);

        Assert.Contains("WARN", capture.GetOutput());
    }

    [Fact]
    public void PrintStatusTable_ShouldKeepRunEntriesAsInfo_WhenRenderedAfterDisable()
    {
        using var capture = new ConsoleOutputCapture();

        ConsoleTable.PrintStatusTable(
            new[] { new StatusSnapshot { Area = "Autostart (Run)", Item = "Entry", Current = "Present" } },
            intent: ConsoleTable.StatusIntent.AfterDisable,
            showResultColumn: true,
            predictive: false);

        Assert.Contains("INFO", capture.GetOutput());
    }

    [Fact]
    public void PrintStatusTable_ShouldFallbackToToString_ForPrimitiveRows()
    {
        using var capture = new ConsoleOutputCapture();

        var printed = ConsoleTable.PrintStatusTable(new[] { "plain row" });

        Assert.True(printed);
        Assert.Contains("plain row", capture.GetOutput());
    }
}
