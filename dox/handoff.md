# G Equalizer — Handoff Document

**Date:** 2026-06-23
**Repo:** https://github.com/ReDCLiF-Unknow/G-Equalizer (private)
**Branch:** main

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

**All phases complete. App is release-ready.**

The app builds and runs against .NET 10. The UI is fully functional — sliders, toggle, preset combo, animated frequency visualizer, error banner, tray icon, hide-to-tray on close all work. When EqualizerAPO is not installed, the app correctly shows the error banner and disables EQ controls.

| Phase | Status |
|---|---|
| Phase 1: Project setup, EQ writer, tray, settings | **Done** |
| Phase 2: Preset switching UI + frequency visualizer | **Done** |
| Phase 3: Hearing calibration wizard (NAudio) | **Done** |
| Phase 4: Settings screen | **Done** |
| Phase 4: Installer (NSIS + portable ZIP) | **Done** |

---

## File Structure (as built)

```
GamingEqualizer/
  GamingEqualizer.csproj        .NET 10, NAudio + Newtonsoft.Json
  app.manifest                  asInvoker (dev); change to requireAdministrator for release
  GlobalUsings.cs               Resolves WPF vs WinForms namespace conflicts
  App.xaml / App.xaml.cs        App entry, dark theme resource dict, tray init
  MainWindow.xaml / .cs         10-band EQ UI, preset combo, toggle, error banner, animated visualizer
  CalibrationWizard.xaml / .cs  Hearing calibration wizard (Phase 3 — complete)
  SettingsWindow.xaml / .cs     Settings screen: launch-with-Windows, default preset, re-calibrate
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
    app-icon.ico                Multi-size (16/32/48/256px) — used for exe + window title bar
    tray-icon-on.ico            Full-color version of app icon for tray (EQ on)
    tray-icon-off.ico           Desaturated/dimmed version for tray (EQ off)

%AppData%\GamingEqualizer\  (runtime, not in repo)
  AppSettings.json
  HearingProfile.json
  error.log
```

---

## Where to Start Next

**v1 is shipped.** Distribution artifacts are in `dist/`:
- `GEqualizer-Setup-1.0.0.exe` — **48 MB all-in-one installer** (downloads + installs EqualizerAPO, installs G Equalizer, prompts reboot). Share this file directly (e.g. via Telegram).
- `GEqualizer-portable.zip` — 66 MB portable ZIP (just extract and run, no installer)
- `app/GamingEqualizer.exe` — raw self-contained EXE (166 MB uncompressed)
- `installer.nsi` — NSIS source script; rebuild with `makensis installer.nsi` if you need to update the installer

The Settings screen (`SettingsWindow.xaml/.cs`) is complete with:
- "Launch with Windows" toggle — writes/removes `HKCU\...\Run` entry with `--minimized` flag
- Default preset selector (saved in `AppSettings.DefaultPreset`)
- Re-run calibration button (applies result back to main window via `DialogResult = true` + `NewCalibrationGains`)
- Opened via "⚙ Settings" button in the MainWindow header

---

## Known Issues / Things to Fix

None for v1. All previously known issues are resolved:

| Was | Resolution |
|---|---|
| `app.manifest` was `asInvoker` | Now `requireAdministrator` in release build |
| Tray icons were placeholders | Replaced with real icons generated from the app logo PNG |

---

## Key Dependencies

| Dependency | Notes |
|---|---|
| EqualizerAPO | Downloaded and installed automatically by the NSIS installer. App detects it at `C:\Program Files\EqualizerAPO\`. Not bundled in the EXE due to licensing. |
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

## v2 Plan

**Theme:** Full gaming aesthetic redesign — purple→pink gradient palette (Razer/ROG style), glowing slider thumbs, custom styled controls throughout.

**Concept file:** [v2-concept.md](v2-concept.md) — full design spec for v2.

Key changes planned for v2:

| Area | v1 | v2 |
|---|---|---|
| Color palette | Flat neon green (#00FF88) | Purple→pink gradient across bands (#7c3aed → #f472b6) |
| Titlebar | Plain text + toggle button | Logo icon, live status pill, all buttons in one row |
| Visualizer | Bottom of the panel | Top of the window — first thing you see |
| Sliders | Default WPF style | Custom styled with glowing thumbs + colored fills |
| Preset selector | Dropdown (ComboBox) | Clickable chip row |
| Buttons | Flat bordered | Styled with gaming accent colors (purple primary, pink danger) |

**Also planned for v2:** first-run tutorial / onboarding walkthrough for new users.

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
