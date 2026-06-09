# Value-based theme/accent + per-folder index stats — design

Date: 2026-06-09
Branch: `feat/value-based-theme-folder-stats`
Release target: `v0.4.0`

## Goal

Implement the revised Settings UI from `blink_design_handoff/settings/README.md`:

1. **Theme** becomes a *value-based* base-color control (5 presets + hex field + live
   preview). Luminance of the base color picks the dark/light token set; surfaces are
   derived from the base color. A leading **시스템** chip keeps OS auto-follow.
2. **Accent color** becomes a *value-based* hex control (5 presets + hex + preview),
   applied live across both windows + launcher.
3. Each indexed-folder row shows **`{n} 파일 · {size} · 마지막 인덱싱 {상대시각}`**
   (or `대기 중…` when never indexed).

## Constraints

- `Blink.App` is **WPF, Windows-only** — not buildable/runnable on macOS. Only
  `Blink.Core`/`Blink.Cli` build + test locally (`dotnet test Blink.Core.Tests`).
  Pure logic therefore lives in `Blink.Core` so it is unit-tested; WPF wrappers stay thin.
- The release is cut by pushing a `v*` tag → `.github/workflows/release.yml` builds the
  Windows installer in CI. CI is the App-layer build gate.

## 1. Config model (`Blink.Core/Config/AppConfig.cs`)

New persisted fields (legacy fields retained for one-time migration on load):

| field | type | default | meaning |
|---|---|---|---|
| `theme_mode` | string | `"system"` | `"system"` (follow OS) or `"value"` (use `base_color`) |
| `base_color` | string | `"#0B0D12"` | window background hex; used when `theme_mode == "value"` |
| `accent` | string | `"#3B7FE3"` | accent hex |
| `folder_index_times` | `Dictionary<string,string>` | `{}` | path → ISO-8601 UTC of last successful index |

Migration on `Load()` when new fields are absent/empty:
- legacy `theme`: `"dark"` → mode `value` + base `#0B0D12`; `"light"` → mode `value` +
  base `#F6F7FA`; `"system"` (or unknown) → mode `system`.
- legacy `accent_hue` (int) → `accent` hex via `Oklch.ToColor(0.64, 0.155, hue)` math
  (replicated as pure code in Core; see ColorMath).
- Keep writing legacy `theme`/`accent_hue` too (best-effort) so older builds degrade
  gracefully — optional, low priority.

## 2. Pure color math (`Blink.Core/Theming/ColorMath.cs`) — testable

No WPF dependency. Operates on plain structs/tuples.

- `bool TryParseHex(string s, out (byte r, byte g, byte b) rgb)` — accepts `#RGB`,
  `#RRGGBB`, with/without `#`. Invalid → false.
- `string ToHex(byte r, byte g, byte b)` — `#RRGGBB`.
- `double RelativeLuminance(byte r, byte g, byte b)` — sRGB→linear, WCAG luminance 0..1.
- `(double h, double s, double l) ToHsl(...)` / `(byte,byte,byte) FromHsl(...)`.
- `(byte,byte,byte) Mix(rgbA, rgbB, double t)` — linear-ish interpolation.
- `(byte,byte,byte) AdjustLightness(rgb, double deltaL)` — for surface offsets.
- `(byte r,byte g,byte b) OklchToRgb(double l, double c, double hDeg)` — port of the
  App `Oklch` math so accent-hue migration is testable in Core.

The App `Oklch`/`ThemeManager` may delegate to this or keep their own copy; Core owns the
authoritative tested implementation.

## 3. `ThemeManager` refactor (`Blink.App/Theming/ThemeManager.cs`)

New entry point: `Apply(string themeMode, string baseHex, string accentHex)`.

1. Resolve **effective base color**: `themeMode == "system"` → `#0B0D12` (OS dark) or
   `#F6F7FA` (OS light) via existing `OsPrefersLight()`; else parse `baseHex` (invalid →
   last valid / default).
2. `RelativeLuminance(base) < 0.45` → **dark** token set, else **light**. Text / dim /
   faint / hairline / rowhover come from the fixed dark or light set (contrast-safe),
   exactly as today.
3. Derive surfaces from the base color by lightness offsets (lighter for dark bases,
   toward-white for light bases): `Surface` (= base), `Surface2`, `Inset`, `Titlebar`,
   `Tile`, `BgGlass`, `BgGlass2`. Keep the *relationships* of the current token set.
4. Derive accent tokens from `accentHex`: `Accent` (solid), `AccentSoft` (16% α),
   `AccentLine` (50% α), `RowSel` (~20% α), `Mark` (~30% α), `Blink.AccentColor`
   (solid Color for glows), `AccentGlow` (55%).

`Current` (Dark/Light) is set from the luminance decision. Expose effective base/accent so
`App`/launcher can persist + repaint. Keep `Toggle()` working (flip mode value base
between a dark and light preset) or retire it in favor of explicit values; tray
"테마 전환" should still work — map it to swapping base between `#0B0D12`/`#F6F7FA`.

## 4. Settings UI

### `Blink.App/Controls/SwatchHexPicker.xaml(.cs)` — new reusable control
- Row of preset swatch dots · divider · `#` + 6-char mono hex field · live preview box.
- Optional leading **시스템** chip (used by the theme picker only).
- `SelectedHex` (string), `IsSystem` (bool), and a `ColorChanged` event.
- Invalid hex → redden the input, keep last valid value. Custom (non-preset) value →
  deselect all preset dots. Selecting 시스템 → `IsSystem = true`, disable hex/swatches.

### `Blink.App/SettingsWindow.xaml(.cs)`
- **테마 row**: replace the 3 `RadioButton`s with a `SwatchHexPicker` whose presets are
  다크 `#0B0D12` · 슬레이트 `#14161C` · 미드나이트 `#0A0C18` · 라이트 `#F6F7FA` ·
  페이퍼 `#E9ECF2`, plus the 시스템 chip. Live-apply via `ThemeManager.Apply`.
- **강조 색상 row (new)**: a `SwatchHexPicker` (no 시스템 chip) with presets
  `#3B7FE3` · `#4F46E5` · `#06B6D4` · `#14B8A6` · `#F43F5E`. Live-apply.
- `Save_Click` persists `theme_mode`, `base_color`, `accent` into `AppConfig`.

## 5. Per-folder stats

### `Blink.Core/Store/IIndexStore.cs` + `SqliteFtsStore.cs`
- Add `(long FileCount, long TotalBytes) FolderStats(string root)`.
  - `WHERE doc_id = $p OR doc_id LIKE $like` (same prefix logic as `IterDocsUnder`).
  - `FileCount` counts bundle members:
    `SUM(CASE WHEN is_bundle=1 THEN member_count ELSE 1 END)`.
  - `TotalBytes = SUM(size)`.

### `Blink.App/SettingsWindow.xaml.cs` `FolderRow`
- New fields: `FileCount`, `SizeText`, `LastIndexedText`; computed `Sub`:
  - never indexed → `"대기 중…"`.
  - else → `"{n:N0} 파일 · {humanSize} · 마지막 인덱싱 {상대시각}"`.
- Helpers (in App, thin): human size (B/KB/MB/GB) and relative-time (방금/N분 전/N시간
  전/N일 전).
- `SettingsWindow` ctor gains a `Func<string,(long count,long bytes)>? statsProvider` and
  reads `folder_index_times` from `AppConfig`. Provider injected by `App.OpenSettings`
  (closes over `_store.FolderStats`).

## 6. Recording per-folder timestamps

### `Blink.App/IndexingService.cs`
- Add `event Action<string>? FolderCompleted;` fired after each folder's
  `Index` + `Pruner.Apply` succeeds.

### `Blink.App/App.xaml.cs`
- Subscribe: accumulate `path → DateTime.UtcNow` in a dict; on `Completed`, write into
  `_config.FolderIndexTimes` and `_config.Save()`.

## 7. Tests (macOS-runnable, `Blink.Core.Tests`)

- `ColorMathTests`: hex parse (3/6/invalid), round-trip `ToHex`, luminance ordering
  (black<white, known thresholds), HSL round-trip, mix endpoints, Oklch parity vs the
  existing accent presets.
- `AppConfigTests` (extend): migration from legacy `theme`/`accent_hue`; default values;
  `folder_index_times` round-trip through save/load.
- `SqliteFtsStoreTests` (extend): `FolderStats` count + bytes over a seeded set,
  including a bundle row (member_count) and a nested subfolder; non-matching root → (0,0).

## 8. Release v0.4.0

1. Land all changes on `feat/value-based-theme-folder-stats`; `dotnet test
   Blink.Core.Tests` green locally.
2. Merge to `main`.
3. `git tag v0.4.0 && git push origin v0.4.0` → `release.yml` builds the Windows
   installer + GitHub Release. (Tag push is the outward step; user pre-authorized via
   "릴리즈까지 진행해".)

## Out of scope

- `variantHud.jsx`, launcher Direction A/B work, hotkey rebinding, DB-path live switch.
- Real incremental per-file index timestamps (we record per-folder run completion time).
