namespace OmenGamingHubUnlocker.Tests.UI;

public sealed class ConsoleActivityIndicatorTests
{
    [Fact]
    public void Run_ShouldReturnTheOperationResultAndShowActivity()
    {
        using var capture = new ConsoleOutputCapture();

        var result = ConsoleActivityIndicator.Run("Inspecting", () => 42);

        Assert.Equal(42, result);
        Assert.Contains("Inspecting", capture.GetOutput());
    }

    [Fact]
    public void Run_ShouldPropagateTheOriginalException()
    {
        using var capture = new ConsoleOutputCapture();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ConsoleActivityIndicator.Run<int>(
                "Inspecting",
                () => throw new InvalidOperationException("inspection failed")));

        Assert.Equal("inspection failed", exception.Message);
    }
}
