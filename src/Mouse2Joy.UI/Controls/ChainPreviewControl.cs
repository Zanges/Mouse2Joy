using System.Windows;
using System.Windows.Media;
using Mouse2Joy.Engine.Modifiers;
using Mouse2Joy.Persistence;
using Mouse2Joy.Persistence.Models;

namespace Mouse2Joy.UI.Controls;

/// <summary>
/// Replaces the v1 <c>CurveEditorControl</c>. Renders an input → output
/// graph for the entire modifier chain.
///
/// Sweep strategy by source type:
/// <list type="bullet">
///   <item>Scalar input (after a converter or for a Scalar-source chain): sweep x in [-1, 1] over the Scalar pipeline.</item>
///   <item>Delta input (mouse-axis): synthesize a steady-state — feed a per-tick delta that maps to each x in [-1, 1] via the StickDynamics' "counts → full deflection" param, then run the rest of the chain. Approximate: shows the shape, not the dynamics.</item>
///   <item>Digital input: no meaningful sweep — render two-bar "off / on" output values instead.</item>
/// </list>
///
/// Modifiers are evaluated in fresh (stateless-equivalent) instances per sample so the rendering reflects the chain configuration, not its in-flight runtime state.
/// </summary>
public sealed class ChainPreviewControl : FrameworkElement
{
    public static readonly DependencyProperty SourceProperty =
        DependencyProperty.Register(nameof(Source), typeof(InputSource), typeof(ChainPreviewControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public InputSource? Source
    {
        get => (InputSource?)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public static readonly DependencyProperty TargetProperty =
        DependencyProperty.Register(nameof(Target), typeof(OutputTarget), typeof(ChainPreviewControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public OutputTarget? Target
    {
        get => (OutputTarget?)GetValue(TargetProperty);
        set => SetValue(TargetProperty, value);
    }

    public static readonly DependencyProperty ModifiersProperty =
        DependencyProperty.Register(nameof(Modifiers), typeof(IReadOnlyList<Modifier>), typeof(ChainPreviewControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<Modifier>? Modifiers
    {
        get => (IReadOnlyList<Modifier>?)GetValue(ModifiersProperty);
        set => SetValue(ModifiersProperty, value);
    }

    private static readonly Brush Bg = new SolidColorBrush(Color.FromRgb(28, 28, 32));
    private static readonly Brush Grid = new SolidColorBrush(Color.FromRgb(60, 60, 70));
    private static readonly Brush Line = new SolidColorBrush(Color.FromRgb(0, 200, 120));
    private static readonly Brush Hint = new SolidColorBrush(Color.FromRgb(180, 180, 190));
    private static readonly Brush BarOff = new SolidColorBrush(Color.FromRgb(70, 70, 90));
    private static readonly Brush BarOn = new SolidColorBrush(Color.FromRgb(0, 200, 120));
    private static readonly Pen GridPen = new(Grid, 0.5);
    private static readonly Pen LinePen = new(Line, 1.5);

    static ChainPreviewControl()
    {
        Bg.Freeze(); Grid.Freeze(); Line.Freeze(); Hint.Freeze();
        BarOff.Freeze(); BarOn.Freeze();
        GridPen.Freeze(); LinePen.Freeze();
    }

    protected override Size MeasureOverride(Size availableSize) => new(200, 200);

    protected override void OnRender(DrawingContext drawingContext)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        if (w <= 0 || h <= 0)
        {
            return;
        }

        drawingContext.DrawRectangle(Bg, null, new Rect(0, 0, w, h));

        var src = Source;
        var tgt = Target;
        var mods = Modifiers ?? Array.Empty<Modifier>();
        if (src is null || tgt is null)
        {
            return;
        }

        var validation = ChainValidator.Validate(src, mods, tgt);
        if (!validation.IsValid)
        {
            DrawHint(drawingContext, w, h, "Invalid chain — fix to preview");
            return;
        }

        var sourceType = ModifierTypes.GetSourceOutputType(src);
        var targetType = ModifierTypes.GetTargetInputType(tgt);

        switch (sourceType, targetType)
        {
            case (SignalType.Digital, SignalType.Digital):
                DrawDigitalToDigital(drawingContext, w, h, src, mods);
                break;
            case (SignalType.Digital, SignalType.Scalar):
                DrawDigitalToScalar(drawingContext, w, h, src, mods);
                break;
            case (SignalType.Delta, SignalType.Scalar):
                DrawDeltaToScalar(drawingContext, w, h, src, mods, tgt);
                break;
            case (SignalType.Delta, SignalType.Digital):
                DrawDeltaToScalar(drawingContext, w, h, src, mods, tgt);
                break;
            default:
                DrawHint(drawingContext, w, h, "(no preview)");
                break;
        }

        // Center cross + quarter grid.
        drawingContext.DrawLine(GridPen, new Point(w / 2, 0), new Point(w / 2, h));
        drawingContext.DrawLine(GridPen, new Point(0, h / 2), new Point(w, h / 2));
    }

    private void DrawHint(DrawingContext dc, double w, double h, string text)
    {
        var ft = new FormattedText(text, System.Globalization.CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight, new Typeface("Segoe UI"), 12, Hint, 1.0);
        dc.DrawText(ft, new Point((w - ft.Width) / 2, (h - ft.Height) / 2));
    }

    private void DrawDigitalToDigital(DrawingContext dc, double w, double h, InputSource src, IReadOnlyList<Modifier> mods)
    {
        // Two bars: off-state output and on-state output. Both should be
        // booleans; Modifier list is empty by convention.
        var off = EvaluateDigital(src, mods, digitalIn: false);
        var on = EvaluateDigital(src, mods, digitalIn: true);
        DrawTwoBars(dc, w, h, off, on);
    }

    private void DrawDigitalToScalar(DrawingContext dc, double w, double h, InputSource src, IReadOnlyList<Modifier> mods)
    {
        var off = EvaluateScalarFromDigital(src, mods, digitalIn: false);
        var on = EvaluateScalarFromDigital(src, mods, digitalIn: true);
        DrawTwoScalarBars(dc, w, h, off, on);
    }

    private void DrawDeltaToScalar(DrawingContext dc, double w, double h, InputSource src, IReadOnlyList<Modifier> mods, OutputTarget tgt)
    {
        // Strategy: skip the StickDynamics modifier and treat its output as a
        // sweepable Scalar in [-1, 1]. The preview then shows what the rest
        // of the chain (Sensitivity / Deadzone / Curve / Invert / etc.) does
        // to the integrated stick deflection. Identical semantics to the v1
        // CurveEditorControl, which also only ever plotted post-integration.
        //
        // The user's "what does the integrator feel like" mental model is
        // captured by the StickDynamics params themselves (counts → full,
        // decay rate); the sweep here would just produce a straight line if
        // we tried to faithfully render integration steady-state, since each
        // mode's steady-state is linear in xTarget. So the more useful
        // visualization is "what shape does the chain apply on top".
        var sdIndex = -1;
        for (int i = 0; i < mods.Count; i++)
        {
            if (mods[i] is StickDynamicsModifier) { sdIndex = i; break; }
        }
        // Modifiers AFTER the StickDynamics are the post-integration shape.
        // (In a valid Delta→Scalar chain, modifiers before it would have to
        // also be Delta→Delta, which we don't have any of in v1.)
        var postStick = sdIndex >= 0
            ? mods.Skip(sdIndex + 1).ToArray()
            : mods.ToArray();
        DrawScalarSweep(dc, w, h, postStick);
    }

    /// <summary>
    /// Sweep x in [-1, 1] through a Scalar→Scalar chain (or any prefix of
    /// modifiers that consumes Scalar). Each sample uses fresh evaluators so
    /// stateful modifiers (RampUp/RampDown) don't bleed across samples.
    /// </summary>
    private void DrawScalarSweep(DrawingContext dc, double w, double h, IReadOnlyList<Modifier> scalarMods)
    {
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            const int N = 64;
            for (int i = 0; i <= N; i++)
            {
                var x = -1.0 + 2.0 * i / N;
                var y = EvaluateScalarChain(scalarMods, x);
                var px = (x + 1) / 2 * w;
                var py = (1 - (y + 1) / 2) * h;
                if (i == 0)
                {
                    ctx.BeginFigure(new Point(px, py), false, false);
                }
                else
                {
                    ctx.LineTo(new Point(px, py), true, false);
                }
            }
        }
        geometry.Freeze();
        dc.DrawGeometry(null, LinePen, geometry);
    }

    private static double EvaluateScalarChain(IReadOnlyList<Modifier> mods, double x)
    {
        // Fresh evaluators per sample — no cross-sample bleed from Ramp state.
        Signal sig = Signal.Scalar(x);
        foreach (var m in mods)
        {
            if (!m.Enabled)
            {
                continue;
            }
            // Skip non-Scalar→Scalar modifiers defensively (shouldn't happen
            // on a validated chain past the StickDynamics, but guard anyway).
            var io = ModifierTypes.GetIO(m);
            if (io.In != SignalType.Scalar)
            {
                continue;
            }

            var ev = ChainBuilder.BuildEvaluator(m);
            sig = ev.Evaluate(in sig, 0.01);
        }
        return sig.Type == SignalType.Scalar ? sig.ScalarValue : 0.0;
    }

    private void DrawTwoBars(DrawingContext dc, double w, double h, bool offOut, bool onOut)
    {
        var pad = 12.0;
        var barW = (w - pad * 3) / 2;
        DrawBar(dc, pad, h, barW, offOut ? 1.0 : 0.0, "off");
        DrawBar(dc, pad * 2 + barW, h, barW, onOut ? 1.0 : 0.0, "on");
    }

    private void DrawTwoScalarBars(DrawingContext dc, double w, double h, double offOut, double onOut)
    {
        var pad = 12.0;
        var barW = (w - pad * 3) / 2;
        DrawBar(dc, pad, h, barW, offOut, "off");
        DrawBar(dc, pad * 2 + barW, h, barW, onOut, "on");
    }

    private void DrawBar(DrawingContext dc, double x, double h, double width, double valueClamped, string label)
    {
        var v = Math.Max(-1, Math.Min(1, valueClamped));
        var midY = h / 2;
        var barH = Math.Abs(v) * (h / 2 - 24);
        var rect = v >= 0
            ? new Rect(x, midY - barH, width, barH)
            : new Rect(x, midY, width, barH);
        dc.DrawRectangle(BarOn, null, rect);
        var ft = new FormattedText(label, System.Globalization.CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight, new Typeface("Segoe UI"), 11, Hint, 1.0);
        dc.DrawText(ft, new Point(x + (width - ft.Width) / 2, h - ft.Height - 2));
    }

    private static bool EvaluateDigital(InputSource src, IReadOnlyList<Modifier> mods, bool digitalIn)
    {
        // Empty chain: pass-through.
        var sig = Signal.Digital(digitalIn);
        foreach (var m in mods)
        {
            if (!m.Enabled)
            {
                continue;
            }

            var ev = ChainBuilder.BuildEvaluator(m);
            sig = ev.Evaluate(in sig, 0.01);
        }
        return sig.Type == SignalType.Digital ? sig.DigitalValue : sig.ScalarValue > 0.5;
    }

    private static double EvaluateScalarFromDigital(InputSource src, IReadOnlyList<Modifier> mods, bool digitalIn)
    {
        var sig = Signal.Digital(digitalIn);
        foreach (var m in mods)
        {
            if (!m.Enabled)
            {
                continue;
            }

            var ev = ChainBuilder.BuildEvaluator(m);
            sig = ev.Evaluate(in sig, 0.01);
        }
        return sig.Type == SignalType.Scalar ? sig.ScalarValue : (sig.DigitalValue ? 1.0 : 0.0);
    }

}
