Set-StrictMode -Version Latest

function ConvertTo-NormalizedVersion {
    param(
        [Parameter(Mandatory)]
        [string]$Version,

        [ValidateRange(1, 4)]
        [int]$SegmentCount = 4
    )

    $trimmedVersion = $Version.Trim()
    if ($trimmedVersion -notmatch '^\d+(\.\d+){0,3}$') {
        throw "Version '$Version' must contain one to four numeric segments."
    }

    $segments = [Collections.Generic.List[string]]::new()
    foreach ($segment in $trimmedVersion.Split('.')) {
        $segments.Add($segment)
    }

    if ($segments.Count -gt $SegmentCount) {
        throw "Version '$Version' has more than $SegmentCount segments."
    }

    while ($segments.Count -lt $SegmentCount) {
        $segments.Add("0")
    }

    return $segments -join '.'
}

function Get-ProjectReleaseInfo {
    param(
        [Parameter(Mandatory)]
        [string]$ProjectPath
    )

    if (-not (Test-Path -LiteralPath $ProjectPath -PathType Leaf)) {
        throw "Project file not found: $ProjectPath"
    }

    [xml]$project = Get-Content -Raw -LiteralPath $ProjectPath
    $properties = $project.Project.PropertyGroup

    $buildVersion = [string]($properties.Version | Select-Object -First 1)
    $assemblyVersion = [string]($properties.AssemblyVersion | Select-Object -First 1)
    $fileVersion = [string]($properties.FileVersion | Select-Object -First 1)
    $displayVersion = [string]($properties.InformationalVersion | Select-Object -First 1)

    $requiredVersions = @{
        Version = $buildVersion
        AssemblyVersion = $assemblyVersion
        FileVersion = $fileVersion
        InformationalVersion = $displayVersion
    }

    foreach ($entry in $requiredVersions.GetEnumerator()) {
        if ([string]::IsNullOrWhiteSpace($entry.Value)) {
            throw "The project property '$($entry.Key)' is missing."
        }
    }

    $normalizedBuildVersion = ConvertTo-NormalizedVersion -Version $buildVersion
    $normalizedAssemblyVersion = ConvertTo-NormalizedVersion -Version $assemblyVersion
    $normalizedFileVersion = ConvertTo-NormalizedVersion -Version $fileVersion
    $normalizedDisplayVersion = ConvertTo-NormalizedVersion -Version $displayVersion

    if ($normalizedBuildVersion -ne $normalizedFileVersion -or
        $normalizedAssemblyVersion -ne $normalizedFileVersion -or
        $normalizedDisplayVersion -ne $normalizedFileVersion) {
        throw @"
Project versions are inconsistent:
  Version              : $buildVersion
  AssemblyVersion      : $assemblyVersion
  FileVersion          : $fileVersion
  InformationalVersion : $displayVersion
All values must describe the same numeric version.
"@
    }

    return [pscustomobject]@{
        BuildVersion = $buildVersion.Trim()
        DisplayVersion = $displayVersion.Trim()
        PackageVersion = $normalizedFileVersion
        ReleaseTag = "v$($displayVersion.Trim())"
    }
}

function Get-ReleaseArtifactName {
    param(
        [Parameter(Mandatory)]
        [string]$DisplayVersion,

        [Parameter(Mandatory)]
        [ValidateSet("win-x64", "win-arm64")]
        [string]$RuntimeIdentifier
    )

    return "OmenGamingHubUnlocker-v$DisplayVersion-$RuntimeIdentifier.exe"
}
