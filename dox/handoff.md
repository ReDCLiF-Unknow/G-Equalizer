# G Equalizer — Handoff Document

**Date:** 2026-06-23
**Repo:** https://github.com/ReDCLiF-Unknow/G-Equalizer
**Branch:** main
**Last commit:** 78920d6

---

## What This Project Is

A Windows desktop app (C# / WPF / .NET 10) that applies system-wide audio equalization for PC gamers. It acts as a frontend controller for EqualizerAPO — it reads and writes EqualizerAPO's config file to change the EQ in real time without touching audio drivers manually.

Key features:
- 10-band EQ (32Hz–16kHz, ±12 dB per band)
- On/off toggle from the app or system tray
- Gaming presets: FPS, RPG, Cinematic, Flat
- Hearing calibration wizard (generates a personal EQ curve via NAudio sine tones)
- Real-time frequency visualizer
- Persists all state across restarts

---

## Current Status

**Phase 1 complete. Phase 2 and 3 not yet started.**

The app builds and runs against .NET 10. The UI is functional — sliders, toggle, preset combo, error banner, tray icon, hide-to-tray on close all work. When EqualizerAPO is not installed, the app correctly shows the error banner and disables EQ controls.

| Phase | Status |
|---|---|
| Phase 1: Project setup, EQ writer, tray, settings | **Done** |
| Phase 2: Preset switching UI + frequency visualizer | Not started |
| Phase 3: Hearing calibration wizard (NAudio) | Stub exists, not wired |
| Phase 4: Settings screen + installer | Not started |

---

## File Structure (as built)

```
GamingEqualizer/
  GamingEqualizer.csproj        .NET 10, NAudio + Newtonsoft.Json
  app.manifest                  asInvoker (dev); change to requireAdministrator for release
  GlobalUsings.cs               Resolves WPF vs WinForms namespace conflicts
  App.xaml / App.xaml.cs        App entry, dark theme resource dict, tray init
  MainWindow.xaml / .cs         10-band EQ UI, preset combo, toggle, error banner
  CalibrationWizard.xaml / .cs  Hearing calibration wizard stub (Phase 3)
  TrayController.cs             NotifyIcon, Toggle/Open/Quit, hide-to-tray
  EQConfigWriter.cs             Apply(bands) / Bypass(), retry + Include fallback
  PresetManager.cs              Loads Presets/*.json, falls back to Flat
  Logger.cs                     Appends to %AppData%\GamingEqualizer\error.log
  Models/
    AppSettings.cs              Load/save JSON to %AppData%\GamingEqualizer\
    Preset.cs
    HearingProfile.cs
  Presets/
    FPS.json / RPG.json / Cinematic.json / Flat.json
  Assets/
    tray-icon-on.ico            Placeholder — replace with real icons before release
    tray-icon-off.ico           Placeholder — replace with real icons before release

%AppData%\GamingEqualizer\  (runtime, not in repo)
  AppSettings.json
  HearingProfile.json
  error.log
```

---

## Where to Start Next

**Phase 2** — Preset switching + frequency visualizer:

7. Preset JSON files are already in `Presets/` and `PresetManager.cs` already loads them. Wire up the `PresetCombo` dropdown in `MainWindow.xaml.cs` — it already has a `PresetCombo_SelectionChanged` handler but the combo has no items populated from `PresetManager` on load. Fix `PopulatePresetCombo()` to call `_presetManager.Load()` first and populate items.
8. Add the animated frequency visualizer — a WPF `Canvas` with 10 bars (one per band) that animate to current gain values on every slider/preset change. Use a `DispatcherTimer` for smooth transitions. Add this to `MainWindow.xaml` below the slider grid.

**Phase 3** — Hearing calibration (the wizard stub already exists in `CalibrationWizard.xaml/.cs`):
- The NAudio sine tone playback, threshold slider, and gain algorithm are already implemented. Test and verify the full wizard flow end-to-end.

**Phase 4** — Settings screen + installer:
- "Launch with Windows" registry toggle (`HKCU\...\Run`)
- MSIX self-contained publish (bundles .NET 10 runtime)
- Restore `app.manifest` to `requireAdministrator` for the release build

---

## Known Issues / Things to Fix

| Issue | Notes |
|---|---|
| `app.manifest` is `asInvoker` | Changed for dev convenience. Must be `requireAdministrator` in release so the app can write to the EqualizerAPO config dir. |
| Tray icons are placeholders | `tray-icon-on.ico` and `tray-icon-off.ico` in `Assets/` are generated 16×16 placeholder icons. Replace with real art before release. |
| `PresetCombo` not populated on load | `PopulatePresetCombo()` is called before `_presetManager.Load()`. Reorder or call Load inside. |

---

## Key Dependencies

| Dependency | Notes |
|---|---|
| EqualizerAPO | Must be installed separately by the user. App detects it at `C:\Program Files\EqualizerAPO\`. Not bundled due to licensing. |
| NAudio (NuGet) | Used for calibration wizard sine tone playback |
| Newtonsoft.Json (NuGet) | Read/write preset and profile JSON files |
| .NET 10 | Target runtime. Installer should publish self-contained. |

---

## Critical Design Decisions Already Made

- **UAC:** App manifest uses `requireAdministrator` so it can write to the EqualizerAPO config directory. Fallback: write to a user-writable path and chain via EqualizerAPO `Include` directive.
- **EQ filter spec:** Peaking EQ, Q = 1.41, ±12 dB range per band.
- **Calibration algorithm:** `gain = -(threshold_dB - reference_dB)`, clamped to ±12 dB, normalized so loudest band = 0 dB. Applied as a base layer under preset gains.
- **State storage:** `%AppData%\GamingEqualizer\AppSettings.json` — active preset, on/off state, band gains, launch-with-Windows flag, last calibration.
- **Error policy:** Config write failures show an error banner and revert; corrupted JSON files are skipped and logged to `error.log`; NAudio device failure cancels the calibration wizard with a clear message.
- **WPF + WinForms coexistence:** `UseWindowsForms=true` is needed for `NotifyIcon`. All ambiguities (`Application`, `Orientation`, `HorizontalAlignment`, etc.) are resolved in `GlobalUsings.cs`.

---

## Out of Scope for v1

- Mac / Linux support
- Per-app EQ
- Microphone processing
- Cloud sync
- Left/right ear calibration (flagged as a v2 candidate)

---

## Reference Documents

- [Concept document](gaming-equalizer-concept.md) — full feature spec and architecture
- [Implementation plan](implementation-plan.md) — phased build plan with task-level detail
