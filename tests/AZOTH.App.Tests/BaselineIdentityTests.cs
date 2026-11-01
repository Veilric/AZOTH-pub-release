using System.Net;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using AZOTH.App.Controls;
using AZOTH.App.Services;
using AZOTH.App.ViewModels.Pages;
using AZOTH.Core.Localization;
using AZOTH.Core.Settings;
using Microsoft.Extensions.Logging.Abstractions;

namespace AZOTH.App.Tests;

public class BaselineIdentityTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Unconfigured_update_checks_never_contact_a_release_server(bool savedPreference)
    {
        TestServices.EnsureInitialized("en");
        var handler = new RecordingNetwork();
        var updates = new UpdateService(NullLogger<UpdateService>.Instance, handler);
        var folder = Path.Combine(Path.GetTempPath(), "azoth-baseline-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new UserDataStore(folder);
            store.Settings.CheckForUpdates = savedPreference;
            store.SaveSettings();
            var reloaded = new UserDataStore(folder);
            reloaded.Load();

            await updates.CheckAsync(TestContext.Current.CancellationToken);
            using var about = new AboutPageViewModel(AppHost.Get<ILocalizer>(), reloaded, updates);
            await about.CheckNowCommand.ExecuteAsync(null);

            Assert.Equal(0, handler.Requests);
            Assert.False(updates.IsUpdateAvailable);
        }
        finally
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
    }

    [AvaloniaTheory]
    [InlineData("en")]
    [InlineData("fr")]
    [InlineData("ko")]
    [InlineData("ar")]
    public void Product_identity_and_fallback_preview_strings_are_consistent(string language)
    {
        TestServices.EnsureInitialized(language);
        var loc = AppHost.Get<ILocalizer>();
        Assert.Equal("AZOTH", loc["app.name"]);
        Assert.Equal("by Veilric", loc["app.tagline"]);
        Assert.Equal("Organization Preview", loc["ordo.preview_title"]);
        Assert.Equal("Advanced / More Formats", loc["builder.more_formats"]);
        Assert.Contains("Filee", loc["about.origin_text"]);
        var updates = new UpdateService(NullLogger<UpdateService>.Instance, new RecordingNetwork());
        Assert.False(updates.IsConfigured);
        var folder = Directory.CreateTempSubdirectory("azoth-brand-").FullName;
        try
        {
            using var about = new AboutPageViewModel(loc, new UserDataStore(folder), updates);
            Assert.Equal("https://github.com/Veilric/AZOTH-pub-release", about.RepositoryUrl);
            Assert.Contains("Permission is hereby granted", about.Notices);
        }
        finally { Directory.Delete(folder, true); TestServices.EnsureInitialized("en"); }
    }

    [Fact]
    public void New_settings_do_not_enable_unconfigured_release_checks() =>
        Assert.False(new AppSettings().CheckForUpdates);

    [AvaloniaFact]
    public async Task Unconfigured_update_install_never_downloads_or_starts_an_installer()
    {
        var handler = new RecordingNetwork();
        var started = 0;
        var updates = new UpdateService(NullLogger<UpdateService>.Instance, handler)
        {
            InstallsItself = true,
            StartProcess = _ => { started++; return null; },
        };
        updates.UseRelease(
            new UpdateService.ReleaseFile("AZOTH-9.9.9-win-Setup.exe", "https://example.test/setup.exe", 123),
            new UpdateService.ReleaseFile("AZOTH-9.9.9-SHA256SUMS.txt", "https://example.test/checksums.txt", 100));

        await updates.UpdateAsync();

        Assert.Equal(0, handler.Requests);
        Assert.Equal(0, started);
        Assert.Equal(UpdateStep.Idle, updates.Step);
    }

    [Fact]
    public void An_upstream_windows_installer_marker_does_not_identify_an_azoth_installation()
    {
        var folder = Directory.CreateTempSubdirectory("azoth-upstream-install-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(folder, "unins000.exe"), "upstream installer");
            Assert.False(UpdateService.IsInstalledIn(folder));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [AvaloniaFact]
    public void The_product_does_not_ship_upstream_release_promotions()
    {
        TestServices.EnsureInitialized("en");
        Assert.Empty(SlideDeck.LoadAll("AZOTH"));
    }

    [AvaloniaFact]
    public void Default_accents_are_readable_in_both_themes_and_custom_colors_remain_custom()
    {
        TestServices.EnsureInitialized("en");
        var themes = AppHost.Get<ThemeService>();
        themes.Apply(new ThemeSettings(), "en");
        foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            Assert.True(Application.Current!.TryGetResource("AccentBrush", variant, out var accent));
            Assert.True(Application.Current.TryGetResource("OnAccentBrush", variant, out var foreground));
            var background = Assert.IsAssignableFrom<ISolidColorBrush>(accent).Color;
            var text = Assert.IsAssignableFrom<ISolidColorBrush>(foreground).Color;
            var high = Math.Max(RelativeLuminance(background), RelativeLuminance(text));
            var low = Math.Min(RelativeLuminance(background), RelativeLuminance(text));
            Assert.True((high + 0.05) / (low + 0.05) >= 4.5, $"Insufficient accent contrast in {variant}");
        }

        var custom = new ThemeSettings { Accent = "#116699" };
        Assert.Null(custom.PresetKey);
        themes.Apply(custom, "en");
        Assert.True(Application.Current!.TryGetResource("AccentBrush", ThemeVariant.Light, out var customBrush));
        Assert.Equal(Color.Parse(custom.Accent), Assert.IsAssignableFrom<ISolidColorBrush>(customBrush).Color);
    }

    private static double RelativeLuminance(Color color)
    {
        static double Channel(byte value) => value / 255.0 <= 0.04045
            ? value / 255.0 / 12.92 : Math.Pow((value / 255.0 + 0.055) / 1.055, 2.4);
        return 0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);
    }

    private sealed class RecordingNetwork : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
