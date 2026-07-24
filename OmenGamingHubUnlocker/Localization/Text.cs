namespace OmenGamingHubUnlocker.Localization;

/// <summary>
/// Provides a simple global facade over the current localization service.
/// </summary>
public static class Text
{
    private static LocalizationService _service = new(new InMemoryLanguagePreferenceStore(), AppLanguage.English);

    public static AppLanguage CurrentLanguage => _service.CurrentLanguage;

    public static void Initialize(LocalizationService service)
        => _service = service ?? throw new ArgumentNullException(nameof(service));

    public static string Get(string key)
        => _service.Get(key);

    public static string Format(string key, params object?[] arguments)
        => _service.Format(key, arguments);

    public static void SetLanguage(AppLanguage language)
        => _service.SetLanguage(language);

    public static AppLanguage ToggleLanguage()
        => _service.ToggleLanguage();

    private sealed class InMemoryLanguagePreferenceStore : ILanguagePreferenceStore
    {
        public AppLanguage? Load() => AppLanguage.English;
        public void Save(AppLanguage language) { }
    }
}
