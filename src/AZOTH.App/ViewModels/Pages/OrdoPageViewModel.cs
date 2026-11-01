using AZOTH.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
namespace AZOTH.App.ViewModels.Pages;
/// <summary>Only the page attachment is transient; operations and history belong to the app.</summary>
public sealed class OrdoPageViewModel(OrdoWorkspace workspace) : ObservableObject
{
    public OrdoWorkspace Workspace { get; } = workspace;
}
