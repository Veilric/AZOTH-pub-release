using System.Text.Json;
using AZOTH.Core.Presets;
using AZOTH.Core.Profiles;
using AZOTH.Core.Settings;

namespace AZOTH.Core.Donuts;

/// <summary>Persistent editor state independent of the active execution snapshot. Call from the UI thread.
/// All exposed snapshots are defensive copies. Only UseDraft and Activate change the active selection.</summary>
public sealed class DonutWorkspace : IDisposable
{
    private readonly UserDataStore _store;
    private readonly string _path;
    private readonly DonutState _state;
    private bool _disposed;

    public DonutWorkspace(UserDataStore store)
    {
        _store = store;
        _path = Path.Combine(store.Directory, "donuts.json");
        _state = Read();
        ImportLegacyProfiles();
        if (_state.Active is { } active)
            _store.RegisterRuntimePresets(active.Presets);
        _store.LibraryChanged += LibraryChanged;
        _store.LibraryImported += LibraryImported;
    }

    public DonutSnapshot Draft => _state.Draft.Clone();
    public DonutSnapshot? Active => _state.Active?.Clone();
    public IReadOnlyList<SavedDonut> Saved => _state.Saved.Select(p => p.Clone()).ToList();
    public IReadOnlyList<SavedDonut> Templates => TemplatesFor(advanced: false);
    public IReadOnlyList<SavedDonut> TemplatesFor(bool advanced) => (advanced ? BuiltInData.CreateProfiles() : BuiltInData.CreatePrimaryProfiles()).Select(p => new SavedDonut
    {
        Id = p.Id,
        Name = p.Id,
        Snapshot = Capture(p, BuiltInData.CreatePresets())
    }).ToList();
    public bool IsConfigured => _state.Active is { } active && IsUsable(active);
    public event EventHandler? Changed;

    /// <summary>Copies an action template and its group children. Returns its private draft recipe ID.</summary>
    public string AddAction(string catalogPresetId, DonutFace face = DonutFace.Front)
    {
        var template = _store.Presets.FirstOrDefault(p => p.Id == catalogPresetId)
            ?? throw new ArgumentException("Unknown action template.", nameof(catalogPresetId));
        var draft = _state.Draft.Clone();
        var ring = Face(draft, face);
        if (ring.Count >= ToolbarProfile.MaxSlices)
            throw new InvalidOperationException("A face supports at most twelve actions.");
        var captured = Capture(new ToolbarProfile { PresetIds = [template.Id] }, _store.Presets);
        var copy = Remap(captured);
        draft.Presets.AddRange(copy.Presets);
        var id = copy.Profile.PresetIds.Single();
        ring.Add(id);
        SetDraft(draft);
        return id;
    }

    public void RemoveAction(string actionId, DonutFace face = DonutFace.Front)
    {
        var draft = _state.Draft.Clone();
        if (!Face(draft, face).Remove(actionId))
            throw new ArgumentException("Action is not on that face.", nameof(actionId));
        SetDraft(Capture(draft.Profile, draft.Presets, draft.SavedPresetId));
    }

    public void MoveAction(string actionId, DonutFace face, int newIndex)
    {
        var draft = _state.Draft.Clone();
        var ring = Face(draft, face);
        if (!ring.Contains(actionId)) throw new ArgumentException("Action is not on that face.", nameof(actionId));
        if (newIndex < 0 || newIndex >= ring.Count) throw new ArgumentOutOfRangeException(nameof(newIndex));
        ring.Remove(actionId);
        ring.Insert(newIndex, actionId);
        SetDraft(draft);
    }

    /// <summary>Edits a detached recipe, including group children by their IDs. IDs and group references stay valid.</summary>
    public void ConfigureAction(string actionId, Action<Preset> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var draft = _state.Draft.Clone();
        var index = draft.Presets.FindIndex(p => p.Id == actionId);
        if (index < 0) throw new ArgumentException("Unknown draft action.", nameof(actionId));
        var recipe = draft.Presets[index].Clone();
        configure(recipe);
        if (recipe.Id != actionId) throw new ArgumentException("Action IDs cannot change.");
        draft.Presets[index] = recipe.Clone();
        Validate(draft);
        SetDraft(draft);
    }

    /// <summary>Edits layout name, metadata and face membership/order; references must belong to this draft.</summary>
    public void ConfigureDraft(Action<ToolbarProfile> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var draft = _state.Draft.Clone();
        var profile = draft.Profile.Clone();
        configure(profile);
        profile.Id = draft.Profile.Id;
        draft.Profile = profile.Clone();
        Validate(draft);
        SetDraft(Capture(draft.Profile, draft.Presets, draft.SavedPresetId));
    }

    public void UseDraft()
    {
        if (!IsUsable(_state.Draft)) throw new InvalidOperationException("Choose at least one usable action first.");
        SetActive(_state.Draft);
    }

    public string SaveAs(string name)
    {
        name = ValidateName(name);
        var saved = new SavedDonut { Name = name, Snapshot = _state.Draft.Clone() };
        saved.Snapshot.Profile.Id = saved.Id;
        saved.Snapshot.Profile.Name = name;
        saved.Snapshot.Profile.NameKey = null;
        saved.Snapshot.SavedPresetId = saved.Id;
        _state.Saved.Add(saved);
        // Saving identifies the editor's source, without activating it.
        _state.Draft.SavedPresetId = saved.Id;
        _state.Draft.Profile.Id = saved.Id;
        Save();
        return saved.Id;
    }

    public void UpdateSaved(string id)
    {
        var saved = FindSaved(id);
        var copy = _state.Draft.Clone();
        copy.Profile.Id = id;
        copy.Profile.Name = saved.Name;
        copy.Profile.NameKey = null;
        copy.SavedPresetId = id;
        saved.Snapshot = copy;
        Save();
    }

    public void Rename(string id, string name)
    {
        var saved = FindSaved(id);
        name = ValidateName(name, id);
        saved.Name = name;
        saved.Snapshot.Profile.Name = name;
        saved.Snapshot.Profile.NameKey = null;
        Save();
    }

    public string Duplicate(string id, string name)
    {
        var source = FindSaved(id);
        name = ValidateName(name);
        var saved = new SavedDonut { Name = name, Snapshot = source.Snapshot.Clone() };
        saved.Snapshot.Profile.Id = saved.Id;
        saved.Snapshot.Profile.Name = name;
        saved.Snapshot.Profile.NameKey = null;
        saved.Snapshot.SavedPresetId = saved.Id;
        _state.Saved.Add(saved);
        Save();
        return saved.Id;
    }

    public void Delete(string id)
    {
        var saved = FindSaved(id);
        _state.Saved.Remove(saved);
        if (_state.Active?.SavedPresetId == id) _state.Active.SavedPresetId = null;
        if (_state.Draft.SavedPresetId == id) _state.Draft.SavedPresetId = null;
        Save();
    }

    public void Activate(string id)
    {
        var saved = FindSaved(id);
        if (!IsUsable(saved.Snapshot)) throw new InvalidOperationException("This donut has no usable actions.");
        SetActive(saved.Snapshot);
    }

    public void LoadSaved(string id) => SetDraft(FindSaved(id).Snapshot.Clone());

    public void LoadTemplate(string id, bool advanced = false)
    {
        var template = TemplatesFor(advanced).FirstOrDefault(p => p.Id == id)
            ?? TemplatesFor(advanced: true).FirstOrDefault(p => p.Id == id)
            ?? throw new ArgumentException("Unknown donut template.", nameof(id));
        var draft = Remap(template.Snapshot);
        draft.Profile.Id = Guid.NewGuid().ToString("N");
        draft.SavedPresetId = null;
        SetDraft(draft);
    }

    /// <summary>Explicitly discards edits, restoring the editor's saved source, otherwise the active donut or empty.</summary>
    public void DiscardDraft()
    {
        var source = _state.Saved.FirstOrDefault(p => p.Id == _state.Draft.SavedPresetId)?.Snapshot
            ?? _state.Active ?? new DonutSnapshot();
        SetDraft(source.Clone());
    }

    public void ResetDraft() => SetDraft(new DonutSnapshot());

    /// <summary>Clears user donut choices; running execution recipes remain registered in the store.</summary>
    public void ResetWorkspace()
    {
        _state.Draft = new DonutSnapshot();
        _state.Active = null;
        _state.Saved.Clear();
        // Remember old legacy inputs so a restart does not re-import choices explicitly cleared here.
        Save();
    }

    /// <summary>Preserves new legacy profiles without activation or replacing workspace-owned definitions.
    /// Unchanged built-ins are only templates; edited recipes count as a customized layout.</summary>
    public int ImportLegacyProfiles() => ImportProfiles(_store.Profiles, explicitImport: false);

    private int ImportProfiles(IEnumerable<ToolbarProfile> profiles, bool explicitImport)
    {
        var count = 0;
        var builtins = BuiltInData.CreateProfiles().ToDictionary(p => p.Id);
        var recipes = BuiltInData.CreatePresets();
        foreach (var profile in profiles)
        {
            var snapshot = Capture(profile, _store.Presets, profile.Id);
            var fingerprint = JsonSerializer.Serialize(snapshot, DonutJsonContext.Default.DonutSnapshot);
            if (!explicitImport && _state.ImportedProfiles.TryGetValue(profile.Id, out var previous) && previous == fingerprint) continue;
            _state.ImportedProfiles[profile.Id] = fingerprint;
            if (!explicitImport && builtins.TryGetValue(profile.Id, out var builtin))
            {
                var expected = Capture(builtin, recipes, profile.Id);
                if (fingerprint == JsonSerializer.Serialize(expected, DonutJsonContext.Default.DonutSnapshot)) continue;
            }
            var existing = _state.Saved.FirstOrDefault(p => p.Id == profile.Id);
            if (existing is not null)
            {
                // Once imported, a saved donut owns its settings. Routine catalog changes must not replace it.
                if (!explicitImport) continue;
                if (fingerprint == JsonSerializer.Serialize(existing.Snapshot, DonutJsonContext.Default.DonutSnapshot)) continue;
            }
            // An explicitly chosen conflicting import becomes another independent definition.
            var savedId = existing is null ? profile.Id : Guid.NewGuid().ToString("N");
            snapshot.Profile.Id = savedId;
            snapshot.SavedPresetId = savedId;
            var name = string.IsNullOrWhiteSpace(profile.Name) ? profile.Id : profile.Name.Trim();
            // Existing data can have duplicate display names. Preserve it with an explicit, visible suffix.
            var baseName = name;
            for (var n = 2; _state.Saved.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)); n++)
                name = $"{baseName} ({n})";
            _state.Saved.Add(new SavedDonut { Id = savedId, Name = name, Snapshot = snapshot });
            count++;
        }
        Save();
        return count;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _store.LibraryChanged -= LibraryChanged;
        _store.LibraryImported -= LibraryImported;
    }

    private void LibraryChanged(object? sender, EventArgs e) => ImportLegacyProfiles();
    private void LibraryImported(object? sender, PresetBundle bundle) => ImportProfiles(bundle.Profiles, explicitImport: true);
    private SavedDonut FindSaved(string id) => _state.Saved.FirstOrDefault(p => p.Id == id)
        ?? throw new ArgumentException("Unknown saved donut.", nameof(id));

    private string ValidateName(string name, string? excluding = null)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Enter a donut name.", nameof(name));
        name = name.Trim();
        if (_state.Saved.Any(p => p.Id != excluding && p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("A donut already has this name.", nameof(name));
        return name;
    }

    private static List<string> Face(DonutSnapshot snapshot, DonutFace face) => face switch
    {
        DonutFace.Front => snapshot.Profile.PresetIds,
        DonutFace.Back => snapshot.Profile.BackPresetIds ??= [],
        _ => throw new ArgumentOutOfRangeException(nameof(face))
    };

    private void SetDraft(DonutSnapshot draft) { Validate(draft); _state.Draft = draft; Save(); }
    private void SetActive(DonutSnapshot snapshot)
    {
        var active = Remap(snapshot);
        Validate(active);
        _store.RegisterRuntimePresets(active.Presets);
        _state.Active = active;
        Save();
    }

    private static bool IsUsable(DonutSnapshot snapshot)
    {
        var ids = snapshot.Profile.PresetIds.Concat(snapshot.Profile.BackPresetIds ?? []).ToHashSet();
        return snapshot.Presets.Any(p => ids.Contains(p.Id) && (!p.IsGroup || p.SubPresetIds.Count > 0));
    }

    private static DonutSnapshot Capture(ToolbarProfile profile, IEnumerable<Preset> presets, string? savedId = null)
    {
        var all = presets.ToDictionary(p => p.Id);
        var selected = profile.PresetIds.Concat(profile.BackPresetIds ?? []).ToHashSet();
        foreach (var group in selected.Where(all.ContainsKey).Select(id => all[id]).Where(p => p.IsGroup).ToList())
            selected.UnionWith(group.SubPresetIds);
        var snapshot = new DonutSnapshot
        {
            Profile = profile.Clone(),
            SavedPresetId = savedId,
            Presets = all.Values.Where(p => selected.Contains(p.Id)).Select(p => p.Clone()).ToList()
        };
        snapshot.Profile.BackPresetIds ??= [];
        Validate(snapshot);
        return snapshot;
    }

    private static DonutSnapshot Remap(DonutSnapshot snapshot)
    {
        var copy = snapshot.Clone();
        var ids = copy.Presets.ToDictionary(p => p.Id, _ => "donut-" + Guid.NewGuid().ToString("N"));
        copy.Profile.PresetIds = copy.Profile.PresetIds.Select(id => ids[id]).ToList();
        copy.Profile.BackPresetIds = (copy.Profile.BackPresetIds ?? []).Select(id => ids[id]).ToList();
        foreach (var preset in copy.Presets)
        {
            preset.Id = ids[preset.Id];
            if (preset.IsGroup) preset.SubPresetIds = preset.SubPresetIds.Select(id => ids[id]).ToList();
        }
        return copy;
    }

    private static void Validate(DonutSnapshot snapshot)
    {
        var ids = snapshot.Presets.Select(p => p.Id).ToHashSet();
        if (ids.Count != snapshot.Presets.Count) throw new InvalidDataException("Duplicate action IDs.");
        foreach (var face in new[] { snapshot.Profile.PresetIds, snapshot.Profile.BackPresetIds ?? [] })
            if (face.Count > ToolbarProfile.MaxSlices || face.Count != face.Distinct().Count() || face.Any(id => !ids.Contains(id)))
                throw new InvalidDataException("Invalid donut face or missing recipe.");
        var children = snapshot.Presets.Where(p => !p.IsGroup).Select(p => p.Id).ToHashSet();
        foreach (var group in snapshot.Presets.Where(p => p.IsGroup))
            if (group.SubPresetIds.Count > Preset.MaxSubPresets || group.SubPresetIds.Count != group.SubPresetIds.Distinct().Count()
                || group.SubPresetIds.Any(id => !children.Contains(id))) throw new InvalidDataException("Invalid group recipes.");
    }

    private DonutState Read()
    {
        if (!File.Exists(_path)) return new DonutState();
        try
        {
            using var stream = File.OpenRead(_path);
            var state = JsonSerializer.Deserialize(stream, DonutJsonContext.Default.DonutState)
                ?? throw new InvalidDataException("Empty donut state.");
            if (state.SchemaVersion != 1) throw new NotSupportedException("Unsupported donut state version; preserve it for a newer AZOTH.");
            Validate(state.Draft);
            if (state.Active is { } active) Validate(active);
            foreach (var saved in state.Saved) Validate(saved.Snapshot);
            return state;
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or NullReferenceException or ArgumentException)
        {
            // Do not overwrite the only copy of corrupt state. If a backup cannot be made, fail without writing.
            File.Copy(_path, _path + ".broken-" + Guid.NewGuid().ToString("N"));
            return new DonutState();
        }
    }

    private void Save()
    {
        System.IO.Directory.CreateDirectory(_store.Directory);
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, _state, DonutJsonContext.Default.DonutState);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, _path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
