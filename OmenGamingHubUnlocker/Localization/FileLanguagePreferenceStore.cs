namespace OmenGamingHubUnlocker.Localization;

/// <summary>
/// Stores the selected UI language inside the user's AppData profile.
/// </summary>
public sealed class FileLanguagePreferenceStore(string? settingsFilePath = null) : ILanguagePreferenceStore
{
    private readonly string _settingsFilePath = string.IsNullOrWhiteSpace(settingsFilePath)
        ? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            AppInfo.AppName,
            "ui-settings.json")
        : settingsFilePath;

    public AppLanguage? Load()
    {
        try
        {
            if (!File.Exists(_settingsFilePath))
                return null;

            var json = File.ReadAllText(_settingsFilePath);
            var settings = JsonSerializer.Deserialize<LanguageSettings>(json);
            return ParseLanguageCode(settings?.Language);
        }
        catch
        {
            return null;
        }
    }

    public void Save(AppLanguage language)
    {
        try
        {
            var directoryPath = Path.GetDirectoryName(_settingsFilePath);
            if (string.IsNullOrWhiteSpace(directoryPath))
                return;

            Directory.CreateDirectory(directoryPath);

            var settings = new LanguageSettings(ToLanguageCode(language));
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_settingsFilePath, json);
        }
        catch
        {
            // Localization preference persistence is best-effort and must never block the main app flow.
        }
    }

    internal static string ToLanguageCode(AppLanguage language)
        => language == AppLanguage.Russian ? "ru" : "en";

    internal static AppLanguage? ParseLanguageCode(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "ru" or "ru-ru" => AppLanguage.Russian,
            "en" or "en-us" or "en-gb" => AppLanguage.English,
            _ => null
        };
    }

    private sealed record LanguageSettings(string Language);
}
