namespace AZOTH.Core.Files;

public sealed record FileEnumerationIssue(string Path, FileRefusal Refusal);
public sealed class FileEnumerationResult
{
    public IReadOnlyList<FileSnapshot> Files { get; }
    public IReadOnlyList<FileEnumerationIssue> Issues { get; }
    internal FileEnumerationResult(IEnumerable<FileSnapshot> files, IEnumerable<FileEnumerationIssue> issues)
        => (Files, Issues) = (Array.AsReadOnly(files.ToArray()), Array.AsReadOnly(issues.ToArray()));
}

/// <summary>No linked directories; directory identities deduplicate overlapping roots and bind-mount cycles.</summary>
public sealed class FileTreeEnumerator(IFileMetadata metadata)
{
    public FileEnumerationResult Enumerate(IEnumerable<string> roots, bool recursive = true,
        IProgress<int>? progress = null, CancellationToken cancellationToken = default, IEnumerable<string>? excludedRoots = null)
    {
        var excluded = (excludedRoots ?? []).Select(p => Path.TrimEndingDirectorySeparator(Path.GetFullPath(p))).ToArray();
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var files = new List<FileSnapshot>(); var issues = new List<FileEnumerationIssue>();
        var directories = new HashSet<FileIdentity>(); var paths = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<(string Path, bool Root)>(roots.Select(x => (x, true)).Reverse());
        while (pending.TryPop(out var item))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (excluded.Length > 0)
            {
                string normalized;
                try { normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(item.Path)); }
                catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException)
                { issues.Add(new(item.Path, FileFailures.FromException(ex))); continue; }
                if (excluded.Any(root => normalized.Equals(root, comparison) || normalized.StartsWith(Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar, comparison))) continue;
            }
            var result = metadata.Read(item.Path);
            if (result.Refusal is { } refusal) { issues.Add(new(item.Path, refusal)); continue; }
            var entry = result.Snapshot;
            if (entry?.Identity is null) { issues.Add(new(item.Path, new(FileRefusalKind.IdentityUnavailable, "Physical identity unavailable."))); continue; }
            if (entry.Kind == FileEntryKind.Directory)
            {
                if ((!item.Root && !recursive) || !directories.Add(entry.Identity)) continue;
                try
                {
                    foreach (var child in Directory.EnumerateFileSystemEntries(entry.Path))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        pending.Push((child, false));
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                { issues.Add(new(entry.Path, FileFailures.FromException(ex))); }
            }
            else if (FileFailures.ValidateRegular(result) is { } error) issues.Add(new(entry.Path, error));
            else if (paths.Add(entry.Path)) { files.Add(entry); progress?.Report(files.Count); }
        }
        return new(files, issues);
    }
}
