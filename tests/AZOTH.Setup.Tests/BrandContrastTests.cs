using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;

namespace AZOTH.Setup.Tests;

public class BrandContrastTests
{
    [AvaloniaFact]
    public void Installer_links_have_readable_contrast_in_both_themes()
    {
        App.ApplyLook(Application.Current!, new SetupTexts("en"));
        var window = new Window();
        foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            Assert.True(window.TryFindResource("AccentBrush", variant, out var accent));
            Assert.True(window.TryFindResource("SurfaceBrush", variant, out var surface));
            var text = Luminance(Assert.IsAssignableFrom<ISolidColorBrush>(accent).Color);
            var background = Luminance(Assert.IsAssignableFrom<ISolidColorBrush>(surface).Color);
            var contrast = (Math.Max(text, background) + 0.05) / (Math.Min(text, background) + 0.05);
            Assert.True(contrast >= 4.5, $"Installer link contrast in {variant}: {contrast:0.00}:1");
        }
    }

    private static double Luminance(Color color)
    {
        static double Channel(byte value) => value / 255.0 <= 0.04045
            ? value / 255.0 / 12.92 : Math.Pow((value / 255.0 + 0.055) / 1.055, 2.4);
        return 0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);
    }
}
