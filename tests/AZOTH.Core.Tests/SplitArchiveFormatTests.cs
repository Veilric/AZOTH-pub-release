// Split archives and files (.001, #653): read-only archives on the archive donut, learned by existing libraries.

using AZOTH.Core.Formats;
using AZOTH.Core.Presets;
using AZOTH.Core.Profiles;
using AZOTH.Core.Settings;

namespace AZOTH.Core.Tests;

public class SplitArchiveFormatTests
{
    [Theory]
    [InlineData("backup.7z.001")]
    [InlineData("movie.mp4.001")]
    public void The_first_part_is_an_archive_on_the_archive_donut(string name)
    {
        var split = FormatRegistry.Detect(name);
        Assert.NotNull(split);
        Assert.Equal(("split", FormatCategory.Archive, false), (split.Id, split.Category, split.Writable));
        Assert.Equal("archives", ProfileSelector.Select(BuiltInData.CreateProfiles(), [name])?.Id);
    }

    [Fact]
    public void Version_53_libraries_learn_the_first_parts_once()
    {
        string[] parts = ["001", "7z.001", "zip.001", "tar.001"];
        var presets = BuiltInData.CreatePresets();
        var profiles = BuiltInData.CreateProfiles();
        var archive = profiles.Single(p => p.Id == "archives");
        archive.Extensions.RemoveAll(parts.Contains);

        Assert.True(SettingsMigrations.ApplyToLibrary(53, presets, profiles));

        Assert.All(parts, part => Assert.Contains(part, archive.Extensions));
        Assert.False(SettingsMigrations.ApplyToLibrary(53, presets, profiles));
    }
}
