using AZOTH.Core.Files;

namespace AZOTH.Core.Ordo;

public enum OrdoFileType { Any, Image, Video, Audio, Document, Archive, Code, Other }
public sealed record OrdoRule(string Id, string Destination, OrdoFileType Type = OrdoFileType.Any,
    string? Extension = null, DateTimeOffset? ModifiedFromUtc = null, DateTimeOffset? ModifiedBeforeUtc = null,
    bool GroupByModifiedMonth = false);
public sealed class OrdoConfiguration
{
    public string SourceRoot { get; }
    public bool Recursive { get; }
    public IReadOnlyList<OrdoRule> Rules { get; }
    public OrdoConfiguration(string sourceRoot, IEnumerable<OrdoRule> rules, bool recursive = true)
        => (SourceRoot, Rules, Recursive) = (Path.GetFullPath(sourceRoot), Array.AsReadOnly(rules.ToArray()), recursive);
}
public sealed record OrdoProgress(string Phase, int FilesDiscovered, int FilesPrepared);
public sealed record OrdoIssue(string Path, string Detail, bool Retryable = true);
public sealed record OrdoPlannedMove(string RuleId, FileFingerprint Source, string Destination, FileIdentity SourceParentIdentity);
public sealed class OrdoPlan
{
    public string ConfigurationSignature { get; }
    public IReadOnlyList<OrdoPlannedMove> Moves { get; }
    public IReadOnlyList<OrdoIssue> Issues { get; }
    internal OrdoPlan(string signature, IEnumerable<OrdoPlannedMove> moves, IEnumerable<OrdoIssue> issues)
        => (ConfigurationSignature, Moves, Issues) = (signature, Array.AsReadOnly(moves.ToArray()), Array.AsReadOnly(issues.ToArray()));
}
public enum OrdoEntryState { Prepared, Completed, Conflict, Skipped, UndoPrepared, Undone }
public sealed record OrdoJournalEntry(OrdoPlannedMove Move, OrdoEntryState State,
    FileFingerprint? DestinationFingerprint = null, string? Detail = null, bool UndoPending = false);
public sealed record OrdoOwnedDirectory(string Path, FileIdentity Identity);
public sealed record OrdoOperation(int Version, Guid Id, DateTimeOffset CreatedUtc, string ConfigurationSignature,
    IReadOnlyList<OrdoJournalEntry> Entries, IReadOnlyList<OrdoOwnedDirectory> CreatedDirectories, bool Cancelled = false);
public interface IOrdoFileMover { void Move(string source, string destination); }
public sealed class OrdoFileMover : IOrdoFileMover
{
    public void Move(string source, string destination) => File.Move(source, destination, overwrite: false);
}
