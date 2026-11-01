using AZOTH.Core.Files;

namespace AZOTH.Core.Echo;

// Scan objects are detached read-only snapshots. UI selection lives outside these objects.
public sealed class EchoFile
{
    public FileSnapshot Snapshot { get; }
    public IReadOnlyList<string> Aliases { get; }
    internal EchoFile(FileSnapshot snapshot, IEnumerable<string> aliases)
        => (Snapshot, Aliases) = (snapshot, Array.AsReadOnly(aliases.ToArray()));
}
public sealed class EchoGroup
{
    public string Sha256 { get; }
    public long Length { get; }
    public IReadOnlyList<EchoFile> Files { get; }
    internal EchoGroup(string sha256, long length, IEnumerable<EchoFile> files)
        => (Sha256, Length, Files) = (sha256, length, Array.AsReadOnly(files.ToArray()));
}
public sealed record EchoIssue(string Path, FileRefusal Refusal);
public sealed record EchoProgress(string Phase, int FilesDiscovered, int FilesHashed);
public sealed class EchoScanResult
{
    public IReadOnlyList<EchoGroup> Groups { get; }
    public IReadOnlyList<EchoIssue> Issues { get; }
    /// <summary>Logical duplicate bytes eligible for removal while retaining a verified copy.
    /// Not allocated disk bytes: Trash retains content; compression, reflinks and external hardlinks affect storage.</summary>
    public long EstimatedSavingsBytes { get; }
    public int FilesHashed { get; }
    internal EchoScanResult(IEnumerable<EchoGroup> groups, IEnumerable<EchoIssue> issues, long estimatedSavingsBytes, int filesHashed)
        => (Groups, Issues, EstimatedSavingsBytes, FilesHashed) =
            (Array.AsReadOnly(groups.ToArray()), Array.AsReadOnly(issues.ToArray()), estimatedSavingsBytes, filesHashed);
}
public enum EchoCleanupStatus { Trashed, Refused, TrashRefused, Indeterminate, Cancelled }
public sealed record EchoCleanupOutcome(string Path, EchoCleanupStatus Status, string? Detail = null);
public sealed class EchoCleanupResult
{
    public IReadOnlyList<EchoCleanupOutcome> Outcomes { get; }
    internal EchoCleanupResult(IEnumerable<EchoCleanupOutcome> outcomes) => Outcomes = Array.AsReadOnly(outcomes.ToArray());
}
/// <summary>Success means the Trash provider confirmed the move. False is a provider refusal; exceptions leave the outcome indeterminate.</summary>
public sealed record TrashResult(bool Succeeded, string? Detail = null)
{
    public static TrashResult Success { get; } = new(true);
}
public delegate Task<TrashResult> EchoTrash(string path, CancellationToken cancellationToken);
