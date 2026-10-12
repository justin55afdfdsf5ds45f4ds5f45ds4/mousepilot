# MousePilot

Turn a sequence of mouse clicks into an app launcher. A small Windows utility with a local dashboard, instant rule updates, and no extra mouse hardware.

[![Build](https://github.com/justin55afdfdsf5ds45f4ds5f45ds4/mousepilot/actions/workflows/build.yml/badge.svg)](https://github.com/justin55afdfdsf5ds45f4ds5f45ds4/mousepilot/actions/workflows/build.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-795be8.svg)](LICENSE)

**[Download the Windows app](https://github.com/justin55afdfdsf5ds45f4ds5f45ds4/mousepilot/releases/latest)**

## Get started

1. Download `MousePilot-<version>-windows.zip` from Releases and extract the whole ZIP.
2. Double-click `MousePilot.exe`. Its dashboard opens in your browser.
3. Select **New shortcut**, add your mouse inputs in order, choose an app or website, and select **Create shortcut**.
4. Switch to another app and perform the gesture. Saved changes apply immediately.

For example: **Left → Left → Left → Right → Open Claude**. Claude is an example; choose any app detected on your PC, or browse for an `.exe` or `.lnk` file. New installations begin with no rules. MousePilot is independent of Claude and Anthropic.

Keep the `web` folder and `MousePilot.exe.config` beside the executable. The app runs in the system tray after you close its dashboard. Double-click the tray icon to reopen it, or right-click to pause or quit. **Start with Windows** is optional and off by default.

The release executable is unsigned. Source and build scripts are included in this repository for people who prefer to build it themselves.

## What it does

- Builds gestures from 1–8 inputs: left, right, middle, wheel up/down, and mouse back/forward buttons.
- Opens installed Windows apps, Store apps, custom application shortcuts, or HTTP(S) websites.
- Applies rule edits, enable/disable changes, and global pause immediately.
- Lets you adjust timing, test an action, and practice a gesture without launching anything.
- Rejects conflicting sequence prefixes and uses a 1.5-second cooldown after a match.
- Saves rules locally and shows recent rule activity.

Middle click means pressing the scroll wheel. Wheel up/down means rotating it. Side buttons require a mouse that has those buttons; ordinary left/right/middle gestures work with a normal mouse.

## How gestures behave

MousePilot observes input without swallowing it. Clicks still reach the foreground application, and wheel turns still scroll. Choose gestures that do not overlap with your everyday clicks; single left-click or wheel rules can fire frequently. Rules pause while the dashboard has focus so you can edit safely.

Each adjacent input must arrive within the rule's selected time gap. After a match, another gesture can trigger after the 1.5-second cooldown. Inputs injected by other automation software are ignored. Lock screens, sign-in screens, and secure system prompts are unsupported.

This is a Windows desktop app. The browser dashboard is served by the running local helper; hosting the HTML alone on GitHub Pages cannot provide global mouse detection.

## Build from source

Requires Windows with the .NET Framework 4.x compiler. There are no NuGet, npm, Python, or third-party runtime dependencies.

```powershell
git clone https://github.com/justin55afdfdsf5ds45f4ds5f45ds4/mousepilot.git
cd mousepilot
powershell -NoProfile -ExecutionPolicy Bypass -File .\Build.ps1
.\MousePilot.exe
```

Quit a running copy before rebuilding it in place. To compile somewhere else, use `Build.ps1 -OutputDirectory <directory>`; run `Package.ps1` to assemble all runtime files into a ZIP.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Test.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\Test.ps1 -IncludeNativeHook
powershell -NoProfile -ExecutionPolicy Bypass -File .\Package.ps1 -Version 1.0.0
```

The default tests cover the pure sequence matcher. The optional native smoke tests install and remove a real mouse hook on an interactive Windows desktop without synthesizing mouse input or launching apps. If Node.js is installed, `node --check web/app.js` also checks dashboard JavaScript syntax. Package output and SHA-256 checksums are written to `.artifacts/`.

GitHub Actions builds and tests pushes and pull requests. Version tags (`v*`) publish a release ZIP and checksum after the build passes.

## Project layout

| Path | Purpose |
| --- | --- |
| `src/Program.cs` | Tray app, local dashboard server, settings, and action execution |
| `src/GestureEngine.cs` | Sequence matching and Windows low-level mouse hook |
| `src/AppCatalog.cs` | Installed app discovery and launching |
| `web/` | Dashboard HTML, CSS, and JavaScript |
| `tests/` | Matcher tests and native hook lifecycle smoke test |
| `Build.ps1`, `Test.ps1`, `Package.ps1` | Build, verify, and package without dependency installation |

## Local data and privacy

Settings live in `%LOCALAPPDATA%\MousePilot`. Rules and custom app paths are saved in `rules.json`; `session.json` contains a temporary authenticated dashboard link. **Do not share or commit these files.** They are excluded from source and release archives.

The dashboard accepts loopback requests, requires a random session token for its API, checks request origins, and does not enable CORS. The application has no telemetry or cloud account requirement. Raw mouse coordinates are not saved. Opening a website or an app can of course use that application's network connection.

To uninstall, disable **Start with Windows**, quit MousePilot, and delete its extracted folder. Remove `%LOCALAPPDATA%\MousePilot` separately if you also want to delete saved settings.

## Contributing

Issues and pull requests are welcome. Include your Windows version, reproducible steps, and the expected result. Do not include local session tokens or private application paths. For changes to gesture behavior, add a regression test and run `Test.ps1`. Keep the local-only operation and dependency-free build intact.

## License

[MIT](LICENSE).
