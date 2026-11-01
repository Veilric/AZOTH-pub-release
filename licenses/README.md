# Component license texts

These texts preserve third-party terms alongside AZOTH's source. They are not relicensed under the root MIT license. Root LICENSE preserves the original Filee MIT notice verbatim; NOTICE.md identifies AZOTH modifications and project artwork.

- `pypandoc-hwpx.LICENSE`: full upstream MIT notice for adapted mapping/layout and the HWPX reference asset.
- `Svg.Skia.LICENSE`: upstream license at the repository revision recorded by Svg.Custom 5.1.1; Svg.Custom itself declares **MS-PL**, reproduced in `MS-PL.txt`.
- `fontTools.LICENSE`: fontTools license, including cu2qu credits and terms. AZOTH documents the cu2qu approach in its cubic-to-quadratic implementation.
- `RtfPipe.LICENSE`: upstream MIT text supplementing older NuGet metadata that lacks a license field.
- `libuiohook.COPYING.LESSER.md`, `GPL-3.0.txt`, `LGPL-3.0.txt`: native hook licensing texts; SharpHook's wrapper is separately MIT.
- `MPL-2.0.txt`, `MPL-1.1.txt`, `Apache-2.0.txt`, `GPL-2.0.txt`, `LGPL-2.1.txt`: reference license texts for components listed in THIRD-PARTY-NOTICES.md. Copies of standard Apache/GNU license texts come from the development system's common-license collection; authoritative texts are available from [Apache](https://www.apache.org/licenses/LICENSE-2.0) and [GNU](https://www.gnu.org/licenses/).
- `upstream-sources.json`: retrieval URLs, date and SHA-256 for separately retrieved public upstream texts. Some URLs track upstream branches; hashes identify the exact recorded texts rather than claiming correspondence with every shipped binary.
- `nuget-manifest.json`: all restored NuGet metadata records used for this preparation, including direct, transitive, build/test-only packages and potentially unused cached versions. Declared metadata is not an independently verified native-payload license inventory. `directReferenceProjectsById` identifies projects that directly reference that package ID; it does not assert that every cached version is selected. An empty list means transitive or another cached package, not proof of runtime inclusion.
- `nuget/<package>/<version>/...`: textual license/NOTICE files found in those packages, preserved with their original text. The manifest records hashes and component metadata. No package DLLs, native libraries or NuGet archives are included.

Font-fixture OFL remains at `tests/AZOTH.Engines.Tests/Fonts/OFL.txt`. Full notices for optional theme examples remain at `docs/themes/NOTICE.md`; retain those when copying their associated assets.

Regenerate the NuGet collection after restoring dependencies:

```sh
python3 build/collect-dependency-notices.py --package-root /path/to/restored/nuget/packages
```

The collector is offline and copies text only. Use a clean restore cache for the intended project/configuration/runtime to reduce unused records. Reconcile the resulting inventory against `project.assets.json` and actual publish contents before a binary release. Metadata gaps, license mismatches, native subcomponents and exact corresponding-source availability require review; collection alone is not a compliance determination. Existing output files are retained, so remove obsolete component notice files deliberately after reviewing dependency changes.
