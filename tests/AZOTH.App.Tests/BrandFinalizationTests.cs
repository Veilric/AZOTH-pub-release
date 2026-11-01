using System.Text.Json;
using System.Xml.Linq;
using AZOTH.App.Services;

namespace AZOTH.App.Tests;

public class BrandFinalizationTests
{
    private static string Root => WhatsNewMedia.RepositoryRoot;

    [Fact]
    public void Every_shipped_language_has_the_attribution_and_a_labeled_origin_credit()
    {
        foreach (var path in Directory.EnumerateFiles(Path.Combine(Root, "src", "AZOTH.App", "Assets", "i18n"), "*.json"))
        {
            var strings = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))!;
            Assert.Equal("AZOTH", strings["app.name"]);
            Assert.Equal("by Veilric", strings["app.tagline"]);
            Assert.Equal("Credits / Open-Source Licenses", strings["about.origin"]);
            Assert.Contains("Filee by Myeonggu Kim (KnifeLemon)", strings["about.origin_text"]);
            Assert.Contains("github.com/Veilric/AZOTH-pub-release", strings["installer.error.damaged"]);
        }
    }

    [Fact]
    public void Readmes_describe_the_available_workspaces_and_preserve_upstream_credit()
    {
        foreach (var path in Directory.EnumerateFiles(Root, "README*.md"))
        {
            var text = File.ReadAllText(path);
            Assert.Contains("by Veilric", text);
            Assert.DoesNotContain("planned for later stages", text);
            Assert.Contains("ECHO", text); Assert.Contains("ORDO", text);
            Assert.Contains("https://github.com/KnifeLemon/Filee", text);
        }
        Assert.Contains("Permission is hereby granted", File.ReadAllText(Path.Combine(Root, "LICENSE")));
    }

    [Fact]
    public void Product_metadata_and_release_configuration_are_independent()
    {
        var properties = XDocument.Load(Path.Combine(Root, "Directory.Build.props"));
        Assert.Equal("https://github.com/Veilric/AZOTH-pub-release", properties.Descendants("AZOTHRepositoryUrl").Single().Value);
        Assert.Empty(properties.Descendants("AZOTHUpdateRepositoryUrl").Single().Value);
        var workflow = File.ReadAllText(Path.Combine(Root, ".github", "workflows", "release.yml"));
        Assert.Contains("vars.AZOTH_RELEASES_ENABLED == 'true'", workflow);
        Assert.Contains("github.repository_owner == 'Veilric'", workflow);
        Assert.Equal("https://github.com/Veilric/AZOTH-pub-release", UpdateService.WebsiteUrl);
    }
}
