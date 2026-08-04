<h1 align="center">Omen Gaming Hub Unlocker</h1>

<p align="center">
  <a href="README.md">🇬🇧 English</a> · <a href="README.ru.md">🇷🇺 Русский</a>
</p>

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
  A small helper tool for <strong>HP OMEN</strong> laptops and desktops.
</p>

It keeps **OMEN Gaming Hub** installed, but stops it from starting on its own and calling home **before you're ready** — for example, before you turn on a VPN.

✅ Typical use:

1. Start Windows
2. Turn on your VPN (if you need one)
3. Open OMEN Gaming Hub yourself, when you're ready

...instead of OMEN waking up on its own and talking to HP's servers first.

> 🧭 If you're here because OMEN Gaming Hub says something like **"not available in your region," "wrong region,"** or **"country not supported"**:
> This tool does **not** change your region in Windows or the Microsoft Store.
> It stops OMEN from starting on its own and, if you choose, blocks its internet access — so you can turn on your VPN first, then open OMEN yourself.

---

## 📥 Download

Get the latest **portable, single-file** build from GitHub Releases:

- **Download:** https://github.com/Avazbek22/OmenGamingHubUnlocker/releases/latest

> Tip: use **win-x64** for most modern OMEN devices.

---

## ✅ Requirements

- Windows **10** or **11**
- Administrator rights (Windows will ask automatically)

---

## 🚀 Quick start

✅ Best practice: close OMEN Gaming Hub first (the window, and the tray icon if it's running).

1. 📦 Download **OmenGamingHubUnlocker.exe** from Releases
2. ▶️ Run it
3. 🛡️ Approve the Administrator prompt
4. 🎛️ Choose from the menu:

   - **[1] Check status** — see what's currently on or off (services, tasks, firewall, hosts)
   - **[2] Dry run** — preview what would change, without changing anything yet
   - **[3] Activate scripts** — turn the blocks on
   - **[4] Disable scripts** — undo everything and restore your original settings
   - **[5] Reset OMEN & Activate** — reset OMEN's saved data, then turn the blocks back on
   - **[6] Help**
   - **[7] About**
   - **[8] Change language**
   - **[9] Support the project (Boosty)**
   - **[0] Exit**

5. 🔄 A reboot is **optional**, but it's a good way to confirm everything starts up clean.

---

## 🧰 What "Activate scripts" does

- Blocks OMEN's internet access, with a firewall rule that survives OMEN updates
- Sets OMEN's background services to "Manual" and stops the ones currently running
- Turns off OMEN's scheduled tasks and stops any that are running
- Closes OMEN's background processes
- Removes OMEN from Windows startup — your original settings are saved first, so this can always be undone
- Optionally blocks known HP/OMEN web addresses
- Double-checks everything actually stayed off before saying "done"

Your previous settings are always saved before anything changes.

---

## ♻️ What "Disable scripts" does

- Puts services and scheduled tasks back exactly how they were
- Restores your original Windows startup entries
- Checks everything is back to normal before removing the network block
- Removes only the firewall rules and hosts entries this tool created
- If something can't be fully restored, it keeps the network block on, to stay safe

---

## 🧱 The firewall rule survives OMEN updates

OMEN Gaming Hub updates (especially through the Microsoft Store) often change file paths and executable names. Most simple firewall blocks break after an update — this one doesn't.

The main rule is tied to OMEN's Store package ID, not to a specific file path. Every time you run Activate, the tool also:

- keeps that main rule active while it checks for new file paths
- finds any new OMEN files and adds rules for them
- removes old rules that no longer apply
- confirms every current OMEN file is actually blocked

This closes a gap that used to exist between an OMEN reset and the firewall catching up.

---

## 🧠 What this tool is — and isn't

✅ This tool:

- does **not** uninstall OMEN Gaming Hub
- does **not** remove drivers or core Windows components
- only touches:
  - the startup type of selected services
  - selected scheduled tasks
  - HP/OMEN startup entries in the registry
  - optional firewall rules it creates itself
  - optional hosts file entries it creates itself

❌ This tool is **not**:

- a crack, patch, or permanent region changer
- a Microsoft Store region bypass by itself

---

## 🧩 Technical overview (for devs)

- **Language:** C#
- **Runtime:** .NET 10
- **App type:** Console app (Windows 10/11), single-file portable build
- **Elevation:** Uses an application manifest to request Administrator (UAC) on startup
- **Core operations:**
  - service management via Windows APIs, with safe fallbacks where needed
  - scheduled task disable and running-instance termination
  - registry Run entry cleanup (common locations)
  - package-SID and executable firewall rules, with COM/PowerShell fallback and post-write verification
  - atomic, encoding-preserving hosts and rollback-state writes
  - interactive-user validation before AppX or HKCU operations

The UI is designed to be predictable:
- **Status** = current facts
- **Dry run** = "will do" predictions
- **Activate/Disable** = actual changes + a final snapshot

### Build release artifacts

Run the release script from a clean local `main` branch that exactly matches `origin/main`:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Publish-Release.ps1
```

The script runs the test suite and produces self-contained single-file executables for `win-x64` and `win-arm64` in `artifacts\release\v<version>`, together with `SHA256SUMS.txt`.

Version properties have distinct purposes and are validated before every release:

- `InformationalVersion` is the public GitHub version used by tags and artifact names, e.g. `3.2`.
- `Version` is the .NET build version, e.g. `3.2.0`.
- `FileVersion` and `AssemblyVersion` are the four-part Windows and WinGet version, e.g. `3.2.0.0`.

The script stops if these values don't describe the same release.

### Publish to WinGet

The WinGet helper supports both the first package submission and later updates:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Publish-WinGet.ps1
```

It reads the project version, verifies the public GitHub release and both architectures, generates manifests, runs `winget validate`, optionally performs a local install/uninstall test, and can submit the PR through WingetCreate.

For a non-interactive update after publishing a GitHub release:

```powershell
.\scripts\Publish-WinGet.ps1 -Version 3.3 -NonInteractive -InstallTest -Submit
```

Prerequisites:

- Current Windows Package Manager client
- Current `Microsoft.WingetCreate` package
- GitHub authentication configured in WingetCreate for PR submission

> WinGet treats Omen Gaming Hub Unlocker as a portable application. `winget uninstall` removes the executable and command alias, but it does not reverse protection previously applied to Windows. Run **Disable scripts** before uninstalling if you also want to remove the managed firewall, hosts, service, task, and startup changes.

---

## 🧩 Troubleshooting

### SmartScreen: "Windows protected your PC"

Portable, unsigned tools from GitHub can trigger SmartScreen:

- Click **More info** → **Run anyway**

### OMEN still flashes for a second on login

UWP apps can briefly initialize during login or update checks. If services and tasks are turned off (and network is optionally blocked), a short flicker doesn't necessarily mean it's calling home.

### Found this by searching for:

- "OMEN Gaming Hub not available in my region"
- "OMEN Gaming Hub wrong region"
- "OMEN Gaming Hub region locked"
- "HP OMEN Gaming Hub country not supported"
- "OMEN Gaming Hub VPN workaround"

This tool is built exactly for that workflow: **boot → VPN → open OMEN yourself.**

---

## 🤝 Support the project

Omen Gaming Hub Unlocker is free and open source. If it helped you keep OMEN usable, you can support continued development, compatibility testing, and future updates:

<p align="center">
  <a href="https://boosty.to/avazbek22">
    <img src=".github/assets/boosty-support.svg" width="800" alt="Support OmenGamingHubUnlocker on Boosty">
  </a>
</p>

You can also help by:

- ⭐ **Starring** the repository
- 🍴 **Forking** it and adapting it to your OMEN model or setup
- 🐛 Opening an **Issue** if something breaks or OMEN changes its behavior
- 🔧 Sending a **PR** — better detection, safer rollback, new endpoints are all welcome

---

## 📄 License

MIT — see [LICENSE](LICENSE) for details.
