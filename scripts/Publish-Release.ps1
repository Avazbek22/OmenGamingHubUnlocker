[CmdletBinding()]
param(
    [ValidateNotNullOrEmpty()]
    [string]$Configuration = "Release",

    [ValidateNotNullOrEmpty()]
    [string]$ReleaseBranch = "main",

    [switch]$SkipRepositoryChecks,

    [switch]$ValidateConfigurationOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "release-helpers.ps1")

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$solutionPath = Join-Path $repositoryRoot "OmenGamingHubUnlocker.sln"
$projectPath = Join-Path $repositoryRoot "OmenGamingHubUnlocker\OmenGamingHubUnlocker.csproj"
$artifactsRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot "artifacts"))
$releaseRoot = [IO.Path]::GetFullPath((Join-Path $artifactsRoot "release"))
$runtimeIdentifiers = @("win-x64", "win-arm64")

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
}

function Get-NativeOutput {
    param(
        [Parameter(Mandatory)]
        [string]$FilePath,

        [Parameter(Mandatory)]
        [string[]]$Arguments
    )

    $output = & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Command '$FilePath' failed with exit code $LASTEXITCODE."
    }

    return ($output -join "`n").Trim()
}

function Assert-ReleaseRepositoryState {
    $currentBranch = Get-NativeOutput "git" @("-C", $repositoryRoot, "branch", "--show-current")
    if ($currentBranch -ne $ReleaseBranch) {
        throw "Release builds must run from '$ReleaseBranch'. Current branch: '$currentBranch'."
    }

    $changes = @(& git -C $repositoryRoot status --porcelain --untracked-files=all)
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to inspect the Git working tree."
    }

    if ($changes.Count -gt 0) {
        throw "Release builds require a clean working tree.`n$($changes -join "`n")"
    }

    Invoke-NativeCommand "git" @("-C", $repositoryRoot, "fetch", "--quiet", "origin", $ReleaseBranch)

    $headCommit = Get-NativeOutput "git" @("-C", $repositoryRoot, "rev-parse", "HEAD")
    $remoteCommit = Get-NativeOutput "git" @("-C", $repositoryRoot, "rev-parse", "origin/$ReleaseBranch")
    if ($headCommit -ne $remoteCommit) {
        throw "Local '$ReleaseBranch' must exactly match 'origin/$ReleaseBranch' before publishing."
    }
}

function Assert-SafeReleasePath {
    $expectedPrefix = $artifactsRoot.TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar

    if (-not $releaseRoot.StartsWith($expectedPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Release output must remain inside '$artifactsRoot'."
    }
}

$versionInfo = Get-ProjectReleaseInfo -ProjectPath $projectPath

if ($ValidateConfigurationOnly) {
    Write-Host "Release configuration is valid:" -ForegroundColor Green
    Write-Host "  Build version   : $($versionInfo.BuildVersion)"
    Write-Host "  Display version : $($versionInfo.DisplayVersion)"
    Write-Host "  Package version : $($versionInfo.PackageVersion)"
    Write-Host "  Release tag     : $($versionInfo.ReleaseTag)"
    return
}

if ($SkipRepositoryChecks) {
    Write-Warning "Repository branch and cleanliness checks were explicitly skipped."
}
else {
    Assert-ReleaseRepositoryState
}

Assert-SafeReleasePath

$displayVersion = $versionInfo.DisplayVersion
$releaseDirectory = Join-Path $releaseRoot $versionInfo.ReleaseTag
$stagingDirectory = Join-Path $releaseRoot ".staging"

if (Test-Path -LiteralPath $releaseRoot) {
    Remove-Item -LiteralPath $releaseRoot -Recurse -Force
}

New-Item -ItemType Directory -Path $releaseDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $stagingDirectory -Force | Out-Null

Invoke-NativeCommand "dotnet" @(
    "test",
    $solutionPath,
    "--configuration", $Configuration,
    "--nologo"
)

$releaseArtifacts = foreach ($runtimeIdentifier in $runtimeIdentifiers) {
    $publishDirectory = Join-Path $stagingDirectory $runtimeIdentifier

    Invoke-NativeCommand "dotnet" @(
        "publish",
        $projectPath,
        "--configuration", $Configuration,
        "--runtime", $runtimeIdentifier,
        "--self-contained", "true",
        "--output", $publishDirectory,
        "--nologo",
        "-p:PublishSingleFile=true",
        "-p:IncludeNativeLibrariesForSelfExtract=true",
        "-p:EnableCompressionInSingleFile=true",
        "-p:DebugType=None",
        "-p:DebugSymbols=false",
        "-p:ContinuousIntegrationBuild=true"
    )

    $publishedExecutable = Join-Path $publishDirectory "OmenGamingHubUnlocker.exe"
    if (-not (Test-Path -LiteralPath $publishedExecutable -PathType Leaf)) {
        throw "The $runtimeIdentifier single-file executable was not produced."
    }

    $artifactName = Get-ReleaseArtifactName `
        -DisplayVersion $displayVersion `
        -RuntimeIdentifier $runtimeIdentifier
    $artifactPath = Join-Path $releaseDirectory $artifactName
    Copy-Item -LiteralPath $publishedExecutable -Destination $artifactPath
    $artifact = Get-Item -LiteralPath $artifactPath

    if ($artifact.VersionInfo.FileVersion -ne $versionInfo.PackageVersion) {
        throw "Unexpected FileVersion in '$artifactName': $($artifact.VersionInfo.FileVersion)."
    }

    if ($artifact.VersionInfo.ProductVersion -ne $displayVersion) {
        throw "Unexpected ProductVersion in '$artifactName': $($artifact.VersionInfo.ProductVersion)."
    }

    $artifact
}

$checksumLines = foreach ($artifact in $releaseArtifacts) {
    $hash = Get-FileHash -LiteralPath $artifact.FullName -Algorithm SHA256
    "$($hash.Hash.ToLowerInvariant())  $($artifact.Name)"
}

$checksumPath = Join-Path $releaseDirectory "SHA256SUMS.txt"
[IO.File]::WriteAllLines($checksumPath, $checksumLines, [Text.Encoding]::ASCII)

Remove-Item -LiteralPath $stagingDirectory -Recurse -Force

Write-Host
Write-Host "Release $($versionInfo.ReleaseTag) is ready:" -ForegroundColor Green
foreach ($artifact in $releaseArtifacts) {
    Write-Host "  $($artifact.Name) ($([Math]::Round($artifact.Length / 1MB, 2)) MiB)"
}
Write-Host "  SHA256SUMS.txt"
Write-Host
Get-Content -LiteralPath $checksumPath
