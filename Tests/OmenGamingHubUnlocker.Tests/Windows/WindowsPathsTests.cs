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

    [Fact]
    public void GetSystemExecutable_ShouldReturnAbsoluteSystemPath()
    {
        var path = WindowsPaths.GetSystemExecutable("sc.exe");

        Assert.True(Path.IsPathRooted(path));
        Assert.Equal(WindowsPaths.SystemDirectory, Path.GetDirectoryName(path), ignoreCase: true);
        Assert.Equal("sc.exe", Path.GetFileName(path), ignoreCase: true);
    }

    [Fact]
    public void WindowsPowerShell_ShouldUseTheSystemDirectory()
    {
        Assert.True(Path.IsPathRooted(WindowsPaths.WindowsPowerShell));
        Assert.StartsWith(
            WindowsPaths.SystemDirectory,
            WindowsPaths.WindowsPowerShell,
            StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(@"..\sc.exe")]
    [InlineData(@"subdirectory\sc.exe")]
    public void GetSystemExecutable_ShouldRejectPathTraversal(string fileName)
    {
        Assert.Throws<ArgumentException>(() => WindowsPaths.GetSystemExecutable(fileName));
    }
}
