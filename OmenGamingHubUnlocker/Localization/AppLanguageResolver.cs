using System.Globalization;

namespace OmenGamingHubUnlocker.Localization;

/// <summary>
/// Resolves the startup UI language from a saved preference or the current Windows culture chain.
/// </summary>
public static class AppLanguageResolver
{
    public static AppLanguage ResolveStartupLanguage(ILanguagePreferenceStore preferenceStore)
        => preferenceStore.Load() ?? DetectSystemLanguage();

    public static AppLanguage DetectSystemLanguage()
        => DetectFromCultures(
            CultureInfo.CurrentUICulture,
            CultureInfo.CurrentCulture,
            CultureInfo.InstalledUICulture);

    public static AppLanguage DetectFromCultures(params CultureInfo?[] cultures)
    {
        foreach (var culture in cultures)
        {
            var languageCode = culture?.TwoLetterISOLanguageName;
            if (string.Equals(languageCode, "ru", StringComparison.OrdinalIgnoreCase))
                return AppLanguage.Russian;
        }

        return AppLanguage.English;
    }
}
