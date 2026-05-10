using Mouse2Joy.Persistence.Models;

namespace Mouse2Joy.Engine;

public enum RawEventKind
{
    MouseMove,
    MouseButton,
    MouseScroll,
    Key
}

/// <summary>
/// A normalized, value-typed event flowing from the input backend to the engine.
/// Held in a <see cref="System.Threading.Channels.Channel{T}"/> — keep it small.
/// </summary>
public readonly record struct RawEvent(
    RawEventKind Kind,
    int MouseDeltaX,
    int MouseDeltaY,
    MouseButton MouseButton,
    bool ButtonDown,
    ScrollDirection Scroll,
    int ScrollClicks,
    VirtualKey Key,
    bool KeyDown,
    KeyModifiers Modifiers,
    long TimestampTicks)
{
    public static RawEvent ForMouseMove(int dx, int dy, long ticks) =>
        new(RawEventKind.MouseMove, dx, dy, default, false, default, 0, default, false, KeyModifiers.None, ticks);

    public static RawEvent ForMouseButton(MouseButton button, bool down, KeyModifiers mods, long ticks) =>
        new(RawEventKind.MouseButton, 0, 0, button, down, default, 0, default, false, mods, ticks);

    public static RawEvent ForMouseScroll(ScrollDirection dir, int clicks, KeyModifiers mods, long ticks) =>
        new(RawEventKind.MouseScroll, 0, 0, default, false, dir, clicks, default, false, mods, ticks);

    public static RawEvent ForKey(VirtualKey key, bool down, KeyModifiers mods, long ticks) =>
        new(RawEventKind.Key, 0, 0, default, false, default, 0, key, down, mods, ticks);
}
