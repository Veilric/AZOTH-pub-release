using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using AZOTH.App.Services;
using AZOTH.App.ViewModels;
using AZOTH.Core.Donuts;
using AZOTH.Core.Localization;
using AZOTH.Core.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace AZOTH.App.Tests;

[Collection("MainWindow")]
public class DonutBuilderTests
{
    [AvaloniaFact]
    public void Toolbar_navigation_opens_the_builder_for_a_fresh_app()
    {
        TestServices.EnsureInitialized();
        using var model = new MainWindowViewModel(AppHost.Services, AppHost.Get<ILocalizer>(), AppHost.Get<UpdateService>());
        model.Navigate("toolbar");
        Assert.Equal("DonutBuilderPageViewModel", model.CurrentPage.GetType().Name);
    }

    [AvaloniaFact]
    public void Live_radial_uses_only_the_explicit_active_snapshot_for_every_extension()
    {
        TestServices.EnsureInitialized();
        var workspace = AppHost.Get<DonutWorkspace>();
        workspace.ResetWorkspace();
        workspace.AddAction("to-png");
        workspace.AddAction("to-pdf");
        workspace.UseDraft();
        var radial = AppHost.Get<RadialViewModel>();
        foreach (var file in new[] { "a.jpg", "a.pdf", "unknown.xyz" })
        {
            radial.Load([file], true);
            Assert.Equal(workspace.Active!.Profile.PresetIds, radial.Items.Select(i => i.Id));
        }
        workspace.ResetWorkspace();
        radial.Load(["a.jpg"], true);
        Assert.Empty(radial.Items);
    }

    [AvaloniaFact]
    public void Unavailable_back_actions_remain_visible_in_the_chosen_order()
    {
        TestServices.EnsureInitialized();
        var workspace = AppHost.Get<DonutWorkspace>(); workspace.ResetWorkspace();
        workspace.AddAction("to-png"); workspace.AddAction("to-webp", DonutFace.Back); workspace.UseDraft();
        var radial = AppHost.Get<RadialViewModel>(); radial.Load(["unknown.xyz"], true);
        Assert.All(radial.Items, item => Assert.False(item.IsEnabled));
        Assert.True(radial.ToggleFace());
        Assert.Equal(workspace.Active!.Profile.BackPresetIds, radial.Items.Select(item => item.Id));
        Assert.All(radial.Items, item => Assert.False(item.IsEnabled));
        workspace.ResetWorkspace();
    }

    [AvaloniaFact]
    public void Unconfigured_drag_and_known_file_entry_points_open_builder_without_a_radial()
    {
        TestServices.EnsureInitialized();
        AppHost.Get<DonutWorkspace>().ResetWorkspace();
        var radial = AppHost.Get<RadialController>();
        var windows = AppHost.Get<WindowService>();
        try
        {
            radial.ShowForDrag(300, 300);
            Assert.False(radial.IsVisible);
            Assert.Equal("toolbar", ((MainWindowViewModel)windows.Main!.DataContext!).SelectedItem.Key);
            radial.ShowForFiles(300, 300, ["a.png"]);
            Assert.False(radial.IsVisible);
            Assert.Equal("DonutBuilderPageViewModel", ((MainWindowViewModel)windows.Main.DataContext!).CurrentPage.GetType().Name);
        }
        finally { windows.Main?.Close(); }
    }
}

[Collection("MainWindow")]
public class DonutBuilderBehaviorTests
{
    private sealed class Fixture : IDisposable
    {
        private readonly string _directory = Directory.CreateTempSubdirectory("azoth-builder-").FullName;
        public UserDataStore Store { get; }
        public DonutWorkspace Workspace { get; }
        public AZOTH.App.ViewModels.Pages.DonutBuilderPageViewModel Model { get; }
        public Fixture()
        {
            TestServices.EnsureInitialized();
            Store = new UserDataStore(_directory); Store.Load();
            Workspace = new DonutWorkspace(Store);
            Model = new(Workspace, Store, AppHost.Get<ILocalizer>(), AppHost.Get<WindowService>());
        }
        public void Dispose() { Model.Dispose(); Workspace.Dispose(); Directory.Delete(_directory, true); }
    }

    [AvaloniaFact]
    public void Advanced_formats_are_optional_and_custom_presets_remain_visible()
    {
        using var f = new Fixture();
        Assert.Equal(6, f.Model.Templates.Count);
        Assert.DoesNotContain(f.Model.Catalog, p => p.Id == "to-tiff");
        Assert.Contains(f.Model.Catalog, p => p.Id == "to-xlsx");
        f.Store.Presets.Add(new() { Id = "my-tiff", Name = "Print TIFF", TargetFormat = "tiff" });
        f.Store.SaveLibrary();
        Assert.Contains(f.Model.Catalog, p => p.Id == "my-tiff");
        f.Model.ShowMoreFormats = true;
        Assert.Contains(f.Model.Catalog, p => p.Id == "to-tiff");
        Assert.Contains(f.Model.Templates, p => p.Value == "geo");
        f.Model.SelectedTemplate = f.Model.Templates.First(p => p.Value == "geo");
        f.Model.LoadTemplateCommand.Execute(null);
        Assert.NotEmpty(f.Model.Actions);
        f.Workspace.ResetDraft();
        f.Model.AddAction("to-tiff");
        f.Model.ShowMoreFormats = false;
        Assert.Single(f.Model.Actions);
        Assert.Equal("tiff", f.Workspace.Draft.Presets.Single().TargetFormat);
    }

    [AvaloniaFact]
    public void Fresh_builder_is_empty_and_selection_or_saving_never_activates()
    {
        using var f = new Fixture(); var vm = f.Model;
        Assert.Empty(vm.Actions); Assert.Empty(vm.Saved); Assert.False(vm.CanUse);
        Assert.Contains("No active", vm.ActiveStatus);
        vm.SelectedCatalog = vm.Catalog.First(p => p.Id == "to-png");
        Assert.False(f.Workspace.IsConfigured); Assert.Empty(vm.Actions);
        vm.AddCommand.Execute(null);
        Assert.Single(vm.Actions); Assert.True(vm.CanUse); Assert.False(f.Workspace.IsConfigured);
        vm.Name = "Pictures"; vm.SaveAsCommand.Execute(null);
        Assert.Single(vm.Saved); Assert.False(f.Workspace.IsConfigured);
        vm.SelectedSaved = vm.Saved[0];
        Assert.False(f.Workspace.IsConfigured);
        vm.UseCommand.Execute(null); Assert.True(f.Workspace.IsConfigured);
    }

    [AvaloniaFact]
    public void Saved_donuts_and_active_jobs_keep_independent_settings_across_edit_switch_delete_and_restart()
    {
        using var f = new Fixture(); var vm = f.Model;
        vm.AddAction("jpg-70"); var firstAction = vm.Actions.Single().Id;
        vm.Name = "Small JPEG"; vm.SaveAsCommand.Execute(null); var first = vm.Saved.Single().Value;
        vm.UseCommand.Execute(null);
        var running = f.Workspace.Active!;
        f.Workspace.ConfigureAction(firstAction, p => p.Image.Quality = 93);
        vm.Name = "Large JPEG"; vm.SaveAsCommand.Execute(null); var second = vm.SelectedSaved!.Value;
        Assert.NotEqual(first, second);
        Assert.Equal(70, running.Presets.Single().Image.Quality);
        Assert.Equal(70, f.Workspace.Saved.Single(s => s.Id == first).Snapshot.Presets.Single().Image.Quality);
        vm.ActivateCommand.Execute(null);
        Assert.Equal(93, f.Workspace.Active!.Presets.Single().Image.Quality);
        vm.Name = "Copy"; vm.DuplicateCommand.Execute(null);
        Assert.Equal(3, vm.Saved.Count);
        vm.SelectedSaved = vm.Saved.First(s => s.Value == first); vm.LoadSavedCommand.Execute(null);
        Assert.Equal(70, f.Workspace.Draft.Presets.Single().Image.Quality);
        vm.Name = "Renamed"; vm.RenameCommand.Execute(null);
        vm.DeleteCommand.Execute(null);
        Assert.Equal(second, f.Workspace.Active!.SavedPresetId);
        var store = new UserDataStore(f.Store.Directory); store.Load();
        using var restarted = new DonutWorkspace(store);
        Assert.Equal(93, restarted.Active!.Presets.Single().Image.Quality);
        Assert.Equal(2, restarted.Saved.Count);
        Assert.Equal(70, restarted.Draft.Presets.Single().Image.Quality);
        vm.SelectedSaved = vm.Saved.First(s => s.Value == second); vm.DeleteCommand.Execute(null);
        Assert.True(f.Workspace.IsConfigured); Assert.Null(f.Workspace.Active!.SavedPresetId);
    }

    [AvaloniaFact]
    public void Faces_reorder_remove_discard_templates_and_invalid_names_preserve_explicit_activation()
    {
        using var f = new Fixture(); var vm = f.Model;
        vm.AddAction("to-png"); vm.AddAction("to-pdf");
        vm.SelectedAction = vm.Actions[1]; vm.MoveUpCommand.Execute(null);
        Assert.Equal("pdf", f.Workspace.Draft.Presets.First(p => p.Id == vm.Actions[0].Id).TargetFormat);
        vm.EditingBack = true; vm.AddAction("to-webp"); Assert.Single(vm.Actions);
        vm.UseCommand.Execute(null); var activeId = f.Workspace.Active!.Profile.Id;
        vm.Name = "One"; vm.SaveAsCommand.Execute(null);
        vm.Name = "one"; vm.SaveAsCommand.Execute(null); Assert.NotNull(vm.Message); Assert.Single(vm.Saved);
        vm.Name = " "; vm.SaveAsCommand.Execute(null); Assert.NotNull(vm.Message);
        vm.SelectedAction = vm.Actions[0]; vm.RemoveCommand.Execute(null); Assert.Empty(vm.Actions);
        vm.DiscardCommand.Execute(null); Assert.Single(vm.Actions);
        vm.ResetCommand.Execute(null); Assert.Empty(vm.Actions);
        Assert.Equal(activeId, f.Workspace.Active!.Profile.Id);
        vm.SelectedTemplate = vm.Templates.First(t => t.Value == "images"); vm.LoadTemplateCommand.Execute(null);
        Assert.NotEmpty(f.Workspace.Draft.Profile.PresetIds);
        Assert.Equal(activeId, f.Workspace.Active!.Profile.Id);
        vm.EditingBack = false;
        while (vm.Actions.Count < 12) vm.AddAction("to-png");
        vm.AddAction("to-png"); Assert.Equal(12, vm.Actions.Count); Assert.NotNull(vm.Message);
    }

    [AvaloniaFact]
    public void Confirmed_general_library_reset_clears_every_donut_choice_and_stays_empty_after_restart()
    {
        using var f = new Fixture();
        f.Model.AddAction("to-png"); f.Model.Name = "Pictures";
        f.Model.SaveAsCommand.Execute(null); f.Model.UseCommand.Execute(null);
        using var services = new ServiceCollection().AddSingleton(f.Workspace).BuildServiceProvider();
        using var settings = ActivatorUtilities.CreateInstance<AZOTH.App.ViewModels.Pages.GeneralPageViewModel>(services, f.Store, AppHost.Get<ILocalizer>(),
            AppHost.Get<AZOTH.Core.Platform.IPlatformServices>(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<AZOTH.App.ViewModels.Pages.GeneralPageViewModel>.Instance);
        settings.ResetLibrary();
        Assert.False(f.Workspace.IsConfigured); Assert.Empty(f.Workspace.Saved); Assert.Empty(f.Workspace.Draft.Presets);
        var store = new UserDataStore(f.Store.Directory); store.Load();
        using var restarted = new DonutWorkspace(store);
        Assert.False(restarted.IsConfigured); Assert.Empty(restarted.Saved); Assert.Empty(restarted.Draft.Presets);
        Assert.NotEmpty(store.Presets); Assert.Empty(store.Profiles);
    }

    [AvaloniaFact]
    public void Catalog_search_and_categories_exclude_execution_copies()
    {
        using var f = new Fixture(); var vm = f.Model;
        vm.AddAction("to-png"); vm.UseCommand.Execute(null);
        Assert.DoesNotContain(vm.Catalog, item => item.Id == f.Workspace.Active!.Profile.PresetIds[0]);
        vm.Search = "PNG"; Assert.Contains(vm.Catalog, item => item.Id == "to-png");
        Assert.All(vm.Catalog, item => Assert.True(item.Label.Contains("PNG", StringComparison.OrdinalIgnoreCase)
            || item.Caption?.Contains("PNG", StringComparison.OrdinalIgnoreCase) == true));
        vm.Search = ""; vm.Category = vm.Categories.First(c => c.Value == AZOTH.App.ViewModels.Pages.PaletteTab.Archive);
        Assert.Contains(vm.Catalog, item => item.Id == "zip-all");
        Assert.DoesNotContain(vm.Catalog, item => item.Id == "to-png");
    }

    [AvaloniaFact]
    public void An_open_radial_keeps_group_children_and_deferred_back_recipes_after_switch_and_library_reset()
    {
        using var f = new Fixture();
        var group = f.Store.Presets.First(p => p.IsGroup && p.SubPresetIds.Count > 0);
        f.Workspace.AddAction(group.Id); f.Workspace.AddAction("to-webp", DonutFace.Back); f.Workspace.UseDraft();
        var radial = new RadialViewModel(f.Store, AppHost.Get<ILocalizer>(), AppHost.Get<PresetAvailability>(), f.Workspace);
        radial.Load(["a.jpg"], true);
        var child = radial.PresetShownAt(0, 0)!;
        var childId = child.Id; var childTarget = child.TargetFormat;
        f.Workspace.ResetDraft(); f.Workspace.AddAction("to-pdf"); f.Workspace.UseDraft();
        f.Store.Presets.Clear();
        Assert.Equal(childId, radial.PresetShownAt(0, 0)!.Id);
        Assert.Equal(childTarget, radial.PresetShownAt(0, 0)!.TargetFormat);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        radial.ToggleFace(); Assert.Equal("webp", radial.Presets.Single().TargetFormat);
        Assert.Equal(childId, f.Store.FindPreset(childId)!.Id);
    }

    [AvaloniaFact]
    public void Update_saved_is_independent_from_active_and_discard_restores_updated_definition()
    {
        using var f = new Fixture(); var vm = f.Model;
        vm.AddAction("jpg-70"); vm.Name = "Pictures"; vm.SaveAsCommand.Execute(null); vm.UseCommand.Execute(null);
        var action = f.Workspace.Draft.Presets.Single();
        var editor = new PresetEditorViewModel(action, AppHost.Get<ILocalizer>()) { Quality = 84 };
        Assert.True(editor.Apply());
        f.Workspace.ConfigureAction(action.Id, p => AZOTH.App.ViewModels.Pages.DonutBuilderPageViewModel.CopyRecipe(action, p));
        vm.UpdateSavedCommand.Execute(null);
        Assert.Contains("saved version changed", vm.ActiveStatus);
        Assert.Equal(70, f.Workspace.Active!.Presets.Single().Image.Quality);
        vm.ResetCommand.Execute(null); vm.SelectedSaved = vm.Saved[0]; vm.LoadSavedCommand.Execute(null);
        Assert.Equal(84, f.Workspace.Draft.Presets.Single().Image.Quality);
    }
}

[Collection("MainWindow")]
public class DonutBuilderRenderTests
{
    [AvaloniaTheory]
    [InlineData(ThemeMode.Light, false)]
    [InlineData(ThemeMode.Dark, false)]
    [InlineData(ThemeMode.Light, true)]
    [InlineData(ThemeMode.Dark, true)]
    public void Real_builder_renders_empty_and_populated_and_supports_keyboard_editing(ThemeMode mode, bool populated)
    {
        TestServices.EnsureInitialized();
        var workspace = AppHost.Get<DonutWorkspace>(); workspace.ResetWorkspace();
        var themes = AppHost.Get<ThemeService>(); themes.Apply(new ThemeSettings { Mode = mode, ReduceMotion = true }, "en");
        using var model = new MainWindowViewModel(AppHost.Services, AppHost.Get<ILocalizer>(), AppHost.Get<UpdateService>());
        var builder = Assert.IsType<AZOTH.App.ViewModels.Pages.DonutBuilderPageViewModel>(model.CurrentPage);
        if (populated)
        {
            builder.AddAction("to-png"); builder.AddAction("jpg-70"); builder.AddAction("rotate");
            builder.EditingBack = true; builder.AddAction("to-pdf"); builder.EditingBack = false;
            builder.Name = "Daily conversions"; builder.SaveAsCommand.Execute(null); builder.UseCommand.Execute(null);
            builder.AddAction("to-webp");
            Assert.Contains("Unsaved draft", builder.DraftStatus);
        }
        var window = new AZOTH.App.Views.MainWindow { DataContext = model, Width = 1120, Height = 1100 };
        try
        {
            window.Show();
            for (var i = 0; i < 10; i++) { Avalonia.Threading.Dispatcher.UIThread.RunJobs(); Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
            var page = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window).OfType<AZOTH.App.Views.Pages.DonutBuilderPage>().Single();
            Assert.NotNull(page.FindControl<Avalonia.Controls.ListBox>("CatalogList"));
            Assert.NotNull(page.FindControl<AZOTH.App.Controls.DonutMenu>("Preview"));
            Assert.DoesNotContain(Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window).OfType<Avalonia.Controls.TextBlock>(), t => t.Text?.StartsWith("View not found") == true);
            RenderTests.Save(window, $"azoth-builder-{(populated ? "populated" : "empty")}-{mode.ToString().ToLowerInvariant()}.png");
            if (populated)
            {
                builder.UpdateSavedCommand.Execute(null);
                Assert.Contains("saved version changed", builder.ActiveStatus);
                RenderTests.Pump();
                RenderTests.Save(window, $"azoth-builder-active-changed-{mode.ToString().ToLowerInvariant()}.png");
            }
            var catalog = page.FindControl<Avalonia.Controls.ListBox>("CatalogList")!;
            builder.SelectedCatalog = builder.Catalog.First(a => a.Id == "to-png");
            catalog.ScrollIntoView(builder.SelectedCatalog); RenderTests.Pump();
            Assert.True(catalog.ContainerFromIndex(catalog.SelectedIndex)!.Focus());
            var before = builder.Actions.Count;
            Avalonia.Headless.HeadlessWindowExtensions.KeyPress(window, Avalonia.Input.Key.Enter, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.Enter, null);
            Assert.Equal(before + 1, builder.Actions.Count);
            var actions = page.FindControl<Avalonia.Controls.ListBox>("ActionList")!;
            RenderTests.Pump(); Assert.True(actions.ContainerFromIndex(actions.SelectedIndex)!.Focus());
            Avalonia.Headless.HeadlessWindowExtensions.KeyPress(window, Avalonia.Input.Key.Delete, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.Delete, null);
            Assert.Equal(before, builder.Actions.Count);
            if (!populated && mode == ThemeMode.Light)
            {
                window.Width = 820; window.Height = 760; RenderTests.Pump();
                RenderTests.Save(window, "azoth-builder-minimum-light.png");
            }
        }
        finally { window.Close(); themes.Apply(new ThemeSettings(), "en"); workspace.ResetWorkspace(); }
    }
}
