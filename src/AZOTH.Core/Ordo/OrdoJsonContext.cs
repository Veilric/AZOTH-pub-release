using System.Text.Json.Serialization;
namespace AZOTH.Core.Ordo;

internal sealed record OrdoConfigurationData(string SourceRoot, bool Recursive, IReadOnlyList<OrdoRule> Rules);
[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(OrdoMonitorState))]
[JsonSerializable(typeof(OrdoOperation))]
[JsonSerializable(typeof(OrdoJournalHeader))]
[JsonSerializable(typeof(OrdoJournalEntry))]
[JsonSerializable(typeof(OrdoOwnedDirectory))]
[JsonSerializable(typeof(OrdoConfigurationData))]
internal partial class OrdoJsonContext : JsonSerializerContext;
