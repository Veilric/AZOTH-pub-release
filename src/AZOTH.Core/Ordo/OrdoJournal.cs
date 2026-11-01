using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
namespace AZOTH.Core.Ordo;

/// <summary>Versioned operation header with independently flushed, atomically replaced entry/ownership records.
/// No file contents are stored. Process-crash recovery is supported; directory-entry durability across sudden
/// power loss is platform dependent. Use the shared mutation coordinator when modifying an existing operation.</summary>
public sealed class OrdoJournal
{
    private readonly string _directory;
    public OrdoJournal(string directory) => _directory = Path.GetFullPath(directory);
    /// <summary>Creates or replaces a complete operation. The service uses incremental writes after creation.</summary>
    public void Save(OrdoOperation operation)
    {
        Validate(operation);
        Directory.CreateDirectory(OperationDirectory(operation.Id));
        for (var i = 0; i < operation.Entries.Count; i++) SaveEntry(operation, i);
        for (var i = 0; i < operation.CreatedDirectories.Count; i++) SaveDirectoryRecord(operation, i);
        SaveHeader(operation);
    }
    internal void SaveEntry(OrdoOperation operation, int index) => Write(Path.Combine(OperationDirectory(operation.Id), $"entry-{index:D8}.json"), operation.Entries[index], OrdoJsonContext.Default.OrdoJournalEntry);
    internal void SaveDirectory(OrdoOperation operation, int index)
    {
        SaveDirectoryRecord(operation, index);
        SaveHeader(operation);
    }
    private void SaveDirectoryRecord(OrdoOperation operation, int index) =>
        Write(Path.Combine(OperationDirectory(operation.Id), $"directory-{index:D8}.json"), operation.CreatedDirectories[index], OrdoJsonContext.Default.OrdoOwnedDirectory);
    internal void SaveHeader(OrdoOperation operation) => Write(Path.Combine(OperationDirectory(operation.Id), "operation.json"),
        new OrdoJournalHeader(operation.Version, operation.Id, operation.CreatedUtc, operation.ConfigurationSignature, operation.Entries.Count, operation.CreatedDirectories.Count, operation.Cancelled), OrdoJsonContext.Default.OrdoJournalHeader);
    private static void Write<T>(string path, T value, JsonTypeInfo<T> type)
    {
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, value, type);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temp, path, overwrite: true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    private static T Read<T>(string path, JsonTypeInfo<T> type)
    {
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize(stream, type) ?? throw new InvalidDataException("Empty ORDO journal record.");
    }
    public OrdoOperation Load(Guid id)
    {
        var folder = OperationDirectory(id);
        var header = Read(Path.Combine(folder, "operation.json"), OrdoJsonContext.Default.OrdoJournalHeader);
        if (header.Id != id || header.Version != 1 || header.EntryCount < 0 || header.DirectoryCount < 0)
            throw new InvalidDataException("Unsupported or invalid ORDO journal header.");
        var entries = Enumerable.Range(0, header.EntryCount).Select(i => Read(Path.Combine(folder, $"entry-{i:D8}.json"), OrdoJsonContext.Default.OrdoJournalEntry)).ToArray();
        var directories = Enumerable.Range(0, header.DirectoryCount).Select(i => Read(Path.Combine(folder, $"directory-{i:D8}.json"), OrdoJsonContext.Default.OrdoOwnedDirectory)).ToArray();
        var operation = new OrdoOperation(header.Version, id, header.CreatedUtc, header.ConfigurationSignature, Array.AsReadOnly(entries), Array.AsReadOnly(directories), header.Cancelled);
        Validate(operation);
        return operation;
    }
    public IReadOnlyList<OrdoOperation> List() => !Directory.Exists(_directory) ? Array.Empty<OrdoOperation>() :
        Array.AsReadOnly(Directory.EnumerateDirectories(_directory).Where(path => Guid.TryParseExact(Path.GetFileName(path), "N", out _) && File.Exists(Path.Combine(path, "operation.json")))
            .Select(path => Load(Guid.Parse(Path.GetFileName(path)))).OrderByDescending(op => op.CreatedUtc).ToArray());
    private string OperationDirectory(Guid id) => Path.Combine(_directory, id.ToString("N"));
    private static void Validate(OrdoOperation operation)
    {
        if (operation.Version != 1 || operation.Id == Guid.Empty || operation.Entries is null || operation.CreatedDirectories is null)
            throw new InvalidDataException("Unsupported or invalid ORDO journal.");
        foreach (var entry in operation.Entries)
        {
            if (!Enum.IsDefined(entry.State) || entry.Move?.Source?.Snapshot?.Identity is null || entry.Move.SourceParentIdentity is null ||
                !Path.IsPathFullyQualified(entry.Move.Source.Snapshot.Path) || !Path.IsPathFullyQualified(entry.Move.Destination) ||
                entry.Move.Source.Snapshot.Path == entry.Move.Destination || entry.Move.Source.Sha256.Length != 64)
                throw new InvalidDataException("Invalid ORDO entry.");
        }
        if (operation.CreatedDirectories.Any(d => d.Identity is null || !Path.IsPathFullyQualified(d.Path)))
            throw new InvalidDataException("Invalid ORDO directory ownership.");
    }
}
internal sealed record OrdoJournalHeader(int Version, Guid Id, DateTimeOffset CreatedUtc, string ConfigurationSignature,
    int EntryCount, int DirectoryCount, bool Cancelled);
