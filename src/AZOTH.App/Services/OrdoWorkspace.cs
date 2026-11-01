using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia.Threading;
using AZOTH.App.ViewModels;
using AZOTH.Core.Localization;
using AZOTH.Core.Ordo;
using AZOTH.Core.Settings;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AZOTH.App.Services;

/// <summary>Process-lifetime editor and operation state; pages only attach to this workspace.</summary>
public sealed partial class OrdoWorkspace : ObservableObject, IAsyncDisposable
{
    private readonly OrdoPlanner _planner;
    private readonly OrdoService _service;
    private readonly OrdoJournal _journal;
    private readonly OrdoMonitor _monitor;
    private readonly UserDataStore _store;
    private readonly SystemIntegrationService _integration;
    private readonly ILocalizer _loc;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly HashSet<OrdoRuleEditor> _subscriptions = [];
    private readonly SemaphoreSlim _historyRead = new(1);
    private readonly string _editorPath;
    private Task _edits = Task.CompletedTask;
    private Task? _loop;
    private Task? _coreWork;
    private CancellationTokenSource? _operation;
    private OrdoPlan? _preview;
    private OrdoConfiguration? _reviewedConfiguration;
    private bool _loading;
    private bool _disposed;
    private bool _editorBroken;
    private bool _editorReadOnly;
    private bool _monitorWasBusy;
    private int _revision;
    private string? _editFailure;

    public OrdoWorkspace(OrdoPlanner planner, OrdoService service, OrdoJournal journal, OrdoMonitor monitor,
        UserDataStore store, SystemIntegrationService integration, ILocalizer loc)
    {
        (_planner, _service, _journal, _monitor, _store, _integration, _loc) = (planner, service, journal, monitor, store, integration, loc);
        _editorPath = Path.Combine(store.Directory, "ordo-editor.json");
        _loading = true;
        Rules.CollectionChanged += (_, _) => RulesChanged();
        LoadEditor();
        _loading = false;
        _monitor.Changed += MonitorChanged;
        _loc.LanguageChanged += LanguageChanged;
        if (string.IsNullOrEmpty(Status)) Status = _loc["ordo.ready"];
        _ = RefreshHistoryAsync();
    }

    [ObservableProperty] private string _sourceRoot = "";
    [ObservableProperty] private bool _recursive = true;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private string _progressText = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewRows))]
    private OrdoPreview? _previewDisplay;
    [ObservableProperty] private bool _reviewMonitoring;
    public ObservableCollection<OrdoRuleEditor> Rules { get; } = [];
    public IReadOnlyList<OrdoPreviewMove> PreviewRows => PreviewDisplay?.Moves ?? [];
    public ObservableCollection<OrdoHistoryRow> History { get; } = [];
    public bool EditorAvailable => !IsBusy && !_editorReadOnly;
    public bool HasPreview => _preview is not null;
    public bool CanApply => !IsBusy && _preview is { Moves.Count: > 0 };
    public bool CanEnroll => !IsBusy && HasPreview && ReviewMonitoring;
    public string MonitorStatus => _monitor.Status;
    public bool MonitoringEnabled => _monitor.Enabled;
    public int PendingCount => _monitor.PendingCount;
    public bool StartupAvailable => _integration.RegistrationEnabled && !_integration.SeparateDataDirectory;
    public string StartupExplanation => _loc[StartupAvailable ? "ordo.startup_available" : "ordo.startup_unavailable"];
    public bool StartWithSystem
    {
        get => StartupAvailable && _store.Settings.StartWithSystem;
        set
        {
            if (!StartupAvailable) return;
            _store.Settings.StartWithSystem = value;
            _store.SaveSettings(); _integration.Apply(_store.Settings);
            if (_integration.Error is { } error) Status = error;
            OnPropertyChanged();
        }
    }

    partial void OnSourceRootChanged(string value) => Edited();
    partial void OnRecursiveChanged(bool value) => Edited();
    partial void OnIsBusyChanged(bool value) { UpdateAuthorization(); OnPropertyChanged(nameof(EditorAvailable)); RefreshRuleCommands(); }
    partial void OnReviewMonitoringChanged(bool value) { OnPropertyChanged(nameof(CanEnroll)); EnrollCommand.NotifyCanExecuteChanged(); }

    private IReadOnlyList<Choice<OrdoFileType>> TypeChoices() => Enum.GetValues<OrdoFileType>()
        .Select(t => new Choice<OrdoFileType>(t, _loc["ordo.type." + t])).ToArray();

    private void RulesChanged()
    {
        foreach (var old in _subscriptions.Where(r => !Rules.Contains(r)).ToArray())
        { old.PropertyChanged -= RuleChanged; _subscriptions.Remove(old); }
        foreach (var row in Rules.Where(r => !_subscriptions.Contains(r)))
        {
            row.TypeChoices = TypeChoices();
            row.MoveUpCommand = new RelayCommand(() => MoveRule(row, -1), () => EditorAvailable && Rules.IndexOf(row) > 0);
            row.MoveDownCommand = new RelayCommand(() => MoveRule(row, 1), () => EditorAvailable && Rules.IndexOf(row) < Rules.Count - 1);
            row.RemoveCommand = new RelayCommand(() => Rules.Remove(row), () => EditorAvailable);
            row.PropertyChanged += RuleChanged; _subscriptions.Add(row);
        }
        RefreshRuleCommands(); Edited();
    }
    private void RuleChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(OrdoRuleEditor.TypeChoices) or nameof(OrdoRuleEditor.SelectedType)) return;
        Edited();
    }
    private void RefreshRuleCommands()
    {
        foreach (var row in Rules)
        { row.MoveUpCommand?.NotifyCanExecuteChanged(); row.MoveDownCommand?.NotifyCanExecuteChanged(); row.RemoveCommand?.NotifyCanExecuteChanged(); }
    }
    private void MoveRule(OrdoRuleEditor row, int delta)
    {
        var index = Rules.IndexOf(row); var target = index + delta;
        if (EditorAvailable && index >= 0 && target >= 0 && target < Rules.Count) Rules.Move(index, target);
    }
    [RelayCommand] public void AddRule() { if (EditorAvailable) Rules.Add(new()); }
    [RelayCommand]
    public void UseStarters()
    {
        if (!EditorAvailable) return;
        _loading = true; Rules.Clear();
        foreach (var (type, destination) in new[] { (OrdoFileType.Document, "Documents"), (OrdoFileType.Image, "Pictures"),
                     (OrdoFileType.Archive, "Archives"), (OrdoFileType.Audio, "Audio"), (OrdoFileType.Video, "Video") })
            Rules.Add(new() { Type = type, Destination = destination });
        _loading = false; Edited();
    }

    private void InvalidatePreview()
    {
        _preview = null; _reviewedConfiguration = null; ReviewMonitoring = false;
        PreviewDisplay = null; UpdateAuthorization();
    }
    private void UpdateAuthorization()
    {
        OnPropertyChanged(nameof(HasPreview)); OnPropertyChanged(nameof(CanApply)); OnPropertyChanged(nameof(CanEnroll));
        ApplyCommand.NotifyCanExecuteChanged(); EnrollCommand.NotifyCanExecuteChanged();
    }
    private void Edited()
    {
        if (_loading || _disposed || _editorReadOnly) return;
        _revision++; _operation?.Cancel(); InvalidatePreview();
        var pause = _monitor.PauseAsync(_loc["ordo.paused_edit"]); // Cancels immediately, before the asynchronous disk write.
        var snapshot = new OrdoEditorState(1, SourceRoot, Recursive, Rules.Select(r => new OrdoEditorRule(
            r.Id, r.Type, r.Extension, r.Destination, r.ModifiedFrom, r.ModifiedBefore, r.GroupByMonth)).ToArray());
        _edits = PersistEditAsync(_edits, pause, snapshot);
    }
    private async Task PersistEditAsync(Task prior, Task pause, OrdoEditorState snapshot)
    {
        try
        {
            await prior.ConfigureAwait(false); await pause.ConfigureAwait(false);
            await Task.Run(() =>
            {
                Directory.CreateDirectory(_store.Directory);
                if (_editorBroken && File.Exists(_editorPath))
                { File.Move(_editorPath, _editorPath + ".broken-" + Guid.NewGuid().ToString("N")); _editorBroken = false; }
                var temp = _editorPath + ".tmp";
                using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                { JsonSerializer.Serialize(stream, snapshot, OrdoEditorJsonContext.Default.OrdoEditorState); stream.Flush(true); }
                File.Move(temp, _editorPath, true);
                _editFailure = null;
            }).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException)
        {
            var message = _loc.Format("ordo.save_failed", ex.Message);
            _editFailure = message;
            Dispatcher.UIThread.Post(() => { if (!_disposed && _editFailure == message) Status = message; });
        }
    }
    private void LoadEditor()
    {
        try
        {
            if (File.Exists(_editorPath))
            {
                var data = JsonSerializer.Deserialize(File.ReadAllText(_editorPath), OrdoEditorJsonContext.Default.OrdoEditorState)
                    ?? throw new InvalidDataException("Empty editor settings.");
                if (data.Version != 1) { _editorReadOnly = true; Status = _loc["ordo.newer_settings"]; return; }
                if (data.Rules is null || data.SourceRoot is null || data.Rules.Any(r => r is null))
                    throw new InvalidDataException("Incomplete editor settings.");
                SourceRoot = data.SourceRoot; Recursive = data.Recursive;
                foreach (var row in data.Rules) Rules.Add(new()
                {
                    Id = row.Id,
                    Type = row.Type,
                    Extension = row.Extension,
                    Destination = row.Destination,
                    ModifiedFrom = row.ModifiedFrom,
                    ModifiedBefore = row.ModifiedBefore,
                    GroupByMonth = row.GroupByMonth
                });
                return;
            }
            var legacy = Path.Combine(_store.Directory, "ordo-editor.txt");
            if (File.Exists(legacy))
            {
                var lines = File.ReadAllLines(legacy);
                if (lines.Length >= 2)
                {
                    SourceRoot = lines[0]; Recursive = lines[1] == "true";
                    foreach (var line in lines.Skip(2).Where(l => !string.IsNullOrWhiteSpace(l)))
                    {
                        var parts = line.Split('|', StringSplitOptions.TrimEntries);
                        if (parts.Length != 6 || !Enum.TryParse<OrdoFileType>(parts[0], out var type) || !bool.TryParse(parts[5], out var month))
                            throw new InvalidDataException("Invalid saved rule.");
                        Rules.Add(new() { Type = type, Extension = parts[1], Destination = parts[2], ModifiedFrom = parts[3], ModifiedBefore = parts[4], GroupByMonth = month });
                    }
                    return;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException)
        { Rules.Clear(); _editorBroken = true; Status = _loc.Format("ordo.load_failed", ex.Message); }
        foreach (var (type, destination) in new[] { (OrdoFileType.Document, "Documents"), (OrdoFileType.Image, "Pictures"),
                     (OrdoFileType.Archive, "Archives"), (OrdoFileType.Audio, "Audio"), (OrdoFileType.Video, "Video") })
            Rules.Add(new() { Type = type, Destination = destination });
    }

    public OrdoConfiguration ReadConfiguration()
    {
        if (_editorReadOnly) throw new InvalidOperationException(_loc["ordo.newer_settings"]);
        if (string.IsNullOrWhiteSpace(SourceRoot) || !Path.IsPathFullyQualified(SourceRoot)) throw new ArgumentException(_loc["ordo.choose_source"]);
        if (Rules.Count == 0) throw new ArgumentException(_loc["ordo.add_rule_first"]);
        DateTimeOffset? Date(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            if (!DateTimeOffset.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var value))
                throw new ArgumentException(_loc["ordo.invalid_date"]);
            return value;
        }
        return new(SourceRoot, Rules.Select(r => new OrdoRule(r.Id, r.Destination, r.Type, r.Extension,
            Date(r.ModifiedFrom), Date(r.ModifiedBefore), r.GroupByMonth)), Recursive);
    }
    private async Task<T> TrackAsync<T>(Task<T> task) { _coreWork = task; return await task; }
    private async Task TrackAsync(Task task) { _coreWork = task; await task; }
    private async Task RunAsync(Func<CancellationToken, Task> action)
    {
        if (IsBusy || _disposed) return;
        IsBusy = true; using var cancel = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token); _operation = cancel;
        try
        {
            await _edits;
            if (_editFailure is { } error) throw new IOException(error);
            cancel.Token.ThrowIfCancellationRequested(); await action(cancel.Token);
        }
        catch (OperationCanceledException) { Status = _loc["ordo.cancelled"]; }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or JsonException)
        { Status = ex.Message; }
        finally
        {
            _coreWork = null; _operation = null; IsBusy = false;
            await RefreshHistoryAsync(); NotifyMonitor();
        }
    }

    [RelayCommand]
    public Task PreviewAsync() => RunAsync(async token =>
    {
        InvalidatePreview(); var config = ReadConfiguration(); var revision = _revision;
        ProgressText = _loc["ordo.preparing"];
        var progress = new Progress<OrdoProgress>(p => ProgressText = _loc.Format("ordo.progress", p.FilesDiscovered, p.FilesPrepared));
        var plan = await TrackAsync(_planner.PreviewAsync(config, progress, token));
        token.ThrowIfCancellationRequested();
        if (revision != _revision) throw new OperationCanceledException(token);
        _preview = plan; _reviewedConfiguration = config;
        PreviewDisplay = OrdoPreview.FromPlan(plan, config, _loc);
        Status = _loc["ordo.preview_ready"];
    });
    [RelayCommand(CanExecute = nameof(CanApply))]
    public Task ApplyAsync() => RunAsync(async token =>
    {
        if (_preview is null || _reviewedConfiguration is null) throw new InvalidOperationException(_loc["ordo.preview_first"]);
        var plan = _preview; var config = _reviewedConfiguration; InvalidatePreview();
        ProgressText = _loc["ordo.applying"];
        await TrackAsync(_monitor.PauseAndWaitAsync(_loc["ordo.paused_manual"]));
        var result = await TrackAsync(_service.ApplyAsync(plan, config, token));
        Status = _loc.Format("ordo.applied", result.Entries.Count(e => e.State == OrdoEntryState.Completed), result.Entries.Count);
    });
    [RelayCommand(CanExecute = nameof(CanEnroll))]
    public Task EnrollAsync() => RunAsync(async token =>
    {
        if (!ReviewMonitoring || _preview is null || _reviewedConfiguration is null) throw new InvalidOperationException(_loc["ordo.review_monitor_first"]);
        var history = await Task.Run(_journal.List, token);
        if (history.Any(o => o.Entries.Any(e => e.State is OrdoEntryState.Prepared or OrdoEntryState.UndoPrepared or OrdoEntryState.Conflict)))
            throw new InvalidOperationException(_loc["ordo.unresolved_history"]);
        token.ThrowIfCancellationRequested();
        await TrackAsync(_monitor.EnrollAsync(_reviewedConfiguration));
        InvalidatePreview(); Status = _loc["ordo.enrolled"];
    });
    [RelayCommand]
    public Task PauseAsync() => RunAsync(async _ =>
    { await TrackAsync(_monitor.PauseAndWaitAsync(_loc["ordo.paused_user"])); Status = _loc["ordo.paused_user"]; });
    [RelayCommand] public void Cancel() => _operation?.Cancel();
    public Task UndoAsync(Guid id) => RunAsync(async token =>
    {
        InvalidatePreview(); ProgressText = _loc["ordo.undoing"];
        await TrackAsync(_monitor.PauseAndWaitAsync(_loc["ordo.paused_undo"]));
        var result = await TrackAsync(_service.UndoAsync(id, token));
        Status = _loc.Format("ordo.undone", result.Entries.Count(e => e.State == OrdoEntryState.Undone), result.Entries.Count);
    });
    public Task ReconcileAsync(Guid id) => RunAsync(async token =>
    {
        await TrackAsync(_monitor.PauseAndWaitAsync(_loc["ordo.paused_manual"]));
        await TrackAsync(_service.ReconcileAsync(id, token)); Status = _loc["ordo.reconciled"];
    });
    [RelayCommand]
    public async Task RefreshHistoryAsync()
    {
        if (!await _historyRead.WaitAsync(0)) return;
        try
        {
            var records = await Task.Run(_journal.List);
            await OnUiAsync(() =>
            {
                if (_disposed) return;
                History.Clear();
                foreach (var record in records.OrderByDescending(o => o.CreatedUtc)) History.Add(new(record, this, _loc));
            });
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            await _monitor.PauseAsync(_loc["ordo.history_failed"]);
            await OnUiAsync(() => Status = _loc["ordo.history_failed"] + " " + ex.Message);
        }
        finally { _historyRead.Release(); }
    }

    private void NotifyMonitor()
    { OnPropertyChanged(nameof(MonitorStatus)); OnPropertyChanged(nameof(MonitoringEnabled)); OnPropertyChanged(nameof(PendingCount)); }
    private void MonitorChanged(object? sender, EventArgs e)
    {
        var busy = _monitor.IsOrganizing;
        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed) return;
            NotifyMonitor();
            if (_monitorWasBusy && !busy) _ = RefreshHistoryAsync();
            _monitorWasBusy = busy;
        });
    }
    private void LanguageChanged(object? sender, EventArgs e)
    {
        _loading = true;
        foreach (var row in Rules) row.TypeChoices = TypeChoices();
        _loading = false; OnPropertyChanged(nameof(StartupExplanation));
        if (_preview is { } plan && _reviewedConfiguration is { } config)
            PreviewDisplay = OrdoPreview.FromPlan(plan, config, _loc);
        _ = RefreshHistoryAsync();
    }
    public void Start() => _loop ??= Task.Run(MonitorLoopAsync);
    private async Task MonitorLoopAsync()
    {
        try
        {
            while (!_shutdown.IsCancellationRequested)
            {
                await _monitor.PollAsync().ConfigureAwait(false);
                await Task.Delay(TimeSpan.FromSeconds(5), _shutdown.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
    }
    private static Task OnUiAsync(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess()) { action(); return Task.CompletedTask; }
        return Dispatcher.UIThread.InvokeAsync(action).GetTask();
    }
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true; _monitor.Changed -= MonitorChanged; _loc.LanguageChanged -= LanguageChanged;
        _shutdown.Cancel(); _operation?.Cancel();
        await _monitor.DisposeAsync().ConfigureAwait(false);
        try { if (_coreWork is { } work) await work.ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        await _edits.ConfigureAwait(false);
        if (_loop is { } loop) await loop.ConfigureAwait(false);
    }
}

public sealed class OrdoHistoryRow
{
    public string Title { get; }
    public string Outcomes { get; }
    public IAsyncRelayCommand UndoCommand { get; }
    public IAsyncRelayCommand ReconcileCommand { get; }
    public OrdoHistoryRow(OrdoOperation operation, OrdoWorkspace owner, ILocalizer loc)
    {
        Title = $"{operation.CreatedUtc.ToLocalTime():g} · {operation.Id.ToString("N")[..8]}";
        Outcomes = string.Join('\n', operation.Entries.Select(e => $"{loc["ordo.state." + e.State]}: {e.Move.Source.Snapshot.Path} → {e.Move.Destination}{(e.Detail is null ? "" : " — " + e.Detail)}"));
        UndoCommand = new AsyncRelayCommand(() => owner.UndoAsync(operation.Id));
        ReconcileCommand = new AsyncRelayCommand(() => owner.ReconcileAsync(operation.Id));
    }
}
internal sealed record OrdoEditorRule(string Id, OrdoFileType Type, string Extension, string Destination, string ModifiedFrom, string ModifiedBefore, bool GroupByMonth);
internal sealed record OrdoEditorState(int Version, string SourceRoot, bool Recursive, OrdoEditorRule[] Rules);
[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(OrdoEditorState))]
internal partial class OrdoEditorJsonContext : JsonSerializerContext;
