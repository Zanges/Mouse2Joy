using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Mouse2Joy.Engine;
using Mouse2Joy.Engine.State;
using Mouse2Joy.Persistence.Models;

namespace Mouse2Joy.UI.Overlay.Widgets;

/// <summary>
/// Status text widget — a single text readout sourced from the engine. The source
/// is one of: engine mode, profile name, a single button's pressed/released text,
/// or a single axis numeric value. Replaces the older bundled "Status" widget
/// (mode dot + text); the dot is now the separate <see cref="EngineStatusIndicatorWidget"/>.
///
/// Auto-sizes to the rendered text + optional background padding. The
/// <see cref="WidgetConfig.Width"/>/<c>Height</c> values are deliberately ignored
/// so font, content, rotation, and vertical-stack determine the footprint.
/// </summary>
public sealed class StatusWidget : OverlayWidget
{
    public override string TypeId => "Status";

    /// <summary>Source-kind enum values exposed in the editor.</summary>
    public static readonly string[] SourceKinds = { "Text", "Mode", "Profile", "Button", "Axis" };

    /// <summary>Axis format options.</summary>
    public static readonly string[] AxisFormats = { "Decimal", "Percent" };

    public static IReadOnlyList<OptionDescriptor> OptionSchema { get; } = new OptionDescriptor[]
    {
        new("sourceKind", "Source", OptionKind.Enum, "Mode", EnumValues: SourceKinds),
        new("sourceName", "Source name", OptionKind.String, ""),
        new("label", "Label", OptionKind.String, ""),
        new("pressedText", "Pressed text", OptionKind.String, "Pressed"),
        new("releasedText", "Released text", OptionKind.String, ""),
        new("axisFormat", "Axis format", OptionKind.Enum, "Decimal", EnumValues: AxisFormats),
        new("axisDecimals", "Axis decimals", OptionKind.Int, 2, Min: 0, Max: 4),
        new("fontFamily", "Font", OptionKind.String, "Segoe UI"),
        new("fontSize", "Size", OptionKind.Int, 12, Min: 6, Max: 72),
        new("bold", "Bold", OptionKind.Bool, false),
        new("italic", "Italic", OptionKind.Bool, false),
        new("underline", "Underline", OptionKind.Bool, false),
        new("rotation", "Rotation", OptionKind.Int, 0, Min: 0, Max: 359),
        new("verticalStack", "Upright letters", OptionKind.Bool, false),
        new("letterSpacing", "Letter spacing", OptionKind.Int, 0, Min: -10, Max: 40),
        new("textColor", "Text color", OptionKind.Color, "#FFFFFF"),
        new("showBackground", "Show background", OptionKind.Bool, false),
        new("backgroundColor", "Background color", OptionKind.Color, "#8C000000")
    };

    /// <summary>
    /// Padding added around the rendered text when the background is drawn. Also
    /// used as a small breathing room when no background is shown so that the
    /// auto-sized footprint isn't a tight rectangle hugging the glyph bounds.
    /// </summary>
    private const double Padding = 4;

    /// <summary>
    /// Compute the auto-sized footprint for a Status widget without instantiating
    /// the FrameworkElement. The overlay coordinator calls this so that anchor
    /// math (e.g. SelfAnchor=Bottom) lines up with the widget's actual rendered
    /// size rather than the unused <see cref="WidgetConfig.Width"/>/<c>Height</c>
    /// values that this widget ignores.
    /// </summary>
    public static Size MeasureFootprint(WidgetConfig cfg)
    {
        return BuildPlan(cfg, snapshot: null).Size;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        return BuildPlan(Config, Snapshot).Size;
    }

    protected override void OnRender(DrawingContext dc)
    {
        var render = BuildPlan(Config, Snapshot);
        // Set the visual's Width/Height so the hosting Canvas honours the auto-sized
        // footprint. The host (OverlayWidgetHost) doesn't size the widget itself —
        // it only positions it — so we drive size from here.
        if (Width != render.Size.Width) Width = render.Size.Width;
        if (Height != render.Size.Height) Height = render.Size.Height;

        if (render.IsEmpty) return;

        if (render.ShowBackground)
        {
            dc.DrawRoundedRectangle(render.BackgroundBrush, Outline,
                new Rect(0, 0, render.Size.Width, render.Size.Height), 4, 4);
        }

        if (render.Glyphs is not null)
        {
            // Per-glyph placement (upright-stacked text, or any mode with
            // letter spacing). Each glyph is already positioned in the widget's
            // local coordinate space. When GlyphRotation is non-zero (the
            // non-upright + spaced case), rotate around the glyph's stride
            // point so the glyphs follow the rotated stride axis just like the
            // fast horizontal path would.
            foreach (var g in render.Glyphs)
            {
                if (g.GlyphRotation != 0)
                {
                    dc.PushTransform(new RotateTransform(g.GlyphRotation, g.RotationCentre.X, g.RotationCentre.Y));
                    dc.DrawText(g.Text, g.DrawPosition);
                    dc.Pop();
                }
                else
                {
                    dc.DrawText(g.Text, g.DrawPosition);
                }
            }
        }
        else if (render.SingleText is not null)
        {
            // Fast path: horizontal text, no letter spacing, optional whole-block
            // rotation. The glyphs render as a single FormattedText run rotated
            // around the centre of the AABB.
            if (render.Rotation != 0)
            {
                dc.PushTransform(new RotateTransform(
                    render.Rotation,
                    render.Size.Width / 2.0,
                    render.Size.Height / 2.0));
                var offsetX = (render.Size.Width - render.ContentSize.Width) / 2.0;
                var offsetY = (render.Size.Height - render.ContentSize.Height) / 2.0;
                dc.DrawText(render.SingleText,
                    new Point(offsetX + render.InkOriginX, offsetY + render.InkOriginY));
                dc.Pop();
            }
            else
            {
                dc.DrawText(render.SingleText,
                    new Point(render.InkOriginX, render.InkOriginY));
            }
        }
    }

    /// <summary>
    /// Resolve all options into a render plan (text geometry + brushes + size).
    /// Static so it can be called both at render time (with the live snapshot,
    /// for accurate text values) and at layout time (with a null snapshot, for
    /// auto-sizing on the resolver path before any rendering happens).
    /// </summary>
    /// <param name="snapshot">
    /// Live engine state. When null, source-resolved values are stand-ins of
    /// representative width — e.g., a button source uses pressedText so the
    /// box doesn't shrink when the button releases. The label is always honoured.
    /// </param>
    private static RenderPlan BuildPlan(WidgetConfig cfg, EngineStateSnapshot? snapshot)
    {
        var sourceKind = ReadOptionString(cfg, "sourceKind", "Mode");
        var sourceName = ReadOptionString(cfg, "sourceName", "");
        var label = ReadOptionString(cfg, "label", "");
        var value = ResolveValueStatic(cfg, snapshot, sourceKind, sourceName);

        // Compose the final string. Empty value with a non-empty label still
        // renders the label alone (useful as a static text label), but a fully
        // empty composition is treated as "nothing to draw".
        var composed = (label, value) switch
        {
            ("", "") => "",
            ("", var v) => v,
            (var l, "") => l,
            (var l, var v) => $"{l} {v}"
        };

        var fontFamily = ReadOptionString(cfg, "fontFamily", "Segoe UI");
        var fontSize = Math.Max(6, ReadOptionInt(cfg, "fontSize", 12));
        var bold = ReadOptionBool(cfg, "bold", false);
        var italic = ReadOptionBool(cfg, "italic", false);
        var underline = ReadOptionBool(cfg, "underline", false);
        var rotation = ((ReadOptionInt(cfg, "rotation", 0) % 360) + 360) % 360;
        var upright = ReadOptionBool(cfg, "verticalStack", false);
        var letterSpacing = ReadOptionInt(cfg, "letterSpacing", 0);
        var textBrush = ReadOptionColorBrush(cfg, "textColor", TextBrush);
        var showBackground = ReadOptionBool(cfg, "showBackground", false);
        var backgroundBrush = ReadOptionColorBrush(cfg, "backgroundColor", BgBrush);

        var typeface = new Typeface(
            new FontFamily(fontFamily),
            italic ? FontStyles.Italic : FontStyles.Normal,
            bold ? FontWeights.Bold : FontWeights.Normal,
            FontStretches.Normal);

        if (string.IsNullOrEmpty(composed))
        {
            return new RenderPlan
            {
                IsEmpty = true,
                Size = new Size(0, 0)
            };
        }

        // Two layout paths:
        //   - Per-glyph placement: required when the user wants upright-stacked
        //     text (each glyph remains readable along an arbitrary stack axis)
        //     or non-zero letter spacing (FormattedText has no spacing API, so
        //     glyphs must be drawn one-by-one to insert gaps).
        //   - Single FormattedText: horizontal, no letter spacing — the fast
        //     path. Whole-block rotation is applied at draw time around the
        //     widget's centre.
        var perGlyph = upright || letterSpacing != 0;

        if (!perGlyph)
        {
            return BuildSinglePlan(composed, typeface, fontSize, textBrush, underline,
                rotation, showBackground, backgroundBrush);
        }

        return BuildPerGlyphPlan(composed, typeface, fontSize, textBrush, underline,
            rotation, upright, letterSpacing, showBackground, backgroundBrush);
    }

    /// <summary>
    /// Single-FormattedText plan: horizontal layout with optional whole-block
    /// rotation. Uses ink bounds (not the FormattedText line box) to centre
    /// the visible glyphs in the padded background rect.
    /// </summary>
    private static RenderPlan BuildSinglePlan(string composed, Typeface typeface, double fontSize,
        Brush textBrush, bool underline, int rotation, bool showBackground, Brush backgroundBrush)
    {
        var single = MakeFormattedText(composed, typeface, fontSize, textBrush, underline);
        // Ink-bound rectangle of the glyph run laid out from origin (0,0). The
        // content size comes from this — not single.Width/Height — so the box
        // doesn't carry the typeface's empty descender/leading space.
        var inkBounds = single.BuildGeometry(new Point(0, 0))?.Bounds ?? Rect.Empty;
        if (inkBounds.IsEmpty)
        {
            inkBounds = new Rect(0, 0, single.Width, single.Height);
        }
        var paddedW = inkBounds.Width + 2 * Padding;
        var paddedH = inkBounds.Height + 2 * Padding;
        var inkOriginX = Padding - inkBounds.Left;
        var inkOriginY = Padding - inkBounds.Top;

        Size outerSize;
        if (rotation == 0)
        {
            outerSize = new Size(paddedW, paddedH);
        }
        else
        {
            var rad = rotation * Math.PI / 180.0;
            var cos = Math.Abs(Math.Cos(rad));
            var sin = Math.Abs(Math.Sin(rad));
            outerSize = new Size(paddedW * cos + paddedH * sin, paddedW * sin + paddedH * cos);
        }

        return new RenderPlan
        {
            IsEmpty = false,
            Size = outerSize,
            ContentSize = new Size(paddedW, paddedH),
            ShowBackground = showBackground,
            BackgroundBrush = backgroundBrush,
            Rotation = rotation,
            SingleText = single,
            InkOriginX = inkOriginX,
            InkOriginY = inkOriginY
        };
    }

    /// <summary>
    /// Per-glyph plan: each character is its own FormattedText placed along a
    /// stride axis that runs in the same direction the whole-block rotation
    /// would point (rotation=0 → horizontal, rotation=90 → top-to-bottom).
    /// When <paramref name="upright"/> is true, each glyph is counter-rotated
    /// so it stays upright on screen — letters remain readable along the
    /// rotated layout axis (e.g. diagonal text at rotation=45). When
    /// <paramref name="upright"/> is false, glyphs are rotated WITH the stride
    /// (just like the single-FormattedText path), with this path only chosen
    /// because <paramref name="letterSpacing"/> is non-zero.
    /// </summary>
    private static RenderPlan BuildPerGlyphPlan(string composed, Typeface typeface, double fontSize,
        Brush textBrush, bool underline, int rotation, bool upright, int letterSpacing,
        bool showBackground, Brush backgroundBrush)
    {
        // Build per-glyph FormattedTexts and gather each one's ink bounds.
        // Stride uses the natural horizontal advance (FormattedText.Width); the
        // rotation slider tilts that horizontal axis in screen space. The
        // "upright" toggle does NOT change the stride axis or the advance —
        // it only affects whether each glyph receives a per-glyph counter-rotation
        // when drawn.
        var glyphs = new List<(FormattedText Ft, Rect InkBounds, double Advance)>(composed.Length);
        foreach (var ch in composed)
        {
            var ft = MakeFormattedText(ch.ToString(), typeface, fontSize, textBrush, underline);
            var ink = ft.BuildGeometry(new Point(0, 0))?.Bounds ?? Rect.Empty;
            if (ink.IsEmpty) ink = new Rect(0, 0, ft.Width, ft.Height);
            glyphs.Add((ft, ink, ft.Width));
        }

        // Stride direction = (1,0) rotated by θ in screen space.
        var rad = rotation * Math.PI / 180.0;
        var cos = Math.Cos(rad);
        var sin = Math.Sin(rad);
        var strideDir = new Vector(cos, sin);

        // Walk glyphs along the stride axis. Each glyph's ink centre is anchored
        // on the stride line so mixed-width glyphs (e.g. "I" vs "M") sit
        // symmetrically. When upright is on we counter-rotate around the ink
        // centre so the visible glyph stays unrotated; when upright is off we
        // rotate around the same point so the glyphs follow the stride axis
        // (matching the single-FormattedText path's whole-block rotation).
        var positions = new List<PerGlyphPlacement>(glyphs.Count);
        double pos = 0;
        for (int i = 0; i < glyphs.Count; i++)
        {
            var (ft, ink, advance) = glyphs[i];
            var strideOffset = strideDir * pos;
            var stridePoint = new Point(strideOffset.X, strideOffset.Y);
            var inkCenter = new Point(ink.Left + ink.Width / 2.0, ink.Top + ink.Height / 2.0);

            // Compute the screen-space bounding rect for this glyph's ink so we
            // can build the overall AABB. When the glyph is rotated WITH the
            // stride (upright=false), the ink rect rotates too. When upright=true,
            // the glyph stays axis-aligned and the rect is the natural ink box
            // translated to its stride position.
            Rect screenInkRect;
            if (upright)
            {
                // Glyph ink centre lands at stridePoint; the rect is the natural
                // ink rect translated so its centre is at stridePoint.
                var rectX = stridePoint.X - ink.Width / 2.0;
                var rectY = stridePoint.Y - ink.Height / 2.0;
                screenInkRect = new Rect(rectX, rectY, ink.Width, ink.Height);
            }
            else
            {
                // Rotate the ink rect's four corners around stridePoint by θ and
                // take the AABB. The ink rect's centre is at stridePoint
                // (we centre the glyph on the stride line same way as upright).
                screenInkRect = RotatedRectAABB(stridePoint, ink.Width, ink.Height, cos, sin);
            }

            positions.Add(new PerGlyphPlacement(ft, stridePoint, inkCenter, screenInkRect));
            pos += advance + letterSpacing;
        }

        // AABB of all glyph ink rects in screen space.
        var bounds = positions[0].ScreenInkRect;
        for (int i = 1; i < positions.Count; i++)
        {
            bounds.Union(positions[i].ScreenInkRect);
        }

        // Translate every glyph so the AABB top-left is at (Padding, Padding).
        var dx = Padding - bounds.Left;
        var dy = Padding - bounds.Top;
        var placed = new List<PositionedGlyph>(positions.Count);
        foreach (var p in positions)
        {
            // The FormattedText's origin (top-left of the line box). For upright
            // glyphs we want the ink centre to land at stridePoint (translated
            // by dx/dy); the FormattedText origin sits at stridePoint - inkCenter.
            // For non-upright (rotated) glyphs we want the same anchoring, but
            // the rotation is applied around the glyph's ink centre at draw time.
            var translatedStride = new Point(p.StridePoint.X + dx, p.StridePoint.Y + dy);
            var drawPos = new Point(translatedStride.X - p.InkCenter.X, translatedStride.Y - p.InkCenter.Y);
            // Centre of rotation for non-upright glyphs (in widget-local coords)
            // is the same as the translatedStride point — that's where the ink
            // centre sits.
            placed.Add(new PositionedGlyph(p.Text, drawPos, upright ? 0.0 : (double)rotation, translatedStride));
        }

        var paddedW = bounds.Width + 2 * Padding;
        var paddedH = bounds.Height + 2 * Padding;

        return new RenderPlan
        {
            IsEmpty = false,
            Size = new Size(paddedW, paddedH),
            ContentSize = new Size(paddedW, paddedH),
            ShowBackground = showBackground,
            BackgroundBrush = backgroundBrush,
            Rotation = 0, // glyph rotation is per-glyph in PositionedGlyph
            Glyphs = placed
        };
    }

    /// <summary>
    /// AABB of a rectangle of width <paramref name="w"/> / height <paramref name="h"/>
    /// centred on <paramref name="centre"/>, rotated by the angle whose cosine and
    /// sine are <paramref name="cos"/> / <paramref name="sin"/>.
    /// </summary>
    private static Rect RotatedRectAABB(Point centre, double w, double h, double cos, double sin)
    {
        var hw = w / 2.0;
        var hh = h / 2.0;
        // Four corner offsets from centre, rotated.
        var c1 = new Point( hw * cos -  hh * sin,  hw * sin +  hh * cos);
        var c2 = new Point( hw * cos - -hh * sin,  hw * sin + -hh * cos);
        var c3 = new Point(-hw * cos -  hh * sin, -hw * sin +  hh * cos);
        var c4 = new Point(-hw * cos - -hh * sin, -hw * sin + -hh * cos);
        var minX = Math.Min(Math.Min(c1.X, c2.X), Math.Min(c3.X, c4.X));
        var maxX = Math.Max(Math.Max(c1.X, c2.X), Math.Max(c3.X, c4.X));
        var minY = Math.Min(Math.Min(c1.Y, c2.Y), Math.Min(c3.Y, c4.Y));
        var maxY = Math.Max(Math.Max(c1.Y, c2.Y), Math.Max(c3.Y, c4.Y));
        return new Rect(centre.X + minX, centre.Y + minY, maxX - minX, maxY - minY);
    }

    private readonly record struct PerGlyphPlacement(
        FormattedText Text,
        Point StridePoint,
        Point InkCenter,
        Rect ScreenInkRect);

    private static FormattedText MakeFormattedText(string text, Typeface typeface, double size, Brush brush, bool underline)
    {
        var ft = new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            typeface,
            size,
            brush,
            1.0);
        if (underline)
        {
            ft.SetTextDecorations(TextDecorations.Underline);
        }
        return ft;
    }

    /// <summary>
    /// Static value resolver — uses the supplied snapshot when available; otherwise
    /// returns a representative stand-in suitable for layout measurement (so the
    /// auto-sized box is wide enough for the worst case and doesn't change size
    /// between values where it's avoidable).
    /// </summary>
    private static string ResolveValueStatic(WidgetConfig cfg, EngineStateSnapshot? snapshot, string sourceKind, string sourceName)
    {
        switch (sourceKind)
        {
            case "Text":
                // Static text reuses the `label` field for its content (the
                // editor hides the regular Label row for this source kind so
                // the user only sees one input). Returning "" means the
                // composed string ends up being just the label.
                return "";
            case "Mode":
                return snapshot?.Mode.ToString() ?? EngineMode.Active.ToString();
            case "Profile":
                if (snapshot is null) return "(no profile)";
                return string.IsNullOrEmpty(snapshot.ProfileName) ? "(no profile)" : snapshot.ProfileName;
            case "Button":
            {
                var pressedText = ReadOptionString(cfg, "pressedText", "Pressed");
                var releasedText = ReadOptionString(cfg, "releasedText", "");
                var mask = ParseButtonMask(sourceName);
                if (mask == XInputButtons.None) return "";
                if (snapshot is null)
                {
                    // Use whichever string is wider so the box doesn't grow on press.
                    return pressedText.Length >= releasedText.Length ? pressedText : releasedText;
                }
                var pressed = (snapshot.Buttons & mask) != 0;
                return pressed ? pressedText : releasedText;
            }
            case "Axis":
            {
                if (!IsKnownAxis(sourceName)) return "";
                var format = ReadOptionString(cfg, "axisFormat", "Decimal");
                var decimals = Math.Max(0, Math.Min(4, ReadOptionInt(cfg, "axisDecimals", 2)));
                var val = snapshot is null ? 0.0 : ReadAxisStatic(snapshot, sourceName);
                if (format.Equals("Percent", StringComparison.OrdinalIgnoreCase))
                {
                    var fmt = "F" + decimals;
                    return (val * 100.0).ToString(fmt, CultureInfo.InvariantCulture) + "%";
                }
                else
                {
                    var pos = "+0." + new string('0', decimals);
                    var neg = "-0." + new string('0', decimals);
                    var zero = "0." + new string('0', decimals);
                    return val.ToString($"{pos};{neg};{zero}", CultureInfo.InvariantCulture);
                }
            }
            default:
                return "";
        }
    }

    private static bool IsKnownAxis(string source) => source switch
    {
        "LeftTrigger" or "RightTrigger" or "LeftStickX" or "LeftStickY" or "RightStickX" or "RightStickY" => true,
        _ => false
    };

    private static double ReadAxisStatic(EngineStateSnapshot s, string source) => source switch
    {
        "LeftTrigger" => s.LeftTrigger,
        "RightTrigger" => s.RightTrigger,
        "LeftStickX" => s.LeftStickX,
        "LeftStickY" => s.LeftStickY,
        "RightStickX" => s.RightStickX,
        "RightStickY" => s.RightStickY,
        _ => 0.0
    };

    // ---- Static option readers (mirror OverlayWidget instance helpers). ------------
    // Used by BuildPlan so the plan can be computed without a constructed widget
    // instance — required for the resolver's layout-time auto-size measurement.

    private static bool ReadOptionBool(WidgetConfig cfg, string key, bool fallback)
    {
        if (!cfg.Options.TryGetValue(key, out var v)) return fallback;
        return v.ValueKind switch
        {
            System.Text.Json.JsonValueKind.True => true,
            System.Text.Json.JsonValueKind.False => false,
            _ => fallback
        };
    }

    private static string ReadOptionString(WidgetConfig cfg, string key, string fallback)
    {
        if (!cfg.Options.TryGetValue(key, out var v)) return fallback;
        return v.ValueKind == System.Text.Json.JsonValueKind.String ? v.GetString() ?? fallback : fallback;
    }

    private static int ReadOptionInt(WidgetConfig cfg, string key, int fallback)
    {
        if (!cfg.Options.TryGetValue(key, out var v)) return fallback;
        return v.ValueKind == System.Text.Json.JsonValueKind.Number && v.TryGetInt32(out var n) ? n : fallback;
    }

    private static Brush ReadOptionColorBrush(WidgetConfig cfg, string key, Brush fallback)
    {
        var hex = ReadOptionString(cfg, key, "");
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

    private static XInputButtons ParseButtonMask(string source) => source switch
    {
        "A" => XInputButtons.A,
        "B" => XInputButtons.B,
        "X" => XInputButtons.X,
        "Y" => XInputButtons.Y,
        "LeftShoulder" => XInputButtons.LeftShoulder,
        "RightShoulder" => XInputButtons.RightShoulder,
        "LeftThumb" => XInputButtons.LeftThumb,
        "RightThumb" => XInputButtons.RightThumb,
        "Back" => XInputButtons.Back,
        "Start" => XInputButtons.Start,
        "Guide" => XInputButtons.Guide,
        "DPadUp" => XInputButtons.DPadUp,
        "DPadDown" => XInputButtons.DPadDown,
        "DPadLeft" => XInputButtons.DPadLeft,
        "DPadRight" => XInputButtons.DPadRight,
        _ => XInputButtons.None
    };

    /// <summary>
    /// One pre-positioned glyph. <see cref="DrawPosition"/> is the
    /// FormattedText draw origin in widget-local coordinates, with the run
    /// already translated so the glyph-collection AABB is flush against
    /// (Padding, Padding). When <see cref="GlyphRotation"/> is non-zero, the
    /// glyph is drawn rotated around <see cref="RotationCentre"/> so the
    /// per-glyph rotation matches the layout's stride angle (used for the
    /// non-upright + letter-spacing path so individual glyphs follow the
    /// rotated stride just like the single-FormattedText fast path would).
    /// </summary>
    private readonly record struct PositionedGlyph(
        FormattedText Text,
        Point DrawPosition,
        double GlyphRotation,
        Point RotationCentre);

    private sealed class RenderPlan
    {
        public bool IsEmpty;
        public Size Size;
        public Size ContentSize;
        public bool ShowBackground;
        public Brush BackgroundBrush = Brushes.Transparent;
        public int Rotation;
        public FormattedText? SingleText;
        public List<PositionedGlyph>? Glyphs;
        public double InkOriginX;
        public double InkOriginY;
    }
}
