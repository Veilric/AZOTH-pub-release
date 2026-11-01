using Avalonia.Controls;
using Avalonia.Interactivity;
using AZOTH.App.Services;
using AZOTH.Core.Settings;

namespace AZOTH.App.Views;

/// <summary>
/// "Keep AZOTH running?" when the main window closes. <c>ShowDialog</c> returns <see cref="CloseAction.Tray"/>,
/// <see cref="CloseAction.Quit"/>, or null when cancelled.
/// </summary>
public partial class CloseAskWindow : Window
{
    public CloseAskWindow()
    {
        InitializeComponent();
        Motion.Track(this);
        UiZoom.Track(this);
    }

    /// <summary>"Don't ask again" was ticked: the answer becomes the setting.</summary>
    public bool DontAskAgain => DontAsk.IsChecked == true;

    private void OnTray(object? sender, RoutedEventArgs e) => Close(CloseAction.Tray);

    private void OnQuit(object? sender, RoutedEventArgs e) => Close(CloseAction.Quit);

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);
}
