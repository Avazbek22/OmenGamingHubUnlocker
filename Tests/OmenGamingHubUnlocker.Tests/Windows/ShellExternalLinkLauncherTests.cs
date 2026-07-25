using System.ComponentModel;

namespace OmenGamingHubUnlocker.Tests.Windows;

public sealed class ShellExternalLinkLauncherTests
{
    [Fact]
    public void TryOpen_ShouldPassHttpsUrlToWindowsShell()
    {
        ProcessStartInfo? capturedStartInfo = null;
        var launcher = new ShellExternalLinkLauncher(startInfo =>
        {
            capturedStartInfo = startInfo;
            return true;
        });

        var opened = launcher.TryOpen(AppInfo.SupportUrl);

        Assert.True(opened);
        Assert.NotNull(capturedStartInfo);
        Assert.Equal(AppInfo.SupportUrl, capturedStartInfo.FileName);
        Assert.True(capturedStartInfo.UseShellExecute);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("boosty.to/avazbek22")]
    [InlineData("http://boosty.to/avazbek22")]
    [InlineData("file:///C:/Windows/System32/cmd.exe")]
    public void TryOpen_ShouldRejectMissingRelativeOrNonHttpsUrls(string? url)
    {
        var processStartAttempted = false;
        var launcher = new ShellExternalLinkLauncher(_ =>
        {
            processStartAttempted = true;
            return true;
        });

        var opened = launcher.TryOpen(url);

        Assert.False(opened);
        Assert.False(processStartAttempted);
    }

    [Fact]
    public void TryOpen_ShouldReturnFalse_WhenShellDoesNotStartAProcess()
    {
        var launcher = new ShellExternalLinkLauncher(_ => false);

        var opened = launcher.TryOpen(AppInfo.SupportUrl);

        Assert.False(opened);
    }

    [Theory]
    [MemberData(nameof(SupportedShellExceptions))]
    public void TryOpen_ShouldReturnFalse_WhenWindowsShellFails(Exception exception)
    {
        var launcher = new ShellExternalLinkLauncher(_ => throw exception);

        var opened = launcher.TryOpen(AppInfo.SupportUrl);

        Assert.False(opened);
    }

    public static TheoryData<Exception> SupportedShellExceptions { get; } = new()
    {
        new Win32Exception(),
        new InvalidOperationException(),
        new NotSupportedException(),
        new PlatformNotSupportedException()
    };
}
