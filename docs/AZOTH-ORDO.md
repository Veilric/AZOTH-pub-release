# AZOTH ORDO

ORDO organizes files locally using ordered rules. It does not upload files or classify document contents online. ECHO remains the separate duplicate-review workspace.

## Organize a folder

1. Open **ORDO · Organize** and choose the source folder. Include subfolders if needed.
2. Edit the rules. The first matching rule wins; use the arrow buttons to change priority. Starter rules cover Documents, Images, Archives, Audio, and Video. Documents include PDFs and common office/text formats.
3. Set destination folders relative to the source, or browse to another location. Optional extension and UTC modified-date filters combine with the selected type. Month grouping creates subfolders such as `2026-10`.
4. Select **Prepare preview**. Review each source, destination, matching rule, byte total, and reported issue. Files are unchanged at this point.
5. Select **Organize reviewed files**. Changes to rules invalidate the previous preview. Newly occupied destinations and changed sources stay in place; ORDO does not choose a different unseen name during execution.

Unmatched files, symbolic links, hardlinked files, incomplete-download names, and unverifiable files stay. Destination subtrees and AZOTH's own data are excluded. ORDO reserves deterministic names such as `report (1).pdf` rather than overwriting existing files.

## Undo and interrupted work

Operation history persists across application restarts. **Undo / retry undo** checks the recorded file identity and SHA-256 content, then restores moves in reverse order. An occupied original path, changed/missing file, or unavailable/replaced original parent produces a retryable conflict. ORDO never restores over somebody else's file or recreates an unrelated missing parent tree.

**Check interrupted operation** reconciles persisted preparation records with the actual paths without moving or deleting file contents. Ambiguous outcomes preserve the paths for review. Only verified, empty directories created by the operation can be removed. Undo pauses monitoring first so restored files are not immediately organized again.

Cancellation stops subsequent moves. A current move, including a cross-filesystem copy/move, finishes verification and journal persistence before the operation stops. Pending and completed results belong to the application workspace, so navigating away or hiding the main window does not discard them.

## Enable monitoring

Prepare the current preview, acknowledge automatic future moves, then select **Enable reviewed monitoring**. This first version monitors one source folder with multiple ordered rules.

- Existing paths at enrollment form the baseline and stay, including later edits or replacement files at those paths.
- New arrivals are checked every five seconds. Their metadata must settle before a fresh fingerprint and move. This is a practical heuristic; it cannot prove another application has finished writing.
- Monitoring that was enabled at application quit catches missed arrivals after restart. Explicitly pausing and enrolling again baselines the files then present.
- Rule edits revoke approval immediately. Missing/replaced sources, unreadable history, or unresolved operations pause monitoring for review.
- Overlapping automatic sources and ORDO/conversion output-to-source feedback are rejected. Existing conversion-only pipelines remain supported.
- AZOTH must stay running in the tray. Quitting cancels later moves and waits for the current ORDO move/journal. Optional login startup is available only in packaged builds; source builds and separate review profiles do not register startup integration.

## Local state and limits

Rules and monitoring enrollment use versioned JSON in AZOTH's data directory. Move history uses independent operation, entry, and owned-directory records under `ordo-history`; it stores metadata and fingerprints, not file contents. Unknown future state versions are preserved. Corrupt editor/enrollment data is preserved before an explicit replacement.

Cross-filesystem move/undo and platform metadata have dedicated tests with disposable fixtures. Native Windows/macOS behavior still requires target-system verification for the 0.1.1 candidate. Path checks and moves do not freeze external filesystem writers. Flushed records support process-restart recovery; sudden-power-loss directory durability is not promised.

Some newer ORDO strings use English in translated interface catalogs.

See [BUILDING.md](BUILDING.md) for test commands and [the README](../README.md) for source-candidate status and platform limits.
