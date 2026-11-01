using Avalonia.Headless.XUnit;
using AZOTH.App.Services;
using AZOTH.App.ViewModels;
using AZOTH.App.ViewModels.Pages;
using AZOTH.Core.Donuts;
using AZOTH.Core.Echo;
using AZOTH.Core.Files;
using AZOTH.Core.Localization;
using AZOTH.Core.Presets;
using AZOTH.Core.Settings;
using AZOTH.Platform.Linux;
using Microsoft.Extensions.DependencyInjection;

namespace AZOTH.App.Tests;

[Collection("MainWindow")]
public class EchoWorkspaceTests
{
    private static string FakeTrashPath(string path) => Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(path))!, Path.GetFileName(path) + ".fake-trash");

    private sealed class Fixture : IDisposable
    {
        private string OwnedRoot { get; } = Directory.CreateTempSubdirectory("azoth-echo-ui-").FullName;
        public string Root => Path.Combine(OwnedRoot, "scan");
        public EchoPageViewModel Model { get; }
        public EchoWorkspace Workspace { get; }
        public List<string> Calls { get; } = [];
        public Fixture(EchoTrash? trash = null, string content = "same", FileMutationCoordinator? mutations = null, Func<IFileMetadata, IFileMetadata>? transform = null, Action<string>? reveal = null)
        {
            TestServices.EnsureInitialized();
            Directory.CreateDirectory(Root);
            File.WriteAllText(Path.Combine(Root, "keep.txt"), content);
            File.WriteAllText(Path.Combine(Root, "copy.txt"), content);
            File.WriteAllText(Path.Combine(Root, "third.txt"), content);
            IFileMetadata metadata = OperatingSystem.IsLinux() ? new LinuxFileMetadata()
                : OperatingSystem.IsWindows() ? new AZOTH.Platform.Windows.WindowsFileMetadata()
                : OperatingSystem.IsMacOS() ? new AZOTH.Platform.MacOS.MacOSFileMetadata()
                : new UnsupportedFileMetadata();
            metadata = transform?.Invoke(metadata) ?? metadata;
            var service = new EchoService(metadata, new FileFingerprintService(metadata), mutations ?? new FileMutationCoordinator());
            Workspace = new(service, async (path, token) =>
            {
                Calls.Add(path);
                if (trash is not null) return await trash(path, token);
                File.Move(path, FakeTrashPath(path));
                return TrashResult.Success;
            }, reveal ?? (_ => { }), AppHost.Get<ILocalizer>());
            Model = new(Workspace);
            Model.RootsText = Root;
        }
        public void Dispose() { Model.Dispose(); Workspace.Dispose(); Directory.Delete(OwnedRoot, true); }
    }

    private sealed class WorkspaceServices(EchoWorkspace workspace) : IServiceProvider, IServiceProviderIsService
    {
        public object? GetService(Type type) => type == typeof(EchoWorkspace) ? workspace
            : type == typeof(IServiceProviderIsService) ? this : AppHost.Services.GetService(type);
        public bool IsService(Type type) => type == typeof(EchoWorkspace)
            || AppHost.Get<IServiceProviderIsService>().IsService(type);
    }

    // Losing the workspace on disposal must fail these tests: native work completes after navigation.
    [AvaloniaTheory]
    [InlineData(EchoCleanupStatus.Trashed, false)]
    [InlineData(EchoCleanupStatus.TrashRefused, false)]
    [InlineData(EchoCleanupStatus.Indeterminate, false)]
    [InlineData(EchoCleanupStatus.Trashed, true)]
    [InlineData(EchoCleanupStatus.TrashRefused, true)]
    [InlineData(EchoCleanupStatus.Indeterminate, true)]
    public async Task Native_Trash_result_is_recoverable_after_navigation_or_window_reopening(EchoCleanupStatus expected, bool closeWindow)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var f = new Fixture(async (path, _) =>
        {
            entered.SetResult(); await release.Task;
            if (expected == EchoCleanupStatus.Indeterminate) throw new IOException("controlled uncertainty");
            if (expected == EchoCleanupStatus.TrashRefused) return new(false, "controlled refusal");
            File.Move(path, FakeTrashPath(path)); return TrashResult.Success;
        });
        var services = new WorkspaceServices(f.Workspace);
        using var firstWindow = new MainWindowViewModel(services, AppHost.Get<ILocalizer>(), AppHost.Get<UpdateService>());
        firstWindow.Navigate("echo");
        var first = Assert.IsType<EchoPageViewModel>(firstWindow.CurrentPage);
        Assert.Empty(first.Groups); Assert.Empty(f.Calls); // Visit is read-only.
        first.RootsText = f.Root;
        await first.ScanAsync(); first.Groups[0].Files[0].IsSelected = true; first.Review();
        var selectedPath = Assert.Single(first.ReviewPaths);
        var cleanup = first.ConfirmAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        if (closeWindow) firstWindow.Dispose(); else firstWindow.Navigate("home");
        release.SetResult(); await cleanup;
        using var reopened = closeWindow
            ? new MainWindowViewModel(services, AppHost.Get<ILocalizer>(), AppHost.Get<UpdateService>()) : null;
        var window = reopened ?? firstWindow;
        window.Navigate("echo");
        var returned = Assert.IsType<EchoPageViewModel>(window.CurrentPage);
        Assert.NotSame(first, returned);
        var outcome = Assert.Single(returned.Outcomes);
        Assert.Equal(selectedPath, outcome.Path); Assert.Equal(expected, outcome.Status);
        Assert.NotEmpty(returned.OutcomeMessages); Assert.False(returned.IsBusy); Assert.False(returned.IsReviewed);
        window.Navigate("home"); window.Navigate("echo");
        Assert.Equal(outcome, Assert.Single(Assert.IsType<EchoPageViewModel>(window.CurrentPage).Outcomes));
        Assert.Single(f.Calls);
    }

    [AvaloniaFact]
    public async Task Returning_during_native_Trash_exposes_pending_work_and_can_cancel_remaining_candidates()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var f = new Fixture(async (path, _) =>
        {
            entered.SetResult(); await release.Task;
            File.Move(path, FakeTrashPath(path)); return TrashResult.Success;
        });
        using var main = new MainWindowViewModel(new WorkspaceServices(f.Workspace), AppHost.Get<ILocalizer>(), AppHost.Get<UpdateService>());
        main.Navigate("echo");
        var first = Assert.IsType<EchoPageViewModel>(main.CurrentPage); first.RootsText = f.Root;
        await first.ScanAsync(); first.Groups[0].Files[0].IsSelected = true; first.Groups[0].Files[1].IsSelected = true;
        first.Review(); var cleanup = first.ConfirmAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        main.Navigate("home"); main.Navigate("echo");
        var returned = Assert.IsType<EchoPageViewModel>(main.CurrentPage);
        var busyChanges = new List<bool>();
        returned.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(EchoPageViewModel.IsBusy)) busyChanges.Add(returned.IsBusy);
        };
        try
        {
            Assert.True(returned.IsBusy); Assert.False(returned.CanConfirm); Assert.False(returned.CanReview);
            returned.CancelCommand.Execute(null);
        }
        finally { release.TrySetResult(); await cleanup; }
        Assert.Equal(2, returned.Outcomes.Count);
        Assert.Contains(returned.Outcomes, o => o.Status == EchoCleanupStatus.Trashed);
        Assert.Contains(returned.Outcomes, o => o.Status == EchoCleanupStatus.Cancelled);
        Assert.Single(f.Calls); Assert.False(returned.IsBusy); Assert.False(returned.IsReviewed);
        Assert.Contains(false, busyChanges); // The returned page must notify bindings when shared work finishes.
        main.Navigate("home"); main.Navigate("echo");
        Assert.Equal(2, Assert.IsType<EchoPageViewModel>(main.CurrentPage).Outcomes.Count);
    }

    [AvaloniaFact]
    public void Production_workspace_survives_actual_main_window_close_and_reopen_without_starting_work()
    {
        TestServices.EnsureInitialized();
        var windows = AppHost.Get<WindowService>();
        var workspace = AppHost.Get<EchoWorkspace>();
        var originalRoots = workspace.RootsText;
        var owned = Directory.CreateTempSubdirectory("azoth-echo-lifetime-").FullName;
        try
        {
            windows.ShowMain("echo"); RenderTests.Pump();
            var first = Assert.IsType<EchoPageViewModel>(Assert.IsType<MainWindowViewModel>(windows.Main!.DataContext).CurrentPage);
            first.RootsText = owned;
            Assert.False(first.IsBusy); Assert.Empty(first.Groups);
            windows.Main.Close(); RenderTests.Pump(); Assert.Null(windows.Main);
            windows.ShowMain("echo"); RenderTests.Pump();
            var returned = Assert.IsType<EchoPageViewModel>(Assert.IsType<MainWindowViewModel>(windows.Main!.DataContext).CurrentPage);
            Assert.NotSame(first, returned); Assert.Equal(owned, returned.RootsText);
            Assert.False(returned.IsBusy); Assert.Empty(returned.Groups); Assert.Empty(returned.Outcomes);
            Assert.False(returned.CanConfirm);
        }
        finally
        {
            workspace.RootsText = originalRoots;
            windows.Main?.Close(); RenderTests.Pump(); Directory.Delete(owned);
        }
    }

    [AvaloniaFact]
    public async Task Scan_is_read_only_unchecked_and_reports_progress_and_issues()
    {
        using var f = new Fixture();
        f.Model.RootsText += "\n" + Path.Combine(f.Root, "missing");
        await f.Model.ScanAsync();
        Assert.Single(f.Model.Groups);
        Assert.All(f.Model.Groups[0].Files, row => Assert.False(row.IsSelected));
        Assert.False(f.Model.CanReview);
        Assert.NotEmpty(f.Model.ProgressText);
        Assert.NotEmpty(f.Model.Issues);
        Assert.Empty(f.Calls);
    }

    [AvaloniaFact]
    public async Task Cancelled_scan_has_no_stale_candidates()
    {
        using var f = new Fixture(); await f.Model.ScanAsync();
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await f.Model.ScanAsync(cancellation.Token);
        Assert.Empty(f.Model.Groups);
        Assert.Contains("cancel", f.Model.Status, StringComparison.OrdinalIgnoreCase);
    }

    [AvaloniaFact]
    public async Task Exact_review_is_invalidated_by_selection_roots_and_recursion_and_requires_survivor()
    {
        using var f = new Fixture(); await f.Model.ScanAsync();
        var rows = f.Model.Groups[0].Files;
        rows[0].IsSelected = true;
        Assert.True(f.Model.CanReview);
        await f.Model.ConfirmAsync(); Assert.Empty(f.Calls);
        f.Model.Review(); Assert.True(f.Model.IsReviewed);
        rows[1].IsSelected = true; Assert.False(f.Model.IsReviewed);
        rows[2].IsSelected = true; Assert.False(f.Model.CanReview);
        rows[2].IsSelected = false; f.Model.Review();
        f.Model.Recursive = false; Assert.False(f.Model.IsReviewed);
        await f.Model.ScanAsync(); f.Model.Groups[0].Files[0].IsSelected = true; f.Model.Review();
        f.Model.RootsText += "\n"; Assert.False(f.Model.IsReviewed);
        Assert.Empty(f.Calls);
    }

    [AvaloniaFact]
    public async Task Zero_byte_selection_is_valid_and_confirm_refreshes_actual_files()
    {
        using var f = new Fixture(content: ""); await f.Model.ScanAsync();
        f.Model.Groups[0].Files[0].IsSelected = true;
        Assert.Equal(1, f.Model.SelectedCount); Assert.True(f.Model.CanReview);
        f.Model.Review(); await f.Model.ConfirmAsync();
        Assert.Single(f.Calls); Assert.Equal(EchoCleanupStatus.Trashed, Assert.Single(f.Model.Outcomes).Status);
        Assert.Equal(0, f.Model.SelectedCount); Assert.False(f.Model.IsReviewed);
    }

    [AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task Refusal_and_indeterminate_outcomes_remain_visible_after_reconciliation(bool throws)
    {
        using var f = new Fixture((_, _) => throws ? throw new IOException("provider uncertain") : Task.FromResult(new TrashResult(false, "provider refused")));
        await f.Model.ScanAsync(); f.Model.Groups[0].Files[0].IsSelected = true;
        f.Model.Review(); await f.Model.ConfirmAsync();
        Assert.Equal(throws ? EchoCleanupStatus.Indeterminate : EchoCleanupStatus.TrashRefused, Assert.Single(f.Model.Outcomes).Status);
        Assert.Equal(3, f.Model.Groups.Single().Files.Count);
        Assert.Contains("provider", f.Model.Outcomes[0].Detail);
    }

    [AvaloniaFact]
    public async Task Partial_success_and_stale_file_refusal_are_visible_without_reusing_review()
    {
        var count = 0;
        using var f = new Fixture((path, _) =>
        {
            count++;
            if (count == 2) return Task.FromResult(new TrashResult(false, "fake Trash refusal"));
            File.Move(path, FakeTrashPath(path)); return Task.FromResult(TrashResult.Success);
        });
        await f.Model.ScanAsync();
        f.Model.Groups[0].Files[0].IsSelected = true; f.Model.Groups[0].Files[1].IsSelected = true;
        f.Model.Review();
        Assert.Equal(f.Model.Groups[0].Files.Take(2).Select(r => r.Path), f.Model.ReviewPaths);
        await f.Model.ConfirmAsync();
        Assert.Contains(f.Model.Outcomes, o => o.Status == EchoCleanupStatus.Trashed);
        Assert.Contains(f.Model.Outcomes, o => o.Status == EchoCleanupStatus.TrashRefused);
        Assert.Equal(2, f.Model.Groups.Single().Files.Count);
        var selected = f.Model.Groups[0].Files[0]; selected.IsSelected = true; f.Model.Review();
        File.WriteAllText(selected.Path, "changed after review");
        await f.Model.ConfirmAsync();
        Assert.Equal(EchoCleanupStatus.Refused, Assert.Single(f.Model.Outcomes).Status);
        Assert.False(f.Model.IsReviewed); Assert.Empty(f.Model.Groups);
        f.Model.Dispose();
        using var returned = new EchoPageViewModel(f.Workspace);
        Assert.Equal(EchoCleanupStatus.Refused, Assert.Single(returned.Outcomes).Status);
        Assert.NotEmpty(returned.OutcomeMessages); Assert.False(returned.IsReviewed);
    }

    [AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    public void Actual_donut_launch_ignores_empty_and_directory_context_without_scanning(bool empty)
    {
        TestServices.EnsureInitialized();
        var workspace = AppHost.Get<DonutWorkspace>(); workspace.ResetWorkspace();
        var windows = AppHost.Get<WindowService>(); string? requested = null;
        void OnNavigation(object? sender, string key) => requested = key;
        var directory = Directory.CreateTempSubdirectory("azoth-launch-context-").FullName;
        try
        {
            workspace.AddAction("tool-echo"); workspace.UseDraft();
            var radial = AppHost.Get<RadialViewModel>();
            string[] context = empty ? [] : [directory]; radial.Load(context, true);
            Assert.True(Assert.Single(radial.Items).IsEnabled);
            windows.WorkspaceNavigationRequested += OnNavigation;
            typeof(RadialController).GetMethod("Convert", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .Invoke(AppHost.Get<RadialController>(), [0, -1, context, null]);
            Assert.Equal("echo", requested);
            var echo = Assert.IsType<EchoPageViewModel>(((MainWindowViewModel)windows.Main!.DataContext!).CurrentPage);
            Assert.Empty(echo.Groups); Assert.Equal("", echo.RootsText);
        }
        finally { windows.WorkspaceNavigationRequested -= OnNavigation; windows.Main?.Close(); workspace.ResetWorkspace(); Directory.Delete(directory); }
    }

    private sealed class TransformMetadata(IFileMetadata native, Func<FileMetadataResult, FileMetadataResult> transform) : IFileMetadata
    {
        public FileMetadataResult Read(string path) => transform(native.Read(path));
    }

    [AvaloniaFact]
    public async Task Cancel_while_waiting_for_mutation_gate_is_visible_and_never_calls_Trash()
    {
        var gate = new FileMutationCoordinator();
        using var f = new Fixture(mutations: gate); await f.Model.ScanAsync();
        f.Model.Groups[0].Files[0].IsSelected = true; f.Model.Review();
        using var lease = await gate.AcquireAsync();
        var cleanup = f.Model.ConfirmAsync();
        Assert.True(f.Model.IsBusy); f.Model.CancelCommand.Execute(null); await cleanup;
        Assert.Empty(f.Calls); Assert.False(f.Model.IsReviewed);
        Assert.Contains(f.Model.OutcomeMessages, message => message.Contains("cancel", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(3, f.Model.Groups.Single().Files.Count);
    }

    [AvaloniaFact]
    public async Task Unexpected_cleanup_failure_remains_visible_when_refresh_succeeds()
    {
        var failOnce = false;
        using var f = new Fixture(transform: native => new TransformMetadata(native, result =>
        {
            if (failOnce) { failOnce = false; throw new IOException("unexpected metadata failure"); }
            return result;
        }));
        await f.Model.ScanAsync(); f.Model.Groups[0].Files[0].IsSelected = true; f.Model.Review();
        failOnce = true; await f.Model.ConfirmAsync();
        Assert.Contains(f.Model.OutcomeMessages, message => message.Contains("unexpected metadata failure"));
        Assert.False(f.Model.IsReviewed); Assert.Empty(f.Calls); Assert.Equal(3, f.Model.Groups.Single().Files.Count);
    }

    [AvaloniaFact]
    public async Task Cancellation_during_refresh_keeps_actual_Trash_outcome_and_discards_stale_groups()
    {
        var refresh = false; EchoPageViewModel? model = null;
        using var f = new Fixture((path, _) =>
        {
            File.Move(path, FakeTrashPath(path)); refresh = true; return Task.FromResult(TrashResult.Success);
        }, transform: native => new TransformMetadata(native, result =>
        {
            if (refresh) model!.CancelCommand.Execute(null);
            return result;
        }));
        model = f.Model;
        await model.ScanAsync(); model.Groups[0].Files[0].IsSelected = true; model.Review();
        await model.ConfirmAsync();
        Assert.Equal(EchoCleanupStatus.Trashed, Assert.Single(model.Outcomes).Status);
        Assert.Empty(model.Groups); Assert.Contains("cancel", model.Status, StringComparison.OrdinalIgnoreCase);
        Assert.False(model.IsReviewed);
    }

    [AvaloniaFact]
    public async Task Scan_failure_is_visible_and_drops_old_authorization()
    {
        using var f = new Fixture(transform: native => new TransformMetadata(native, _ => throw new IOException("metadata probe failed")));
        await f.Model.ScanAsync();
        Assert.Contains("metadata probe failed", f.Model.Status);
        Assert.Empty(f.Model.Groups); Assert.False(f.Model.CanReview); Assert.False(f.Model.IsBusy);
    }

    [AvaloniaFact]
    public async Task Consolidated_aliases_are_visible_retained_and_can_be_opened()
    {
        var revealed = new List<string>();
        using var f = new Fixture(transform: native => new TransformMetadata(native, result =>
            result.Snapshot is { Kind: FileEntryKind.RegularFile } entry && Path.GetFileName(entry.Path) != "third.txt"
                ? new(entry with { Identity = new("fixture", "same-physical-file"), LinkCount = 2 }, null) : result), reveal: revealed.Add);
        await f.Model.ScanAsync();
        var group = Assert.Single(f.Model.Groups); Assert.Equal(2, group.Files.Count);
        var retained = group.Files.Single(file => !file.CanSelect);
        Assert.Contains("hardlinks", retained.Exclusion); Assert.Contains("copy.txt", retained.Aliases); Assert.Contains("keep.txt", retained.Aliases);
        retained.IsSelected = true; Assert.False(retained.IsSelected);
        retained.RevealCommand.Execute(null); Assert.Equal(retained.Path, Assert.Single(revealed));
        group.Files.Single(file => file.CanSelect).IsSelected = true;
        Assert.True(f.Model.CanReview); f.Model.Review(); await f.Model.ConfirmAsync();
        Assert.Single(f.Calls); Assert.Equal(EchoCleanupStatus.Trashed, Assert.Single(f.Model.Outcomes).Status);
    }

    [AvaloniaTheory]
    [InlineData(ThemeMode.Light)] [InlineData(ThemeMode.Dark)]
    public async Task Real_echo_workspace_renders_scan_and_exact_review_in_both_themes(ThemeMode mode)
    {
        using var f = new Fixture();
        AppHost.Get<ThemeService>().Apply(new ThemeSettings { Mode = mode, ReduceMotion = true }, "en");
        using var main = new MainWindowViewModel(AppHost.Services, AppHost.Get<ILocalizer>(), AppHost.Get<UpdateService>());
        main.Navigate("echo"); main.CurrentPage = f.Model;
        var window = new AZOTH.App.Views.MainWindow { DataContext = main, Width = 1120, Height = 1200 };
        try
        {
            window.Show(); RenderTests.Pump();
            Assert.Empty(f.Model.Groups);
            RenderTests.Save(window, $"azoth-echo-empty-{mode.ToString().ToLowerInvariant()}.png");
            await f.Model.ScanAsync(); RenderTests.Pump();
            RenderTests.Save(window, $"azoth-echo-scan-{mode.ToString().ToLowerInvariant()}.png");
            f.Model.Groups[0].Files[0].IsSelected = true; f.Model.Review(); RenderTests.Pump();
            Assert.Single(f.Model.ReviewPaths);
            RenderTests.Save(window, $"azoth-echo-review-{mode.ToString().ToLowerInvariant()}.png");
            if (mode == ThemeMode.Light)
            {
                window.Width = 820; window.Height = 760; RenderTests.Pump();
                RenderTests.Save(window, "azoth-echo-minimum-light.png");
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Catalog_refresh_leaves_existing_active_saved_and_draft_snapshots_unchanged()
    {
        TestServices.EnsureInitialized(); var root = Directory.CreateTempSubdirectory("azoth-catalog-restart-").FullName;
        try
        {
            var store = new UserDataStore(root); store.Load();
            using (var workspace = new DonutWorkspace(store))
            {
                workspace.AddAction("to-png"); workspace.SaveAs("My PNG donut"); workspace.UseDraft();
                workspace.AddAction("to-webp");
            }
            store.Presets.RemoveAll(p => p.Id is "tool-echo" or "tool-ordo"); store.SaveLibrary(); store.SaveSettings();
            var before = File.ReadAllBytes(Path.Combine(root, "donuts.json"));
            var restarted = new UserDataStore(root); restarted.Load();
            using var loaded = new DonutWorkspace(restarted);
            Assert.Equal(before, File.ReadAllBytes(Path.Combine(root, "donuts.json")));
            Assert.Single(loaded.Active!.Profile.PresetIds); Assert.Single(loaded.Saved);
            Assert.Equal(2, loaded.Draft.Profile.PresetIds.Count);
            Assert.Contains(restarted.Presets, p => p.Id == "tool-echo"); Assert.Contains(restarted.Presets, p => p.Id == "tool-ordo");
        }
        finally { Directory.Delete(root, true); }
    }

    [AvaloniaTheory]
    [InlineData("tool-echo", "echo")] [InlineData("tool-ordo", "ordo")]
    public void Workspace_actions_survive_current_catalog_restart_and_private_snapshot_IDs(string id, string destination)
    {
        TestServices.EnsureInitialized(); var root = Directory.CreateTempSubdirectory("azoth-launch-").FullName;
        var windows = AppHost.Get<WindowService>(); string? requested = null;
        void OnNavigation(object? sender, string key) => requested = key;
        try
        {
            var store = new UserDataStore(root); store.Load();
            store.Presets.RemoveAll(p => p.Id is "tool-echo" or "tool-ordo"); store.SaveLibrary(); store.SaveSettings();
            var restarted = new UserDataStore(root); restarted.Load();
            Assert.Contains(restarted.Presets, p => p.Id == id);
            using var workspace = new DonutWorkspace(restarted);
            Assert.Null(workspace.Active); Assert.Empty(workspace.Draft.Profile.PresetIds);
            workspace.AddAction(id); workspace.UseDraft();
            var recipe = workspace.Active!.Presets.Single(); Assert.NotEqual(id, recipe.Id);
            Assert.Null(AppHost.Get<PresetAvailability>().Check(recipe, []));
            windows.WorkspaceNavigationRequested += OnNavigation;
            windows.ShowTool(["unknown.xyz"], recipe);
            Assert.Equal(destination, requested);
            if (destination == "echo")
            {
                var model = Assert.IsType<MainWindowViewModel>(windows.Main!.DataContext);
                var echo = Assert.IsType<EchoPageViewModel>(model.CurrentPage);
                Assert.Empty(echo.Groups); Assert.Equal("", echo.RootsText);
            }
        }
        finally { windows.WorkspaceNavigationRequested -= OnNavigation; windows.Main?.Close(); Directory.Delete(root, true); }
    }
}
