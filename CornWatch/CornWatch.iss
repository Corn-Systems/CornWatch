; ─────────────────────────────────────────────────────────────────────────
; CornWatch.iss — Corn Systems installer script (Inno Setup 6.x)
;
; Lives in:  CornWatch\CornWatch\  (next to the .csproj)
;
; USAGE:
;   1. Publish (from CornWatch\CornWatch\):
;        dotnet publish -c Release
;      Output: bin\Release\net10.0-windows\win-x64\publish\CornWatch.exe
;   2. Compile:
;        ISCC.exe CornWatch.iss
;   3. Output: installer_output\CornWatch-Setup-<version>.exe
;
; The version is read from the published exe so it can't drift from the .csproj.
; ─────────────────────────────────────────────────────────────────────────

#define MyAppName      "CornWatch"
#define MyAppPublisher "Corn Systems"
#define MyAppURL       "https://github.com/Corn-Systems/CornWatch"
#define MyAppExeName   "CornWatch.exe"
#define PublishDir     "bin\Release\net10.0-windows\win-x64\publish"
#define MyAppVersion   GetVersionNumbersString(PublishDir + "\" + MyAppExeName)

[Setup]
; Fixed GUID — never change between releases; Windows uses it to detect upgrades/uninstall.
AppId={{A2D7F81C-3E96-4B0A-8C14-5F2A9D0E7B32}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
AppUpdatesURL={#MyAppURL}/releases
VersionInfoVersion={#MyAppVersion}
VersionInfoCompany={#MyAppPublisher}
VersionInfoProductName={#MyAppName}

; Per-machine, 64-bit only, Program Files\CornWatch
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppPublisher}
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Windows 10 1809+ minimum (WebView2 Evergreen baseline)
MinVersion=10.0.17763

OutputDir=installer_output
OutputBaseFilename=CornWatch-Setup-{#MyAppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
UninstallDisplayName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}
; Prompt to close a running instance before copying files
CloseApplications=yes
RestartApplications=no

#ifexist "..\LICENSE"
LicenseFile=..\LICENSE
#endif
#ifexist "assets\cornwatch.ico"
SetupIconFile=assets\cornwatch.ico
#endif

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
; Desktop shortcut on by default
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"

[Files]
; Single self-contained exe + any sidecar files in the publish folder
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
#ifexist "..\LICENSE"
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion
#endif

[Icons]
Name: "{group}\{#MyAppName}";           Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}";     Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
; ── WebView2 Evergreen Bootstrapper ────────────────────────────────────────
; WebView2 ships with Windows 11 and most modern Win10 images, but older or
; debloated machines may not have it.  The bootstrapper is a tiny (~2 MB)
; self-extracting stub from Microsoft that detects and downloads the runtime
; only when needed.  It exits silently when the runtime is already present.
;
; To enable: download MicrosoftEdgeWebView2Setup.exe from
;   https://developer.microsoft.com/en-us/microsoft-edge/webview2/
; and drop it next to CornWatch.iss.  The #ifexist guard keeps the .iss
; compilable even when the bootstrapper is absent (e.g. CI builds).
#ifexist "MicrosoftEdgeWebView2Setup.exe"
Filename: "{tmp}\MicrosoftEdgeWebView2Setup.exe"; \
  StatusMsg: "Installing WebView2 Runtime (if needed)..."; \
  Parameters: "/silent /install"; \
  Flags: shellexec waituntilterminated skipifdoesntexist; \
  BeforeInstall: ExtractBootstrapper
#endif

; Launch CornWatch after install (skip in /SILENT or /VERYSILENT runs).
; No runasoriginaluser here — CornWatch doesn't use per-user aliases like winget.
Filename: "{app}\{#MyAppExeName}"; \
  Description: "Launch {#MyAppName}"; \
  Flags: nowait postinstall skipifsilent

[Code]
// Extract the bootstrapper to {tmp} so it can run elevated from there.
procedure ExtractBootstrapper();
begin
  ExtractTemporaryFile('MicrosoftEdgeWebView2Setup.exe');
end;

[UninstallDelete]
; Leave %AppData%\CornSystems\CornWatch alone (settings, logs, history —
; survives reinstall / upgrade).  Only clean up stale files in the install dir.
Type: files; Name: "{app}\*.log"
