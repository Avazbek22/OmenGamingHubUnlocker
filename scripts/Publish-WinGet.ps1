<#
.SYNOPSIS
  Creates or updates the Omen Gaming Hub Unlocker package in winget-pkgs.

.DESCRIPTION
  The script uses the public GitHub release as the immutable installer source,
  validates both architectures, generates WinGet manifests in a disposable
  artifacts folder, optionally tests a local installation, and can submit a
  pull request through WingetCreate.

  The first accepted version is generated locally. Later versions are based on
  the existing community manifest through `wingetcreate update`.
#>
[CmdletBinding()]
param(
    [string]$Version = "",

    [ValidateSet("Auto", "New", "Update")]
    [string]$Mode = "Auto",

    [string]$PackageIdentifier = "OlimoffDev.OmenGamingHubUnlocker",

    [string]$Repository = "Avazbek22/OmenGamingHubUnlocker",

    [switch]$InstallTest,

    [switch]$KeepTestInstall,

    [switch]$Submit,

    [switch]$LocalOnly,

    [switch]$NonInteractive,

    [switch]$PlanOnly,

    [string]$OutDirectory = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "release-helpers.ps1")

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$projectPath = Join-Path $repositoryRoot "OmenGamingHubUnlocker\OmenGamingHubUnlocker.csproj"
$manifestSchemaVersion = "1.12.0"
$commandAlias = "ogh-unlocker"
$packageName = "Omen Gaming Hub Unlocker"

function Write-Step {
    param([Parameter(Mandatory)][string]$Message)

    Write-Host
    Write-Host "=== $Message ===" -ForegroundColor Cyan
}

function Ensure-Command {
    param([Parameter(Mandatory)][string]$Name)

    if ($null -eq (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Required command not found: $Name"
    }
}

function Invoke-NativeCommand {
    param(
        [Parameter(Mandatory)]
        [string]$FilePath,

        [Parameter(Mandatory)]
        [string[]]$Arguments
    )

    Write-Host "> $FilePath $($Arguments -join ' ')" -ForegroundColor DarkGray
    $commandOutput = @(& $FilePath @Arguments 2>&1)
    $exitCode = $LASTEXITCODE

    foreach ($outputLine in $commandOutput) {
        Write-Host $outputLine
    }

    if ($exitCode -ne 0) {
        throw "Command '$FilePath' failed with exit code $exitCode."
    }

    return $commandOutput
}

function Read-Optional {
    param(
        [Parameter(Mandatory)]
        [string]$Prompt,

        [string]$DefaultValue = ""
    )

    $suffix = if ([string]::IsNullOrWhiteSpace($DefaultValue)) {
        ""
    }
    else {
        " [$DefaultValue]"
    }

    $inputValue = Read-Host ($Prompt + $suffix)
    if ([string]::IsNullOrWhiteSpace($inputValue)) {
        return $DefaultValue
    }

    return $inputValue.Trim()
}

function Get-GitHubRelease {
    param(
        [Parameter(Mandatory)]
        [string]$RepositoryName,

        [Parameter(Mandatory)]
        [string]$ReleaseTag
    )

    $encodedTag = [Uri]::EscapeDataString($ReleaseTag)
    $apiUrl = "https://api.github.com/repos/$RepositoryName/releases/tags/$encodedTag"
    $headers = @{
        Accept = "application/vnd.github+json"
        "User-Agent" = "OmenGamingHubUnlocker-WinGet-Publisher"
        "X-GitHub-Api-Version" = "2022-11-28"
    }

    try {
        $release = Invoke-RestMethod -Uri $apiUrl -Headers $headers -Method Get
    }
    catch {
        throw "GitHub release '$ReleaseTag' was not found in '$RepositoryName': $($_.Exception.Message)"
    }

    if ($release.draft) {
        throw "GitHub release '$ReleaseTag' is still a draft."
    }

    if ($release.prerelease) {
        throw "GitHub release '$ReleaseTag' is a prerelease and cannot be published to WinGet."
    }

    return $release
}

function Get-ReleaseInstallerEntries {
    param(
        [Parameter(Mandatory)]
        [object]$Release,

        [Parameter(Mandatory)]
        [string]$DisplayVersion
    )

    $entries = foreach ($architecture in @("x64", "arm64")) {
        $runtimeIdentifier = "win-$architecture"
        $assetName = Get-ReleaseArtifactName `
            -DisplayVersion $DisplayVersion `
            -RuntimeIdentifier $runtimeIdentifier
        $asset = @($Release.assets | Where-Object { $_.name -eq $assetName }) | Select-Object -First 1

        if ($null -eq $asset) {
            throw "Release asset '$assetName' was not found."
        }

        [pscustomobject]@{
            Architecture = $architecture
            Name = $assetName
            Url = [string]$asset.browser_download_url
            Digest = [string]$asset.digest
        }
    }

    return @($entries)
}

function Get-InstallerSha256 {
    param(
        [Parameter(Mandatory)]
        [object]$InstallerEntry
    )

    if ($InstallerEntry.Digest -match '^sha256:([a-fA-F0-9]{64})$') {
        return $Matches[1].ToUpperInvariant()
    }

    $temporaryFile = Join-Path ([IO.Path]::GetTempPath()) (
        "ogh-winget-" + [Guid]::NewGuid().ToString("N") + ".exe")

    try {
        Write-Host "Downloading $($InstallerEntry.Name) to calculate SHA-256..."
        Invoke-WebRequest `
            -Uri $InstallerEntry.Url `
            -OutFile $temporaryFile `
            -UseBasicParsing
        return (Get-FileHash -LiteralPath $temporaryFile -Algorithm SHA256).Hash
    }
    finally {
        if (Test-Path -LiteralPath $temporaryFile) {
            Remove-Item -LiteralPath $temporaryFile -Force
        }
    }
}

function Test-PackageExists {
    param([Parameter(Mandatory)][string]$Identifier)

    & wingetcreate show $Identifier *> $null
    return $LASTEXITCODE -eq 0
}

function Resolve-PublishMode {
    param(
        [Parameter(Mandatory)]
        [string]$RequestedMode,

        [Parameter(Mandatory)]
        [bool]$PackageExists,

        [Parameter(Mandatory)]
        [string]$Identifier
    )

    if ($RequestedMode -eq "Auto") {
        if ($PackageExists) {
            return "Update"
        }

        return "New"
    }

    if ($RequestedMode -eq "New" -and $PackageExists) {
        throw "Package '$Identifier' already exists. Use Update mode."
    }

    if ($RequestedMode -eq "Update" -and -not $PackageExists) {
        throw "Package '$Identifier' does not exist yet. Use New mode."
    }

    return $RequestedMode
}

function Resolve-ManifestRoot {
    param(
        [Parameter(Mandatory)]
        [string]$OutputDirectory,

        [Parameter(Mandatory)]
        [string]$Identifier,

        [Parameter(Mandatory)]
        [string]$PackageVersion
    )

    $identifierParts = $Identifier.Split('.')
    if ($identifierParts.Length -lt 2) {
        throw "Unexpected package identifier: $Identifier"
    }

    $publisher = $identifierParts[0]
    $packageName = $identifierParts[1]
    $expectedPath = Join-Path $OutputDirectory (
        "manifests\{0}\{1}\{2}\{3}" -f
        $publisher.Substring(0, 1).ToLowerInvariant(),
        $publisher,
        $packageName,
        $PackageVersion)

    if (Test-Path -LiteralPath $expectedPath -PathType Container) {
        return $expectedPath
    }

    $fallback = Get-ChildItem `
        -LiteralPath $OutputDirectory `
        -Directory `
        -Recurse `
        -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -eq $PackageVersion } |
        Select-Object -First 1

    if ($null -eq $fallback) {
        throw "Manifest directory was not found under '$OutputDirectory'."
    }

    return $fallback.FullName
}

function Write-Utf8File {
    param(
        [Parameter(Mandatory)]
        [string]$Path,

        [Parameter(Mandatory)]
        [string]$Content
    )

    $utf8WithoutBom = [Text.UTF8Encoding]::new($false)
    [IO.File]::WriteAllText($Path, $Content.Trim() + [Environment]::NewLine, $utf8WithoutBom)
}

function New-InitialManifests {
    param(
        [Parameter(Mandatory)]
        [string]$ManifestRoot,

        [Parameter(Mandatory)]
        [string]$Identifier,

        [Parameter(Mandatory)]
        [string]$PackageVersion,

        [Parameter(Mandatory)]
        [string]$ReleaseTag,

        [Parameter(Mandatory)]
        [string]$ReleaseDate,

        [Parameter(Mandatory)]
        [string]$Name,

        [Parameter(Mandatory)]
        [object[]]$InstallerEntries
    )

    New-Item -ItemType Directory -Path $ManifestRoot -Force | Out-Null

    $installerLines = foreach ($installerEntry in $InstallerEntries) {
        $sha256 = Get-InstallerSha256 -InstallerEntry $installerEntry
@"
- Architecture: $($installerEntry.Architecture)
  InstallerUrl: $($installerEntry.Url)
  InstallerSha256: $sha256
"@
    }

    $versionManifest = @"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.version.$manifestSchemaVersion.schema.json

PackageIdentifier: $Identifier
PackageVersion: $PackageVersion
DefaultLocale: en-US
ManifestType: version
ManifestVersion: $manifestSchemaVersion
"@

    $installerManifest = @"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.installer.$manifestSchemaVersion.schema.json

PackageIdentifier: $Identifier
PackageVersion: $PackageVersion
MinimumOSVersion: 10.0.19041.0
InstallerType: portable
Commands:
- $commandAlias
ReleaseDate: $ReleaseDate
Installers:
$($installerLines -join [Environment]::NewLine)
ManifestType: installer
ManifestVersion: $manifestSchemaVersion
"@

    $localeManifest = @"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.defaultLocale.$manifestSchemaVersion.schema.json

PackageIdentifier: $Identifier
PackageVersion: $PackageVersion
PackageLocale: en-US
Publisher: Olimoff Dev
PublisherUrl: https://github.com/Avazbek22
PublisherSupportUrl: https://github.com/$Repository/issues
PackageName: $Name
PackageUrl: https://github.com/$Repository
License: MIT
LicenseUrl: https://github.com/$Repository/blob/main/LICENSE
Copyright: Copyright (c) 2025 Avazbek Olimov
ShortDescription: Unofficial utility for controlling OMEN Gaming Hub background activity and network access.
Description: >-
  Keeps OMEN Gaming Hub installed while managing its background processes,
  services, scheduled tasks, startup entries, firewall rules, and known network
  endpoints. Includes status inspection, dry-run planning, rollback, and AppX
  reset workflows. This project is not affiliated with or endorsed by HP.
InstallationNotes: >-
  WinGet removes only the portable executable and command alias. To reverse
  changes applied by this utility, run Restore original settings before
  uninstalling the package.
Moniker: $commandAlias
Tags:
- firewall
- gaming
- hp
- omen
- privacy
- startup
- utility
- windows
ReleaseNotesUrl: https://github.com/$Repository/releases/tag/$ReleaseTag
Documentations:
- DocumentLabel: Project documentation
  DocumentUrl: https://github.com/$Repository#readme
ManifestType: defaultLocale
ManifestVersion: $manifestSchemaVersion
"@

    Write-Utf8File `
        -Path (Join-Path $ManifestRoot "$Identifier.yaml") `
        -Content $versionManifest
    Write-Utf8File `
        -Path (Join-Path $ManifestRoot "$Identifier.installer.yaml") `
        -Content $installerManifest
    Write-Utf8File `
        -Path (Join-Path $ManifestRoot "$Identifier.locale.en-US.yaml") `
        -Content $localeManifest
}

function Update-ExistingManifests {
    param(
        [Parameter(Mandatory)]
        [string]$OutputDirectory,

        [Parameter(Mandatory)]
        [string]$Identifier,

        [Parameter(Mandatory)]
        [string]$PackageVersion,

        [Parameter(Mandatory)]
        [string]$ReleaseTag,

        [Parameter(Mandatory)]
        [string]$ReleaseDate,

        [Parameter(Mandatory)]
        [object[]]$InstallerEntries
    )

    $urlArguments = @(
        $InstallerEntries |
        ForEach-Object { "$($_.Url)|$($_.Architecture)" }
    )

    $arguments = @("update", "--urls") +
        $urlArguments +
        @(
            "--version", $PackageVersion,
            "--release-notes-url", "https://github.com/$Repository/releases/tag/$ReleaseTag",
            "--release-date", $ReleaseDate,
            "--out", $OutputDirectory,
            $Identifier
        )

    Invoke-NativeCommand -FilePath "wingetcreate" -Arguments $arguments | Out-Null
}

function Invoke-ManifestValidation {
    param([Parameter(Mandatory)][string]$ManifestRoot)

    Invoke-NativeCommand `
        -FilePath "winget" `
        -Arguments @("validate", "--manifest", $ManifestRoot) |
        Out-Null
}

function Test-PackageInstalled {
    param(
        [Parameter(Mandatory)]
        [string]$Identifier,

        [Parameter(Mandatory)]
        [string]$Name
    )

    & winget list --id $Identifier --exact --disable-interactivity *> $null
    if ($LASTEXITCODE -eq 0) {
        return $true
    }

    # Local portable manifests use an internal ARP identifier until their
    # package identifier exists in the public WinGet source.
    & winget list --name $Name --exact --disable-interactivity *> $null
    return $LASTEXITCODE -eq 0
}

function Invoke-ManifestInstallTest {
    param(
        [Parameter(Mandatory)]
        [string]$ManifestRoot,

        [Parameter(Mandatory)]
        [string]$Identifier,

        [Parameter(Mandatory)]
        [string]$Name,

        [Parameter(Mandatory)]
        [bool]$KeepInstalled
    )

    if (Test-PackageInstalled -Identifier $Identifier -Name $Name) {
        throw "Package '$Identifier' is already installed. Refusing to overwrite it during the test."
    }

    Invoke-NativeCommand `
        -FilePath "winget" `
        -Arguments @(
            "install",
            "--manifest", $ManifestRoot,
            "--accept-package-agreements",
            "--accept-source-agreements",
            "--disable-interactivity"
        ) |
        Out-Null

    if (-not (Test-PackageInstalled -Identifier $Identifier -Name $Name)) {
        throw "Local installation completed, but '$Identifier' was not registered by WinGet."
    }

    if (-not $KeepInstalled) {
        Invoke-NativeCommand `
            -FilePath "winget" `
            -Arguments @(
                "uninstall",
                "--name", $Name,
                "--exact",
                "--disable-interactivity"
            ) |
            Out-Null
    }
}

function Submit-Manifests {
    param(
        [Parameter(Mandatory)]
        [string]$ManifestRoot,

        [Parameter(Mandatory)]
        [string]$Identifier,

        [Parameter(Mandatory)]
        [string]$PackageVersion,

        [Parameter(Mandatory)]
        [string]$PublishMode
    )

    $pullRequestTitle = if ($PublishMode -eq "New") {
        "New package: $Identifier version $PackageVersion"
    }
    else {
        "Update $Identifier to $PackageVersion"
    }

    Write-Host "> wingetcreate submit --prtitle `"$pullRequestTitle`" $ManifestRoot" -ForegroundColor DarkGray
    $submissionOutput = wingetcreate submit --prtitle $pullRequestTitle $ManifestRoot 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) {
        throw "WingetCreate submission fc~7âÚ$z{-®éÜj×Ð¨¨(4(Ô¸ƒÂ~RI•‰½½Ð¥Ì€¨©½ÁÑ¥½¹…°¨¨°‰ÕÐÉ•½µµ•¹‘•¥˜å½ÔÝ…¹Ð„±•…¸ƒŠqÍÑ…ÉÑÕÀÙ•É¥™¥…Ñ¥½»Št¸4(4(´´´4(4(ŒŒƒÂ~žÀ]¡…ÐƒŠqÑ¥Ù…Ñ”ÍÉ¥ÁÑÏŠt‘½•Ì4(4)]¡•¸å½ÔÉÕ¸€¨©Ñ¥Ù…Ñ”ÍÉ¥ÁÑÌ¨¨°Ñ¡”Ñ½½°è4(4(´€¨©¥É•Ý…±°è¨¨É•…Ñ•Ì„Ù•ÉÍ¥½¸µ¥¹‘•Á•¹‘•¹ÐÁ…­…”µM%‰±½¬…¹•áÁ±¥¥Ð½ÕÑ‰½Õ¹‰±½­Ì™½ÈÕÉÉ•¹Ð=58•á•ÕÑ…‰±•Ì4(´€¨©M•ÉÙ¥•Ìè¨¨Í•ÑÌ=58µ½Ý¹•Í•ÉÙ¥•ÌÑ¼€¨©5…¹Õ…°¨¨…¹ÍÑ½ÁÌÉÕ¹¹¥¹œ¥¹ÍÑ…¹•Ì4(´€¨©Q…Í­Ìè¨¨‘¥Í…‰±•Ì=58Í¡•‘Õ±•Ñ…Í­Ì…¹ÍÑ½ÁÌÉÕ¹¹¥¹œ¥¹ÍÑ…¹•Ì4(´€¨©AÉ½•ÍÍ•Ìè¨¨Ñ•Éµ¥¹…Ñ•Ì‘¥Í½Ù•É•Á…­…”…¹­¹½Ý¸•áÑ•É¹…°=58‰…­É½Õ¹ÁÉ½•ÍÍ•Ì4(´€¨©IÕ¸­•åÌè¨¨É•µ½Ù•Ìµ…Ñ¡¥¹œ=58…ÕÑ½ÍÑ…ÉÐ•¹ÑÉ¥•Ì…™Ñ•ÈÍ…Ù¥¹œÑ¡•¥È•á…ÐÙ…±Õ•Ì4(´€¨©!½ÍÑÌ€¡½ÁÑ¥½¹…°¤è¨¨…‘‘Ì€ÄÈÜ¸À¸À¸Ä€¸¸¹€µ…ÁÁ¥¹Ì™½È­¹½Ý¸!@½=58•¹‘Á½¥¹ÑÌ4(´€¨©Y•É¥™¥…Ñ¥½¸è¨¨É•ÅÕ¥É•ÌÑÝ¼½¹Í•ÕÑ¥Ù”ÍÑ…‰±”Í¹…ÁÍ¡½ÑÌ‰•™½É”É•Á½ÉÑ¥¹œÍÕ•ÍÌ4(4)I½±±‰…¬ÍÑ…Ñ”¥ÌÍ…Ù•‰•™½É”ÍÑ…ÉÑÕÀÍ•ÑÑ¥¹Ì…É”¡…¹•¸4(4(´´´4(4(ŒŒƒŠfï¾â<]¡…ÐƒŠq¥Í…‰±”ÍÉ¥ÁÑÏŠt‘½•Ì4(4)]¡•¸å½ÔÉÕ¸€¨©¥Í…‰±”ÍÉ¥ÁÑÌ¨¨°Ñ¡”Ñ½½°è4(4(´I•ÍÑ½É•ÌÑ¡”•á…ÐÍ…Ù•Í•ÉÙ¥”ÍÑ…ÉÑÕÀ…¹ÉÕ¹¹¥¹œÍÑ…Ñ•Ì°¥¹±Õ‘¥¹œ•±…å•ÕÑ¼MÑ…ÉÐ4(´I•ÍÑ½É•ÌÍ…Ù•Ñ…Í¬•¹…‰±•ÍÑ…Ñ•Ì…¹IÕ¸µ•¹ÑÉäÙ…±Õ•Ì4(´Y•É¥™¥•ÌÍÑ…ÉÑÕÀÉ•ÍÑ½É…Ñ¥½¸‰•™½É”É•µ½Ù¥¹œ¹•ÑÝ½É¬ÁÉ½Ñ•Ñ¥½¸4(´I•µ½Ù•Ì½¹±ä™¥É•Ý…±°ÉÕ±•Ì…¹¡½ÍÑÌ•¹ÑÉ¥•Ì½Ý¹•‰äÑ¡¥ÌÑ½½°4(´-••ÁÌÑ¡”É½±±‰…¬™¥±”…¹¹•ÑÝ½É¬ÁÉ½Ñ•Ñ¥½¸¥˜É•ÍÑ½É…Ñ¥½¸¥Ì¥¹½µÁ±•Ñ”4(4(´´´4(4(ŒŒƒÂ~žÄ¥É•Ý…±°ÉÕ±•Ì…É”ÕÁ‘…Ñ”µÍ…™”€¡¥µÁ½ÉÑ…¹Ð¤4(4)=58…µ¥¹œ!ÕˆÕÁ‘…Ñ•Ì€¡•ÍÁ•¥…±±ä™É½´5¥É½Í½™ÐMÑ½É”¤…¸¡…¹”¥¹Ñ•É¹…°Á…Ñ¡Ì½•á•ÕÑ…‰±•Ì¸4(4)Q¡”ÁÉ¥µ…Éä™¥É•Ý…±°ÉÕ±”¥Ì‰½Õ¹Ñ¼Ñ¡”ÍÑ…‰±”MÑ½É”Á…­…”M%É…Ñ¡•ÈÑ¡…¸„Ù•ÉÍ¥½¹•¥¹ÍÑ…±±…Ñ¥½¸Á…Ñ ¸=¸•… …Ñ¥Ù…Ñ¥½¸Ñ¡”Ñ½½°…±Í¼è4(4(´­••ÁÌÑ¡”Á…­…”ÉÕ±”…Ñ¥Ù”Ý¡¥±”Á…Ñ ÉÕ±•Ì…É”É•™É•Í¡•4(´É”µ‘¥Í½Ù•ÉÌÁ…­…”…¹•áÑ•É¹…°=58•á•ÕÑ…‰±•Ì4(´É•µ½Ù•Ì½‰Í½±•Ñ”Á…Ñ ÉÕ±•Ì4(´Ù•É¥™¥•Ì•Ù•ÉäÕÉÉ•¹Ð•á•ÕÑ…‰±”¡…Ì…¸•¹…‰±•½ÕÑ‰½Õ¹‰±½¬ÉÕ±”4(4)Q¡¥Ì…Ù½¥‘ÌÑ¡”Õ¹ÁÉ½Ñ•Ñ•Ý¥¹‘½ÜÑ¡…ÐÁÉ•Ù¥½ÕÍ±ä•á¥ÍÑ•‰•ÑÝ••¸ÁÁ`É•Í•Ð…¹™¥É•Ý…±°É•™É•Í ¸4(4(´´´4(4(ŒŒƒÂ~ž€]¡…ÐÑ¡¥ÌÑ½½°¥Ì€¡…¹¥Ì¹½Ð¤4(4+ŠrQ¡¥ÌÑ½½°è4(4(´‘½•Ì€¨©¹½Ð¨¨Õ¹¥¹ÍÑ…±°=58…µ¥¹œ!Õˆ4(´‘½•Ì€¨©¹½Ð¨¨É•µ½Ù”‘É¥Ù•ÉÌ½È½É”]¥¹‘½ÝÌ½µÁ½¹•¹ÑÌ4(´½¹±äÑ½Õ¡•Ìè4(€€´ÍÑ…ÉÑÕÀÑåÁ”½˜Í•±•Ñ•Í•ÉÙ¥•Ì4(€€´Í•±•Ñ•Í¡•‘Õ±•Ñ…Í­Ì4(€€´!@½=58IÕ¸•¹ÑÉ¥•Ì¥¸Ñ¡”É•¥ÍÑÉä4(€€´½ÁÑ¥½¹…°™¥É•Ý…±°ÉÕ±•ÌÉ•…Ñ•‰äÑ¡¥ÌÑ½½°4(€€´½ÁÑ¥½¹…°¡½ÍÑÌ•¹ÑÉ¥•ÌÉ•…Ñ•‰äÑ¡¥ÌÑ½½°4(4+Šv0Q¡¥ÌÑ½½°¥Ì¹½Ðè4(4(´„É…¬€¼Á…Ñ €¼Á•Éµ…¹•¹ÐƒŠqÉ•¥½¸¡…¹•ËŠt4(´„5¥É½Í½™ÐMÑ½É”É•¥½¸‰åÁ…ÍÌ‰ä¥ÑÍ•±˜4(4(´´´4(4(ŒŒƒÂ~ž¤Q•¡¹¥…°½Ù•ÉÙ¥•Ü€¡™½È‘•ÙÌ¤4(4(´€¨©1…¹Õ…”è¨¨Œ4(´€¨©IÕ¹Ñ¥µ”è¨¨€¨¨¹9P€ÄÀ¨¨4(´€¨©ÁÀÑåÁ”è¨¨½¹Í½±”…ÁÀ€¡]¥¹‘½ÝÌ€ÄÀ¼ÄÄ¤°Í¥¹±”µ™¥±”Á½ÉÑ…‰±”‰Õ¥±4(´€¨©±•Ù…Ñ¥½¸è¨¨UÍ•Ì…¸…ÁÁ±¥…Ñ¥½¸µ…¹¥™•ÍÐÑ¼É•ÅÕ•ÍÐ€¨©‘µ¥¹¥ÍÑÉ…Ñ½È¨¨€¡U¤½¸ÍÑ…ÉÑÕÀ4(´€¨©½É”½Á•É…Ñ¥½¹Ìè¨¨4(€€´Í•ÉÙ¥•Ìµ…¹…•µ•¹ÐÙ¥„]¥¹‘½ÝÌA%Ì€¡…¹Í…™”™…±±‰…­ÌÝ¡•É”¹••‘•¤4(€€´Í¡•‘Õ±•Ñ…Í¬‘¥Í…‰±”…¹ÉÕ¹¹¥¹œµ¥¹ÍÑ…¹”Ñ•Éµ¥¹…Ñ¥½¸4(€€´É•¥ÍÑÉäIÕ¸•¹ÑÉ¥•Ì±•…¹ÕÀ€¡½µµ½¸±½…Ñ¥½¹Ì¤4(€€´Á…­…”µM%…¹•á•ÕÑ…‰±”™¥É•Ý…±°ÉÕ±•ÌÝ¥Ñ =4½A½Ý•ÉM¡•±°™…±±‰…¬…¹Á½ÍÐµÝÉ¥Ñ”Ù•É¥™¥…Ñ¥½¸4(€€´…Ñ½µ¥Œ°•¹½‘¥¹œµÁÉ•Í•ÉÙ¥¹œ¡½ÍÑÌ…¹É½±±‰…¬µÍÑ…Ñ”ÝÉ¥Ñ•Ì4(€€´¥¹Ñ•É…Ñ¥Ù”µÕÍ•ÈÙ…±¥‘…Ñ¥½¸‰•™½É”ÁÁ`½È!-T½Á•É…Ñ¥½¹Ì4(4)Q¡”U$¥Ì‘•Í¥¹•Ñ¼‰”ÁÉ•‘¥Ñ…‰±”è4(´€¨©MÑ…ÑÕÌ¨¨€ôÕÉÉ•¹Ð™…ÑÌ€€4(´€¨©ÉäÉÕ¸¨¨€ôƒŠq]¥±°ƒŠ›ŠtÁÉ•‘¥Ñ¥½¹Ì€€4(´€¨©Ñ¥Ù…Ñ”½¥Í…‰±”¨¨€ô…Ñ¥½¹…‰±”¡…¹•Ì€¬™¥¹…°Í¹…ÁÍ¡½Ð€€4(4(´´´4(4(ŒŒƒÂ~ž¤QÉ½Õ‰±•Í¡½½Ñ¥¹œ4(4(ŒŒŒMµ…ÉÑMÉ••¸èƒŠq]¥¹‘½ÝÌÁÉ½Ñ•Ñ•å½ÕÈAŠt4)A½ÉÑ…‰±”Õ¹Í¥¹•Ñ½½±Ì™É½´¥Ñ!Õˆµ…äÑÉ¥•ÈMµ…ÉÑMÉ••¸è4(4(´±¥¬€¨©5½É”¥¹™¼¨¨ƒŠH€¨©IÕ¸…¹åÝ…ä¨¨4(4(ŒŒŒ=58ÍÑ¥±°™±…Í¡•Ì™½È„Í•½¹½¸±½¥¸4)U]@…ÁÁÌ…¸‰É¥•™±ä¥¹¥Ñ¥…±¥é”‘ÕÉ¥¹œ±½¥¸½ÕÁ‘…Ñ”¡•­Ì¸€€4)%˜Í•ÉÙ¥•Ì½Ñ…Í­Ì…É”Ñ…µ•…¹€¡½ÁÑ¥½¹…±±ä¤¹•ÑÝ½É¬¥Ì‰±½­•°„Í¡½ÉÐ™±¥­•È‘½•Ì¹½Ð¹••ÍÍ…É¥±äµ•…¸¥ÐÁ¡½¹•Ì¡½µ”¸4(4(ŒŒŒM•…É ­•åÝ½É‘Ì€¡¡½ÜÁ•½Á±”ÕÍÕ…±±ä™¥¹Ñ¡¥Ì¤4)%˜å½×ŠeÉ”¡•É”‰•…ÕÍ”½˜½¹”½˜Ñ¡•Í”è4(´ƒŠq=58…µ¥¹œ!Õˆ¹½Ð…Ù…¥±…‰±”¥¸µäÉ•¥½»Št4(´ƒŠq=58…µ¥¹œ!ÕˆÝÉ½¹œÉ•¥½»Št4(´ƒŠq=58…µ¥¹œ!ÕˆÉ•¥½¸±½­•“Št4(´ƒŠq!@=58…µ¥¹œ!Õˆ½Õ¹ÑÉä¹½ÐÍÕÁÁ½ÉÑ•“Št4(´ƒŠq=58…µ¥¹œ!ÕˆYA8Ý½É­…É½Õ¹“Št4(4)Q¡¥ÌÑ½½°¥ÌÍÁ•¥™¥…±±äµ…‘”™½ÈÑ¡”Ý½É­™±½Üè€¨©‰½½ÐƒŠHYA8ƒŠH±…Õ¹ =58µ…¹Õ…±±ä¨¨¸4(4(´´´4(4(ŒŒƒÂ~’tMÕÁÁ½ÉÐÑ¡”ÁÉ½©•Ð()=µ•¹…µ¥¹!Õ‰U¹±½­•È¥Ì™É•”…¹½Á•¸Í½ÕÉ”¸%˜¥Ð¡•±Á•å½Ô­••À=58ÕÍ…‰±”°½¹Í¥‘•ÈÍÕÁÁ½ÉÑ¥¹œ½¹Ñ¥¹Õ•‘•Ù•±½Áµ•¹Ð°½µÁ…Ñ¥‰¥±¥ÑäÑ•ÍÑ¥¹œ°…¹™ÕÑÕÉ”ÕÁ‘…Ñ•Ìè((ñÀ…±¥¸ô‰•¹Ñ•Èˆø(€€ñ„¡É•˜ô‰¡ÑÑÁÌè¼½‰½½ÍÑä¹Ñ¼½…Ù…é‰•¬ÈÈˆø(€€€€ñ¥µœÍÉŒôˆ¹¥Ñ¡Õˆ½…ÍÍ•ÑÌ½‰½½ÍÑäµÍÕÁÁ½ÉÐ¹ÍÙœˆÝ¥‘Ñ ôˆàÀÀˆ…±Ðô‰MÕÁÁ½ÉÐ=µ•¹…µ¥¹!Õ‰U¹±½­•È½¸	½½ÍÑäˆø(€€ð½„ø(ð½Àø()e½Ô…¸…±Í¼ÍÕÁÁ½ÉÐÑ¡”ÁÉ½©•Ð‰ä½¹ÑÉ¥‰ÕÑ¥¹œè((´ƒŠ¶@€¨©MÑ…È¨¨Ñ¡”É•Á½Í¥Ñ½Éä€€(´ƒÂ~6Ð€¨©½É¬¨¨¥Ð…¹…‘…ÁÐ¥ÐÑ¼å½ÕÈ=58µ½‘•°½Í•ÑÕÀ€€4(´ƒÂ~Bl=Á•¸…¸€¨©%ÍÍÕ”¨¨¥˜Í½µ•Ñ¡¥¹œ‰É•…­Ì½È=58¡…¹•Ì¥ÑÌ‰•¡…Ù¥½È€€4(´ƒÂ~RœAIÌ…É”Ý•±½µ”€¡‰•ÑÑ•È‘•Ñ•Ñ¥½¸°Í…™•ÈÉ½±±‰…¬°¹•Ü•¹‘Á½¥¹ÑÌ¤4(4)UÍ”¥Ð°Í¡…É”¥Ð°…¹•¹©½ä„ÅÕ¥•Ñ•È=58•áÁ•É¥•¹”ƒÂ~f04(4(´´´4(4(ŒŒƒÂ~N1¥•¹Í”€¡5%P¤4(