namespace OmenGamingHubUnlocker.Tests.UI;

public sealed class ConsoleHelpersTests
{
    [Fact]
    public void WriteSection_ShouldRenderVisibleTitle()
    {
        using var capture = new ConsoleOutputCapture();

        ConsoleHelpers.WriteSection("Test Section");

        Assert.Contains("=== Test Section ===", capture.GetOutput());
    }

    [Fact]
    public void PrintKeyValue_ShouldRenderSingleLine()
    {
        using var capture = new ConsoleOutputCapture();

        ConsoleHelpers.PrintKeyValue("Key", "Value");

        Assert.Contains("Key: Value", capture.GetOutput());
    }

    [Fact]
    public void PrintLinesAnimated_ShouldRenderAllLines()
    {
        using var capture = new ConsoleOutputCapture();
        var oldDelay = ConsoleHelpers.LineDelayMs;
        var oldJitter = ConsoleHelpers.LineJitterMs;

        ConsoleHelpers.LineDelayMs = 0;
        ConsoleHelpers.LineJitterMs = 0;

        try
        {
            ConsoleHelpers.PrintLinesAnimated(["line1", "line2"]);
        }
        finally
        {
            ConsoleHelpers.LineDelayMs = oldDelay;
            ConsoleHelpers.LineJitterMs = oldJitter;
        }

        var output = capture.GetOutput();
        Assert.Contains("line1", output);
        Assert.Contains("line2", output);
    }
}
