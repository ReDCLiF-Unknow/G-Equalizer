# G Equalizer — Handoff Document

**Date:** 2026-06-25 (updated)
**Repo:** https://github.com/ReDCLiF-Unknow/G-Equalizer (private)
**Branch:** main

---

## What This Project Is

A Windows desktop app (C# / WPF / .NET 10) that applies system-wide audio equalization for PC gamers. It acts as a frontend controller for EqualizerAPO — it reads and writes EqualizerAPO's config file to change the EQ in real time without touching audio drivers manually.

Key features:
- 10-band EQ (32Hz–16kHz, ±12 dB per band) with per-band tooltips; double-click any slider to reset to 0 dB
- On/off toggle from the app or system tray
- Gaming presets: FPS, RPG, Cinematic, Music, Flat + custom presets (save/import/export)
- Per-ear hearing calibration wizard (left/right separately via NAudio panning + sine tones); per-band re-test on results screen
- Real-time frequency visualizer (80-bar WASAPI loopback + FFT, or EQ-mode animation); 3 color modes: Gradient / Solid / Peak Glow
- AutoEQ headphone correction import (parametric .txt → blended 10-band preset)
- Mini / compact always-on-top widget (500×58 px)
- Global hotkeys: Ctrl+Alt+E (toggle), Ctrl+Alt+P (cycle preset)
- First-run onboarding walkthrough (4-step modal)
- Sound Boost: 0–20 dB preamp boost, toggle button in titlebar + slider in Settings, real-time apply
- Persists all state across restarts

---

## Current Status

**v2.3.0 released.** Installer and portable ZIP in `dist/`.

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

---

## File Structure (as built)

```
GamingEqualizer/
  GamingEqualizer.csproj        .NET 10, NAudio + Newtonsoft.Json
  app.manifest                  requireAdministrator (release); switch to asInvoker for dev
  GlobalUsings.cs               Resolves WPF vs WinForms namespace conflicts
  DwmHelper.cs                  Static helper — ApplyDarkTitlebar(window) via DwmSetWindowAttribute
  App.xaml / App.xaml.cs        App entry, dark theme resource dict, tray init, first-run onboarding trigger
  MainWindow.xaml / .cs         10-band EQ UI, preset chips, toggle, visualizer, live mode, mini mode, band tooltips, boost button
  MiniWindow.xaml / .cs         Always-on-top compact widget (500×58px, draggable)
  OnboardingWizard.xaml / .cs   4-step first-run walkthrough (Welcome / Presets / Hotkeys / Calibration)
  SavePresetDialog.xaml / .cs   Name-input dialog for saving custom presets
  CalibrationWizard.xaml / .cs  Per-ear hearing calibration: 14 steps (7 left + 7 right), panned sine tones
  SettingsWindow.xaml / .cs     Settings: launch-with-Windows, default preset, re-calibrate, import/export, boost slider
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
    FPS.json / RPG.json / Cinematic.json / Flat.json / Music.json
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
| `GEqualizer-Setup-2.3.0.exe` | 48 MB | All-in-one NSIS installer — downloads + installs EqualizerAPO, installs G Equalizer, prompts reboot |
| `GEqualizer-portable.zip` | 68 MB | Portable ZIP — extract and run, no installer needed |
| `app/GamingEqualizer.exe` | 166 MB | Raw self-contained EXE (uncompressed) |
| `installer.nsi` | — | NSIS source; rebuild with `& "C:\Program Files (x86)\NSIS\makensis.exe" installer.nsi` |

**Publish command** (run from `GamingEqualizer/`):
```
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true -o ..\dist\app
```

> **Important:** After `dotnet publish`, copy the valid icon before rebuilding the installer — the publish output corrupts `dist/app/app-icon.ico`:
> ```
> Copy-Item GamingEqualizer\Assets\app-icon.ico dist\app\app-icon.ico -Force
> ```

---

## Known Issues / Things to Fix

None. All previously known issues are resolved.

| Was | Resolution |
|---|---|
| `app.manifest` was `asInvoker` | Now `requireAdministrator` in release build |
| Tray icons were placeholders | Replaced with custom shield design matching app palette |
| `Icon="Assets/app-icon.ico"` crashed on .NET 10 | Set programmatically via `BitmapFrame.Create(pack://...)` in `MainWindow` constructor |
| White Windows titlebar on all windows | Fixed via `DwmSetWindowAttribute(DWMWA_CAPTION_COLOR)` in `DwmHelper.ApplyDarkTitlebar()` — applied to all windows |
| Tray icons not appearing (showing generic icon) | Changed from `<Resource>` to `<Content CopyToOutputDirectory>` in .csproj so they're on disk at runtime |
| `dotnet publish` corrupts `dist/app/app-icon.ico` | Manually copy `Assets/app-icon.ico` → `dist/app/` after every publish, before rebuilding the NSIS installer |

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
- **Sound Boost:** `BoostDb` (0–20 dB, default 0) folds into the EqualizerAPO `Preamp:` line as `Preamp: (-6 + boostDb) dB`. The `-6 dB` base headroom is always present to prevent clipping. `EQConfigWriter.Apply` and `ApplyPerEar` both accept `boostDb = 0f` as a default. `BoostEnabled` gates whether boost is applied — toggle button in titlebar, checkbox + slider in Settings. Settings notifies MainWindow via `onBoostChanged` callback (passed at construction) so EQ re-applies in real time while the slider is dragged.
- **Titlebar color:** All windows call `DwmHelper.ApplyDarkTitlebar(this)` in `OnSourceInitialized`. The color `#1a0533` is stored as COLORREF `0x00330519` in `DwmHelper.cs`. Windows 11 only — on older Windows it silently no-ops.
- **App icon:** Set programmatically in `MainWindow` constructor via `BitmapFrame.Create("pack://application:,,,/Assets/app-icon.ico")` — avoids the .NET 10 XAML crash. `<ApplicationIcon>` in .csproj handles the exe/taskbar icon. Tray icons loaded from file path (must be `<Content>` not `<Resource>` in .csproj).
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

## Out of Scope

- Mac / Linux support
- Per-app EQ
- Microphone processing
- Cloud sync

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

**Where to start next session:** All v3 features are complete. Options: plan v4, or ship as-is. No known bugs or outstanding work.
