using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using AZOTH.App.Services;
using AZOTH.Setup.ViewModels;
using AZOTH.Setup.Views;

namespace AZOTH.Setup;

public sealed class App : Application
{
    /// <summary>The window's model, set by Program before Avalonia starts.</summary>
    internal static SetupViewModel? Model { get; set; }

    /// <summary>Setup's texts, published as resources.</summary>
    internal static SetupTexts? Texts { get; set; }

    /// <summary>/SILENT: start at once, show only the progress, close when done.</summary>
    internal static bool Silent { get; set; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (Texts is { } texts)
            ApplyLook(this, texts);
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && Model is { } model)
        {
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
            desktop.MainWindow = new SetupWindow(model, Silent);
        }
        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// AZOTH's look in <paramref name="texts"/>' language: default accent, radius, Windows' fonts in the language's
    /// order, its reading direction; texts as resources.
    /// </summary>
    internal static void ApplyLook(Application app, SetupTexts texts)
    {
        var res = app.Resources;
        var lightAccent = new ResourceDictionary();
        var darkAccent = new ResourceDictionary();
        SoftPalette.ApplyAccent(lightAccent, Color.Parse(SoftPalette.DefaultAccent));
        SoftPalette.ApplyAccent(darkAccent, Color.Parse("#D6AF68"), isDark: true);
        SoftPalette.ClearTopLevelAccent(res);
        res.ThemeDictionaries[ThemeVariant.Light] = lightAccent;
        res.ThemeDictionaries[ThemeVariant.Dark] = darkAccent;
        SoftPalette.ApplyCornerRadius(res, SoftPalette.DefaultCornerRadius);
        void Apply()
        {
            res["AppFontFamily"] = FontFamily.Parse(SoftPalette.SystemFontFamilies(texts.Language));
            SoftPalette.ApplyFlowDirection(res, texts.Language);
            texts.Publish(res);
        }
        Apply();
        texts.LanguageChanged += (_, _) => Apply();
    }
}
