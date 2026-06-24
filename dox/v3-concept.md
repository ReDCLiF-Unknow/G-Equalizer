# G Equalizer — v3 Concept

**Status:** Planning
**Builds on:** v2 (shipped 2026-06-24) — purple/pink palette, per-ear calibration, onboarding, mini mode, live visualizer, hotkeys.

---

## Goals

v2 shipped a complete, polished EQ tool. v3's theme is **intelligence and integration** — the app should know what you're doing and adapt automatically, and should connect to the broader headphone/audio ecosystem.

---

## Feature Ideas

---

### 1. Auto-Preset Switching

Detect the foreground process and switch presets automatically based on what the user is running.

**How it works:**
- Background `DispatcherTimer` (every ~2s) polls `GetForegroundWindow` → `GetWindowThreadProcessId` → process name
- User maps process names to presets in Settings (e.g. `cs2.exe → FPS`, `spotify.exe → Music`, `discord.exe → Flat`)
- When a mapped process comes to the foreground, G Equalizer silently switches to the mapped preset
- A toast notification (tray balloon) confirms the switch: *"Switched to FPS for Counter-Strike 2"*
- Manual preset changes override auto-switching until the next process change

**UI:**
- New "Auto-Switch" section in SettingsWindow, collapsed behind a toggle that is **off by default**
- The toggle must be explicitly enabled by the user before any rules take effect — G Equalizer never switches presets automatically out of the box
- When enabled: table shows Process name | Preset dropdown | Remove button
- "Add rule" button opens a small dialog: process name text field + preset picker
- The toggle state is persisted in `AppSettings.json`; disabling it suspends all rules without deleting them

**Files to change:** `SettingsWindow.xaml/.cs`, `AppSettings.cs` (new `AutoSwitchEnabled` bool defaulting to `false` + `AutoSwitchRules` list), `MainWindow.xaml.cs` (polling timer only starts when `AutoSwitchEnabled` is true), `TrayController.cs` (balloon notification)

---

### 2. AutoEQ / Headphone Correction Profile Import

Let users load a correction curve for their specific headphone model from the AutoEQ database, on top of their gaming preset.

**How it works:**
- AutoEQ produces parametric EQ files (`.txt`) with `Filter X: ON PK Fc Y Hz Gain Z dB Q W` lines — the same format EqualizerAPO already uses
- User browses for an AutoEQ `.txt` file in Settings
- G Equalizer parses the parametric filters, re-samples them to the 10 fixed bands (weighted average of overlapping filters), and stores as a correction layer
- This correction is applied on top of any preset — similar to how per-ear calibration works today (`BlendWithPreset`)
- A "Headphone correction: active" indicator appears in the titlebar or Settings

**UI:**
- New "Headphone Correction" card in SettingsWindow
- "Import AutoEQ profile (.txt)" button + active profile name label + "Clear" button
- Link to AutoEQ GitHub in the UI for discoverability

**Files to change:** `AppSettings.cs` (new `HeadphoneCorrection[10]`), `EQConfigWriter.cs` (blend correction into output), `SettingsWindow.xaml/.cs`, `MainWindow.xaml.cs` (indicator)

---

### 3. Preset Sharing via Export Code

Let users share custom presets as a short text code (base64) that can be pasted into another G Equalizer install.

**How it works:**
- A preset's 10 band gains (float[10], each ±12 dB at 0.5 dB resolution) encode into ~40 bits → base64 → ~8 character code (e.g. `GEQ-a3Fx9Z`)
- "Share" button next to each custom preset in Settings → copies the code to clipboard
- "Import code" button → paste dialog → decodes and saves as a new preset

**UI:**
- Small "Share" icon button (📋) next to custom presets in the import/export section of Settings
- "Import from code" text field + Import button alongside the existing file import

**Files to change:** `SettingsWindow.xaml/.cs`, `PresetManager.cs` (encode/decode helpers)

---

### 4. Calibration Improvements

Two additions to the existing per-ear calibration wizard:

**a) Re-test individual bands**
- After calibration completes, the results screen shows each of the 7 measured bands with its gain value
- A "Re-test" button next to each band re-runs just that one frequency for both ears without redoing the full wizard
- Useful when one band feels off without redoing all 14 steps

**b) Reference level warning**
- Before the first tone plays, a 1 kHz reference tone plays at -20 dB and asks "Can you hear this clearly?" with Yes / Adjust volume / No
- If the user says No (audio device issue), calibration is cancelled with a clear message
- Prevents silent failures where all thresholds end up at -60 dB because the output device is muted

**Files to change:** `CalibrationWizard.xaml/.cs`

---

### 5. UI Polish

Small quality-of-life improvements that didn't make v2:

**a) Slider double-click to reset**
- Double-clicking any EQ slider snaps it back to 0 dB with the existing transition animation

**b) Keyboard navigation**
- Tab moves focus between sliders; arrow keys adjust by 1 dB; Shift+arrow by 0.5 dB
- Currently sliders have `IsTabStop=False`; this would re-enable focus with custom key handling

**c) Tray tooltip shows active preset**
- Currently the tray icon has a static tooltip; update it to show e.g. *"G Equalizer — FPS | EQ ON"*

**d) Visualizer color mode toggle**
- Option to switch the 80-bar visualizer from the current gradient (purple→pink) to a single solid color or a peak-glow mode where bars flash white at their peak

**Files to change:** `MainWindow.xaml.cs` (a, b, d), `TrayController.cs` (c), `AppSettings.cs` (d setting)

---

## Out of Scope for v3

- Mac / Linux support (EqualizerAPO is Windows-only)
- Per-app EQ via separate EqualizerAPO device routing (complex, different architecture)
- Microphone processing
- Cloud sync / account system

---

## Implementation Order (suggested)

| Priority | Feature | Complexity |
|---|---|---|
| High | Slider double-click reset + tray tooltip | Low |
| High | Calibration reference level warning | Low |
| High | Preset share code (export/import) | Medium |
| Medium | AutoEQ headphone correction import | Medium |
| Medium | Calibration re-test individual bands | Medium |
| Low | Auto-preset switching | High |
| Low | Visualizer color mode toggle | Low |
