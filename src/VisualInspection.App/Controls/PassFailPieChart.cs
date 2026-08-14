using System.Windows;
using System.Windows.Media;

namespace VisualInspection.App.Controls;

public sealed class PassFailPieChart : FrameworkElement
{
    private static readonly Brush PassBrush = FrozenBrush(Color.FromRgb(61, 205, 88));
    private static readonly Brush FailBrush = FrozenBrush(Color.FromRgb(201, 64, 58));
    private static readonly Brush EmptyBrush = FrozenBrush(Color.FromRgb(226, 234, 230));
    private static readonly Pen SeparatorPen = FrozenPen(Colors.White, 2);

    public static readonly DependencyProperty PassCountProperty = DependencyProperty.Register(
        nameof(PassCount),
        typeof(int),
        typeof(PassFailPieChart),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender),
        value => (int)value >= 0);

    public static readonly DependencyProperty FailCountProperty = DependencyProperty.Register(
        nameof(FailCount),
        typeof(int),
        typeof(PassFailPieChart),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender),
        value => (int)value >= 0);

    public int PassCount
    {
        get => (int)GetValue(PassCountProperty);
        set => SetValue(PassCountProperty, value);
    }

    public int FailCount
    {
        get => (int)GetValue(FailCountProperty);
        set => SetValue(FailCountProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        var size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0)
        {
            return;
        }

        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        var radius = Math.Max(1, size / 2 - SeparatorPen.Thickness);
        var total = PassCount + FailCount;
        if (total <= 0)
        {
            drawingContext.DrawEllipse(EmptyBrush, SeparatorPen, center, radius, radius);
            return;
        }

        if (FailCount == 0)
        {
            drawingContext.DrawEllipse(PassBrush, SeparatorPen, center, radius, radius);
            return;
        }

        if (PassCount == 0)
        {
            drawingContext.DrawEllipse(FailBrush, SeparatorPen, center, radius, radius);
            return;
        }

        var passSweep = 360d * PassCount / total;
        drawingContext.DrawGeometry(
            PassBrush,
            SeparatorPen,
            CreateSector(center, radius, -90, passSweep));
        drawingContext.DrawGeometry(
            FailBrush,
            SeparatorPen,
            CreateSector(center, radius, -90 + passSweep, 360 - passSweep));
    }

    private static StreamGeometry CreateSector(
        Point center,
        double radius,
        double startAngle,
        double sweepAngle)
    {
        var geometry = new StreamGeometry();
        using var context = geometry.Open();
        context.BeginFigure(center, true, true);
        context.LineTo(PointAt(center, radius, startAngle), true, false);
        context.ArcTo(
            PointAt(center, radius, startAngle + sweepAngle),
            new Size(radius, radius),
            0,
            sweepAngle > 180,
            SweepDirection.Clockwise,
            true,
            false);
        geometry.Freeze();
        return geometry;
    }

    private static Point PointAt(Point center, double radius, double angle)
    {
        var radians = angle * Math.PI / 180;
        return new Point(
            center.X + radius * Math.Cos(radians),
            center.Y + radius * Math.Sin(radians));
    }

    private static Brush FrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Pen FrozenPen(Color color, double thickness)
    {
        var pen = new Pen(FrozenBrush(color), thickness);
        pen.Freeze();
        return pen;
    }
}
