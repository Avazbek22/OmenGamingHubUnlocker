namespace OmenGamingHubUnlocker.App;

/// <summary>
/// Serves embedded documentation content so the UI can stay focused on navigation and rendering only.
/// </summary>
public static class DocumentationProvider
{
    private static readonly Lazy<IReadOnlyDictionary<DocumentationDocument, string[]>> CachedDocuments = new(LoadDocuments);

    public static IReadOnlyList<string> GetLines(DocumentationDocument document)
    {
        try
        {
            return CachedDocuments.Value[document];
        }
        catch (Exception exception)
        {
            return
            [
                "Documentation is unavailable.",
                $"Reason: {exception.Message}"
            ];
        }
    }

    private static IReadOnlyDictionary<DocumentationDocument, string[]> LoadDocuments()
    {
        return new Dictionary<DocumentationDocument, string[]>
        {
            [DocumentationDocument.Help] = LoadDocumentLines("Help.txt"),
            [DocumentationDocument.About] = LoadDocumentLines("About.txt")
        };
    }

    private static string[] LoadDocumentLines(string fileName)
    {
        var assembly = typeof(DocumentationProvider).Assembly;
        var resourceName = assembly
            .GetManifestResourceNames()
            .SingleOrDefault(name => name.EndsWith($".Docs.{fileName}", StringComparison.OrdinalIgnoreCase));

        if (string.IsNullOrWhiteSpace(resourceName))
            throw new InvalidOperationException($"Embedded documentation resource '{fileName}' was not found.");

        using var stream = assembly.GetManifestResourceStream(resourceName)
                           ?? throw new InvalidOperationException($"Embedded documentation resource '{fileName}' could not be opened.");
        using var reader = new StreamReader(stream);

        return reader
            .ReadToEnd()
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n');
    }
}

public enum DocumentationDocument
{
    Help = 0,
    About = 1
}
