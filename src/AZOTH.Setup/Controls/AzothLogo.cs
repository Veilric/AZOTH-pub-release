// Official AZOTH artwork. Reduced motion draws the finished mark.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace AZOTH.Setup.Controls;

public sealed class AzothLogo : Control
{
    public static readonly StyledProperty<bool> AnimateProperty =
        AvaloniaProperty.Register<AzothLogo, bool>(nameof(Animate), true);

    private static readonly Avalonia.Media.Imaging.Bitmap Logo = new(
        Avalonia.Platform.AssetLoader.Open(new Uri("avares://AZOTH.Setup/Assets/Icons/azoth.png")));
    private readonly FrameLoop _loop;
    private double _progress;

    static AzothLogo() => AffectsRender<AzothLogo>(AnimateProperty);

    public AzothLogo()
    {
        FlowDirection = FlowDirection.LeftToRight;
        _progress = SetupMotion.Enabled ? 0 : 1;
        _loop = new FrameLoop(this, elapsed =>
        {
            _progress = Math.Min(1, elapsed.TotalMilliseconds / 400);
            InvalidateVisual();
            return _progress < 1;
        });
    }

    public bool Animate { get => GetValue(AnimateProperty); set => SetValue(AnimateProperty, value); }

    public void Replay()
    {
        if (!SetupMotion.Enabled || !Animate)
            return;
        _progress = 0;
        _loop.Restart();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var side = Math.Min(double.IsInfinity(availableSize.Width) ? 96 : availableSize.Width,
            double.IsInfinity(availableSize.Height) ? 96 : availableSize.Height);
        return new Size(side, side);
    }

    public override void Render(DrawingContext context)
    {
        var side = Math.Min(Bounds.Width, Bounds.Height);
        if (side <= 0)
            return;
        var scale = side / 512;
        var view = Matrix.CreateScale(scale, scale)
                   * Matrix.CreateTranslation((Bounds.Width - side) / 2, (Bounds.Height - side) / 2);
        using (context.PushTransform(view))
        using (context.PushOpacity(Animate && SetupMotion.Enabled ? EaseOut(_progress) : 1))
        {
            context.DrawImage(Logo, new Rect(0, 0, 512, 512));
        }
    }

    internal static double EaseOut(double t) => 1 - Math.Pow(1 - t, 3);

    internal static double BackOut(double t)
    {
        const double c1 = 1.70158;
        const double c3 = c1 + 1;
        return 1 + c3 * Math.Pow(t - 1, 3) + c1 * Math.Pow(t - 1, 2);
    }
}
