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
ShortDescription: Manages OMEN Gaming Hub background activity, startup items, and network access on Windows.
Description: >-
  A portable Windows utility for keeping OMEN Gaming Hub installed while
  controlling its background activity and network access. It can stop OMEN
  processes, set related services to Manual, disable scheduled tasks and startup
  entries, block known HP and OMEN network endpoints, inspect the current state,
  reset the app, and restore the original settings. It may help when OMEN Gaming
  Hub stops working after an update or displays region-related errors. This
  project is not affiliated with HP.
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
        throw "WingetCreate submission failed.`n$submissionOutput"
    }

    Write-Host $submissionOutput
    $pullRequestMatch = [regex]::Match(
        $submissionOutput,
        'https://github\.com/microsoft/winget-pkgs/pull/\d+')

    if ($pullRequestMatch.Success) {
        return $pullRequestMatch.Value
    }

    return $null
}

if ($Submit -and $LocalOnly) {
    throw "Use either -Submit or -LocalOnly, not both."
}

Ensure-Command -Name "winget"
Ensure-Command -Name "wingetcreate"

$projectReleaseInfo = Get-ProjectReleaseInfo -ProjectPath $projectPath
$displayVersion = if ([string]::IsNullOrWhiteSpace($Version)) {
    if ($NonInteractive) {
        $projectReleaseInfo.DisplayVersion
    }
    else {
        Read-Optional `
            -Prompt "Release version" `
            -DefaultValue $projectReleaseInfo.DisplayVersion
    }
}
else {
    $Version.Trim()
}

$packageVersion = ConvertTo-NormalizedVersion -Version $displayVersion
$releaseTag = "v$displayVersion"
$release = Get-GitHubRelease -RepositoryName $Repository -ReleaseTag $releaseTag
$installerEntries = Get-ReleaseInstallerEntries `
    -Release $release `
    -DisplayVersion $displayVersion
$releaseDate = ([DateTimeOffset]$release.published_at).UtcDateTime.ToString(
    "yyyy-MM-dd",
    [Globalization.CultureInfo]::InvariantCulture)
$packageExists = Test-PackageExists -Identifier $PackageIdentifier
$publishMode = Resolve-PublishMode `
    -RequestedMode $Mode `
    -PackageExists $packageExists `
    -Identifier $PackageIdentifier

$resolvedOutputDirectory = if ([string]::IsNullOrWhiteSpace($OutDirectory)) {
    Join-Path $repositoryRoot "artifacts\winget\$releaseTag"
}
else {
    [IO.Path]::GetFullPath($OutDirectory)
}

Write-Step -Message "WinGet publication plan"
Write-Host "Mode              : $publishMode"
Write-Host "Package identifier: $PackageIdentifier"
Write-Host "Display version   : $displayVersion"
Write-Host "Package version   : $packageVersion"
Write-Host "Release tag       : $releaseTag"
Write-Host "Release date      : $releaseDate"
Write-Host "Output directory  : $resolvedOutputDirectory"
foreach ($installerEntry in $installerEntries) {
    Write-Host (
        "Installer          : {0} -> {1}" -f
        $installerEntry.Architecture,
        $installerEntry.Url)
}

if ($PlanOnly) {
    return
}

if (Test-Path -LiteralPath $resolvedOutputDirectory) {
    Remove-Item -LiteralPath $resolvedOutputDirectory -Recurse -Force
}
New-Item -ItemType Directory -Path $resolvedOutputDirectory -Force | Out-Null

if ($publishMode -eq "New") {
    $identifierParts = $PackageIdentifier.Split('.')
    $manifestRoot = Join-Path $resolvedOutputDirectory (
        "manifests\{0}\{1}\{2}\{3}" -f
        $identifierParts[0].Substring(0, 1).ToLowerInvariant(),
        $identifierParts[0],
        $identifierParts[1],
        $packageVersion)

    Write-Step -Message "Generating initial manifests"
    New-InitialManifests `
        -ManifestRoot $manifestRoot `
        -Identifier $PackageIdentifier `
        -PackageVersion $packageVersion `
        -ReleaseTag $releaseTag `
        -ReleaseDate $releaseDate `
        -Name $packageName `
        -InstallerEntries $installerEntries
}
else {
    Write-Step -Message "Generating updated manifests"
    Update-ExistingManifests `
        -OutputDirectory $resolvedOutputDirectory `
        -Identifier $PackageIdentifier `
        -PackageVersion $packageVersion `
        -ReleaseTag $releaseTag `
        -ReleaseDate $releaseDate `
        -InstallerEntries $installerEntries
    $manifestRoot = Resolve-ManifestRoot `
        -OutputDirectory $resolvedOutputDirectory `
        -Identifier $PackageIdentifier `
        -PackageVersion $packageVersion
}

Write-Step -Message "Validating manifests"
Invoke-ManifestValidation -ManifestRoot $manifestRoot

$shouldRunInstallTest = $InstallTest
if (-not $NonInteractive -and -not $InstallTest) {
    $installTestAnswer = Read-Optional `
        -Prompt "Run local install and uninstall test? y/N" `
        -DefaultValue "N"
    $shouldRunInstallTest = $installTestAnswer -match '^(y|yes|д|да)$'
}

if ($shouldRunInstallTest) {
    Write-Step -Message "Testing local installation"
    Invoke-ManifestInstallTest `
        -ManifestRoot $manifestRoot `
        -Identifier $PackageIdentifier `
        -Name $packageName `
        -KeepInstalled $KeepTestInstall
}

$shouldSubmit = $Submit
if (-not $NonInteractive -and -not $Submit -and -not $LocalOnly) {
    $submitAnswer = Read-Optional `
        -Prompt "Submit PR to microsoft/winget-pkgs now? Y/n" `
        -DefaultValue "Y"
    $shouldSubmit = $submitAnswer -notmatch '^(n|no|н|нет)$'
}

if (-not $shouldSubmit) {
    Write-Step -Message "Completed locally"
    Write-Host "Manifest path: $manifestRoot"
    return
}

Write-Step -Message "Submitting manifests"
$pullRequestUrl = Submit-Manifests `
    -ManifestRoot $manifestRoot `
    -Identifier $PackageIdentifier `
    -PackageVersion $packageVersion `
    -PublishMode $publishMode

Write-Step -Message "Completed"
if ([string]::IsNullOrWhiteSpace($pullRequestUrl)) {
    Write-Host "PR submitted. Check the WingetCreate output above for its URL."
}
else {
    Write-Host "PR: $pullRequestUrl"
}
Write-Host "Manifest path: $manifestRoot"
