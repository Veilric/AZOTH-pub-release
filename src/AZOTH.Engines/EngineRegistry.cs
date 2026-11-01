// THE place where conversion engines are registered. Adding an engine = one line in CreateAll.

using AZOTH.Core.Conversion;
using AZOTH.Engines.Archives;
using AZOTH.Engines.Cad;
using AZOTH.Engines.Data;
using AZOTH.Engines.Ebooks;
using AZOTH.Engines.Email;
using AZOTH.Engines.Fonts;
using AZOTH.Engines.Geo;
using AZOTH.Engines.Hwp;
using AZOTH.Engines.Hwp.Hwpx;
using AZOTH.Engines.Icns;
using AZOTH.Engines.Infrastructure;
using AZOTH.Engines.Magick;
using AZOTH.Engines.Media;
using AZOTH.Engines.Models3D;
using AZOTH.Engines.Ocr;
using AZOTH.Engines.Office;
using AZOTH.Engines.Pdf;
using AZOTH.Engines.Subtitles;
using AZOTH.Engines.Text;
using AZOTH.Engines.Vector;
using AZOTH.Engines.Wic;
using Microsoft.Extensions.Logging;

namespace AZOTH.Engines;

/// <summary>Creates every built-in converter.</summary>
public static class EngineRegistry
{
    /// <summary>
    /// Instantiates all converters. Order only matters as a tie-breaker when no priority is set, so it mirrors
    /// AppSettings.EnginePriority: built-in engines first, the optional downloads last. Software installed on the
    /// system (Microsoft Office, a system-wide LibreOffice) is never used.
    /// </summary>
    /// <param name="loggers">
    /// The app's log; FFmpeg writes which hardware encoders work and when one failed, LibreOffice when a file needed
    /// its container repacked.
    /// </param>
    public static IReadOnlyList<IConverter> CreateAll(EngineEnvironment env, ILoggerFactory? loggers = null) =>
    [
        new MagickImageConverter(),
        new PdfSharpConverter(),
        new PdfiumConverter(),
        new RhwpConverter(),
        new UnhwpConverter(),
        new MarkdownConverter(),
        new SpreadsheetConverter(),
        new OoxmlConverter(),
        new HwpxConverter(),
        new ArchiveConverter(),
        new VectorConverter(),
        new IcnsConverter(),
        new FontConverter(),
        new DocxWriterConverter(),
        new PdfTextConverter(),
        new PdfSlidesConverter(),
        new PdfTablesConverter(),
        new EmlConverter(),
        new DataConverter(),
        new SubtitleConverter(),
        new WicImageConverter(),
        new FfmpegConverter(loggers?.CreateLogger<FfmpegConverter>()),
        new CadConverter(),
        new ModelConverter(),
        new GeoConverter(),
        new EbookConverter(),
        new PandocConverter(),
        new GhostscriptConverter(),
        new VisionConverter(), // before Tesseract: a Mac's OCR step (Tesseract reads the languages Vision doesn't)
        new TesseractConverter(),
        new CalibreConverter(),
        new LibreOfficeConverter(env, loggers?.CreateLogger<LibreOfficeConverter>()),
    ];

    /// <summary>Finds the PDF merger among the converters.</summary>
    public static IPdfMerger? FindPdfMerger(IEnumerable<IConverter> converters) =>
        converters.OfType<IPdfMerger>().FirstOrDefault();

    /// <summary>Finds the engine that writes several pages into one multi-page TIFF.</summary>
    public static ITiffMerger? FindTiffMerger(IEnumerable<IConverter> converters) =>
        converters.OfType<ITiffMerger>().FirstOrDefault();

    /// <summary>Finds the engine that packs several files into one archive ("Compress into one archive").</summary>
    public static IFileCombiner? FindFileCombiner(IEnumerable<IConverter> converters) =>
        converters.OfType<IFileCombiner>().FirstOrDefault();

    /// <summary>The engine that joins videos, audio files and pictures into one (FFmpeg).</summary>
    public static IMediaJoiner? FindMediaJoiner(IEnumerable<IConverter> converters) =>
        converters.OfType<IMediaJoiner>().FirstOrDefault();
}
