using System.Windows;
using System.Windows.Media;
using Mouse2Joy.Engine.Mapping;
using Mouse2Joy.Persistence.Models;

namespace Mouse2Joy.UI.Controls;

public sealed class CurveEditorControl : FrameworkElement
{
    public static readonly DependencyProperty CurveProperty =
        DependencyProperty.Register(nameof(Curve), typeof(Curve), typeof(CurveEditorControl),
            new FrameworkPropertyMetadata(Curve.Default, FrameworkPropertyMetadataOptions.AffectsRender));

    public Curve Curve
    {
        get => (Curve)GetValue(CurveProperty);
        set => SetValue(CurveProperty, value);
    }

    public static readonly DependencyProperty LiveInputProperty =
        DependencyProperty.Register(nameof(LiveInput), typeof(double), typeof(CurveEditorControl),
            new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender));

    public double LiveInput
    {
        get => (double)GetValue(LiveInputProperty);
        set => SetValue(LiveInputProperty, value);
    }

    private static readonly Brush Bg = new SolidColorBrush(Color.FromRgb(28, 28, 32));
    private static readonly Brush Grid = new SolidColorBrush(Color.FromRgb(60, 60, 70));
    private static readonly Brush Line = new SolidColorBrush(Color.FromRgb(0, 200, 120));
    private static readonly Brush Dot = new SolidColorBrush(Color.FromRgb(255, 200, 0));
    private static readonly Pen GridPen = new(Grid, 0.5);
    private static readonly Pen LinePen = new(Line, 1.5);

    static CurveEditorControl()
    {
        Bg.Freeze(); Grid.Freeze(); Line.Freeze(); Dot.Freeze();
        GridPen.Freeze(); LinePen.Freeze();
    }

    protected override Size MeasureOverride(Size availableSize) => new(200, 200);

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        dc.DrawRectangle(Bg, null, new Rect(0, 0, w, h));
        // Grid: center cross + quarter lines.
        dc.DrawLine(GridPen, new Point(w / 2, 0), new Point(w / 2, h));
        dc.DrawLine(GridPen, new Point(0, h / 2), new Point(w, h / 2));
        for (int i = 1; i < 4; i++)
        {
            dc.DrawLine(GridPen, new Point(w * i / 4, 0), new Point(w * i / 4, h));
            dc.DrawLine(GridPen, new Point(0, h * i / 4), new Point(w, h * i / 4));
        }

        // Curve: x in [-1, 1] -> y = CurveEvaluator.Evaluate(x, Curve).
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            const int N = 64;
            for (int i = 0; i <= N; i++)
            {
                var x = -1.0 + 2.0 * i / N;
                var y = CurveEvaluator.Evaluate(x, Curve);
                var px = (x + 1) / 2 * w;
                var py = (1 - (y + 1) / 2) * h;
                if (i == 0) ctx.BeginFigure(new Point(px, py), false, false);
                else ctx.LineTo(new Point(px, py), true, false);
            }
        }
        geometry.Freeze();
        dc.DrawGeometry(null, LinePen, geometry);

        if (!double.IsNaN(LiveInput))
        {
            var x = Math.Clamp(LiveInput, -1, 1);
            var y = CurveEvaluator.Evaluate(x, Curve);
            var px = (x + 1) / 2 * w;
            var py = (1 - (y + 1) / 2) * h;
            dc.DrawEllipse(Dot, null, new Point(px, py), 4, 4);
        }
    }
}
