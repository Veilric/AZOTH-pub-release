// Usage text of the "azoth" command.

using System.Reflection;
using AZOTH.Core.Watching;

namespace AZOTH.Cli;

internal static class Help
{
    public static string Version =>
        typeof(Help).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "?";

    public static string Text(string? command) => command?.ToLowerInvariant() switch
    {
        "convert" => Convert,
        "watch" => Watch,
        "formats" => "Usage: azoth formats [<format>] [--json]\n\nLists every format, or what one format converts to.\n" +
                     "With --json, prints the same as JSON (for scripts and other programs).\n",
        "presets" => "Usage: azoth presets\n\nLists the presets saved in AZOTH (built-in and your own).\n",
        _ => General,
    };

    private const string General = """
        AZOTH: convert files offline from the command line.

        Usage:
          azoth convert <files or folders...> --to <format> [options]
          azoth convert <files or folders...> --preset <preset> [options]
          azoth watch <folder> --to <format> [options]
          azoth formats [<format>] [--json]
          azoth presets
          azoth help [<command>]
          azoth version

        Examples:
          azoth convert photo.heic --to jpg
          azoth convert *.png --to webp --quality 80 -o converted
          azoth convert D:\Scans --recursive --preset to-pdf
          azoth watch D:\Inbox --to pdf --move-originals

        Presets and engines are the ones in the AZOTH app. Exit codes: 0 all converted, 1 some failed,
        2 wrong arguments or a missing input, 3 nothing to convert.

        """;

    private const string Convert = """
        Usage: azoth convert <files or folders...> (--to <format> | --preset <preset>) [options]

        Files can be wildcards (*.heic). Folders convert the files in them that AZOTH knows.

        Options:
          -t, --to <format>      Target format, e.g. jpg, png, pdf, docx, mp4 (see 'azoth formats')
          -p, --preset <preset>  A preset from the app, by id or name (see 'azoth presets')
          -o, --output <folder>  Save there instead of next to the sources
          -r, --recursive        Include sub folders
          -q, --quality <1-100>  Image quality for JPG, WebP, AVIF, …
              --overwrite        Replace existing output files (default: add a number)
              --skip             Leave existing output files alone
              --keep-dates       Give converted files the created and modified dates of the originals
              --zip              Save the result of each file as one ZIP (a Shapefile's files together)
              --json             Print the result as JSON (for scripts and other programs)
              --quiet            Print nothing but errors

        """;

    private static string Watch => FormattableString.Invariant($"""
        Usage: azoth watch <folder> (--to <format> | --preset <preset>) [options]

        Converts files that land in the folder until Ctrl+C. Files are converted once they are complete (no
        longer growing or open in another program). Output goes to a "converted" folder inside it unless -o is given.

        Options:
          -t, --to <format>      Target format
          -p, --preset <preset>  A preset from the app, by id or name
          -o, --output <folder>  Where converted files go (default: <folder>\converted)
          -r, --recursive        Also watch sub folders (their files keep the sub folder in the output)
              --move-originals   Move converted sources to <folder>\originals; files already waiting are
                                 converted at start
              --settle <seconds> How long a file must stay unchanged (default {FolderWatcher.DefaultSettleTime.TotalSeconds})
              --keep-dates       Give converted files the created and modified dates of the originals
              --zip              Save the result of each file as one ZIP (a Shapefile's files together)
              --quiet            Print nothing but errors

        """);
}
