using Avalonia;
using Avalonia.Headless;
using AZOTH.App;
using AZOTH.App.Services;
using AZOTH.Core.Settings;

[assembly: AvaloniaTestApplication(typeof(AZOTH.App.Tests.TestAppBuilder))]
// One application for all tests. Per test, Avalonia disposes the font manager and loads the embedded Noto fonts
// again, and on Windows the test process then crashed with 0xC0000005 (only builds with the fonts, i.e. releases).
[assembly: AvaloniaTestIsolation(AvaloniaTestIsolationLevel.PerAssembly)]

namespace AZOTH.App.Tests;

/// <summary>Headless Avalonia with real Skia rendering so views can be captured as images.</summary>
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseSkia()
            .UseHarfBuzz()
            .ConfigureFonts(fonts => fonts.AddFontCollection(new AZOTHFontCollection()))
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

/// <summary>Builds the service container once against a throw-away data folder (never touches %APPDATA%).</summary>
internal static class TestServices
{
    private static Application? _initializedFor;

    public static string DataDirectory { get; } =
        Path.Combine(Path.GetTempPath(), "azoth-app-tests", Guid.NewGuid().ToString("N")[..8]);

    /// <summary>
    /// The headless runner may create a new Application per test, so services (and the localization
    /// dictionary attached to Application.Resources) are rebuilt whenever the application changes.
    /// </summary>
    public static void EnsureInitialized(string language = "en")
    {
        var app = Application.Current!;
        if (!ReferenceEquals(_initializedFor, app))
        {
            AppHost.Build(DataDirectory);
            AppHost.Get<UserDataStore>().Load();
            AppHost.Get<WhatsNewService>().ResourceAssembly = "AZOTH.App.Tests";
            AppHost.Get<LocalizationService>().Attach(app);
            _initializedFor = app;
        }

        var store = AppHost.Get<UserDataStore>();
        store.Settings.Language = language;
        AppHost.Get<LocalizationService>().SetLanguage(language);
        AppHost.Get<ThemeService>().Apply(store.Settings.Theme, language);
    }

    // Explicit fixtures for historical rendering/gesture tests. Real-app tests keep the owned empty state.
    private static readonly Dictionary<string, string> LegacyExecutionIds = [];
    public static void EnsureLegacyInitialized(string language = "en")
    {
        EnsureInitialized(language);
        var store = AppHost.Get<UserDataStore>();
        if (store.Profiles.Count == 0) store.Profiles.AddRange(AZOTH.Core.Presets.BuiltInData.CreateProfiles());
        ActivateLegacyProfile(store.Profiles.First(p => p.Id == "images"));
    }

    public static void LoadLegacy(AZOTH.App.ViewModels.RadialViewModel model, IReadOnlyList<string> files, bool clickMode, bool backFace = false)
    {
        var store = AppHost.Get<UserDataStore>();
        var profile = AZOTH.Core.Profiles.ProfileSelector.Select(store.Profiles, files);
        if (profile is not null) ActivateLegacyProfile(profile);
        else AppHost.Get<AZOTH.Core.Donuts.DonutWorkspace>().ResetWorkspace();
        model.Load(files, clickMode, backFace);
    }

    public static string LegacyExecutionId(string id) => LegacyExecutionIds.GetValueOrDefault(id, id);

    public static void ActivateLegacyProfile(AZOTH.Core.Profiles.ToolbarProfile profile)
    {
        var workspace = AppHost.Get<AZOTH.Core.Donuts.DonutWorkspace>();
        var store = AppHost.Get<UserDataStore>();
        workspace.ResetDraft();
        LegacyExecutionIds.Clear();
        foreach (var id in profile.PresetIds.Where(id => store.FindPreset(id) is not null))
            workspace.AddAction(id);
        foreach (var id in (profile.BackPresetIds ?? []).Where(id => store.FindPreset(id) is not null))
            workspace.AddAction(id, AZOTH.Core.Donuts.DonutFace.Back);
        if (workspace.Draft.Profile.PresetIds.Count + (workspace.Draft.Profile.BackPresetIds?.Count ?? 0) == 0) return;
        workspace.UseDraft();
        var active = workspace.Active!;
        void Map(IEnumerable<string> originals, IEnumerable<string> runtime)
        {
            foreach (var (original, running) in originals.Where(id => store.FindPreset(id) is not null).Zip(runtime))
            {
                LegacyExecutionIds[original] = running;
                var source = store.FindPreset(original)!;
                var target = active.Presets.First(p => p.Id == running);
                foreach (var (child, runtimeChild) in source.SubPresetIds.Zip(target.SubPresetIds)) LegacyExecutionIds.TryAdd(child, runtimeChild);
            }
        }
        Map(profile.PresetIds, active.Profile.PresetIds);
        Map(profile.BackPresetIds ?? [], active.Profile.BackPresetIds ?? []);
    }

    /// <summary>Where rendered screenshots go: tests/AZOTH.App.Tests/bin/.../screenshots.</summary>
    public static string ScreenshotDirectory
    {
        get
        {
            var dir = Path.Combine(AppContext.BaseDirectory, "screenshots");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }
}
