using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using AZOTH.App.ViewModels.Pages;

namespace AZOTH.App.Views.Pages;

public partial class EchoPage : UserControl
{
    public EchoPage() => InitializeComponent();

    private async void OnPickFolders(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not EchoPageViewModel { IsBusy: false } model || TopLevel.GetTopLevel(this) is not { } top) return;
        var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            AllowMultiple = true,
            Title = AZOTH.App.Services.AppHost.Get<AZOTH.Core.Localization.ILocalizer>()["echo.pick"],
        });
        if (model.IsBusy || !ReferenceEquals(DataContext, model)) return;
        var additions = folders.Select(folder => folder.TryGetLocalPath()).OfType<string>();
        model.RootsText = string.Join(Environment.NewLine,
            model.RootsText.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Concat(additions).Distinct(AZOTH.Core.Platform.FileSystemPaths.Comparer));
    }
}
