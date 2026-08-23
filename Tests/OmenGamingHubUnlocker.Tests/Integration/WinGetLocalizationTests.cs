using System.Text.RegularExpressions;

namespace OmenGamingHubUnlocker.Tests.Integration;

public sealed partial class WinGetLocalizationTests
{
    private static readonly string[] ExpectedLocales =
    [
        "en-US", "ru-RU", "zh-CN", "zh-TW", "de-DE", "fr-FR", "es-ES", "es-MX",
        "pt-BR", "pt-PT", "ja-JP", "ko-KR", "pl-PL", "tr-TR", "it-IT", "th-TH",
        "uk-UA", "cs-CZ", "nl-NL", "sv-SE", "da-DK", "nb-NO", "fi-FI", "hu-HU",
        "ro-RO", "el-GR", "bg-BG", "id-ID", "ms-MY", "vi-VN"
    ];

    private static readonly string[] GlobalTags =
    [
        "omen-gaming-hub",
        "hp-omen-gaming-hub",
        "omen-gaming-hub-unlocker",
        "omen-command-center"
    ];

    [Fact]
    public void Metadata_ShouldContainEverySupportedLocaleInStableOrder()
    {
        using var document = LoadMetadata();
        var localizations = document.RootElement.GetProperty("Localizations");
        var actualLocales = localizations
            .EnumerateArray()
            .Select(localization => localization.GetProperty("Locale").GetString())
            .ToArray();

        Assert.Equal(ExpectedLocales, actualLocales);
        Assert.DoesNotContain("es-419", actualLocales);
    }

    [Fact]
    public void Metadata_ShouldProvideCompleteLocalizedContent()
    {
        using var document = LoadMetadata();

        foreach (var localization in document.RootElement.GetProperty("Localizations").EnumerateArray())
        {
            var locale = GetRequiredString(localization, "Locale");
            Assert.Matches(LocalePattern(), locale);
            AssertLength(localization, "LanguageName", 2, 100, locale);
            AssertLength(localization, "ShortDescription", 3, 256, locale);
            AssertLength(localization, "Description", 3, 10_000, locale);
            AssertLength(localization, "InstallationNotes", 1, 10_000, locale);
            AssertLength(localization, "DocumentationLabel", 1, 100, locale);
            Assert.Contains("OMEN Gaming Hub", GetRequiredString(localization, "ShortDescription"), StringComparison.Ordinal);
            Assert.Contains("OMEN Gaming Hub", GetRequiredString(localization, "Description"), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Metadata_ShouldUseSixteenUniqueSearchTagsPerLocale()
    {
        using var document = LoadMetadata();
        var globalTags = document.RootElement
            .GetProperty("GlobalTags")
            .EnumerateArray()
            .Select(tag => tag.GetString())
            .ToArray();
        Assert.Equal(GlobalTags, globalTags);

        foreach (var localization in document.RootElement.GetProperty("Localizations").EnumerateArray())
        {
            var locale = GetRequiredString(localization, "Locale");
            var localizedTags = localization
                .GetProperty("SearchTags")
                .EnumerateArray()
                .Select(tag => tag.GetString() ?? string.Empty)
                .ToArray();
            Assert.Equal(12, localizedTags.Length);

            var allTags = GlobalTags.Concat(localizedTags).ToArray();
            Assert.Equal(16, allTags.Distinct(StringComparer.OrdinalIgnoreCase).Count());
            foreach (var tag in allTags)
            {
                Assert.InRange(tag.Length, 1, 40);
                Assert.DoesNotMatch(WhitespacePattern(), tag);
                Assert.Equal(tag.ToLowerInvariant(), tag);
                Assert.DoesNotContain("vpn", tag, StringComparison.OrdinalIgnoreCase);
            }

            Assert.True(
                localizedTags.Any(tag => tag.Contains("omen", StringComparison.OrdinalIgnoreCase)),
                $"{locale} must contain localized OMEN search phrases.");
        }
    }

    [Fact]
    public void Generator_ShouldWriteOneValidlyShapedManifestPerLocale()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var helperPath = GetWinGetFilePath("winget-localization.ps1");
        var metadataPath = GetWinGetFilePath("winget-localizations.json");
        var script = $$"""
            . '{{EscapePowerShellLiteral(helperPath)}}'
            $files = Write-WinGetLocaleManifests `
                -ManifestRoot '{{EscapePowerShellLiteral(temporaryDirectory.Path)}}' `
                -LocalizationDataPath '{{EscapePowerShellLiteral(metadataPath)}}' `
                -Identifier 'OlimoffDev.OmenGamingHubUnlocker' `
                -PackageVersion '3.3.0.0' `
                -ReleaseTag 'v3.3' `
                -Repository 'Avazbek22/OmenGamingHubUnlocker' `
                -Name 'Omen Gaming Hub Unlocker' `
                -CommandAlias 'ogh-unlocker' `
                -ManifestSchemaVersion '1.12.0'
            if ($files.Count -ne 30) { throw "Expected 30 locale manifests; found $($files.Count)." }
            """;

        var succeeded = PowerShellRunner.TryRunScript(script, out var output, out var error, 20_000);

        Assert.True(succeeded, $"PowerShell output: {output}{Environment.NewLine}PowerShell error: {error}");
        var manifests = Directory.GetFiles(temporaryDirectory.Path, "*.locale.*.yaml");
        Assert.Equal(30, manifests.Length);
        Assert.DoesNotContain(manifests, path => path.Contains("es-419", StringComparison.Ordinal));

        var englishPath = Path.Combine(
            temporaryDirectory.Path,
            "OlimoffDev.OmenGamingHubUnlocker.locale.en-US.yaml");
        var russianPath = Path.Combine(
            temporaryDirectory.Path,
            "OlimoffDev.OmenGamingHubUnlocker.locale.ru-RU.yaml");
        var english = File.ReadAllText(englishPath, Encoding.UTF8);
        var russian = File.ReadAllText(russianPath, Encoding.UTF8);
        Assert.Contains("ManifestType: defaultLocale", english, StringComparison.Ordinal);
        Assert.Contains("ManifestType: locale", russian, StringComparison.Ordinal);
        Assert.Contains("omen-gaming-hub-not-available-in-region", english, StringComparison.Ordinal);
        Assert.Contains("omen-недоступен-в-регионе", russian, StringComparison.Ordinal);
        Assert.False(File.ReadAllBytes(englishPath).AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }));
    }

    private static JsonDocument LoadMetadata()
        => JsonDocument.Parse(File.ReadAllBytes(GetWinGetFilePath("winget-localizations.json")));

    private static string GetWinGetFilePath(string fileName)
        => Path.Combine(AppContext.BaseDirectory, "WinGet", fileName);

    private static string GetRequiredString(JsonElement localization, string propertyName)
    {
        var value = localization.GetProperty(propertyName).GetString();
        return Assert.IsType<string>(value);
    }

    private static void AssertLength(
        JsonElement localization,
        string propertyName,
        int minimum,
        int maximum,
        string locale)
    {
        var value = GetRequiredString(localization, propertyName).Trim();
        Assert.True(
            value.Length >= minimum && value.Length <= maximum,
            $"{locale}.{propertyName} must contain {minimum}-{maximum} characters; found {value.Length}.");
    }

    private static string EscapePowerShellLiteral(string value)
        => value.Replace("'", "''", StringComparison.Ordinal);

    [GeneratedRegex(@"^([a-zA-Z]{2,3}|[iI]-[a-zA-Z]+|[xX]-[a-zA-Z]{1,8})(-[a-zA-Z]{1,8})*$")]
    private static partial Regex LocalePattern();

    [GeneratedRegex(@"\s")]
    private static partial Regex WhitespacePattern();
}
