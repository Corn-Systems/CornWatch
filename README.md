# 🌽 CornWatch — Windows System Health Dashboard

> *"Keeping an eye on your kernel"*

A companion utility to [Win11 Optimizer](https://github.com/Corn-Studios/win11op).
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
| **Health Alerts**    | Auto-generated warnings + critical flags                  |
| **Health Score**     | Composite 0–100 rating                                    |
| **Snapshot Export**  | One-click JSON or PNG snapshot to Documents               |
| **Tray Mode**        | Minimizes to tray, optional launch-at-startup toggle      |

While minimized to the tray, polling automatically slows to 5s to stay out
of the way; it returns to live 1s updates when the window is reopened.

---

## Architecture

```
CornWatch/
├── Program.cs                   Entry point
├── Core/
│   ├── SystemMonitor.cs         Background polling engine (1s ticks)
│   ├── GpuMonitor.cs            GPU Engine counters + DXGI VRAM query
│   ├── ProcessWatchdog.cs       Top-N process sampling (CPU delta based)
│   ├── SnapshotExporter.cs      JSON snapshot export
│   ├── StartupManager.cs        HKCU Run key startup toggle
│   └── AdlxBridge.cs            AMD ADLX stub (temps/fan — planned)
├── Models/
│   └── SystemSnapshot.cs        Data model serialised to JSON
└── UI/
    └── Dashboard/
        ├── MainForm.cs           WinForms host + WebView2 + tray + JS bridge
        └── dashboard.html        HTML/CSS/JS dashboard frontend
```

**Data flow:**

```
System APIs (PerformanceCounter + WMI + DXGI)
       ↓
  SystemMonitor (background thread)
       ↓  SystemSnapshot (JSON)
  MainForm.PushToJs()
       ↓  ExecuteScriptAsync
  dashboard.html → window.cornWatch.onSnapshot()
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
> **Note:** CPU temperature readings via WMI may require running as Administrator.
> GPU temperature/fan/power require vendor SDK integration (see Planned Features).

---

## Planned Features

- [ ] GPU temp/fan/clock/power via AMD ADLX + NVIDIA NVML
- [ ] Historical data persistence (SQLite)
- [ ] Configurable alert thresholds
- [ ] Tray balloon notifications on critical alerts
- [ ] Dark/light theme toggle

---

## Related Projects

- [Win11 Optimizer](https://github.com/Corn-Studios/win11op) — Windows performance & privacy tweaks
- [CornDownloader](https://github.com/ConnorCorn07/CornDownloader) — Utility auto-downloader for fresh installs
- [CornTools](https://github.com/Corn-Studios/CornTools) — Unified Corn Studios launcher

---

## Credits

Built by Corn Studios with development assistance from Claude by Anthropic.
Licensed under MIT.
