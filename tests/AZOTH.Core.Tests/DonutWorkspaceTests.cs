using AZOTH.Core.Donuts;
using AZOTH.Core.Presets;
using AZOTH.Core.Profiles;
using AZOTH.Core.Settings;

namespace AZOTH.Core.Tests;

public class DonutWorkspaceTests
{
    [Fact]
    public void Fresh_and_reset_library_remain_empty_after_consistency_repair_and_restart()
    {
        using var dir = new TempDir();
        var store = new UserDataStore(dir.Path);
        store.Load();
        Assert.Empty(store.Profiles);
        Assert.NotEmpty(store.Presets);
        store.SaveLibrary();
        store.Settings.SchemaVersion = 1;
        store.SaveSettings();
        var reloaded = new UserDataStore(dir.Path);
        reloaded.Load();
        Assert.Empty(reloaded.Profiles);
        reloaded.Profiles.Add(new ToolbarProfile { PresetIds = ["to-jpg"] });
        reloaded.ResetLibrary();
        Assert.Empty(reloaded.Profiles);
        reloaded.Load();
        Assert.Empty(reloaded.Profiles);
    }

    [Fact]
    public void Empty_workspace_requires_explicit_nonempty_use_and_never_selects_templates()
    {
        using var dir = new TempDir();
        var store = new UserDataStore(dir.Path); store.Load();
        using var workspace = new DonutWorkspace(store);
        Assert.False(workspace.IsConfigured);
        Assert.Empty(workspace.Draft.Profile.PresetIds);
        Assert.Empty(workspace.Saved);
        Assert.NotEmpty(workspace.Templates);
        Assert.Throws<InvalidOperationException>(() => workspace.UseDraft());
        workspace.LoadTemplate("images");
        Assert.NotEmpty(workspace.Draft.Profile.PresetIds);
        Assert.Null(workspace.Active);
        workspace.ResetDraft();
        using var restarted = new DonutWorkspace(store);
        Assert.Empty(restarted.Draft.Profile.PresetIds);
        Assert.False(restarted.IsConfigured);
    }

    [Fact]
    public void Saving_updating_and_loading_are_independent_of_explicit_activation_and_persist()
    {
        using var dir = new TempDir();
        var store = new UserDataStore(dir.Path); store.Load();
        using var workspace = new DonutWorkspace(store);
        var action = workspace.AddAction("to-jpg");
        workspace.ConfigureAction(action, p => p.Image.Quality = 31);
        var saved = workspace.SaveAs("Photos");
        Assert.Null(workspace.Active);
        workspace.Activate(saved);
        var activeId = workspace.Active!.Profile.PresetIds.Single();
        workspace.ConfigureAction(action, p => p.Image.Quality = 74);
        workspace.UpdateSaved(saved);
        Assert.Equal(31, store.FindPreset(activeId)!.Image.Quality);
        Assert.Equal(74, workspace.Saved.Single().Snapshot.Presets.Single().Image.Quality);
        workspace.ResetDraft();
        workspace.Activate(saved);
        Assert.Empty(workspace.Draft.Profile.PresetIds);
        workspace.LoadSaved(saved);
        Assert.Equal(74, workspace.Draft.Presets.Single().Image.Quality);
        workspace.ConfigureAction(action, p => p.Image.Quality = 16);
        using var restarted = new DonutWorkspace(store);
        Assert.Equal(16, restarted.Draft.Presets.Single().Image.Quality);
        Assert.Equal(74, restarted.Active!.Presets.Single().Image.Quality);
        Assert.Equal(saved, restarted.Active.Profile.Id);
        Assert.Equal(31, store.FindPreset(activeId)!.Image.Quality);
    }

    [Fact]
    public void Named_operations_validate_conflicts_and_deletion_keeps_an_unnamed_active_snapshot()
    {
        using var dir = new TempDir();
        var store = new UserDataStore(dir.Path); store.Load();
        using var workspace = new DonutWorkspace(store);
        workspace.AddAction("to-jpg");
        var original = workspace.SaveAs(" Photos ");
        Assert.Throws<ArgumentException>(() => workspace.SaveAs("photos"));
        Assert.Throws<ArgumentException>(() => workspace.SaveAs("  "));
        var copy = workspace.Duplicate(original, "Travel");
        Assert.NotEqual(original, copy);
        workspace.Rename(copy, "Trips");
        Assert.Throws<ArgumentException>(() => workspace.Rename(copy, "Photos"));
        workspace.Activate(original);
        workspace.Delete(copy);
        Assert.Equal(original, workspace.Active!.SavedPresetId);
        workspace.Delete(original);
        Assert.True(workspace.IsConfigured);
        Assert.Null(workspace.Active!.SavedPresetId);
        Assert.Empty(workspace.Saved);
        using var restarted = new DonutWorkspace(store);
        Assert.True(restarted.IsConfigured);
        Assert.Empty(restarted.Saved);
    }

    [Fact]
    public void Recipe_and_group_execution_copies_survive_switch_delete_reset_and_external_mutation()
    {
        using var dir = new TempDir();
        var store = new UserDataStore(dir.Path); store.Load();
        store.Presets.Add(new Preset
        {
            Id = "my-group",
            Kind = PresetKind.Group,
            TargetFormat = Preset.GroupTarget,
            SubPresetIds = ["to-jpg"]
        });
        using var workspace = new DonutWorkspace(store);
        var group = workspace.AddAction("my-group");
        var child = workspace.Draft.Presets.Single(p => !p.IsGroup).Id;
        workspace.ConfigureAction(child, p => p.Image.Quality = 28);
        var saved = workspace.SaveAs("Group");
        workspace.UseDraft();
        var runningGroupId = workspace.Active!.Profile.PresetIds.Single();
        var runningChildId = store.FindPreset(runningGroupId)!.SubPresetIds.Single();
        workspace.ConfigureAction(child, p => p.Image.Quality = 90);
        workspace.Activate(saved);
        workspace.Delete(saved);
        workspace.ResetWorkspace();
        store.ResetLibrary();
        Assert.Equal(28, store.FindConversion(runningChildId)!.Image.Quality);
        Assert.Null(store.FindConversion(runningGroupId));
        var escaped = store.FindPreset(runningChildId)!; escaped.Image.Quality = 1;
        Assert.Equal(28, store.FindPreset(runningChildId)!.Image.Quality);
        Assert.DoesNotContain(store.Presets, p => p.Id == runningChildId);
        Assert.Equal(runningChildId, store.FindPreset(runningGroupId)!.SubPresetIds.Single());
    }

    [Fact]
    public void Public_views_and_configuration_callbacks_cannot_mutate_saved_or_active_internals()
    {
        using var dir = new TempDir();
        var store = new UserDataStore(dir.Path); store.Load();
        using var workspace = new DonutWorkspace(store);
        var id = workspace.AddAction("to-jpg");
        Preset? retained = null;
        workspace.ConfigureAction(id, p => { p.Image.Quality = 47; retained = p; });
        var saved = workspace.SaveAs("Safe"); workspace.Activate(saved);
        retained!.Image.Quality = 2;
        workspace.Draft.Presets.Single().Image.Quality = 3;
        workspace.Saved.Single().Snapshot.Presets.Single().Image.Quality = 4;
        workspace.Active!.Presets.Single().Image.Quality = 5;
        Assert.Equal(47, workspace.Draft.Presets.Single().Image.Quality);
        Assert.Equal(47, workspace.Saved.Single().Snapshot.Presets.Single().Image.Quality);
        Assert.Equal(47, workspace.Active!.Presets.Single().Image.Quality);
    }

    [Fact]
    public void Face_order_limits_and_discard_are_persisted_without_changing_active()
    {
        using var dir = new TempDir();
        var store = new UserDataStore(dir.Path); store.Load();
        using var workspace = new DonutWorkspace(store);
        var a = workspace.AddAction("to-jpg", DonutFace.Back);
        var b = workspace.AddAction("to-png", DonutFace.Back);
        workspace.MoveAction(b, DonutFace.Back, 0);
        Assert.Equal(new[] { b, a }, workspace.Draft.Profile.BackPresetIds);
        workspace.UseDraft();
        workspace.RemoveAction(a, DonutFace.Back);
        Assert.Single(workspace.Draft.Profile.BackPresetIds!);
        workspace.DiscardDraft();
        Assert.Equal(2, workspace.Draft.Profile.BackPresetIds!.Count);
        workspace.ResetDraft();
        for (var i = 0; i < 12; i++) workspace.AddAction("to-jpg");
        Assert.Throws<InvalidOperationException>(() => workspace.AddAction("to-png"));
        Assert.Equal(2, workspace.Active!.Profile.BackPresetIds!.Count);
    }

    [Fact]
    public void Legacy_custom_layouts_and_modified_builtin_recipes_are_preserved_without_activation()
    {
        using var dir = new TempDir();
        var store = new UserDataStore(dir.Path); store.Load();
        store.Profiles.AddRange(BuiltInData.CreateProfiles());
        store.FindPreset("to-jpg")!.Image.Quality = 17;
        store.Profiles.Add(new ToolbarProfile { Id = "custom", Name = "My custom", PresetIds = ["to-png"] });
        store.SaveLibrary();
        using var workspace = new DonutWorkspace(store);
        Assert.Contains(workspace.Saved, s => s.Id == "custom");
        Assert.Contains(workspace.Saved, s => s.Id == "images");
        Assert.DoesNotContain(workspace.Saved, s => s.Id == "archives");
        Assert.False(workspace.IsConfigured);
        Assert.Empty(workspace.Draft.Profile.PresetIds);
        store.Profiles.Add(new ToolbarProfile { Id = "import", Name = "Imported", PresetIds = ["to-webp"] });
        store.SaveLibrary();
        Assert.Contains(workspace.Saved, s => s.Id == "import");
        using var restarted = new DonutWorkspace(store);
        Assert.Equal(workspace.Saved.Count, restarted.Saved.Count);
        Assert.False(restarted.IsConfigured);
    }

    [Fact]
    public void Explicit_builtin_import_is_a_saved_choice_while_unchanged_legacy_layouts_are_only_templates()
    {
        using var sourceDir = new TempDir();
        var source = new UserDataStore(sourceDir.Path); source.Load();
        source.Profiles.AddRange(BuiltInData.CreateProfiles());
        var bundle = Path.Combine(sourceDir.Path, "bundle.json"); source.Export(bundle);
        using var dir = new TempDir();
        var store = new UserDataStore(dir.Path); store.Load();
        using var workspace = new DonutWorkspace(store);
        store.Import(bundle);
        Assert.Contains(workspace.Saved, p => p.Id == "images");
        Assert.False(workspace.IsConfigured);
        workspace.ResetWorkspace();
        using var restarted = new DonutWorkspace(store);
        Assert.Empty(restarted.Saved);
        Assert.False(restarted.IsConfigured);
    }

    [Fact]
    public void Persisted_active_group_recipes_reregister_with_a_new_store_after_catalog_reset()
    {
        using var dir = new TempDir();
        var store = new UserDataStore(dir.Path); store.Load();
        using var workspace = new DonutWorkspace(store);
        workspace.AddAction("rotate"); workspace.UseDraft();
        var groupId = workspace.Active!.Profile.PresetIds.Single();
        var childId = store.FindPreset(groupId)!.SubPresetIds.First();
        var expected = store.FindPreset(childId)!.Image.Rotation;
        store.ResetLibrary();
        var newStore = new UserDataStore(dir.Path); newStore.Load();
        using var restarted = new DonutWorkspace(newStore);
        Assert.Equal(expected, newStore.FindConversion(childId)!.Image.Rotation);
        Assert.NotNull(newStore.FindPreset(groupId));
        Assert.DoesNotContain(newStore.Presets, p => p.Id == childId);
    }

    [Fact]
    public void Corrupt_state_is_backed_up_and_future_schema_is_never_overwritten()
    {
        using var dir = new TempDir();
        var store = new UserDataStore(dir.Path); store.Load();
        var path = Path.Combine(dir.Path, "donuts.json");
        File.WriteAllText(path, "{ broken");
        using (var workspace = new DonutWorkspace(store)) Assert.False(workspace.IsConfigured);
        var backup = Assert.Single(Directory.GetFiles(dir.Path, "donuts.json.broken-*"));
        Assert.Equal("{ broken", File.ReadAllText(backup));
        File.WriteAllText(path, "{ \"schemaVersion\": 999 }");
        Assert.Throws<NotSupportedException>(() => new DonutWorkspace(store));
        Assert.Contains("999", File.ReadAllText(path));
    }

    [Fact]
    public void Unchanged_legacy_templates_never_become_saved_or_active()
    {
        using var dir = new TempDir();
        var store = new UserDataStore(dir.Path); store.Load();
        store.Profiles.AddRange(BuiltInData.CreateProfiles()); store.SaveLibrary();
        using var workspace = new DonutWorkspace(store);
        Assert.Empty(workspace.Saved);
        Assert.False(workspace.IsConfigured);
        Assert.Empty(workspace.Draft.Presets);
    }

    [Fact]
    public void Empty_legacy_profiles_still_upgrade_custom_recipe_options_without_adding_layouts()
    {
        using var dir = new TempDir();
        var store = new UserDataStore(dir.Path); store.Load();
        store.Settings.SchemaVersion = 7;
        store.Presets.Add(new Preset { Id = "legacy", Output = { Location = OutputLocation.SameFolder } });
        store.SaveSettings(); store.SaveLibrary();
        var upgraded = new UserDataStore(dir.Path); upgraded.Load();
        Assert.Empty(upgraded.Profiles);
        Assert.Equal(OutputLocation.Default, upgraded.FindPreset("legacy")!.Output.Location);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Migrated_saved_edits_survive_source_changes_and_explicit_import(bool explicitImport)
    {
        using var dir = new TempDir();
        var store = new UserDataStore(dir.Path); store.Load();
        store.Profiles.Add(new ToolbarProfile { Id = "custom", Name = "Source", PresetIds = ["to-jpg"], BackPresetIds = [] });
        store.SaveLibrary();
        using var workspace = new DonutWorkspace(store);
        workspace.LoadSaved("custom");
        workspace.ConfigureAction("to-jpg", p => p.Image.Quality = 31);
        var back = workspace.AddAction("to-png", DonutFace.Back);
        workspace.UpdateSaved("custom");
        workspace.Rename("custom", "Independent");
        store.FindPreset("to-jpg")!.Image.Quality = 84;
        store.Profiles.Single().Name = "Changed source";
        store.Profiles.Single().PresetIds.Add("to-webp");
        if (explicitImport)
        {
            var bundle = Path.Combine(dir.Path, "import.json");
            store.Export(bundle);
            store.Import(bundle);
        }
        else store.SaveLibrary();
        var saved = workspace.Saved.Single(p => p.Id == "custom");
        Assert.Equal("Independent", saved.Name);
        Assert.Equal(new[] { "to-jpg" }, saved.Snapshot.Profile.PresetIds);
        Assert.Equal(new[] { back }, saved.Snapshot.Profile.BackPresetIds);
        Assert.Equal(31, saved.Snapshot.Presets.Single(p => p.Id == "to-jpg").Image.Quality);
        if (explicitImport)
            Assert.Contains(workspace.Saved, p => p.Id != "custom" && p.Snapshot.Profile.PresetIds.Contains("to-webp"));
        var newStore = new UserDataStore(dir.Path); newStore.Load();
        using var restarted = new DonutWorkspace(newStore);
        var preserved = restarted.Saved.Single(p => p.Id == "custom");
        Assert.Equal("Independent", preserved.Name);
        Assert.Equal(31, preserved.Snapshot.Presets.Single(p => p.Id == "to-jpg").Image.Quality);
        Assert.False(restarted.IsConfigured);
    }

    [Theory]
    [InlineData(32, false)]
    [InlineData(32, true)]
    [InlineData(7, false)]
    [InlineData(7, true)]
    public void Retained_empty_builtin_layout_stays_empty_through_migration_workspace_and_restart(int schema, bool missingBack)
    {
        using var dir = new TempDir();
        var store = new UserDataStore(dir.Path); store.Load();
        store.Settings.SchemaVersion = schema;
        store.Presets.Add(new Preset { Id = "legacy", Output = { Location = OutputLocation.SameFolder } });
        store.SaveSettings(); store.SaveLibrary();
        // Write the old file directly: SaveLibrary consistency repair normalizes a missing back face.
        File.WriteAllText(Path.Combine(dir.Path, "profiles.json"), missingBack
            ? "[{\"id\":\"images\",\"presetIds\":[]}]"
            : "[{\"id\":\"images\",\"presetIds\":[],\"backPresetIds\":[]}]");
        var upgraded = new UserDataStore(dir.Path); upgraded.Load();
        var profile = upgraded.Profiles.Single(p => p.Id == "images");
        Assert.Empty(profile.PresetIds);
        Assert.Empty(profile.BackPresetIds!);
        if (schema == 7) Assert.Equal(OutputLocation.Default, upgraded.FindPreset("legacy")!.Output.Location);
        using var workspace = new DonutWorkspace(upgraded);
        var saved = workspace.Saved.Single(p => p.Id == "images");
        Assert.Empty(saved.Snapshot.Profile.PresetIds);
        Assert.Empty(saved.Snapshot.Profile.BackPresetIds!);
        Assert.Empty(saved.Snapshot.Presets);
        Assert.False(workspace.IsConfigured);
        Assert.Throws<InvalidOperationException>(() => workspace.Activate("images"));
        upgraded.SaveLibrary();
        var newStore = new UserDataStore(dir.Path); newStore.Load();
        Assert.Empty(newStore.Profiles.Single(p => p.Id == "images").PresetIds);
        Assert.Empty(newStore.Profiles.Single(p => p.Id == "images").BackPresetIds!);
        using var restarted = new DonutWorkspace(newStore);
        Assert.Empty(restarted.Saved.Single(p => p.Id == "images").Snapshot.Presets);
        Assert.False(restarted.IsConfigured);
    }

    [Fact]
    public void Migration_does_not_refill_an_explicitly_empty_face_of_a_nonempty_layout()
    {
        using var dir = new TempDir();
        var store = new UserDataStore(dir.Path); store.Load();
        store.Settings.SchemaVersion = 32;
        store.Profiles.Add(new ToolbarProfile { Id = "images", PresetIds = ["to-jpg"], BackPresetIds = [] });
        store.SaveSettings(); store.SaveLibrary();
        var upgraded = new UserDataStore(dir.Path); upgraded.Load();
        Assert.Equal(new[] { "to-jpg" }, upgraded.Profiles.Single(p => p.Id == "images").PresetIds);
        Assert.Empty(upgraded.Profiles.Single(p => p.Id == "images").BackPresetIds!);
    }
}
