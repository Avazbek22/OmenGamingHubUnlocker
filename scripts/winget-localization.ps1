Set-StrictMode -Version Latest

$script:WinGetLocalePattern =
    '^([a-zA-Z]{2,3}|[iI]-[a-zA-Z]+|[xX]-[a-zA-Z]{1,8})(-[a-zA-Z]{1,8})*$'
# WinGet rejects numeric region subtags, so es-MX carries the Latin American Spanish metadata.
$script:ExpectedWinGetLocales = @(
    'en-US', 'ru-RU', 'zh-CN', 'zh-TW', 'de-DE', 'fr-FR', 'es-ES', 'es-MX',
    'pt-BR', 'pt-PT', 'ja-JP', 'ko-KR', 'pl-PL', 'tr-TR', 'it-IT', 'th-TH',
    'uk-UA', 'cs-CZ', 'nl-NL', 'sv-SE', 'da-DK', 'nb-NO', 'fi-FI', 'hu-HU',
    'ro-RO', 'el-GR', 'bg-BG', 'id-ID', 'ms-MY', 'vi-VN'
)

function Assert-WinGetTextField {
    param(
        [Parameter(Mandatory)]
        [string]$Locale,

        [Parameter(Mandatory)]
        [string]$FieldName,

        [AllowEmptyString()]
        [string]$Value,

        [Parameter(Mandatory)]
        [int]$MinimumLength,

        [Parameter(Mandatory)]
        [int]$MaximumLength
    )

    $length = $Value.Trim().Length
    if ($length -lt $MinimumLength -or $length -gt $MaximumLength) {
        throw "$Locale.$FieldName must contain $MinimumLength-$MaximumLength characters; found $length."
    }
}

function Assert-WinGetLocalizationData {
    param([Parameter(Mandatory)][object]$Data)

    $globalTags = @($Data.GlobalTags)
    if ($globalTags.Count -ne 4) {
        throw "WinGet localization data must define exactly 4 global tags."
    }

    $localizations = @($Data.Localizations)
    if ($localizations.Count -ne $script:ExpectedWinGetLocales.Count) {
        throw (
            "WinGet localization data must define exactly {0} locales; found {1}." -f
            $script:ExpectedWinGetLocales.Count,
            $localizations.Count)
    }

    $actualLocales = @($localizations | ForEach-Object { [string]$_.Locale })
    $duplicateLocales = @(
        $actualLocales |
        Group-Object { $_.ToLowerInvariant() } |
        Where-Object Count -gt 1
    )
    if ($duplicateLocales.Count -gt 0) {
        throw "Duplicate WinGet locales: $($duplicateLocales.Name -join ', ')."
    }

    if ([string]::Join('|', $actualLocales) -ne
        [string]::Join('|', $script:ExpectedWinGetLocales)) {
        throw (
            "WinGet locales must use the supported order: {0}." -f
            ($script:ExpectedWinGetLocales -join ', '))
    }

    foreach ($localization in $localizations) {
        $locale = [string]$localization.Locale
        if ($locale -notmatch $script:WinGetLocalePattern) {
            throw "Locale '$locale' is not accepted by the WinGet manifest schema."
        }

        Assert-WinGetTextField $locale 'LanguageName' ([string]$localization.LanguageName) 2 100
        Assert-WinGetTextField $locale 'ShortDescription' ([string]$localization.ShortDescription) 3 256
        Assert-WinGetTextField $locale 'Description' ([string]$localization.Description) 3 10000
        Assert-WinGetTextField $locale 'InstallationNotes' ([string]$localization.InstallationNotes) 1 10000
        Assert-WinGetTextField $locale 'DocumentationLabel' ([string]$localization.DocumentationLabel) 1 100

        if (([string]$localization.ShortDescription).IndexOf(
                'OMEN Gaming Hub',
                [StringComparison]::Ordinal) -lt 0) {
            throw "$locale.ShortDescription must preserve the OMEN Gaming Hub product name."
        }

        if (([string]$localization.Description).IndexOf(
                'OMEN Gaming Hub',
                [StringComparison]::Ordinal) -lt 0) {
            throw "$locale.Description must preserve the OMEN Gaming Hub product name."
        }

        $searchTags = @($localization.SearchTags)
        if ($searchTags.Count -ne 12) {
            throw "$locale.SearchTags must contain exactly 12 localized SEO tags."
        }

        $allTags = @($globalTags) + $searchTags
        $duplicateTags = @(
            $allTags |
            Group-Object { ([string]$_).ToLowerInvariant() } |
            Where-Object Count -gt 1
        )
        if ($duplicateTags.Count -gt 0) {
            throw "$locale contains duplicate tags: $($duplicateTags.Name -join ', ')."
        }

        foreach ($tagValue in $allTags) {
            $tag = [string]$tagValue
            Assert-WinGetTextField $locale 'Tag' $tag 1 40
            if ($tag -match '\s') {
                throw "$locale tag '$tag' contains whitespace; use hyphens instead."
            }

            if ($tag -cne $tag.ToLowerInvariant()) {
                throw "$locale tag '$tag' must be lowercase."
            }

            if ($tag -match '(?i)vpn') {
                throw "$locale tag '$tag' contains the intentionally excluded VPN keyword."
            }
        }
    }
}

function Get-WinGetLocalizationData {
    param([Parameter(Mandatory)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "WinGet localization data was not found: $Path"
    }

    try {
        $data = Get-Content -LiteralPath $Path -Raw -Encoding UTF8 |
            ConvertFrom-Json
    }
    catch {
        throw "Unable to read WinGet localization data '$Path': $($_.Exception.Message)"
    }

    Assert-WinGetLocalizationData -Data $data
    return $data
}

function ConvertTo-YamlSingleQuotedScalar {
    param([Parameter(Mandatory)][string]$Value)

    return "'$($Value.Replace("'", "''"))'"
}

function ConvertTo-YamlFoldedBlock {
    param(
        [Parameter(Mandatory)]
        [string]$Value,

        [int]$Indentation = 2
    )

    $indent = ' ' * $Indentation
    $normalizedValue = $Value.Trim() -replace "\r\n?", "`n"
    return (($normalizedValue -split "`n") | ForEach-Object { $indent + $_ }) -join "`n"
}

function Write-WinGetUtf8File {
    param(
        [Parameter(Mandatory)]
        [string]$Path,

        [Parameter(Mandatory)]
        [string]$Content
    )

    $utf8WithoutBom = [Text.UTF8Encoding]::new($false)
    [IO.File]::WriteAllText(
        $Path,
        $Content.Trim() + [Environment]::NewLine,
        $utf8WithoutBom)
}

function New-WinGetLocaleManifestContent {
    param(
        [Parameter(Mandatory)]
        [object]$Localization,

        [Parameter(Mandatory)]
        [string[]]$GlobalTags,

        [Parameter(Mandatory)]
        [string]$Identifier,

        [Parameter(Mandatory)]
        [string]$PackageVersion,

        [Parameter(Mandatory)]
        [string]$ReleaseTag,

        [Parameter(Mandatory)]
        [string]$Repository,

        [Parameter(Mandatory)]
        [string]$Name,

        [Parameter(Mandatory)]
        [string]$CommandAlias,

        [Parameter(Mandatory)]
        [string]$ManifestSchemaVersion
    )

    $locale = [string]$Localization.Locale
    $isDefaultLocale = $locale -eq 'en-US'
    $schemaType = if ($isDefaultLocale) { 'defaultLocale' } else { 'locale' }
    $manifestType = $schemaType
    $shortDescription = ConvertTo-YamlFoldedBlock ([string]$Localization.ShortDescription)
    $description = ConvertTo-YamlFoldedBlock ([string]$Localization.Description)
    $installationNotes = ConvertTo-YamlFoldedBlock ([string]$Localization.InstallationNotes)
    $documentationLabel = ConvertTo-YamlSingleQuotedScalar ([string]$Localization.DocumentationLabel)
    $tagLines = @($GlobalTags) + @($Localization.SearchTags) |
        ForEach-Object { '- ' + (ConvertTo-YamlSingleQuotedScalar ([string]$_)) }

    $commonLocalizedMetadata = @"
ShortDescription: >-
$shortDescription
Description: >-
$description
InstallationNotes: >-
$installationNotes
Tags:
$($tagLines -join [Environment]::NewLine)
Documentations:
- DocumentLabel: $documentationLabel
  DocumentUrl: https://github.com/$Repository#readme
"@

    if ($isDefaultLocale) {
        return @"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.defaultLocale.$ManifestSchemaVersion.schema.json

PackageIdentifier: $Identifier
PackageVersion: $PackageVersion
PackageLocale: $locale
Publisher: Olimoff Dev
PublisherUrl: https://github.com/Avazbek22
PublisherSupportUrl: https://github.com/$Repository/issues
PackageName: $Name
PackageUrl: https://github.com/$Repository
License: MIT
LicenseUrl: https://github.com/$Repository/blob/main/LICENSE
Copyright: Copyright (c) 2025 Avazbek Olimov
$commonLocalizedMetadata
Moniker: $CommandAlias
ReleaseNotesUrl: https://github.com/$Repository/releases/tag/$ReleaseTag
ManifestType: $manifestType
ManifestVersion: $ManifestSchemaVersion
"@
    }

    return @"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.locale.$ManifestSchemaVersion.schema.json

PackageIdentifier: $Identifier
PackageVersion: $PackageVersion
PackageLocale: $locale
$commonLocalizedMetadata
ManifestType: $manifestType
ManifestVersion: $ManifestSchemaVersion
"@
}

function Write-WinGetLocaleManifests {
    param(
        [Parameter(Mandatory)]
        [string]$ManifestRoot,

        [Parameter(Mandatory)]
        [string]$LocalizationDataPath,

        [Parameter(Mandatory)]
        [string]$Identifier,

        [Parameter(Mandatory)]
        [string]$PackageVersion,

        [Parameter(Mandatory)]
        [string]$ReleaseTag,

        [Parameter(Mandatory)]
        [string]$Repository,

        [Parameter(Mandatory)]
        [string]$Name,

        [Parameter(Mandatory)]
        [string]$CommandAlias,

        [Parameter(Mandatory)]
        [string]$ManifestSchemaVersion
    )

    if (-not (Test-Path -LiteralPath $ManifestRoot -PathType Container)) {
        throw "Manifest directory was not found: $ManifestRoot"
    }

    $data = Get-WinGetLocalizationData -Path $LocalizationDataPath
    $existingLocaleFiles = @(
        Get-ChildItem `
            -LiteralPath $ManifestRoot `
            -Filter "$Identifier.locale.*.yaml" `
            -File `
            -ErrorAction SilentlyContinue
    )
    foreach ($existingLocaleFile in $existingLocaleFiles) {
        Remove-Item -LiteralPath $existingLocaleFile.FullName -Force
    }

    $writtenFiles = foreach ($localization in @($data.Localizations)) {
        $locale = [string]$localization.Locale
        $manifestParameters = @{
            Localization = $localization
            GlobalTags = @($data.GlobalTags)
            Identifier = $Identifier
            PackageVersion = $PackageVersion
            ReleaseTag = $ReleaseTag
            Repository = $Repository
            Name = $Name
            CommandAlias = $CommandAlias
            ManifestSchemaVersion = $ManifestSchemaVersion
        }
        $content = New-WinGetLocaleManifestContent @manifestParameters
        $path = Join-Path $ManifestRoot "$Identifier.locale.$locale.yaml"
        Write-WinGetUtf8File -Path $path -Content $content
        Get-Item -LiteralPath $path
    }

    return @($writtenFiles)
}
