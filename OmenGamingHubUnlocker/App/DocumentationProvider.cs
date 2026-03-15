using System.Globalization;
using System.Reflection;

namespace OmenGamingHubUnlocker.App;

/// <summary>
/// Serves embedded documentation content so the UI can stay focused on navigation and rendering only.
/// </summary>
public static class DocumentationProvider
{
    private static readonly Lazy<IReadOnlyDictionary<(DocumentationDocument Document, AppLanguage Language), string[]>> CachedDocuments = new(LoadDocuments);

    public static IReadOnlyList<string> GetLines(DocumentationDocument document)
        => GetLines(document, Text.CurrentLanguage);

    public static IReadOnlyList<string> GetLines(DocumentationDocument document, AppLanguage language)
    {
        try
        {
            return CachedDocuments.Value[(document, language)];
        }
        catch (Exception exception)
        {
            return
            [
                Text.Get("documentation.unavailable"),
                Text.Format("documentation.reason", exception.Message)
            ];
        }
    }

    private static IReadOnlyDictionary<(DocumentationDocument Document, AppLanguage Language), string[]> LoadDocuments()
    {
        return new Dictionary<(DocumentationDocument Document, AppLanguage Language), string[]>
        {
            [(DocumentationDocument.Help, AppLanguage.English)] = LoadDocumentLines("Help.txt", AppLanguage.English),
            [(DocumentationDocument.Help, AppLanguage.Russian)] = LoadDocumentLines("Help.txt", AppLanguage.Russian),
            [(DocumentationDocument.About, AppLanguage.English)] = LoadDocumentLines("About.txt", AppLanguage.English),
            [(DocumentationDocument.About, AppLanguage.Russian)] = LoadDocumentLines("About.txt", AppLanguage.Russian)
        };
    }

    private static string[] LoadDocumentLines(string fileName, AppLanguage language)
    {
        var resourceAssembly = ResolveResourceAssembly(language);
        var resourceName = resourceAssembly
            .GetManifestResourceNames()
            .SingleOrDefault(name => name.EndsWith($".Docs.{fileName}", StringComparison.OrdinalIgnoreCase));

        if (string.IsNullOrWhiteSpace(resourceName))
            throw new InvalidOperationException($"Embedded documentation resource '{fileName}' for '{language}' was not found.");

        using var stream = resourceAssembly.GetManifestResourceStream(resourceName)
                           ?? throw new InvalidOperationException($"Embedded documentation resource '{fileName}' for '{language}' could not be opened.");
        using var reader = new StreamReader(stream);

        return reader
            .ReadToEnd()
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n');
    }

    private static Assembly ResolveResourceAssembly(AppLanguage language)
    {
        var mainAssembly = typeof(DocumentationProvider).Assembly;
        var culture = new CultureInfo(GetCultureName(language));

        try
        {
            return mainAssembly.GetSatelliteAssembly(culture);
        }
        catch (Exception exception) when (exception is FileNotFoundException or FileLoadException or BadImageFormatException)
        {
            throw new InvalidOperationException($"Embedded documentation satellite assembly for '{language}' could not be loaded.", exception);
        }
    }

    private static string GetCultureName(AppLanguage language)
        => language switch
        {
            AppLanguage.English => "en",
            AppLanguage.Russian => "ru",
            _ => throw new ArgumentOutOfRangeException(nameof(language), language, "Unsupported documentation language.")
        };
}

public enum DocumentationDocument
{
    Help = 0,
    About = 1
}
