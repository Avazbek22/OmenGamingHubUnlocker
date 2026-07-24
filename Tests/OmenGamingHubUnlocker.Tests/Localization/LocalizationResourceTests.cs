namespace OmenGamingHubUnlocker.Tests.Localization;

public sealed class LocalizationResourceTests
{
    [Fact]
    public void EnglishAndRussianResources_ShouldHaveIdenticalUniqueKeys()
    {
        var english = LoadResource("en.json");
        var russian = LoadResource("ru.json");

        Assert.Equal(
            english.Keys.OrderBy(key => key, StringComparer.Ordinal),
            russian.Keys.OrderBy(key => key, StringComparer.Ordinal));
    }

    [Fact]
    public void Translations_ShouldUseTheSameFormatPlaceholders()
    {
        var english = LoadResource("en.json");
        var russian = LoadResource("ru.json");

        foreach (var (key, englishValue) in english)
        {
            var englishPlaceholders = ReadPlaceholders(englishValue);
            var russianPlaceholders = ReadPlaceholders(russian[key]);
            Assert.True(
                englishPlaceholders.SetEquals(russianPlaceholders),
                $"Placeholder mismatch for '{key}': EN=[{string.Join(",", englishPlaceholders)}], RU=[{string.Join(",", russianPlaceholders)}]");
        }
    }

    private static Dictionary<string, string> LoadResource(string fileName)
    {
        var assembly = typeof(LocalizationService).Assembly;
        var resourceName = Assert.Single(
            assembly.GetManifestResourceNames(),
            name => name.EndsWith($".Localization.Resources.{fileName}", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resourceName);
        Assert.NotNull(stream);
        using var document = JsonDocument.Parse(stream);

        var properties = document.RootElement.EnumerateObject().ToList();
        var duplicate = properties
            .GroupBy(property => property.Name, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        Assert.Null(duplicate);

        return properties.ToDictionary(
            property => property.Name,
            property => property.Value.GetString() ?? string.Empty,
            StringComparer.Ordinal);
    }

    private static HashSet<int> ReadPlaceholders(string value)
    {
        var placeholders = new HashSet<int>();
        foreach (System.Text.RegularExpressions.Match match in
                 System.Text.RegularExpressions.Regex.Matches(value, @"\{(\d+)(?:[^}]*)\}"))
        {
            placeholders.Add(int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture));
        }

        return placeholders;
    }
}
