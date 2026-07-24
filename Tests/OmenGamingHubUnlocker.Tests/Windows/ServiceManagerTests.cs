namespace OmenGamingHubUnlocker.Tests.Windows;

public sealed class ServiceManagerTests
{
    [Theory]
    [InlineData("Auto", "Automatic")]
    [InlineData("Automatic", "Automatic")]
    [InlineData("Delayed-Auto", "Automatic")]
    [InlineData("Demand", "Manual")]
    [InlineData("Manual", "Manual")]
    [InlineData("Disabled", "Disabled")]
    public void NormalizeStartMode_ShouldMapWmiAndScNames(string value, string expected)
    {
        Assert.Equal(expected, ServiceManager.NormalizeStartMode(value));
    }

    [Fact]
    public void NormalizeStartMode_ShouldPreserveUnknownValueForDiagnostics()
    {
        Assert.Equal("Boot", ServiceManager.NormalizeStartMode("Boot"));
    }
}
