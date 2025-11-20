# Omen Gaming Hub Unlocker

"Small" PowerShell script that **keeps HP OMEN Gaming Hub installed**, but:

* stops it from **auto-starting with Windows** (services, tasks, Run keys)
* optionally blocks **all network access** for every OMEN `.exe` via Windows Firewall

Main idea:
You can still launch OMEN Gaming Hub **manually after enabling a VPN**, but it will not:

* wake up on its own at boot,
* call home and detect that you are in a region like RU,
* reset its state or lock itself before VPN is active.

Tested on **Windows 11** with an **HP OMEN laptop**.

---

## What the script does

1. **Ensures Admin rights**
   If the script is not running elevated, it restarts itself with `runas` (UAC prompt) and exits the non‑admin instance.

2. **Tames HP/OMEN services**
   Finds and sets these and similar services to `Manual`:

   * `HPAppHelperCap` (HP App Helper HSA Service)
   * `HPDiagsCap` (HP Diagnostics HSA Service)
   * `HPNetworkCap` (HP Network HSA Service)
   * `HPOmenCap` (HP Omen HSA Service)
   * `HPSysInfoCap` (HP System Info HSA Service)
   * `HpTouchpointAnalyticsService` (HP Insights / Analytics)

3. **Disables HP/OMEN scheduled tasks**
   Finds and disables tasks whose names/paths match:

   * `*Omen*`, `*OMEN*`
   * `*HP Support Assistant*`, `*HP JumpStart*`, `*HP Wolf*`, `*HP Analytics*`, `*HP HSA*`

   This stops additional auto‑launch attempts from Task Scheduler.

4. **Cleans Run auto‑start registry keys**
   Scans:

   * `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run`
   * `HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run`
   * `HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run`

   and removes values that look like HP/OMEN auto‑start entries.

5. **Optionally blocks OMEN network access via Firewall**
   If `ManageFirewall = $true`:

   * Locates the OMEN UWP package: `AD2F1837.OMENCommandCenter`.
   * Finds **all `.exe` files** under its `InstallLocation`.
   * Creates outbound‑blocking firewall rules for each `.exe`, named like:

     ```text
     Tame-OMEN - HP.Omen.OmenCommandCenter.exe
     Tame-OMEN - OmenBGMonitor.exe
     Tame-OMEN - OmenCommandCenterBackground.exe
     ...
     ```

   This prevents OMEN from talking to the network at all (region checks, telemetry, etc.),
   even if some OMEN window briefly flickers at login.

6. **Keeps the window open at the end**
   After finishing, the script prints a summary and waits for **Enter**, so the user can read the output when it was started with "Run with PowerShell".

---

## Configuration

At the top of `OmenGamingHubUnlocker.ps1` there is a small configuration section:

```powershell
# Apply changes immediately by default
$DryRun             = $false

# If you want to only tame auto-start without blocking network, set this to $false.
$ManageFirewall     = $true

# Prefix for firewall rule display names
$FirewallRulePrefix = "Tame-OMEN"
```

* `DryRun`:

  * `True`  → script only **prints** what it would do, no changes applied.
  * `False` → script **applies** all changes immediately (default here).

* `ManageFirewall`:

  * `True`  → also create firewall rules that block OMEN `.exe` network access.
  * `False` → do **not** touch firewall, only services/tasks/Run.

* `FirewallRulePrefix`:

  * All created rules start with this prefix.
  * Makes it easy to find and remove them later.

---

## Requirements

* Windows 10 or 11
* HP OMEN machine with OMEN Gaming Hub installed
* PowerShell 5+ (built‑in)
* Admin rights (UAC prompt will appear when the script self‑elevates)

---

## How to run (simple, single‑file usage)

1. Download `OmenGamingHubUnlocker.ps1`.
2. Right‑click the file → **Properties** → if there is an **“Unblock”** checkbox, tick it → OK.
3. Right‑click `OmenGamingHubUnlocker.ps1` → **Run with PowerShell**.
4. Approve the UAC prompt.
5. The script will:

   * ensure it runs as Administrator,
   * list found services/tasks/Run entries/OMEN executables,
   * apply changes (if `DryRun = $false`).
6. At the end it will show a summary and wait for you to press **Enter** before closing.
7. Reboot Windows.

---

## After reboot

* OMEN should **not start by itself** at login.
* HP/OMEN helper services should have `Startup type: Manual`.
* HP/OMEN scheduled tasks should be **Disabled**.
* Firewall rules like `Tame-OMEN - <exe>.exe` should exist and block outbound traffic for OMEN executables (if `ManageFirewall = $true`).

Then you can:

1. Boot into Windows.
2. Enable VPN.
3. Launch **OMEN Gaming Hub** manually.

Since firewall rules block OMEN’s network access, it should **not be able to detect your real region on boot** and auto‑lock itself before VPN is active.

If you want OMEN online features back, disable firewall blocking (see below).

---

## Removing firewall rules (unblock OMEN network)

If you later decide to give OMEN network access back, you can delete the firewall rules by prefix.

From an elevated PowerShell:

```powershell
$prefix = "Tame-OMEN"
$rules  = Get-NetFirewallRule -DisplayName "$prefix - *" -ErrorAction SilentlyContinue

if ($rules) {
    $rules | ForEach-Object {
        Write-Host "Removing rule: $($_.DisplayName)"
        Remove-NetFirewallRule -DisplayName $_.DisplayName -ErrorAction SilentlyContinue
    }
} else {
    Write-Host "No firewall rules with prefix '$prefix' found."
}
```

After this, OMEN executables are no longer blocked by these rules.

---

## Encoding note (to avoid parsing issues)

If you edit the script:

* Save it as **ANSI** or **UTF‑8 with BOM**.
* Avoid mixing non‑ASCII characters in comments with "UTF‑8 without BOM" in old editors.

If you see weird characters or `ParserError: TerminatorExpectedAtEndOfString`, it is almost always an encoding problem.

---

## FAQ

### The script says “running scripts is disabled” or “file is not digitally signed”

Run PowerShell as Administrator and:

```powershell
Set-ExecutionPolicy RemoteSigned -Scope CurrentUser
```

Answer `Y`.
If Windows still complains that the file is from the internet:

```powershell
Unblock-File .\OmenGamingHubUnlocker.ps1
```

Then run it again.

---

### OMEN still briefly flickers on the taskbar

This may happen due to UWP startup behavior, but with:

* HP/OMEN services set to `Manual`,
* HP/OMEN tasks disabled,
* firewall rules blocking all OMEN `.exe` outbound traffic,

it should no longer be able to:

* auto-start fully,
* detect your real region before VPN,
* or reset itself based on that.

The flicker becomes a purely visual side effect, not a functional one.

---

### I only want to stop auto-start, but keep network for OMEN

Set:

```powershell
$ManageFirewall = $false
```

Now the script will:

* tame services,
* disable tasks,
* clean Run auto-start,

but will **not** add any firewall rules.

---

### I want a dry-run first

Set:

```powershell
$DryRun = $true
```

Run the script.
It will:

* print all target services, tasks, Run-entries and `.exe` files,
* but will **not** change anything.

If the output looks safe, set `DryRun = $false` and run again.

---

## License

MIT License
