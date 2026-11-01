using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using AZOTH.App.Controls;
using AZOTH.App.Services;

namespace AZOTH.App.Tests;

[Collection("MainWindow")]
public class StartupLogoTests
{
    [AvaloniaFact]
    public void Main_interface_stays_hidden_until_logo_finishes()
    {
        TestServices.EnsureInitialized("en");
        var previous = StartupLogo.HasPlayed;
        var motion = Motion.Enabled;
        StartupLogo.HasPlayed = false;
        Motion.Set(true);
        var window = new AZOTH.App.Views.MainWindow();
        try
        {
            var logo = window.FindControl<StartupLogo>("LaunchLogo")!;
            var content = window.FindControl<Grid>("MainInterface")!;
            var splash = window.FindControl<Border>("LaunchSurface")!;
            Assert.False(content.IsVisible);
            Assert.True(splash.IsVisible);
            window.Show();
            logo.Frame(TimeSpan.FromSeconds(10));
            logo.Frame(TimeSpan.FromSeconds(11));
            logo.Frame(TimeSpan.FromSeconds(11.5));
            Assert.True(logo.IsVisible);
            Assert.False(content.IsVisible);
            logo.Frame(TimeSpan.FromSeconds(12.4));
            Assert.True(content.IsVisible);
            Assert.False(splash.IsVisible);
        }
        finally { window.Close(); StartupLogo.HasPlayed = previous; Motion.Set(motion); }
    }

    [AvaloniaFact]
    public void Hidden_window_does_not_consume_reveal_and_first_visible_frame_only_arms_it()
    {
        var previous = StartupLogo.HasPlayed;
        var motion = Motion.Enabled;
        var logo = new StartupLogo();
        var window = new Window { Content = logo, Width = 800, Height = 600 };
        try
        {
            Motion.Set(true);
            StartupLogo.HasPlayed = false;
            logo.Frame(TimeSpan.FromSeconds(10));
            Assert.False(StartupLogo.HasPlayed);
            Assert.False(logo.IsVisible);
            window.Show();
            logo.Frame(TimeSpan.FromSeconds(20));
            Assert.False(StartupLogo.HasPlayed);
            Assert.True(logo.IsVisible);
            logo.Frame(TimeSpan.FromSeconds(21));
            Assert.True(StartupLogo.HasPlayed);
            Assert.Equal(0, logo.Opacity);
            logo.Frame(TimeSpan.FromSeconds(21.5));
            Assert.Equal(1, logo.Opacity);
            logo.Frame(TimeSpan.FromSeconds(22.4));
            Assert.False(logo.IsVisible);
            window.Hide();
            window.Show();
            logo.Frame(TimeSpan.FromSeconds(30));
            Assert.False(logo.IsVisible);
            var reopened = new StartupLogo();
            window.Content = reopened;
            reopened.Frame(TimeSpan.FromSeconds(31));
            Assert.False(reopened.IsVisible);
        }
        finally { window.Close(); StartupLogo.HasPlayed = previous; Motion.Set(motion); }
    }

    [AvaloniaFact]
    public void Minimizing_before_first_frame_cannot_leave_interface_hidden()
    {
        var previous = StartupLogo.HasPlayed;
        var motion = Motion.Enabled;
        StartupLogo.HasPlayed = false;
        Motion.Set(true);
        var logo = new StartupLogo();
        var window = new Window { Content = logo, Width = 800, Height = 600 };
        try
        {
            window.Show();
            window.WindowState = WindowState.Minimized;
            logo.Frame(TimeSpan.FromSeconds(10));
            window.WindowState = WindowState.Normal;
            Assert.True(logo.IsContentReady);
            Assert.False(logo.IsVisible);
        }
        finally { window.Close(); StartupLogo.HasPlayed = previous; Motion.Set(motion); }
    }

    [AvaloniaFact]
    public void Minimizing_during_reveal_finishes_it_before_restore()
    {
        var previous = StartupLogo.HasPlayed;
        var motion = Motion.Enabled;
        var logo = new StartupLogo();
        var window = new Window { Content = logo, Width = 800, Height = 600 };
        try
        {
            StartupLogo.HasPlayed = false;
            Motion.Set(true);
            window.Show();
            logo.Frame(TimeSpan.FromSeconds(10));
            logo.Frame(TimeSpan.FromSeconds(11));
            Assert.True(logo.IsVisible);
            window.WindowState = WindowState.Minimized;
            logo.Frame(TimeSpan.FromSeconds(11.2));
            Assert.False(logo.IsVisible);
            window.WindowState = WindowState.Normal;
            logo.Frame(TimeSpan.FromSeconds(12));
            Assert.False(logo.IsVisible);
            Assert.True(StartupLogo.HasPlayed);
        }
        finally { window.Close(); StartupLogo.HasPlayed = previous; Motion.Set(motion); }
    }

    [AvaloniaFact]
    public void Reduced_motion_skips_reveal_and_cancels_a_running_reveal()
    {
        var previous = Motion.Enabled;
        try
        {
            Motion.Set(true);
            var logo = new StartupLogo();
            logo.RevealAt(.2);
            Assert.InRange(logo.Opacity, 0.01, 0.99);
            Motion.Set(false);
            Assert.Equal(1, logo.RevealAt(.2));
            Assert.False(logo.IsVisible);
            Assert.Null(logo.RenderTransform);
        }
        finally { Motion.Set(previous); }
    }
}
