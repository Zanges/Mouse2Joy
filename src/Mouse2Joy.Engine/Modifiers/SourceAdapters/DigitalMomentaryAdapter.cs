using Mouse2Joy.Persistence.Models;

namespace Mouse2Joy.Engine.Modifiers.SourceAdapters;

/// <summary>
/// Momentary Digital for scroll: latched true on the tick a matching scroll
/// event arrives, reset to false at end-of-tick. Mirrors today's "treat
/// scroll as a momentary press; release happens via tick reset" rule.
/// </summary>
internal sealed class DigitalMomentaryAdapter : ISourceAdapter
{
    private readonly MouseScrollSource _source;
    private bool _firedThisTick;

    public DigitalMomentaryAdapter(MouseScrollSource source)
    {
        _source = source;
    }

    public InputSource Source => _source;
    public SignalType OutputType => SignalType.Digital;

    public bool Matches(in RawEvent ev)
        => ev.Kind == RawEventKind.MouseScroll && ev.Scroll == _source.Direction;

    public void Apply(in RawEvent ev)
    {
        _firedThisTick = true;
    }

    public Signal EndOfTick()
    {
        var sig = Signal.Digital(_firedThisTick);
        _firedThisTick = false;
        return sig;
    }

    public void Reset() => _firedThisTick = false;
}
