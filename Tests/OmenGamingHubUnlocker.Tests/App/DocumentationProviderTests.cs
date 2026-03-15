namespace OmenGamingHubUnlocker.Tests.App;

public sealed class DocumentationProviderTests
{
    [Fact]
    public void GetLines_ForHelp_ShouldLoadEmbeddedHelpDocument()
    {
        var lines = DocumentationProvider.GetLines(DocumentationDocument.Help);

        Assert.NotEmpty(lines);
        Assert.Contains(lines, line => line.Contains("Quick start:", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("Recommended workflow after an OMEN update:", StringComparison.Ordinal));
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
