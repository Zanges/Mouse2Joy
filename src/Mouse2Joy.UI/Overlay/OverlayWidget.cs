using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using Mouse2Joy.Engine.State;
using Mouse2Joy.Persistence.Models;

namespace Mouse2Joy.UI.Overlay;

public abstract class OverlayWidget : FrameworkElement
{
    public abstract string TypeId { get; }
    public WidgetConfig Config { get; set; } = new();

    protected EngineStateSnapshot Snapshot { get; private set; } = EngineStateSnapshot.Empty;

    public void RenderState(EngineStateSnapshot snapshot)
    {
        Snapshot = snapshot;
        InvalidateVisual();
    }

    protected static readonly Brush BgBrush = new SolidColorBrush(Color.FromArgb(140, 0, 0, 0));
    protected static readonly Brush AccentBrush = new SolidColorBrush(Color.FromArgb(255, 0, 200, 120));
    protected static readonly Brush TextBrush = Brushes.White;
    protected static readonly Pen Outline = new(new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)), 1);

    static OverlayWidget()
    {
        BgBrush.Freeze();
        AccentBrush.Freeze();
        Outline.Freeze();
    }

    // Per-widget options live in WidgetConfig.Options as a Dictionary<string, JsonElement>.
    // These helpers coerce the JSON elements back to typed values, falling back to a default
    // when the key is absent or the kind doesn't match. Each widget defines a schema in its
    // OptionSchema property; the Widget Editor UI uses that schema to render fields.

    protected bool ReadBool(string key, bool fallback)
    {
        if (!Config.Options.TryGetValue(key, out var v)) return fallback;
        return v.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => fallback
        };
    }

    protected string ReadString(string key, string fallback)
    {
        if (!Config.Options.TryGetValue(key, out var v)) return fallback;
        return v.ValueKind == JsonValueKind.String ? v.GetString() ?? fallback : fallback;
    }

    protected int ReadInt(string key, int fallback)
    {
        if (!Config.Options.TryGetValue(key, out var v)) return fallback;
        return v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : fallback;
    }

    /// <summary>
    /// Reads a hex color string (e.g. "#FF8800") from Options and returns a frozen brush.
    /// Falls back to <paramref name="fallback"/> when the key is absent or unparseable.
    /// </summary>
    protected Brush ReadColorBrush(string key, Brush fallback)
    {
        var hex = ReadString(key, "");
        if (string.IsNullOrEmpty(hex)) return fallback;
        try
        {
            var converted = ColorConverter.ConvertFromString(hex);
            if (converted is not Color c) return fallback;
            var brush = new SolidColorBrush(c);
            brush.Freeze();
            return brush;
        }
        catch
        {
            return fallback;
        }
    }

    /// <summary>
    /// Same as <see cref="ReadColorBrush"/> but returns a Pen for outline-style use.
    /// </summary>
    protected Pen ReadColorPen(string key, Pen fallback, double thickness = 1.0)
    {
        var hex = ReadString(key, "");
        if (string.IsNullOrEmpty(hex)) return fallback;
        try
        {
            var converted = ColorConverter.ConvertFromString(hex);
            if (converted is not Color c) return fallback;
            var brush = new SolidColorBrush(c);
            brush.Freeze();
            var pen = new Pen(brush, thickness);
            pen.Freeze();
            return pen;
        }
        catch
        {
            return fallback;
        }
    }
}
