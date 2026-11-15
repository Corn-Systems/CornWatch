# Changelog

All notable changes to CornWatch are documented here.
Format based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [0.3.0] — 2026-11-14

Built and smoke-tested on Windows 11 (dashboard, settings dialog, hidden tray start, exports of live data).

### Added

- **Settings panel** in the dashboard: poll interval, every alert threshold, start-minimized, update-check and notification toggles. Values are validated and clamped before saving.
- **Critical-alert notifications:** a tray balloon when a critical alert newly appears (5-minute cooldown per alert, skipped while the dashboard has focus, toggle in settings). Clicking it opens the dashboard.
- **24-hour health chart** in the alerts column, backed by `history.json`.
- App version shown in the dashboard header.
- **GPU temperature, fan speed and power draw** via LibreHardwareMonitor (AMD ADL / NVIDIA NVAPI / Intel). No admin rights needed. GPU temperature alerts can now actually fire.
- **Multi-GPU view:** every hardware GPU is monitored; with more than one, the GPU card gets a selector and alerts name the card.
- **CPU temperature from vendor sensors** when running elevated with the PawnIO driver installed; otherwise the ACPI thermal zone is still used when elevated. (There is no supported way to read CPU temperature without elevation.)
- **Corn Systems theme:** the dashboard and window now use the shared navy / corn-gold palette.
- **Unit tests** (`CornWatch.Tests`, xUnit) for health scoring, version comparison, settings clamping and GPU LUID naming, and a **GitHub Actions** workflow that builds, tests and publishes on every push and pull request.
- Warning-level alerts for RAM and VRAM and a critical-level alert for full disks. These already cost health score but produced no alert.

### Changed

- **Naming convention:** the code follows the Corn Systems C# standard used by the other repos — PascalCase types, methods, properties and constants; `_camelCase` private fields; camelCase locals and parameters. JSON on disk and over the JS bridge stays camelCase.
- `dashboard.html` renamed to `Dashboard.html` (csproj, `MainForm` and README updated).
- JS bridge methods are PascalCase to match the C# `CornBridge` class (`ExportSnapshot`, `GetSettings`, …). The host object is still `cornBridge`.
- `settings.json` and `history.json` now write camelCase keys. Existing PascalCase files still load (case-insensitive read).
- Settings `pollIntervalMs` is clamped to 250–30 000 ms on load.
- Task Manager and Resource Monitor launch via shell execute, so Windows shows a UAC prompt instead of failing silently.
- JSON snapshot exports are written as UTF-8 without a BOM.
- Restoring from the tray returns polling to the configured interval instead of a hardcoded 1 s.
- `StartupManager.Toggle()`, `Enable()` and `Disable()` are replaced by `SetEnabled(bool)`. A failed disable now raises an error instead of being swallowed.

### Fixed

- **WebView2 could not start when installed to Program Files.** Its profile folder defaulted to the install directory, which is read-only for standard users; it now lives in `%LocalAppData%\CornSystems\CornWatch\WebView2`. The fallback screen also no longer claims the runtime is missing for unrelated errors.
- **GPU load ignored programs started after CornWatch** (for example a game launched later), and exited programs raised an error every tick. The GPU Engine category is now re-read each tick.
- **GPU selection was a guess** based on engine counts. The adapter, its name, VRAM and LUID now come straight from DXGI (largest dedicated VRAM wins).
- VRAM "total" used the per-process DXGI budget, which under-reports; it now uses the adapter's dedicated memory.
- **Network figures were megabytes/s labelled Mbps** and could track an idle adapter. They are now real megabits/s on the adapter that has the default gateway, re-picked every 10 s.
- **Health history held 24 minutes, not 24 hours** (1440 one-second entries) and was never shown. It now stores one averaged entry per minute.
- `--minimized` / start-minimized briefly showed a window stub and popped a "minimised to tray" balloon at every boot. The window now starts fully hidden.
- Restoring from the tray always returned a maximized window to normal size.
- Clicking any tray balloon after an update notice opened the releases page.
- Tray "Launch at startup" checkbox went stale when the dashboard button changed the setting.
- CPU temperature query threw on every tick for non-admin users; it now stops after three failures.
- Network shares and optical drives were counted as disks (and could hang the poll or cost health score).
- Installer: bundling the WebView2 bootstrapper failed at install time because it was never added to `[Files]`.
- `Dashboard.html` was still tracked by git as `dashboard.html`, which breaks case-sensitive builds.
- CPU core grid and drive list overflowed their card on machines with many cores or drives.
- Configured poll interval was never applied. It was assigned after the timer started.
- CPU core counters sorted as strings (core 10 before core 2). They now sort numerically.
- DXGI adapter enumeration released skipped adapters twice.
- `Timer` callbacks could overlap and touch performance counters concurrently. Overlapping ticks are now skipped.
- `SystemMonitor.Dispose` could dispose counters mid-poll. It now waits (up to 2 s) for an in-flight poll.
- UI push used `Invoke`, which could block the poll thread against the UI thread. It now uses `BeginInvoke` and tolerates a closing form.
- GPU engine counters for unused engine types were created and never disposed. Only counters that are read are created now.
- Startup checkbox could show the wrong state when the registry write failed. It now reverts to the real state.
- Update check is cancelled on close, and the balloon click handler is attached before the balloon is shown.
- Main mutex is released and history flushed even if `Application.Run` throws.
- WMI searcher results and rows are disposed.

### Removed

- `Core/AdlxBridge.cs`: unused AMD ADLX stub containing a TODO. The GPU temperature/fan/power roadmap item in the README is unchanged.
- Dead code: `AppInfo.Publisher`, `AppPaths.CrashLog`, `HealthHistory.Snapshot()`, `SystemInfo` (folded into `SessionLog`), an unused `GetSettings` bridge stub, unused GPU video counters, and the never-populated `GpuInfo.D3dEngines` / `coreClockMhz` fields.
- Redundant `Application.SetHighDpiMode` call (DPI mode comes from `ApplicationHighDpiMode` in the csproj).
- `partial` on the main form and its empty `InitializeComponent` wrapper.

### Internal

- `Core/HealthEvaluator.cs`: one table of checks produces both the score and the alerts. Alerts carry a stable `id`.
- `Core/DxgiAdapters.cs`, `Core/MemoryInfo.cs`, `Core/NetworkMeter.cs` split out of the monitors; RAM no longer costs a WMI query per tick.
- `CornBridge` moved to its own file; new bridge methods `GetSettings`, `SaveSettings`, `GetHealthHistory`, `GetVersion`.
- Session log collapses a message that repeats back-to-back into one line plus a count.
- `Core/HardwareSensors.cs` wraps LibreHardwareMonitor; `Theme.cs` holds the WinForms-side colours.
- GPU temperature / fan / power in the snapshot JSON are `null` (not `0`) when no sensor exists.
- NuGet XML doc files are no longer copied into the publish folder.
- Removed the dead D3D-engine block from `Dashboard.html`, unused CSS, and the redundant `primaryGpu` copy in exported JSON.
- New `Core/Wmi.cs` (`WmiQuery.Query`) replaces four hand-rolled WMI loops.
- `AppPaths.WriteAtomic`, `AppPaths.SnapshotFile` and `AppPaths.EnsureDir` replace duplicated file-write and directory-creation code.
- `SnapshotExporter.ToJson` is the single place JSON options are defined (previously duplicated in `MainForm`).
- `MainForm.Launch` replaces four separate `Process.Start` blocks.
- `HealthHistory` and `SettingsManager` singletons use `Lazy<T>`.
- Health score and alerts are table-driven (one `Check` helper). Scoring thresholds and weights are unchanged.
- DXGI vtable calls go through one generic `Fn<T>` helper.

## [0.2.0]

Previous release. See the git history for details.
