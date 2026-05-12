using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Mouse2Joy.Engine.Modifiers;
using Mouse2Joy.Persistence.Models;

namespace Mouse2Joy.UI.Controls;

/// <summary>
/// Interactive curve editor for <see cref="CurveEditorModifier"/>. Renders
/// the curve via the same Fritsch-Carlson math as the runtime, and lets the
/// user manipulate control points with the mouse:
/// <list type="bullet">
///   <item>Left-drag a point to move it.</item>
///   <item>Left-click empty area to add a new point at the click position.</item>
///   <item>Right-click a point to remove it (down to a minimum of 2 points).</item>
///   <item>Hold Shift while dragging to snap to a 0.05 grid.</item>
/// </list>
/// Points reorder automatically by X on every drag tick so the curve stays
/// sorted without visual snapping or clamps.
/// </summary>
public sealed class CurveEditorCanvas : FrameworkElement
{
    private const int MinPointCount = 2;
    private const int MaxPointCount = 7;
    private const double PointRadiusPx = 6.0;
    private const double HitRadiusPx = 12.0;
    private const double SnapIncrement = 0.05;

    public static readonly DependencyProperty PointsProperty =
        DependencyProperty.Register(
            nameof(Points),
            typeof(IReadOnlyList<CurvePoint>),
            typeof(CurveEditorCanvas),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public IReadOnlyList<CurvePoint>? Points
    {
        get => (IReadOnlyList<CurvePoint>?)GetValue(PointsProperty);
        set => SetValue(PointsProperty, value);
    }

    public static readonly DependencyProperty SymmetricProperty =
        DependencyProperty.Register(
            nameof(Symmetric),
            typeof(bool),
            typeof(CurveEditorCanvas),
            new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    public bool Symmetric
    {
        get => (bool)GetValue(SymmetricProperty);
        set => SetValue(SymmetricProperty, value);
    }

    // --- Visual brushes --------------------------------------------------

    private static readonly Brush Bg = new SolidColorBrush(Color.FromRgb(28, 28, 32));
    private static readonly Brush Grid = new SolidColorBrush(Color.FromRgb(60, 60, 70));
    private static readonly Brush Line = new SolidColorBrush(Color.FromRgb(0, 200, 120));
    private static readonly Brush PointFill = new SolidColorBrush(Color.FromRgb(0, 200, 120));
    private static readonly Brush PointHoverFill = new SolidColorBrush(Color.FromRgb(120, 230, 180));
    private static readonly Brush PointDragFill = new SolidColorBrush(Color.FromRgb(255, 220, 80));
    private static readonly Brush Hint = new SolidColorBrush(Color.FromRgb(180, 180, 190));
    private static readonly Pen GridPenLight = new(Grid, 0.5);
    private static readonly Pen GridPenAxis = new(new SolidColorBrush(Color.FromRgb(100, 100, 115)), 1.0);
    private static readonly Pen LinePen = new(Line, 2.0);
    private static readonly Pen PointStroke = new(new SolidColorBrush(Color.FromRgb(20, 20, 22)), 1.0);

    static CurveEditorCanvas()
    {
        Bg.Freeze(); Grid.Freeze(); Line.Freeze();
        PointFill.Freeze(); PointHoverFill.Freeze(); PointDragFill.Freeze();
        Hint.Freeze();
        GridPenLight.Freeze(); GridPenAxis.Freeze(); LinePen.Freeze(); PointStroke.Freeze();
    }

    // --- Interaction state -----------------------------------------------

    private int _draggingIndex = -1;
    private int _hoverIndex = -1;

    public CurveEditorCanvas()
    {
        Focusable = true;
        MouseLeftButtonDown += OnMouseLeftDown;
        MouseLeftButtonUp += OnMouseLeftUp;
        MouseMove += OnMouseMove;
        MouseRightButtonDown += OnMouseRightDown;
        MouseLeave += OnMouseLeave;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        // Reasonable default; the popout window will set explicit dimensions.
        var w = double.IsInfinity(availableSize.Width) ? 400 : availableSize.Width;
        var h = double.IsInfinity(availableSize.Height) ? 400 : availableSize.Height;
        return new Size(w, h);
    }

    // --- Coordinate transforms -------------------------------------------

    private double XMin => Symmetric ? 0.0 : -1.0;
    private double XMax => 1.0;
    private double YMin => -1.0;
    private double YMax => 1.0;

    private Point CurveToPixel(double curveX, double curveY)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        var px = (curveX - XMin) / (XMax - XMin) * w;
        var py = (1.0 - (curveY - YMin) / (YMax - YMin)) * h;
        return new Point(px, py);
    }

    private (double X, double Y) PixelToCurve(Point p)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        var x = XMin + (p.X / w) * (XMax - XMin);
        var y = YMin + (1.0 - p.Y / h) * (YMax - YMin);
        if (x < XMin) x = XMin;
        if (x > XMax) x = XMax;
        if (y < YMin) y = YMin;
        if (y > YMax) y = YMax;
        return (x, y);
    }

    private int HitTestPoint(Point mousePos)
    {
        if (Points is null) return -1;
        for (int i = 0; i < Points.Count; i++)
        {
            var p = Points[i];
            var px = CurveToPixel(p.X, p.Y);
            var dx = mousePos.X - px.X;
            var dy = mousePos.Y - px.Y;
            if (dx * dx + dy * dy <= HitRadiusPx * HitRadiusPx) return i;
        }
        return -1;
    }

    private static double SnapIfShift(double value)
    {
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) return value;
        return Math.Round(value / SnapIncrement) * SnapIncrement;
    }

    // --- Mouse handlers --------------------------------------------------

    private void OnMouseLeftDown(object sender, MouseButtonEventArgs e)
    {
        if (Points is null) return;
        Focus();
        var pos = e.GetPosition(this);
        var hit = HitTestPoint(pos);

        if (hit >= 0)
        {
            // Begin dragging existing point.
            _draggingIndex = hit;
            CaptureMouse();
            e.Handled = true;
            return;
        }

        // Click in empty area → add new point at click coords (if under max).
        if (Points.Count >= MaxPointCount) return;

        var (cx, cy) = PixelToCurve(pos);
        cx = SnapIfShift(cx);
        cy = SnapIfShift(cy);

        var newPoints = Points.Append(new CurvePoint(cx, cy))
            .OrderBy(p => p.X)
            .ToArray();
        Points = newPoints;

        // Find the new point's index after sort so subsequent drag tracks it.
        _draggingIndex = -1;
        for (int i = 0; i < newPoints.Length; i++)
        {
            if (Math.Abs(newPoints[i].X - cx) < 1e-9 && Math.Abs(newPoints[i].Y - cy) < 1e-9)
            {
                _draggingIndex = i;
                break;
            }
        }
        CaptureMouse();
        e.Handled = true;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        var pos = e.GetPosition(this);

        if (_draggingIndex < 0)
        {
            // Just hover-tracking for visual feedback.
            var newHover = HitTestPoint(pos);
            if (newHover != _hoverIndex)
            {
                _hoverIndex = newHover;
                Cursor = newHover >= 0 ? Cursors.Hand : Cursors.Cross;
                InvalidateVisual();
            }
            return;
        }

        if (Points is null || _draggingIndex >= Points.Count) return;

        var (cx, cy) = PixelToCurve(pos);
        cx = SnapIfShift(cx);
        cy = SnapIfShift(cy);

        // Replace the dragged point and re-sort. Track the dragged point's
        // new index (it may have moved as we crossed neighbors).
        var arr = Points.ToArray();
        arr[_draggingIndex] = new CurvePoint(cx, cy);
        var sorted = arr.OrderBy(p => p.X).ToArray();

        // Find where our dragged point ended up. We identify it by value
        // equality with the just-set coordinates. Since drag is continuous,
        // this is reliable (no two points have identical coords by accident
        // unless the user is being adversarial, which we don't need to handle).
        var newIdx = -1;
        for (int i = 0; i < sorted.Length; i++)
        {
            if (Math.Abs(sorted[i].X - cx) < 1e-9 && Math.Abs(sorted[i].Y - cy) < 1e-9)
            {
                newIdx = i;
                break;
            }
        }
        if (newIdx >= 0) _draggingIndex = newIdx;
        Points = sorted;
        e.Handled = true;
    }

    private void OnMouseLeftUp(object sender, MouseButtonEventArgs e)
    {
        if (_draggingIndex >= 0)
        {
            _draggingIndex = -1;
            ReleaseMouseCapture();
            InvalidateVisual();
            e.Handled = true;
        }
    }

    private void OnMouseRightDown(object sender, MouseButtonEventArgs e)
    {
        if (Points is null) return;
        if (Points.Count <= MinPointCount) return;

        var pos = e.GetPosition(this);
        var hit = HitTestPoint(pos);
        if (hit < 0) return;

        var newPoints = Points.Where((_, i) => i != hit).ToArray();
        Points = newPoints;
        if (_hoverIndex == hit) _hoverIndex = -1;
        e.Handled = true;
    }

    private void OnMouseLeave(object sender, MouseEventArgs e)
    {
        if (_hoverIndex >= 0)
        {
            _hoverIndex = -1;
            InvalidateVisual();
        }
    }

    // --- Rendering -------------------------------------------------------

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        dc.DrawRectangle(Bg, null, new Rect(0, 0, w, h));

        // Grid: faint lines at quarter points; bolder line at axis (x=0 in
        // full-range mode, y=0 always).
        if (Symmetric)
        {
            // X range [0,1] — quarter grid at 0.25, 0.5, 0.75; y-axis bold at 0.
            for (int i = 1; i < 4; i++)
            {
                var x = i / 4.0;
                var px = CurveToPixel(x, 0).X;
                dc.DrawLine(GridPenLight, new Point(px, 0), new Point(px, h));
            }
        }
        else
        {
            // X range [-1, 1] — grid at -0.5, 0.5; bold axis at 0.
            foreach (var x in new[] { -0.5, 0.5 })
            {
                var px = CurveToPixel(x, 0).X;
                dc.DrawLine(GridPenLight, new Point(px, 0), new Point(px, h));
            }
            var axisX = CurveToPixel(0, 0).X;
            dc.DrawLine(GridPenAxis, new Point(axisX, 0), new Point(axisX, h));
        }
        // Horizontal grid at y=-0.5, 0.5; axis at y=0.
        foreach (var y in new[] { -0.5, 0.5 })
        {
            var py = CurveToPixel(0, y).Y;
            dc.DrawLine(GridPenLight, new Point(0, py), new Point(w, py));
        }
        var axisY = CurveToPixel(0, 0).Y;
        dc.DrawLine(GridPenAxis, new Point(0, axisY), new Point(w, axisY));

        // Curve (when we have ≥2 points).
        if (Points is { Count: >= 2 })
        {
            DrawCurve(dc, w, h);
            DrawPoints(dc);
        }
        else
        {
            DrawHint(dc, w, h, "Add at least 2 points");
        }
    }

    private void DrawCurve(DrawingContext dc, double w, double h)
    {
        // Build a transient evaluator from the current points + symmetric flag
        // so the rendered curve is identical to runtime evaluation. Go through
        // ChainBuilder so we don't depend on the evaluator's internal accessibility
        // (consistent with how ChainPreviewControl does it).
        var modifier = new CurveEditorModifier
        {
            Points = Points!.ToArray(),
            Symmetric = Symmetric,
        };
        var eval = ChainBuilder.BuildEvaluator(modifier);

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            const int N = 128;
            for (int i = 0; i <= N; i++)
            {
                var x = XMin + (XMax - XMin) * i / N;
                var y = eval.Evaluate(Signal.Scalar(x), 0.01).ScalarValue;
                var px = CurveToPixel(x, y);
                if (i == 0) ctx.BeginFigure(px, false, false);
                else ctx.LineTo(px, true, false);
            }
        }
        geometry.Freeze();
        dc.DrawGeometry(null, LinePen, geometry);
    }

    private void DrawPoints(DrawingContext dc)
    {
        for (int i = 0; i < Points!.Count; i++)
        {
            var p = Points[i];
            var pixel = CurveToPixel(p.X, p.Y);
            var fill = i == _draggingIndex ? PointDragFill
                     : i == _hoverIndex ? PointHoverFill
                     : PointFill;
            var radius = i == _draggingIndex ? PointRadiusPx + 2 : PointRadiusPx;
            dc.DrawEllipse(fill, PointStroke, pixel, radius, radius);
        }
    }

    private static void DrawHint(DrawingContext dc, double w, double h, string text)
    {
        var ft = new FormattedText(text, System.Globalization.CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight, new Typeface("Segoe UI"), 12, Hint, 1.0);
        dc.DrawText(ft, new Point((w - ft.Width) / 2, (h - ft.Height) / 2));
    }
}
