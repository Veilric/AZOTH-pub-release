using AZOTH.Core.Files;

namespace AZOTH.Core.Echo;

public sealed class EchoService(IFileMetadata metadata, FileFingerprintService fingerprints, FileMutationCoordinator mutations)
{
    /// <summary>Size-first, sequential streaming hashing. Memory holds metadata, never whole file content.
    /// Cancellation throws without publishing partial scan groups.</summary>
    public Task<EchoScanResult> ScanAsync(IEnumerable<string> roots, bool recursive = true,
        IProgress<EchoProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var rootSnapshot = roots.ToArray();
        return Task.Run(async () =>
        {
            var enumeration = new FileTreeEnumerator(metadata).Enumerate(rootSnapshot, recursive,
                new InlineProgress<int>(count => progress?.Report(new("Enumerating", count, 0))), cancellationToken);
            var issues = enumeration.Issues.Select(x => new EchoIssue(x.Path, x.Refusal)).ToList();
            var physical = enumeration.Files.GroupBy(x => x.Identity!).Select(g => new EchoFile(
                g.OrderBy(x => x.Path, StringComparer.Ordinal).First(), g.Select(x => x.Path).Order(StringComparer.Ordinal))).ToArray();
            var groups = new List<EchoGroup>(); var hashed = 0;
            foreach (var sizeGroup in physical.GroupBy(x => x.Snapshot.Length).Where(g => g.Count() > 1))
            {
                var matches = new Dictionary<string, List<EchoFile>>(StringComparer.Ordinal);
                foreach (var file in sizeGroup)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var result = await fingerprints.ComputeAsync(file.Snapshot.Path, cancellationToken).ConfigureAwait(false);
                    hashed++;
                    progress?.Report(new("Hashing", enumeration.Files.Count, hashed));
                    if (result.Refusal is { } error) { issues.Add(new(file.Snapshot.Path, error)); continue; }
                    if (result.Fingerprint!.Snapshot != file.Snapshot)
                    { issues.Add(new(file.Snapshot.Path, new(FileRefusalKind.Changed, "File changed since enumeration."))); continue; }
                    if (!matches.TryGetValue(result.Fingerprint.Sha256, out var bucket))
                        matches[result.Fingerprint.Sha256] = bucket = [];
                    bucket.Add(file);
                }
                groups.AddRange(matches.Where(x => x.Value.Count > 1).Select(x => new EchoGroup(x.Key, sizeGroup.Key, x.Value)));
            }
            cancellationToken.ThrowIfCancellationRequested();
            long savings = 0;
            foreach (var group in groups)
            {
                var removable = group.Files.Count(x => x.Snapshot.LinkCount == 1 && x.Aliases.Count == 1);
                if (removable == group.Files.Count) removable--; // retain a distinct physical copy
                // Saturate huge logical totals instead of wrapping into a misleading negative number.
                savings = (long)Math.Min((decimal)long.MaxValue, (decimal)savings + (decimal)group.Length * removable);
            }
            return new EchoScanResult(groups, issues, savings, hashed);
        }, cancellationToken);
    }

    /// <summary>Explicit selections only. Revalidates current candidate and unselected survivor before each Trash.
    /// Cancellation retains completed outcomes; a cancellation/exception during Trash is indeterminate.</summary>
    public async Task<EchoCleanupResult> CleanupAsync(EchoScanResult scan, IEnumerable<string> selectedPaths,
        EchoTrash trash, CancellationToken cancellationToken = default)
    {
        var outcomes = new List<EchoCleanupOutcome>();
        var selected = new List<string>();
        foreach (var path in selectedPaths)
        {
            try { selected.Add(Path.TrimEndingDirectorySeparator(Path.GetFullPath(path))); }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
            { outcomes.Add(new(path, EchoCleanupStatus.Refused, ex.Message)); }
        }
        selected = selected.Distinct(StringComparer.Ordinal).ToList();
        var selectedSet = selected.ToHashSet(StringComparer.Ordinal);
        if (selected.Count == 0) return new(outcomes);
        IDisposable lease;
        try { lease = await mutations.AcquireAsync(cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException)
        { return new(outcomes.Concat(selected.Select(x => new EchoCleanupOutcome(x, EchoCleanupStatus.Cancelled)))); }
        using (lease)
        {
            foreach (var path in selected)
            {
                if (cancellationToken.IsCancellationRequested)
                { outcomes.Add(new(path, EchoCleanupStatus.Cancelled)); continue; }
                var group = scan.Groups.FirstOrDefault(g => g.Files.Any(f => f.Aliases.Contains(path, StringComparer.Ordinal)));
                var candidate = group?.Files.First(f => f.Aliases.Contains(path, StringComparer.Ordinal));
                if (candidate is null || candidate.Snapshot.LinkCount != 1 || candidate.Aliases.Count != 1)
                { outcomes.Add(new(path, EchoCleanupStatus.Refused, "Not an eligible single-link scanned file.")); continue; }
                try
                {
                    var current = await ValidateAsync(candidate, group!.Sha256, cancellationToken).ConfigureAwait(false);
                    if (current is null || current.LinkCount != 1)
                    { outcomes.Add(new(path, EchoCleanupStatus.Refused, "Candidate changed or cannot be verified.")); continue; }
                    FileSnapshot? survivor = null;
                    foreach (var retained in group.Files.Where(f => f.Snapshot.Identity != current.Identity && !f.Aliases.Any(selectedSet.Contains)))
                    {
                        survivor = await ValidateAsync(retained, group.Sha256, cancellationToken).ConfigureAwait(false);
                        if (survivor is not null) break;
                    }
                    if (survivor is null || !StillCurrent(current) || !StillCurrent(survivor))
                    { outcomes.Add(new(path, EchoCleanupStatus.Refused, "No unchanged, unselected verified survivor.")); continue; }
                    cancellationToken.ThrowIfCancellationRequested();
                }
                catch (OperationCanceledException)
                { outcomes.Add(new(path, EchoCleanupStatus.Cancelled)); continue; }

                // The delegate is the sole mutation boundary. Never fall back to File.Delete.
                try
                {
                    var result = await trash(path, cancellationToken).ConfigureAwait(false);
                    outcomes.Add(new(path, result.Succeeded ? EchoCleanupStatus.Trashed : EchoCleanupStatus.TrashRefused, result.Detail));
                }
                catch (Exception ex)
                { outcomes.Add(new(path, EchoCleanupStatus.Indeterminate, ex.Message)); }
            }
        }
        return new(outcomes);
    }

    private async Task<FileSnapshot?> ValidateAsync(EchoFile file, string sha256, CancellationToken cancellationToken)
    {
        var result = await fingerprints.ComputeAsync(file.Snapshot.Path, cancellationToken).ConfigureAwait(false);
        return result.Fingerprint is { } current && current.Snapshot == file.Snapshot && current.Sha256 == sha256
            ? current.Snapshot : null;
    }
    private bool StillCurrent(FileSnapshot expected)
    {
        var result = metadata.Read(expected.Path);
        return result.Refusal is null && result.Snapshot == expected;
    }
    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    { public void Report(T value) => report(value); }
}
