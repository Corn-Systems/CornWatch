# Changelog

All notable changes to CornWatch are documented here.
Format based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased] — changes since 0.2.0

> **Not yet built or run.** This refactor was written without a .NET SDK on hand.
> Run `dotnet build` and smoke-test the dashboard before tagging a release.

### Changed

- **Naming convention:** file names are PascalCase; every other identifier (types, members, constants, enum values, locals, fields) is camelCase. Private fields lose their `_` prefix.
  - Namespaces and `Main` are unchanged. The generated `ApplicationConfiguration` lives in the root namespace and the CLR requires `Main`.
- `dashboard.html` renamed to `Dashboard.html` (csproj, `MainForm` and README updated).
- JS bridge methods are now camelCase (`exportSnapshot`, `exportPng`, `getStartupEnabled`, `setStartupEnabled`, `getGpuSensorDump`, `openProcessManager`, `openResourceMonitor`). `Dashboard.html` call sites updated. The host object is still `cornBridge`.
- `settings.json` and `history.json` now write camelCase keys. Existing PascalCase files still load (case-insensitive read).
- Settings `pollIntervalMs` is clamped to 250–30 000 ms on load.
- Task Manager and Resource Monitor launch via shell execute, so Windows shows a UAC prompt instead of failing silently.
- JSON snapshot exports are written as UTF-8 without a BOM.
- Restoring from the tray returns polling to the configured interval instead of a hardcoded 1 s.
- `startupManager.toggle()`, `enable()` and `disable()` are replaced by `setEnabled(bool)`. A failed disable now raises an error instead of being swallowed.

### Fixed

- Configured poll interval was never applied. It was assigned after the timer started.
- CPU core counters sorted as strings (core 10 before core 2). They now sort numerically.
- DXGI adapter enumeration released skipped adapters twice.
- `Timer` callbacks could overlap and touch performance counters concurrently. Overlapping ticks are now skipped.
- `systemMonitor.Dispose` could dispose counters mid-poll. It now waits (up to 2 s) for an in-flight poll.
- UI push used `Invoke`, which could block the poll thread against the UI thread. It now uses `BeginInvoke` and tolerates a closing form.
- GPU engine counters for unused engine types were created and never disposed. Only counters that are read are created now.
- Startup checkbox could show the wrong state when the registry write failed. It now reverts to the real state.
- Update check is cancelled on close, and the balloon click handler is attached before the balloon is shown.
- Main mutex is released and history flushed even if `Application.Run` throws.
- WMI searcher results and rows are disposed.

### Removed

- `Core/AdlxBridge.cs`: unused AMD ADLX stub containing a TODO. The GPU temperature/fan/power roadmap item in the README is unchanged.
- Dead code: `AppInfo.Publisher`, `AppPaths.CrashLog`, `HealthHistory.Snapshot()`, `SystemInfo` (folded into `sessionLog`), `cornBridge.GetSettings`, unused GPU video counters, and the never-populated `gpuInfo.d3dEngines` / `coreClockMhz` fields.
- Redundant `Application.SetHighDpiMode` call (DPI mode comes from `ApplicationHighDpiMode` in the csproj).
- `partial` on the main form and its empty `InitializeComponent` wrapper.

### Internal

- New `Core/Wmi.cs` (`wmiQuery.query`) replaces four hand-rolled WMI loops.
- `appPaths.writeAtomic`, `appPaths.snapshotFile` and `appPaths.ensureDir` replace duplicated file-write and directory-creation code.
- `snapshotExporter.toJson` is the single place JSON options are defined (previously duplicated in `MainForm`).
- `mainForm.launch` replaces four separate `Process.Start` blocks.
- `healthHistory` and `settingsManager` singletons use `Lazy<T>`.
- Health score and alerts are table-driven (`penalty` and `add` helpers). Scoring thresholds and weights are unchanged.
- DXGI vtable calls go through one generic `fn<T>` helper.
- Type names that were entirely lowercase (`wmi`, `program`, `entry`) became `wmiQuery`, `programEntry` and `historyEntry` to avoid compiler warning CS8981.

## [0.2.0]

Previous release. See the git history for details.
