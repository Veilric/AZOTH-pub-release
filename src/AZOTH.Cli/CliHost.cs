// The conversion pipeline without the app: the user's settings and presets, every engine (bundled ones next to the
// executable, downloaded ones in the per-user engines folder) and a job queue. Mirrors AppHost in AZOTH.App.

using AZOTH.Core.Conversion;
using AZOTH.Core.Localization;
using AZOTH.Core.Platform;
using AZOTH.Core.Settings;
using AZOTH.Engines;
using AZOTH.Engines.Infrastructure;

namespace AZOTH.Cli;

internal sealed class CliHost : IAsyncDisposable
{
    private CliHost(UserDataStore store, ConverterCatalog catalog, JobQueue queue)
    {
        Store = store;
        Catalog = catalog;
        Queue = queue;
    }

    public UserDataStore Store { get; }
    public ConverterCatalog Catalog { get; }
    public JobQueue Queue { get; }
    public ILocalizer Texts { get; } = new EnglishTexts();

    /// <param name="dataDirectory">Settings folder; the app's (%APPDATA%\AZOTH) when null.</param>
    public static CliHost Create(string? dataDirectory = null)
    {
        var store = new UserDataStore(dataDirectory ?? UserDataStore.DefaultDirectory);
        store.Load();
        EngineEnvironment.OwnCopies = new Dictionary<string, string>(store.Settings.EngineOwnCopies);
        EngineEnvironment.LibreOfficeForSlides = store.Settings.LibreOfficeForSlides;
        EngineEnvironment.LibreOfficeForHwp = store.Settings.LibreOfficeForHwp;
        EngineEnvironment.LibreOfficeForDocuments = store.Settings.LibreOfficeForDocuments;
        EngineEnvironment.LibreOfficeForSheets = store.Settings.LibreOfficeForSheets;
        var converters = EngineRegistry.CreateAll(new EngineEnvironment(store.Directory));
        var catalog = new ConverterCatalog(converters) { Priority = store.Settings.EnginePriority };
        var queue = new JobQueue(catalog, EngineRegistry.FindPdfMerger(converters), combiner: EngineRegistry.FindFileCombiner(converters),
            tiffMerger: EngineRegistry.FindTiffMerger(converters), mediaJoiner: EngineRegistry.FindMediaJoiner(converters),
            moveToTrash: Trash());
        return new CliHost(store, catalog, queue);
    }

    /// <summary>The system's Recycle Bin / Trash for "Replace the original", as in the app; none elsewhere.</summary>
    private static Func<string, TrashRefusal?>? Trash() =>
        OperatingSystem.IsWindows() ? new AZOTH.Platform.Windows.WindowsPlatformServices().MoveToTrash
        : OperatingSystem.IsMacOS() ? new AZOTH.Platform.MacOS.MacOSPlatformServices().MoveToTrash
        : OperatingSystem.IsLinux() ? new AZOTH.Platform.Linux.LinuxPlatformServices().MoveToTrash
        : null;

    public ValueTask DisposeAsync() => Queue.DisposeAsync();
}
