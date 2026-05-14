using System.Windows;
using System.Windows.Threading;
using Mouse2Joy.Engine;
using Mouse2Joy.Persistence.Models;
using Mouse2Joy.UI.Interop;
using Mouse2Joy.UI.Overlay;

namespace Mouse2Joy.UI.Views;

/// <summary>
/// One per-monitor click-through HUD window. Owned by <see cref="OverlayCoordinator"/>;
/// receives engine ticks via the dispatcher timer and forwards them to its
/// <see cref="OverlayWidgetHost"/>. Permanently non-interactive.
/// </summary>
public partial class OverlayWindow : Window
{
    private readonly OverlayWidgetHost _host;
    private readonly DispatcherTimer _timer;
    private InputEngine? _engine;

    public MonitorInfo MonitorInfo { get; }

    public OverlayWindow(MonitorInfo monitor)
    {
        InitializeComponent();
        MonitorInfo = monitor;

        var dip = monitor.BoundsDip;
        Left = dip.X;
        Top = dip.Y;
        Width = dip.Width;
        Height = dip.Height;

        _host = new OverlayWidgetHost(HostCanvas);

        _timer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(16)  // ~60 Hz
        };
        _timer.Tick += OnUiTick;
    }

    public void AttachEngine(InputEngine engine) => _engine = engine;

    /// <summary>
    /// Replace this window's widgets. Entries carry already-resolved absolute
    /// positions in the *monitor's local DIP space* (top-left of the monitor = 0,0).
    /// </summary>
    public void LoadResolved(IEnumerable<(WidgetConfig Config, double AbsX, double AbsY)> entries)
        => _host.LoadResolved(entries);

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        WindowStyles.MakeOverlay(this, clickThrough: true);
        _timer.Start();
    }

    protected override void OnClosed(EventArgs e)
    {
        _timer.Stop();
        base.OnClosed(e);
    }

    private void OnUiTick(object? sender, EventArgs e)
    {
        if (_engine is null)
        {
            return;
        }

        _host.Tick(_engine.Current);
    }
}
