# Tame-OmenAutoStart.ps1
# Goal:
#   Keep HP OMEN Gaming Hub installed, but:
#     - prevent it from auto-starting with Windows
#     - stop HP/OMEN helper services and tasks from auto-starting
#     - optionally block ALL network access for every OMEN .exe via Windows Firewall
#
# How it works:
#   1. Ensures the script is running as Administrator (self-elevates if needed).
#   2. Finds HP/OMEN-related services and sets StartupType to Manual.
#   3. Finds HP/OMEN-related scheduled tasks and disables them.
#   4. Scans common Run registry keys and removes HP/OMEN auto-start entries.
#   5. Finds the OMEN UWP package (AD2F1837.OMENCommandCenter),
#      locates all .exe files inside its InstallLocation
#      and creates outbound-blocking firewall rules for them (if enabled).
#
# Usage (typical for end users):
#   - Right-click this .ps1 file > "Run with PowerShell".
#   - The script will auto-elevate (UAC prompt) and apply changes.
#
# Advanced:
#   - You can toggle the behavior in the CONFIGURATION SECTION below:
#       DryRun        : if $true, show what will happen but do not change anything.
#       ManageFirewall: if $false, skip all firewall rule creation.
#
# Notes:
#   - The script does NOT uninstall OMEN Gaming Hub.
#   - The script does NOT touch drivers or Windows system services.
#   - Services are set to Manual, so OMEN can still start them when you
#     launch OMEN Gaming Hub manually (e.g. after enabling a VPN).
#   - Firewall rules are prefixed with a custom name, so they can be
#     removed later if needed.

# ========================= CONFIGURATION SECTION ============================

# By default we apply changes immediately:
#   - services/tasks/Run are modified
#   - firewall rules are created (if ManageFirewall = $true)
$DryRun             = $false

# If you want to only tame auto-start without blocking network, set this to $false.
$ManageFirewall     = $true

# Prefix for firewall rule display names. Used so rules are easy to find/remove.
$FirewallRulePrefix = "Tame-OMEN"

# ========================= ADMIN ELEVATION CHECK ============================

# This function ensures the script runs as Administrator.
# If not, it restarts itself with elevated privileges and exits the current process.
function Ensure-Admin {
    $currentIdentity  = [Security.Principal.WindowsIdentity]::GetCurrent()
    $currentPrincipal = New-Object Security.Principal.WindowsPrincipal($currentIdentity)
    $isAdmin = $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

    if (-not $isAdmin) {
        Write-Host "Current PowerShell session is not elevated. Restarting as Administrator..." -ForegroundColor Yellow

        $psi = New-Object System.Diagnostics.ProcessStartInfo
        $psi.FileName  = "powershell.exe"
        $psi.Arguments = "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`""
        $psi.Verb      = "runas"   # triggers UAC prompt

        try {
            [System.Diagnostics.Process]::Start($psi) | Out-Null
        }
        catch {
            Write-Host "Failed to restart script as Administrator. Please run it manually as admin." -ForegroundColor Red
        }

        # Exit current (non-admin) instance
        exit
    }
}

Ensure-Admin

# ========================= SCRIPT START =====================================

Write-Host "=== HP OMEN auto-start tamer ===" -ForegroundColor Cyan
Write-Host ("DryRun = {0}" -f $DryRun) -ForegroundColor Yellow
Write-Host ("ManageFirewall = {0}" -f $ManageFirewall) -ForegroundColor Yellow

# ========================= 1. HP / OMEN SERVICES ============================
# This block:
#   - collects known HP/OMEN helper services by ServiceName and DisplayName,
#   - prints them,
#   - sets their StartupType to Manual (if DryRun = $false).

$serviceNames = @(
    "HPAppHelperCap",
    "HPDiagsCap",
    "HPNetworkCap",
    "HPSysInfoCap",
    "HP Comm Recover",
    "HpTouchpointAnalyticsService",
    "HP TechPulse Core"
)

$serviceDisplayPatterns = @(
    "*OMEN*HSA*",
    "HP App Helper HSA Service",
    "HP Diagnostics HSA Service",
    "HP Network HSA Service",
    "HP System Info HSA Service",
    "HP Analytics Service"
)

$services = @()

# Find by exact service names
foreach ($name in $serviceNames) {
    $svc = Get-Service -Name $name -ErrorAction SilentlyContinue
    if ($null -ne $svc) {
        $services += $svc
    }
}

# Find by display name patterns
$allServices = Get-Service -ErrorAction SilentlyContinue
foreach ($svc in $allServices) {
    foreach ($pattern in $serviceDisplayPatterns) {
        if ($svc.DisplayName -like $pattern) {
            $services += $svc
            break
        }
    }
}

# Remove duplicates and sort for stable output
$services = $services | Sort-Object Name -Unique

if ($services.Count -eq 0) {
    Write-Host "No HP/OMEN services found." -ForegroundColor DarkGray
}
else {
    Write-Host "`n--- HP/OMEN services found ---" -ForegroundColor Green
    foreach ($svc in $services) {
        Write-Host ("{0,-30}  ({1})" -f $svc.Name, $svc.DisplayName)
    }

    if (-not $DryRun) {
        Write-Host "`nSetting StartupType to Manual..." -ForegroundColor Yellow
        foreach ($svc in $services) {
            try {
                # We only change StartupType; we do not force-stop running services here.
                Set-Service -Name $svc.Name -StartupType Manual
                Write-Host ("OK: {0} -> Manual" -f $svc.Name)
            }
            catch {
                Write-Host ("SKIP: failed to modify {0}: {1}" -f $svc.Name, $_) -ForegroundColor Red
            }
        }
    }
}

# ========================= 2. SCHEDULED TASKS ===============================
# This block:
#   - finds scheduled tasks whose names/paths indicate HP/OMEN/HSA,
#   - prints them,
#   - disables them (if DryRun = $false), so they no longer auto-start OMEN.

Write-Host "`nScanning HP/OMEN scheduled tasks..." -ForegroundColor Cyan

$taskPatterns = @(
    "*Omen*",
    "*OMEN*",
    "*HP Support Assistant*",
    "*HP JumpStart*",
    "*HP Wolf*",
    "*HP Analytics*",
    "*HP HSA*"
)

$tasks = Get-ScheduledTask -ErrorAction SilentlyContinue | Where-Object {
    $t = $_
    foreach ($p in $taskPatterns) {
        if ($t.TaskName -like $p -or $t.TaskPath -like $p) { return $true }
    }
    return $false
}

if ($tasks.Count -eq 0) {
    Write-Host "No HP/OMEN scheduled tasks found." -ForegroundColor DarkGray
}
else {
    Write-Host "`n--- HP/OMEN scheduled tasks found ---" -ForegroundColor Green
    foreach ($t in $tasks) {
        Write-Host ("{0}{1}" -f $t.TaskPath, $t.TaskName)
    }

    if (-not $DryRun) {
        Write-Host "`nDisabling tasks..." -ForegroundColor Yellow
        foreach ($t in $tasks) {
            try {
                Disable-ScheduledTask -TaskName $t.TaskName -TaskPath $t.TaskPath -ErrorAction SilentlyContinue | Out-Null
                Write-Host ("OK: {0}{1}" -f $t.TaskPath, $t.TaskName)
            }
            catch {
                Write-Host ("SKIP: failed to disable {0}{1}: {2}" -f $t.TaskPath, $t.TaskName, $_) -ForegroundColor Red
            }
        }
    }
}

# ========================= 3. RUN REGISTRY AUTO-START =======================
# This block:
#   - inspects common Run keys (HKLM / HKCU, 32/64-bit),
#   - prints any HP/OMEN-related values,
#   - removes them (if DryRun = $false), so HP/OMEN will not start from Run.

Write-Host "`nScanning Run registry keys for auto-start entries..." -ForegroundColor Cyan

$runKeys = @(
    "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run",
    "HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run",
    "HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run"
)

$runValuePatterns = @(
    "*Omen*",
    "*OMEN*",
    "*HP Command Center*",
    "*HPSystemEventUtility*",
    "*HP Support Assistant*"
)

foreach ($key in $runKeys) {
    if (-not (Test-Path $key)) {
        Write-Host ("Registry key '{0}' does not exist." -f $key) -ForegroundColor DarkGray
        continue
    }

    $props = Get-ItemProperty -Path $key
    foreach ($property in $props.PSObject.Properties) {
        # Skip PowerShell's own technical properties
        if ($property.Name -like "PS*") { continue }

        $value = [string]$property.Value
        $match = $false
        foreach ($pattern in $runValuePatterns) {
            if ($value -like $pattern -or $property.Name -like $pattern) {
                $match = $true
                break
            }
        }

        if ($match) {
            Write-Host ("Found auto-start entry: {0} -> {1} in {2}" -f $property.Name, $value, $key) -ForegroundColor Green
            if (-not $DryRun) {
                try {
                    Remove-ItemProperty -Path $key -Name $property.Name -ErrorAction SilentlyContinue
                    Write-Host ("Removed: {0}" -f $property.Name) -ForegroundColor Yellow
                }
                catch {
                    Write-Host ("SKIP: failed to remove {0}: {1}" -f $property.Name, $_) -ForegroundColor Red
                }
            }
        }
    }
}

# ========================= 4. FIREWALL BLOCK FOR OMEN EXEs ==================
# This block (if ManageFirewall = $true):
#   - locates the OMEN UWP package (AD2F1837.OMENCommandCenter),
#   - finds every .exe inside its InstallLocation,
#   - prints them,
#   - creates outbound-blocking firewall rules for each .exe (if DryRun = $false).
# This prevents OMEN from reaching the network at all (for region checks, telemetry, etc.).

if ($ManageFirewall) {
    Write-Host "`nScanning OMEN Gaming Hub package for executables..." -ForegroundColor Cyan

    $omenPkg = Get-AppxPackage AD2F1837.OMENCommandCenter -ErrorAction SilentlyContinue
    if ($null -eq $omenPkg) {
        Write-Host "OMENCommandCenter package not found. Skipping firewall rules." -ForegroundColor DarkGray
    }
    else {
        $installPath = $omenPkg.InstallLocation
        Write-Host ("OMENCommandCenter InstallLocation: {0}" -f $installPath) -ForegroundColor Green

        if (-not (Test-Path $installPath)) {
            Write-Host "InstallLocation path does not exist. Skipping firewall rules." -ForegroundColor Red
        }
        else {
            $exeFiles = Get-ChildItem -Path $installPath -Filter *.exe -Recurse -ErrorAction SilentlyContinue

            if (-not $exeFiles -or $exeFiles.Count -eq 0) {
                Write-Host "No .exe files found inside OMENCommandCenter package. Skipping firewall rules." -ForegroundColor DarkGray
            }
            else {
                Write-Host "`n--- OMEN .exe files found ---" -ForegroundColor Green
                foreach ($exe in $exeFiles) {
                    Write-Host $exe.FullName
                }

                if (-not $DryRun) {
                    Write-Host "`nCreating outbound-blocking firewall rules for OMEN executables..." -ForegroundColor Yellow

                    foreach ($exe in $exeFiles) {
                        $ruleName = "$FirewallRulePrefix - $($exe.Name)"
                        try {
                            $existing = Get-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue
                            if ($null -ne $existing) {
                                Write-Host ("Firewall rule already exists: {0}" -f $ruleName) -ForegroundColor DarkGray
                                continue
                            }

                            New-NetFirewallRule `
                                -DisplayName $ruleName `
                                -Direction Outbound `
                                -Program $exe.FullName `
                                -Action Block `
                                -Profile Any `
                                -EdgeTraversalPolicy Block | Out-Null

                            Write-Host ("Firewall rule created: {0}" -f $ruleName) -ForegroundColor Green
                        }
                        catch {
                            Write-Host ("SKIP: failed to create firewall rule for {0}: {1}" -f $exe.FullName, $_) -ForegroundColor Red
                        }
                    }
                }
                else {
                    Write-Host "`nDryRun: firewall rules NOT created. Set `$DryRun = `$false to apply them." -ForegroundColor Yellow
                }
            }
        }
    }
}
else {
    Write-Host "`nFirewall management disabled (ManageFirewall = `$false)." -ForegroundColor DarkGray
}

# ========================= SUMMARY ==========================================

Write-Host "`n=== Done. Reboot and check whether OMEN Gaming Hub still auto-starts and still resolves your region. ===" -ForegroundColor Cyan
if ($DryRun) {
    Write-Host "DryRun is True — nothing was changed. If the output looks good," `
        "edit the script, set `$DryRun = `$false and run it again." -ForegroundColor Yellow
}

# Keep the window open when launched via “Run with PowerShell”
Write-Host ""
Write-Host "Press Enter to close this window..." -ForegroundColor Yellow
[void](Read-Host)