# G-EQ — Handoff Document

**Date:** 2026-07-18 (rebranded "G Equalizer" → **"G-EQ"** across app/installer/website; repo made **public**; GitHub Release `v3.0.0` published with the rebuilt Windows installer; website rewritten from static HTML to **Astro + Tailwind** and wired to real download/GitHub links; added a side-by-side EQ-off/EQ-on video comparison section — clips not recorded yet, see "Website" section below for the exact TODO. Everything from the 2026-07-14 session below this point is unchanged/still accurate except where noted.)
**Repo:** https://github.com/ReDCLiF-Unknow/G-Equalizer (**public** as of 2026-07-18)
**Branch:** main

---

## What This Project Is

A Windows desktop app (C# / WPF / .NET 10) that applies system-wide audio equalization for PC gamers. It acts as a frontend controller for EqualizerAPO — it reads and writes EqualizerAPO's config file to change the EQ in real time without touching audio drivers manually.

Key features:
- 10-band EQ (32Hz–16kHz, ±12 dB per band) with per-band tooltips; double-click any slider to reset to 0 dB
- On/off toggle from the app or system tray
- Gaming presets: FPS, RPG, Cinematic, Music, Flat, PUBG + custom presets (save/import/export)
- Per-ear hearing calibration wizard (left/right separately via NAudio panning + sine tones); per-band re-test on results screen
- Real-time frequency visualizer (80-bar WASAPI loopback + FFT, or EQ-mode animation); 3 color modes: Gradient / Solid / Peak Glow
- AutoEQ headphone correction import (parametric .txt → blended 10-band preset)
- Mini / compact always-on-top widget (640×58 px default, resizable 400–any×58, horizontally-scrollable preset chip row)
- Global hotkeys: Ctrl+Alt+E (toggle), Ctrl+Alt+P (cycle preset)
- First-run onboarding walkthrough (4-step modal)
- Sound Boost: 0–20 dB preamp boost, toggle button in titlebar + slider in Settings, real-time apply
- Persists all state across restarts

---

## Current Status

**v3.0.0 released.** Avalonia cross-platform build. Installer and EXE in `dist/`, rebuilt 2026-07-01 with the stack-overflow fix, duplicate-UI-builder fixes, resizable main window, Settings scroll buttons, and Mini widget overlap fix. All four platform artifacts (Windows installer, macOS arm64/x64, Linux x64) are current as of the latest rebuild.

| Phase | Status |
|---|---|
| Phase 1: Project setup, EQ writer, tray, settings | **Done** |
| Phase 2: Preset switching UI + frequency visualizer | **Done** |
| Phase 3: Hearing calibration wizard (NAudio) | **Done** |
| Phase 4: Settings screen | **Done** |
| Phase 4: Installer (NSIS + portable ZIP) | **Done** |
| v2: Core visual redesign | **Done** |
| v2: Feature additions | **Done** |
| v2: Release build + distribution artifacts | **Done** |
| Post-ship: Custom icon, titlebar color, sound boost, UX polish | **Done** |
| v2.1: Calibration reference step, tray tooltip, preset share codes | **Done** |
| v2.2: AutoEQ import, calibration re-test, visualizer color modes | **Done** |
| v2.2: Button color fix (deep violet), release build | **Done** |
| v2.3: Auto-preset switching, scrollbar theme, Settings polish | **Done** |
| v2.4: Settings inline page, chip color fix, button sizing | **Done** |
| v2.5: Polish pass — tray icon fix, preset delete, reset all bands, DefaultPreset bug, tray quit bypass | **Done** |
| v2.5.1: Tray icon state sync on EQ toggle | **Done** |
| Avalonia migration (cross-platform: Win/Mac/Linux) | **Done** — stack overflow fixed, WPF project removed, v3.0.0 installer shipped |

---

## File Structure (as built)

```
GamingEqualizer/                (Avalonia — cross-platform)
  GamingEqualizer.csproj        .NET 10, NAudio + Newtonsoft.Json + Avalonia 12
  app.manifest                  requireAdministrator (release); switch to asInvoker for dev
  GlobalUsings.cs               Resolves WPF vs WinForms namespace conflicts
  DwmHelper.cs                  Static helper — ApplyDarkTitlebar(window) via DwmSetWindowAttribute
  App.xaml / App.xaml.cs        App entry, dark theme resource dict, tray init, first-run onboarding trigger
  MainWindow.xaml / .cs         10-band EQ UI, preset chips, toggle, visualizer, live mode, mini mode, band tooltips, boost button + inline settings panel (all settings logic lives here; SettingsWindow deleted in v2.4)
  MiniWindow.xaml / .cs         Always-on-top compact widget (500×58px, draggable)
  OnboardingWizard.xaml / .cs   4-step first-run walkthrough (Welcome / Presets / Hotkeys / Calibration)
  SavePresetDialog.xaml / .cs   Name-input dialog for saving custom presets
  CalibrationWizard.xaml / .cs  Per-ear hearing calibration: 14 steps (7 left + 7 right), panned sine tones
  ProcessMappingRow.cs          Simple data class for auto-preset exe→preset mapping rows (was inner class in SettingsWindow)
  HotkeyManager.cs              RegisterHotKey/UnregisterHotKey P/Invoke wrapper
  AudioSpectrumAnalyzer.cs      WasapiLoopbackCapture + FFT → 80-bar spectrum data
  TrayController.cs             NotifyIcon, Toggle/Open/Quit, hide-to-tray
  EQConfigWriter.cs             Apply(bands, boostDb) / ApplyPerEar(left, right, boostDb) / Bypass(), retry + Include fallback
  PresetManager.cs              Loads Presets/*.json, Reload(), falls back to Flat
  PresetShareCode.cs            Static Encode(float[]) / Decode(string) — 10 floats → URL-safe base64 (~56 chars)
  AutoEQImporter.cs             Static Import(filePath) — parses AutoEQ parametric .txt, evaluates each peaking filter at our 10 band freqs, returns float[10]
  Logger.cs                     Appends to %AppData%\GamingEqualizer\error.log
  Models/
    AppSettings.cs              Load/save JSON — bands, preset, cal (left/right/avg), onboarding flag, BoostDb, BoostEnabled
    Preset.cs
    HearingProfile.cs
  Presets/
    FPS.json / RPG.json / Cinematic.json / Flat.json / Music.json / PUBG.json
  Assets/
    app-icon.ico                Custom shield + EQ bars design, purple→pink, multi-size (16/32/48/256px)
    app-icon-backup.ico         Original placeholder icon (kept for reference)
    tray-icon-on.ico            Shield icon, full color — tray when EQ is active
    tray-icon-off.ico           Shield icon, desaturated gray — tray when EQ is disabled

%AppData%\GamingEqualizer\  (runtime, not in repo)
  AppSettings.json              Includes LastCalibrationLeft / LastCalibrationRight (per-ear) + HasCompletedOnboarding + BoostDb + BoostEnabled + VizColorMode
  HearingProfile.json
  error.log
```

---

## Distribution Artifacts

All in `dist/`:

| File | Size | Notes |
|---|---|---|
| `GEqualizer-Setup-3.0.0.exe` | ~31 MB | Windows — all-in-one NSIS installer, downloads + installs EqualizerAPO, installs G Equalizer, prompts reboot |
| `app/GamingEqualizer.exe` | ~100 MB | Windows — raw self-contained EXE (uncompressed, Avalonia) |
| `GEqualizer-macOS-arm64-3.0.0.zip` | ~41 MB | macOS Apple Silicon — `.app` bundle (zip). Unzip, right-click → Open to bypass Gatekeeper. `.icns` icon and `.dmg` need to be generated on macOS. |
| `GEqualizer-macOS-x64-3.0.0.zip` | ~43 MB | macOS Intel — same as above |
| `GEqualizer-linux-x64-3.0.0.tar.gz` | ~40 MB | Linux x64 — tar.gz. Extract and run `./GEqualizer-linux/GamingEqualizer`. `.AppImage` packaging needs Linux tools. |
| `installer.nsi` | — | NSIS source; rebuild with `& "C:\Program Files (x86)\NSIS\makensis.exe" installer.nsi` |

**Publish command** (run from `GamingEqualizer/`):
```
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o ..\dist\app
Copy-Item Assets\app-icon.ico ..\dist\app\app-icon.ico -Force
```

Then rebuild installer from `dist/`:
```
& "C:\Program Files (x86)\NSIS\makensis.exe" installer.nsi
```

---

## Known Issues / Things to Fix

None. All previously known issues are resolved.

| Was | Resolution |
|---|---|
| `app.manifest` was `asInvoker` | Now `requireAdministrator` in release build |
| Tray icons were placeholders | Replaced with custom shield design matching app palette |
| `Icon="Assets/app-icon.ico"` crashed on .NET 10 | Set programmatically via `BitmapFrame.Create(pack://...)` in `MainWindow` constructor |
| White Windows titlebar on all windows | Fixed via `DwmSetWindowAttribute(DWMWA_CAPTION_COLOR)` in `DwmHelper.ApplyDarkTitlebar()` — applied to all windows |
| Tray icons not appearing in single-file publish | Changed tray icons from `<Content CopyToOutputDirectory>` to `<Resource>` — now embedded in EXE, loaded via `Application.GetResourceStream(pack://...)` |
| Tray icon not switching on EQ toggle | `_tray?.SetEqState(enabled)` added to `MainWindow.SetEqState()` — icon now switches between colored and gray on every toggle |
| `dotnet publish` corrupts `dist/app/app-icon.ico` | Manually copy `Assets/app-icon.ico` → `dist/app/` after every publish, before rebuilding the NSIS installer |
| `DefaultPreset` setting saved but never applied | `RestoreState()` now loads the default preset on first launch (when all bands are 0) |
| Tray → Quit left EQ active in EqualizerAPO | Quit now calls `BypassAndQuit()` — writes bypass config before shutdown |
| `AppSettings.Load()` called twice on startup | `App.xaml.cs` now reads `mainWindow.Settings` instead of loading a second instance |

---

## Key Dependencies

| Dependency | Notes |
|---|---|
| EqualizerAPO | Downloaded and installed automatically by the NSIS installer. App detects it at `C:\Program Files\EqualizerAPO\`. Not bundled in the EXE due to licensing. |
| NAudio (NuGet) | Calibration sine tone playback + `PanningSampleProvider` for L/R ear separation + `WasapiLoopbackCapture` + `FastFourierTransform` for live visualizer |
| Newtonsoft.Json (NuGet) | Read/write preset and profile JSON files |
| .NET 10 | Target runtime. Published self-contained single-file. |

---

## Critical Design Decisions

- **UAC:** App manifest uses `requireAdministrator` so it can write to the EqualizerAPO config directory. Fallback: write to a user-writable path and chain via EqualizerAPO `Include` directive.
- **EQ filter spec:** Peaking EQ, Q = 1.41, ±12 dB range per band.
- **Per-ear calibration:** Each ear tested separately (7 frequencies × 2 ears = 14 steps). Signal is panned hard left/right via `PanningSampleProvider(monoSignalGenerator)`. Results stored as `LastCalibrationLeft[10]` + `LastCalibrationRight[10]`. Average stored in `LastCalibration[10]` for slider display. `EQConfigWriter.ApplyPerEar` writes `Channel: L` / `Channel: R` / `Channel: ALL` blocks. When the user subsequently switches presets, `BlendWithPreset` adds the per-ear deviation `(calSide[i] - calAvg[i])` on top of the preset gains — so calibration persists as a transparent hearing-correction layer across preset changes.
- **Onboarding:** `AppSettings.HasCompletedOnboarding` (default `false`). `App.xaml.cs` shows `OnboardingWizard` after `MainWindow` is shown on first run. If the user opts in to calibration on the final step, `MainWindow.OpenCalibrationWizard()` is called immediately after.
- **State storage:** `%AppData%\GamingEqualizer\AppSettings.json` — active preset, on/off state, band gains, launch-with-Windows flag, per-ear calibration, onboarding flag, `BoostDb`, `BoostEnabled`.
- **Sound Boost:** `BoostDb` (0–20 dB, default 0) folds into the EqualizerAPO `Preamp:` line as `Preamp: (-6 + boostDb) dB`. The `-6 dB` base headroom is always present to prevent clipping. `EQConfigWriter.Apply` and `ApplyPerEar` both accept `boostDb = 0f` as a default. `BoostEnabled` gates whether boost is applied — toggle button in titlebar, checkbox + slider in the inline Settings panel. Since Settings is now inline in MainWindow, boost changes call `RefreshBoostButton()` and `ApplyCurrentGains()` directly (no callback needed).
- **Titlebar color:** All windows call `DwmHelper.ApplyDarkTitlebar(this)` in `OnSourceInitialized`. The color `#1a0533` is stored as COLORREF `0x00330519` in `DwmHelper.cs`. Windows 11 only — on older Windows it silently no-ops.
- **App icon:** Set programmatically in `MainWindow` constructor via `BitmapFrame.Create("pack://application:,,,/Assets/app-icon.ico")` — avoids the .NET 10 XAML crash. `<ApplicationIcon>` in .csproj handles the exe/taskbar icon. Tray icons are `<Resource>` (embedded) and loaded via `Application.GetResourceStream(pack://...)` in `TrayController` — required for single-file publish.
- **Custom icon design:** Shield shape with 7 EQ bars, purple→pink gradient (`#7c3aed → #f472b6`), dark background `#16052E`. Generated with PowerShell + `System.Drawing` — see the generation script in the session history if you need to regenerate. `app-icon-backup.ico` is the original.
- **Slider double-click reset:** `slider.MouseDoubleClick += (_, _) => { slider.Value = 0; }` wired in `BuildSliders()` for each of the 10 sliders.
- **Error policy:** Config write failures show an error banner and revert; corrupted JSON files are skipped and logged to `error.log`; NAudio device failure cancels the calibration wizard with a clear message.
- **WPF + WinForms coexistence:** `UseWindowsForms=true` is needed for `NotifyIcon`. All ambiguities (`Application`, `Orientation`, `HorizontalAlignment`, `OpenFileDialog`, `SaveFileDialog`, `Button`, etc.) are resolved in `GlobalUsings.cs`. File-local aliases (`WpfColor`, `WpfRect`, `WpfEllipse`, `WpfButton`) handle per-file conflicts — do NOT use plain `Color` or `Point` without qualifying the namespace.
- **Visualizer array size:** `_vizCurrent` and `_vizTarget` are `double[80]` (one per bar). In EQ mode, `SetVizTargets()` interpolates from 10 band gains → 80 bars. In live mode, `AudioSpectrumAnalyzer` writes all 80 directly from FFT. Do not shrink these back to 10.
- **Visualizer color modes:** `_vizBrushes[80]` holds `SolidColorBrush` refs (one per bar) so color can be mutated without recreating objects. Gradient/Solid modes set colors once in `ApplyVizColorMode()`. Peak Glow updates `.Color` per-frame in `PositionVizBars()` based on bar height. Mode stored in `AppSettings.VizColorMode` (0/1/2). `VizBarColor(barIndex, intensity, t)` dispatches to the right color formula.
- **Global hotkeys:** Registered in `OnSourceInitialized` via `HotkeyManager`, unregistered in `OnClosed`. Ctrl+Alt+E = toggle, Ctrl+Alt+P = cycle preset. If hotkey registration fails silently (another app owns the combo), no error is shown.
- **Mini window:** `MiniWindow` is non-modal, shares the same `AppSettings` + `PresetManager` references as `MainWindow`. All state mutations (toggle, preset click) route back through `MainWindow` methods via delegates. `RefreshUI()` must be called on `MiniWindow` after any state change to keep it in sync.
- **Band tooltips:** `BandTooltips[10]` array in `MainWindow`. Set as `ToolTip` on the `StackPanel` column for each band — covers gain label, slider canvas, and freq label. No extra visual elements needed.

---

## v2 Feature Summary

| Feature | Notes |
|---|---|
| Purple→pink gradient palette | `#7c3aed → #f472b6` across all 10 bands |
| 80-bar animated gradient visualizer | Top of window; EQ-mode ripple animation or live WASAPI FFT |
| Custom slider visuals | Canvas overlay: colored fill from center + glowing band-colored thumb |
| Preset chip row | ToggleButtons replacing ComboBox |
| Titlebar | Logo icon, live status pill, Mini / Settings / Boost / Enable buttons |
| Music preset | V-shaped curve: bass + treble lift |
| SettingsWindow v2 styling | Matches purple→pink palette |
| Global hotkeys | Ctrl+Alt+E toggle, Ctrl+Alt+P cycle |
| Custom preset save | Name dialog → `Presets/*.json` |
| Preset import / export | `.json` files via Settings |
| Preset transition animations | Smooth slider sweep via `DispatcherTimer` |
| Mini / compact mode | Always-on-top 500×58 widget, draggable |
| Live audio visualizer | WASAPI loopback + FFT, toggleable |
| First-run onboarding walkthrough | 4-step modal with calibration opt-in |
| Per-ear hearing calibration | 14-step wizard, L/R panning, blended into EQ config |
| Band tooltips | Hover any slider column to see frequency description |
| v2 release build | `GEqualizer-Setup-2.0.0.exe` (48 MB) + portable ZIP (66 MB) |

## Post-ship Polish (this session)

| Feature | Notes |
|---|---|
| Custom app icon | Shield + 7 EQ bars, purple→pink, generated via PowerShell + System.Drawing. 16/32/48/256px |
| Custom tray icons | Same shield — on = full color, off = desaturated gray. Changed to `<Content>` so they copy to output dir |
| Window icon (titlebar + taskbar) | Set via `BitmapFrame.Create(pack://...)` in `MainWindow` constructor |
| Dark titlebar on all windows | `DwmHelper.ApplyDarkTitlebar()` applied to MainWindow, SettingsWindow, CalibrationWizard, OnboardingWizard, SavePresetDialog |
| Sound Boost | ⚡ BOOST toggle in titlebar + 0–20 dB slider in Settings. Folds into EqualizerAPO `Preamp:` line. Real-time apply via callback |
| Slider double-click reset | Double-click any EQ band slider to snap it back to 0 dB |

---

## v2.1 Features (this session)

| Feature | Notes |
|---|---|
| Calibration reference level warning | New step 0 in `CalibrationWizard` — plays 1kHz tone at fixed `-20 dB`, instructs user to set system volume before calibration begins. Ear pills + slider hidden on this step. `_phase` starts at `-1` and advances to `0` on Next. |
| Tray tooltip | `TrayController.UpdateTooltip()` — called from `MainWindow.RefreshTrayTooltip()` after every EQ toggle and `ApplyCurrentGains()`. Format: `"G Equalizer [ON] — FPS · Boost +7dB"`. `MainWindow` holds a `_tray` ref set via `SetTray()` from `App.xaml.cs`. |
| Preset share codes | `PresetShareCode.cs` — `Encode`: 10×float32 LE → URL-safe base64 (~56 chars). `Decode`: validates length (40 bytes), clamps to ±12 dB. Two new buttons in Settings PRESETS section: Copy (to clipboard) and Paste (decode → `SavePresetDialog` → save JSON → sets `ImportedPreset` → MainWindow picks up on Settings close). |
| Installer versioned to 2.1.0 | `installer.nsi` `APP_VERSION` updated. Installer EXE icon set via top-level `Icon` directive + `MUI_ICON`/`MUI_UNICON`. |

---

## v2.2 Features (this session)

| Feature | Notes |
|---|---|
| AutoEQ headphone correction import | `AutoEQImporter.cs` — parses AutoEQ parametric `.txt` (peaking filters only; shelves skipped). For each of our 10 fixed band freqs, sums gain contributions from all filters via `G / (1 + (Q × (f/fc − fc/f))²)`. Clamped to ±12 dB. "⬇ Import AutoEQ (.txt)" button in Settings → HEADPHONE CORRECTION section, opens file picker, prompts for preset name (pre-filled from filename) via `SavePresetDialog`, saves JSON, sets `ImportedPreset` — picked up by MainWindow on Settings close. |
| Calibration per-band re-test | Results screen replaced with a 5-column grid: Freq \| Left dB \| ↻ L \| Right dB \| ↻ R. Each ↻ button enters single-step re-test mode for that frequency × ear — plays tone, user adjusts slider, "Done" saves just that threshold and refreshes the results grid. Full calibration not needed. |
| Visualizer color modes | 3 modes cycled by "◈" button next to LIVE in the visualizer header. **Gradient** (default): purple→pink across 80 bars. **Solid**: flat `#7c3aed` accent. **Peak Glow**: bars interpolate dark→gradient color→white based on bar height. Mode persisted in `AppSettings.VizColorMode`. Static modes (Gradient/Solid) set brushes once; Peak Glow updates `SolidColorBrush.Color` per-frame in `PositionVizBars`. |
| Button color fix | `PrimaryButtonStyle` in `App.xaml` changed from near-transparent purple (`#7c3aed14`) to solid dark purple (`#3b1f7a` bg / `#7c3aed` border / `#e0d4ff` text). The transparent style was picking up the user's Windows system accent color (green), causing Save, Calibrate, ENABLE, and other primary buttons to render green instead of purple. |

---

## v2.3 Features (this session)

| Feature | Notes |
|---|---|
| Button color (deep violet) | `PrimaryButtonStyle` rebuilt with explicit `ControlTemplate` — deep violet `#5b21b6` bg, `#7c3aed` border, lightens on hover, darkens on press. System accent color can no longer bleed through. |
| Auto-preset switching | Settings → AUTO-PRESET SWITCHING section. Checkbox to opt in. `DispatcherTimer` polls `GetForegroundWindow` → `GetWindowThreadProcessId` → `Process.GetProcessById` every 2s. Maps exe name → preset via `AppSettings.ProcessPresetMap` (Dictionary with `OrdinalIgnoreCase`). Editable in Settings: add row (TextBox + ComboBox + ＋ Add), ✕ remove per row. Default mappings: cs2.exe/r5apex.exe/VALORANT/RainbowSix → FPS, Spotify → Music. Tray tooltip refreshes on switch. OrdinalIgnoreCase comparer re-applied after JSON deserialization (Newtonsoft loses it). |
| Scrollbar theme | Custom `ScrollBar` style in `App.xaml` — 6px wide, dark `#0d0d1a` track, `#7c3aed` purple thumb, `#a78bfa` on hover, `#f472b6` pink while dragging. Applied app-wide. |
| Settings window polish | Height 570 → 720px. ScrollViewer `Padding="0,0,10,0"` so scrollbar doesn't overlap content. `NewExeBox` placeholder (`GotFocus`/`LostFocus` handlers, dim text). `IconButtonStyle` for ✕ remove buttons — borderless, dim by default, pink on hover. |

---

## v2.4 Features (this session)

| Feature | Notes |
|---|---|
| Settings as inline page | `SettingsWindow.xaml/.cs` deleted. All settings content embedded as a collapsible `Border` (rows 2–4 of MainWindow Grid). "⚙ Settings" button toggles to "← Back". `PopulateSettingsPanel()` initialises state on open. All event handlers and logic moved into `MainWindow.xaml.cs`. `ProcessMappingRow` extracted to its own file. |
| Preset chip color fix | `ChipStyle` `IsChecked` trigger background changed from `#7c3aed1a` (10% alpha — Windows accent bled through as green) to solid `#2d1060`. Border changed to solid `#7c3aed`. System accent color can no longer show through. |
| Save/Calibrate button sizing | Added `Height="26" Padding="10,0" FontSize="11" VerticalAlignment="Center"` to the Save and Calibrate buttons in the preset chip row so they sit flush with the row height rather than overflowing it. |

---

## v2.5 Polish (this session)

| Fix | Notes |
|---|---|
| Tray icon fix (single-file publish) | Tray icons changed from `<Content>` to `<Resource>` in .csproj. `TrayController.LoadIcon()` now uses `Application.GetResourceStream(pack://...)` instead of file path — works correctly in single-file published EXE |
| Reset all bands button | "Reset all" button in the EQUALIZER section header — zeroes all 10 sliders with the same smooth animated transition as a preset switch. Switches active chip to Custom |
| Preset deletion | Custom presets now show a ✕ button next to their chip. Built-in presets (Flat, FPS, RPG, Cinematic, Music) are protected. Deleting removes the JSON file, the chip, and falls back to Flat if it was active |
| DefaultPreset bug fix | `RestoreState()` now applies `DefaultPreset` on first launch (when all bands are 0). Previously the setting was saved but never read |
| Tray quit bypasses EQ | Quit from tray context menu now calls `BypassAndQuit()` — writes EqualizerAPO bypass config before shutting down so EQ doesn't stay active after exit |
| Double AppSettings.Load() fix | `App.xaml.cs` now uses `mainWindow.Settings` (new public property) instead of calling `AppSettings.Load()` a second time on startup |

---

## Out of Scope

- Per-app EQ
- Microphone processing
- Cloud sync

---

## Website (Astro + Tailwind, built and wired to real downloads — not deployed)

**2026-07-18 session:** the app and installer were rebranded from "G Equalizer" to **"G-EQ"** across UI, tray, onboarding, and the installer (internal `AssemblyName`/namespace/`%AppData%` path/registry key were deliberately left as `GamingEqualizer` to avoid breaking existing users' saved presets — see the rebrand commit). The repo (`ReDCLiF-Unknow/G-Equalizer`) was made **public**, and a GitHub Release [`v3.0.0`](https://github.com/ReDCLiF-Unknow/G-Equalizer/releases/tag/v3.0.0) was published with the rebuilt, fully-rebranded Windows installer (`G-EQ-Setup-3.0.0.exe`) plus the existing macOS arm64/x64 zips and Linux tarball — those three are flagged in the release notes as **pre-rebrand and unverified on real hardware** (same caveat as before, just carried forward).

- **Location:** `website/` — a real Astro project now (was a single static `index.html`; converted this session). `src/layouts/Layout.astro` + `src/components/{Nav,Hero,Specs,Demo,Compare,Download,Footer}.astro`, composed in `src/pages/index.astro`. Styling is Tailwind v4 (`@tailwindcss/vite`), with the original CSS custom properties (`--bg`, `--accent`, etc., dark by default, `[data-theme="light"]` override) mapped into Tailwind's theme via `@theme inline` in `src/styles/global.css` — so utilities like `bg-accent`/`text-text-dim`/`border-line` work directly. `npm run dev` (port 4321) / `npm run build` (→ `website/dist/`, gitignored).
- **Design direction unchanged:** hardware-faceplate / spec-sheet aesthetic — near-black violet ground, violet→pink accent, mono type for every number, matches the app itself.
- **Sections (in order):** sticky nav → hero with live SVG frequency-response curve (real PUBG preset data) → spec table → hand-built CSS recreation of the app chrome (no real screenshot yet) → **new: side-by-side EQ-off/EQ-on video comparison** → download cards → footer.
- **Download links are now real:** Windows card links straight to the `G-EQ-Setup-3.0.0.exe` release asset; Linux links straight to the tarball; macOS links to the release page itself (since there are two arch variants, arm64/x64, and picking one for the user would be a guess). Footer "GitHub →" links to the now-public repo.
- **⚠️ TODO — comparison videos not yet recorded:** `src/components/Compare.astro` renders two `<video>` players (labels "EQ Off — raw audio" / "EQ On — PUBG preset") plus a synced "▶ Play both" button, but the actual clips don't exist yet. Drop them in at `website/public/media/eq-off.mp4` and `website/public/media/eq-on.mp4` (exact filenames, see `website/public/media/README.md`) — same source recording, same length, only the EQ differs. `.mp4`/H.264 recommended for browser compatibility. **Remind the user about this if it comes up idle for a while — they said they'd provide the clips later.**
- **Not decided yet:** hosting (GitHub Pages / Netlify / Vercel), domain name, whether to add a changelog/blog page later.
- **Housekeeping left over from the rebrand session:** `dist/GEqualizer-Setup-3.0.0.exe` (the old, pre-rebrand installer, superseded by `dist/G-EQ-Setup-3.0.0.exe`) is still sitting on disk, untracked in git — user said to leave it for now. NSIS and the GitHub CLI (`gh`) were installed on this dev machine via `winget` this session (both were missing); the NSIS `inetc` plugin is now vendored at `dist/nsis-plugins/x86-unicode/INetC.dll` and wired in via `!addplugindir` in `installer.nsi`, so the installer can be rebuilt without writing to `Program Files`.

---

## v3 Planning

Full spec: [v3-concept.md](v3-concept.md)

| Feature | Priority | Complexity | Status | Notes |
|---|---|---|---|---|
| Tray tooltip | High | Low | **Done** | Shows "G Equalizer [ON] — FPS · Boost +7dB" on hover; updates on toggle/preset/boost change |
| Calibration reference level warning | High | Low | **Done** | Step 0 in CalibrationWizard: fixed 1kHz reference tone, ask user to set system volume before starting |
| Preset share codes (base64 export/import) | High | Medium | **Done** | `PresetShareCode.cs` — Encode/Decode. Copy/Paste buttons in Settings → PRESETS section |
| AutoEQ headphone correction import | Medium | Medium | **Done** | `AutoEQImporter.cs`. "⬇ Import AutoEQ (.txt)" in Settings → PRESETS |
| Calibration re-test individual bands | Medium | Medium | **Done** | Results screen grid with ↻ L / ↻ R per frequency |
| Visualizer color mode toggle | Low | Low | **Done** | Gradient / Solid / Peak Glow — "◈" button next to LIVE |
| Auto-preset switching | Low | High | **Done** | `DispatcherTimer` polls `GetForegroundWindow` → process name every 2s. Editable exe→preset map in Settings → AUTO-PRESET SWITCHING section. Toggle to enable/disable. Tray tooltip updates on switch. |
| PUBG preset (louder footsteps) | Medium | Low | **Done** | `Presets/PUBG.json` — bands `[-3, -4, -2, -1, 1, 4, 6, 7, 5, 2]` (32Hz–16kHz). Cuts rumbling bass that masks footstep audio, boosts 1k–8kHz (peak +7dB at 4kHz) where footstep surface texture/directional transients live. Registered in `BuiltInPresets` (protected from deletion) in `MainWindow.axaml.cs`. Default auto-preset-switching map now includes `TslGame.exe → PUBG` in `AppSettings.cs`. **Not yet rebuilt into dist artifacts** — only a Debug build has been run/smoke-launched so far. |

**Where to start next session:** ✅ The stack-overflow regression is fixed and confirmed via live manual testing (repeated tray hide/restore, no crash — see "Avalonia Migration" section below for the root cause and fix). Also fixed this session: duplicate preset chips/sliders/visualizer bars on `OnOpened` re-fire, leaked `DispatcherTimer`s, non-resizable main window, no scroll-wheel-free way to navigate Settings, and Mini widget preset chips overlapping the ON/OFF switch on narrower/high-DPI displays. All four distribution artifacts have been rebuilt with every fix from this session.

Note: automated UI click-testing via computer-use tools does not work on this build — the app's `requireAdministrator` manifest triggers Windows UIPI, which silently blocks simulated mouse/keyboard input to elevated windows. Manual clicking is required for any further live verification.

Remaining packaging steps: macOS `.dmg` (`dist/make-dmg.sh`, needs real Mac) and Linux `.AppImage` (`dist/make-appimage.sh`, needs real Linux). The EQ backends (`MacEQBackend`, `LinuxEQBackend`) are implemented but need real-device smoke testing — the current macOS/Linux archives are cross-published from Windows and unverified on real hardware.

---

## Avalonia Migration (in progress)

**Goal:** Port the WPF UI to Avalonia 12 so the app runs on Windows, macOS, and Linux. EQ backends: Windows → EqualizerAPO, macOS → eqMac HTTP API, Linux → EasyEffects preset file + CLI.

**Strategy:** New project `GamingEqualizer.Avalonia/` lives alongside the original `GamingEqualizer/` (WPF). WPF project stays intact until Avalonia port is complete and compiling.

### What's done (all files complete)

| File | Status | Notes |
|---|---|---|
| `GamingEqualizer.Avalonia.csproj` | ✅ Done | `net10.0` (no -windows), NAudio + Newtonsoft.Json + Avalonia 12.0.5 |
| `Program.cs` | ✅ Done | `GamingEqualizer` namespace, `UsePlatformDetect()` |
| `GlobalUsings.cs` | ✅ Done | `Ellipse`/`Rectangle` aliased explicitly (avoids `Path` ambiguity with `System.IO.Path`); `using Avalonia.Styling` for `ControlTheme` |
| `Platform/IEQBackend.cs` | ✅ Done | Interface: Apply, ApplyPerEar, Bypass, IsAvailable |
| `Platform/WindowsEQBackend.cs` | ✅ Done | Instance `_writer = new EQConfigWriter()`, `IsAvailable` calls `EQConfigWriter.IsEqualizerApoInstalled()` |
| `Platform/StubEQBackend.cs` | ✅ Done | No-op, logs message |
| `Platform/PlatformServices.cs` | ✅ Done | Factory: `IsWindows()` → WindowsEQBackend else Stub |
| `HotkeyManager.cs` | ✅ Done | Rewritten to take `IntPtr` instead of `HwndSource` |
| `DwmHelper.cs` | ✅ Done | Rewritten to take `IntPtr`; wrapped in `IsWindows()` guard |
| `TrayController.cs` | ✅ Done | Uses Avalonia `TrayIcon` + `NativeMenu` instead of WinForms NotifyIcon |
| `MsgBox.cs` | ✅ Done | Simple async helper dialog (replaces WPF `MessageBox.Show`) |
| `App.axaml` | ✅ Done | All `ControlTheme` elements in `Application.Resources > ResourceDictionary` (NOT in `Styles`); `Application.Styles` has FluentTheme + style classes |
| `App.axaml.cs` | ✅ Done | `desktop.Exit` event for tray dispose (no `OnExiting()` override in Avalonia 12) |
| `MainWindow.axaml` | ✅ Done | Full layout ported; `IsSnapToTicks` removed (doesn't exist in Avalonia 12 Slider) |
| `MainWindow.axaml.cs` | ✅ Done | Clipboard via `ClipboardExtensions` (`TryGetTextAsync`/`SetTextAsync`); null guard in `PositionVizBars` |
| `MiniWindow.axaml` | ✅ Done | `WindowDecorations="None"` (not obsolete `SystemDecorations`) |
| `MiniWindow.axaml.cs` | ✅ Done | Pulse timer animates status dot; `BeginMoveDrag(e)` for drag |
| `SavePresetDialog.axaml` | ✅ Done | `PlaceholderText` (not obsolete `Watermark`) |
| `SavePresetDialog.axaml.cs` | ✅ Done | `Close(true/false)` instead of WPF `DialogResult`; `Key.Return` not `Key.Enter` |
| `OnboardingWizard.axaml` | ✅ Done | `BoxShadow` on logo border; step dots with `IsVisible` toggles |
| `OnboardingWizard.axaml.cs` | ✅ Done | `Control[]` pages, `Ellipse[]` dots; `ShouldRunCalibration` public property |
| `CalibrationWizard.axaml` | ✅ Done | `WizardPanel`/`ResultsPanel` toggled by `IsVisible` |
| `CalibrationWizard.axaml.cs` | ✅ Done | `RangeBaseValueChangedEventArgs`; `LinearGradientBrush` via object initializer + `RelativePoint`; `OnClosed` disposes NAudio |
| All platform-agnostic logic files | ✅ Done | Copied verbatim: Models, PresetManager, EQConfigWriter, AutoEQImporter, PresetShareCode, Logger, ProcessMappingRow, AudioSpectrumAnalyzer, Presets/*.json, Assets/*.ico |

### Key Avalonia vs WPF API differences (reference)

- `Visibility.Visible/Collapsed` → `IsVisible = true/false` (code), `IsVisible="True/False"` (XAML)
- `Window.ShowDialog()` → `await window.ShowDialog<bool>(owner)` (returns `bool` not `bool?`)
- `DialogResult = true/false` → `Close(true/false)`  
- `DropShadowEffect` → `BoxShadow="0 0 12 0 #color"` on `Border`
- `DispatcherTimer` → `Avalonia.Threading.DispatcherTimer` (same API)
- `Application.Current.Dispatcher.Invoke` → `Dispatcher.UIThread.InvokeAsync`
- `Button.Style = (Style)Resources["X"]` → `Button.Theme = (ControlTheme)Resources["X"]`; `Style = null` → `Theme = null`
- `ControlTemplate.Triggers` → `Style Selector` pseudo-classes (`:pointerover`, `:pressed`, `:checked`)
- `HwndSource` → `TryGetPlatformHandle()?.Handle` (wrap in `OperatingSystem.IsWindows()`)
- `Win32 WndProc hook` → subclass via `SetWindowLongPtr` (see plan detail below)
- `OpenFileDialog` → `await this.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions{...})`
- `SaveFileDialog` → `await this.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions{...})`
- `Clipboard.SetText(s)` → `await this.Clipboard!.SetTextAsync(s)`
- `Clipboard.GetText()` → `await this.Clipboard!.GetTextAsync()`
- `MessageBox.Show(...)` → `await MsgBox.Info(text, title, owner)` or `await MsgBox.Confirm(text, title, owner)`
- `Canvas.SetLeft/Top` → same static methods in Avalonia
- `DoubleTapped` replaces `MouseDoubleClick` for slider reset
- `PointerPressed` replaces `MouseLeftButtonDown` for drag-to-move
- `window.BeginMoveDrag(e)` replaces `DragMove()`
- `FontWeight.SemiBold` → same in Avalonia
- `ToolTip` attribute → `ToolTip.Tip` in Avalonia XAML
- `IsSnapToTickEnabled` → `IsSnapToTicks`
- `CheckBox.Checked/Unchecked` events → `IsCheckedChanged` (single event)

### Win32 hotkey subclassing for Avalonia (Windows-only, for MainWindow)

```csharp
[DllImport("user32.dll")] static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, WndProcDelegate newProc);
[DllImport("user32.dll")] static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
private WndProcDelegate? _wndProcDelegate; // must hold ref to prevent GC
private IntPtr _originalWndProc;
private IntPtr _hwnd;

// In OnOpened:
if (OperatingSystem.IsWindows())
{
    _hwnd = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
    if (_hwnd != IntPtr.Zero)
    {
        HotkeyManager.Register(_hwnd);
        _wndProcDelegate = WndProc;
        _originalWndProc = SetWindowLongPtr(_hwnd, -4, _wndProcDelegate); // GWL_WNDPROC = -4
    }
    DwmHelper.ApplyDarkTitlebar(_hwnd);
}

// WndProc method:
private IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
{
    if (msg == HotkeyManager.WM_HOTKEY)
    {
        int id = wParam.ToInt32();
        if (id == HotkeyManager.HK_TOGGLE)
            Dispatcher.UIThread.InvokeAsync(() => { SetEqState(!_settings.EqEnabled, true); _settings.Save(); SyncMiniWindow(); });
        else if (id == HotkeyManager.HK_CYCLE)
            Dispatcher.UIThread.InvokeAsync(() => { CyclePreset(); SyncMiniWindow(); });
        return IntPtr.Zero;
    }
    return CallWindowProc(_originalWndProc, hwnd, msg, wParam, lParam);
}

// In OnClosed:
if (_hwnd != IntPtr.Zero) HotkeyManager.Unregister(_hwnd);
```

### Smoke test results (v3.0-beta, 2026-06-28)

| Feature | Result |
|---|---|
| EQ toggle (ENABLE → DISABLE) | ✅ Pass |
| Status pill text ("EQ ACTIVE" / "EQ OFF") | ✅ Pass |
| Preset chip switching (FPS loads correct curve + visualizer) | ✅ Pass |
| Settings panel open (all sections visible) | ✅ Pass |
| Mini mode open (500×58 bar) | ✅ Pass |
| Mini → Expand returns to main window | ✅ Pass |
| Save Preset button | ❌ **Stack overflow crash** — see Known Issues below |

### Known Issues (Avalonia port) — ✅ RESOLVED 2026-07-01

**Stack overflow (`0xc00000fd`) — history of the regression and the actual fix, kept for reference.**

Original root cause (2026-06-28): `SavePresetDialog.ShowDialog<bool>(this)` triggers an Avalonia layout pass on the main window, which fires `SizeChanged` on `VisualizerCanvas`, which calls `PositionVizBars()`, which sets `Canvas.SetLeft/SetTop` and Width/Height on viz bars, triggering another layout pass → infinite recursion.

Applied fix: added a reentrancy guard (`_positioningVizBars` bool) around the body of `PositionVizBars()` in `MainWindow.axaml.cs`. Verified present in the compiled binary (`dist/app/GamingEqualizer.exe`, confirmed via string search — `PositionVizBars`, `positioningVizBars`, and `MacEQBackend` all found in the exe, so this is the current build).

**Despite the fix being in the shipped binary, the app crashed again** with the identical exception code `0xc00000fd` (STATUS_STACK_OVERFLOW) in `ntdll.dll`, twice:
- 2026-06-30 23:57:48 — Fault offset `0x96b4e`
- 2026-07-01 18:02:20 — Fault offset `0x96bea` (crashed spontaneously — user was not actively clicking Save Preset at the time, just had the app open)

This means either:
1. The reentrancy guard doesn't cover the actual recursive path (there may be a second, different infinite-layout loop not going through `PositionVizBars`), or
2. The crash is unrelated to Save Preset entirely and is a general Avalonia layout recursion triggered by something else (window resize, mini-mode toggle, timer tick racing with layout, etc.)

**Diagnostic assets available for next session:**
- Windows generated crash dumps at `%LOCALAPPDATA%\CrashDumps\GamingEqualizer.exe.*.dmp` — 5 dumps found spanning 2026-06-28 through 2026-07-01, including the two most recent regressions.
- `dotnet-dump` global tool was installed (`dotnet tool install --global dotnet-dump`) specifically to analyze these dumps with `dotnet-dump analyze <dump> --command "clrstack"` for a managed call stack — **this analysis was started but not completed/reviewed**. This is the fastest next step to find the real recursive call chain.
- `%AppData%\GamingEqualizer\error.log` was checked and only contains old, unrelated EqualizerAPO config-write permission errors (2026-06-23 to 2026-06-25) — no useful signal for this crash.

**RESOLVED (2026-07-01):** `dotnet-dump analyze GamingEqualizer.exe.8132.dmp --command clrstack` showed the real recursion — it was never in `PositionVizBars`. The managed stack was thousands of frames of `WndProc → CallWindowProc → WndProc → CallWindowProc → ...`, i.e. the Win32 hotkey subclassing in `MainWindow.axaml.cs` was calling itself forever.

Root cause: `OnOpened()` re-runs the subclassing block (`SetWindowLongPtr(_hwnd, -4, _wndProcDelegate)`) every time the window is opened/shown — not just on first launch. `OnOpened` can fire more than once for the same `hwnd` (e.g. hide-to-tray then restore). On the second call, `SetWindowLongPtr` returns the *currently installed* proc as "previous" — which by then is our own `WndProc` thunk from the first subclass — and that got stored into `_originalWndProc`, overwriting the real original. From then on `CallWindowProc(_originalWndProc, ...)` called back into `WndProc` itself, recursing until the stack overflowed (`0xc00000fd`). This explains both the Save Preset crash and the "spontaneous" crash with no user interaction (any tray hide/restore cycle would trigger it).

Fix applied: guarded the subclassing block in `OnOpened` (`MainWindow.axaml.cs`) so it only runs once per hwnd (`if (_hwnd != hwnd) { ... }`), using a local `hwnd` variable and only assigning `_hwnd`/subclassing inside the guard. `HotkeyManager.Register(hwnd)` and `DwmHelper.ApplyDarkTitlebar` still run every time `OnOpened` fires (safe/idempotent), only the `SetWindowLongPtr` subclass call is now one-shot.

**Related bug found during live testing:** confirming `OnOpened` really does re-fire (tray hide/restore) surfaced a second class of bug — several `OnOpened`-driven builder methods in `MainWindow.axaml.cs` were not idempotent:
- `BuildPresetChips()` appended to `ChipPanel.Children`/`_chips` without clearing first → duplicated preset chip row on every restore from tray (visually confirmed: "Cinematic, Flat, FPS, Music, RPG, Custom, Cinematic, Flat, ..."). Fixed: clear `ChipPanel.Children` and `_chips` at the top of the method.
- `BuildSliders()` appended to `SliderGrid.Children` without clearing → duplicate slider columns. Fixed: clear `SliderGrid.Children` first.
- `BuildVisualizer()` appended to `VisualizerCanvas.Children` without clearing, and started a new `DispatcherTimer` every call without stopping the previous one → duplicate viz bars plus leaked ever-accumulating timers ticking in the background. Fixed: clear `VisualizerCanvas.Children` and `_vizTimer?.Stop()` before rebuilding.
- `StartPulse()` had the same leaked-timer pattern (`_pulseTimer` recreated without stopping the old one). Fixed: `_pulseTimer?.Stop()` before reassigning.
- `RefreshAutoPresetTimer()` already guarded against re-creation (`if (_autoPresetTimer == null)`) — left as-is.

Republished (`dotnet publish` win-x64 self-contained single-file) and rebuilt `GEqualizer-Setup-3.0.0.exe` with all of the above fixes. Live smoke test done (2026-07-01): repeated tray hide/restore, preset chips stayed as a single clean row, no crash, Save Preset worked. **Confirmed fixed.**

Since the duplicate-builder bug (`BuildPresetChips`/`BuildSliders`/`BuildVisualizer`/`StartPulse`) lives in shared cross-platform code, not a Windows-only path, the macOS and Linux archives (built 2026-06-30, before this fix) were also stale. Cross-published and repackaged all four distribution artifacts from this Windows machine via `dotnet publish -r <rid> --self-contained true -p:PublishSingleFile=true`:
- `GEqualizer-Setup-3.0.0.exe` (win-x64, NSIS installer)
- `GEqualizer-macOS-arm64-3.0.0.zip` / `GEqualizer-macOS-x64-3.0.0.zip` — `.app` bundle reassembled by hand (Info.plist + AppIcon.icns + publish output under `Contents/MacOS`), matching the structure of the previous release zips. Not smoke-tested (no Mac hardware available) — the underlying WndProc/hotkey fix is Windows-only anyway and doesn't apply here, but the duplicate-builder fix does.
- `GEqualizer-linux-x64-3.0.0.tar.gz` — same publish + repack, not smoke-tested (no Linux hardware available).

**2026-07-01, later same day — additional UX fixes, all four artifacts rebuilt again:**
- Main window is now resizable (`CanResize="True"`, `MinWidth="740" MinHeight="560"`). Size persists to `AppSettings.WindowWidth`/`WindowHeight`, restored on next launch, saved in `OnClosed` (only when `WindowState == Normal`).
- Settings panel: added ▲/▼ buttons next to the "SETTINGS" header (`ScrollUpButton_Click`/`ScrollDownButton_Click` in `MainWindow.axaml.cs`) that page the `SettingsScrollViewer` by a fixed step — for users without a working scroll wheel.
- Mini widget: preset chip row was overflowing past the ON/OFF button on some displays (bug report: "RPG preset goes under on/off switch"). Root cause: 500px default width wasn't enough for 6 chips + logo + status pill + buttons, and the `StackPanel` holding the chips wasn't clipped, so overflow rendered on top of the button. Fixed by wrapping the chip `StackPanel` in a `ScrollViewer` (`ClipToBounds="True"`, `HorizontalScrollBarVisibility="Auto"`) and widening the window (500→640 default, 360→400 min, `CanResize="True"`). Also applied the same defensive fixes as `MainWindow` (`BuildChips`/`StartPulse` in `MiniWindow.axaml.cs` now clear/stop before rebuilding, since `OnOpened` can re-fire there too).
- Rebuilt and repackaged all four distribution artifacts with these fixes.

**2026-07-01, later still — visualizer header text clipping fix, all four artifacts rebuilt again:**
- "◈ GRADIENT/PEAK GLOW" and "○ LIVE" buttons above the frequency visualizer had their text clipped at the top. Root cause: the header row in `MainWindow.axaml` (`Grid.Row="2"` visualizer section) was hardcoded to `Height="16"`, too short for the buttons' font + padding + border. Fixed by changing that `RowDefinition` to `Height="Auto"`. Rebuilt and repackaged all four distribution artifacts.

### Remaining packaging tasks

1. ~~Diagnose and fix the stack-overflow regression~~ — ✅ done 2026-07-01, confirmed via live testing, rebuilt into all four artifacts (see resolution above).
2. **Windows NSIS:** installer up to date with the Avalonia project output and every fix through 2026-07-01 (crash fix, duplicate-builder fixes, resizable window, Settings scroll buttons, Mini widget fix, visualizer header fix). **Does not yet include the PUBG preset** (added later, only smoke-tested in a Debug build) — needs a republish + reinstall before shipping.
3. **macOS:** published + packaged as `.app`/`.zip`, cross-published from Windows and unverified on real hardware. `.dmg` step still needs `hdiutil` on real macOS hardware (see `dist/make-dmg.sh`). Also missing the PUBG preset, same as Windows.
4. **Linux:** published + packaged as `.tar.gz`, unverified on real hardware. `.AppImage` step still needs `appimagetool` on real Linux hardware (see `dist/make-appimage.sh`). Also missing the PUBG preset.
5. WPF project deleted, `GamingEqualizer.Avalonia/` renamed to `GamingEqualizer/` — done
6. `dox/handoff.md` "Out of Scope" updated (Mac/Linux removed) — done
