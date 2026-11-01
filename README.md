<p align="center"><img src="src/AZOTH.App/Assets/Icons/azoth.png" width="96" alt="Official AZOTH symbol" /></p>
<h1 align="center">AZOTH</h1>
<p align="center"><strong>by Veilric</strong> · The Alchemical Order</p>
<p align="center">A local desktop toolkit for converting, reviewing, and organizing files.</p>

AZOTH brings file conversion, reusable conversion shortcuts, duplicate review, and rule-based organization into one desktop application. It is intended for repeated file tasks: converting a batch of images, keeping useful conversion recipes close at hand, reviewing duplicate copies, or sorting new files into chosen folders.

**AZOTH v0.1.1** is the initial public release of AZOTH — by Veilric, part of **The Alchemical Order**. [Download AZOTH v0.1.1](https://github.com/Veilric/AZOTH-pub-release/releases/tag/v0.1.1). Automatic updates remain disabled.

AZOTH is an independently branded fork of [Filee](https://github.com/KnifeLemon/Filee), by Myeonggu Kim (KnifeLemon) and contributors. Upstream copyright, MIT licensing, and third-party notices remain intact. See [origin and licensing](#origin-and-licensing).

## Why AZOTH

Working with many file formats often means switching between separate conversion applications and repeating the same processing steps. Scattered output folders make results harder to track, and duplicate copies can waste storage. AZOTH brings conversion presets, reusable Donuts, duplicate review, and previewed organization into one local workflow, while keeping format-specific engine requirements visible.

## What AZOTH does

| Workflow | Purpose |
| --- | --- |
| Conversion and batch jobs | Convert local files using available engines, saved presets, and chosen output settings. |
| Custom Donuts | Build radial menus of reusable conversion actions and open them through supported desktop triggers. |
| ECHO | Find exact duplicate candidates, inspect the results, and explicitly select copies for Trash-based cleanup. |
| ORDO | Preview ordered file-organization rules, apply reviewed moves, and undo recorded operations. |
| Watch folders | Run configured conversions on arriving files while AZOTH is running. |
| CLI | Convert files, watch a folder, and inspect supported formats or presets from a terminal. |

The desktop includes light/dark appearance, color customization, translated interface catalogs, conversion history, and tray operation. Some newer interface text still uses English in translated catalogs.

Processing takes place locally. Optional engine downloads, browser-link imports, and links opened by the user require network access. AZOTH has no telemetry or product account requirement. Available conversions depend on the engines installed and the source format; a catalog entry is not a guarantee that every file can be converted.

## Preview

[Light appearance](docs/media/azoth-home-light.png) · [Dark appearance](docs/media/azoth-home-dark.png)

The screenshots show AZOTH v0.1.1 in light and dark themes.

## Run from source

AZOTH uses **C#, .NET 10, Avalonia 12.1.3, and MVVM**. Install the SDK selected by [global.json](global.json), then run these commands from the source root:

```bash
dotnet restore AZOTH.slnx
dotnet build AZOTH.slnx -c Release
dotnet run --project src/AZOTH.App -c Release --no-build
```

Use `-c Release` for an ordinary launch. Debug builds include Avalonia developer tools; opening those tools may require a separate Avalonia account. AZOTH itself does not require one.

For an isolated development profile on Linux/macOS:

```bash
AZOTH_DATA_DIR=/tmp/azoth-preview dotnet run --project src/AZOTH.App -c Release
```

PowerShell equivalent:

```powershell
$env:AZOTH_DATA_DIR = Join-Path ([IO.Path]::GetTempPath()) 'azoth-preview'
dotnet run --project src/AZOTH.App -c Release
```

`AZOTH_DATA_DIR` selects separate settings, logs, engine storage, and instance identity. Development copies using it do not register desktop integration. Use a persistent directory if you want to keep that profile.

Optional Noto fonts can be fetched with PowerShell:

```powershell
pwsh build/fetch-fonts.ps1
```

Without them the app uses available system fonts; glyph coverage and layout depend on the machine. Some conversions need optional external engines. Inspect **Settings → Engines** before starting a job. See [build and package instructions](docs/BUILDING.md) and the [engine guide](docs/ENGINES.md).

## First launch and everyday use

On first launch, a brief logo stage leads into the main interface. Use General and Theme to choose preferences, and Engines to inspect optional conversion tools. Depending on the close preference, closing the window can keep AZOTH running in the tray; use Quit to exit.

Start with copies of a few files and a separate output folder. Review output and conflict settings before processing valuable originals.

### Convert a file or batch

1. Open Home and add files through the main drop area or a supported file selection/drop workflow.
2. Choose the conversion preset or target appropriate to the selected files. Install a required engine if the application reports one missing.
3. Check the destination, filename/conflict policy, and any preset option affecting originals. Use a new output folder for the first test.
4. Start the conversion and inspect both the result and the per-file outcome. A mixed batch can contain failed or unsupported items even when other files succeed.

Saved presets retain recipe options for later jobs. For repeat work, place those presets in a custom Donut or configure a conversion watch folder. Watching runs only while the application remains open, including in the tray; quitting stops it.

### Build and use a Donut

Open **Donut toolbar** to create or edit a custom radial menu. Choose a starter where useful, add actions from the catalog, group related actions, and save the menu. Starters provide a starting point; your saved menus and presets remain editable.

Configure how the toolbar opens under **Triggers**. With a supported shortcut or drag gesture, bring files to the Donut and choose the desired conversion action. Review the preset's output settings before using an action on important files. Global drag behavior depends on the operating system, desktop, permissions, and file manager. The main window remains an alternative when a global gesture is unavailable.

### Organize with ORDO

1. Open **ORDO · Organize**, choose one source folder, and decide whether to include subfolders.
2. Arrange rules in priority order and set their destinations. The first matching rule wins.
3. Select **Prepare preview** and inspect exact source/destination paths, totals, skipped files, and conflicts. Previewing does not move files.
4. Select **Organize reviewed files** only after reviewing the plan. Editing rules invalidates that preview; changed or newly occupied paths can be refused during execution.
5. Use operation history for **Undo / retry undo**. Undo verifies recorded identity/content and may require resolving conflicts.

Optional reviewed monitoring handles new arrivals for **one enrolled source folder** while AZOTH runs. Existing files at enrollment are baselined. Monitoring is not an independent system service. See the [ORDO guide](docs/AZOTH-ORDO.md) for safety checks, settling behavior, interruption recovery, and limits.

### Review duplicates with ECHO

Choose a folder to scan in ECHO. It groups candidates by size and SHA-256 content, then lets you inspect and select copies for cleanup. Keep a verified copy unselected. Cleanup rechecks eligibility and retained copies before asking the platform Trash provider to move a selected file; refused or uncertain outcomes need review.

ECHO is **not antivirus software**. Antivirus detections reported during development remain unresolved; they have not been established as false positives. Do not disable protection or bypass a warning to use AZOTH. ECHO's native Windows Recycle Bin behavior also still requires target-system verification. Trash cleanup does not immediately guarantee freed disk space, and it is not a substitute for a backup.

### Use the CLI

From the source tree:

```bash
dotnet run --project src/AZOTH.Cli -c Release -- --help
dotnet run --project src/AZOTH.Cli -c Release -- formats
dotnet run --project src/AZOTH.Cli -c Release -- presets
dotnet run --project src/AZOTH.Cli -c Release -- convert photo.png --to jpg --output converted
```

For an installed package, the same conversion commands use `azoth`:

```bash
azoth convert photo.png --to jpg --output converted
azoth convert pictures --recursive --to webp --output converted
azoth watch incoming --to png --output converted
```

Confirm available targets with `formats` and read `--help` before choosing options. The CLI offers conversion and watch workflows; ECHO and ORDO are desktop workspaces. Do not configure a conversion output to feed back into its watched source.

## Installation and platform limits

Download the packages and `SHA256SUMS.txt` from the [official v0.1.1 release](https://github.com/Veilric/AZOTH-pub-release/releases/tag/v0.1.1). Verify the checksum before installing or extracting. Packages are unsigned; additional native platform testing is ongoing.

| Download | Installation |
| --- | --- |
| [AZOTH-0.1.1-win-Setup.exe](https://github.com/Veilric/AZOTH-pub-release/releases/download/v0.1.1/AZOTH-0.1.1-win-Setup.exe) | Windows x64 installer with per-user or per-machine installation choices. |
| [AZOTH-0.1.1-win-Portable.zip](https://github.com/Veilric/AZOTH-pub-release/releases/download/v0.1.1/AZOTH-0.1.1-win-Portable.zip) | Extract the Windows x64 portable folder and run `AZOTH.exe`. |
| [AZOTH-0.1.1-linux-x64.tar.gz](https://github.com/Veilric/AZOTH-pub-release/releases/download/v0.1.1/AZOTH-0.1.1-linux-x64.tar.gz) | Extract on compatible Linux, run the bundled application, or follow its `INSTALL.txt` to register it. |

The Windows x64 application and Setup require Windows 10 version 1809 (build 17763) or newer; the Windows 11 top-level Explorer menu requires Windows 11. The macOS bundle declares macOS 13 or newer, with native verification still pending.

Windows install paths remain `%LOCALAPPDATA%\Programs\AZOTH` per user and `%ProgramFiles%\AZOTH` per machine. Native Windows fresh install, upgrade from 0.1.0, same-version reinstall, Explorer integration, and uninstall testing remain ongoing. Source review and cross-built package inspection do not establish native Windows compatibility. No signing claim is made.

For the Linux archive, extract it into a chosen directory, read `INSTALL.txt`, and use its `install.sh` only if you want desktop/CLI integration. Running `AZOTH` from the extracted directory does not require that registration. Review the included uninstall instructions before installing.

Linux builds using the pinned downloaded **rhwp** engine have a **glibc 2.39 / Ubuntu 24.04** compatibility floor for that engine. Ubuntu 22.04 compatibility is not claimed for those packages. An alternative engine build needs its own verification. X11 is the primary exercised desktop path. Wayland global drag/shortcut integration varies by compositor; GNOME and KDE have specific integration components. Native Wayland and macOS behavior require additional target-system verification. See [GNOME/Wayland notes](docs/GNOME-WAYLAND.md).

AZOTH uses its own installation/settings identity and does not automatically import, remove, or overwrite a Filee installation. Existing explicit preset/profile import is available. Automatic update checks stay disabled in this release.

## Development, documentation, and reporting

- [Building, tests, and packaging](docs/BUILDING.md)
- [Contributing](CONTRIBUTING.md) and [security reporting](SECURITY.md)
- [Architecture](docs/ARCHITECTURE.md) and [conversion engines](docs/ENGINES.md)
- [ORDO behavior and limits](docs/AZOTH-ORDO.md)
- [Custom themes](docs/CUSTOM-THEMES.md), [adding converters](docs/ADDING-A-CONVERTER.md), and [adding a language](docs/ADDING-A-LANGUAGE.md)
- [Brand assets](docs/BRANDING.md) and [changelog](CHANGELOG.md)

Report issues at [Veilric/AZOTH-pub-release](https://github.com/Veilric/AZOTH-pub-release/issues). Include OS/desktop, application version, relevant engine versions, and reproduction steps when reporting a problem. Use disposable examples and remove private paths/content from logs. Do not send AZOTH-specific reports to Filee's maintainers.

## Origin and licensing

AZOTH — by Veilric is maintained under **The Alchemical Order**. It derives from Filee by **Myeonggu Kim (KnifeLemon) and contributors**, starting at upstream commit `dfe6b6c3c455498540a35301de988b5760730e24`.

The original [MIT license](LICENSE) and copyright are preserved. [NOTICE.md](NOTICE.md) records AZOTH provenance, and [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) records dependency notices. Dependencies and optional engines retain their respective licenses. [Upstream release history](docs/upstream/CHANGELOG.md) is retained for reference. Historical release media and font names in test fixtures preserve test coverage and are not AZOTH product promotion.

<!-- sponsors: src/AZOTH.App/Assets/Sponsors/sponsors.json -->

See [testing and known verification limits](docs/TESTING.md) for baseline failures, skips and native-platform checks.
