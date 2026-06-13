# Blink

[![Release](https://img.shields.io/github/v/release/GideokKim/blink?label=release)](https://github.com/GideokKim/blink/releases/latest)
[![License](https://img.shields.io/badge/license-GPL--3.0-blue)](LICENSE)
[![Buy me a coffee](https://img.shields.io/badge/%E2%98%95%20buy%20me%20a%20coffee-KakaoPay-FFCD00?logo=kakaotalk&logoColor=black)](https://qr.kakaopay.com/281006011000003981911022)

A Korean-friendly instant-search desktop launcher — press **Left Alt + Space**, type, and
search file names *and* contents across large document/NAS trees. Built on .NET 8 with a
SQLite FTS5 engine and a WPF spotlight UI.

> 🌐 Languages: **English** · [한국어](README.md)

> Status: the search **engine** (`Blink.Core`) and the **indexer worker** are fully
> implemented and unit-tested (103 tests) cross-platform. The **WPF app** (`Blink.App`)
> targets `net8.0-windows` and must be built and verified on Windows — see
> [`BUILD-WINDOWS.md`](BUILD-WINDOWS.md).

## Features

- **Korean-aware full-text search** — an n-gram tokenizer (Hangul 2/3-grams) over SQLite
  FTS5, with NFC normalization, so 2-character substrings and mixed Korean/ASCII queries hit.
- **Rich content extraction** — indexes the *contents* of `.txt` / `.md`, `.xlsx`, `.pdf`,
  `.docx`, `.pptx`, `.hwpx` (Hancom), and `.rtf`. Unreadable/unknown files fall back to
  filename-only indexing.
- **Incremental indexing** — re-parses only new/modified files (by mtime); deletions are
  cleaned by a guarded pruner.
- **Junk exclusion** — Office lock files (`~$*.xlsx`), temp files, OS metadata, etc., plus
  an optional `.blinkignore` (gitignore-style).
- **Bundling for scale** — a folder of millions of sequentially-named images collapses to a
  single virtual entry instead of millions of rows; content files stay individually searchable.
- **Built for scale & locked-down networks**
  - 3-pass indexing over a disk-backed scan cache keeps memory flat on multi-million-file trees.
  - An out-of-process **indexer worker** isolates all SMB/NAS reads from the main app, for
    EDR/AV environments that block the main executable's network reads.
  - `drive_split` indexes a whole-drive root (`L:\`) as independent child folders.
- **Resident UI** — tray icon, global hotkey, acrylic spotlight window, inline match-line
  previews, autostart toggle.

## Repository layout

| Project | Target | Description |
|---|---|---|
| `Blink.Core` | `net8.0` | Search engine: tokenizer, FTS5 store, parsers, indexer, pruner, bundling, worker protocol. Cross-platform, unit-tested. |
| `Blink.Indexer.Worker` | `net8.0` | Standalone indexing process; streams store ops as JSON lines (EDR isolation). |
| `Blink.Cli` | `net8.0` | Headless harness: `index` / `search` / `status` / `prune`. |
| `Blink.Core.Tests` | `net8.0` | xUnit test suite (103 tests). |
| `Blink.App` | `net8.0-windows` | WPF spotlight UI. **Windows-only**; not in `Blink.sln`. |

## Quick start (engine — any OS with .NET 8)

```bash
# Run the test suite
dotnet test Blink.Core.Tests -c Release

# Index a folder and search it (headless)
dotnet run --project Blink.Cli -- index "/path/to/docs"
dotnet run --project Blink.Cli -- search 한글검색
dotnet run --project Blink.Cli -- search 글검          # 2-gram Korean substring
dotnet run --project Blink.Cli -- status               # db path, doc count, folders
dotnet run --project Blink.Cli -- prune "/path/to/docs"  # dry-run; add --apply to commit
```

Config and the index DB live under `%APPDATA%\Blink\` (`config.json`, `index.db`), or the
platform equivalent.

## Windows app & installer

- **Build / run the WPF app:** [`BUILD-WINDOWS.md`](BUILD-WINDOWS.md).
- **Build an installer:** yes — a per-user Inno Setup installer is provided in
  [`installer/`](installer/README.md). On Windows: publish the app + worker, then
  `iscc installer\blink.iss` produces `Blink-Setup-<version>.exe` (Korean/English wizard,
  optional autostart, bundles the indexer worker).
- **Cut a release:** push a `vX.Y.Z` tag and GitHub Actions builds the installer and
  publishes a Release with notes attached — see [`RELEASING.md`](RELEASING.md).

## License

**GPL-3.0 open source** — Blink is distributed under the
[GNU General Public License v3.0](LICENSE). Anyone may install, use, study, modify,
and redistribute it for free. If you distribute a modified version or a derivative,
you must release it under the same GPL-3.0 with its source (copyleft).
See [LICENSE](LICENSE) ([한국어 안내](LICENSE.ko.md)).

If Blink saves you time, you can buy me a coffee — see the
[Korean README](README.md#라이선스) for the support link. ☕
