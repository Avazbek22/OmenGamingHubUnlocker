using System.Globalization;

namespace OmenGamingHubUnlocker.Localization;

/// <summary>
/// Loads embedded localization resources and exposes translated strings for the active UI language.
/// </summary>
public sealed class LocalizationService
{
    private static readonly Lazy<IReadOnlyDictionary<AppLanguage, IReadOnlyDictionary<string, string>>> CachedResources = new(LoadResources);

    private readonly ILanguagePreferenceStore _preferenceStore;

    public LocalizationService(ILanguagePreferenceStore preferenceStore, AppLanguage initialLanguage)
    {
        _preferenceStore = preferenceStore;
        CurrentLanguage = initialLanguage;
    }

    public AppLanguage CurrentLanguage { get; private set; }

    public void SetLanguage(AppLanguage language)
    {
        CurrentLanguage = language;
        _preferenceStore.Save(language);
    }

    public AppLanguage ToggleLanguage()
    {
        var nextLanguage = CurrentLanguage == AppLanguage.English
            ? AppLanguage.Russian
            : AppLanguage.English;

        SetLanguage(nextLanguage);
        return nextLanguage;
    }

    public string Get(string key)
    {
        var resources = CachedResources.Value;

        if (resources[CurrentLanguage].TryGetValue(key, out var localizedValue))
            return localizedValue;

        if (resources[AppLanguage.English].TryGetValue(key, out var fallbackValue))
            return fallbackValue;

        return $"[[{key}]]";
    }

    public string Format(string key, params object?[] arguments)
        => string.Format(CultureInfo.CurrentCulture, Get(key), arguments);

    private static IReadOnlyDictionary<AppLanguage, IReadOnlyDictionary<string, string>> LoadResources()
    {
        return new Dictionary<AppLanguage, IReadOnlyDictionary<string, string>>
        {
            [AppLanguage.English] = LoadLanguageResources("en"),
            [AppLanguage.Russian] = LoadLanguageResources("ru")
        };
    }

    private static IReadOnlyDictionary<string, string> LoadLanguageResources(string languageCode)
    {
        var assembly = typeof(LocalizationService).Assembly;
        var resourceName = assembly
            .GetManifestResourceNames()
            .SingleOrDefault(name => name.EndsWith($".Localization.Resources.{languageCode}.json", StringComparison.OrdinalIgnoreCase));

        if (string.IsNullOrWhiteSpace(resourceName))
            throw new InvalidOperationException($"Localization resource '{languageCode}.json' was not found.");

        using var stream = assembly.GetManifestResourceStream(resourceName)
                           ?? throw new InvalidOperationException($"Localization resource '{languageCode}.json' could not be opened.");
        using var reader = new StreamReader(stream);

        return JsonSerializer.Deserialize<Dictionary<string, string>>(reader.ReadToEnd())
               ?? throw new InvalidOperationException($"Localization resource '{languageCode}.json' is invalid.");
    }
}
