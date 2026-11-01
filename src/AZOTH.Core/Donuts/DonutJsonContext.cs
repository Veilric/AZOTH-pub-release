using System.Text.Json.Serialization;

namespace AZOTH.Core.Donuts;

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(DonutState))]
[JsonSerializable(typeof(DonutSnapshot))]
internal sealed partial class DonutJsonContext : JsonSerializerContext;
