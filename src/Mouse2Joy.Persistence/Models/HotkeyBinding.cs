namespace Mouse2Joy.Persistence.Models;

/// <summary>
/// A hotkey identified by a physical scancode plus a normalized modifier
/// bitmask. Matched layout-independent.
/// </summary>
public sealed record HotkeyBinding(VirtualKey Key, KeyModifiers Modifiers)
{
    public bool IsAssigned => !Key.IsNone;
}
