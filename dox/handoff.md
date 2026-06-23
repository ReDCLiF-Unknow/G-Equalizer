# G Equalizer — Handoff Document

**Date:** 2026-06-23
**Repo:** https://github.com/ReDCLiF-Unknow/G-Equalizer
**Branch:** main

---

## What This Project Is

A Windows desktop app (C# / WPF / .NET 8) that applies system-wide audio equalization for PC gamers. It acts as a frontend controller for EqualizerAPO — it reads and writes EqualizerAPO's config file to change the EQ in real time without touching audio drivers manually.

Key features:
- 10-band EQ (32Hz–16kHz, ±12 dB per band)
- On/off toggle from the app or system tray
- Gaming presets: FPS, RPG, Cinematic, Flat
- Hearing calibration wizard (generates a personal EQ curve via NAudio sine tones)
- Real-time frequency visualizer
- Persists all state across restarts

---

## Current Status

No code has been written yet. The project is in the planning stage.

What exists in the repo:

| File | Status |
|---|---|
| `dox/gaming-equalizer-concept.md` | Complete — covers architecture, all features, tech stack, error handling, UAC strategy, state persistence, folder structure |
| `dox/implementation-plan.md` | Complete — 14 numbered tasks across 4 phases, aligned to the concept doc |

---

## Where to Start

Begin with **Phase 1** of the implementation plan:

1. Create the WPF project (`GamingEqualizer.csproj`, `.NET 8`)
2. Add `app.manifest` requesting `requireAdministrator`
3. Add NuGet packages: `NAudio`, `Newtonsoft.Json`
4. Implement `AppSettings.cs` and save/load from `%AppData%\GamingEqualizer\`
5. Implement `EQConfigWriter.cs` with `Apply()` and `Bypass()` methods
6. Build the main window with 10 sliders and on/off toggle
7. Wire up `TrayController.cs`

Full task breakdown is in [implementation-plan.md](implementation-plan.md).

---

## Key Dependencies

| Dependency | Notes |
|---|---|
| EqualizerAPO | Must be installed separately by the user. App detects it at `C:\Program Files\EqualizerAPO\`. Not bundled due to licensing. |
| NAudio (NuGet) | Used only for calibration wizard sine tone playback |
| Newtonsoft.Json (NuGet) | Read/write preset and profile JSON files |
| .NET 8 | Target runtime. Installer should publish self-contained to avoid requiring a separate runtime install. |

---

## Critical Design Decisions Already Made

- **UAC:** App manifest uses `requireAdministrator` so it can write to the EqualizerAPO config directory. Fallback: write to a user-writable path and chain via EqualizerAPO `Include` directive.
- **EQ filter spec:** Peaking EQ, Q = 1.41, ±12 dB range per band.
- **Calibration algorithm:** `gain = -(threshold_dB - reference_dB)`, clamped to ±12 dB, normalized so loudest band = 0 dB. Applied as a base layer under preset gains.
- **State storage:** `%AppData%\GamingEqualizer\AppSettings.json` — active preset, on/off state, band gains, launch-with-Windows flag, last calibration.
- **Error policy:** Config write failures show an error banner and revert; corrupted JSON files are skipped and logged to `error.log`; NAudio device failure cancels the calibration wizard with a clear message.

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
