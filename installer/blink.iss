; Blink — Inno Setup 6 installer script (Windows-only).
;
; Authored on macOS; NOT compiled/tested here — build the publish outputs on Windows
; (see installer/README.md), then compile with:  iscc installer\blink.iss
;
; Per-user install (no admin): app under {localappdata}\Programs\Blink, optional HKCU
; autostart, Korean + English wizard. Ships the EDR-isolation worker exe alongside the app.

#define AppName        "Blink"
; AppVersion can be overridden from the command line: iscc /DAppVersion=1.2.3 blink.iss
#ifndef AppVersion
  #define AppVersion   "0.1.0"
#endif
#define AppPublisher   "Blink"
#define AppExeName     "Blink.App.exe"
#define WorkerExeName  "Blink.Indexer.Worker.exe"
#define AppPublishDir     "..\Blink.App\bin\Release\net8.0-windows\win-x64\publish"
#define WorkerPublishDir  "..\Blink.Indexer.Worker\bin\Release\net8.0\win-x64\publish"

[Setup]
AppId={{B11C7A20-9E4D-4F1B-8C2A-6D5E3F09A1B2}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\{#AppExeName}
SetupIconFile=..\Blink.App\blink.ico
OutputDir=Output
OutputBaseFilename=Blink-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
; Per-user install: no admin prompt, HKCU autostart works without elevation.
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "korean";  MessagesFile: "compiler:Languages\Korean.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
korean.AutoStart=Windows 시작 시 Blink 자동 실행
english.AutoStart=Start Blink when Windows starts

[Tasks]
Name: "autostart";   Description: "{cm:AutoStart}";        GroupDescription: "{cm:AdditionalIcons}"; Flags: checkedonce
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#AppPublishDir}\{#AppExeName}";       DestDir: "{app}"; Flags: ignoreversion
; The worker is optional at install time; skip cleanly if it wasn't published.
Source: "{#WorkerPublishDir}\{#WorkerExeName}"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist

[Icons]
Name: "{group}\{#AppName}";                        Filename: "{app}\{#AppExeName}"
Name: "{group}\{cm:UninstallProgram,{#AppName}}";  Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}";                  Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Registry]
; Autostart via the per-user Run key (matches AutostartManager in the app).
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; \
  ValueType: string; ValueName: "{#AppName}"; ValueData: """{app}\{#AppExeName}"""; \
  Flags: uninsdeletevalue; Tasks: autostart

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#AppName}}"; \
  Flags: nowait postinstall skipifsilent
; Silent runs are the in-app auto-update path: relaunch the new build automatically.
Filename: "{app}\{#AppExeName}"; Flags: nowait; Check: WizardSilent
