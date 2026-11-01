using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using AZOTH.App.Services;

namespace AZOTH.App.Controls;

/// <summary>A once-per-process launch stage that reveals the main interface only after the logo finishes.</summary>
public sealed class StartupLogo : Image
{
    public static readonly StyledProperty<bool> IsContentReadyProperty =
        AvaloniaProperty.Register<StartupLogo, bool>(nameof(IsContentReady));

    /// <summary>True when the launch stage has finished or accessibility/reopening skips it.</summary>
    public bool IsContentReady
    {
        get => GetValue(IsContentReadyProperty);
        private set => SetValue(IsContentReadyProperty, value);
    }

    internal static bool HasPlayed { get; set; }
    private TimeSpan? _start;
    private Window? _window;
    private bool _armed;
    private bool _finished;

    public StartupLogo()
    {
        IsVisible = false;
        IsHitTestVisible = false;
        Opacity = 0;
        IsContentReady = HasPlayed || !Motion.Enabled;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _window = TopLevel.GetTopLevel(this) as Window;
        if (_window is null)
            return;
        _window.Opened += OnOpened;
        _window.PropertyChanged += OnWindowChanged;
        if (_window.IsVisible)
            _window.RequestAnimationFrame(Frame);
    }

    private void OnOpened(object? sender, EventArgs e) => _window?.RequestAnimationFrame(Frame);

    private void OnWindowChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (_window is { } window &&
            ((e.Property == Window.WindowStateProperty && window.WindowState == WindowState.Minimized) ||
             (_armed && e.Property == IsVisibleProperty && !window.IsVisible)))
        {
            HasPlayed = true;
            Finish();
        }
    }

    internal void Frame(TimeSpan now)
    {
        if (_finished || _window is not { } window)
            return;
        if (window.WindowState == WindowState.Minimized)
        {
            HasPlayed = true;
            Finish();
            return;
        }
        if (!window.IsVisible)
            return;
        if (_start is null)
        {
            if (HasPlayed || !Motion.Enabled)
            {
                HasPlayed = true;
                Finish();
                return;
            }
            if (window.Bounds.Width <= 0 || window.Bounds.Height <= 0)
            {
                window.RequestAnimationFrame(Frame);
                return;
            }
            if (!_armed)
            {
                // Let the visible window and launch surface pass through layout/render once before starting the clock.
                _armed = true;
                IsVisible = true;
                window.RequestAnimationFrame(Frame);
                return;
            }
            HasPlayed = true;
            _start = now;
        }
        if (RevealAt((now - _start.Value).TotalSeconds) < 1)
            window.RequestAnimationFrame(Frame);
    }

    internal double RevealAt(double seconds)
    {
        var t = Motion.Enabled ? Math.Clamp(seconds / 1.4, 0, 1) : 1;
        if (t >= 1)
        {
            Finish();
            return 1;
        }
        var reveal = Math.Clamp(seconds / .45, 0, 1);
        var settle = 1 - Math.Pow(1 - reveal, 3);
        Opacity = reveal * Math.Clamp((1.4 - seconds) / .3, 0, 1);
        RenderTransformOrigin = RelativePoint.Center;
        RenderTransform = new ScaleTransform(.92 + .08 * settle, .92 + .08 * settle);
        return t;
    }

    private void Finish()
    {
        _finished = true;
        IsContentReady = true;
        IsVisible = false;
        Opacity = 0;
        RenderTransform = null;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_window is not null)
        {
            _window.Opened -= OnOpened;
            _window.PropertyChanged -= OnWindowChanged;
        }
        _window = null;
        Finish();
        base.OnDetachedFromVisualTree(e);
    }
}
