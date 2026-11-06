namespace AZOTH.App.Tests;

internal static class NativeFileTestPaths
{
    public static string CreateTempDirectory(string prefix)
    {
        var path = Directory.CreateTempSubdirectory(prefix).FullName;
        // Native metadata rejects linked ancestors; macOS /var points to /private/var.
        return OperatingSystem.IsMacOS() && path.StartsWith("/var/", StringComparison.Ordinal)
            ? "/private" + path : path;
    }
}
