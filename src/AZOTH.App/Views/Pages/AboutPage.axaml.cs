using Avalonia.Controls;
using Avalonia.Interactivity;
using AZOTH.App.Services;
using AZOTH.App.ViewModels.Pages;

namespace AZOTH.App.Views.Pages;

public partial class AboutPage : UserControl
{
    public AboutPage() => InitializeComponent();

    private async void OnOpenRepository(object? sender, RoutedEventArgs e)
    {
        if (DataContext is AboutPageViewModel vm && TopLevel.GetTopLevel(this) is { } top)
            await top.Launcher.LaunchUriAsync(new Uri(vm.RepositoryUrl));
    }

    private async void OnOpenUpstream(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is { } top)
            await top.Launcher.LaunchUriAsync(new Uri(UpdateService.UpstreamUrl));
    }
}
