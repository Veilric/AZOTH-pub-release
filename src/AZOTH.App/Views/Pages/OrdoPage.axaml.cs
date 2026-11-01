using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using AZOTH.App.Services;
using AZOTH.App.ViewModels.Pages;
using AZOTH.Core.Localization;
namespace AZOTH.App.Views.Pages;

public partial class OrdoPage : UserControl
{
    public OrdoPage() => InitializeComponent();
    private async void OnPickSource(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not OrdoPageViewModel model || model.Workspace.IsBusy || TopLevel.GetTopLevel(this) is not { } top) return;
        var folders = await top.StorageProvider.OpenFolderPickerAsync(new() { Title = AppHost.Get<ILocalizer>()["ordo.source"], AllowMultiple = false });
        if (!model.Workspace.IsBusy && folders.FirstOrDefault()?.TryGetLocalPath() is { } path) model.Workspace.SourceRoot = path;
    }
    private async void OnPickDestination(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not OrdoPageViewModel model || model.Workspace.IsBusy ||
            sender is not Control { DataContext: OrdoRuleEditor rule } || TopLevel.GetTopLevel(this) is not { } top) return;
        var folders = await top.StorageProvider.OpenFolderPickerAsync(new() { Title = AppHost.Get<ILocalizer>()["ordo.pick_destination"], AllowMultiple = false });
        if (!model.Workspace.IsBusy && model.Workspace.Rules.Contains(rule) && folders.FirstOrDefault()?.TryGetLocalPath() is { } path)
            rule.Destination = path;
    }
}
