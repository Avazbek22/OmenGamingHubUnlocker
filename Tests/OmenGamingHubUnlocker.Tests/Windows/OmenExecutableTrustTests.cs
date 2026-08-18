namespace OmenGamingHubUnlocker.Tests.Windows;

public sealed class OmenExecutableTrustTests
{
    [Fact]
    public void IsTrustedOmenExecutable_ShouldAcceptExistingOmenPath()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var executablePath = Path.Combine(temporaryDirectory.Path, "OmenCap.exe");
        File.WriteAllBytes(executablePath, []);

        Assert.True(OmenExecutableTrust.IsTrustedOmenExecutable(executablePath, "HPOmenCap"));
    }

    [Fact]
    public void IsTrustedOmenExecutable_ShouldRejectSharedWindowsHost()
    {
        var sharedHostPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "svchost.exe");

        Assert.False(OmenExecutableTrust.IsTrustedOmenExecutable(sharedHostPath, "OmenBackground"));
    }
}
