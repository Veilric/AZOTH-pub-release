using Avalonia.Headless.XUnit;
using AZOTH.App.Services;
using AZOTH.App.Views;
using AZOTH.Core.Localization;

namespace AZOTH.App.Tests;

public class SponsorTests
{
    [Theory]
    [InlineData("ko")]
    [InlineData("en")]
    [InlineData("zh-CN")]
    public void No_payment_destinations_are_bundled(string language) =>
        Assert.Empty(SponsorLinks.For(language));

    [AvaloniaFact]
    public void Unconfigured_sponsorship_has_no_payment_buttons()
    {
        TestServices.EnsureInitialized("en");
        var window = SponsorWindow.Create(AppHost.Get<ILocalizer>());
        Assert.Empty(Assert.IsAssignableFrom<IEnumerable<SponsorWay>>(window.Ways.ItemsSource));
    }
}
