namespace OmenGamingHubUnlocker.Localization;

/// <summary>
/// Persists the user's selected UI language between runs.
/// </summary>
public interface ILanguagePreferenceStore
{
    AppLanguage? Load();
    void Save(AppLanguage language);
}
