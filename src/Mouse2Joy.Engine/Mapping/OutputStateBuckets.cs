using Mouse2Joy.Engine.StickModels;
using Mouse2Joy.Persistence.Models;

namespace Mouse2Joy.Engine.Mapping;

/// <summary>
/// Per-tick output staging area, owned exclusively by the engine tick
/// thread. Holds the latest known state of every gamepad output so the
/// final <see cref="XInputReport"/> can be composed at end-of-tick.
/// </summary>
internal sealed class OutputStateBuckets
{
    // Discrete outputs are held latched: a button is "down" until the
    // resolver records a key-up for that mapping.
    public readonly Dictionary<GamepadButton, bool> Buttons = new();
    public readonly Dictionary<DPadDirection, bool> DPad = new();

    // Triggers are analog (0..1). For digital sources mapped to a trigger
    // (e.g. a key), we pulse 1.0 on press, 0.0 on release.
    public readonly Dictionary<Trigger, double> Triggers = new();

    // Per stick axis we keep the *direct* contribution (e.g. from a key
    // mapped to "+LeftStick.X" when held = +1) plus the accumulated
    // mouse-delta routed through any mouse-axis stick processor for that
    // axis. A binding-keyed dictionary holds processors so each binding
    // owns its state across ticks.
    public readonly Dictionary<(Stick Stick, AxisComponent Axis), double> StickDirect = new();
    public readonly Dictionary<Guid, IStickProcessor> StickProcessors = new();

    public void ResetForIdleReport()
    {
        Buttons.Clear();
        DPad.Clear();
        Triggers.Clear();
        StickDirect.Clear();
        // Keep processors but reset their internal state so resuming from
        // a soft-mute starts clean.
        foreach (var p in StickProcessors.Values)
            p.Reset();
    }
}
