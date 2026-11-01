# Testing and known verification limits

Use the SDK in `global.json`. Restore and build before using `--no-build`:

```sh
dotnet restore AZOTH.slnx
dotnet build AZOTH.slnx -c Release
dotnet test AZOTH.slnx -c Release --no-build
```

External engines and native platform facilities are not available on every host. A successful build is distinct from a passing test suite or a verified installer. Keep skipped tests and existing failures visible.

## Source-candidate verification

The 0.1.1 release was restored with a fresh NuGet cache and built in a separate Git-free tree on Ubuntu 24.04 x64, without optional downloaded Noto UI fonts or private build artifacts. The application uses system-font fallback. Release and Debug application builds succeeded; Linux x64, Windows x64 and macOS ARM64 application configurations and Windows Setup cross-published successfully. Cross-publication on Linux does not verify Windows/macOS execution, signing, installer upgrade or Apple distribution requirements.

| Check | Passed | Failed | Skipped |
| --- | ---: | ---: | ---: |
| Core | 857 | 0 | 0 |
| Engines | 1915 | 0 | 200 |
| Platform | 108 | 0 | 11 |
| CLI | 43 | 0 | 0 |
| Focused application release checks | 352 | 0 | 3 |
| Complete Setup on Ubuntu | 191 | 15 | 1 |

Focused app checks cover public metadata, localization, branding, sequential splash, reduced motion, tray reuse, single-instance handling, disabled updates, current Donut Builder and ORDO behavior. Localization layout tests explicitly bypass startup animation when measuring the ready interface. Startup itself has separate lifecycle tests. Screenshots are regenerated from a fresh preview-test process, without shared conversion-test history.

CLI help/formats commands and fresh Linux desktop processes in light/dark/reduced-motion modes were exercised. Tray reopening preserves the original process and does not replay the logo. The source archive contains no generated app binaries.

## Existing baseline failures

The 15 complete Setup failures on Ubuntu have the same names as the established platform baseline. They involve Windows registration, PATH semantics, native imports/DLL locking or payload fixture assumptions. Examples include `InstallerTests`, `PathVariableTests`, `PayloadTests`, `PreviousInstallTests`, `RuntimeCheckTests`, and `UninstallerTests.A_dll_explorer_has_loaded_is_moved_out_and_deleted_at_the_next_restart`. They remain tests, not silently deleted or reclassified as successes.

A broader historical App selector also has 16 existing failures involving legacy Donut face/profile expectations, merge-dialog assumptions and geospatial render expectations: seven `DonutFlipTests.The_editor_switches_between_the_front_and_the_back` language/theme cases; two `DonutOpacityTests.The_theme_page_slider_moves_the_preview_and_is_the_toolbar_pages_setting` cases; four `MergeOrderTests` cases; two `RenderTests.Toolbar_geospatial_donut_renders` cases; and `ToolbarPaletteModelTests.Resetting_a_built_in_donut_invalidates_only_that_profile`. These are separate from the passing current-feature selection above. The complete App suite is not claimed to pass, and known failures are not excluded from the normal full-suite command.

## Before distributing binaries

- Test fresh and upgrade installations on native Windows, including both install scopes, existing settings, Explorer registration and uninstall.
- Verify native macOS launch, Finder integration, permissions, signing and notarization on the intended architectures.
- Exercise native Wayland integrations on their supported desktops; Ubuntu X11 tests do not establish Wayland behavior.
- Keep ECHO's antivirus warning under investigation. It is neither resolved nor a confirmed false positive. Never disable or bypass antivirus protections to declare success.
- Complete the binary dependency/license closure checks in [DEPENDENCIES.md](DEPENDENCIES.md), then verify package hashes and provenance. Unsigned local packages are not final verified public releases.
