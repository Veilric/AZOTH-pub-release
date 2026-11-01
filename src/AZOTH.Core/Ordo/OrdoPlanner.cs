using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using AZOTH.Core.Files;

namespace AZOTH.Core.Ordo;

public sealed class OrdoPlanner(IFileMetadata metadata, FileFingerprintService fingerprints, IEnumerable<string>? protectedRoots = null)
{
    private readonly string[] _protectedRoots = protectedRoots?.Select(Path.GetFullPath).ToArray() ?? [];
    internal IReadOnlyList<string> ProtectedRoots => _protectedRoots;
    public Task<OrdoPlan> PreviewAsync(OrdoConfiguration configuration, IProgress<OrdoProgress>? progress = null,
        CancellationToken cancellationToken = default, IEnumerable<string>? eligibleSourcePaths = null)
    {
        var requestedPaths = eligibleSourcePaths?.ToArray();
        return Task.Run(async () =>
        {
            var destinations = Validate(configuration);
            if (destinations.Any(d => _protectedRoots.Any(r => OrdoPaths.Within(d, r) || OrdoPaths.Within(r, d))))
                throw new ArgumentException("Destinations cannot overlap protected application data.");
            var signature = Signature(configuration);
            var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            var eligible = requestedPaths?.Select(path => Path.GetFullPath(path, configuration.SourceRoot)).ToHashSet(comparer);
            if (eligible is not null && eligible.Any(path => !OrdoPaths.Within(path, Path.TrimEndingDirectorySeparator(configuration.SourceRoot))))
                throw new ArgumentException("Eligible source paths must belong to the configured source root.");
            if (eligible is { Count: 0 }) return new OrdoPlan(signature, [], []);
            var files = new FileTreeEnumerator(metadata).Enumerate([configuration.SourceRoot], configuration.Recursive,
                new InlineProgress<int>(count => progress?.Report(new("Enumerating", count, 0))), cancellationToken, destinations.Concat(_protectedRoots));
            var issues = files.Issues.Select(i => new OrdoIssue(i.Path, i.Refusal.Detail)).ToList();
            var moves = new List<OrdoPlannedMove>();
            // Ignore case for reservations on every platform: conservative on case-sensitive filesystems.
            var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var identities = new HashSet<FileIdentity>();
            foreach (var file in files.Files.OrderBy(f => f.Path, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (eligible is not null && !eligible.Contains(file.Path)) continue;
                if (IsTemporary(file.Path))
                { issues.Add(new(file.Path, "Incomplete or temporary download; left in place.", false)); continue; }
                if (file.LinkCount != 1)
                { issues.Add(new(file.Path, "Hardlinked file; left in place to preserve its links.", false)); continue; }
                if (!identities.Add(file.Identity!))
                { issues.Add(new(file.Path, "Another path to this physical file is already included.", false)); continue; }
                var index = -1;
                for (var i = 0; i < configuration.Rules.Count; i++)
                    if (Matches(configuration.Rules[i], file)) { index = i; break; }
                if (index < 0) { issues.Add(new(file.Path, "No rule matched; left in place.", false)); continue; }
                var fingerprint = await fingerprints.ComputeAsync(file.Path, cancellationToken).ConfigureAwait(false);
                if (fingerprint.Fingerprint is not { } source || source.Snapshot != file)
                { issues.Add(new(file.Path, fingerprint.Refusal?.Detail ?? "Source changed during preparation.")); continue; }
                var parent = metadata.Read(Path.GetDirectoryName(file.Path)!);
                if (parent.Snapshot is not { Kind: FileEntryKind.Directory, Identity: not null } directory)
                { issues.Add(new(file.Path, "Source parent cannot be verified.")); continue; }
                var rule = configuration.Rules[index];
                var target = rule.GroupByModifiedMonth ? Path.Combine(destinations[index], file.ModifiedUtc.UtcDateTime.ToString("yyyy-MM", CultureInfo.InvariantCulture)) : destinations[index];
                var name = Path.GetFileName(file.Path); var destination = Path.Combine(target, name); var suffix = 0;
                while (reserved.Contains(destination) || OrdoPaths.Exists(metadata, destination))
                    destination = Path.Combine(target, $"{Path.GetFileNameWithoutExtension(name)} ({++suffix}){Path.GetExtension(name)}");
                reserved.Add(destination);
                moves.Add(new(rule.Id, source, destination, directory.Identity));
                progress?.Report(new("Preparing", files.Files.Count, moves.Count));
            }
            return new OrdoPlan(signature, moves, issues);
        }, cancellationToken);
    }

    internal static string Signature(OrdoConfiguration config) => Convert.ToHexString(SHA256.HashData(
        JsonSerializer.SerializeToUtf8Bytes(new OrdoConfigurationData(config.SourceRoot, config.Recursive, config.Rules), OrdoJsonContext.Default.OrdoConfigurationData)));

    internal static string[] Validate(OrdoConfiguration config)
    {
        if (config.Rules.Any(r => string.IsNullOrWhiteSpace(r.Id)) || config.Rules.Select(r => r.Id).Distinct(StringComparer.Ordinal).Count() != config.Rules.Count)
            throw new ArgumentException("Rules need unique, nonempty IDs.");
        return config.Rules.Select(rule =>
        {
            if (string.IsNullOrWhiteSpace(rule.Destination) || !Enum.IsDefined(rule.Type) || rule.ModifiedFromUtc >= rule.ModifiedBeforeUtc)
                throw new ArgumentException("Invalid rule destination, type or date interval.");
            if (rule.Destination.Split(['/', '\\']).Contains("..") || (!Path.IsPathFullyQualified(rule.Destination) && Path.IsPathRooted(rule.Destination)))
                throw new ArgumentException("Destination traversal or drive-relative paths are not allowed.");
            var path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rule.Destination, config.SourceRoot));
            if (OrdoPaths.Within(config.SourceRoot, path)) throw new ArgumentException("Destination must not equal or contain the source root.");
            if (rule.Extension?.IndexOfAny(['/', '\\', '*', '?', ':']) >= 0) throw new ArgumentException("Extension must be a literal file extension.");
            return path;
        }).ToArray();
    }
    internal static bool IsTemporary(string path)
    {
        var name = Path.GetFileName(path);
        return name.StartsWith("~$", StringComparison.Ordinal) || name.EndsWith('~') ||
            new[] { ".tmp", ".temp", ".part", ".partial", ".crdownload", ".download", ".opdownload", ".!ut", ".filepart", ".swp" }.Contains(Path.GetExtension(name), StringComparer.OrdinalIgnoreCase);
    }
    private static bool Matches(OrdoRule rule, FileSnapshot file) =>
        (rule.Type == OrdoFileType.Any || rule.Type == Classify(file.Path)) &&
        (string.IsNullOrWhiteSpace(rule.Extension) || Path.GetExtension(file.Path).Equals("." + rule.Extension.TrimStart('.'), StringComparison.OrdinalIgnoreCase)) &&
        (rule.ModifiedFromUtc is null || file.ModifiedUtc >= rule.ModifiedFromUtc) &&
        (rule.ModifiedBeforeUtc is null || file.ModifiedUtc < rule.ModifiedBeforeUtc);
    public static OrdoFileType Classify(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" or ".png" or ".gif" or ".webp" or ".svg" or ".bmp" or ".tiff" or ".tif" or ".heic" or ".avif" => OrdoFileType.Image,
        ".mp4" or ".mkv" or ".mov" or ".avi" or ".webm" => OrdoFileType.Video,
        ".mp3" or ".wav" or ".flac" or ".ogg" or ".m4a" or ".aac" => OrdoFileType.Audio,
        ".pdf" or ".txt" or ".doc" or ".docx" or ".xls" or ".xlsx" or ".ppt" or ".pptx" or ".odt" or ".ods" or ".odp" or ".md" or ".csv" or ".hwp" or ".hwpx" or ".rtf" or ".epub" or ".mobi" => OrdoFileType.Document,
        ".zip" or ".7z" or ".rar" or ".tar" or ".gz" or ".bz2" => OrdoFileType.Archive,
        ".cs" or ".js" or ".ts" or ".tsx" or ".py" or ".rs" or ".go" or ".cpp" or ".c" or ".html" or ".css" or ".json" => OrdoFileType.Code,
        _ => OrdoFileType.Other
    };
    private sealed class InlineProgress<T>(Action<T> action) : IProgress<T> { public void Report(T value) => action(value); }
}

internal static class OrdoPaths
{
    internal static bool Within(string path, string root) => path.Equals(root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) ||
        path.StartsWith(Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    internal static bool Missing(IFileMetadata metadata, string path) => metadata.Read(path).Refusal?.Kind == FileRefusalKind.Missing;
    // Failure to prove absence reserves a name; don't loop on an inaccessible parent forever.
    internal static bool Exists(IFileMetadata metadata, string path)
    {
        var result = metadata.Read(path);
        if (result.Refusal is { Kind: not FileRefusalKind.Missing } refusal) throw new IOException(refusal.Detail);
        return result.Snapshot is not null;
    }
    internal static FileSnapshot DirectorySnapshot(IFileMetadata metadata, string path)
    {
        var result = metadata.Read(path);
        if (result.Snapshot is not { Kind: FileEntryKind.Directory, Identity: not null } directory)
            throw new IOException(result.Refusal?.Detail ?? "Directory is linked or unavailable.");
        return directory;
    }
    internal static void ValidateParents(IFileMetadata metadata, string path)
    {
        for (var parent = Path.GetDirectoryName(path); parent is not null; parent = Path.GetDirectoryName(parent))
            DirectorySnapshot(metadata, parent);
    }
}
