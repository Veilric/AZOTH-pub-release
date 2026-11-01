using System.Collections.ObjectModel;
using System.ComponentModel;
using AZOTH.App.Services;
using AZOTH.Core.Echo;
using AZOTH.Core.Localization;
using AZOTH.Core.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AZOTH.App.ViewModels.Pages;

/// <summary>A disposable view of the shared workspace; confirmed operations outlive this page.</summary>
public sealed class EchoPageViewModel : ObservableObject, IDisposable
{
    private readonly EchoWorkspace _workspace;
    private readonly bool _ownsWorkspace;
    private bool _disposed;

    [Microsoft.Extensions.DependencyInjection.ActivatorUtilitiesConstructor]
    public EchoPageViewModel(EchoWorkspace workspace)
    {
        _workspace = workspace;
        workspace.PropertyChanged += OnWorkspaceChanged;
    }

    public EchoPageViewModel(EchoService service, IPlatformServices platform, ILocalizer loc)
        : this(new EchoWorkspace(service, platform, loc)) => _ownsWorkspace = true;

    public EchoPageViewModel(EchoService service, EchoTrash trash, Action<string> reveal, ILocalizer loc)
        : this(new EchoWorkspace(service, trash, reveal, loc)) => _ownsWorkspace = true;

    public string RootsText { get => _workspace.RootsText; set => _workspace.RootsText = value; }
    public bool Recursive { get => _workspace.Recursive; set => _workspace.Recursive = value; }
    public bool IsBusy { get => _workspace.IsBusy; set => _workspace.IsBusy = value; }
    public string Status => _workspace.Status;
    public string ProgressText => _workspace.ProgressText;
    public string Estimate => _workspace.Estimate;
    public bool IsReviewed { get => _workspace.IsReviewed; set => _workspace.IsReviewed = value; }
    public ObservableCollection<EchoGroupRow> Groups => _workspace.Groups;
    public ObservableCollection<string> Issues => _workspace.Issues;
    public ObservableCollection<EchoCleanupOutcome> Outcomes => _workspace.Outcomes;
    public ObservableCollection<string> ReviewPaths => _workspace.ReviewPaths;
    public ObservableCollection<string> OutcomeMessages => _workspace.OutcomeMessages;
    public int SelectedCount => _workspace.SelectedCount;
    public bool CanReview => _workspace.CanReview;
    public bool CanConfirm => _workspace.CanConfirm;
    public string SelectionSummary => _workspace.SelectionSummary;
    public IAsyncRelayCommand ScanCommand => _workspace.ScanCommand;
    public IRelayCommand CancelCommand => _workspace.CancelCommand;
    public IRelayCommand ReviewCommand => _workspace.ReviewCommand;
    public IRelayCommand DismissReviewCommand => _workspace.DismissReviewCommand;
    public IAsyncRelayCommand ConfirmCommand => _workspace.ConfirmCommand;
    public Task ScanAsync(CancellationToken token = default) => _disposed ? Task.CompletedTask : _workspace.ScanAsync(token);
    public void Review() { if (!_disposed) _workspace.Review(); }
    public Task ConfirmAsync() => _disposed ? Task.CompletedTask : _workspace.ConfirmAsync();
    public string OutcomeText(EchoCleanupOutcome outcome) => _workspace.OutcomeText(outcome);
    public static string FormatBytes(long bytes) => EchoWorkspace.FormatBytes(bytes);

    private void OnWorkspaceChanged(object? sender, PropertyChangedEventArgs e) => OnPropertyChanged(e.PropertyName);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _workspace.PropertyChanged -= OnWorkspaceChanged;
        _workspace.DetachPage();
        if (_ownsWorkspace) _workspace.Dispose();
    }
}

public sealed class EchoGroupRow
{
    public string Hash { get; }
    public string Details { get; }
    public ObservableCollection<EchoFileRow> Files { get; } = [];
    public EchoGroupRow(EchoGroup group, EchoWorkspace owner, Action<string> reveal, ILocalizer loc)
    {
        Hash = "SHA-256 · " + group.Sha256;
        Details = loc.Format("echo.group", group.Files.Count, EchoPageViewModel.FormatBytes(group.Length));
        foreach (var file in group.Files) Files.Add(new(file, owner, reveal, loc));
    }
}

public sealed partial class EchoFileRow : ObservableObject
{
    private readonly EchoWorkspace _owner;
    public string Path { get; }
    public string Name => System.IO.Path.GetFileName(Path);
    public long Length { get; }
    public string Details { get; }
    public string Exclusion { get; }
    public string Aliases { get; }
    public bool CanSelect { get; }
    public bool HasAliases => Aliases.Length > 0;
    public bool HasExclusion => Exclusion.Length > 0;
    [ObservableProperty] private bool _isSelected;
    public IRelayCommand RevealCommand { get; }
    public EchoFileRow(EchoFile file, EchoWorkspace owner, Action<string> reveal, ILocalizer loc)
    {
        _owner = owner; Path = file.Snapshot.Path; Length = file.Snapshot.Length;
        CanSelect = file.Snapshot.LinkCount == 1 && file.Aliases.Count == 1;
        Details = $"{EchoPageViewModel.FormatBytes(Length)} · {file.Snapshot.ModifiedUtc.ToLocalTime():g}";
        Exclusion = CanSelect ? "" : loc.Format("echo.excluded", file.Snapshot.LinkCount, file.Aliases.Count);
        Aliases = file.Aliases.Count > 1 ? string.Join(Environment.NewLine, file.Aliases) : "";
        RevealCommand = new RelayCommand(() => reveal(Path));
    }
    partial void OnIsSelectedChanged(bool value)
    {
        if (value && !CanSelect) { IsSelected = false; return; }
        _owner.SelectionChanged();
    }
}
