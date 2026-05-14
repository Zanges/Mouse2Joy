using System.Windows;
using Microsoft.Win32;
using Mouse2Joy.Engine;
using Mouse2Joy.Persistence.Models;
using Mouse2Joy.UI.Interop;
using Mouse2Joy.UI.Overlay.Widgets;
using Mouse2Joy.UI.Views;

namespace Mouse2Joy.UI.Overlay;

/// <summary>
/// Owns one click-through <see cref="OverlayWindow"/> per connected monitor.
/// Resolves widget parent/offset relations into absolute per-monitor positions
/// and dispatches each widget to the right window. Listens for display changes
/// and re-applies the layout when monitors come and go.
/// </summary>
public sealed class OverlayCoordinator : IDisposable
{
    private readonly InputEngine _engine;
    private readonly Dictionary<int, OverlayWindow> _windows = new();
    private OverlayLayout? _lastLayout;
    private bool _shown;
    private bool _disposed;

    public OverlayCoordinator(InputEngine engine)
    {
        _engine = engine;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
    }

    /// <summary>Replace the layout that this coordinator renders.</summary>
    public void Apply(OverlayLayout layout)
    {
        _lastLayout = layout;
        if (_shown) Render();
    }

    public void Show()
    {
        _shown = true;
        EnsureWindowsForMonitors();
        Render();
        foreach (var w in _windows.Values) w.Show();
    }

    public void Hide()
    {
        _shown = false;
        foreach (var w in _windows.Values) w.Hide();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        foreach (var w in _windows.Values)
        {
            try { w.Close(); } catch { /* swallow on shutdown */ }
        }
        _windows.Clear();
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        // The event can fire on a background thread — marshal to the UI dispatcher.
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            EnsureWindowsForMonitors();
            if (_shown) Render();
        });
    }

    private void EnsureWindowsForMonitors()
    {
        var monitors = MonitorEnumerator.Enumerate();
        var seen = new HashSet<int>();
        foreach (var m in monitors)
        {
            seen.Add(m.Index);
            if (!_windows.TryGetValue(m.Index, out var w))
            {
                w = new OverlayWindow(m);
                w.AttachEngine(_engine);
                _windows[m.Index] = w;
                if (_shown) w.Show();
            }
            else
            {
                // Monitor may have moved or resized — re-apply bounds.
                var dip = m.BoundsDip;
                w.Left = dip.X;
                w.Top = dip.Y;
                w.Width = dip.Width;
                w.Height = dip.Height;
            }
        }
        // Close windows for monitors that no longer exist.
        foreach (var idx in _windows.Keys.Where(k => !seen.Contains(k)).ToList())
        {
            try { _windows[idx].Close(); } catch { /* swallow */ }
            _windows.Remove(idx);
        }
    }

    /// <summary>
    /// One widget's resolved render rectangle in monitor-local DIPs, plus the
    /// effective monitor index it belongs on. <c>X</c>/<c>Y</c> are the widget's
    /// top-left in canvas coordinates (what <see cref="System.Windows.Controls.Canvas.SetLeft"/>
    /// expects); <c>W</c>/<c>H</c> are the rendered size.
    /// </summary>
    private readonly record struct ResolvedRect(double X, double Y, double W, double H, int Mon);

    /// <summary>
    /// Resolves each widget to its render rectangle and pushes per-monitor buckets
    /// into the corresponding overlay window.
    /// </summary>
    private void Render()
    {
        if (_lastLayout is null) return;
        if (_windows.Count == 0) return;

        var byId = _lastLayout.Widgets
            .GroupBy(w => w.Id)
            // Defensive: if duplicate ids slip in, take the first; resolution still proceeds.
            .ToDictionary(g => g.Key, g => g.First());

        // Pre-resolve every widget's monitor-local rectangle. Walks parent chains
        // recursively, memoized so each widget is resolved once per Render().
        var resolved = new Dictionary<string, ResolvedRect>();
        foreach (var w in _lastLayout.Widgets)
        {
            if (resolved.ContainsKey(w.Id)) continue;
            Resolve(w, byId, resolved);
        }

        // Bucket by monitor and push to the right window. Widgets pointing at a
        // monitor that doesn't exist fall back to the lowest available index.
        var fallbackIndex = _windows.Keys.Min();
        var buckets = new Dictionary<int, List<(WidgetConfig, double, double)>>();
        foreach (var w in _lastLayout.Widgets)
        {
            if (!resolved.TryGetValue(w.Id, out var r)) continue;
            var monIdx = _windows.ContainsKey(r.Mon) ? r.Mon : fallbackIndex;
            if (!buckets.TryGetValue(monIdx, out var list))
                buckets[monIdx] = list = new();
            list.Add((w, r.X, r.Y));
        }

        foreach (var kv in _windows)
        {
            buckets.TryGetValue(kv.Key, out var entries);
            kv.Value.LoadResolved(entries ?? Enumerable.Empty<(WidgetConfig, double, double)>());
        }
    }

    private ResolvedRect Resolve(
        WidgetConfig cfg,
        IReadOnlyDictionary<string, WidgetConfig> byId,
        Dictionary<string, ResolvedRect> memo)
    {
        if (memo.TryGetValue(cfg.Id, out var done)) return done;

        // Width/Height live directly on the config now — no scale multiplier.
        // Auto-sized widgets (Status text widget) ignore the config dims and
        // measure themselves from font + content; query the type's static
        // measurer so anchor math (e.g. SelfAnchor=Bottom) lines up with the
        // actual rendered size rather than stale persisted dims.
        var size = MeasureForLayout(cfg);

        // Cycle defense: pre-seed memo so a re-entry resolves to a degenerate
        // top-left placement instead of stack-overflowing on a malformed file.
        memo[cfg.Id] = new ResolvedRect(0, 0, size.Width, size.Height, cfg.MonitorIndex);

        // Determine the reference frame (a Rect in monitor-local DIPs) and the
        // effective monitor for this widget.
        Rect frame;
        int monIdx;
        if (!string.IsNullOrEmpty(cfg.ParentId) && byId.TryGetValue(cfg.ParentId, out var parent))
        {
            var pr = Resolve(parent, byId, memo);
            frame = new Rect(pr.X, pr.Y, pr.W, pr.H);
            monIdx = pr.Mon;
        }
        else
        {
            // Root widget: anchor against the monitor bounds in monitor-local DIPs.
            // We don't actually need the monitor's size from the live monitor list —
            // the overlay window is sized to the monitor, so the frame is (0,0,W,H)
            // where W/H come from the monitor enumeration. We look it up here so
            // anchoring against the monitor's bottom-right etc. works regardless of
            // whether the monitor is currently connected.
            var monSize = LookupMonitorSize(cfg.MonitorIndex);
            frame = new Rect(0, 0, monSize.Width, monSize.Height);
            monIdx = cfg.MonitorIndex;
        }

        // anchor on the reference frame, then apply user offset; subtract the
        // self-anchor offset on the widget itself to get the widget's top-left.
        var anchorPx = AnchorPoint(frame, cfg.AnchorPoint);
        var selfPx = AnchorPoint(new Rect(0, 0, size.Width, size.Height), cfg.SelfAnchor);
        var x = anchorPx.X + cfg.X - selfPx.X;
        var y = anchorPx.Y + cfg.Y - selfPx.Y;

        var result = new ResolvedRect(x, y, size.Width, size.Height, monIdx);
        memo[cfg.Id] = result;
        return result;
    }

    /// <summary>
    /// Returns the layout-time size for a widget. Most widgets simply use their
    /// stored Width/Height; auto-sized text widgets (Status) measure from font +
    /// content so the anchor math lines up with the actual rendered footprint.
    /// </summary>
    private static Size MeasureForLayout(WidgetConfig cfg)
    {
        if (cfg.Type == "Status")
        {
            return StatusWidget.MeasureFootprint(cfg);
        }
        return new Size(Math.Max(0, cfg.Width), Math.Max(0, cfg.Height));
    }

    /// <summary>
    /// Look up the monitor's size from the most-recently-enumerated monitor list.
    /// Falls back to a sane default (the primary's size, or a guess) if the
    /// requested index isn't present — the resolver still produces *some* placement
    /// rather than stranding the widget.
    /// </summary>
    private Size LookupMonitorSize(int index)
    {
        if (_windows.TryGetValue(index, out var w))
            return new Size(w.Width, w.Height);
        if (_windows.Count > 0)
        {
            var any = _windows.Values.First();
            return new Size(any.Width, any.Height);
        }
        return new Size(1920, 1080);
    }

    /// <summary>
    /// Returns the absolute point on <paramref name="r"/> at the given anchor
    /// (e.g. <see cref="Anchor.BottomRight"/> = <c>(r.Right, r.Bottom)</c>).
    /// </summary>
    private static Point AnchorPoint(Rect r, Anchor a) => a switch
    {
        Anchor.TopLeft => new Point(r.Left, r.Top),
        Anchor.Top => new Point(r.Left + r.Width / 2.0, r.Top),
        Anchor.TopRight => new Point(r.Right, r.Top),
        Anchor.Left => new Point(r.Left, r.Top + r.Height / 2.0),
        Anchor.Center => new Point(r.Left + r.Width / 2.0, r.Top + r.Height / 2.0),
        Anchor.Right => new Point(r.Right, r.Top + r.Height / 2.0),
        Anchor.BottomLeft => new Point(r.Left, r.Bottom),
        Anchor.Bottom => new Point(r.Left + r.Width / 2.0, r.Bottom),
        Anchor.BottomRight => new Point(r.Right, r.Bottom),
        _ => new Point(r.Left, r.Top)
    };
}
