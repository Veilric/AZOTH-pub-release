using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using AZOTH.App.ViewModels.Pages;

namespace AZOTH.App.Views.Pages;

public partial class DonutBuilderPage : UserControl
{
    public DonutBuilderPage()
    {
        InitializeComponent();
        CatalogList.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key != Key.Enter || DataContext is not DonutBuilderPageViewModel vm || !vm.CanAdd) return;
            vm.AddCommand.Execute(null); e.Handled = true;
        }, RoutingStrategies.Tunnel);
        CatalogList.DoubleTapped += (_, _) =>
        {
            if (DataContext is DonutBuilderPageViewModel { CanAdd: true } vm) vm.AddCommand.Execute(null);
        };
        ActionList.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (DataContext is not DonutBuilderPageViewModel vm) return;
            if (e.Key == Key.Delete) vm.RemoveCommand.Execute(null);
            else if (e.Key == Key.Up && e.KeyModifiers.HasFlag(KeyModifiers.Alt)) vm.MoveUpCommand.Execute(null);
            else if (e.Key == Key.Down && e.KeyModifiers.HasFlag(KeyModifiers.Alt)) vm.MoveDownCommand.Execute(null);
            else if (e.Key == Key.Enter) vm.EditActionCommand.Execute(null);
            else return;
            e.Handled = true;
        }, RoutingStrategies.Tunnel);
        Preview.SegmentContextRequested += (_, index) =>
        {
            if (DataContext is DonutBuilderPageViewModel vm && index >= 0 && index < vm.Actions.Count)
                vm.EditActionCommand.Execute(vm.Actions[index]);
        };
    }
}
