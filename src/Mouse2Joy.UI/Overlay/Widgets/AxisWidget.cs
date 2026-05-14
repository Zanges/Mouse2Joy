using System.Windows;
using System.Windows.Media;

namespace Mouse2Joy.UI.Overlay.Widgets;

/// <summary>
/// Axis widget — renders ONE filled bar driven by a single 1D source. Sources
/// span both unipolar triggers (0..1) and bipolar stick axes (-1..+1); the
/// renderer dispatches on the source name and centres bipolar bars at zero.
/// Replaces the old "Triggers" widget which always drew both LT and RT.
/// </summary>
public sealed class AxisWidget : OverlayWidget
{
    public override string TypeId => "Axis";

    /// <summary>Sources accepted by the Axis widget. Order is the editor's combobox order.</summary>
    public static readonly string[] Sources =
    {
        "LeftTrigger", "RightTrigger",
        "LeftStickX", "LeftStickY", "RightStickX", "RightStickY"
    };

    public static IReadOnlyList<OptionDescriptor> OptionSchema { get; } = new OptionDescriptor[]
    {
        new("source", "Source", OptionKind.Enum, "LeftTrigger", EnumValues: Sources),
        new("orientation", "Orientation", OptionKind.Enum, "horizontal", EnumValues: new[] { "horizontal", "vertical" }),
        new("accentColor", "Accent color", OptionKind.Color, "#FF00C878"),
        new("showBackground", "Show background", OptionKind.Bool, false)
    };

    protected override Size MeasureOverride(Size availableSize)
        => new(Config.Width, Config.Height);

    protected override void OnRender(DrawingContext drawingContext)
    {
        var w = Math.Max(0, Config.Width);
        var h = Math.Max(0, Config.Height);
        var accent = ReadColorBrush("accentColor", AccentBrush);
        var vertical = ReadString("orientation", "horizontal").Equals("vertical", StringComparison.OrdinalIgnoreCase);
        var source = ReadString("source", "LeftTrigger");
        var (rawValue, bipolar, _) = ReadSource(source);

        if (ReadBool("showBackground", false))
        {
            drawingContext.DrawRoundedRectangle(BgBrush, Outline, new Rect(0, 0, w, h), 6, 6);
        }

        // Inset a couple of pixels so the bar doesn't touch the widget edge.
        const double pad = 2;
        var barRect = new Rect(pad, pad, Math.Max(0, w - 2 * pad), Math.Max(0, h - 2 * pad));
        drawingContext.DrawRectangle(null, Outline, barRect);

        if (vertical)
        {
            DrawVerticalFill(drawingContext, accent, barRect, rawValue, bipolar);
        }
        else
        {
            DrawHorizontalFill(drawingContext, accent, barRect, rawValue, bipolar);
        }
    }

    /// <summary>
    /// Look up the named source on the engine snapshot. Returns the raw value plus
    /// a flag indicating whether the value is bipolar (sticks: -1..+1) vs unipolar
    /// (triggers: 0..1), and a short label.
    /// </summary>
    private (double value, bool bipolar, string label) ReadSource(string source) => source switch
    {
        "LeftTrigger" => (Snapshot.LeftTrigger, false, "LT"),
        "RightTrigger" => (Snapshot.RightTrigger, false, "RT"),
        "LeftStickX" => (Snapshot.LeftStickX, true, "LX"),
        "LeftStickY" => (Snapshot.LeftStickY, true, "LY"),
        "RightStickX" => (Snapshot.RightStickX, true, "RX"),
        "RightStickY" => (Snapshot.RightStickY, true, "RY"),
        _ => (0.0, false, "?")
    };

    private static void DrawHorizontalFill(DrawingContext dc, Brush accent, Rect bar, double value, bool bipolar)
    {
        if (bipolar)
        {
            // Centre the fill at 0; positive grows right, negative grows left.
            var v = Math.Clamp(value, -1, 1);
            var halfW = bar.Width / 2.0;
            var midX = bar.X + halfW;
            if (v >= 0)
            {
                dc.DrawRectangle(accent, null, new Rect(midX, bar.Y, halfW * v, bar.Height));
            }
            else
            {
                dc.DrawRectangle(accent, null, new Rect(midX + halfW * v, bar.Y, halfW * -v, bar.Height));
            }
        }
        else
        {
            var v = Math.Clamp(value, 0, 1);
            dc.DrawRectangle(accent, null, new Rect(bar.X, bar.Y, bar.Width * v, bar.Height));
        }
    }

    private static void DrawVerticalFill(DrawingContext dc, Brush accent, Rect bar, double value, bool bipolar)
    {
        if (bipolar)
        {
            var v = Math.Clamp(value, -1, 1);
            var halfH = bar.Height / 2.0;
            var midY = bar.Y + halfH;
            // Bipolar Y: positive = up (gamepad convention), negative = down.
            if (v >= 0)
            {
                dc.DrawRectangle(accent, null, new Rect(bar.X, midY - halfH * v, bar.Width, halfH * v));
            }
            else
            {
                dc.DrawRectangle(accent, null, new Rect(bar.X, midY, bar.Width, halfH * -v));
            }
        }
        else
        {
            var v = Math.Clamp(value, 0, 1);
            var fillHeight = bar.Height * v;
            dc.DrawRectangle(accent, null, new Rect(bar.X, bar.Bottom - fillHeight, bar.Width, fillHeight));
        }
    }
}
