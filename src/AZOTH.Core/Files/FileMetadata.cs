namespace AZOTH.Core.Files;

public enum FileEntryKind { RegularFile, Directory, Link, Other }
public enum FileRefusalKind { Missing, AccessDenied, InvalidPath, Link, NotRegularFile, IdentityUnavailable, Changed, IoError, Unsupported }
public sealed record FileIdentity(string Volume, string FileId);
/// <param name="ChangeStamp">Opaque native change-time stamp, retaining sub-tick precision where available.</param>
public sealed record FileSnapshot(string Path, FileIdentity? Identity, ulong LinkCount, long Length,
    DateTimeOffset ModifiedUtc, FileEntryKind Kind, string ChangeStamp = "");
public sealed record FileRefusal(FileRefusalKind Kind, string Detail);
public sealed record FileMetadataResult(FileSnapshot? Snapshot, FileRefusal? Refusal);
/// <summary>Reads without following the leaf or any linked/reparse ancestor. Unknown identity is never safe to mutate.</summary>
public interface IFileMetadata { FileMetadataResult Read(string path); }

/// <summary>Portable ancestor validation around an OS-specific, non-following metadata read.
/// This detects observed replacements; it cannot freeze independent external filesystem writers.</summary>
public abstract class SafeFileMetadata : IFileMetadata
{
    protected abstract FileMetadataResult ReadEntry(string normalizedPath);

    public FileMetadataResult Read(string path)
    {
        try
        {
            path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            var parents = new Stack<string>();
            for (var parent = Path.GetDirectoryName(path); parent is not null; parent = Path.GetDirectoryName(parent))
                parents.Push(parent);
            var verified = new List<FileSnapshot>();
            foreach (var parent in parents)
            {
                var result = ReadEntry(parent);
                if (result.Refusal is { } error) return new(null, error);
                if (result.Snapshot is not { Kind: FileEntryKind.Directory, Identity: not null } snapshot)
                    return new(null, new(result.Snapshot?.Kind == FileEntryKind.Link ? FileRefusalKind.Link : FileRefusalKind.IdentityUnavailable, parent));
                verified.Add(snapshot);
            }
            var entry = ReadEntry(path);
            foreach (var parent in verified)
            {
                var current = ReadEntry(parent.Path);
                if (current.Refusal is { } error) return new(null, error);
                if (current.Snapshot is not { Kind: FileEntryKind.Directory } snapshot || snapshot.Identity != parent.Identity)
                    return new(null, new(current.Snapshot?.Kind == FileEntryKind.Link ? FileRefusalKind.Link : FileRefusalKind.Changed, parent.Path));
            }
            return entry;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return new(null, FileFailures.FromException(ex));
        }
    }
}

public sealed class UnsupportedFileMetadata : IFileMetadata
{
    public FileMetadataResult Read(string path) => new(null, new(FileRefusalKind.Unsupported, "Native file identity is unavailable on this platform."));
}

internal static class FileFailures
{
    internal static FileRefusal FromException(Exception ex) => new(ex switch
    {
        FileNotFoundException or DirectoryNotFoundException => FileRefusalKind.Missing,
        UnauthorizedAccessException => FileRefusalKind.AccessDenied,
        ArgumentException or NotSupportedException => FileRefusalKind.InvalidPath,
        _ => FileRefusalKind.IoError
    }, ex.Message);

    internal static FileRefusal? ValidateRegular(FileMetadataResult result) => result.Refusal ?? (result.Snapshot switch
    {
        { Kind: FileEntryKind.Link } => new(FileRefusalKind.Link, "Linked files are excluded."),
        { Kind: not FileEntryKind.RegularFile } => new(FileRefusalKind.NotRegularFile, "Not a regular file."),
        { Identity: not null, LinkCount: > 0, Length: >= 0 } => null,
        _ => new(FileRefusalKind.IdentityUnavailable, "Physical identity or link count is unavailable.")
    });
}
