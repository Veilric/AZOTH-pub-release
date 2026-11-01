// Exports the original AZOTH SVG to desktop, tray, Windows and macOS icons.
// Run from the repository root: dotnet run build/tools/make-icon.cs
#:package Magick.NET-Q8-AnyCPU

using ImageMagick;

var outDir = Path.Combine("src", "AZOTH.App", "Assets", "Icons");
var source = Path.Combine(outDir, "azoth.svg");
var settings = new MagickReadSettings { BackgroundColor = MagickColors.Transparent, Format = MagickFormat.Svg };
using var master = new MagickImage(File.ReadAllBytes(source), settings);
master.Write(Path.Combine(outDir, "azoth.png"), MagickFormat.Png);
master.Settings.SetDefine(MagickFormat.Icon, "auto-resize", "256,64,48,32,24,16");
master.Write(Path.Combine(outDir, "azoth.ico"), MagickFormat.Icon);
Console.WriteLine($"AZOTH icons written to {outDir}");
