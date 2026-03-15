namespace OmenGamingHubUnlocker.Tests.Windows;

public sealed class WindowsPathsTests
{
    [Fact]
    public void HostsPath_ShouldPointToWindowsHostsFile()
    {
        Assert.True(System.IO.Path.IsPathRooted(WindowsPaths.HostsPath));
        Assert.EndsWith(@"System32\drivers\etc\hosts", WindowsPaths.HostsPath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProgramFiles_ShouldBeRooted()
    {
        Assert.True(System.IO.Path.IsPathRooted(WindowsPaths.ProgramFiles));
        Assert.False(string.IsNullOrWhiteSpace(WindowsPaths.ProgramFiles));
    }

    [Fact]
    public void ProgramFilesX86_ShouldBeRooted()
    {
        Assert.True(System.IO.Path.IsPathRooted(WindowsPaths.ProgramFilesX86));
        Assert.False(string.IsNullOrWhiteSpace(WindowsPaths.ProgramFilesX86));
    }
}
