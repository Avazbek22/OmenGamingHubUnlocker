# Omen Gaming Hub Unlocker

Small helper tool for **HP OMEN** laptops and desktops.

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
   - **[4] Disable scripts** — removes rules/hosts created by this tool, attempts to re-enable services/tasks
   - **[5] About** — mini README
   - **[6] Exit**

5. 🔄 Reboot is **optional**, but recommended if you want a clean “startup verification”.

---

## 🧰 What “Activate scripts” does

When you run **Activate scripts**, the tool applies changes (best-effort):

- **Services:** sets typical HP/OMEN helper/telemetry services to **Manual**
- **Tasks:** disables scheduled tasks matching HP/OMEN patterns
- **Run keys:** removes HP/OMEN autostart entries from common registry Run locations *(one-way by design)*
- **Firewall (optional):** discovers OMEN-related executables and creates **outbound block** rules using a prefix (default: `Tame-OMEN`)
- **Hosts (optional):** adds `127.0.0.1 ...` mappings for known HP/OMEN endpoints

At the end, the tool prints a **status snapshot table**.

---

## ♻️ What “Disable scripts” does

When you run **Disable scripts**, the tool (best-effort):

- Removes **Firewall rules** created by this tool (by prefix)
- Removes **hosts entries** created by this tool
- Attempts to re-enable HP/OMEN **services/tasks**

> Important: **Run (autostart) registry entries are not restored** by design.  
> If you need OMEN to autostart again, use OMEN’s settings (if available) or reinstall/repair OMEN Gaming Hub.

---

## 🧱 Firewall rules are update-safe (important)

OMEN Gaming Hub updates (especially from Microsoft Store) can change internal paths/executables.

This tool manages firewall rules by **prefix** (default: `Tame-OMEN`). On each activation it:

- removes **all old** rules with that prefix
- re-discovers OMEN-related executables again
- creates a **fresh set** of outbound block rules

So after an OMEN update you typically just run **Activate scripts** again — and your rules stay clean.

---

## 🧠 What this tool is (and is not)

✅ This tool:

- does **not** uninstall OMEN Gaming Hub
- does **not** remove drivers or core Windows components
- only touches (best-effort):
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
- **Core operations (best-effort, resilient):**
  - services management via Windows APIs (and safe fallbacks where needed)
  - scheduled tasks management (pattern-based)
  - registry Run entries cleanup (common locations)
  - firewall management via Windows Firewall COM (`HNetCfg.*`) with fallback to PowerShell cmdlets when needed
  - hosts file edits with tagged entries (so they can be cleanly removed)

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

If this tool helped you:

- ⭐ **Star** the repository  
- 🍴 **Fork** it and adapt it to your OMEN model/setup  
- 🐛 Open an **Issue** if something breaks or OMEN changes its behavior  
- 🔧 PRs are welcome (better detection, safer rollback, new endpoints)

Use it, share it, and enjoy a quieter OMEN experience 🙌

---

## 📄 License (MIT)
