<h1 align="center">Omen Gaming Hub Unlocker</h1>

<p align="center">
  <a href="https://github.com/Avazbek22/OmenGamingHubUnlocker/releases">
    <img src="https://img.shields.io/github/downloads/Avazbek22/OmenGamingHubUnlocker/total?style=flat-square&amp;color=0078d4" alt="Total downloads">
  </a>
  <img src="https://img.shields.io/github/license/Avazbek22/OmenGamingHubUnlocker?style=flat-square" alt="MIT license">
  <img src="https://img.shields.io/badge/.NET-10-512BD4?style=flat-square&amp;logo=dotnet&amp;logoColor=white" alt=".NET 10">
  <img src="https://img.shields.io/badge/Windows-10%20%7C%2011-0078D4?style=flat-square&amp;logo=windows11&amp;logoColor=white" alt="Windows 10 and 11">
  <img src="https://img.shields.io/github/repo-size/Avazbek22/OmenGamingHubUnlocker?style=flat-square" alt="Repository size">
</p>

<p align="center">
  <a href="https://boosty.to/avazbek22">
    <img src=".github/assets/boosty-support.svg" width="800" alt="Support OmenGamingHubUnlocker on Boosty">
  </a>
</p>

<p align="center">
  Small helper tool for <strong>HP OMEN</strong> laptops and desktops.
</p>

It keeps **OMEN Gaming Hub** installed, but helps you prevent unwanted background behavior and “region-check” style network calls **before you are ready** (for example, before you connect a VPN).

✅ Typical scenario:

1. Boot Windows  
2. Turn on your VPN (if you need it)  
3. Launch OMEN Gaming Hub manually  

…without OMEN waking up on its own and talking to HP services first.

> 🧭 If you found this repo because **“OMEN Gaming Hub is not available in your region / wrong region / region locked / country not supported”**:  
> This tool does **not** “change the region” inside Windows/Microsoft Store.  
> It helps by stopping auto-start + optionally blocking OMEN networking until *you* decide to open OMEN (often after VPN).

---

## 📥 Download

Get the latest **portable single-file** build from GitHub Releases:

- **Download:** https://github.com/Avazbek22/OmenGamingHubUnlocker/releases/latest

> Tip: prefer **win-x64** for most modern OMEN devices.

---

## 🏗️ Build release artifacts

Run the release script from a clean local `main` branch that exactly matches `origin/main`:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Publish-Release.ps1
```

The script runs the test suite and produces self-contained single-file executables for `win-x64` and `win-arm64` in `artifacts\release\v<version>`, together with `SHA256SUMS.txt`.

---

## ✅ Requirements

- Windows **10** or **11**
- Administrator rights (**UAC is requested automatically**)

---

## 🚀 Quick start

✅ Best practice: close OMEN Gaming Hub first (window + tray background icons, if possible).

1. 📦 Download **OmenGamingHubUnlocker.exe** from Releases
2. ▶️ Run **OmenGamingHubUnlocker.exe**
3. 🛡️ Approve the UAC prompt (Administrator)
4. 🎛️ Menu:

   - **[1] Check status** — shows current state (services / tasks / firewall / hosts)
   - **[2] Dry run** — deep analysis + preview (“Will …” predictions)
   - **[3] Activate scripts** — applies changes
   - **[4] Disable scripts** — restores the exact saved startup state and removes this tool's network blocks
   - **[5] Reset OMEN & Activate** — resets AppX data while keeping OMEN isolated, then re-applies protection
   - **[6] Help**
   - **[7] About**
   - **[8] Change language**
   - **[9] Support the project (Boosty)**
   - **[0] Exit**

5. 🔄 Reboot is **optional**, but recommended if you want a clean “startup verification”.

---

## 🧰 What “Activate scripts” does

When you run **Activate scripts**, the tool:

- **Firewall:** creates a version-independent package-SID block and explicit outbound blocks for current OMEN executables
- **Services:** sets OMEN-owned services to **Manual** and stops running instances
- **Tasks:** disables OMEN scheduled tasks and stops running instances
- **Processes:** terminates discovered package and known external OMEN background processes
- **Run keys:** removes matching OMEN autostart entries after saving their exact values
- **Hosts (optional):** adds `127.0.0.1 ...` mappings for known HP/OMEN endpoints
- **Verification:** requires two consecutive stable snapshots before reporting success

Rollback state is saved before startup settings are changed.

---

## ♻️ What “Disable scripts” does

When you run **Disable scripts**, the tool:

- Restores the exact saved service startup and running states, including Delayed Auto Start
- Restores saved task enabled states and Run-entry values
- Verifies startup restoration before removing network protection
- Removes only firewall rules and hosts entries owned by this tool
- Keeps the rollback file and network protection if restoration is incomplete

---

## 🧱 Firewall rules are update-safe (important)

OMEN Gaming Hub updates (especially from Microsoft Store) can change internal paths/executables.

The primary firewall rule is bound to the stable Store package SID rather than a versioned installation path. On each activation the tool also:

- keeps the package rule active while path rules are refreshed
- re-discovers package and external OMEN executables
- removes obsolete path rules
- verifies every current executable has an enabled outbound block rule

This avoids the unprotected window that previously existed between AppX reset and firewall refresh.

---

## 🧠 What this tool is (and is not)

✅ This tool:

- does **not** uninstall OMEN Gaming Hub
- does **not** remove drivers or core Windows components
- only touches:
  - startup type of selected services
  - selected scheduled tasks
  - HP/OMEN Run entries in the registry
  - optional firewall rules created by this tool
  - optional hosts entries created by this tool

❌ This tool is not:

- a crack / patch / permanent “region changer”
- a Microsoft Store region bypass by itself

---

## 🧩 Technical overview (for devs)

- **Language:** C#
- **Runtime:** **.NET 10**
- **App type:** Console app (Windows 10/11), single-file portable build
- **Elevation:** Uses an application manifest to request **Administrator** (UAC) on startup
- **Core operations:**
  - services management via Windows APIs (and safe fallbacks where needed)
  - scheduled task disable and running-instance termination
  - registry Run entries cleanup (common locations)
  - package-SID and executable firewall rules with COM/PowerShell fallback and post-write verification
  - atomic, encoding-preserving hosts and rollback-state writes
  - interactive-user validation before AppX or HKCU operations

The UI is designed to be predictable:
- **Status** = current facts  
- **Dry run** = “Will …” predictions  
- **Activate/Disable** = actionable changes + final snapshot  

---

## 🧩 Troubleshooting

### SmartScreen: “Windows protected your PC”
Portable unsigned tools from GitHub may trigger SmartScreen:

- Click **More info** → **Run anyway**

### OMEN still flashes for a second on login
UWP apps can briefly initialize during login/update checks.  
If services/tasks are tamed and (optionally) network is blocked, a short flicker does not necessarily mean it phones home.

### Search keywords (how people usually find this)
If you’re here because of one of these:
- “OMEN Gaming Hub not available in my region”
- “OMEN Gaming Hub wrong region”
- “OMEN Gaming Hub region locked”
- “HP OMEN Gaming Hub country not supported”
- “OMEN Gaming Hub VPN workaround”

This tool is specifically made for the workflow: **boot → VPN → launch OMEN manually**.

---

## 🤝 Support the project

OmenGamingHubUnlocker is free and open source. If it helped you keep OMEN usable, consider supporting continued development, compatibility testing, and future updates:

<p align="center">
  <a href="https://boosty.to/avazbek22">
    <img src=".github/assets/boosty-support.svg" width="800" alt="Support OmenGamingHubUnlocker on Boosty">
  </a>
</p>

You can also support the project by contributing:

- ⭐ **Star** the repository  
- 🍴 **Fork** it and adapt it to your OMEN model/setup  
- 🐛 Open an **Issue** if something breaks or OMEN changes its behavior  
- 🔧 PRs are welcome (better detection, safer rollback, new endpoints)

Use it, share it, and enjoy a quieter OMEN experience 🙌

---

## 📄 License (MIT)
