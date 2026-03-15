namespace OmenGamingHubUnlocker.Tests.App;

public sealed class DocumentationProviderTests
{
    [Fact]
    public void GetLines_ForHelp_ShouldLoadEmbeddedHelpDocument()
    {
        var lines = DocumentationProvider.GetLines(DocumentationDocument.Help);

        Assert.NotEmpty(lines);
        Assert.Contains(lines, line => line.Contains("Menu guide:", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("It does not uninstall OMEN.", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("Simple recommendation:", StringComparison.Ordinal));
    }

    [Fact]
    public void GetLines_ForAbout_ShouldLoadEmbeddedAboutDocument()
    {
        var lines = DocumentationProvider.GetLines(DocumentationDocument.About);

        Assert.NotEmpty(lines);
        Assert.Contains(lines, line => line.Contains("Author:", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("MIT", StringComparison.Ordinal));
    }
}
