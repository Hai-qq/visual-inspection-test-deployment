using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace VisualInspection.App.Controls;

public sealed class DonutChart : FrameworkElement
{
    public static readonly DependencyProperty PassRateProperty = DependencyProperty.Register(
        nameof(PassRate), typeof(double), typeof(DonutChart),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CenterTextProperty = DependencyProperty.Register(
        nameof(CenterText), typeof(string), typeof(DonutChart),
        new FrameworkPropertyMetadata("--", FrameworkPropertyMetadataOptions.AffectsRender));

    public double PassRate
    {
        get => (double)GetValue(PassRateProperty);
        set => SetValue(PassRateProperty, value);
    }

    public string CenterText
    {
        get => (string)GetValue(CenterTextProperty);
        set => SetValue(CenterTextProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        var size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0) return;

        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        var radius = Math.Max(1, size / 2 - 8);
        var thickness = Math.Max(8, size * 0.12);
        var rate = Math.Clamp(PassRate, 0, 1);

        drawingContext.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromRgb(224, 232, 228)), thickness), center, radius, radius);

        if (rate > 0)
        {
            var geometry = CreateArc(center, radius, -90, Math.Min(rate * 359.999, 359.999));
            drawingContext.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromRgb(61, 205, 88)), thickness)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round
            }, geometry);
        }

        var label = new FormattedText(
            CenterText, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI Semibold"), size * 0.13,
            new SolidColorBrush(Color.FromRgb(35, 52, 45)),
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        drawingContext.DrawText(label, new Point(center.X - label.Width / 2, center.Y - label.Height / 2));
    }

    private static StreamGeometry CreateArc(Point center, double radius, double startAngle, double sweepAngle)
    {
        static Point At(Point origin, double r, double degrees)
        {
            var radians = degrees * Math.PI / 180;
            return new Point(origin.X + r * Math.Cos(radians), origin.Y + r * Math.Sin(radians));
        }

        var geometry = new StreamGeometry();
        using var context = geometry.Open();
        context.BeginFigure(At(center, radius, startAngle), false, false);
        context.ArcTo(At(center, radius, startAngle + sweepAngle), new Size(radius, radius), 0,
            sweepAngle > 180, SweepDirection.Clockwise, true, false);
        geometry.Freeze();
        return geometry;
    }
}
