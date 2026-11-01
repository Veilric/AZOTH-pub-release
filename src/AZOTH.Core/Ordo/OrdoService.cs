using AZOTH.Core.Files;

namespace AZOTH.Core.Ordo;

/// <summary>Owns the shared non-reentrant mutation lease. Callers must not hold that lease around these APIs.</summary>
public sealed class OrdoService(IFileMetadata metadata, FileFingerprintService fingerprints,
    FileMutationCoordinator mutations, OrdoJournal journal, IOrdoFileMover? mover = null)
{
    private readonly IOrdoFileMover _mover = mover ?? new OrdoFileMover();

    public Task<OrdoOperation> ApplyAsync(OrdoPlan plan, OrdoConfiguration configuration,
        CancellationToken cancellationToken = default) => Task.Run(async () =>
    {
        using var lease = await mutations.AcquireAsync(cancellationToken).ConfigureAwait(false);
        OrdoPlanner.Validate(configuration);
        if (plan.ConfigurationSignature != OrdoPlanner.Signature(configuration))
            throw new InvalidOperationException("Rules changed; prepare and approve a new preview.");
        var operation = new OrdoOperation(1, Guid.NewGuid(), DateTimeOffset.UtcNow, plan.ConfigurationSignature,
            plan.Moves.Select(move => new OrdoJournalEntry(move, OrdoEntryState.Skipped, Detail: "Not attempted.")).ToArray(), []);
        journal.Save(operation);
        for (var i = 0; i < operation.Entries.Count; i++)
        {
            if (cancellationToken.IsCancellationRequested) { operation = operation with { Cancelled = true }; journal.SaveHeader(operation); break; }
            var entry = operation.Entries[i]; var source = entry.Move.Source;
            try
            {
                var current = await fingerprints.ComputeAsync(source.Snapshot.Path, cancellationToken).ConfigureAwait(false);
                if (current.Fingerprint != source || source.Snapshot.LinkCount != 1)
                    throw new IOException("Source changed or cannot be verified.");
                if (!OrdoPaths.Missing(metadata, entry.Move.Destination)) throw new IOException("Reserved destination is occupied or unavailable.");
                operation = EnsureDirectory(operation, Path.GetDirectoryName(entry.Move.Destination)!);
                OrdoPaths.ValidateParents(metadata, entry.Move.Destination);
                if (OrdoPaths.DirectorySnapshot(metadata, Path.GetDirectoryName(source.Snapshot.Path)!).Identity != entry.Move.SourceParentIdentity)
                    throw new IOException("Source parent changed.");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            { operation = operation with { Cancelled = true }; journal.SaveHeader(operation); break; }
            catch (Exception ex) when (IsFileError(ex))
            { operation = Set(operation, i, entry with { Detail = ex.Message }); journal.SaveEntry(operation, i); continue; }

            // Never call the mover unless preparation is successfully persisted and disk-flushed.
            entry = entry with { State = OrdoEntryState.Prepared, Detail = null };
            operation = Set(operation, i, entry); journal.SaveEntry(operation, i);
            try
            {
                OrdoPaths.ValidateParents(metadata, entry.Move.Destination);
                OrdoPaths.ValidateParents(metadata, source.Snapshot.Path);
                if (metadata.Read(source.Snapshot.Path).Snapshot != source.Snapshot ||
                    OrdoPaths.DirectorySnapshot(metadata, Path.GetDirectoryName(source.Snapshot.Path)!).Identity != entry.Move.SourceParentIdentity ||
                    !OrdoPaths.Missing(metadata, entry.Move.Destination))
                    throw new IOException("Source or destination changed after preparation.");
                _mover.Move(source.Snapshot.Path, entry.Move.Destination);
                // Do not pass cancellation into the post-move verification/journal completion.
                entry = await ResolveMove(entry).ConfigureAwait(false);
            }
            catch (Exception ex) when (IsFileError(ex))
            { entry = entry with { State = OrdoEntryState.Conflict, Detail = $"Move outcome needs reconciliation: {ex.Message}" }; }
            operation = Set(operation, i, entry); journal.SaveEntry(operation, i);
        }
        return Freeze(operation);
    }, cancellationToken);

    public Task<OrdoOperation> ReconcileAsync(Guid operationId, CancellationToken cancellationToken = default) => Task.Run(async () =>
    {
        using var lease = await mutations.AcquireAsync(cancellationToken).ConfigureAwait(false);
        return Freeze(await Reconcile(Working(journal.Load(operationId)), cancellationToken).ConfigureAwait(false));
    }, cancellationToken);

    public Task<OrdoOperation> UndoAsync(Guid operationId, CancellationToken cancellationToken = default) => Task.Run(async () =>
    {
        using var lease = await mutations.AcquireAsync(cancellationToken).ConfigureAwait(false);
        var operation = await Reconcile(Working(journal.Load(operationId)), cancellationToken).ConfigureAwait(false);
        operation = operation with { Cancelled = false };
        journal.SaveHeader(operation);
        for (var i = operation.Entries.Count - 1; i >= 0; i--)
        {
            if (cancellationToken.IsCancellationRequested) { operation = operation with { Cancelled = true }; journal.SaveHeader(operation); break; }
            var entry = operation.Entries[i];
            if (entry.State == OrdoEntryState.Undone || entry.DestinationFingerprint is null) continue;
            var original = entry.Move.Source.Snapshot.Path;
            FileFingerprint current;
            try
            {
                var result = await fingerprints.ComputeAsync(entry.Move.Destination, cancellationToken).ConfigureAwait(false);
                if (result.Fingerprint is not { } verified || !SameIdentityContent(verified, entry.DestinationFingerprint))
                    throw new IOException("Organized file changed, was replaced, linked or is unavailable.");
                current = verified;
                if (!OrdoPaths.Missing(metadata, original)) throw new IOException("Original path is occupied or unavailable.");
                OrdoPaths.ValidateParents(metadata, original);
                if (OrdoPaths.DirectorySnapshot(metadata, Path.GetDirectoryName(original)!).Identity != entry.Move.SourceParentIdentity)
                    throw new IOException("Original parent identity changed.");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            { operation = operation with { Cancelled = true }; journal.SaveHeader(operation); break; }
            catch (Exception ex) when (IsFileError(ex))
            { operation = Set(operation, i, entry with { State = OrdoEntryState.Conflict, Detail = ex.Message }); journal.SaveEntry(operation, i); continue; }

            entry = entry with { State = OrdoEntryState.UndoPrepared, UndoPending = true, Detail = null };
            operation = Set(operation, i, entry); journal.SaveEntry(operation, i);
            var moverEntered = false;
            try
            {
                OrdoPaths.ValidateParents(metadata, original);
                OrdoPaths.ValidateParents(metadata, entry.Move.Destination);
                if (metadata.Read(entry.Move.Destination).Snapshot != current.Snapshot ||
                    OrdoPaths.DirectorySnapshot(metadata, Path.GetDirectoryName(original)!).Identity != entry.Move.SourceParentIdentity ||
                    !OrdoPaths.Missing(metadata, original))
                    throw new IOException("Paths changed after undo preparation.");
                moverEntered = true;
                _mover.Move(entry.Move.Destination, original);
                entry = await ResolveUndo(entry).ConfigureAwait(false);
            }
            catch (Exception ex) when (IsFileError(ex))
            {
                entry = entry with
                {
                    State = OrdoEntryState.Conflict,
                    UndoPending = moverEntered,
                    Detail = moverEntered ? $"Undo outcome needs reconciliation: {ex.Message}" : $"Undo refused before move: {ex.Message}"
                };
            }
            operation = Set(operation, i, entry); journal.SaveEntry(operation, i);
        }
        CleanupDirectories(operation);
        return Freeze(operation);
    }, cancellationToken);

    private async Task<OrdoOperation> Reconcile(OrdoOperation operation, CancellationToken cancellationToken)
    {
        for (var i = 0; i < operation.Entries.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = operation.Entries[i];
            if (entry.State is not (OrdoEntryState.Prepared or OrdoEntryState.UndoPrepared or OrdoEntryState.Conflict)) continue;
            if (entry.UndoPending) entry = await ResolveUndo(entry).ConfigureAwait(false);
            else if (entry.DestinationFingerprint is null) entry = await ResolveMove(entry).ConfigureAwait(false);
            operation = Set(operation, i, entry); journal.SaveEntry(operation, i);
        }
        return operation;
    }
    private async Task<OrdoJournalEntry> ResolveMove(OrdoJournalEntry entry)
    {
        var source = entry.Move.Source;
        if (OrdoPaths.Missing(metadata, source.Snapshot.Path))
        {
            var destination = await fingerprints.ComputeAsync(entry.Move.Destination).ConfigureAwait(false);
            if (destination.Fingerprint is { } actual && SameContent(actual, source))
                return entry with { State = OrdoEntryState.Completed, DestinationFingerprint = actual, Detail = null };
        }
        else if (OrdoPaths.Missing(metadata, entry.Move.Destination))
        {
            var original = await fingerprints.ComputeAsync(source.Snapshot.Path).ConfigureAwait(false);
            if (original.Fingerprint == source) return entry with { State = OrdoEntryState.Skipped, Detail = "Prepared move did not occur." };
        }
        return entry with { State = OrdoEntryState.Conflict, Detail = "Ambiguous or changed source/destination; both paths were preserved." };
    }
    private async Task<OrdoJournalEntry> ResolveUndo(OrdoJournalEntry entry)
    {
        if (entry.DestinationFingerprint is not { } expected) return entry with { State = OrdoEntryState.Conflict, Detail = "Missing undo fingerprint." };
        if (OrdoPaths.Missing(metadata, entry.Move.Destination))
        {
            var original = await fingerprints.ComputeAsync(entry.Move.Source.Snapshot.Path).ConfigureAwait(false);
            if (original.Fingerprint is { } restored && SameContent(restored, expected))
                return entry with { State = OrdoEntryState.Undone, UndoPending = false, Detail = null };
        }
        return entry with { State = OrdoEntryState.Conflict, UndoPending = true, Detail = "Undo incomplete or paths changed; retry after resolving the conflict." };
    }
    private OrdoOperation EnsureDirectory(OrdoOperation operation, string path)
    {
        var existing = metadata.Read(path);
        if (existing.Snapshot is { Kind: FileEntryKind.Directory, Identity: not null })
        { OrdoPaths.ValidateParents(metadata, path); return operation; }
        if (existing.Refusal?.Kind != FileRefusalKind.Missing) throw new IOException("Destination parent is not a safe directory.");
        var parent = Path.GetDirectoryName(path) ?? throw new IOException("Destination volume is unavailable.");
        operation = EnsureDirectory(operation, parent);
        OrdoPaths.DirectorySnapshot(metadata, parent); OrdoPaths.ValidateParents(metadata, path);
        var staging = Path.Combine(parent, ".azoth-ordo-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        var identity = OrdoPaths.DirectorySnapshot(metadata, staging).Identity!;
        var renamed = false;
        try
        {
            Directory.Move(staging, path); renamed = true;
            var actual = OrdoPaths.DirectorySnapshot(metadata, path);
            if (actual.Identity != identity) throw new IOException("Created directory identity changed.");
            operation = operation with { CreatedDirectories = Array.AsReadOnly(operation.CreatedDirectories.Append(new OrdoOwnedDirectory(path, actual.Identity!)).ToArray()) };
            journal.SaveDirectory(operation, operation.CreatedDirectories.Count - 1);
        }
        catch (IOException) when (!renamed && metadata.Read(path).Snapshot is { Kind: FileEntryKind.Directory, Identity: not null })
        { OrdoPaths.ValidateParents(metadata, path); }
        finally
        {
            if (!renamed && metadata.Read(staging).Snapshot is { Kind: FileEntryKind.Directory } current && current.Identity == identity)
                try { Directory.Delete(staging, recursive: false); } catch (IOException) { }
        }
        return operation;
    }
    private void CleanupDirectories(OrdoOperation operation)
    {
        if (operation.Entries.Any(e => e.State is not (OrdoEntryState.Undone or OrdoEntryState.Skipped))) return;
        IReadOnlyList<OrdoOperation> history;
        try { history = journal.List(); }
        catch (Exception ex) when (IsFileError(ex) || ex is System.Text.Json.JsonException) { return; }
        var requiredParents = history.Where(op => op.Id != operation.Id).SelectMany(op => op.Entries)
            .Where(e => e.State is not (OrdoEntryState.Undone or OrdoEntryState.Skipped))
            .Select(e => Path.GetDirectoryName(e.Move.Source.Snapshot.Path)!).ToArray();
        foreach (var owned in operation.CreatedDirectories.Reverse())
        {
            if (requiredParents.Any(parent => OrdoPaths.Within(parent, owned.Path))) continue;
            try
            {
                OrdoPaths.ValidateParents(metadata, owned.Path);
                if (metadata.Read(owned.Path).Snapshot is { Kind: FileEntryKind.Directory } current && current.Identity == owned.Identity)
                    Directory.Delete(owned.Path, recursive: false);
            }
            catch (Exception ex) when (IsFileError(ex)) { /* Nonempty, replaced or unavailable directories stay. */ }
        }
    }
    private static bool SameContent(FileFingerprint actual, FileFingerprint expected) => actual.Snapshot.LinkCount == 1 &&
        actual.Snapshot.Length == expected.Snapshot.Length && actual.Sha256 == expected.Sha256;
    private static bool SameIdentityContent(FileFingerprint actual, FileFingerprint expected) => SameContent(actual, expected) && actual.Snapshot.Identity == expected.Snapshot.Identity;
    private static bool IsFileError(Exception ex) => ex is IOException or UnauthorizedAccessException;
    private static OrdoOperation Working(OrdoOperation operation) => operation with { Entries = operation.Entries.ToArray() };
    private static OrdoOperation Freeze(OrdoOperation operation) => operation with { Entries = Array.AsReadOnly(operation.Entries.ToArray()) };
    private static OrdoOperation Set(OrdoOperation operation, int index, OrdoJournalEntry entry)
    {
        // Working arrays never escape a service call; public results are detached read-only snapshots.
        ((OrdoJournalEntry[])operation.Entries)[index] = entry;
        return operation;
    }
}
