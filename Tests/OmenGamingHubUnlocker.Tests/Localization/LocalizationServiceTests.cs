using System.Globalization;

namespace OmenGamingHubUnlocker.Tests.Localization;

public sealed class LocalizationServiceTests
{
    [Fact]
    public void Get_ShouldReturnEnglishStrings_ForEnglishLanguage()
    {
        var service = new LocalizationService(new TestPreferenceStore(), AppLanguage.English);

        Assert.Equal("Change language", service.Get("menu.changeLanguage"));
        Assert.Equal("Snapshot", service.Get("common.snapshot"));
    }

    [Fact]
    public void Get_ShouldReturnRussianStrings_ForRussianLanguage()
    {
        var service = new LocalizationService(new TestPreferenceStore(), AppLanguage.Russian);

        Assert.Equal("Сменить язык", service.Get("menu.changeLanguage"));
        Assert.Equal("Снимок состояния", service.Get("common.snapshot"));
    }

    [Fact]
    public void ToggleLanguage_ShouldUpdateCurrentLanguage_AndPersistSelection()
    {
        var preferenceStore = new TestPreferenceStore();
        var service = new LocalizationService(preferenceStore, AppLanguage.English);

        var newLanguage = service.ToggleLanguage();

        Assert.Equal(AppLanguage.Russian, newLanguage);
        Assert.Equal(AppLanguage.Russian, service.CurrentLanguage);
        Assert.Equal(AppLanguage.Russian, preferenceStore.SavedLanguage);
    }

    [Fact]
    public void DetectFromCultures_ShouldPreferRussian_WhenRussianCultureIsPresent()
    {
        var detectedLanguage = AppLanguageResolver.DetectFromCultures(
            new CultureInfo("en-US"),
            new CultureInfo("ru-RU"));

        Assert.Equal(AppLanguage.Russian, detectedLanguage);
    }

    private sealed class TestPreferenceStore : ILanguagePreferenceStore
    {
        public AppLanguage? SavedLanguage { get; private set; }

        public AppLanguage? Load() => SavedLanguage;

        public void Save(AppLanguage language)
        {
            SavedLanguage = language;
        }
    }
}
