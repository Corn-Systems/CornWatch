# 🌽 CornWatch — Windows System Health Dashboard

> *"Keeping an eye on your kernel"*

A companion utility to [Win11 Optimizer](https://github.com/Corn-Systems/win11op).
Real-time system health monitoring with a Corn Systems-themed WebView2 dashboard.

---

## Features

| Panel                | What it shows                                             |
| -------------------- | --------------------------------------------------------- |
| **CPU Gauge**        | Total usage ring, sparkline history, temperature          |
| **RAM Gauge**        | Usage ring, sparkline, used/total GB                      |
| **GPU Panel**        | Load, VRAM, temperature, fan, power — for every GPU       |
| **Network Chart**    | Live send/recv graph, Mbps readouts, active adapter       |
| **CPU Core Heatmap** | Per-core colour-coded usage grid                          |
| **Disk I/O & Space** | R/W MB/s, per-drive fill bars                             |
| **Top Processes**    | Live top-8 process groups by CPU% (grouped like Task Mgr) |
| **Health Alerts**    | Auto-generated warnings + critical flags (configurable)   |
| **Health Score**     | Composite 0–100 rating with a 24-hour history chart       |
| **Snapshot Export**  | One-click JSON or PNG snapshot to Documents               |
| **Tray Mode**        | Minimizes to tray, optional launch-at-startup toggle      |
| **Notifications**    | Tray balloon when a new critical alert appears            |
| **Settings Panel**   | Poll interval, alert thresholds and toggles, in the UI    |
| **Update Check**     | Background check against GitHub Releases on startup       |

While minimized to the tray, polling automatically slows to 5s to stay out
of the way; it returns to the configured interval when the window is reopened.

---

## Architecture

```
CornWatch/
├── Program.cs                   Entry point (single-instance, crash handler)
├── AppInfo.cs                   Version, repo URL, User-Agent strings
├── AppPaths.cs                  All on-disk paths in one place
├── AppSettings.cs               User preferences + SettingsManager
├── SessionLog.cs                Persistent daily log
├── UpdateChecker.cs             GitHub Releases version check
├── Theme.cs                     Corn Systems colours for the WinForms shell
├── HealthHistory.cs             Rolling 24-hour health score history (per minute)
├── Core/
│   ├── SystemMonitor.cs         Background polling engine (builds each snapshot)
│   ├── HealthEvaluator.cs       Health score + alerts from one table of checks
│   ├── GpuMonitor.cs            Per-GPU load, VRAM and sensors
│   ├── HardwareSensors.cs       LibreHardwareMonitor wrapper (GPU/CPU sensors)
│   ├── DxgiAdapters.cs          DXGI adapter enumeration (name, VRAM, LUID)
│   ├── MemoryInfo.cs            Physical memory via GlobalMemoryStatusEx
│   ├── NetworkMeter.cs          Throughput on the active network adapter
│   ├── ProcessWatchdog.cs       Top-N process sampling (CPU delta based)
│   ├── SnapshotExporter.cs      JSON snapshot export
│   ├── StartupManager.cs        HKCU Run key startup toggle
│   └── Wmi.cs                   Disposing WMI query helper
├── Models/
│   └── SystemSnapshot.cs        Data model serialised to JSON
└── UI/
    └── Dashboard/
        ├── MainForm.cs           WinForms host + WebView2 + tray + notifications
        ├── CornBridge.cs         JS ↔ C# bridge (exports, settings, history)
        └── Dashboard.html        HTML/CSS/JS dashboard frontend
```

Tests live in `CornWatch.Tests/` (xUnit); CI is `.github/workflows/build.yml`.

**Data flow:**

```
System APIs (PerformanceCounter + Win32 + WMI + DXGI)
       ↓
  SystemMonitor (background thread)
       ↓  SystemSnapshot (JSON)
  MainForm.OnSnapshotReady()
       ↓  ExecuteScriptAsync
  Dashboard.html → window.cornWatch.onSnapshot()
       ↓
  DOM updates (gauges, charts, tables, alerts)
```

---

## Tech Stack

- **C# .NET 10 + WinForms** — app host, system polling
- **PerformanceCounter** — CPU, disk, GPU engine and VRAM live metrics
- **Win32 / .NET APIs** — RAM (`GlobalMemoryStatusEx`), network (`NetworkInterface`)
- **WMI (System.Management)** — CPU name, temperature
- **DXGI (P/Invoke)** — GPU name, LUID and true VRAM without the WMI 4GB cap
- **LibreHardwareMonitorLib** — GPU temperature / fan / power, elevated CPU temperature
- **Microsoft.Web.WebView2** — Chromium-based HTML panel
- **Vanilla HTML/CSS/JS** — dashboard frontend (no frameworks needed)

---

## Getting Started

```
# Prerequisites: .NET 10 SDK, Windows 10/11, WebView2 Runtime
dotnet restore
dotnet run
```

> **Note:** GPU temperature, fan speed and power draw work without admin rights.
> CPU temperature requires running as Administrator (Windows offers no
> unprivileged source); installing the [PawnIO](https://pawnio.eu/) driver gives
> accurate per-CPU readings, otherwise the ACPI thermal zone is used.
> The dashboard shows **N/A** for unavailable sensors rather than zero.

Run the tests with `dotnet test` from the repository root.

---

## Settings & Persistence

User preferences are stored at `%AppData%\CornSystems\CornWatch\settings.json`
and survive reinstalls. Everything except window size is editable from the
**Settings** button in the dashboard:

- Poll interval (default 1 s, slows to 5 s in tray)
- Window size and state
- Start minimized to tray
- All alert thresholds (CPU %, CPU temp, RAM %, disk %, GPU temp, VRAM %)
- Update check on/off
- Critical-alert tray notifications on/off

Health score history (last 24 hours, one averaged entry per minute) is stored
alongside settings in `history.json` and persists across restarts.

---

## Installer

A ready-to-compile Inno Setup 6 script (`CornWatch.iss`) is included next to
the `.csproj`. It produces a per-machine installer to `Program Files\CornWatch`
with Desktop and Start Menu shortcuts, an uninstaller entry, and an optional
WebView2 Evergreen bootstrapper.

```
cd CornWatch
dotnet publish -c Release
ISCC.exe CornWatch.iss
# → installer_output\CornWatch-Setup-<version>.exe
```

---

## Planned Features

- [x] GPU temp / fan / power via vendor sensors
- [x] Configurable alert thresholds in the UI (settings panel)
- [x] Tray balloon notifications on critical alerts
- [x] Multi-GPU view
- [ ] Dark / light theme toggle

---

## Related Projects

- [Win11 Optimizer](https://github.com/Corn-Systems/win11op) — Windows performance & privacy tweaks
- [CornDownloader](https://github.com/Corn-Systems/CornDownloader) — Utility auto-downloader for fresh installs
- [CornTools](https://github.com/Corn-Systems/CornTools) — Unified Corn Systems launcher

---

## Credits

Built by Corn Systems with development assistance from Claude by Anthropic.
Licensed under MIT.