# G Equalizer — Handoff Document

**Date:** 2026-06-24
**Repo:** https://github.com/ReDCLiF-Unknow/G-Equalizer (private)
**Branch:** main

---

## What This Project Is

A Windows desktop app (C# / WPF / .NET 10) that applies system-wide audio equalization for PC gamers. It acts as a frontend controller for EqualizerAPO — it reads and writes EqualizerAPO's config file to change the EQ in real time without touching audio drivers manually.

Key features:
- 10-band EQ (32Hz–16kHz, ±12 dB per band) with per-band tooltips explaining each frequency range
- On/off toggle from the app or system tray
- Gaming presets: FPS, RPG, Cinematic, Music, Flat + custom presets (save/import/export)
- Per-ear hearing calibration wizard (left/right separately via NAudio panning + sine tones)
- Real-time frequency visualizer (80-bar WASAPI loopback + FFT, or EQ-mode animation)
- Mini / compact always-on-top widget (500×58 px)
- Global hotkeys: Ctrl+Alt+E (toggle), Ctrl+Alt+P (cycle preset)
- First-run onboarding walkthrough (4-step modal)
- Persists all state across restarts

---

## Current Status

**v2 shipped.** All features complete. Builds clean (0 errors, 0 warnings).

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

---

## File Structure (as built)

```
GamingEqualizer/
  GamingEqualizer.csproj        .NET 10, NAudio + Newtonsoft.Json
  app.manifest                  requireAdministrator (release); switch to asInvoker for dev
  GlobalUsings.cs               Resolves WPF vs WinForms namespace conflicts
  App.xaml / App.xaml.cs        App entry, dark theme resource dict, tray init, first-run onboarding trigger
  MainWindow.xaml / .cs         10-band EQ UI, preset chips, toggle, visualizer, live mode, mini mode, band tooltips
  MiniWindow.xaml / .cs         Always-on-top compact widget (500×58px, draggable)
  OnboardingWizard.xaml / .cs   4-step first-run walkthrough (Welcome / Presets / Hotkeys / Calibration)
  SavePresetDialog.xaml / .cs   Name-input dialog for saving custom presets
  CalibrationWizard.xaml / .cs  Per-ear hearing calibration: 14 steps (7 left + 7 right), panned sine tones
  SettingsWindow.xaml / .cs     Settings: launch-with-Windows, default preset, re-calibrate, import/export
  HotkeyManager.cs              RegisterHotKey/UnregisterHotKey P/Invoke wrapper
  AudioSpectrumAnalyzer.cs      WasapiLoopbackCapture + FFT → 80-bar spectrum data
  TrayController.cs             NotifyIcon, Toggle/Open/Quit, hide-to-tray
  EQConfigWriter.cs             Apply(bands) / ApplyPerEar(left, right) / Bypass(), retry + Include fallback
  PresetManager.cs              Loads Presets/*.json, Reload(), falls back to Flat
  Logger.cs                     Appends to %AppData%\GamingEqualizer\error.log
  Models/
    AppSettings.cs              Load/save JSON — bands, preset, cal (left/right/avg), onboarding flag
    Preset.cs
    HearingProfile.cs
  Presets/
    FPS.json / RPG.json / Cinematic.json / Flat.json / Music.json
  Assets/
    app-icon.ico                Multi-size (16/32/48/256px) — ApplicationIcon in .csproj
    tray-icon-on.ico            Full-color version of app icon for tray (EQ on)
    tray-icon-off.ico           Desaturated/dimmed version for tray (EQ off)

%AppData%\GamingEqualizer\  (runtime, not in repo)
  AppSettings.json              Includes LastCalibrationLeft / LastCalibrationRight (per-ear) + HasCompletedOnboarding
  HearingProfile.json
  error.log
```

---

## Distribution Artifacts

All in `dist/`:

| File | Size | Notes |
|---|---|---|
| `GEqualizer-Setup-2.0.0.exe` | 48 MB | All-in-one NSIS installer — downloads + installs EqualizerAPO, installs G Equalizer, prompts reboot |
| `GEqualizer-portable.zip` | 66 MB | Portable ZIP — extract and run, no installer needed |
| `app/GamingEqualizer.exe` | 166 MB | Raw self-contained EXE (uncompressed) |
| `installer.nsi` | — | NSIS source; rebuild with `& "C:\Program Files (x86)\NSIS\makensis.exe" installer.nsi` |

**Publish command** (run from `GamingEqualizer/`):
```
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true -o ..\dist\app
```

---

## Known Issues / Things to Fix

None. All previously known issues are resolved.

| Was | Resolution |
|---|---|
| `app.manifest` was `asInvoker` | Now `requireAdministrator` in release build |
| Tray icons were placeholders | Replaced with real icons generated from the app logo PNG |
| `Icon="Assets/app-icon.ico"` crashed on .NET 10 | Removed XAML `Icon=` attribute; exe icon comes from `<ApplicationIcon>` in .csproj |

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
- **State storage:** `%AppData%\GamingEqualizer\AppSettings.json` — active preset, on/off state, band gains, launch-with-Windows flag, per-ear calibration, onboarding flag.
- **Error policy:** Config write failures show an error banner and revert; corrupted JSON files are skipped and logged to `error.log`; NAudio device failure cancels the calibration wizard with a clear message.
- **WPF + WinForms coexistence:** `UseWindowsForms=true` is needed for `NotifyIcon`. All ambiguities (`Application`, `Orientation`, `HorizontalAlignment`, `OpenFileDialog`, `SaveFileDialog`, `Button`, etc.) are resolved in `GlobalUsings.cs`. File-local aliases (`WpfColor`, `WpfRect`, `WpfEllipse`, `WpfButton`) handle per-file conflicts — do NOT use plain `Color` or `Point` without qualifying the namespace.
- **Visualizer array size:** `_vizCurrent` and `_vizTarget` are `double[80]` (one per bar). In EQ mode, `SetVizTargets()` interpolates from 10 band gains → 80 bars. In live mode, `AudioSpectrumAnalyzer` writes all 80 directly from FFT. Do not shrink these back to 10.
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
| Titlebar | Logo icon, live status pill, Mini / Settings / Disable buttons |
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

---

## Out of Scope

- Mac / Linux support
- Per-app EQ
- Microphone processing
- Cloud sync

---

## v3 Planning

Full spec: [v3-concept.md](v3-concept.md)

| Feature | Priority | Complexity | Notes |
|---|---|---|---|
| Slider double-click reset + tray tooltip | High | Low | Quality-of-life; no new data model |
| Calibration reference level warning | High | Low | Add a pre-wizard check tone step |
| Preset share codes (base64 export/import) | High | Medium | Encode float[10] → ~8-char string |
| AutoEQ headphone correction import | Medium | Medium | Parse parametric `.txt`, blend into EQ output |
| Calibration re-test individual bands | Medium | Medium | Results screen gets per-band Re-test buttons |
| Auto-preset switching | Low | High | **Off by default.** User opts in via Settings toggle. Polls foreground process, maps exe → preset. |
| Visualizer color mode toggle | Low | Low | Solid color or peak-glow alternative to gradient |

**Where to start next session:** pick any High-priority item above — they're all self-contained and don't depend on each other.
