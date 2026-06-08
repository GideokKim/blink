# Blink — Windows Build & Install Guide

Blink is a Korean-friendly instant-search desktop launcher.

- **`Blink.Core`** — the search engine (n-gram tokenizer, SQLite FTS5 store, parsers,
  indexer, search provider, config). Cross-platform (`net8.0`), fully unit-tested.
- **`Blink.Cli`** — a headless smoke harness for the engine (`net8.0`).
- **`Blink.App`** — the WPF spotlight UI (`net8.0-windows`, **Windows-only**).

> ⚠️ **`Blink.App` was authored on macOS and has NOT been compiled or run.** It targets
> WPF (`net8.0-windows`), which does not build on macOS/Linux, so it is deliberately
> **excluded from `Blink.sln`**. Everything in `Blink.App` is best-effort and must be
> built + verified on Windows. The engine (`Blink.Core` + tests) is fully verified.

---

## 1. Prerequisites (Windows 10 1809+ / Windows 11)

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (x64).
- Windows 10 build 17763 (1809) or later. Acrylic blur uses:
  - **Win11 (build 22000+):** DWM system backdrop + rounded corners.
  - **Win10 1809+:** `SetWindowCompositionAttribute` acrylic + an HRGN rounded region.

Verify the SDK:

```powershell
dotnet --version      # 8.0.x
```

---

## 2. Add the WPF app to the solution (one-time, on Windows)

`Blink.App` is not part of `Blink.sln` (it can't build on macOS where the repo was created).
On Windows, add it:

```powershell
cd <repo>
dotnet sln Blink.sln add Blink.App\Blink.App.csproj
```

---

## 3. Build & run

```powershell
# Build everything
dotnet build Blink.sln -c Release

# Run the engine tests (these already pass cross-platform)
dotnet test Blink.Core.Tests -c Release

# Run the WPF app
dotnet run --project Blink.App -c Release
```

The app is **resident**: it shows a tray icon and stays running. Press **Left Alt + Space**
to summon the spotlight. Right-click the tray icon for **Settings…** / **Quit**.

### Headless engine smoke test (no UI)

```powershell
dotnet run --project Blink.Cli -- index "C:\path\to\your\docs"
dotnet run --project Blink.Cli -- search 한글검색
dotnet run --project Blink.Cli -- search 글검          # 2-gram Korean substring
```

Config + DB live under `%APPDATA%\Blink\` (`config.json`, `index.db`).

---

## 4. Publish a single self-contained executable

```powershell
dotnet publish Blink.App -c Release -r win-x64 --self-contained `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true
```

Output: `Blink.App\bin\Release\net8.0-windows\win-x64\publish\Blink.App.exe`.

`IncludeNativeLibrariesForSelfExtract=true` is required so the bundled native
`e_sqlite3.dll` (FTS5) extracts correctly at runtime. On first launch the app runs an
**FTS5 self-test** and fails fast with a clear message if FTS5 is unavailable.

---

## 5. Install / autostart (optional)

- **Copy install:** place `Blink.App.exe` anywhere (e.g. `%LOCALAPPDATA%\Blink\`).
- **Run at login:** add a shortcut to `Blink.App.exe` in
  `shell:startup` (`%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup`), or add a
  `Run` registry value:

  ```powershell
  reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v Blink `
    /t REG_SZ /d "%LOCALAPPDATA%\Blink\Blink.App.exe" /f
  ```

A proper per-user installer (Inno Setup) now lives in **`installer/`** — it publishes the
app + indexer worker and offers the autostart task. See `installer/README.md`.

---

## 6. Manual verification checklist (AC4–AC9, Windows-only)

These acceptance criteria can only be checked on Windows (the engine ACs — AC1/2/3/6/7-logic/10 —
are covered by `dotnet test`):

- [ ] **AC4** Left **Alt+Space** summons a frameless, acrylic window at the **top-third** of
      the primary monitor and brings it to the foreground. **Right-Alt+Space does NOT trigger.**
      Acrylic renders (Win11 backdrop / Win10 `ACCENT_ENABLE_ACRYLICBLURBEHIND`); text `#111111`
      stays legible; if composition fails, a flat translucent window is the only acceptable fallback.
- [ ] **AC5** Typing triggers a **150 ms-debounced** search; clearing the box clears results;
      the first row is auto-selected.
- [ ] **AC6** **↑/↓** moves selection; the selected row inline-expands **≤5** content lines that
      contain all query tokens; the previous selection collapses.
- [ ] **AC7** Scrolling to the end appends the next page (offset += 50) up to **1000**; beyond the
      cap a row with the exact text **`1000+ — narrow your query`** appears.
- [ ] **AC8** **Enter** opens the file with the default handler then hides. If the open fails
      (e.g. file deleted), it reveals the **parent folder** + shows a **~2 s** footer toast, then
      hides. **Shift+Enter** always opens the parent folder.
- [ ] **AC9** **Esc** or focus loss hides the window (the app stays resident); focus-hide is
      suppressed while the fallback toast is visible.

---

## 7. Known assumptions to confirm on Windows

The WPF layer was written against the fixed `Blink.Core` interfaces without compilation.
Confirm these (each is a 1-line fix if wrong):

- `HotkeyHook` low-level hook + dedicated message pump; clean shutdown uses the **native**
  Win32 thread id (`GetCurrentThreadId`) with `PostThreadMessage(WM_QUIT)`.
- `ForegroundHelper.BringToForeground` uses `AttachThreadInput` (not Alt-synthesis).
- `AcrylicHelper.TryApply` OS branch: never call the Win11-only DWM attributes on Win10.
  Do **not** set `AllowsTransparency=true` on the Win11 backdrop path.
- `SearchController` debounce/pagination is tied to `DispatcherTimer` (WPF), so its logic is
  **not** unit-tested here; verify pagination + the `1000+` sentinel manually (or add a
  Windows-only test project — `Pagination` is factored out as pure, testable math).
- Folder picker uses `System.Windows.Forms.FolderBrowserDialog` (`UseWindowsForms=true`).
- Tray icon uses `System.Windows.Forms.NotifyIcon` with the default application icon.
