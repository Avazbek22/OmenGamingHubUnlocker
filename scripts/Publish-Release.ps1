[CmdletBinding()]
param(
    [ValidateNotNullOrEmpty()]
    [string]$Configuration = "Release",

    [ValidateNotNullOrEmpty()]
    [string]$ReleaseBranch = "main",

    [switch]$SkipRepositoryChecks
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

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

function Get-ProjectVersion {
    [xml]$project = Get-Content -Raw -LiteralPath $projectPath
    $version = [string]($project.Project.PropertyGroup.Version | Select-Object -First 1)

    if ([string]::IsNullOrWhiteSpace($version)) {
        throw "The project Version property is missing."
    }

    return $version.Trim()
}

if ($SkipRepositoryChecks) {
    Write-Warning "Repository branch and cleanliness checks were explicitly skipped."
}
else {
    Assert-ReleaseRepositoryState
}

Assert-SafeReleasePath

$version = Get-ProjectVersion
$releaseDirectory = Join-Path $releaseRoot "v$version"
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

    $artifactName = "OmenGamingHubUnlocker-v$version-$runtimeIdentifier.exe"
    $artifactPath = Join-Path $releaseDirectory $artifactName
    Copy-Item -LiteralPath $publishedExecutable -Destination $artifactPath
    Get-Item -LiteralPath $artifactPath
}

$checksumLines = foreach ($artifact in $releaseArtifacts) {
    $hash = Get-FileHash -LiteralPath $artifact.FullName -Algorithm SHA256
    "$($hash.Hash.ToLowerInvariant())  $($artifact.Name)"
}

$checksumPath = Join-Path $releaseDirectory "SHA256SUMS.txt"
[IO.File]::WriteAllLines($checksumPath, $checksumLines, [Text.Encoding]::ASCII)

Remove-Item -LiteralPath $stagingDirectory -Recurse -Force

Write-Host
Write-Host "Release v$version is ready:" -ForegroundColor Green
foreach ($artifact in $releaseArtifacts) {
    Write-Host "  $($artifact.Name) ($([Math]::Round($artifact.Length / 1MB, 2)) MiB)"
}
Write-Host "  SHA256SUMS.txt"
Write-Host
Get-Content -LiteralPath $checksumPath
