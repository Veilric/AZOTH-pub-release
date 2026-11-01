using AZOTH.App.ViewModels;
using AZOTH.Core.Ordo;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AZOTH.App.Services;

/// <summary>Editable fields for one ordered rule; workspace commits them only through a reviewed preview.</summary>
public sealed partial class OrdoRuleEditor : ObservableObject
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    [ObservableProperty] private OrdoFileType _type = OrdoFileType.Any;
    [ObservableProperty] private string _extension = "";
    [ObservableProperty] private string _destination = "Organized";
    [ObservableProperty] private string _modifiedFrom = "";
    [ObservableProperty] private string _modifiedBefore = "";
    [ObservableProperty] private bool _groupByMonth;
    private IReadOnlyList<Choice<OrdoFileType>> _typeChoices = [];
    public IReadOnlyList<Choice<OrdoFileType>> TypeChoices
    {
        get => _typeChoices;
        internal set { _typeChoices = value; OnPropertyChanged(); OnPropertyChanged(nameof(SelectedType)); }
    }
    public Choice<OrdoFileType>? SelectedType
    {
        get => TypeChoices.FirstOrDefault(c => c.Value == Type);
        set { if (value is not null) Type = value.Value; }
    }
    partial void OnTypeChanged(OrdoFileType value) => OnPropertyChanged(nameof(SelectedType));
    public IRelayCommand? MoveUpCommand { get; internal set; }
    public IRelayCommand? MoveDownCommand { get; internal set; }
    public IRelayCommand? RemoveCommand { get; internal set; }
}
