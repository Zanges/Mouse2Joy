using System.Windows.Controls;
using Mouse2Joy.Engine.State;
using Mouse2Joy.Persistence.Models;
using Mouse2Joy.UI.Overlay.Widgets;

namespace Mouse2Joy.UI.Overlay;

/// <summary>
/// Hosts widgets on a Canvas and propagates the engine state snapshot to each on
/// every tick. Pure presentation — no input handling, no drag, no configure mode.
/// The owning <see cref="Mouse2Joy.UI.Views.OverlayWindow"/> is permanently
/// click-through; widget positioning happens in the parent
/// <see cref="OverlayCoordinator"/> and is passed in via <see cref="LoadResolved"/>.
/// </summary>
public sealed class OverlayWidgetHost
{
    private readonly Canvas _canvas;
    private readonly List<OverlayWidget> _widgets = new();

    public OverlayWidgetHost(Canvas canvas) { _canvas = canvas; }

    /// <summary>
    /// Replace this host's widgets with the supplied set. Each entry carries the
    /// already-resolved absolute (X, Y) for the widget on this monitor.
    /// </summary>
    public void LoadResolved(IEnumerable<(WidgetConfig Config, double AbsX, double AbsY)> entries)
    {
        _canvas.Children.Clear();
        _widgets.Clear();
        foreach (var (cfg, absX, absY) in entries)
        {
            if (!cfg.Visible) continue;
            var widget = Create(cfg.Type);
            if (widget is null) continue;
            widget.Config = cfg;
            widget.Opacity = 0.85;
            Canvas.SetLeft(widget, absX);
            Canvas.SetTop(widget, absY);
            _canvas.Children.Add(widget);
            _widgets.Add(widget);
        }
    }

    public void Tick(EngineStateSnapshot snapshot)
    {
        for (int i = 0; i < _widgets.Count; i++)
            _widgets[i].RenderState(snapshot);
    }

    private static OverlayWidget? Create(string type) => type switch
    {
        "Status" => new StatusWidget(),
        "EngineStatusIndicator" => new EngineStatusIndicatorWidget(),
        "Axis" => new AxisWidget(),
        "TwoAxis" => new TwoAxisWidget(),
        "Button" => new ButtonWidget(),
        "Background" => new BackgroundWidget(),
        "MouseActivity" => new MouseActivityWidget(),
        "ButtonGrid" => new ButtonGridWidget(),
        _ => null
    };
}
