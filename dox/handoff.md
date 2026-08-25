# G-EQ — Handoff Document

## ⏭️ Start here (2026-08-24)

**Both land in `f4af92a`, cleanly verified this time — a live Discord call had contaminated every
earlier attempt (see the technique below), and a quiet endpoint (confirmed via
`AudioMeterInformation.MasterPeakValue` reading exactly 0 first) made the difference.**

1. **The double-fire bug is fixed.** Reported by the user as "reacts at the start and the end of
   the sound"; reproduced with a tone that stops abruptly instead of decaying — the FFT frame
   straddling real signal and true digital silence leaks energy across every bin at once, so every
   band spiked simultaneously at the moment of cutoff (`band1` measured `ratio_avg=69.7`), reading
   exactly like a broadband onset. The tell: the very next frame reads a literal `0.000`, which no
   real sound ever produces immediately after a hit. Each detected rise is now held as a candidate
   for one extra analyzer frame (~85 ms) and only pulses if the following frame clears
   `BeatClickRejectFloor = 0.03`. Verified clean: silence and a sustained tone both hold at zero;
   the abrupt-stop click is now actively rejected (not just absent — `rejected clicks: [2,1,0,1,1,
   0,0,0,0]` on the reproduction case) rather than firing.
   **One real caveat surfaced and resolved during testing, worth knowing if this ever needs
   revisiting:** a kick+hihat mix using a **pure 8 kHz sine** as the hihat proxy showed the top
   three bands rejecting every hit — looked exactly like a regression. Swapping to band-limited
   *noise* (a real cymbal is broadband, not one tone) made it disappear completely, 0 rejections,
   full detection. A pure tone concentrates almost all its energy in one FFT bin, so once that bin
   fades even slightly the whole band's averaged energy can hit a literal zero — a real broadband
   hit spreads across many bins and never does. **Not a flaw in the fix; a limitation of testing
   percussion with a single sine wave.** If BEAT ever seems to miss real cymbals/hi-hats, don't
   assume this fix is the cause without checking against real (broadband) audio first.
2. **Nine beat bands instead of five**, by request: the original five kept, with one interleaved
   between each pair (`BeatBandBounds = {0,13,21,29,37,45,53,61,69,80}`), each interleaved band
   tinted lighter and partly transparent (`LightenAndFade`, 35% toward white, ~55% alpha) so it
   reads as "the extra band" rather than an unrelated hue. Confirmed live — a zoomed screenshot
   during playback shows the tinted band's cluster reading visibly greyer/more muted than the
   two full-colour clusters either side of it, the alternating pattern by design.

**BEAT mode is done and verified live, not just offline.** Five independent frequency bands
(~20-80 Hz / 80-300 Hz / 300 Hz-2 kHz / 2-6 kHz / 6-20 kHz), each with its own onset detector and
its own arch that tapers to true zero at its edges — confirmed with screenshots of the actual
running app (not the offline harness) playing a synthetic kick+hihat mix: five separated peaks,
independently rising and falling frame to frame, not one ridge moving as a block. LIVE/BEAT
persistence also confirmed live and unprompted — a freshly launched process came up with BEAT
already on, no click, because `RestoreAudioVizMode()` read `VizBeatMode: true` from
`AppSettings.json` at startup. See `3a77e0f`, `dcef541`, `17cd91b`, `82efc1b` for the sequence.

**Correction: an elevated foreground process CAN be closed from inside a session, conditionally.**
Every earlier note in this document says otherwise — that was wrong, or at least incomplete.
`Stop-Process` against an elevated PID fails with Access Denied from a medium-integrity shell, as
documented, but spawning an elevated helper works:
```powershell
Start-Process powershell.exe -Verb RunAs -Wait -ArgumentList '-NoProfile','-Command','taskkill /PID <pid> /F'
```
This produced a UAC prompt and succeeded within seconds on 2026-08-24, unblocking a `bin\Debug`
lock that had been held by a stale process for hours across most of a session. The catch: it
depends on a human being present to approve the prompt — the same document earlier records this
exact approach stalling on an unapproved prompt. Try it before assuming a lock is unbreakable; just
don't expect it to work unattended.

**Still open from before, now with an accurate count:** six code commits (`06ada69`, `3c920ba`,
`82efc1b`, `17cd91b`, `dcef541`, `3a77e0f`) sit on `main` ahead of the last real release
(`v3.0.3`, tagged, published, live on the website). Nothing in them has ever shipped. **A version
bump, publish, and `v3.0.4` release is the next concrete step** — see the table below for what it
would contain.

**The build works again.** SDK `10.0.400` was installed on 2026-08-15 via
`winget install --id Microsoft.DotNet.SDK.10`; before that the box had only `8.0.204` against a
`net10.0` project and every build died on `NETSDK1045`. `dotnet build` is clean (0 errors; the 3
`AVLN3001` XAML warnings on CalibrationWizard/MiniWindow/SavePresetDialog are long-standing and
unrelated). G-EQ was also not installed and EqualizerAPO was not present at all (not merely
detached) until `dist/G-EQ-Setup-3.0.2.exe` was run on 2026-08-13.

**Build lock:** once the app has been launched from `bin\Debug`, closing its window only hides it
to the tray and the next build fails on a file lock. Quit from the **tray icon**; `taskkill` needs
elevation. To check that code merely compiles while it runs, build to a throwaway `-o` directory.

**Unreleased work sits on `main` ahead of 3.0.3 — five changes, none in any shipped build.**
A 3.0.4 build and release is the next step.

- `06ada69` — **uninstalling no longer leaves the EQ applied forever.** The uninstaller now writes
  a flat `Preamp: 0 dB`, since EqualizerAPO keeps applying `config.txt` whether or not G-EQ
  exists. Also a named per-session **mutex** stopping a second instance (which used to leave the
  newer copy silently holding no hotkeys while both wrote the APO config), and the Settings panel
  **resets its scroll offset**.
- `3c920ba` — **Bass Boost preset** (`+7 +6 +4 +2 0 0 0 0 +1 +1`). Note it loads *first*
  alphabetically, so it takes `Ctrl+Alt+1` and shifts the other preset hotkeys down one.
- `82efc1b` — **the LIVE visualizer never worked, and now does**, plus a new **BEAT** mode.

**The visualizer bug is worth understanding before touching that code.** `AudioSpectrumAnalyzer`
normalised twice: NAudio's `FastFourierTransform.FFT` already scales by 1/N, and the code divided
by `FftSize` again. Measured against a synthetic sine, a full-scale tone landed at **-79.4 dB**, a
hair above the `-80 dB` floor, and amplitude 0.5 at **-85.5 dB**, under the floor and clamped to
zero — so every bar read `0.000` at any volume and the display sat flat. It is now scaled `x4`
(x2 for the mirrored negative bins, x2 for the Hann window's 0.5 coherent gain), putting a
full-scale sine near 0 dBFS. **Loopback capture itself was always fine** — that was verified
separately and is not where to look if this regresses.

**BEAT mode** pulses the bars on onsets instead of tracing the spectrum: it averages the kick band
(bars 0-15, ~20-80 Hz) and fires when that jumps above its own rolling 1.5 s average. It requires
a **rise** over the previous frame as well as excess over the average — without that the rolling
average self-oscillates on sustained sound (a spike lifts the average, the ratio dips under the
threshold, the spike ages out, it re-fires), and a steady 440 Hz tone produced a phantom beat
about twice a second. Both modes share one capture and are mutually exclusive.

**v3.0.3 is built, committed to `main` (`1fdbc0e`), installed, released and live on the website.** `fix/launch-with-windows-init`
was merged (fast-forward) and shipped. "Launch with Windows" works, verified through the app's own
code path: opening Settings with a task present shows the box ticked (`IsRegistered()` reads
correctly), unticking deletes the task, ticking recreates it. On the installed 3.0.3 the task
registers as `C:\Program Files\GEqualizer\GamingEqualizer.exe --minimized`, `RunLevel=HIGHEST`,
`LogonType=InteractiveToken`, logon trigger `PT10S`, and the running app holds `Ctrl+Q`, `Ctrl+E`
and `Ctrl+Alt+1`.

**The reboot test passed on the shipped build (2026-08-15, 23:23 boot).** The task fired at
23:24:05 — logon plus the `PT10S` delay — starting the installed copy. Only a tray icon appeared;
no window, which is the point of `--minimized`. `Ctrl+Q`, `Ctrl+E` and `Ctrl+Alt+1` were all held
by the app afterwards, and since `HotkeyManager.Register` runs inside `MainWindow.OnOpened`, that
proves initialisation ran on a minimized start. Autostart is done: elevated, no UAC prompt at
logon, preset and hotkeys live.

**If you ever need to re-test it:** probe the hotkeys, not `config.txt`. `RestoreState()` calls
`SetEqState(…, writeConfig: false)`, so a fresh start deliberately does not write the APO config —
an earlier attempt was wasted on that mistake. Always probe an unused combination too, to prove the
test discriminates, and run exactly one instance: with two, whichever starts first takes the
hotkeys and the other silently gets none, which is what muddied the first attempt.

**A caution about the diagnosis, because it cost an hour.** Several rounds were spent chasing a
phantom fourth bug: the box appeared to tick while `LaunchWithWindows` stayed `false`, no task
appeared, and nothing was logged. There was no bug. The `SETTINGS` heading is fixed while the list
beneath it scrolls, so a scrolled panel looks like the top, and the control being clicked was a
different checkbox further down — which is exactly why settings kept saving while
`LaunchWithWindows` never moved. **When a settings control seems inert, first confirm which row is
actually on screen.** The "Launch with Windows" row is the first item, directly above
`Default preset:`.

1. **`requireAdministrator` blocked autostart outright — fixed by replacing the Run key with a
   logon task.** Windows silently skips `HKCU\…\Run` entries whose target requires elevation: UAC
   does not prompt at logon, the app just never starts, and nothing is logged anywhere. Since
   `app.manifest` ships `requireAdministrator`, the Run value the checkbox wrote could never fire
   — the feature was dead on arrival regardless of the other two faults.
   `Platform/StartupTask.cs` now registers a logon-triggered task through
   `schtasks /Create /XML`. **The XML form is deliberate:** the `schtasks` CLI defaults
   `ExecutionTimeLimit` to `P3D` (which would kill a tray app after three days) and both battery
   settings to true (which would stop it starting on a laptop); only the XML reaches those.
   `HighestAvailable` starts the elevated app without a prompt, `InteractiveToken` avoids storing
   a password, and a `PT10S` delay stops the APO health check querying the audio endpoint before
   it is enumerated. Stale Run values are cleaned up whenever the setting is touched, and the
   NSIS uninstaller deletes the task.
2. **The `--minimized` path skipped all initialisation — fixed on the branch.** `App.axaml.cs`
   never called `Show()` for a `--minimized` start, but `MainWindow.OnOpened` is where the
   sliders, `RestoreState()`, hotkey registration, the auto-preset timer and the APO health check
   are wired, and it only fires on the first `Show()`. An autostarted G-EQ came up as a bare tray
   icon: no preset applied, dead hotkeys, no per-game switching, springing to life only when the
   tray icon was clicked. Now shown minimised then immediately hidden.
3. **The first-run wizard never ran on any install — fixed on the branch.** `Opened` fires
   *during* `Show()`, and the handler was attached after it, so it was dead on arrival.
   `HasCompletedOnboarding` is assigned in that handler alone and is still `false` on this
   months-old profile, which is the proof. Now attached before `Show()`, deferred through
   `Dispatcher.UIThread.InvokeAsync` so `Show()` finishes before a modal parents to the window,
   and still flag-guarded because `Opened` re-fires on every tray restore. **Expect the wizard to
   appear once** on the next normal launch of a build from that branch.

**Three more UI faults were found while trying to test the above** (`06d09af`), each of which was
independently making the app feel broken:

- **Hotkey rebinding never worked at all.** `CaptureHotkey_Click` armed capture mode while the
  app's own hotkeys were still registered, and a registered combination goes to its owner as
  `WM_HOTKEY` without ever reaching the focused window as key input — so the combinations a user
  is most likely to press while rebinding (the current ones) could not be captured. Pressing
  Ctrl+Alt+E to rebind the toggle just toggled the EQ. Registration is now released for the
  duration of the capture and restored on every exit path.
- **None of the three settings checkbox captions were clickable** — caption as a sibling
  `TextBlock` left only the ~16px box as a hit target. Now the `CheckBox`'s `Content`.
- **The "EQ is switched off" hint hardcoded `Ctrl+Alt+E`**, so after any rebind it pointed at a
  combination that did nothing. Reads `_settings.HotkeyToggle` now.

**Useful techniques from this session, all read-only:**

- **Validate task XML without registering anything:** `Schedule.Service` → `NewTask(0).XmlText = …`
  parses against the real schema and throws on violations. Cheap to re-run after any edit to
  `BuildTaskXml`. (Element order inside `<Settings>` turned out not to matter — Task Scheduler
  accepted an order that differs from what it emits itself.)
- **Prove a global hotkey is actually held:** `RegisterHotKey` is exclusive, so attempting to
  register the same combination from a probe process returns error `1409` when the app owns it.
  Always probe an unused combination too, to confirm the test discriminates.
- **Prove `OnOpened` ran on a `--minimized` start:** `EnumWindows` lists invisible top-level
  windows, so a hidden `G-EQ` window means `Show()` was called. Before the fix no window existed.
- **Creating the task needs elevation** (`schtasks /Create` with `HighestAvailable` returns "Access
  is denied" from a medium-integrity shell). The app has it; a helper shell does not.
- **Test the audio path without the UI.** A throwaway console project referencing NAudio, with
  `<Compile Include="…\AudioSpectrumAnalyzer.cs" />` linked straight from the app, runs the real
  capture and FFT and prints bar values. That is what exposed the all-zero bars, and it is far
  faster than clicking LIVE and squinting. Drive it with generated WAVs — a 440 Hz tone as a
  steady-signal control, and an exponentially-decaying 55 Hz thump every 500 ms as a 120 BPM beat
  source — played through `System.Media.SoundPlayer.PlayLooping()`. **Always confirm the tone is
  actually audible** via `MMDevice.AudioMeterInformation.MasterPeakValue` before trusting a
  negative result; one "no false positives" run was meaningless because the player had not
  started.
- **A clean negative control needs the whole endpoint quiet, not just "my test file isn't
  playing."** A supposed silence test on 2026-08-24 produced beats across nearly every band; the
  cause was a live Discord call, not a bug — confirmed by correlating detections against
  `MMDevice.AudioMeterInformation.MasterPeakValue` sampled inside the same probe process (peak
  tracked every "phantom" beat exactly), then `device.AudioSessionManager.Sessions` to find which
  PID actually held an active stream (`Get-Process -Id <pid>` on the session's `GetProcessID()`
  named it as Discord; Spotify's session read `Inactive` the whole time and was innocent). WASAPI
  loopback captures whatever the endpoint outputs, from any process — before trusting a "should be
  silent" result, check the session list, not just whichever app you expect to be the source.

**Carried over, roughly in priority order:**

1. **Nobody has confirmed the EQ is actually audible.** Still true, and now with a fresh
   EqualizerAPO install behind it — attachment to the playback device was **not** re-verified on
   2026-08-15. `config.txt` currently holds only `Preamp: 0 dB` because `EqEnabled` is `false`.
   **Ask the user to confirm by ear**; it cannot be verified from inside a session.
2. **Comparison videos still owed** — see the Website section.
3. **macOS/Linux are stranded on 3.0.0** — three releases behind, still pre-rebrand, still
   unverified on real hardware. The website deliberately points those two cards at the v3.0.0
   assets, which is why **v3.0.0 must not be deleted**.
4. **Microphone EQ** — discussed, deliberately deferred. See "Microphone EQ" below; it carries a
   real bug worth fixing regardless.

**If EqualizerAPO comes untied again:** it happened once on 2026-08-07 — the device vanished from
both the endpoint's `FxProperties` and EqualizerAPO's Child APOs, which is what unticking it in
the Configurator does. The install itself was verified healthy (DLL, config, registry, both COM
classes), so **reinstalling EqualizerAPO is the wrong move** — re-tick the device in
`Configurator.exe` → *Playback devices* and reboot. If it detaches repeatedly, look for something
re-enumerating the headset (SteelSeries GG, Windows Update) rather than blaming the install.

*Distinguish that from a genuinely absent install:* on 2026-08-13 `HKLM\SOFTWARE\EqualizerAPO` did
not exist at all, so reinstalling **was** correct there. The app already tells the two apart in
`CheckEqBackendHealth()` — no install at all fails `_backend.IsAvailable`
(`WindowsEQBackend` → `EQConfigWriter.IsEqualizerApoInstalled()`) and shows the "EqualizerAPO
missing" banner, whereas installed-but-detached reaches `EqApoStatus.NotAttached` and shows the
Configurator/reboot banner. **Read which banner is up before deciding to reinstall.**

**Cannot be verified from inside a session:** the app ships `requireAdministrator`, so Windows
UIPI silently blocks synthesised mouse/keyboard input to its window. Hotkeys, banners, colours and
any UI behaviour must be confirmed by the user by hand — injected keystrokes look like they
succeed and do nothing. Two visual bugs shipped on 2026-08-07 because of that (the backdrop was far
too heavy; its geometry was clipped rather than scaled) — when changing visuals, say plainly that
it needs eyeballing rather than implying it was checked.

**Correction (2026-08-15): the window *can* be screenshotted.** The earlier claim that no tool here
could capture a native Win32 window is wrong. This works, and was used to confirm the app renders:

```powershell
Add-Type -AssemblyName System.Drawing          # + P/Invoke GetWindowRect, SetProcessDPIAware
[void][W]::SetProcessDPIAware()                # MUST come first, or you capture a clipped
[void][W]::GetWindowRect($h, [ref]$r)          # top-left corner at the wrong scale on this 4K display
$bmp = New-Object System.Drawing.Bitmap($w, $ht)
[System.Drawing.Graphics]::FromImage($bmp).CopyFromScreen($r.L, $r.T, 0, 0, $bmp.Size)
```

Caveats: it copies a *screen region*, so the window must be on-screen and unobscured — and it
cannot be raised first, because UIPI blocks `SetForegroundWindow` against the elevated window and a
fullscreen game will snatch focus straight back (a capture attempt on 2026-08-15 returned PUBG
instead of G-EQ). Ask the user to bring the window forward, then capture. Reading pixels is
possible; *driving* the UI still is not.

**Rebuild ritual:** closing the app window only **hides it to the tray**, so `dotnet build` fails
with a file lock on `bin\Debug\net10.0\GamingEqualizer.exe`. Ask the user to quit from the **tray
icon**; `taskkill` needs elevation and has stalled on an unapproved UAC prompt before. To check
whether code merely *compiles* while the app is running, build to a throwaway `-o` directory.

---

**Date:** 2026-07-18 (rebranded "G Equalizer" → **"G-EQ"** across app/installer/website; repo made **public**; GitHub Release `v3.0.0` published with the rebuilt Windows installer; website rewritten from static HTML to **Astro + Tailwind**, made interactive (clickable presets drive real gain data in the demo section), and **deployed live via GitHub Pages**; added a side-by-side EQ-off/EQ-on video comparison section with an automatic "Coming soon" placeholder until the clips are recorded. Everything from the 2026-07-14 session below this point is unchanged/still accurate except where noted.)
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

## v3.0.3 (2026-08-15)

Built and installed from `1fdbc0e`, Windows only. **No GitHub Release published yet** — the asset
exists at `dist/G-EQ-Setup-3.0.3.exe` and is committed, but nothing has been tagged or uploaded,
and the website still advertises 3.0.2.

| Change | Notes |
|---|---|
| "Launch with Windows" rewritten as a logon task | `Platform/StartupTask.cs`. A Run value could never have fired: the app is `requireAdministrator` and Windows silently skips elevated Run entries at logon. Uses `schtasks /Create /XML` — the XML form is required to reach `ExecutionTimeLimit=PT0S` (CLI default `P3D` would kill a tray app after three days) and the battery settings (CLI defaults would block it on a laptop). `PT10S` delay so the APO health check does not query the audio endpoint too early. |
| `--minimized` start now initialises | `App.axaml.cs` never called `Show()` for a minimized start, but `MainWindow.OnOpened` is where sliders, `RestoreState()`, hotkeys, the auto-preset timer and the APO check all live, and it fires only on the first `Show()`. An autostarted G-EQ was an inert tray icon. Now shown minimized then hidden. |
| Hotkey rebinding fixed | Capture ran with the app's own hotkeys still registered, and a registered combination goes to its owner as `WM_HOTKEY` without reaching the focused window — so the current bindings could never be captured. Registration is released during capture, restored on every exit path. |
| Checkbox captions clickable | All three settings checkboxes had the caption as a sibling `TextBlock`, leaving only the ~16px box as a hit target. |
| First-run wizard actually fires | The `Opened` handler was attached *after* `Show()`, which is when `Opened` fires — dead on arrival on every install since the Avalonia migration. |
| "EQ is switched off" hint | Hardcoded `Ctrl+Alt+E`; now reads `_settings.HotkeyToggle`. |
| Uninstaller deletes the logon task | Only from 3.0.3 on — uninstalling an older build strands the task. |

---

## v3.0.2 (2026-08-07)

Released: [v3.0.2](https://github.com/ReDCLiF-Unknow/G-Equalizer/releases/tag/v3.0.2), Windows only.
Asset uploaded, download URL verified 200, live site confirmed serving `G-EQ v3.0.2`.

| Change | Notes |
|---|---|
| Accent colour themes | Settings → ACCENT COLOUR. 14 named hues (`ThemeColors.Palette`, colour-wheel order) × 5 tones (Deep/Standard/Bright/Pastel/Neon). Swatches render each hue **at the currently selected tone**, so the row previews the actual result. Violet + Standard reproduces the original look exactly. |
| Hideable presets | Settings → VISIBLE PRESETS. `VisiblePresets()` is the single filter every preset walk goes through — chip row, cycling, and the 1…9 hotkeys — so hiding one renumbers the rest. Hiding the active preset switches to the first visible one; the last visible preset cannot be hidden. |
| Backdrop toned down | Was too large/dense/high-contrast and read as scribbles over the sliders. Also fixed: glyph geometry was drawn at 50×36 while `Width`/`Height` said 38×28 — a `Path` **clips** rather than scales, so outlines were cut off. Coordinates must stay in step with `HpHalfW`/`HpHalfH`. |

**How runtime theming works — do not undo this.** Accent brushes were originally defined *twice*,
inside two `Style.Resources` blocks. Style resources shadow `Application.Resources`, so any runtime
update would have been silently swallowed. They now live **once in `Application.Resources`**, and
~20 hard-coded hex literals across the control themes reference them via `DynamicResource`. XAML
therefore recolours itself; anything drawn in code holds its own brushes and must be rebuilt
explicitly — that is what `ApplyAccentTheme()` is for. `ThemeColors.Apply` is also called in the
`MainWindow` constructor *before* controls are built, so the saved accent is used on first draw
instead of flashing violet.

Rather than hard-coding gradient pairs, each accent is generated: the gradient runs **60° around
the wheel and slightly lighter**, which is the relationship the original violet→pink had. That is
why every hue keeps the same character.

**Known gap:** `MiniWindow` still has hard-coded violet in a couple of spots and does not follow
the accent.

### The 0-byte install (resolved)

`C:\Program Files\GEqualizer\` had contained a single 0-byte `GamingEqualizer.exe` with no registry
entry and no uninstaller — an extraction that died during the very first file copy. Re-running the
installer fixed it completely, so **it was an interrupted run, not an installer bug**. ~100 MB
decompressing from a solid-LZMA archive takes a while with little feedback, which is the likely
reason it got interrupted.

Verifying an install: note that **NSIS is 32-bit**, so its `HKLM` writes are redirected — the
Add/Remove Programs entry lives under `HKLM\SOFTWARE\WOW6432Node\...\Uninstall\GEqualizer`, not the
64-bit view. The Start Menu folder is the **per-user** one (`%APPDATA%\...\Start Menu\Programs\G-EQ`),
not ProgramData. Checking the obvious paths reports false failures.

### Microphone EQ (considered, deferred)

Feasible — EqualizerAPO supports capture devices — but it needs `Device:`-scoped config sections,
which `EQConfigWriter` does not currently emit.

**This is a live bug, not just a blocker:** `BuildConfig`/`BuildPerEarConfig` write a flat
`config.txt` with no `Device:` lines, so EqualizerAPO applies it to *every* device it is installed
on. If a user ticks a microphone in the Configurator, **the playback EQ gets applied to their
mic** — a +7 dB 4 kHz footstep boost on their voice. Worth fixing whether or not mic EQ is built.
Users should be told to tick playback devices only until then.

---

## v3.0.1 (2026-08-06)

Released: [v3.0.1](https://github.com/ReDCLiF-Unknow/G-Equalizer/releases/tag/v3.0.1), Windows only.
Asset `G-EQ-Setup-3.0.1.exe` uploaded and its download URL verified (HTTP 200). The v3.0.0 release
is **deliberately kept** — it is still the only source of the macOS/Linux archives, which the
website links to — and its notes now open with a callout sending Windows users to 3.0.1.

| Change | Notes |
|---|---|
| Customisable global hotkeys | Settings → HOTKEYS. Click *Toggle EQ* / *Cycle preset*, press the combination, Esc cancels. Preset selection takes a modifier from a dropdown. New `Hotkey.cs` parses/formats combos (`"Ctrl+Alt+E"`) and maps Avalonia key events to Win32 VKs (letters, digits, F1–F24, numpad). **Modifier-less combos are rejected on purpose** — a bare global hotkey swallows that key system-wide. |
| Direct preset selection | `Ctrl+Alt+1…9` → nth preset chip, skipping "Custom". Hotkey ids are `HK_PRESET_BASE + 0..8`. Confirmed working by the user. |
| Hotkey conflicts surfaced | `RegisterHotKey`'s return value used to be discarded, so a combination already owned by another app left a hotkey that silently never fired. `HotkeyManager.Register` now returns the names of what it could not claim, and the banner says what to rebind. |
| "EQ is off" hint | **This was the cause of a real "the equalizer doesn't work" report.** Moving a slider or picking a preset while the EQ was disabled did nothing — sliders moved, the visualizer reacted, audio never changed, nothing explained why. Those paths go through `ApplyIfEnabled()`, which shows an accent-coloured banner. `SetEqState(true)` clears it. |
| `EqApoDiagnostics` | `IsAvailable` was `Directory.Exists()` on the EqualizerAPO folder, which says nothing about audibility. Now resolves the default render endpoint via NAudio and looks for an EqualizerAPO APO in its `FxProperties`, warning when there is none. CLSIDs are resolved through their COM registration, not hard-coded. Any failure returns `Ok` — a diagnostic must never block the app. |
| Patterned EQ backdrop | Tiled 16px speaker-grille dot lattice (`GrilleBgBrush`) plus headphone outlines scattered by `BuildHeadphoneBackdrop()`. Rejection-sampled against a circular bound so none overlap at any rotation; re-scatters on resize (>8px) and clears before rebuilding, since `OnOpened` re-fires. |
| Version metadata | The exe carried **no version resource at all** before this. `<Version>3.0.1</Version>` added; it now reports `ProductVersion 3.0.1`, `FileVersion 3.0.1.0`. |

**Do not distinguish legacy vs modern APO slots in diagnostics.** It was tried and reverted:
confirming which is live means reading `audiodg.exe`'s loaded modules, and that is a protected
process — it reports exactly **1 module** (itself), so absence of `EqualizerAPO.dll` there proves
nothing. An earlier diagnosis built on that reading was wrong. EqualizerAPO's default install mode
uses the legacy pre/post-mix slots and works on most drivers, so legacy-only is not reportable.

**Dev-machine notes:** NSIS and the GitHub CLI were installed via `winget` in the previous session;
the NSIS `inetc` plugin is vendored at `dist/nsis-plugins/x86-unicode/INetC.dll` and wired in with
`!addplugindir`, so the installer rebuilds without needing write access to `Program Files`.
Closing the app window only **hides it to the tray** — a rebuild will fail with a file lock until
it is quit properly (tray → Quit), and `taskkill` needs elevation.

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
| `G-EQ-Setup-3.0.3.exe` | 31.4 MB | **Current.** Windows — all-in-one NSIS installer. Built 2026-08-15 from `1fdbc0e`; exe reports `3.0.3+2e6f22b`. Its uninstaller deletes the logon task; earlier ones do not. |
| `G-EQ-Setup-3.0.2.exe` / `3.0.1` / `3.0.0` | ~31 MB each | Superseded. **Do not delete 3.0.0** — the website's macOS/Linux cards point at the v3.0.0 release assets. |
| `app/` | — | Publish staging dir, **gitignored**. Recreated by the publish command below. |
| `GEqualizer-macOS-arm64-3.0.0.zip` | ~41 MB | macOS Apple Silicon — `.app` bundle (zip). Unzip, right-click → Open to bypass Gatekeeper. `.icns` icon and `.dmg` need to be generated on macOS. |
| `GEqualizer-macOS-x64-3.0.0.zip` | ~43 MB | macOS Intel — same as above |
| `GEqualizer-linux-x64-3.0.0.tar.gz` | ~40 MB | Linux x64 — tar.gz. Extract and run `./GEqualizer-linux/GamingEqualizer`. `.AppImage` packaging needs Linux tools. |
| `installer.nsi` | — | NSIS source; rebuild with `& "C:\Program Files (x86)\NSIS\makensis.exe" installer.nsi` |

**macOS and Linux are now four releases behind** (3.0.0 vs 3.0.3), still pre-rebrand, still never
run on real hardware. Of this cycle's fixes, the `--minimized` initialisation fix and the UI fixes
(clickable captions, hotkey rebinding, the hint text) are cross-platform and apply to them; the
scheduled-task autostart is Windows-only — `StartupTask` is guarded by `OperatingSystem.IsWindows()`
and the settings row is hidden elsewhere, so those platforms have no autostart at all.

**Two staging steps are easy to miss** and both bit this release: the publish output does **not**
contain `app-icon.ico` (it is an `AvaloniaResource`, embedded in the exe), so it must be copied in
or `makensis` fails on the installer icon; and a `net10.0` publish emits ~100 MB of `.pdb` files
into `dist/app/`, which never reach the installer only because `installer.nsi` uses
`File /r /x "*.pdb"`.

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

**Open (found 2026-08-15, see "Start here" for detail):**

| Issue | State |
|---|---|
| BEAT fired twice per sound — once on real onset, once on an abrupt stop's spectral-leakage click | Fixed (`f4af92a`): one-frame candidate hold + `BeatClickRejectFloor`. Verified clean (quiet endpoint confirmed first): silence/tone hold at zero, the click is actively rejected. Caveat: a pure-tone percussion proxy can look like a false rejection — use broadband noise when testing this, see "Start here" |
| Nine beat bands with a tinted interleave, added by request | Fixed (`f4af92a`). Negative controls clean across all nine bands; tint confirmed live (zoomed screenshot shows the alternating muted/full-colour pattern) |
| **Seven code changes are unreleased** (`06ada69`, `3c920ba`, `82efc1b`, `17cd91b`, `dcef541`, `3a77e0f`, `f4af92a`) — uninstall unbypass, single instance, scroll reset, Bass Boost preset, visualizer scaling fix, BEAT mode + persistence, the click fix + 9-band tinting. No 3.0.4 build exists; the published 3.0.3 has none of them | **Open.** Needs a version bump, publish, `makensis`, release |
| BEAT mode and LIVE mode had never been seen running in the app, only validated offline | Fixed — confirmed live 2026-08-24 via screenshots of the running process (five independent peaks against a kick+hihat mix, BEAT auto-restoring on a fresh launch). Not yet tried against real Spotify playback specifically, only synthetic test signals |
| **The playback EQ is applied to every device EqualizerAPO is attached to, microphones included** | **Open, and the worst one left.** `BuildConfig`/`BuildPerEarConfig` emit a flat `config.txt` with no `Device:` lines, so ticking a mic in the Configurator puts the +7 dB 4 kHz footstep boost on the user's voice. Fixing it is also what unblocks mic EQ — see "Microphone EQ" below |
| **macOS/Linux are four releases behind** on 3.0.0 — they miss the cross-platform half of these fixes | **Open.** Cross-publish + repackage; still never verified on real hardware |
| **Node 20.9.0 cannot build the website** (Astro needs ≥22.12.0) — no dev server, so visual changes ship unverified | **Open.** CI uses Node 22 so deploys are fine; worth fixing before the comparison videos land |
| `ResetAccent_Click` sets `_suppressSettings` true/false around `RefreshAccentControls()` with no `try`/`finally` | **Open, latent.** A throw there silently deadens the whole Settings panel until restart — the same shape as the bug hardened in `PopulateSettingsPanel` |
| Auto-preset switching ends in a bare `catch { }`; `BypassAndQuit` swallows a failed bypass; APO health is only checked in `OnOpened` | **Open, minor.** Each fails silently: per-game switching can stop forever, quitting can leave the EQ applied, a mid-session APO detach goes unreported |
| Uninstalling left the EQ applied forever — the uninstaller never reset EqualizerAPO's `config.txt` | Fixed (`06ada69`), NSIS compiles; **not yet exercised by a real uninstall** |
| Nothing stopped two instances running — the second silently held no hotkeys and both wrote `config.txt` | Fixed (`06ada69`), verified: two launches leave one process. The surface-a-hidden-window path is **not** yet hands-on tested |
| Settings panel kept its scroll offset under a fixed heading, making a scrolled list look like the top | Fixed (`06ada69`) |
| Windows artifact predated every fix in the autostart cycle | Fixed — `G-EQ-Setup-3.0.3.exe` built from `1fdbc0e`, installed, reboot-verified, released and live on the website |
| Autostart end to end (task fires → app initialises → hotkeys live) | **Verified** on the shipped build, 2026-08-15 |
| "Launch with Windows" could never work — Windows skips `HKCU\…\Run` entries needing elevation, and `app.manifest` is `requireAdministrator` | Fixed (`fca165a`), verified: tick creates the task, untick deletes it |
| `--minimized` start skipped `Show()`, so `MainWindow.OnOpened` — and therefore every bit of initialisation — never ran | Fixed (`2eed91c`), verified via `EnumWindows` (hidden window exists) and hotkey probing (both bindings held) |
| Hotkey rebinding never worked — capture ran with the app's own hotkeys still registered, so they swallowed the keystrokes | Fixed (`06d09af`), verified: rebound to Ctrl+Q/Ctrl+E, both registered, old Ctrl+Alt+E released |
| All three settings checkbox captions were dead to clicks | Fixed (`06d09af`), verified |
| "EQ is switched off" hint hardcoded `Ctrl+Alt+E` regardless of the real binding | Fixed (`06d09af`) |
| First-run wizard never fired on any install: `Opened` handler attached after `Show()`, which is when `Opened` fires | Fixed (`2eed91c`). **Not yet observed firing** — expect it once on the next normal launch |

The autostart row is a direct consequence of the `requireAdministrator` decision in the table
below — the two are in tension, and choosing elevation is what forces autostart through Task
Scheduler. If elevation is ever dropped, a plain Run value would work again and
`Platform/StartupTask.cs` could go.

**Resolved:**

| Was | Resolution |
|---|---|
| `app.manifest` was `asInvoker` | Now `requireAdministrator` in release build — but see the open issue above: this is what breaks Run-key autostart |
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

## Website (Astro + Tailwind, live on GitHub Pages)

**2026-07-18 session:** the app and installer were rebranded from "G Equalizer" to **"G-EQ"** across UI, tray, onboarding, and the installer (internal `AssemblyName`/namespace/`%AppData%` path/registry key were deliberately left as `GamingEqualizer` to avoid breaking existing users' saved presets — see the rebrand commit). The repo (`ReDCLiF-Unknow/G-Equalizer`) was made **public**, and a GitHub Release [`v3.0.0`](https://github.com/ReDCLiF-Unknow/G-Equalizer/releases/tag/v3.0.0) was published with the rebuilt, fully-rebranded Windows installer (`G-EQ-Setup-3.0.0.exe`) plus the existing macOS arm64/x64 zips and Linux tarball — those three are flagged in the release notes as **pre-rebrand and unverified on real hardware** (same caveat as before, just carried forward).

- **Live at:** https://redclif-unknow.github.io/G-Equalizer/ — GitHub Pages, deployed via `.github/workflows/deploy-website.yml` (build + `actions/deploy-pages`), triggered automatically on every push to `main` that touches `website/**`. Enabled via `gh api -X POST repos/.../pages -f build_type=workflow`. Was briefly disabled and re-enabled mid-session at the user's request (`gh api -X DELETE .../pages` / re-`POST`) — no config changes needed either time, it's a pure on/off toggle.
- **Location:** `website/` — a real Astro project now (was a single static `index.html`; converted this session). `src/layouts/Layout.astro` + `src/components/{Nav,Hero,Specs,Demo,Compare,Download,Footer}.astro`, composed in `src/pages/index.astro`. Styling is Tailwind v4 (`@tailwindcss/vite`), with the original CSS custom properties (`--bg`, `--accent`, etc., dark by default, `[data-theme="light"]` override) mapped into Tailwind's theme via `@theme inline` in `src/styles/global.css` — so utilities like `bg-accent`/`text-text-dim`/`border-line` work directly. `npm run dev` (port 4321) / `npm run build` (→ `website/dist/`, gitignored).
- **Base path gotcha:** `astro.config.mjs` sets `site`/`base: '/G-Equalizer/'` for the project-page URL. Any hardcoded absolute path (favicon, video `src`, anything starting with `/`) must be prefixed with `import.meta.env.BASE_URL` (which already includes the trailing slash) or it 404s once deployed — this bit us once with the favicon (`/G-Equalizerfavicon.ico`, missing separator) before the base was given its own trailing slash. When running the dev server locally, the site is at `http://localhost:4321/G-Equalizer/`, not the bare root.
- **Design direction unchanged:** hardware-faceplate / spec-sheet aesthetic — near-black violet ground, violet→pink accent, mono type for every number, matches the app itself.
- **Sections (in order):** sticky nav → hero with live SVG frequency-response curve (real PUBG preset data) → spec table → **interactive demo** (see below) → side-by-side EQ-off/EQ-on video comparison → download cards → footer.
- **Demo section is now interactive:** `src/components/Demo.astro` — clicking a preset chip (Flat/FPS/PUBG/RPG/Cinematic/Music) updates the title, the 10 slider bars/gain labels, and the 20-bar spectrum display, all driven by the real gain arrays copied from `GamingEqualizer/Presets/*.json`. The 20-bar spectrum is derived by interpolating the 10 band values (same idea as the app's own 10→80 visualizer) plus a small fixed jitter array so it reads as organic rather than a stair-step; formula is `42% baseline + 10%/dB`, deliberately exaggerated beyond the literal slider scale so the shape change between presets is obvious rather than subtle. The 10-band sliders themselves use the app's literal scale (`|gain|×6px` over a 46px, `overflow-hidden` track — clipping was added after taller bars like +7 initially poked up into the gain label above them).
- **Comparison section auto-detects missing videos:** `src/components/Compare.astro` checks at *build time* (`fs.existsSync` against `public/media/eq-off.mp4` / `eq-on.mp4`) whether the clips exist. If not, it renders a dashed "Coming soon" placeholder card instead of a `<video>` element, and hides the "▶ Play both" button entirely. **The user still owes these two clips** — same recording matched as closely as possible, EQ off vs. the PUBG preset on (copy was deliberately worded "recorded to match as closely as possible" rather than "the same recording," since they'll be separate takes, not one file). Once dropped into `website/public/media/` with those exact filenames and pushed, the next deploy switches to real players automatically — no code change needed. **Remind the user if this comes up idle for a while.**
- **Download links are real:** Windows card links straight to the `G-EQ-Setup-3.0.0.exe` release asset; Linux links straight to the tarball; macOS links to the release page itself (since there are two arch variants, arm64/x64, and picking one for the user would be a guess). Footer "GitHub →" links to the now-public repo.
- **Not decided yet:** custom domain name, whether to add a changelog/blog page later.
- **Housekeeping:** the old pre-rebrand `dist/GEqualizer-Setup-3.0.0.exe` (superseded by `dist/G-EQ-Setup-3.0.0.exe`) was deleted from disk at the user's request. NSIS and the GitHub CLI (`gh`) were installed on this dev machine via `winget` this session (both were missing, needed `gh auth login --web` device-code flow since no token existed); the NSIS `inetc` plugin is now vendored at `dist/nsis-plugins/x86-unicode/INetC.dll` and wired in via `!addplugindir` in `installer.nsi`, so the installer can be rebuilt without writing to `Program Files`. A desktop shortcut (`G-EQ.lnk`, Desktop) now points at the Debug build exe for quick local launches — note it'll trigger a UAC prompt every time since `app.manifest` is `requireAdministrator`.

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
