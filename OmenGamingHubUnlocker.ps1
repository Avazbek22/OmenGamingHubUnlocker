param()

# ============================================================
#  CONFIGURATION
# ============================================================

# If $true – only print actions, do not change anything
$DryRun             = $false

# If $true – manage Windows Firewall rules for OMEN executables
$ManageFirewall     = $true

# Prefix for firewall rule DisplayName, for easy clean-up
$FirewallRulePrefix = "Tame-OMEN"

# If $true – add entries to hosts file to block known OMEN / HP endpoints
$ManageHosts        = $true

# Domains that will be mapped to 127.0.0.1 in hosts when $ManageHosts is $true
$HostsDomainsToBlock = @(
    "hpbp.io",
    "api.hpbp.io",
    "hpgamestream.com",
    "content.hpgamestream.com"
)

# ============================================================
#  ELEVATE TO ADMIN IF NEEDED
# ============================================================

$currIdentity  = [Security.Principal.WindowsIdentity]::GetCurrent()
$currPrincipal = New-Object Security.Principal.WindowsPrincipal($currIdentity)
$isAdmin       = $currPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

if (-not $isAdmin) {
    Write-Host ""
    Write-Host "Restarting script as Administrator..." -ForegroundColor Yellow

    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName  = "powershell.exe"
    $psi.Arguments = "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`""
    $psi.Verb      = "runas"

    try {
        [System.Diagnostics.Process]::Start($psi) | Out-Null
    } catch {
        Write-Host "Failed to restart script as Administrator: $($_.Exception.Message)" -ForegroundColor Red
    }

    exit
}

# ============================================================
#  OUTPUT HELPERS
# ============================================================

function Write-Section {
    param([string]$Text)
    Write-Host ""
    Write-Host "=== $Text ===" -ForegroundColor Cyan
}

function Write-Info {
    param([string]$Text)
    Write-Host "    $Text"
}

function Write-Success {
    param([string]$Text)
    Write-Host "[OK]   $Text" -ForegroundColor Green
}

function Write-WarningMessage {
    param([string]$Text)
    Write-Host "[WARN] $Text" -ForegroundColor Yellow
}

function Write-ErrorMessage {
    param([string]$Text)
    Write-Host "[ERR]  $Text" -ForegroundColor Red
}

$Summary = [System.Collections.Generic.List[string]]::new()

# ============================================================
#  SERVICES: SET TO MANUAL
# ============================================================

function Set-OmenServicesToManual {

    Write-Section "Services: set HP / OMEN services to Manual"

    $servicePatterns = @(
        "*OMEN*",
        "*Omen*",
        "*HP Gaming*",
        "*HPGame*",
        "*HP OMEN*"
    )

    try {
        $services = Get-Service | Where-Object {
            $svc = $_
            $matched = $false
            foreach ($pattern in $servicePatterns) {
                if ($svc.Name -like $pattern -or $svc.DisplayName -like $pattern) {
                    $matched = $true
                    break
                }
            }
            return $matched
        }
    } catch {
        Write-WarningMessage "Could not query services: $($_.Exception.Message)"
        return
    }

    if (-not $services) {
        Write-Info "No matching HP / OMEN services found."
        return
    }

    foreach ($svc in $services | Sort-Object Name -Unique) {
        $name        = $svc.Name
        $displayName = $svc.DisplayName
        $info        = "$name ($displayName)"

        if ($svc.StartType -eq "Manual") {
            Write-Info "Service already Manual: $info"
            continue
        }

        Write-Info "Setting service to Manual: $info"

        if (-not $DryRun) {
            try {
                Set-Service -Name $name -StartupType Manual -ErrorAction Stop
                $Summary.Add("Service set to Manual: $info") | Out-Null
                Write-Success "Service set to Manual: $info"
            } catch {
                Write-WarningMessage "Failed to change service $info. Error: $($_.Exception.Message)"
            }
        }
    }
}

# ============================================================
#  SCHEDULED TASKS: DISABLE
# ============================================================

function Disable-OmenScheduledTasks {

    Write-Section "Scheduled tasks: disable HP / OMEN tasks"

    $taskPatterns = @(
        "*Omen*",
        "*OMEN*",
        "*HP.OMEN*",
        "*OMEN Gaming*",
        "*HP Support Assistant*"
    )

    try {
        $tasks = Get-ScheduledTask | Where-Object {
            $t = $_
            $matched = $false
            foreach ($pattern in $taskPatterns) {
                if ($t.TaskName -like $pattern -or $t.TaskPath -like $pattern) {
                    $matched = $true
                    break
                }
            }
            return $matched
        }
    } catch {
        Write-WarningMessage "Could not query scheduled tasks: $($_.Exception.Message)"
        return
    }

    if (-not $tasks) {
        Write-Info "No matching HP / OMEN scheduled tasks found."
        return
    }

    foreach ($task in $tasks | Sort-Object TaskPath, TaskName -Unique) {
        $fullName = "$($task.TaskPath)$($task.TaskName)"

        Write-Info "Disabling task: $fullName"

        if (-not $DryRun) {
            try {
                Disable-ScheduledTask -TaskName $task.TaskName -TaskPath $task.TaskPath -ErrorAction Stop | Out-Null
                $Summary.Add("Scheduled task disabled: $fullName") | Out-Null
                Write-Success "Scheduled task disabled: $fullName"
            } catch {
                Write-WarningMessage "Failed to disable task $fullName. Error: $($_.Exception.Message)"
            }
        }
    }
}

# ============================================================
#  REGISTRY RUN ENTRIES: REMOVE HP / OMEN AUTOSTART
# ============================================================

function Clean-OmenRunEntries {

    Write-Section "Registry: remove HP / OMEN Run auto-start entries"

    $runKeys = @(
        "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run",
        "HKLM:\Software\Microsoft\Windows\CurrentVersion\Run"
    )

    $namePatterns = @(
        "*OMEN*",
        "*Omen*",
        "*HPGaming*",
        "*HP Gaming*",
        "*OMENCommandCenter*",
        "*OCC*",
        "*HP Support Assistant*"
    )

    foreach ($keyPath in $runKeys) {
        $key = Get-Item $keyPath -ErrorAction SilentlyContinue
        if (-not $key) {
            continue
        }

        $valueNames = $key.GetValueNames()
        if (-not $valueNames) {
            continue
        }

        foreach ($valueName in $valueNames) {
            $valueData = $key.GetValue($valueName)

            $match = $false
            foreach ($pattern in $namePatterns) {
                if ($valueName -like $pattern) {
                    $match = $true
                    break
                }

                if ($valueData -is [string] -and $valueData -like $pattern) {
                    $match = $true
                    break
                }
            }

            if ($match) {
                Write-Info "Removing Run entry '$valueName' from $keyPath"

                if (-not $DryRun) {
                    try {
                        Remove-ItemProperty -Path $keyPath -Name $valueName -ErrorAction Stop
                        $Summary.Add("Removed Run entry: $keyPath -> $valueName") | Out-Null
                        Write-Success "Removed Run entry: $keyPath -> $valueName"
                    } catch {
                        Write-WarningMessage "Failed to remove Run entry '$valueName' from '$keyPath'. Error: $($_.Exception.Message)"
                    }
                }
            }
        }
    }
}

# ============================================================
#  EXECUTABLE DISCOVERY: FIND ALL OMEN-RELATED .EXE FILES
# ============================================================

function Get-OmenExecutables {

    Write-Section "Discovery: find OMEN executables for firewall rules"

    $executables = New-Object System.Collections.Generic.HashSet[string]

    # 1) UWP packages: OMEN Gaming Hub, OMEN Command Center, etc.
    $appxFilters = @(
        "*OMENCommandCenter*",
        "*OMENGamingHub*",
        "*OMEN*Gaming*Hub*",
        "*HPInc.OMEN*",
        "*HPInc.HPOmenGamingHub*",
        "*HPOMEN*"
    )

    try {
        $appxPackages = Get-AppxPackage | Where-Object {
            $pkg = $_
            $matched = $false
            foreach ($pattern in $appxFilters) {
                if ($pkg.Name -like $pattern -or $pkg.PackageFamilyName -like $pattern) {
                    $matched = $true
                    break
                }
            }
            return $matched
        }
    } catch {
        Write-WarningMessage "Could not query AppX packages. Error: $($_.Exception.Message)"
        $appxPackages = @()
    }

    foreach ($pkg in $appxPackages) {
        if (-not $pkg.InstallLocation -or -not (Test-Path $pkg.InstallLocation)) {
            continue
        }

        Write-Info "Scanning AppX package: $($pkg.Name) at $($pkg.InstallLocation)"

        try {
            $exes = Get-ChildItem -Path $pkg.InstallLocation -Recurse -Include *.exe -File -ErrorAction SilentlyContinue
            foreach ($exe in $exes) {
                [void]$executables.Add($exe.FullName)
            }
        } catch {
            Write-WarningMessage "Failed to scan '$($pkg.InstallLocation)'. Error: $($_.Exception.Message)"
        }
    }

    # 2) Classic Program Files locations (fallback / additional)
    $programDirs = @()

    if ($env:ProgramFiles) {
        $programDirs += (Join-Path $env:ProgramFiles "HP\OMEN Gaming Hub")
        $programDirs += (Join-Path $env:ProgramFiles "HP Inc\OMEN Gaming Hub")
        $programDirs += (Join-Path $env:ProgramFiles "HP\OMENCommandCenter")
    }

    if ($env:ProgramFiles -and $env:ProgramFiles -ne $env:ProgramFilesx86) {
        $programDirs += (Join-Path ${env:ProgramFiles(x86)} "HP\OMEN Gaming Hub")
        $programDirs += (Join-Path ${env:ProgramFiles(x86)} "HP Inc\OMEN Gaming Hub")
        $programDirs += (Join-Path ${env:ProgramFiles(x86)} "HP\OMENCommandCenter")
    }

    foreach ($dir in $programDirs | Sort-Object -Unique) {
        if (-not (Test-Path $dir)) {
            continue
        }

        Write-Info "Scanning Program Files directory: $dir"

        try {
            $exes = Get-ChildItem -Path $dir -Recurse -Include *.exe -File -ErrorAction SilentlyContinue
            foreach ($exe in $exes) {
                [void]$executables.Add($exe.FullName)
            }
        } catch {
            Write-WarningMessage "Failed to scan '$dir'. Error: $($_.Exception.Message)"
        }
    }

    if ($executables.Count -eq 0) {
        Write-WarningMessage "No OMEN executables were found. Firewall rules will not be created."
    } else {
        Write-Success "Found $($executables.Count) executable(s) to use in firewall rules."
    }

    return $executables
}

# ============================================================
#  FIREWALL: REMOVE OLD RULES AND CREATE NEW ONES
# ============================================================

function Update-OmenFirewallRules {

    if (-not $ManageFirewall) {
        Write-Section "Firewall: skipped (ManageFirewall = false)"
        return
    }

    Write-Section "Firewall: refresh rules for OMEN executables"

    # 1) Remove existing rules with our prefix in DisplayName
    $displayNamePattern = "$FirewallRulePrefix - *"

    try {
        $existingRules = Get-NetFirewallRule -DisplayName $displayNamePattern -ErrorAction SilentlyContinue
    } catch {
        Write-WarningMessage "Could not query existing firewall rules. Error: $($_.Exception.Message)"
        $existingRules = @()
    }

    if ($existingRules) {
        Write-Info "Removing existing firewall rules with prefix '$FirewallRulePrefix - '"

        foreach ($rule in $existingRules) {
            Write-Info "Removing rule: $($rule.DisplayName)"

            if (-not $DryRun) {
                try {
                    Remove-NetFirewallRule -Name $rule.Name -ErrorAction Stop
                    $Summary.Add("Removed firewall rule: $($rule.DisplayName)") | Out-Null
                    Write-Success "Removed firewall rule: $($rule.DisplayName)"
                } catch {
                    Write-WarningMessage "Failed to remove firewall rule '$($rule.DisplayName)'. Error: $($_.Exception.Message)"
                }
            }
        }
    } else {
        Write-Info "No existing firewall rules with prefix '$FirewallRulePrefix - ' were found."
    }

    # 2) Discover current executables and create new rules
    $executables = Get-OmenExecutables

    if ($executables.Count -eq 0) {
        return
    }

    foreach ($exePath in $executables | Sort-Object) {
        $fileName    = [System.IO.Path]::GetFileName($exePath)
        $displayName = "$FirewallRulePrefix - $fileName"

        Write-Info "Creating outbound block rule for: $fileName ($exePath)"

        if (-not $DryRun) {
            try {
                New-NetFirewallRule `
                    -DisplayName $displayName `
                    -Direction Outbound `
                    -Program $exePath `
                    -Action Block `
                    -Profile Any `
                    -Enabled True `
                    -ErrorAction Stop | Out-Null

                $Summary.Add("Firewall: blocked outbound traffic for $fileName") | Out-Null
                Write-Success "Created firewall rule: $displayName"
            } catch {
                Write-WarningMessage "Failed to create firewall rule for '$exePath'. Error: $($_.Exception.Message)"
            }
        }
    }
}

# ============================================================
#  HOSTS: OPTIONAL DOMAIN BLOCKING
# ============================================================

function Update-HostsFile {

    if (-not $ManageHosts) {
        Write-Section "hosts: skipped (ManageHosts = false)"
        return
    }

    Write-Section "hosts: block known OMEN / HP endpoints"

    $hostsPath = Join-Path $env:SystemRoot "System32\drivers\etc\hosts"

    if (-not (Test-Path $hostsPath)) {
        Write-WarningMessage "hosts file was not found at '$hostsPath'."
        return
    }

    try {
        $hostsContent = Get-Content -Path $hostsPath -ErrorAction Stop
    } catch {
        Write-WarningMessage "Could not read hosts file. Error: $($_.Exception.Message)"
        return
    }

    foreach ($domain in $HostsDomainsToBlock) {
        $escapedDomain = [Regex]::Escape($domain)
        $regex         = "^\s*\d{1,3}(\.\d{1,3}){3}\s+$escapedDomain(\s|$)"

        $alreadyPresent = $false
        foreach ($line in $hostsContent) {
            if ($line -match $regex) {
                $alreadyPresent = $true
                break
            }
        }

        if ($alreadyPresent) {
            Write-Info "Domain already present in hosts: $domain"
            continue
        }

        $newLine = "127.0.0.1`t$domain`t# OmenGamingHubUnlocker"

        Write-Info "Adding hosts entry: $newLine"

        if ($DryRun) {
            continue
        }

        $maxRetries = 3
        $delayMs    = 300

        $added = $false

        for ($attempt = 1; $attempt -le $maxRetries -and -not $added; $attempt++) {
            try {
                Add-Content -Path $hostsPath -Value $newLine -ErrorAction Stop
                $added = $true
            } catch [System.IO.IOException] {
                if ($attempt -lt $maxRetries) {
                    Write-WarningMessage "hosts file is locked by another process (attempt $attempt of $maxRetries). Retrying..."
                    Start-Sleep -Milliseconds $delayMs
                } else {
                    Write-WarningMessage "hosts file is locked by another process. Giving up on domain '$domain'."
                }
            } catch {
                Write-WarningMessage "Failed to add hosts entry for '$domain'. Error: $($_.Exception.Message)"
                break
            }
        }

        if ($added) {
            $Summary.Add("hosts: $domain mapped to 127.0.0.1") | Out-Null
            Write-Success "Added hosts entry for domain: $domain"
            # keep in-memory copy in sync for next iterations
            $hostsContent += $newLine
        } else {
            $Summary.Add("hosts: FAILED to map $domain to 127.0.0.1") | Out-Null
        }
    }
}

# ============================================================
#  MAIN FLOW
# ============================================================

Write-Section "Omen Gaming Hub Unlocker"
Write-Info "DryRun             = $DryRun"
Write-Info "ManageFirewall     = $ManageFirewall"
Write-Info "FirewallRulePrefix = $FirewallRulePrefix"
Write-Info "ManageHosts        = $ManageHosts"
Write-Host ""

Set-OmenServicesToManual
Disable-OmenScheduledTasks
Clean-OmenRunEntries
Update-OmenFirewallRules
Update-HostsFile

# ============================================================
#  SUMMARY
# ============================================================

Write-Section "Summary"

if ($DryRun) {
    Write-WarningMessage "DryRun is enabled. No changes were actually applied."
}

if ($Summary.Count -eq 0) {
    Write-WarningMessage "No changes were recorded. Either nothing matched, or everything was already configured."
} else {
    foreach ($item in $Summary) {
        Write-Success $item
    }
}

Write-Host ""
[void] (Read-Host "Press Enter to exit...")
