namespace OmenGamingHubUnlocker.Tests.Windows;

public sealed class ExecutablePathResolverTests
{
    [Fact]
    public void TryResolveExistingExecutable_ShouldParseQuotedPathWithArguments()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var executablePath = Path.Combine(temporaryDirectory.Path, "OMEN Helper.exe");
        File.WriteAllBytes(executablePath, []);

        var resolved = ExecutablePathResolver.TryResolveExistingExecutable(
            $"\"{executablePath}\" --background",
            out var actualPath);

        Assert.True(resolved);
        Assert.Equal(Path.GetFullPath(executablePath), actualPath, ignoreCase: true);
    }

    [Fact]
    public void TryResolveExistingExecutable_ShouldParseUnquotedServicePathWithSpaces()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var executablePath = Path.Combine(temporaryDirectory.Path, "OmenCap.exe");
        File.WriteAllBytes(executablePath, []);

        var resolved = ExecutablePathResolver.TryResolveExistingExecutable(
            $"{executablePath} -service",
            out var actualPath);

        Assert.True(resolved);
        Assert.Equal(Path.GetFullPath(executablePath), actualPath, ignoreCase: true);
    }

    [Fact]
    public void TryResolveExistingExecutable_ShouldRejectMissingExecutable()
    {
        var resolved = ExecutablePathResolver.TryResolveExistingExecutable(
            @"C:\missing\Omen.exe --background",
            out var actualPath);

        Assert.False(resolved);
        Assert.Empty(actualPath);
    }
}
