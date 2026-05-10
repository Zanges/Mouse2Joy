namespace Mouse2Joy.Persistence.Models;

public enum MouseAxis { X, Y }

public enum MouseButton { Left, Right, Middle, X1, X2 }

public enum ScrollDirection { Up, Down }

public enum Stick { Left, Right }

public enum AxisComponent { X, Y }

public enum Trigger { Left, Right }

public enum DPadDirection { Up, Down, Left, Right }

public enum GamepadButton
{
    A,
    B,
    X,
    Y,
    LeftBumper,
    RightBumper,
    LeftStick,
    RightStick,
    Back,
    Start,
    Guide
}

[Flags]
public enum KeyModifiers
{
    None = 0,
    Shift = 1 << 0,
    Ctrl = 1 << 1,
    Alt = 1 << 2,
    Win = 1 << 3
}

/// <summary>
/// A physical scancode (set 1) plus the E0 prefix flag, identifying a key
/// independent of keyboard layout. Stored this way so hotkeys survive
/// localization changes.
/// </summary>
public readonly record struct VirtualKey(ushort Scancode, bool Extended)
{
    public static VirtualKey None => new(0, false);
    public bool IsNone => Scancode == 0;
}
