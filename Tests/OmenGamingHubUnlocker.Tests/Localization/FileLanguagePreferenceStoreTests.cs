namespace OmenGamingHubUnlocker.Tests.Localization;

public sealed class FileLanguagePreferenceStoreTests
{
    [Fact]
    public void Load_ShouldReturnNull_WhenSettingsFileDoesNotExist()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var settingsFilePath = Path.Combine(temporaryDirectory.Path, "ui-settings.json");
        var store = new FileLanguagePreferenceStore(settingsFilePath);

        var loadedLanguage = store.Load();

        Assert.Null(loadedLanguage);
    }

    [Fact]
    public void Save_ThenLoad_ShouldRoundTripLanguage()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var settingsFilePath = Path.Combine(temporaryDirectory.Path, "ui-settings.json");
        var store = new FileLanguagePreferenceStore(settingsFilePath);

        store.Save(AppLanguage.Russian);

        var loadedLanguage = store.Load();

        Assert.Equal(AppLanguage.Russian, loadedLanguage);
    }

    [Fact]
    public void ResolveStartupLanguage_ShouldUseSavedPreference_WhenPresent()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var settingsFilePath = Path.Combine(temporaryDirectory.Path, "ui-settings.json");
        var store = new FileLanguagePreferenceStore(settingsFilePath);
        store.Save(AppLanguage.Russian);

        var startupLanguage = AppLanguageResolver.ResolveStartupLanguage(store);

        Assert.Equal(AppLanguage.Russian, startupLanguage);
    }
}
