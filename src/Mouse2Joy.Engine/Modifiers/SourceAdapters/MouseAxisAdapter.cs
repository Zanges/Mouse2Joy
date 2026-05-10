using Mouse2Joy.Persistence.Models;

namespace Mouse2Joy.Engine.Modifiers.SourceAdapters;

/// <summary>Accumulates signed mouse-count deltas within a tick; emits a Delta signal at end-of-tick and resets.</summary>
internal sealed class MouseAxisAdapter : ISourceAdapter
{
    private readonly MouseAxisSource _source;
    private double _accumulated;

    public MouseAxisAdapter(MouseAxisSource source)
    {
        _source = source;
    }

    public InputSource Source => _source;
    public SignalType OutputType => SignalType.Delta;

    public bool Matches(in RawEvent ev)
        => ev.Kind == RawEventKind.MouseMove;

    public void Apply(in RawEvent ev)
    {
        var delta = _source.Axis == MouseAxis.X ? ev.MouseDeltaX : ev.MouseDeltaY;
        _accumulated += delta;
    }

    public Signal EndOfTick()
    {
        var sig = Signal.Delta(_accumulated);
        _accumulated = 0;
        return sig;
    }

    public void Reset() => _accumulated = 0;
}
