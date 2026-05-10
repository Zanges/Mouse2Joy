using Mouse2Joy.Persistence.Models;

namespace Mouse2Joy.Engine.Modifiers;

/// <summary>
/// Tagged-union value carrying a chain's current signal. Exactly one of
/// the three slots is meaningful per <see cref="Type"/>; the others are
/// undefined. Pass by <c>in</c> ref to avoid per-tick allocation pressure.
/// </summary>
public readonly struct Signal
{
    public readonly SignalType Type;
    public readonly double ScalarValue;
    public readonly double DeltaValue;
    public readonly bool DigitalValue;

    private Signal(SignalType type, double scalar, double delta, bool digital)
    {
        Type = type;
        ScalarValue = scalar;
        DeltaValue = delta;
        DigitalValue = digital;
    }

    public static Signal Scalar(double v) => new(SignalType.Scalar, v, 0, false);
    public static Signal Delta(double v) => new(SignalType.Delta, 0, v, false);
    public static Signal Digital(bool v) => new(SignalType.Digital, 0, 0, v);

    public static readonly Signal ZeroScalar = new(SignalType.Scalar, 0, 0, false);
    public static readonly Signal ZeroDelta = new(SignalType.Delta, 0, 0, false);
    public static readonly Signal FalseDigital = new(SignalType.Digital, 0, 0, false);
}
