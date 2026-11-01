using System.Collections.ObjectModel;
using AZOTH.App.ViewModels.Pages;
using AZOTH.Core.Echo;
using AZOTH.Core.Localization;
using AZOTH.Core.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AZOTH.App.Services;

/// <summary>App-lifetime ECHO operation state. Page disposal never abandons pending native results.</summary>
public sealed partial class EchoWorkspace : ObservableObject, IDisposable
{
    private readonly EchoService _service;
    private readonly EchoTrash _trash;
    private readonly Action<string> _reveal;
    private readonly ILocalizer _loc;
    private EchoScanResult? _scan;
    private string[]? _reviewedPaths;
    private CancellationTokenSource? _operation;
    private bool _disposed;
    private int _generation;

    [Microsoft.Extensions.DependencyInjection.ActivatorUtilitiesConstructor]
    public EchoWorkspace(EchoService service, IPlatformServices platform, ILocalizer loc)
        : this(service, (path, token) => Task.Run(() =>
        {
            // Native Trash is synchronous. Once started, await its actual result even if cancellation arrives.
            token.ThrowIfCancellationRequested();
            var refusal = platform.MoveToTrash(path);
            return refusal is null ? TrashResult.Success
                : new TrashResult(false, loc[refusal.ReasonKey] + (refusal.Detail is null ? "" : ": " + refusal.Detail));
        }), platform.RevealInFileManager, loc) { }

    public EchoWorkspace(EchoService service, EchoTrash trash, Action<string> reveal, ILocalizer loc)
        => (_service, _trash, _reveal, _loc) = (service, trash, reveal, loc);

    [ObservableProperty] private string _rootsText = "";
    [ObservableProperty] private bool _recursive = true;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private string _progressText = "";
    [ObservableProperty] private string _estimate = "";
    [ObservableProperty] private bool _isReviewed;
    public ObservableCollection<EchoGroupRow> Groups { get; } = [];
    public ObservableCollection<string> Issues { get; } = [];
    public ObservableCollection<EchoCleanupOutcome> Outcomes { get; } = [];
    public ObservableCollection<string> ReviewPaths { get; } = [];
    public ObservableCollection<string> OutcomeMessages { get; } = [];
    public int SelectedCount => Groups.Sum(g => g.Files.Count(f => f.IsSelected));
    public bool CanReview => !IsBusy && _scan is not null && SelectedCount > 0
        && Groups.All(g => g.Files.Any(f => !f.IsSelected));
    public bool CanConfirm => !IsBusy && IsReviewed && _reviewedPaths is { Length: > 0 };
    public string SelectionSummary => _loc.Format("echo.selection", SelectedCount,
        FormatBytes(Groups.SelectMany(g => g.Files).Where(f => f.IsSelected).Aggregate(0L,
            (sum, row) => sum > long.MaxValue - row.Length ? long.MaxValue : sum + row.Length)));

    partial void OnRootsTextChanged(string value) => ClearScan();
    partial void OnRecursiveChanged(bool value) => ClearScan();
    partial void OnIsBusyChanged(bool value) => NotifySelection();
    partial void OnIsReviewedChanged(bool value) => OnPropertyChanged(nameof(CanConfirm));

    private void InvalidateReview()
    {
        _reviewedPaths = null; IsReviewed = false; ReviewPaths.Clear();
    }
    internal void SelectionChanged()
    {
        InvalidateReview(); NotifySelection();
    }
    private void NotifySelection()
    {
        OnPropertyChanged(nameof(SelectedCount)); OnPropertyChanged(nameof(CanReview));
        OnPropertyChanged(nameof(CanConfirm)); OnPropertyChanged(nameof(SelectionSummary));
    }
    private void ClearScan()
    {
        _generation++; _operation?.Cancel(); _scan = null; Groups.Clear(); Issues.Clear();
        Estimate = ""; ProgressText = ""; SelectionChanged();
    }

    [RelayCommand]
    public async Task ScanAsync(CancellationToken token = default)
    {
        if (IsBusy || _disposed) return;
        ClearScan(); Outcomes.Clear(); OutcomeMessages.Clear();
        var roots = RootsText.Split(['\n', '\r'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (roots.Length == 0) { Status = _loc["echo.choose_roots"]; return; }
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(token);
        _operation = operation; IsBusy = true;
        try { await ReadScanAsync(roots, Recursive, operation.Token); }
        catch (OperationCanceledException) { if (!_disposed) Status = _loc["echo.cancelled"]; }
        catch (Exception ex) { if (!_disposed) Status = _loc["echo.scan_failed"] + " " + ex.Message; }
        finally { _operation = null; IsBusy = false; }
    }

    private async Task ReadScanAsync(string[] roots, bool recursive, CancellationToken token)
    {
        var generation = _generation;
        Status = _loc["echo.scanning"];
        var progress = new Progress<EchoProgress>(p =>
        {
            if (!_disposed && generation == _generation)
                ProgressText = _loc.Format(p.Phase == "Hashing" ? "echo.progress_hash" : "echo.progress_enumerate", p.FilesDiscovered, p.FilesHashed);
        });
        var result = await _service.ScanAsync(roots, recursive, progress, token);
        if (_disposed || generation != _generation) return;
        _generation++;
        _scan = result; Groups.Clear(); Issues.Clear();
        foreach (var group in result.Groups) Groups.Add(new(group, this, _reveal, _loc));
        foreach (var issue in result.Issues) Issues.Add($"{issue.Path} — {issue.Refusal.Kind}: {issue.Refusal.Detail}");
        Estimate = _loc.Format("echo.estimate", FormatBytes(result.EstimatedSavingsBytes));
        ProgressText = _loc.Format("echo.progress_complete", result.FilesHashed);
        Status = _loc.Format("echo.complete", result.Groups.Count, result.Issues.Count);
        SelectionChanged();
    }

    [RelayCommand]
    public void Review()
    {
        if (!CanReview) return;
        _reviewedPaths = Groups.SelectMany(g => g.Files).Where(f => f.IsSelected).Select(f => f.Path).ToArray();
        ReviewPaths.Clear(); foreach (var path in _reviewedPaths) ReviewPaths.Add(path);
        IsReviewed = true;
    }

    [RelayCommand]
    private void DismissReview() => InvalidateReview();

    [RelayCommand]
    public async Task ConfirmAsync()
    {
        if (!CanConfirm || _scan is null || _disposed) return;
        var scan = _scan; var selected = _reviewedPaths!;
        var roots = RootsText.Split(['\n', '\r'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var recursive = Recursive;
        InvalidateReview(); Outcomes.Clear(); OutcomeMessages.Clear(); IsBusy = true;
        using var operation = new CancellationTokenSource(); _operation = operation;
        try
        {
            var result = await Task.Run(() => _service.CleanupAsync(scan, selected, _trash, operation.Token));
            foreach (var outcome in result.Outcomes) { Outcomes.Add(outcome); OutcomeMessages.Add(OutcomeText(outcome)); }
        }
        catch (OperationCanceledException) { OutcomeMessages.Add(_loc["echo.cleanup_cancelled"]); }
        catch (Exception ex) { OutcomeMessages.Add(_loc["echo.cleanup_failed"] + " " + ex.Message); }
        finally
        {
            _operation = null;
            // The old scan cannot authorize another cleanup, even on refusal/cancellation/uncertain results.
            ClearScan();
            if (!_disposed)
            {
                using var refresh = new CancellationTokenSource();
                _operation = refresh;
                try
                {
                    await ReadScanAsync(roots, recursive, refresh.Token);
                    Status = _loc["echo.reconciled"];
                }
                catch (OperationCanceledException) { Status = _loc["echo.refresh_cancelled"]; }
                catch (Exception ex) { Status = _loc["echo.scan_failed"] + " " + ex.Message; }
                finally { _operation = null; }
            }
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Cancel() => _operation?.Cancel();

    public string OutcomeText(EchoCleanupOutcome outcome) =>
        $"{outcome.Path} — {_loc["echo.outcome." + outcome.Status]}{(outcome.Detail is null ? "" : ": " + outcome.Detail)}";
    public static string FormatBytes(long bytes) => $"{bytes:N0} B";
    internal void DetachPage() => InvalidateReview();

    public void Dispose() { _disposed = true; _generation++; _operation?.Cancel(); InvalidateReview(); }
}
