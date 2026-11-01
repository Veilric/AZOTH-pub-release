// Composition root: every long-lived service is created here. Views get services through AppHost.Get<T>().

using AZOTH.App.Services.Triggers;
using AZOTH.App.ViewModels;
using AZOTH.Core.Conversion;
using AZOTH.Core.Donuts;
using AZOTH.Core.Echo;
using AZOTH.Core.Files;
using AZOTH.Core.Localization;
using AZOTH.Core.Ordo;
using AZOTH.Core.Platform;
using AZOTH.Core.Settings;
using AZOTH.Core.Web;
using AZOTH.Engines;
using AZOTH.Engines.Infrastructure;
using AZOTH.Platform.Linux;
using AZOTH.Platform.MacOS;
using AZOTH.Platform.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AZOTH.App.Services;

public static class AppHost
{
    private static IServiceProvider? _services;

    public static IServiceProvider Services => _services ?? throw new InvalidOperationException("AppHost not built.");

    public static T Get<T>() where T : notnull => Services.GetRequiredService<T>();

    public static IServiceProvider Build(string? dataDirectory = null, UserDataStore? preloadedStore = null)
    {
        var dataDir = dataDirectory ?? UserDataStore.DefaultDirectory;
        var services = new ServiceCollection();

        services.AddLogging(b => b
            .AddProvider(new FileLoggerProvider(Path.Combine(dataDir, "logs")))
            .SetMinimumLevel(LogLevel.Information));

        services.AddSingleton(sp => preloadedStore ?? new UserDataStore(dataDir, sp.GetRequiredService<ILogger<UserDataStore>>()));
        services.AddSingleton<IPlatformServices>(_ =>
            OperatingSystem.IsWindows() ? new WindowsPlatformServices()
            : OperatingSystem.IsMacOS() ? new MacOSPlatformServices()
            : OperatingSystem.IsLinux() ? new LinuxPlatformServices()
            : new NullPlatformServices());

        services.AddSingleton<IFileMetadata>(_ =>
            OperatingSystem.IsWindows() ? new WindowsFileMetadata()
            : OperatingSystem.IsMacOS() ? new MacOSFileMetadata()
            : OperatingSystem.IsLinux() ? new LinuxFileMetadata()
            : new UnsupportedFileMetadata());
        services.AddSingleton<FileFingerprintService>();
        // Shared by ECHO cleanup and all ORDO move/undo operations.
        services.AddSingleton<FileMutationCoordinator>();
        services.AddSingleton<EchoService>();
        services.AddSingleton(sp => new EchoWorkspace(
            sp.GetRequiredService<EchoService>(), sp.GetRequiredService<IPlatformServices>(),
            sp.GetRequiredService<ILocalizer>()));
        services.AddTransient(sp => new AZOTH.App.ViewModels.Pages.EchoPageViewModel(
            sp.GetRequiredService<EchoWorkspace>()));

        services.AddSingleton<OrdoSourceClaims>();
        services.AddSingleton(sp => new OrdoJournal(Path.Combine(sp.GetRequiredService<UserDataStore>().Directory, "ordo-history")));
        services.AddSingleton(sp => new OrdoPlanner(sp.GetRequiredService<IFileMetadata>(),
            sp.GetRequiredService<FileFingerprintService>(), [sp.GetRequiredService<UserDataStore>().Directory]));
        services.AddSingleton(sp => new OrdoService(sp.GetRequiredService<IFileMetadata>(),
            sp.GetRequiredService<FileFingerprintService>(), sp.GetRequiredService<FileMutationCoordinator>(), sp.GetRequiredService<OrdoJournal>()));
        services.AddSingleton(sp => new OrdoMonitor(sp.GetRequiredService<IFileMetadata>(), sp.GetRequiredService<OrdoPlanner>(),
            sp.GetRequiredService<OrdoService>(), sp.GetRequiredService<OrdoJournal>(),
            Path.Combine(sp.GetRequiredService<UserDataStore>().Directory, "ordo-monitor.json"), sp.GetRequiredService<OrdoSourceClaims>()));
        services.AddSingleton<OrdoWorkspace>();

        services.AddSingleton<LocalizationService>();
        services.AddSingleton<ILocalizer>(sp => sp.GetRequiredService<LocalizationService>());
        services.AddSingleton<ThemeService>();
        services.AddSingleton<SystemIntegrationService>();
        services.AddSingleton<ElevationService>();

        // Conversion engines
        services.AddSingleton(_ => new EngineEnvironment(dataDir));
        services.AddSingleton(sp => EngineRegistry.CreateAll(sp.GetRequiredService<EngineEnvironment>(), sp.GetRequiredService<ILoggerFactory>()));
        services.AddSingleton(sp => new ConverterCatalog(sp.GetRequiredService<IReadOnlyList<IConverter>>()));
        services.AddSingleton(sp => new JobQueue(
            sp.GetRequiredService<ConverterCatalog>(),
            EngineRegistry.FindPdfMerger(sp.GetRequiredService<IReadOnlyList<IConverter>>()),
            sp.GetRequiredService<ILogger<JobQueue>>(),
            EngineRegistry.FindFileCombiner(sp.GetRequiredService<IReadOnlyList<IConverter>>()),
            EngineRegistry.FindTiffMerger(sp.GetRequiredService<IReadOnlyList<IConverter>>()),
            EngineRegistry.FindMediaJoiner(sp.GetRequiredService<IReadOnlyList<IConverter>>()),
            sp.GetRequiredService<IPlatformServices>().MoveToTrash));

        // Files dragged from a browser with only an address are downloaded without the browser's cookies.
        services.AddSingleton(_ => new WebDownloader(userAgent: $"AZOTH/{typeof(AppHost).Assembly.GetName().Version?.ToString(3)}"));
        services.AddSingleton<EngineDownloadService>();
        services.AddSingleton<PresetAvailability>();
        // Lazy: first resolved after App.OnFrameworkInitializationCompleted has loaded the store.
        services.AddSingleton<DonutWorkspace>();
        services.AddSingleton<RadialViewModel>();
        services.AddSingleton<ConversionService>();
        services.AddSingleton<SpreadsheetPrintService>();
        services.AddSingleton<WatchFolderService>();
        services.AddSingleton<TriggerService>();
        services.AddSingleton<RadialController>();
        services.AddSingleton<TrayService>();
        services.AddSingleton<UpdateService>();
        services.AddSingleton<MemoryTrimmer>();
        services.AddSingleton<WindowService>();
        services.AddSingleton<WhatsNewService>();

        _services = services.BuildServiceProvider();
        return _services;
    }
}
