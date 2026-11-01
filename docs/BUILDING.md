# Building and checking AZOTH

These instructions apply to AZOTH v0.1.1 at Veilric/AZOTH-pub-release. Installers are available from the official v0.1.1 GitHub Release. Build outputs and successful compilation alone do not establish that an installer is safe or fully tested on its target platform.

## Requirements

- The .NET 10 SDK selected by [global.json](../global.json); dependency versions are centralized in [Directory.Packages.props](../Directory.Packages.props).
- A supported desktop environment for the Avalonia 12.1.3 UI. AZOTH uses C# and MVVM, with platform-specific integration projects.
- Public network access for initial NuGet restore and optional tool downloads. A private cache, Git history, or previous installer is not required to build the source.
- PowerShell 7 (`pwsh`) for the supplied fetching/packaging scripts. Platform-native SDK/tool requirements vary by package.

## Build and run

From the source root:

```bash
dotnet restore AZOTH.slnx
dotnet build AZOTH.slnx -c Release
dotnet run --project src/AZOTH.App -c Release --no-build
dotnet run --project src/AZOTH.Cli -c Release -- --help
```

Use Release for a normal application launch. Debug builds include Avalonia developer tools; opening them may require a separate Avalonia account. That is not an AZOTH account requirement.

Use a separate data directory during development:

```bash
AZOTH_DATA_DIR=/tmp/azoth-development dotnet run --project src/AZOTH.App -c Release
```

PowerShell:

```powershell
$env:AZOTH_DATA_DIR = Join-Path ([IO.Path]::GetTempPath()) 'azoth-development'
dotnet run --project src/AZOTH.App -c Release
```

This isolates settings, logs, engines, and single-instance identity and prevents development-profile desktop registration. Existing running instances use their own profiles; quit the relevant instance before checking a fresh-process startup animation. Tray reopening intentionally skips that animation. Choose a persistent development directory to retain settings.

## Optional fonts and engines

The repository omits downloaded Noto UI fonts. Source builds can use installed system fonts, with machine-dependent layout/glyph coverage. To fetch pinned, checksum-verified fonts before building:

```powershell
pwsh build/fetch-fonts.ps1
```

Conversion support varies by format and available engines. Engines can be selected in **Settings → Engines** or fetched selectively:

```powershell
pwsh build/fetch-engines.ps1 -Only rhwp,pandoc
```

See [ENGINES.md](ENGINES.md) for dependencies and licensing. Do not copy a maintainer's private downloaded-engine directory as an undocumented build prerequisite. The small font and geospatial files under tests are separate committed fixtures; preserve them, including their license and sidecar files.

## Verification

```bash
dotnet test AZOTH.slnx -c Release --no-build
dotnet format AZOTH.slnx --verify-no-changes --no-restore --severity warn
```

Run tests relevant to a change and record commands, platform, failures, and skips. Some suites depend on optional engines or native Windows/macOS integration; the complete suite is not claimed to pass on every development machine. Headless rendering tests write screenshots beneath test-project build output. Historical upstream slideshow media are embedded by the App/Setup test projects, not distributed as AZOTH product promotion.

For filesystem mutation tests, use disposable files, an isolated profile, and scratch installation locations. Review cleanup, overwrite, cancellation, journal recovery, and undo behavior before applying changes to real data. Cross-built packages require native runtime/install/upgrade/uninstall verification separately.

## Local package generation

Scripts derive their default version from Directory.Build.props; the examples set 0.1.1 explicitly. Outputs go to ignored `Releases/` by default. Source ZIPs and packages should be built and reviewed independently of a private checkout/cache.

On Windows, with the required native build inputs/tooling:

```powershell
pwsh build/build-installer.ps1 -Version 0.1.1 -Portable
```

Expected x64 filenames are `AZOTH-0.1.1-win-Setup.exe` and `AZOTH-0.1.1-win-Portable.zip`. The script builds the application/CLI/Setup, fetches engine inputs, and packages Explorer integration. Its optional prepublished-input parameters are for an already prepared publish, not a prerequisite for a clean build. Certificates are maintainer-provided; no signing is implied by this command. Read the script's help before using signing or check-only options.

On Linux:

```powershell
pwsh build/build-portable.ps1 -Runtime linux-x64 -Version 0.1.1
```

This produces `AZOTH-0.1.1-linux-x64.tar.gz` and checksum metadata. The default pipeline fetches fonts and optional packaged engines. `-SkipFonts` skips fetching rather than verifying fonts are present. The pinned downloaded rhwp engine imposes a glibc 2.39 / Ubuntu 24.04 floor; Ubuntu 22.04 compatibility is not claimed for that combination. [build-linux-native.py](../build/build-linux-native.py) and `-RhwpOverride` support an alternative engine build, which requires its own compatibility verification. The script's older compatibility comments are not evidence that the default downloaded engine works on older distributions.

Portable targets also include `linux-arm64`, `osx-x64`, and `osx-arm64`; their availability in the script does not mean they have all been verified for this candidate. Build Unix packages on the target operating system to preserve permissions and native resources. macOS signing/notarization requires appropriate credentials and tooling. Linux shell integration requires the [GNOME/Wayland guide](GNOME-WAYLAND.md) and included installer resources.

Before offering any package, verify its contents/checksums, embedded source/version identity, engine dependencies, platform launch, integration, upgrade preservation, and uninstall behavior on the actual target. Native Windows installation/upgrade remains pending for this candidate. Do not distribute an unsigned/unverified artifact as an officially validated download or advise bypassing protection warnings.

## Publication boundaries

The intended public destination is [Veilric/AZOTH-pub-release](https://github.com/Veilric/AZOTH-pub-release);  Product/support links and update infrastructure are independent. The update repository setting remains empty, and automatic checks remain disabled. Release workflows are gated; source or package generation does not authorize publishing, signing, or enabling updates.

See [CONTRIBUTING.md](../CONTRIBUTING.md), [SECURITY.md](../SECURITY.md), and [BRANDING.md](BRANDING.md) for maintenance, reporting, and attribution requirements.
