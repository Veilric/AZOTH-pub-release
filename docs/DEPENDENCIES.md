# Dependencies and redistribution

AZOTH 0.1.1 is a source release under The Alchemical Order, intended for [Veilric/AZOTH-pub-release](https://github.com/Veilric/AZOTH-pub-release). The source references dependency packages and optional download manifests. It includes project assets and test fixtures, but does not bundle NuGet dependency binaries, conversion engines, installers or downloaded UI fonts.

Root LICENSE retains Filee's MIT copyright and permission text. NOTICE.md identifies provenance and AZOTH modifications. Individual third-party terms continue to apply; a new repository or fresh history does not erase them. This document records evidence and remaining release work, without a blanket legal-clearance claim.

## Inventory and license evidence

[nuget-manifest.json](../licenses/nuget-manifest.json) records restored package IDs/versions, declared license expressions or files, copyright, project/repository URLs, repository commits when supplied, direct project references by package ID (not proof that each cached version is selected) and copied notice hashes. It includes transitive and build/test packages and multiple cached versions. It is an inventory of restored metadata, not an exact application payload or a formal SBOM. Target-specific resolved graphs live in generated `project.assets.json`; binary inventories must be reconciled separately for each configuration/runtime.

[Component license texts](../licenses/README.md) preserve package-provided text and selected upstream licenses. The offline collector in `build/collect-dependency-notices.py` reproduces the NuGet collection from a restored package cache without copying dependency binaries. NuGet license expressions describe package declarations; native libraries and font payloads may have additional licenses.

| Component/version in restored metadata | License evidence or qualification |
| --- | --- |
| Avalonia 12.1.3, CommunityToolkit.Mvvm 8.4.2, NWayland 0.11.0 | MIT package declarations |
| Svg.Skia and most companion packages 5.1.1 | MIT; upstream text collected |
| **Svg.Custom 5.1.1** | **MS-PL**, separately declared; not covered by the preceding MIT classification |
| Avalonia.ANGLE.Windows.Natives 2.1.27548.20260419 | Package's BSD-style ANGLE LICENSE collected |
| Magick.NET 14.17.2 | Apache-2.0 wrapper; collected Notice.txt contains ImageMagick 7.1.2-32 and delegate-specific terms |
| PDFtoImage 5.2.1 / bblanchon.PDFium 147.0.7690 | MIT / Apache-2.0 package declarations; PDFium native notices remain separate |
| SharpHook 8.0.1 / libuiohook | MIT wrapper / LGPL-3.0 fork; exact native revision/source and redistribution mechanism require binary-release verification |
| OpenMcdf 3.3.0 / UTF.Unknown 2.7.0 | MPL-2.0 / MPL-1.1; preserve notices and exact-version source access when distributing binaries |
| PdfPig 0.1.16 / Markdig 1.4.0 / Tomlyn 2.10.1 | Apache-2.0 / BSD-2-Clause / BSD-2-Clause |
| RtfPipe 2.0.7677.4303 | NuGet license metadata absent; upstream MIT text collected separately |
| AvaloniaUI.DiagnosticsSupport 2.2.3 | SPDX metadata absent; official FAQ expressly allows public project references without a tooling license; do not label it MIT |
| xUnit 3.2.2 and related test packages | Apache-2.0 declarations; test/build dependencies do not automatically ship with the app |

The [resolved version snapshot](DEPENDENCY-VERSIONS.json) records the clean verification project graphs without local paths. All remaining inspected NuGet records, including platform assets, Microsoft runtime/test infrastructure and transitive packages, are listed individually in the manifest. Unknown entries remain unknown rather than defaulting to MIT. The manifest's availability does not imply every entry is a runtime dependency.

## Debug developer tools

`Directory.Packages.props` pins AvaloniaUI.DiagnosticsSupport; `src/AZOTH.App/AZOTH.App.csproj` references it and excludes package assets outside Debug. `src/AZOTH.App/Program.cs` retains `.WithDeveloperTools()` under `#if DEBUG`. The [official Avalonia FAQ](https://docs.avaloniaui.net/tools/faq) permits this integration package to be referenced by public projects and built without a Developer Tools license. Opening the separate Developer Tools requires an Avalonia account and tooling license. Avalonia core's MIT license and this tooling permission are distinct. No tooling account, key or binary is bundled with this source release.

## Source assets and fixtures

- Preserve full MIT terms for pypandoc-hwpx adaptations and `src/AZOTH.Engines/Hwp/Hwpx/blank.hwpx` in `licenses/pypandoc-hwpx.LICENSE`; retain Svg.Skia and fontTools/cu2qu attribution at the adapted implementation surfaces.
- Official supplied AZOTH assets are by Veilric, provided for this project and covered by project MIT terms absent separately stated terms. No independent third-party logo clearance is claimed.
- Four small modified Noto-derived test fonts are tracked under `tests/AZOTH.Engines.Tests/Fonts/`, with complete OFL and original Noto/Adobe notices in its `OFL.txt`. Keep historical names and embedded identities. Test-only status does not exempt source redistribution from OFL.
- Upstream release WebP media under `tests/AZOTH.App.Tests/Fixtures/UpstreamWhatsNew/` retain upstream attribution; they are test fixtures, not AZOTH promotional assets.
- Optional theme examples retain full notices in `docs/themes/NOTICE.md`.
- Noto UI font binaries are optional, pinned/hash-checked downloads from `build/fetch-fonts.ps1` and are not tracked. The downloader currently acquires font binaries without separate OFL files. Before distributing those binaries, verify complete accessible embedded terms or include per-family copyright/OFL texts.

## Optional engines and future binary releases

`src/AZOTH.Engines/Infrastructure/engines.json` pins official download URLs and hashes. rhwp 0.8.7 and 7-Zip 26.03 are used by packaging; other engines download on demand. Their licenses are listed in THIRD-PARTY-NOTICES.md, including GPL, LGPL, MPL, AGPL, Apache and proprietary Microsoft runtime terms. A source manifest linking upstream downloads is distinct from bundling or mirroring the programs. Separate-process invocation does not remove redistribution obligations when those binaries are distributed.

Before public installers or portable binaries are offered, complete these checks:

1. Collect the exact published runtime closure and complete notices for Magick.NET/ImageMagick delegates, ANGLE, PDFium, Skia/HarfBuzz, .NET runtime and Noto fonts. Package SPDX labels and links alone do not satisfy every license. Preserve required NOTICE files and original copyrights.
2. Retain rhwp/7-Zip licenses and unRAR restriction notices; establish license-compliant exact-version corresponding-source access for applicable LGPL/MPL/GPL components and the replaceability/reverse-engineering rights applicable to libuiohook. Review bundled ImageMagick delegate build/linkage obligations.
3. Verify Microsoft C++ runtime distribution rights and preserve applicable terms. Current conda extraction retains Library/bin while discarding package info, and runtime copying retains DLLs only; downloading a redistributable from conda is not itself evidence of every required permission/notice.
4. Audit every optional engine archive if mirrored or bundled, including Ghostscript AGPL, GPL-configured FFmpeg, Pandoc, calibre, H2Orestart and the JRE. Source links must identify accessible matching source and meet the relevant distribution mechanism; a generic project homepage is insufficient evidence on its own.
5. Record the actual platform/engine build provenance. Historical local Linux packages using upstream rhwp had a glibc 2.39 floor; the optional Ubuntu-22.04 native-build recipe does not prove every package runs on Ubuntu 22.04.

Textual notice preparation does not establish that these binary-release tasks have been completed. ECHO antivirus compatibility also remains unresolved; source publication makes no antivirus-compatibility claim.
