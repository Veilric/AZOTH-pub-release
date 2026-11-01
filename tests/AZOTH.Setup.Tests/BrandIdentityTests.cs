using System.Reflection;
using AZOTH.Setup.Installation;

namespace AZOTH.Setup.Tests;

public class BrandIdentityTests
{
    [Fact]
    public void Product_links_and_publisher_do_not_change_established_installation_identity()
    {
        Assert.Equal("https://github.com/Veilric/AZOTH-pub-release", SetupInfo.RepositoryUrl);
        Assert.Equal("https://github.com/Veilric/AZOTH-pub-release", SetupInfo.WebsiteUrl);
        Assert.Equal("AZOTH", SetupNames.AppName);
        Assert.Equal("Veilric", SetupNames.Publisher);
        Assert.Equal("Veilric", typeof(SetupInfo).Assembly.GetCustomAttribute<AssemblyCompanyAttribute>()!.Company);
        Assert.Equal("52A99766-B7BE-43D3-A17E-8ED3D5CEB19F", SetupNames.AppGuid);
        Assert.Equal("AZOTH.exe", SetupNames.AppExe);
        Assert.Equal("uninstall.exe", SetupNames.UninstallerExe);
        Assert.Equal("azoth-install.json", SetupNames.InstallRecordFile);
        Assert.Equal("AZOTH.lnk", SetupNames.ShortcutName);
    }
}
