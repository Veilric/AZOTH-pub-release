using System.Security.Cryptography;

namespace AZOTH.Core.Files;

public sealed record FileFingerprint(FileSnapshot Snapshot, string Sha256);
public sealed record FileFingerprintResult(FileFingerprint? Fingerprint, FileRefusal? Refusal);

public sealed class FileFingerprintService(IFileMetadata metadata)
{
    /// <summary>Streams SHA-256 with bounded buffers; refuses changed identity, links, size or native timestamps.</summary>
    public async Task<FileFingerprintResult> ComputeAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var before = metadata.Read(path);
        if (FileFailures.ValidateRegular(before) is { } refusal) return new(null, refusal);
        try
        {
            await using var stream = new FileStream(before.Snapshot!.Path, new FileStreamOptions
            {
                Mode = FileMode.Open, Access = FileAccess.Read, Share = FileShare.Read,
                BufferSize = 65536, Options = FileOptions.Asynchronous | FileOptions.SequentialScan
            });
            var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
            var after = metadata.Read(path);
            if (after.Refusal is { } error) return new(null, error);
            if (before.Snapshot != after.Snapshot || stream.Length != before.Snapshot.Length)
                return new(null, new(FileRefusalKind.Changed, "File metadata changed while hashing."));
            return new(new(before.Snapshot, Convert.ToHexString(hash)), null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new(null, FileFailures.FromException(ex));
        }
    }
}
