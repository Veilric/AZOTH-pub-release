using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using AZOTH.App.Services;
using AZOTH.App.ViewModels;
using AZOTH.App.ViewModels.Pages;
using AZOTH.Core.Files;
using AZOTH.Core.Localization;
using AZOTH.Core.Ordo;
using AZOTH.Core.Platform;
using AZOTH.Core.Settings;
using AZOTH.Platform.Linux;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
namespace AZOTH.App.Tests;

[Collection("MainWindow")]
public sealed class OrdoWorkspaceTests
{
    private sealed class Fixture : IAsyncDisposable
    {
        public string Root { get; } = NativeFileTestPaths.CreateTempDirectory("azoth-ordo-ui-");
        public string Source => Path.Combine(Root, "inbox");
        public OrdoWorkspace Workspace { get; private set; } = null!;
        public OrdoMonitor Monitor { get; private set; } = null!;
        public OrdoJournal Journal { get; private set; } = null!;
        private readonly IOrdoFileMover? _mover;
        public Fixture(IOrdoFileMover? mover = null)
        {
            _mover = mover;
            TestServices.EnsureInitialized();
            Directory.CreateDirectory(Source);
            Initialize();
        }
        private void Initialize()
        {
            var store = new UserDataStore(Path.Combine(Root, "state")); store.Load();
            IFileMetadata metadata = OperatingSystem.IsLinux() ? new LinuxFileMetadata()
                : OperatingSystem.IsWindows() ? new AZOTH.Platform.Windows.WindowsFileMetadata()
                : OperatingSystem.IsMacOS() ? new AZOTH.Platform.MacOS.MacOSFileMetadata() : new UnsupportedFileMetadata();
            var fingerprints = new FileFingerprintService(metadata);
            var planner = new OrdoPlanner(metadata, fingerprints, [store.Directory]);
            Journal = new(Path.Combine(store.Directory, "history"));
            var service = new OrdoService(metadata, fingerprints, new(), Journal, _mover);
            Monitor = new(metadata, planner, service, Journal, Path.Combine(store.Directory, "monitor.json"), new(), TimeSpan.Zero);
            var integration = new SystemIntegrationService(new NullPlatformServices(), AppHost.Get<ILocalizer>(), NullLogger<SystemIntegrationService>.Instance);
            Workspace = new(planner, service, Journal, Monitor, store, integration, AppHost.Get<ILocalizer>());
            Workspace.SourceRoot = Source;
        }
        public async Task ReloadAsync() { await Workspace.DisposeAsync(); Initialize(); }
        public async ValueTask DisposeAsync()
        {
            await Workspace.DisposeAsync();
            Directory.Delete(Root, true);
        }
    }

    private sealed class WorkspaceServices(OrdoWorkspace workspace) : IServiceProvider, IServiceProviderIsService
    {
        public object? GetService(Type type) => type == typeof(OrdoWorkspace) ? workspace
            : type == typeof(IServiceProviderIsService) ? this : AppHost.Services.GetService(type);
        public bool IsService(Type type) => type == typeof(OrdoWorkspace)
            || AppHost.Get<IServiceProviderIsService>().IsService(type);
    }

    private sealed class ControlledMover(TaskCompletionSource entered, ManualResetEventSlim release) : IOrdoFileMover
    {
        public void Move(string source, string destination)
        {
            entered.TrySetResult();
            if (!release.Wait(TimeSpan.FromSeconds(10))) throw new IOException("Test did not release the fixture move.");
            File.Move(source, destination, false);
        }
    }

    [AvaloniaFact]
    public void Navigation_reuses_app_lifetime_workspace_without_organizing_on_visit()
    {
        TestServices.EnsureInitialized();
        using var window = new MainWindowViewModel(AppHost.Services, AppHost.Get<ILocalizer>(), AppHost.Get<UpdateService>());
        window.Navigate("ordo"); var first = Assert.IsType<OrdoPageViewModel>(window.CurrentPage);
        Assert.False(first.Workspace.IsBusy); Assert.False(first.Workspace.CanApply);
        window.Navigate("home"); window.Navigate("ordo");
        Assert.Same(first.Workspace, Assert.IsType<OrdoPageViewModel>(window.CurrentPage).Workspace);
    }

    [AvaloniaFact]
    public void Organizer_controls_have_accessible_names()
    {
        new AutomationNameTests().Every_icon_button_and_field_on_a_page_has_a_name("ordo");
    }

    [AvaloniaFact]
    public async Task Ordered_rule_controls_drive_preview_and_require_explicit_apply()
    {
        await using var f = new Fixture();
        var source = Path.Combine(f.Source, "report.pdf"); File.WriteAllText(source, "report");
        Assert.Equal(5, f.Workspace.Rules.Count);
        var documents = Assert.Single(f.Workspace.Rules, r => r.Type == OrdoFileType.Document);
        documents.Destination = "My documents";
        await f.Workspace.PreviewAsync();
        Assert.True(File.Exists(source));
        Assert.True(f.Workspace.CanApply);
        Assert.Contains(f.Workspace.PreviewRows, r => r.DestinationPath.Contains(Path.Combine("My documents", "report.pdf"), StringComparison.Ordinal));
        await f.Workspace.ApplyAsync();
        Assert.False(File.Exists(source));
        Assert.Equal("report", File.ReadAllText(Path.Combine(f.Source, "My documents", "report.pdf")));
        var operation = Assert.Single(f.Journal.List());
        await f.Workspace.UndoAsync(operation.Id);
        Assert.True(File.Exists(source));
        Assert.Equal(OrdoEntryState.Undone, f.Journal.Load(operation.Id).Entries[0].State);
    }

    [AvaloniaFact]
    public async Task Structured_preview_maps_exact_destinations_rules_sizes_and_issues_without_moving()
    {
        await using var f = new Fixture();
        var source = Path.Combine(f.Source, "report.pdf"); File.WriteAllText(source, "report");
        File.WriteAllText(Path.Combine(f.Source, "stay.unknown"), "stay");
        File.WriteAllText(Path.Combine(f.Source, "partial.tmp"), "partial");
        var destination = Directory.CreateDirectory(Path.Combine(f.Source, "Documents")).FullName;
        File.WriteAllText(Path.Combine(destination, "report.pdf"), "existing");
        await f.Workspace.PreviewAsync();
        var preview = Assert.IsType<OrdoPreview>(f.Workspace.PreviewDisplay);
        var row = Assert.Single(preview.Moves);
        Assert.Equal("report.pdf", row.FileName);
        Assert.Equal(source, row.SourcePath);
        Assert.Equal(Path.Combine(destination, "report (1).pdf"), row.DestinationPath);
        Assert.Equal(6, row.SizeBytes);
        Assert.Equal(f.Workspace.Rules.Single(r => r.Type == OrdoFileType.Document).Id, row.RuleId);
        Assert.True(row.StartsGroup);
        Assert.Equal(6, preview.TotalBytes);
        Assert.Equal(2, preview.StayingCount);
        Assert.Equal(2, preview.Issues.Count);
        Assert.All(preview.Issues, issue => Assert.False(string.IsNullOrWhiteSpace(issue.Explanation)));
        Assert.All(preview.Issues, issue => Assert.False(issue.NeedsAttention));
        Assert.True(File.Exists(source)); Assert.Empty(f.Journal.List());
        // Display data cannot authorize or redirect an operation.
        Assert.Throws<NotSupportedException>(() => ((IList<OrdoPreviewMove>)preview.Moves).Clear());
        await f.Workspace.ApplyAsync();
        Assert.Equal("report", File.ReadAllText(row.DestinationPath));
        Assert.Equal("existing", File.ReadAllText(Path.Combine(destination, "report.pdf")));
        Assert.Null(f.Workspace.PreviewDisplay);
    }

    [AvaloniaFact]
    public async Task Empty_and_issue_only_previews_have_explicit_states_and_cannot_apply()
    {
        await using var f = new Fixture();
        await f.Workspace.PreviewAsync();
        Assert.True(f.Workspace.HasPreview);
        Assert.True(f.Workspace.PreviewDisplay!.IsEmpty);
        Assert.False(f.Workspace.CanApply);
        File.WriteAllText(Path.Combine(f.Source, "stay.unknown"), "stay");
        await f.Workspace.PreviewAsync();
        Assert.False(f.Workspace.PreviewDisplay!.IsEmpty);
        Assert.False(f.Workspace.PreviewDisplay.HasMoves);
        Assert.Single(f.Workspace.PreviewDisplay.Issues);
        Assert.False(f.Workspace.CanApply);
        f.Workspace.Rules[0].Destination = "Changed";
        Assert.Null(f.Workspace.PreviewDisplay);
        Assert.Empty(f.Workspace.PreviewRows);
        Assert.False(f.Workspace.ApplyCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public async Task Preview_display_groups_moves_but_keeps_each_planners_exact_path()
    {
        await using var f = new Fixture();
        Directory.CreateDirectory(Path.Combine(f.Source, "nested"));
        File.WriteAllText(Path.Combine(f.Source, "a.pdf"), "one");
        File.WriteAllText(Path.Combine(f.Source, "b.png"), "two");
        File.WriteAllText(Path.Combine(f.Source, "nested", "c.pdf"), "three");
        var config = f.Workspace.ReadConfiguration();
        IFileMetadata metadata = OperatingSystem.IsLinux() ? new LinuxFileMetadata()
            : OperatingSystem.IsWindows() ? new AZOTH.Platform.Windows.WindowsFileMetadata()
            : OperatingSystem.IsMacOS() ? new AZOTH.Platform.MacOS.MacOSFileMetadata() : new UnsupportedFileMetadata();
        var plan = await new OrdoPlanner(metadata, new(metadata)).PreviewAsync(config);
        await f.Workspace.PreviewAsync();
        var display = f.Workspace.PreviewDisplay!;
        Assert.Equal(2, display.Moves.Count(r => r.StartsGroup));
        Assert.Equal(plan.Moves.Select(m => (m.Source.Snapshot.Path, m.Destination, m.RuleId, m.Source.Snapshot.Length)).Order(),
            display.Moves.Select(m => (m.SourcePath, m.DestinationPath, m.RuleId, m.SizeBytes)).Order());
        Assert.Empty(f.Journal.List());
    }

    private sealed class RefusingMetadata(IFileMetadata inner) : IFileMetadata
    {
        public FileMetadataResult Read(string path) => Path.GetFileName(path) == "blocked.pdf"
            ? new(null, new(FileRefusalKind.AccessDenied, "Fixture access refusal.")) : inner.Read(path);
    }

    [AvaloniaFact]
    public async Task Warning_mapping_preserves_the_planners_explanation_without_parsing_it()
    {
        await using var f = new Fixture();
        File.WriteAllText(Path.Combine(f.Source, "blocked.pdf"), "blocked");
        IFileMetadata native = OperatingSystem.IsLinux() ? new LinuxFileMetadata()
            : OperatingSystem.IsWindows() ? new AZOTH.Platform.Windows.WindowsFileMetadata()
            : OperatingSystem.IsMacOS() ? new AZOTH.Platform.MacOS.MacOSFileMetadata() : new UnsupportedFileMetadata();
        var metadata = new RefusingMetadata(native);
        var config = f.Workspace.ReadConfiguration();
        var plan = await new OrdoPlanner(metadata, new(metadata)).PreviewAsync(config);
        var display = OrdoPreview.FromPlan(plan, config, AppHost.Get<ILocalizer>());
        var warning = Assert.Single(display.Issues);
        Assert.Equal(Assert.Single(plan.Issues).Detail, warning.Explanation);
        Assert.Equal(plan.Issues[0].Path, warning.Path);
        Assert.True(warning.NeedsAttention);
        Assert.False(display.HasMoves);
        Assert.True(File.Exists(warning.Path));
    }

    [AvaloniaFact]
    public async Task Large_preview_uses_bounded_virtualized_rows_and_scrolls_to_the_last_file()
    {
        await using var f = new Fixture();
        for (var i = 0; i < 250; i++) File.WriteAllText(Path.Combine(f.Source, $"report-{i:D3}.pdf"), "report");
        await f.Workspace.PreviewAsync();
        using var main = new MainWindowViewModel(new WorkspaceServices(f.Workspace), AppHost.Get<ILocalizer>(), AppHost.Get<UpdateService>());
        main.Navigate("ordo");
        // This test measures the ready page's layout, independently of the process-once startup stage.
        var previousMotion = Motion.Enabled;
        Motion.Set(false);
        var window = new AZOTH.App.Views.MainWindow { DataContext = main, Width = 820, Height = 760 };
        try
        {
            window.Show(); RenderTests.Pump();
            var list = window.GetVisualDescendants().OfType<ListBox>().Single(l => l.Name == "PlannedMovesList");
            list.BringIntoView(); RenderTests.Pump();
            Assert.IsType<VirtualizingStackPanel>(list.ItemsPanelRoot);
            Assert.InRange(list.GetVisualDescendants().OfType<ListBoxItem>().Count(), 1, 30);
            list.ScrollIntoView(f.Workspace.PreviewRows[^1]); RenderTests.Pump();
            Assert.Contains(list.GetVisualDescendants().OfType<ListBoxItem>(), item => ReferenceEquals(item.DataContext, f.Workspace.PreviewRows[^1]));
            Assert.True(f.Workspace.CanApply); Assert.Empty(f.Journal.List());
            RenderTests.Save(window, "azoth-ordo-large-minimum.png");
        }
        finally { window.Close(); Motion.Set(previousMotion); }
    }

    [AvaloniaFact]
    public async Task Editing_rule_controls_invalidates_preview_and_monitoring_approval()
    {
        await using var f = new Fixture();
        Assert.NotEmpty(f.Workspace.Rules);
        File.WriteAllText(Path.Combine(f.Source, "image.png"), "image");
        await f.Workspace.PreviewAsync();
        f.Workspace.ReviewMonitoring = true;
        await f.Workspace.EnrollAsync();
        Assert.True(f.Monitor.Enabled);
        await f.Workspace.PreviewAsync();
        f.Workspace.Rules[0].Destination = "Changed pictures";
        Assert.False(f.Monitor.Enabled);
        Assert.False(f.Workspace.CanApply);
        Assert.False(f.Workspace.ReviewMonitoring);
    }

    [AvaloniaFact]
    public async Task Apply_without_preview_never_moves_files_and_undo_pauses_monitoring()
    {
        await using var f = new Fixture();
        var source = Path.Combine(f.Source, "picture.png"); File.WriteAllText(source, "image");
        await f.Workspace.ApplyAsync();
        Assert.True(File.Exists(source)); Assert.Empty(f.Journal.List());
        await f.Workspace.PreviewAsync(); await f.Workspace.ApplyAsync();
        await f.Workspace.PreviewAsync(); f.Workspace.ReviewMonitoring = true; await f.Workspace.EnrollAsync();
        Assert.True(f.Monitor.Enabled);
        await f.Workspace.UndoAsync(Assert.Single(f.Journal.List()).Id);
        Assert.False(f.Monitor.Enabled);
        await f.Monitor.PollAsync(); await f.Monitor.PollAsync();
        Assert.True(File.Exists(source));
        Assert.False(f.Workspace.StartupAvailable);
        f.Workspace.StartWithSystem = true;
        Assert.False(f.Workspace.StartWithSystem);
    }

    [AvaloniaFact]
    public async Task Rule_order_custom_targets_and_dates_survive_workspace_recreation()
    {
        await using var f = new Fixture();
        var source = Path.Combine(f.Source, "report.pdf"); File.WriteAllText(source, "report");
        File.SetLastWriteTimeUtc(source, new(2025, 3, 15, 0, 0, 0, DateTimeKind.Utc));
        var target = Path.Combine(f.Root, "custom-target");
        var rule = new OrdoRuleEditor
        {
            Type = OrdoFileType.Document,
            Extension = "pdf",
            Destination = target,
            ModifiedFrom = "2025-01-01",
            ModifiedBefore = "2026-01-01",
            GroupByMonth = true
        };
        f.Workspace.Rules.Add(rule);
        while (f.Workspace.Rules.IndexOf(rule) > 0) rule.MoveUpCommand!.Execute(null);
        var ids = f.Workspace.Rules.Select(r => r.Id).ToArray();
        await f.ReloadAsync();
        Assert.Equal(ids, f.Workspace.Rules.Select(r => r.Id));
        await f.Workspace.PreviewAsync();
        Assert.True(f.Workspace.CanApply);
        Assert.Contains(f.Workspace.PreviewRows, p => p.DestinationPath.Contains(Path.Combine(target, "2025-03", "report.pdf"), StringComparison.Ordinal));
        f.Workspace.Rules[0].ModifiedBefore = "invalid date";
        await f.Workspace.PreviewAsync();
        Assert.False(f.Workspace.CanApply);
        Assert.True(File.Exists(source));
    }

    [AvaloniaFact]
    public async Task Navigation_and_window_recreation_retain_in_flight_results_and_cancellation()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        await using var f = new Fixture(new ControlledMover(entered, release));
        File.WriteAllText(Path.Combine(f.Source, "a.png"), "one");
        File.WriteAllText(Path.Combine(f.Source, "b.png"), "two");
        await f.Workspace.PreviewAsync();
        var applying = f.Workspace.ApplyAsync();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            using (var first = new MainWindowViewModel(new WorkspaceServices(f.Workspace), AppHost.Get<ILocalizer>(), AppHost.Get<UpdateService>()))
            {
                first.Navigate("ordo"); Assert.True(Assert.IsType<OrdoPageViewModel>(first.CurrentPage).Workspace.IsBusy);
                first.Navigate("home");
            }
            using var reopened = new MainWindowViewModel(new WorkspaceServices(f.Workspace), AppHost.Get<ILocalizer>(), AppHost.Get<UpdateService>());
            reopened.Navigate("ordo");
            var returned = Assert.IsType<OrdoPageViewModel>(reopened.CurrentPage).Workspace;
            returned.Cancel(); release.Set(); await applying;
            Assert.Same(f.Workspace, returned);
            var operation = Assert.Single(f.Journal.List());
            Assert.True(operation.Cancelled);
            Assert.Single(operation.Entries, e => e.State == OrdoEntryState.Completed);
            Assert.True(File.Exists(Path.Combine(f.Source, "b.png")));
            Assert.Single(returned.History);
        }
        finally { release.Set(); await applying; }
    }

    [AvaloniaFact]
    public async Task Shutdown_waits_for_the_current_move_and_persists_its_actual_result()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        await using var f = new Fixture(new ControlledMover(entered, release));
        File.WriteAllText(Path.Combine(f.Source, "a.png"), "one");
        File.WriteAllText(Path.Combine(f.Source, "b.png"), "two");
        await f.Workspace.PreviewAsync(); var applying = f.Workspace.ApplyAsync();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var stopping = f.Workspace.DisposeAsync().AsTask();
            Assert.False(stopping.IsCompleted);
            release.Set(); await stopping; await applying;
            var operation = Assert.Single(f.Journal.List());
            Assert.True(operation.Cancelled);
            Assert.Single(operation.Entries, e => e.State == OrdoEntryState.Completed);
            Assert.True(File.Exists(Path.Combine(f.Source, "b.png")));
        }
        finally { release.Set(); await applying; }
    }

    [AvaloniaFact]
    public async Task Malformed_editor_data_is_preserved_without_crashing_navigation()
    {
        await using var f = new Fixture();
        await f.Workspace.DisposeAsync();
        var path = Path.Combine(f.Root, "state", "ordo-editor.json");
        File.WriteAllText(path, "{\"Version\":1,\"SourceRoot\":\"\",\"Recursive\":true,\"Rules\":null}");
        Assert.Null(await Record.ExceptionAsync(f.ReloadAsync));
        Assert.NotEmpty(f.Workspace.Rules);
        await f.Workspace.PreviewAsync();
        var backup = Assert.Single(Directory.GetFiles(Path.Combine(f.Root, "state"), "ordo-editor.json.broken-*"));
        Assert.Contains("\"Rules\":null", File.ReadAllText(backup));
    }

    [AvaloniaFact]
    public async Task Shutdown_does_not_wait_for_a_UI_callback_when_saving_settings_fails()
    {
        await using var f = new Fixture();
        await f.Workspace.PreviewAsync();
        var path = Path.Combine(f.Root, "state", "ordo-editor.json");
        File.Delete(path); Directory.CreateDirectory(path); // A deterministic write error without changing real permissions.
        f.Workspace.Rules[0].Destination = "Changed";
        var stopping = f.Workspace.DisposeAsync().AsTask();
        var finishedWithoutPumping = stopping.Wait(TimeSpan.FromSeconds(3));
        await stopping;
        Assert.True(finishedWithoutPumping);
    }

    [AvaloniaFact]
    public async Task Unsupported_journal_data_pauses_monitoring_without_crashing_history()
    {
        await using var f = new Fixture();
        await f.Monitor.EnrollAsync(f.Workspace.ReadConfiguration());
        var operation = Path.Combine(f.Root, "state", "history", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(operation);
        File.WriteAllText(Path.Combine(operation, "operation.json"), "{\"Version\":999}");
        Assert.Null(await Record.ExceptionAsync(f.Workspace.RefreshHistoryAsync));
        Assert.False(f.Monitor.Enabled);
    }

    [AvaloniaFact]
    public async Task Background_workspace_loop_organizes_reviewed_arrivals_without_an_open_page()
    {
        await using var f = new Fixture();
        await f.Workspace.PreviewAsync();
        f.Workspace.ReviewMonitoring = true;
        await f.Workspace.EnrollAsync();
        f.Workspace.Start();
        var arrival = Path.Combine(f.Source, "background.png"); File.WriteAllText(arrival, "background data");
        var deadline = DateTime.UtcNow.AddSeconds(14);
        while (File.Exists(arrival) && DateTime.UtcNow < deadline) await Task.Delay(50);
        Assert.False(File.Exists(arrival));
        Assert.Equal("background data", File.ReadAllText(Path.Combine(f.Source, "Pictures", "background.png")));
        await f.Workspace.PauseAsync();
        var paused = Path.Combine(f.Source, "paused.png"); File.WriteAllText(paused, "stay");
        await f.Monitor.PollAsync(); await f.Monitor.PollAsync();
        Assert.True(File.Exists(paused));
        Assert.False(f.Monitor.Enabled);
    }

    [AvaloniaTheory]
    [InlineData(ThemeMode.Light)]
    [InlineData(ThemeMode.Dark)]
    public async Task Real_organizer_renders_rule_controls_preview_and_operation_history(ThemeMode mode)
    {
        await using var f = new Fixture();
        File.WriteAllText(Path.Combine(f.Source, "report.pdf"), "report");
        File.WriteAllText(Path.Combine(f.Source, "screenshot.png"), "image");
        AppHost.Get<ThemeService>().Apply(new ThemeSettings { Mode = mode, ReduceMotion = true }, "en");
        using var main = new MainWindowViewModel(new WorkspaceServices(f.Workspace), AppHost.Get<ILocalizer>(), AppHost.Get<UpdateService>());
        main.Navigate("ordo");
        var window = new AZOTH.App.Views.MainWindow { DataContext = main, Width = 1120, Height = 1600 };
        try
        {
            window.Show(); RenderTests.Pump();
            Assert.Equal(5, window.GetVisualDescendants().OfType<ComboBox>().Count(c => c.SelectedItem is Choice<OrdoFileType>));
            RenderTests.Save(window, $"azoth-ordo-rules-{mode.ToString().ToLowerInvariant()}.png");
            await f.Workspace.PreviewAsync(); RenderTests.Pump();
            Assert.Equal(2, f.Workspace.PreviewRows.Count);
            var organize = window.GetVisualDescendants().OfType<Button>().Single(b => ReferenceEquals(b.Command, f.Workspace.ApplyCommand));
            var label = organize.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == AppHost.Get<ILocalizer>()["ordo.apply"]);
            void CheckContrast()
            {
                var foreground = Assert.IsAssignableFrom<ISolidColorBrush>(label.Foreground).Color;
                var surface = organize.GetVisualDescendants().OfType<ContentPresenter>().First(p => p.Background is ISolidColorBrush);
                var background = Assert.IsAssignableFrom<ISolidColorBrush>(surface.Background).Color;
                var high = Math.Max(Luminance(foreground), Luminance(background));
                var low = Math.Min(Luminance(foreground), Luminance(background));
                Assert.True((high + 0.05) / (low + 0.05) >= 4.5,
                    $"Organize action contrast in {mode}: foreground {foreground}, background {background}");
            }
            CheckContrast();
            window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "OrganizationPreview").BringIntoView(); RenderTests.Pump();
            window.MouseMove(organize.TranslatePoint(new Point(organize.Bounds.Width / 2, organize.Bounds.Height / 2), window)!.Value);
            RenderTests.Pump(); Assert.True(organize.IsPointerOver); CheckContrast();
            window.MouseMove(new Point(0, 0)); RenderTests.Pump();
            RenderTests.Save(window, $"azoth-ordo-preview-{mode.ToString().ToLowerInvariant()}.png");
            if (mode == ThemeMode.Light)
            {
                window.Width = 820; window.Height = 760; RenderTests.Pump();
                RenderTests.Save(window, "azoth-ordo-minimum-light.png");
                window.Width = 1120; window.Height = 1600;
            }
            await f.Workspace.ApplyAsync(); RenderTests.Pump();
            Assert.Single(f.Workspace.History);
            RenderTests.Save(window, $"azoth-ordo-history-{mode.ToString().ToLowerInvariant()}.png");
            window.FindControl<ScrollViewer>("PageScroller")!.ScrollToEnd(); RenderTests.Pump();
            RenderTests.Save(window, $"azoth-ordo-monitor-history-{mode.ToString().ToLowerInvariant()}.png");
        }
        finally { window.Close(); }
    }
    private static double Luminance(Color color)
    {
        static double Channel(byte value) => value / 255.0 <= 0.04045
            ? value / 255.0 / 12.92 : Math.Pow((value / 255.0 + 0.055) / 1.055, 2.4);
        return 0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);
    }

}
