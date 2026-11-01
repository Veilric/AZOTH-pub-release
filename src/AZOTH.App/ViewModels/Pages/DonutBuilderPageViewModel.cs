using System.Text.Json;
using System.Text.Json.Serialization;
using AZOTH.App.Controls;
using AZOTH.App.Services;
using AZOTH.Core.Donuts;
using AZOTH.Core.Localization;
using AZOTH.Core.Presets;
using AZOTH.Core.Profiles;
using AZOTH.Core.Settings;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AZOTH.App.ViewModels.Pages;

public sealed record BuilderAction(string Id, string Label, string? Caption);

/// <summary>The persistent draft editor. Selection and saving never change the execution snapshot.</summary>
public sealed partial class DonutBuilderPageViewModel : ObservableObject, IDisposable
{
    private readonly DonutWorkspace _workspace;
    private readonly UserDataStore _store;
    private readonly ILocalizer _loc;
    private readonly WindowService _windows;

    public DonutBuilderPageViewModel(DonutWorkspace workspace, UserDataStore store, ILocalizer loc, WindowService windows)
    {
        (_workspace, _store, _loc, _windows) = (workspace, store, loc, windows);
        Categories = ToolbarPageViewModel.TabDefinitions.Select(t => new Choice<PaletteTab>(t.Id, loc[t.LabelKey])).ToList();
        _category = Categories[0];
        workspace.Changed += Changed;
        store.LibraryChanged += Changed;
        Refresh();
    }

    public IReadOnlyList<Choice<PaletteTab>> Categories { get; }
    [ObservableProperty] private Choice<PaletteTab> _category;
    [ObservableProperty] private string _search = "";
    [ObservableProperty] private bool _showMoreFormats;
    [ObservableProperty] private bool _editingBack;
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string? _message;
    [ObservableProperty] private BuilderAction? _selectedCatalog;
    [ObservableProperty] private BuilderAction? _selectedAction;
    [ObservableProperty] private Choice<string>? _selectedSaved;
    [ObservableProperty] private Choice<string>? _selectedTemplate;
    [ObservableProperty] private IReadOnlyList<BuilderAction> _catalog = [];
    [ObservableProperty] private IReadOnlyList<BuilderAction> _actions = [];
    [ObservableProperty] private IReadOnlyList<BuilderAction> _children = [];
    [ObservableProperty] private IReadOnlyList<Choice<string>> _saved = [];
    [ObservableProperty] private IReadOnlyList<Choice<string>> _templates = [];
    [ObservableProperty] private IReadOnlyList<DonutItem> _donutItems = [];
    [ObservableProperty] private string _activeStatus = "";
    [ObservableProperty] private string _draftStatus = "";
    [ObservableProperty] private string _faceStatus = "";

    public DonutFace Face => EditingBack ? DonutFace.Back : DonutFace.Front;
    public bool CanUse => _workspace.Draft.Profile.PresetIds.Concat(_workspace.Draft.Profile.BackPresetIds ?? [])
        .Any(id => _workspace.Draft.Presets.Any(p => p.Id == id && (!p.IsGroup || p.SubPresetIds.Count > 0)));
    public bool CanAdd => SelectedCatalog is not null && Actions.Count < ToolbarProfile.MaxSlices;
    public bool HasSavedSelection => SelectedSaved is not null;
    public bool HasActionSelection => SelectedAction is not null;
    public bool CanMoveUp => SelectedAction is { } selected && Actions.ToList().FindIndex(a => a.Id == selected.Id) > 0;
    public bool CanMoveDown => SelectedAction is { } selected && Actions.ToList().FindIndex(a => a.Id == selected.Id) is var i && i >= 0 && i < Actions.Count - 1;

    partial void OnShowMoreFormatsChanged(bool value) => Refresh();
    partial void OnSearchChanged(string value) => RefreshCatalog();
    partial void OnCategoryChanged(Choice<PaletteTab> value) => RefreshCatalog();
    partial void OnEditingBackChanged(bool value) => Refresh();
    partial void OnSelectedCatalogChanged(BuilderAction? value) => OnPropertyChanged(nameof(CanAdd));
    partial void OnSelectedSavedChanged(Choice<string>? value) => OnPropertyChanged(nameof(HasSavedSelection));
    partial void OnSelectedActionChanged(BuilderAction? value)
    {
        var draft = _workspace.Draft;
        var group = draft.Presets.FirstOrDefault(p => p.Id == value?.Id);
        Children = (group?.SubPresetIds ?? []).Select(id => draft.Presets.FirstOrDefault(p => p.Id == id))
            .OfType<Preset>().Select(Item).ToList();
        OnPropertyChanged(nameof(HasActionSelection));
        OnPropertyChanged(nameof(CanMoveUp));
        OnPropertyChanged(nameof(CanMoveDown));
    }

    public void AddAction(string catalogId) => Run(() =>
    {
        var id = _workspace.AddAction(catalogId, Face);
        SelectedAction = Actions.FirstOrDefault(a => a.Id == id);
    });

    [RelayCommand] private void Add() { if (SelectedCatalog is { } item) AddAction(item.Id); }
    [RelayCommand] private void Use() => Run(_workspace.UseDraft);
    [RelayCommand]
    private void SaveAs() => Run(() =>
    {
        var id = _workspace.SaveAs(Name);
        SelectedSaved = Saved.First(s => s.Value == id);
    });
    [RelayCommand] private void UpdateSaved() { if (SelectedSaved is { } s) Run(() => _workspace.UpdateSaved(s.Value)); }
    [RelayCommand] private void Rename() { if (SelectedSaved is { } s) Run(() => _workspace.Rename(s.Value, Name)); }
    [RelayCommand] private void Duplicate() { if (SelectedSaved is { } s) Run(() => _workspace.Duplicate(s.Value, Name)); }
    [RelayCommand] private void Activate() { if (SelectedSaved is { } s) Run(() => _workspace.Activate(s.Value)); }
    [RelayCommand] private void Delete() { if (SelectedSaved is { } s) Run(() => _workspace.Delete(s.Value)); }
    [RelayCommand] private void LoadSaved() { if (SelectedSaved is { } s) Run(() => _workspace.LoadSaved(s.Value)); }
    [RelayCommand] private void LoadTemplate() { if (SelectedTemplate is { } s) Run(() => _workspace.LoadTemplate(s.Value, ShowMoreFormats)); }
    [RelayCommand] private void Discard() => Run(_workspace.DiscardDraft);
    [RelayCommand] private void Reset() => Run(_workspace.ResetDraft);
    [RelayCommand] private void Front() => EditingBack = false;
    [RelayCommand] private void Back() => EditingBack = true;
    [RelayCommand] private void Remove() { if (SelectedAction is { } a) Run(() => _workspace.RemoveAction(a.Id, Face)); }
    [RelayCommand] private void MoveUp() => Move(-1);
    [RelayCommand] private void MoveDown() => Move(1);
    private void Move(int offset)
    {
        if (SelectedAction is not { } a) return;
        var index = Actions.ToList().FindIndex(item => item.Id == a.Id) + offset;
        if (index >= 0 && index < Actions.Count) Run(() => _workspace.MoveAction(a.Id, Face, index));
    }

    /// <summary>Uses the existing advanced editor against detached recipes, including group membership/order.</summary>
    [RelayCommand]
    private async Task EditAction(BuilderAction? action)
    {
        action ??= SelectedAction;
        var draft = _workspace.Draft;
        if (draft.Presets.FirstOrDefault(p => p.Id == action?.Id) is not { } recipe) return;
        var copy = recipe.Clone();
        if (await _windows.EditDraftActionAsync(copy, draft.Presets))
            Run(() => _workspace.ConfigureAction(copy.Id, p => CopyRecipe(copy, p)));
    }

    [RelayCommand]
    private async Task NewAction()
    {
        var preset = new Preset { Name = _loc["toolbar.new_preset"], TargetFormat = "png" };
        if (await _windows.EditPresetAsync(preset, _windows.Main)) AddTemplate(preset);
    }
    [RelayCommand]
    private async Task NewGroup()
    {
        var group = new Preset { Name = _loc["toolbar.new_group"], Kind = PresetKind.Group, TargetFormat = Preset.GroupTarget };
        if (await _windows.EditGroupAsync(group, _windows.Main, isNew: true)) AddTemplate(group);
    }
    private void AddTemplate(Preset preset) => Run(() =>
    {
        _store.Presets.Add(preset);
        _store.SaveLibrary();
        if (Actions.Count < ToolbarProfile.MaxSlices) _workspace.AddAction(preset.Id, Face);
    });

    internal static void CopyRecipe(Preset source, Preset target)
    {
        // ConfigureAction owns ID validation; copy the complete existing editor result, including advanced options.
        target.Name = source.Name; target.NameKey = source.NameKey; target.Kind = source.Kind;
        target.TargetFormat = source.TargetFormat; target.SubPresetIds = [.. source.SubPresetIds];
        target.Image = source.Image.Clone(); target.Pdf = source.Pdf.Clone(); target.Document = source.Document.Clone();
        target.Media = source.Media.Clone(); target.Archive = source.Archive.Clone(); target.Trace = source.Trace.Clone();
        target.Cad = source.Cad.Clone(); target.Output = source.Output.Clone();
        target.Tool = source.Tool;
    }

    private void Run(Action operation)
    {
        try { operation(); Message = null; }
        catch (ArgumentException) { Message = _loc["builder.invalid_name"]; }
        catch (InvalidOperationException) { Message = _loc["builder.invalid_layout"]; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Message = _loc["builder.save_error"]; }
    }
    private void Changed(object? sender, EventArgs e) => Refresh();
    private BuilderAction Item(Preset p) => new(p.Id, _loc.DisplayName(p), p.IsGroup
        ? _loc.Format("donut.group_caption", p.SubPresetIds.Count) : FormatLabels.NameOf(_loc, p.TargetFormat));

    private void RefreshCatalog()
    {
        var selected = SelectedCatalog?.Id;
        var beginnerIds = BuiltInData.CreatePrimaryProfiles().SelectMany(p => p.PresetIds.Concat(p.BackPresetIds ?? []))
            .Concat(["to-xlsx", "to-csv", "zip-all"]).ToHashSet();
        var builtInIds = BuiltInData.CreatePresets().Select(p => p.Id).ToHashSet();
        Catalog = _store.Presets.Where(p => (ShowMoreFormats || beginnerIds.Contains(p.Id)
                || !builtInIds.Contains(p.Id)) && (Category.Value == PaletteTab.All || ToolbarPageViewModel.TabOf(p) == Category.Value)
                && (_loc.DisplayName(p).Contains(Search.Trim(), StringComparison.CurrentCultureIgnoreCase)
                    || p.TargetFormat.Contains(Search.Trim(), StringComparison.OrdinalIgnoreCase)))
            .OrderBy(p => !p.IsGroup).Select(Item).ToList();
        SelectedCatalog = Catalog.FirstOrDefault(p => p.Id == selected);
    }
    private void Refresh()
    {
        var draft = _workspace.Draft;
        var active = _workspace.Active;
        var selection = SelectedAction?.Id;
        var savedId = SelectedSaved?.Value;
        var templateId = SelectedTemplate?.Value;
        Saved = _workspace.Saved.Select(s => new Choice<string>(s.Id, s.Name)).ToList();
        Templates = _workspace.TemplatesFor(ShowMoreFormats).Select(s => new Choice<string>(s.Id, _loc.DisplayName(s.Snapshot.Profile))).ToList();
        SelectedSaved = Saved.FirstOrDefault(s => s.Value == savedId);
        SelectedTemplate = Templates.FirstOrDefault(s => s.Value == templateId);
        var ids = EditingBack ? draft.Profile.BackPresetIds ?? [] : draft.Profile.PresetIds;
        Actions = ids.Select(id => draft.Presets.First(p => p.Id == id)).Select(Item).ToList();
        SelectedAction = Actions.FirstOrDefault(a => a.Id == selection);
        DonutItems = ids.Select(id => draft.Presets.First(p => p.Id == id)).Select(p => new DonutItem
        {
            Id = p.Id,
            Label = _loc.DonutName(p),
            Caption = Item(p).Caption,
            Children = p.IsGroup ? p.SubPresetIds.Select(id => draft.Presets.First(p => p.Id == id))
                .Select(c => new DonutItem { Id = c.Id, Label = _loc.RingName(c) }).ToList() : null,
        }).ToList();
        ActiveStatus = active is null ? _loc["builder.no_active"]
            : _loc.Format("builder.active", _workspace.Saved.FirstOrDefault(s => s.Id == active.SavedPresetId)?.Name ?? _loc["builder.unsaved_donut"]);
        if (active is not null && _workspace.Saved.FirstOrDefault(s => s.Id == active.SavedPresetId) is { } activeSource
            && Fingerprint(activeSource.Snapshot) != Fingerprint(active))
            ActiveStatus += " · " + _loc["builder.active_changed"];
        var source = _workspace.Saved.FirstOrDefault(s => s.Id == draft.SavedPresetId);
        DraftStatus = source is not null && Fingerprint(source.Snapshot) == Fingerprint(draft)
            ? _loc.Format("builder.saved_draft", source.Name) : _loc["builder.unsaved_draft"];
        if (active is not null && Fingerprint(active) == Fingerprint(draft)) DraftStatus += " · " + _loc["builder.applied"];
        FaceStatus = _loc.Format("builder.face_count", _loc[EditingBack ? "toolbar.face_back" : "toolbar.face_front"], ids.Count, ToolbarProfile.MaxSlices);
        RefreshCatalog();
        OnPropertyChanged(nameof(CanUse)); OnPropertyChanged(nameof(CanAdd));
        OnPropertyChanged(nameof(CanMoveUp)); OnPropertyChanged(nameof(CanMoveDown));
    }
    private static string Fingerprint(DonutSnapshot snapshot)
    {
        var copy = snapshot.Clone();
        var ids = copy.Presets.Select((p, i) => (p.Id, Value: i.ToString())).ToDictionary(x => x.Id, x => x.Value);
        foreach (var p in copy.Presets) { p.Id = ids[p.Id]; p.SubPresetIds = p.SubPresetIds.Select(id => ids[id]).ToList(); }
        copy.Profile.Id = ""; copy.Profile.Name = ""; copy.Profile.NameKey = null; copy.SavedPresetId = null;
        copy.Profile.PresetIds = copy.Profile.PresetIds.Select(id => ids[id]).ToList();
        copy.Profile.BackPresetIds = copy.Profile.BackPresetIds?.Select(id => ids[id]).ToList();
        return JsonSerializer.Serialize(copy, BuilderJsonContext.Default.DonutSnapshot);
    }
    public void Dispose() { _workspace.Changed -= Changed; _store.LibraryChanged -= Changed; }
}

[JsonSerializable(typeof(DonutSnapshot))]
internal sealed partial class BuilderJsonContext : JsonSerializerContext;
