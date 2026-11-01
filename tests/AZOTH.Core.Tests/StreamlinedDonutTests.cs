using AZOTH.Core.Donuts;
using AZOTH.Core.Presets;
using AZOTH.Core.Settings;

namespace AZOTH.Core.Tests;

public class StreamlinedDonutTests
{
    [Fact]
    public void Beginner_templates_have_six_categories_and_compact_faces()
    {
        using var dir = new TempDir();
        var store = new UserDataStore(dir.Path); store.Load();
        using var workspace = new DonutWorkspace(store);
        Assert.Equal(new[] { "pdf", "images", "archives", "office", "video", "audio" }, workspace.Templates.Select(t => t.Id));
        var expected = new Dictionary<string, string[]>
        {
            ["pdf"] = ["to-docx", "to-jpg", "to-png", "to-txt"],
            ["images"] = ["to-jpg", "to-png", "to-webp", "to-pdf", "to-gif"],
            ["archives"] = ["extract", "to-zip", "to-7z", "to-tgz"],
            ["office"] = ["to-pdf", "to-docx", "to-txt", "to-html", "spreadsheet-formats"],
            ["video"] = ["to-mp4", "to-webm", "to-mov", "to-gif"],
            ["audio"] = ["to-mp3", "to-wav", "to-m4a", "to-flac"],
        };
        foreach (var template in workspace.Templates)
        {
            Assert.Equal(expected[template.Id], template.Snapshot.Profile.PresetIds);
            Assert.Equal(template.Id == "pdf" ? new[] { "merge-pdf", "pdf-split", "compress-pdf" } : [], template.Snapshot.Profile.BackPresetIds);
            Assert.All(template.Snapshot.Presets, p => Assert.NotEqual("rar", p.TargetFormat));
        }
        Assert.Contains("rar", workspace.Templates.Single(t => t.Id == "archives").Snapshot.Profile.Extensions);
    }

    [Fact]
    public void Template_changes_do_not_refresh_saved_custom_recipes_or_legacy_faces()
    {
        using var dir = new TempDir();
        var store = new UserDataStore(dir.Path); store.Load();
        store.Profiles.Add(new() { Id = "images", Name = "My legacy images", PresetIds = ["to-tiff"], BackPresetIds = ["to-avif"] });
        store.FindPreset("to-jpg")!.Image.Quality = 37;
        store.SaveLibrary(); store.SaveSettings();
        using (var workspace = new DonutWorkspace(store))
        {
            workspace.AddAction("to-jpg"); workspace.SaveAs("My custom donut"); workspace.UseDraft();
        }
        var reloaded = new UserDataStore(dir.Path); reloaded.Load();
        using var reopened = new DonutWorkspace(reloaded);
        Assert.Equal(new[] { "to-tiff" }, reloaded.Profiles.Single().PresetIds);
        Assert.Equal(new[] { "to-avif" }, reloaded.Profiles.Single().BackPresetIds);
        Assert.Equal(37, reopened.Active!.Presets.Single().Image.Quality);
        Assert.Contains(reopened.Saved, s => s.Name == "My custom donut");
        Assert.True(reloaded.ResetProfile(reloaded.Profiles.Single()));
        Assert.Equal(new[] { "to-jpg", "to-png", "to-webp", "to-pdf", "to-gif" }, reloaded.Profiles.Single().PresetIds);
        Assert.Empty(reloaded.Profiles.Single().BackPresetIds!);
        Assert.Equal(37, reopened.Active.Presets.Single().Image.Quality);
        Assert.NotNull(reloaded.FindPreset("to-tiff"));
        Assert.NotNull(reloaded.FindPreset("to-avif"));
    }
}
