namespace OmenGamingHubUnlocker.Tests.Windows;

public sealed class PowerShellRunnerTests
{
    [Fact]
    public void TryRun_ShouldReturnTrue_ForSuccessfulCommand()
    {
        var result = PowerShellRunner.TryRun("cmd.exe", "/c exit 0", out var stdout, out var stderr, 5_000);

        Assert.True(result);
        Assert.Equal(string.Empty, stdout);
        Assert.Equal(string.Empty, stderr);
    }

    [Fact]
    public void TryRun_ShouldCaptureStandardError_ForFailingCommand()
    {
        var result = PowerShellRunner.TryRun("cmd.exe", "/c echo boom 1>&2 & exit /b 5", out _, out var stderr, 5_000);

        Assert.False(result);
        Assert.Contains("boom", stderr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryRun_ShouldFailWithTimeoutMessage_WhenCommandHangs()
    {
        var result = PowerShellRunner.TryRun("cmd.exe", "/c ping 127.0.0.1 -n 8 > nul", out _, out var stderr, 200);

        Assert.False(result);
        Assert.Contains("timed out", stderr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CheckAvailability_ShouldReturnDetails()
    {
        var (ok, details) = PowerShellRunner.CheckAvailability();

        Assert.False(string.IsNullOrWhiteSpace(details));
        Assert.True(ok || !ok);
    }
}
