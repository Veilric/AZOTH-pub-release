using AZOTH.Core.Presets;
using AZOTH.Core.Profiles;

namespace AZOTH.Core.Donuts;

public enum DonutFace { Front, Back }

/// <summary>A layout and all its independent action recipes, including group children.
/// Workspace properties return detached copies: edit through workspace operations to persist changes.</summary>
public sealed class DonutSnapshot
{
    public ToolbarProfile Profile { get; set; } = new() { BackPresetIds = [] };
    public List<Preset> Presets { get; set; } = [];
    public string? SavedPresetId { get; set; }
    public DonutSnapshot Clone() => new()
    {
        Profile = Profile.Clone(),
        Presets = Presets.Select(p => p.Clone()).ToList(),
        SavedPresetId = SavedPresetId
    };
}

public sealed class SavedDonut
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public DonutSnapshot Snapshot { get; set; } = new();
    public SavedDonut Clone() => new() { Id = Id, Name = Name, Snapshot = Snapshot.Clone() };
}

internal sealed class DonutState
{
    public int SchemaVersion { get; set; } = 1;
    public DonutSnapshot Draft { get; set; } = new();
    public DonutSnapshot? Active { get; set; }
    public List<SavedDonut> Saved { get; set; } = [];
    public Dictionary<string, string> ImportedProfiles { get; set; } = [];
}
