using Mouse2Joy.Persistence.Models;

namespace Mouse2Joy.Engine.Modifiers.SourceAdapters;

/// <summary>
/// Latched Digital signal for mouse-button and key sources. The signal stays
/// true between ticks while the input is held and flips on the matching event.
/// </summary>
internal sealed class DigitalLatchAdapter : ISourceAdapter
{
    private readonly InputSource _source;
    private bool _down;

    public DigitalLatchAdapter(InputSource source)
    {
        if (source is not (MouseButtonSource or KeySource))
            throw new ArgumentException("DigitalLatchAdapter only supports mouse-button and key sources.", nameof(source));
        _source = source;
    }

    public InputSource Source => _source;
    public SignalType OutputType => SignalType.Digital;

    public bool Matches(in RawEvent ev) => _source switch
    {
        MouseButtonSource mb => ev.Kind == RawEventKind.MouseButton && ev.MouseButton == mb.Button,
        KeySource ks => ev.Kind == RawEventKind.Key && ev.Key.Equals(ks.Key),
        _ => false
    };

    public void Apply(in RawEvent ev)
    {
        _down = _source switch
        {
            MouseButtonSource => ev.ButtonDown,
            KeySource => ev.KeyDown,
            _ => _down
        };
    }

    public Signal EndOfTick() => Signal.Digital(_down);

    public void Reset() => _down = false;
}
