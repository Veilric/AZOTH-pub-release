using System.Reflection;

namespace AZOTH.Setup;

/// <summary>Facts about this Setup program.</summary>
public static class SetupInfo
{
    /// <summary>GitHub repository from Directory.Build.props (AZOTHRepositoryUrl).</summary>
    public static string RepositoryUrl =>
        typeof(SetupInfo).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "RepositoryUrl")?.Value ?? "";

    /// <summary>AZOTH product information and support.</summary>
    public const string WebsiteUrl = "https://github.com/Veilric/AZOTH-pub-release";

    /// <summary>The version this Setup program was built as (the payload says which AZOTH it installs).</summary>
    public static string Version =>
        typeof(SetupInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0.0.0";
}
