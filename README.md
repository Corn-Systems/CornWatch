# 🌽 CornWatch — Windows System Health Dashboard

> *"Keeping an eye on your kernel"*

A companion utility to [Win11 Optimizer](https://github.com/Corn-Systems/win11op).
Real-time system health monitoring with a terminal-aesthetic WebView2 dashboard.

---

## Features

| Panel                | What it shows                                             |
| -------------------- | --------------------------------------------------------- |
| **CPU Gauge**        | Total usage ring, sparkline history, temperature          |
| **RAM Gauge**        | Usage ring, sparkline, used/total GB                      |
| **GPU Panel**        | Load ring, VRAM usage, compute %, D3D engine breakdown    |
| **Network Chart**    | Live send/recv graph, Mbps readouts, active adapter       |
| **CPU Core Heatmap** | Per-core colour-coded usage grid                          |
| **Disk I/O & Space** | R/W MB/s, per-drive fill bars                             |
| **Top Processes**    | Live top-8 process groups by CPU% (grouped like Task Mgr) |
| **Health Alerts**    | Auto-generated warnings + critical flags (configurable)   |
| **Health Score**     | Composite 0–100 rating with 24-hour history               |
| **Snapshot Export**  | One-click JSON or PNG snapshot to Documents               |
| **Tray Mode**        | Minimizes to tray, optional launch-at-startup toggle      |
| **Update Check**     | Background check against GitHub Releases on startup       |

While minimized to the tray, polling automatically slows to 5s to stay out
of the way; it returns to live 1s updates when the window is reopened.

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
├── HealthHistory.cs             Rolling 24-hour health score ring buffer
├── Core/
│   ├── SystemMonitor.cs         Background polling engine (1s ticks)
│   ├── GpuMonitor.cs            GPU Engine counters + DXGI VRAM query
│   ├── ProcessWatchdog.cs       Top-N process sampling (CPU delta based)
│   ├── SnapshotExporter.cs      JSON snapshot export
│   ├── StartupManager.cs        HKCU Run key startup toggle
│   └── Wmi.cs                   Disposing WMI query helper
├── Models/
│   └── SystemSnapshot.cs        Data model serialised to JSON
└── UI/
    └── Dashboard/
        ├── MainForm.cs           WinForms host + WebView2 + tray + JS bridge
        └── Dashboard.html        HTML/CSS/JS dashboard frontend
```

**Data flow:**

```
System APIs (PerformanceCounter + WMI + DXGI)
       ↓
  SystemMonitor (background thread)
       ↓  SystemSnapshot (JSON)
  mainForm.onSnapshotReady()
       ↓  ExecuteScriptAsync
  Dashboard.html → window.cornWatch.onSnapshot()
       ↓
  DOM updates (gauges, charts, tables, alerts)
```

---

## Tech Stack

- **C# .NET 10 + WinForms** — app host, system polling
- **PerformanceCounter** — CPU, disk, network, GPU engine live metrics
- **WMI (System.Management)** — CPU name, temperature, RAM
- **DXGI (P/Invoke)** — true physical VRAM without the WMI 4GB cap
- **Microsoft.Web.WebView2** — Chromium-based HTML panel
- **Vanilla HTML/CSS/JS** — dashboard frontend (no frameworks needed)

---

## Getting Started

```
# Prerequisites: .NET 10 SDK, Windows 10/11, WebView2 Runtime
dotnet restore
dotnet run
```

> **Note:** CPU temperature readings via WMI require running as Administrator.
> GPU temperature, fan speed, and power draw require vendor SDK integration
> (see Planned Features). The dashboard shows **N/A** for unavailable sensors
> rather than silently displaying zero.

---

## Settings & Persistence

User preferences are stored at `%AppData%\CornSystems\CornWatch\settings.json`
and survive reinstalls. Configurable values include:

- Poll interval (default 1 s, slows to 5 s in tray)
- Window size and state
- Start minimized to tray
- All alert thresholds (CPU %, CPU temp, RAM %, disk %, GPU temp, VRAM %)
- Update check on/off

Health score history (last 24 hours at 1 s resolution) is stored alongside
settings in `history.json` and persists across restarts.

---

## Installer

A ready-to-compile Inno Setup 6 script (`CornWatch.iss`) is included next to
the `.csproj`. It produces a per-machine installer to `Program Files\CornWatch`
with Desktop and Start Menu shortcuts, an uninstaller entry, and an optional
WebView2 Evergreen bootstrapper.

```
dotnet publish -c Release
ISCC.exe CornWatch.iss
# → installer_output\CornWatch-Setup-<version>.exe
```

---

## Planned Features

- [ ] GPU temp / fan / clock / power via AMD ADLX + NVIDIA NVML
- [ ] Configurable alert thresholds in the UI (settings panel)
- [ ] Tray balloon notifications on critical alerts
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