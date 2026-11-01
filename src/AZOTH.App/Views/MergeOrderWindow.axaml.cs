using Avalonia.Controls;
using Avalonia.Interactivity;
using AZOTH.App.Services;
using AZOTH.App.ViewModels;

namespace AZOTH.App.Views;

public partial class MergeOrderWindow : Window
{
    public MergeOrderWindow()
    {
        InitializeComponent();
        Motion.Track(this);
        UiZoom.Track(this);
    }

    public IReadOnlyList<string>? Result { get; private set; }

    private void OnMerge(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MergeOrderViewModel vm || vm.Files.Count < 2) return;
        Result = vm.OrderedFiles;
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();
}
