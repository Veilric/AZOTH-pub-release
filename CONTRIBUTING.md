# Contributing to AZOTH

AZOTH — by Veilric is maintained under The Alchemical Order. AZOTH v0.1.1 is maintained at [Veilric/AZOTH-pub-release](https://github.com/Veilric/AZOTH-pub-release). AZOTH derives from Filee by Myeonggu Kim (KnifeLemon) and contributors. Preserve the upstream MIT license, copyright, provenance, and third-party notices.

## Set up and verify a change

Follow [BUILDING.md](docs/BUILDING.md) for SDK setup, Release-mode source commands, optional fonts/engines, and packaging prerequisites. Use an isolated `AZOTH_DATA_DIR` and disposable files for conversion, duplicate cleanup, organization, and installation experiments.

```bash
dotnet restore AZOTH.slnx
dotnet build AZOTH.slnx -c Release
dotnet test AZOTH.slnx -c Release --no-build
dotnet format AZOTH.slnx --verify-no-changes --no-restore --severity warn
```

Run checks relevant to your change and report the exact commands and platform. Optional-engine and native tests need their tools/operating systems. Skipped tests and pre-existing failures are not passing checks; identify failures clearly rather than claiming the full suite passes. Headless rendering tests write screenshots below test-project build output.

## Preserve product behavior and safety

Keep changes focused. Preserve existing conversion/preset behavior, custom Donuts, ECHO's explicit cleanup selections and revalidation, and ORDO's reviewed plans, conflict checks, persistent journal, and verified undo. Do not weaken file checks to make a test pass. Test filesystem mutations on disposable inputs and keep cancellation/interruption behavior reviewable.

Keep application translations and platform resources when editing English documentation. Historical `UpstreamWhatsNew` media, small font fixtures with their licenses, and geospatial fixture sidecars are required test inputs. They are intentionally distinct from current AZOTH product assets. See [BRANDING.md](docs/BRANDING.md) before changing the logo or attribution.

Do not commit credentials, private file paths, generated installers, downloaded engines/fonts, caches, logs, or personal test data. Public examples must be reproducible from the source tree without a private checkout or Git history.

## Submit or report

Describe the problem, resulting behavior, and validation with a proposed change. Preserve the repository's configured ownership and release safeguards. Automatic updates remain disabled, and release publication/signing require separately configured infrastructure; a source change must not silently enable them.

For ordinary issues, include OS/desktop, version, engine versions, steps, and a minimal disposable example. Follow [SECURITY.md](SECURITY.md) for sensitive reports. Do not direct AZOTH-specific reports to upstream Filee maintainers.

Technical references: [architecture](docs/ARCHITECTURE.md), [engines](docs/ENGINES.md), [adding converters](docs/ADDING-A-CONVERTER.md), [adding languages](docs/ADDING-A-LANGUAGE.md), [themes](docs/CUSTOM-THEMES.md), [ORDO](docs/AZOTH-ORDO.md), and [Wayland integration](docs/GNOME-WAYLAND.md).

See [testing and known verification limits](docs/TESTING.md) for baseline failures, skips and native-platform checks.
