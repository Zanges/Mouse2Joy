namespace Mouse2Joy.Engine.State;

public enum EngineMode
{
    /// <summary>Capture is on (so hotkeys work), but no virtual pad is connected and no input is being suppressed. Safe idle state.</summary>
    Off,
    /// <summary>Capture is on, pad is connected, bindings translate input into pad reports. Selective suppression of bound inputs.</summary>
    Active,
    /// <summary>Capture is on (for hotkeys), pad is connected and idle, real input passes through to OS untouched. Lets the user navigate game menus without "unplugging" the controller.</summary>
    SoftMuted
}

/// <summary>
/// Immutable snapshot of engine state. Single writer (engine tick) replaces
/// the reference each tick via <see cref="Volatile.Write"/>; many readers
/// (overlay, UI status strip) read the latest reference lock-free. One
/// allocation per tick — gen0-only, negligible GC pressure.
/// </summary>
public sealed record EngineStateSnapshot(
    EngineMode Mode,
    string ProfileName,
    double LeftStickX,
    double LeftStickY,
    double RightStickX,
    double RightStickY,
    double LeftTrigger,
    double RightTrigger,
    XInputButtons Buttons,
    int RawMouseDeltaX,
    int RawMouseDeltaY,
    long TickIndex)
{
    public static EngineStateSnapshot Empty { get; } = new(
        Mode: EngineMode.Off,
        ProfileName: "",
        LeftStickX: 0, LeftStickY: 0,
        RightStickX: 0, RightStickY: 0,
        LeftTrigger: 0, RightTrigger: 0,
        Buttons: XInputButtons.None,
        RawMouseDeltaX: 0, RawMouseDeltaY: 0,
        TickIndex: 0);
}
