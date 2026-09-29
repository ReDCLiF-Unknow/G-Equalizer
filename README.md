# G-EQ

System-wide gaming equalizer for Windows (Avalonia, .NET 10). It writes EqualizerAPO's
`config.txt` — presets, per-game auto-switching, hotkeys, tray, mini widget, live visualizer.
Requires [EqualizerAPO](https://sourceforge.net/projects/equalizerapo/) attached to the playback
device; the app cannot make sound change without it.

Current status and history: [`dox/handoff.md`](dox/handoff.md). Read its "Start here" first.

## Layout

| Path | What |
|---|---|
| `GamingEqualizer/` | The app. `Platform/` holds the Windows/macOS/Linux EQ backends; `Presets/` the built-in presets. |
| `website/` | Astro + Tailwind site, deployed by `.github/workflows/deploy-website.yml` on push to `main`. |
| `dist/` | Installers per version, `installer.nsi`, macOS/Linux packaging scripts. `dist/app/` is gitignored staging. |
| `dox/` | Concept docs, implementation plan, handoff. |

## Build and run

Needs .NET SDK 10.

```
dotnet build GamingEqualizer.sln
```

Closing the window only hides it to the tray — quit from the tray icon before rebuilding, or the
build fails on a locked exe.

## Release (Windows)

1. Bump `<Version>` in `GamingEqualizer/GamingEqualizer.csproj` and `APP_VERSION` in `dist/installer.nsi`.
2. Publish, from `GamingEqualizer/`:
   ```
   dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o ..\dist\app
   Copy-Item Assets\app-icon.ico ..\dist\app\app-icon.ico -Force
   ```
   The icon copy is required — it is embedded in the exe, so `makensis` fails without it.
3. Build the installer, from `dist/`:
   ```
   & "C:\Program Files (x86)\NSIS\makensis.exe" installer.nsi
   ```
4. Commit, then publish the release:
   ```
   gh release create v<version> dist/G-EQ-Setup-<version>.exe --title "G-EQ v<version>"
   ```
5. Update the download links and copy in `website/` and push; the workflow deploys it.

Do not delete the v3.0.0 release — the site's macOS/Linux downloads point at its assets.

## Website

```
cd website
npm ci
npm run dev
```

The repo is private, so GitHub Pages is off (free plan) and release assets are not publicly
downloadable. Making the repo public again restores both.
