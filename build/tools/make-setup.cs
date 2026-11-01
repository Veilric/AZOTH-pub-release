// Makes AZOTH-<version>-win-Setup.exe: appends the published app (an LZMA payload with a SHA-256 manifest, see
// src/AZOTH.Setup/Payload/SetupPayload.cs) to the published Setup program. The engines Setup offers on a fresh install
// come from EngineDownloads (engines.json), so Setup offers exactly what Settings → Engines offers.
// Run by build/build-installer.ps1:
//   dotnet run build/tools/make-setup.cs -- <AZOTH.Setup.exe> <publish folder> <version> <output .exe> [--fast]
// LZMA with a 48 MB dictionary, programs through the x86 branch filter; --fast uses a small dictionary, for checks.
#:project ../../src/AZOTH.Setup/AZOTH.Setup.csproj
#:project ../../src/AZOTH.Engines/AZOTH.Engines.csproj
#:property PublishAot=false

using System.Diagnostics;
using System.Reflection;
using AZOTH.Engines.Infrastructure;
using AZOTH.Setup.Payload;

var fast = args.Contains("--fast");
var positional = args.Where(a => a != "--fast").ToArray();
if (positional.Length != 4)
{
    Console.Error.WriteLine("usage: dotnet run build/tools/make-setup.cs -- <AZOTH.Setup.exe> <publish folder> <version> <output .exe> [--fast]");
    return 2;
}
var (program, app, version, output) = (positional[0], positional[1], positional[2], positional[3]);
if (!File.Exists(Path.Combine(app, "AZOTH.exe")))
{
    Console.Error.WriteLine($"AZOTH.exe is missing in {app}");
    return 1;
}

// EngineInstaller marks every installed component folder with this file; LibreOffice extensions ("oxt") sit inside
// LibreOffice and come with it, so they have no folder of their own.
var marker = (string?)typeof(EngineInstaller).GetField("MarkerName", BindingFlags.NonPublic | BindingFlags.Static)?.GetRawConstantValue()
             ?? throw new InvalidOperationException("EngineInstaller.MarkerName not found");
// Setup always targets Windows, even when its payload is packed on Linux. The running host's downloads and
// package dependencies would otherwise omit Windows offers and use Unix download sizes in the installer UI.
var components = EngineDownloads.ComponentsForRuntime("win-x64");
var packages = EngineDownloads.Packages.Select(p => p with
{
    Components = p.Id switch
    {
        "libreoffice" => ["libreoffice", "h2orestart", "jre"],
        "ghostscript" => ["ghostscript", "vcruntime"],
        _ => p.Components,
    },
});
var engines = packages
    .Where(p => p.Components.Count > 0 && p.Components.All(components.ContainsKey))
    .Select(p => new EngineOffer(
        p.Id,
        p.Components.Where(c => components[c].Kind != "oxt").ToList(),
        p.Components.Sum(c => components[c].Size),
        p.InstalledSize,
        p.Components.Sum(c => components[c].Size) < EngineDownloads.SuggestLimit && p.Id is not ("ghostscript" or "tesseract")))
    .ToList();

var watch = Stopwatch.StartNew();
var manifest = SetupPayload.Write(program, app, Path.GetFullPath(output), version, engines, marker, fast, Console.WriteLine);
var size = new FileInfo(output).Length;
Console.WriteLine($"{manifest.Files.Count} files, {manifest.TotalSize / 1_000_000} MB -> {output} ({size / 1_000_000} MB) in {watch.Elapsed.TotalSeconds:0} s; " +
                  $"engines offered: {string.Join(", ", engines.Select(e => e.Suggested ? e.Id + "*" : e.Id))}");
return 0;
