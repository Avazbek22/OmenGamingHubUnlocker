# Omen Gaming Hub Unlocker

Small helper script for HP OMEN laptops and desktops.

It **keeps OMEN Gaming Hub installed**, but:

* stops it from **auto‑starting with Windows**
* turns HP/OMEN helper services and tasks to "Manual" / disabled
* removes HP/OMEN auto‑start entries from the registry
* can (optionally) **block all OMEN .exe files from going online** via Windows Firewall
* can (optionally) **block known HP/OMEN endpoints** via the Windows `hosts` file

So you can:

1. Boot Windows
2. Turn on your VPN
3. Launch **OMEN Gaming Hub** manually

…without OMEN waking up on its own, pinging HP servers first and deciding you are in a "wrong" region.

Tested on **Windows 11** with an **HP OMEN laptop**.

---

## What the script actually does (short version)

* **Runs as Administrator**
  If you start it without admin rights, it will relaunch itself with UAC and continue there.

* **HP / OMEN services → Manual**
  Finds typical HP/OMEN telemetry and helper services and changes their startup type to `Manual`, so they no longer auto‑start on boot.

* **HP / OMEN scheduled tasks → Disabled**
  Looks for tasks with names like `*Omen*`, `*OMEN*`, `*HP Support Assistant*`, etc., and disables them.

* **Cleans Run auto‑start entries**
  Removes HP/OMEN entries from the classic `Run` registry keys (machine + current user), so OMEN is not launched from there.

* **(Optional) Blocks OMEN network access via Windows Firewall**
  Finds the `OMENCommandCenter` UWP package, collects all `.exe` files inside (and some additional classic locations) and creates outbound blocking rules in Windows Firewall for each one.

* **(Optional) Blocks known HP/OMEN endpoints via hosts**
  If enabled, adds entries like `127.0.0.1 hpbp.io` to the system `hosts` file to stop OMEN from talking to specific telemetry / region‑check endpoints.

* **Shows a summary and waits for Enter**
  So you can read what happened when running via the launcher.

---

## Files in this repo

* **`OmenGamingHubUnlocker.ps1`** – main PowerShell script.
* **`Run.bat`** – simple launcher that starts the script with a safe `ExecutionPolicy Bypass` (recommended for most users).

---

## Quick start (recommended way)

1. Download both files:

   * `OmenGamingHubUnlocker.ps1`
   * `Run-OmenGamingHubUnlocker.cmd`
2. Put them in the same folder (for example, on your Desktop).
3. Make sure OMEN Gaming Hub is **closed** (no OMEN window and no background OMEN icons in the tray if possible).
4. Right‑click `Run-OmenGamingHubUnlocker.cmd` → **Run as administrator**.
   (Or double‑click and then approve the UAC dialog.)
5. The script will:

   * restart itself as admin if needed,
   * check for running OMEN processes and offer to close them,
   * list found HP/OMEN services, tasks, Run entries and OMEN executables,
   * apply the changes (and optionally set up firewall/hosts rules).
6. Press **Enter** to close the window when it says it is done.
7. Reboot Windows.

That’s it.

---

## Optional: configuration

At the top of `OmenGamingHubUnlocker.ps1` you can tweak a small config section:

```powershell
$DryRun             = $false  # if true, only print actions, do not change anything
$ManageFirewall     = $true   # if true, block OMEN .exe outbound traffic via Windows Firewall
$FirewallRulePrefix = "Tame-OMEN"  # prefix for created firewall rules
$ManageHosts        = $true   # if true, map known HP/OMEN endpoints to 127.0.0.1 in hosts
```

Typical setups:

* **Full lock‑down (maximum isolation)**

  ```powershell
  $DryRun         = $false
  $ManageFirewall = $true
  $ManageHosts    = $true
  ```

  OMEN will not auto‑start and is blocked from reaching the network both via firewall rules and via `hosts`.

* **Block OMEN via firewall only**

  ```powershell
  $DryRun         = $false
  $ManageFirewall = $true
  $ManageHosts    = $false
  ```

  Services, tasks and Run entries are tamed, firewal rules are created, but `hosts` file is not modified.

* **Only stop auto‑start, keep online features**

  ```powershell
  $DryRun         = $false
  $ManageFirewall = $false
  $ManageHosts    = $false
  ```

  Services, tasks and Run entries are tamed, but neither firewall nor `hosts` are touched.

* **Preview what will happen**

  ```powershell
  $DryRun = $true
  ```

  Script prints everything it *would* do, but makes no changes.

---

## Manual run (if you don’t want to use the .cmd launcher)

1. Right‑click `OmenGamingHubUnlocker.ps1` → **Properties** → if you see an **Unblock** checkbox, tick it → OK.
2. Right‑click `OmenGamingHubUnlocker.ps1` → **Run with PowerShell**.
3. Approve the UAC prompt.
4. Follow the on‑screen output.

If PowerShell says `running scripts is disabled` or `file is not digitally signed`, see the FAQ below.

---

## After reboot – what should change?

* OMEN **no longer auto‑starts** with Windows.

* HP / OMEN helper services show `Startup type: Manual` in Services.

* HP / OMEN scheduled tasks are **Disabled** in Task Scheduler.

* If firewall management is enabled, you see rules like:

  ```text
  Tame-OMEN - SomeOmenExecutable.exe
  ```

  in Windows Defender Firewall, and OMEN cannot talk to the network.

* If `hosts` management is enabled, the system `hosts` file contains entries like:

  ```text
  127.0.0.1   hpbp.io                  # OmenGamingHubUnlocker
  127.0.0.1   api.hpbp.io              # OmenGamingHubUnlocker
  127.0.0.1   hpgamestream.com         # OmenGamingHubUnlocker
  127.0.0.1   content.hpgamestream.com # OmenGamingHubUnlocker
  ```

You can still open OMEN Gaming Hub manually after your VPN is connected.

---

## FAQ

### Is this safe? What does it NOT do?

The script:

* does **not** uninstall OMEN Gaming Hub,
* does **not** remove drivers or core Windows components,
* only changes:

  * startup type of specific HP/OMEN services,
  * some HP/OMEN scheduled tasks,
  * HP/OMEN entries in common Run keys,
  * optional outbound firewall rules for OMEN executables,
  * optional `hosts` entries for known HP/OMEN endpoints.

You can re‑run the script again later – it is idempotent for the typical setup.

---

### I get “running scripts is disabled” or “file is not digitally signed”

If you use the **`.cmd` launcher**, you should not see this.

If you run the `.ps1` directly and get this error:

1. Open **PowerShell as Administrator**.
2. Run:

   ```powershell
   Set-ExecutionPolicy RemoteSigned -Scope CurrentUser
   ```

   Answer `Y`.
3. Optionally unblock the file once:

   ```powershell
   Unblock-File .\OmenGamingHubUnlocker.ps1
   ```
4. Run the script again.

---

### OMEN still flashes for a second on the taskbar – is that normal?

Yes. UWP apps sometimes briefly start or check updates on login.

With this script applied:

* services/tasks are tamed,
* Run entries are removed,
* and (optionally) firewall and `hosts` block OMEN traffic.

So a small visual flicker does not mean it still phones home or resets itself.

---

### How do I remove the firewall rules and give OMEN internet back?

Open **PowerShell as Administrator** and run:

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

After that OMEN executables are no longer blocked by these rules.

If you also enabled `hosts` management, you can manually edit `C:\Windows\System32\drivers\etc\hosts` as Administrator and remove the lines that end with `# OmenGamingHubUnlocker`.

---

### Can I break my system with this?

Very unlikely, but always a good idea to:

* create a restore point before running any tweak script,
* keep a backup of important data,
* read the script if you are curious what it does.

Worst case, you can:

* set startup types back to their old values,
* re‑enable tasks,
* delete the `Tame-OMEN` firewall rules,
* remove the `# OmenGamingHubUnlocker` lines from `hosts`.

---

## Contributing & support

If this script helped you:

* ⭐ **Star the repo** to support the project.
* 🍴 **Fork it** and tweak it for your own setup.
* 🐛 **Open an issue** if something breaks or OMEN changes its behavior.
* 🔧 **Send a PR** if you improve detection of services/tasks or add a safer rollback.

Thanks for using Omen Gaming Hub Unlocker 🙌
