using Avalonia.Headless.XUnit;
using AZOTH.Core.Formats;
using AZOTH.Core.Presets;

namespace AZOTH.App.Tests;

[Collection("MainWindow")]
public class StreamlinedDonutAvailabilityTests
{
    [AvaloniaFact]
    public void Spreadsheet_suggestions_only_enable_for_sources_with_an_installed_route()
    {
        var sheets = new PresetAvailabilityTests.FakeConverter("test-sheets", ("xlsx", "csv"), ("csv", "xlsx"));
        using var f = new PresetAvailabilityTests.Fixture(sheets);
        var group = BuiltInData.RefinementPresets().Single();
        foreach (var id in group.SubPresetIds)
        {
            var preset = BuiltInData.CreatePresets().Single(p => p.Id == id);
            Assert.NotNull(f.Availability.Check(preset, [FormatRegistry.Get("png")]));
            var source = id == "to-csv" ? "xlsx" : "csv";
            Assert.Null(f.Availability.Check(preset, [FormatRegistry.Get(source)]));
            sheets.Available = false; f.Catalog.Refresh();
            Assert.NotNull(f.Availability.Check(preset, [FormatRegistry.Get(source)]));
            sheets.Available = true; f.Catalog.Refresh();
        }
    }

    [AvaloniaFact]
    public void Rar_extraction_uses_the_existing_route_and_no_starter_creates_rar()
    {
        var archives = new PresetAvailabilityTests.FakeConverter("test-archives", ("rar", FormatRegistry.Folder));
        using var f = new PresetAvailabilityTests.Fixture(archives);
        var extract = BuiltInData.CreatePresets().Single(p => p.Id == "extract");
        Assert.Null(f.Availability.Check(extract, [FormatRegistry.Get("rar")]));
        Assert.NotNull(f.Availability.Check(extract, [FormatRegistry.Get("png")]));
        archives.Available = false; f.Catalog.Refresh();
        Assert.NotNull(f.Availability.Check(extract, [FormatRegistry.Get("rar")]));
    }
}
