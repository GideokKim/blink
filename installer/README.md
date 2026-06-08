# Blink — Windows Installer (Inno Setup)

Builds a per-user installer (`Blink-Setup-<version>.exe`) for the WPF app plus the
EDR-isolation indexer worker. **Windows-only**; authored on macOS and not yet compiled.

## Prerequisites

- .NET 8 SDK (x64)
- [Inno Setup 6](https://jrsoftware.org/isdl.php) (provides `iscc` and the bundled
  `Korean.isl` language file)

## 1. Publish the binaries

From the repo root, on Windows PowerShell:

```powershell
# WPF app (self-contained single file; native e_sqlite3 must self-extract for FTS5)
dotnet publish Blink.App -c Release -r win-x64 --self-contained `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true

# Indexer worker (the process that gets allow-listed/signed for SMB in EDR environments)
dotnet publish Blink.Indexer.Worker -c Release -r win-x64 --self-contained `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

These produce:

- `Blink.App\bin\Release\net8.0-windows\win-x64\publish\Blink.App.exe`
- `Blink.Indexer.Worker\bin\Release\net8.0\win-x64\publish\Blink.Indexer.Worker.exe`

(the paths `blink.iss` expects).

## 2. Compile the installer

```powershell
iscc installer\blink.iss
```

Output: `installer\Output\Blink-Setup-0.1.0.exe`.

## What it does

- Installs per-user (no admin) to `%LOCALAPPDATA%\Programs\Blink`.
- Korean + English wizard.
- Optional **"Windows 시작 시 Blink 자동 실행"** task → writes the `HKCU\…\Run` value
  (same key the app's in-product autostart toggle uses; uninstall removes it).
- Start-menu entry, optional desktop icon, launch-after-install.

## EDR note

For locked-down environments, code-sign `Blink.Indexer.Worker.exe` (and the app) and
allow-list the worker for the SMB/NAS reads. The worker is the only process that touches
the indexed tree; the main app only reads/writes the local SQLite index.
