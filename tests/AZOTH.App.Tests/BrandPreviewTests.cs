using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AZOTH.App.Services;
using AZOTH.App.ViewModels;
using AZOTH.App.Views;
using AZOTH.Core.Localization;
using AZOTH.Core.Settings;

namespace AZOTH.App.Tests;

[Collection("MainWindow")]
public class BrandPreviewTests
{
    [AvaloniaTheory]
    [InlineData(ThemeMode.Light, "home")]
    [InlineData(ThemeMode.Dark, "home")]
    [InlineData(ThemeMode.Light, "about")]
    [InlineData(ThemeMode.Dark, "about")]
    public void Brand_and_resources_render_in_both_themes(ThemeMode mode, string page)
    {
        TestServices.EnsureInitialized("en");
        var themes = AppHost.Get<ThemeService>();
        themes.Apply(new ThemeSettings { Mode = mode, ReduceMotion = true }, "en");
        using var model = new MainWindowViewModel(AppHost.Services, AppHost.Get<ILocalizer>(), AppHost.Get<UpdateService>());
        model.Navigate(page);
        var window = new MainWindow { DataContext = model, Width = 1080, Height = 820 };
        try
        {
            window.Show();
            for (var i = 0; i < 10; i++)
            {
                Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            }

            var labels = window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).ToList();
            Assert.Contains(labels, t => t.Text == "AZOTH");
            Assert.Contains(labels, t => t.Text == "by Veilric");
            Assert.DoesNotContain(labels, t => t.Text == "Filee");
            Assert.Contains(window.GetVisualDescendants().OfType<Image>(), image => image.Source is not null);
            RenderTests.Save(window, $"azoth-{page}-{mode.ToString().ToLowerInvariant()}.png");
        }
        finally
        {
            window.Close();
            themes.Apply(new ThemeSettings(), "en");
        }
    }
}
