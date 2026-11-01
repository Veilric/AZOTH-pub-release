using Avalonia.Headless.XUnit;
using AZOTH.App.Services;
using AZOTH.Core.Donuts;
using AZOTH.Core.Formats;
using AZOTH.Core.Settings;

namespace AZOTH.App.Tests;

public class DonutToolSnapshotTests
{
    [AvaloniaTheory]
    [InlineData("tool-pdf-crop", "pdf")]
    [InlineData("tool-pdf-pages", "pdf")]
    [InlineData("tool-pdf-add", "pdf")]
    [InlineData("tool-sheet-studio", "xlsx")]
    public void Tool_availability_survives_snapshot_ID_remapping_and_restart(string id, string format)
    {
        TestServices.EnsureInitialized();
        var directory = Directory.CreateTempSubdirectory("azoth-snapshot-tool-").FullName;
        try
        {
            var store = new UserDataStore(directory); store.Load();
            using var workspace = new DonutWorkspace(store);
            workspace.AddAction(id); workspace.UseDraft();
            var original = store.Presets.Single(p => p.Id == id);
            var availability = AppHost.Get<PresetAvailability>();
            var expected = availability.Check(original, [FormatRegistry.Get(format)]);
            var snapshot = workspace.Active!.Presets.Single();
            Assert.NotEqual(original.Id, snapshot.Id);
            Assert.Equal(expected, availability.Check(snapshot, [FormatRegistry.Get(format)]));
            var reloadedStore = new UserDataStore(directory); reloadedStore.Load();
            using var reloaded = new DonutWorkspace(reloadedStore);
            Assert.Equal(expected, availability.Check(reloaded.Active!.Presets.Single(), [FormatRegistry.Get(format)]));
        }
        finally { Directory.Delete(directory, true); }
    }

    [AvaloniaFact]
    public void A_snapshot_PDF_pages_tool_opens_the_page_editor()
    {
        TestServices.EnsureInitialized();
        var directory = Directory.CreateTempSubdirectory("azoth-snapshot-dispatch-").FullName;
        var windows = AppHost.Get<WindowService>();
        try
        {
            var file = Path.Combine(directory, "sample.pdf");
            using (var document = new PdfSharp.Pdf.PdfDocument()) { document.AddPage(); document.Save(file); }
            var store = new UserDataStore(directory); store.Load();
            using var workspace = new DonutWorkspace(store);
            workspace.AddAction("tool-pdf-pages"); workspace.UseDraft();
            windows.ShowTool([file], workspace.Active!.Presets.Single());
            Assert.NotNull(windows.PdfPageEditor);
        }
        finally
        {
            windows.PdfPageEditor?.Close();
            if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
                foreach (var window in desktop.Windows.OfType<AZOTH.App.Views.CropOverlayWindow>().ToArray()) window.Close();
            Directory.Delete(directory, true);
        }
    }
}
