# Gaming Equalizer App: Concept — v2

## Visual Theme

Purple → pink gradient palette (Razer/ROG style). Dark backgrounds (`#07070f` body, `#0b0b16` app surface, `#08080f` titlebar). All interactive accents use the gradient band `#7c3aed → #f472b6`.

---

## Layout (top to bottom)

```
┌─────────────────────────────────────────────────────────┐
│  🎮 GEQ   • EQ ACTIVE        [Settings] [Calibrate] [■] │  ← Titlebar
├─────────────────────────────────────────────────────────┤
│  FREQUENCY RESPONSE                                      │
│  ▓▓▓▓▓▒▒░░░░░░░▒▒▓▓▓▓▓▓▓▓▓▓▓▓▓▓                        │  ← Animated visualizer
├─────────────────────────────────────────────────────────┤
│  EQUALIZER                                               │
│  +2  +3  +1  -1  -2  +2  +4  +5  +3  +1               │
│  │   │   │   │   │   │   │   │   │   │                 │  ← 10-band sliders
│  32  64  125 250 500 1k  2k  4k  8k  16k               │
├─────────────────────────────────────────────────────────┤
│  PRESET   [ FPS ] [ RPG ] [ Cinematic ] [ Music ] [ Flat ] [Custom]│  ← Chip row
└─────────────────────────────────────────────────────────┘
```

---

## Titlebar

| Element | Detail |
|---|---|
| Logo icon | 30×30px, gradient `#7c3aed → #db2777`, 7px radius, glow shadow |
| Logo text | `GEQ` — `G` white, `EQ` purple (`#a78bfa`), 800 weight, 4px letter-spacing |
| Status pill | Border `#7c3aed55`, dot pulses via CSS animation, text `EQ ACTIVE` |
| Settings btn | Flat, border `#252538`, hover → `#44447a` |
| Calibrate btn | Purple tint (`#7c3aed14` bg), hover glows |
| Disable btn | Pink/danger tint (`#db277710` bg) |

---

## Frequency Visualizer

- 80-bar animated canvas (requestAnimationFrame)
- Each bar color interpolated along the gradient (`#7c3aed → #f472b6`)
- Bar height = EQ gain envelope + ripple animation (`sin` waves, phase-shifted)
- Positive gains: bars grow **upward** from center, gradient top→fade
- Negative gains: bars grow **downward**, dimmed (0.45 opacity / inverted gradient)
- Horizontal zero line at vertical center (`#1e1e3a`)

---

## EQ Bands

10 bands: 32 Hz, 64, 125, 250, 500, 1k, 2k, 4k, 8k, 16k Hz

Each band column (top→bottom):
1. Gain value label (colored to match band, `+N` / `-N`)
2. Vertical slider track (4px wide, 120px tall, `#141428`)
3. Zero tick mark at vertical center
4. Colored fill — grows up (positive) or down (negative) from center
5. Glowing thumb — 13px circle, band color border, `box-shadow` glow
6. Frequency label (9px, muted `#333355`)

Thumb position formula: `top = 60 - (gain/12 × 55) - 6 px`

---

## Preset Chips

Chips: **FPS · RPG · Cinematic · Music · Flat · Custom**

| State | Style |
|---|---|
| Default | Border `#1e1e34`, text `#44445a` |
| Hover | Border `#7c3aed55`, text `#8877cc` |
| Active | Border `#7c3aed88`, text `#c4b5fd`, bg `#7c3aed18`, inner + outer glow |

---

## Color Palette

| Role | Value |
|---|---|
| Body bg | `#06060e` |
| App surface | `#0b0b16` |
| Titlebar / sections | `#07070f` / `#08080f` |
| Section borders | `#181828` / `#1a1a2e` |
| Band 0 (32 Hz) | `#7c3aed` |
| Band 9 (16k Hz) | `#f472b6` |
| Accent text | `#a78bfa` |
| Muted text | `#333355` |

---

## Changes from v1

| Area | v1 | v2 |
|---|---|---|
| Color palette | Flat neon green `#00FF88` | Purple→pink gradient |
| Titlebar | Plain text + toggle button | Logo icon, status pill, action buttons |
| Visualizer | Bottom of panel, static bars | Top of window, animated 80-bar canvas |
| Sliders | Default WPF style | Custom template: colored fill, glowing thumb, zero tick |
| Preset selector | ComboBox dropdown | Clickable chip row |
| Buttons | Flat bordered | Tinted with purple/pink accent colors |

---

## Also Planned for v2

- First-run onboarding / tutorial walkthrough for new users
- Left/right ear calibration option in the calibration wizard
- **Global hotkeys** — toggle EQ on/off or cycle presets without opening the app (configurable in Settings)
- **Custom preset save** — users can dial in their own curve, name it, and save it; "Custom" chip opens a save dialog
- **Mini / compact mode** — small always-on-top widget showing just the status pill + preset chips for use while gaming
- **Preset import / export** — share `.json` preset files; import button in Settings, export from right-click on a chip
- **Live audio visualizer** — bars animate to actual system audio output (WASAPI loopback via NAudio) instead of just the EQ curve
- **Preset transition animations** — sliders animate smoothly when switching presets instead of snapping
