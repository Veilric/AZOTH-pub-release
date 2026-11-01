namespace AZOTH.App.Services;

/// <summary>A payment destination, used only when deliberately configured by the maintainer.</summary>
public sealed record SponsorLink(string Id, string Name, string Url, string Icon, string Color);

public static class SponsorLinks
{
    /// <summary>AZOTH has no configured donation destinations.</summary>
    public static IReadOnlyList<SponsorLink> For(string language) => [];
}
