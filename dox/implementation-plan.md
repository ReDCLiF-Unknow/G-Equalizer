# G Equalizer — Implementation Plan

## Phase 1: Project Setup & Core App

**Goal:** WPF shell + EQ config writing + system tray

1. **Create WPF project**
   - `GamingEqualizer.csproj` targeting .NET 8 (WinForms interop for NotifyIcon)
   - Add NuGet packages: `NAudio`, `Newtonsoft.Json`
   - Add `app.manifest` with `requestedExecutionLevel level="requireAdministrator"` for write access to EqualizerAPO config dir

2. **App settings persistence** (`Models/AppSettings.cs`)
   - Save/load `AppSettings.json` to `%AppData%\GamingEqualizer\`
   - Stores: active preset, EQ on/off state, per-band gains, launch-with-Windows flag, last calibration applied
   - Load on startup; save on every state change

3. **EqualizerAPO detection** (`EQConfigWriter.cs`)
   - On startup, check `C:\Program Files\EqualizerAPO\` exists
   - If missing: show blocking modal with download link, disable all EQ controls
   - "Check again" button re-runs detection without restarting the app

4. **EQ Config Writer** (`EQConfigWriter.cs`)
   - Write/overwrite `config.txt` in EqualizerAPO's config dir
   - Two methods: `Apply(float[] bands)` and `Bypass()`
   - Format: `Preamp: -6 dB` + `Filter N: ON PK Fc [Hz] Gain [dB] Q 1.41`
   - On write failure: retry once after 200ms; if still failing, show error banner and revert to last known good state
   - Fallback: if `Program Files` write is blocked despite UAC, write to user-writable path and chain via EqualizerAPO `Include` directive

5. **10-Band EQ UI** (`MainWindow.xaml`)
   - 10 vertical sliders: 32, 64, 125, 250, 500, 1k, 2k, 4k, 8k, 16k Hz, range ±12 dB
   - On/Off toggle button
   - Dark WPF theme with neon accents
   - Restore last active band values and on/off state from `AppSettings` on load

6. **System Tray** (`TrayController.cs`)
   - `NotifyIcon` with right-click menu: Toggle / Open / Quit
   - Swap icon between `tray-icon-on.ico` and `tray-icon-off.ico`
   - Override `OnClosing` to hide to tray instead of exit
   - Dispose `NotifyIcon` on actual app exit to prevent memory leak

---

## Phase 2: Presets & Visualizer

**Goal:** Preset switching + animated EQ bar display

7. **Preset JSON files** (`Presets/FPS.json`, etc.)
   - Schema: `{ "name": "FPS", "bands": [0, -2, 0, 1, 2, 3, 4, 5, 3, 1] }`
   - 4 presets: FPS, RPG, Cinematic, Flat
   - On corrupted/unreadable JSON: skip the file, log to `%AppData%\GamingEqualizer\error.log`, fall back to Flat

8. **Preset loader** (`PresetManager.cs`)
   - Loads all JSONs from `Presets/` at startup
   - Dropdown/button row in UI to select preset, instantly writes config
   - Restore last active preset from `AppSettings` on load

9. **Real-time frequency visualizer**
   - WPF `Canvas` with animated bars (one per band)
   - Bars animate to current gain values on every slider/preset change
   - Neon bar colors using WPF `DispatcherTimer` for smooth transitions

---

## Phase 3: Hearing Calibration

**Goal:** Wizard that generates a personal EQ curve

10. **Calibration Wizard** (`CalibrationWizard.xaml`)
    - Step-through dialog: one frequency per screen (7 total: 125, 250, 500, 1k, 2k, 4k, 8kHz)
    - NAudio `SineWaveProvider32` plays tone at fixed level via `WaveOutEvent` with 50ms buffer
    - If no audio output device found: show error dialog, cancel wizard
    - User drags slider to "barely audible" threshold → click Next
    - Final screen: show generated curve, option to apply

11. **Threshold → EQ curve algorithm**
    - `gain = -(threshold_dB - reference_dB)` per frequency
    - Clamp each band to ±12 dB
    - Normalize so the loudest band is 0 dB (prevents overall volume boost)
    - Apply as base layer; gaming preset gains are summed on top

12. **Profile persistence** (`UserProfiles/HearingProfile.json`)
    - Stored in `%AppData%\GamingEqualizer\`
    - Schema: `{ "frequencies": [...], "thresholds": [...], "headphone": "...", "date": "..." }`
    - Load on startup, apply as base layer before preset gains
    - On corrupted file: log error, proceed without calibration curve

---

## Phase 4: Polish & Distribution

**Goal:** Startup option, settings screen, installer

13. **Settings screen**
    - "Launch with Windows" toggle — writes/removes `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`; app starts minimized to tray when launched this way
    - Default preset selector
    - Re-run calibration button

14. **Installer**
    - MSIX (preferred, via Visual Studio publish) or NSIS
    - Publish as self-contained to bundle .NET 8 runtime — no separate runtime install required on target machine
    - Does NOT bundle EqualizerAPO — links to it instead (licensing)

---

## File Structure

```
GamingEqualizer/
  GamingEqualizer.csproj
  app.manifest
  App.xaml / App.xaml.cs
  MainWindow.xaml / .cs
  CalibrationWizard.xaml / .cs
  TrayController.cs
  EQConfigWriter.cs
  PresetManager.cs
  Models/
    Preset.cs
    HearingProfile.cs
    AppSettings.cs
  Presets/
    FPS.json
    RPG.json
    Cinematic.json
    Flat.json
  Assets/
    tray-icon-on.ico
    tray-icon-off.ico

%AppData%\GamingEqualizer\  (runtime, not in repo)
  AppSettings.json
  HearingProfile.json
  error.log
```

---

## Key Risks

| Risk | Mitigation |
|---|---|
| EqualizerAPO config format changes | Pin to known format; test on fresh install |
| Write permission to `Program Files` | `requireAdministrator` manifest; fallback to `Include` directive if still blocked |
| NAudio device unavailable during calibration | Catch device open failure; show clear error and cancel wizard |
| Tray icon memory leak | Dispose `NotifyIcon` on actual app exit |
| Corrupted preset or profile JSON | Skip file, log to `error.log`, fall back to Flat preset |
| .NET 8 not present on user machine | Self-contained publish bundles runtime in installer |
