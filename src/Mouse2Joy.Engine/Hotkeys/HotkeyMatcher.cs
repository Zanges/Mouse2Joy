using Mouse2Joy.Persistence.Models;

namespace Mouse2Joy.Engine.Hotkeys;

/// <summary>
/// Stateless matcher: given an incoming key event and the currently-held
/// modifier set, decide if a configured hotkey just fired (on key-down).
///
/// The capture loop owns its own modifier-down tracking (see
/// <see cref="HotkeyModifierTracker"/>) independent of binding suppression,
/// so a modifier bound to a gamepad output is still observable here.
/// </summary>
public static class HotkeyMatcher
{
    /// <summary>
    /// Returns true if <paramref name="hotkey"/> just fired. Hotkeys fire only
    /// on key-down of the non-modifier key while the configured modifier set
    /// is exactly held (no extra modifiers).
    /// </summary>
    public static bool Match(in RawEvent ev, HotkeyBinding? hotkey, KeyModifiers heldModifiers)
    {
        if (hotkey is null || !hotkey.IsAssigned)
        {
            return false;
        }

        if (ev.Kind != RawEventKind.Key || !ev.KeyDown)
        {
            return false;
        }

        if (!ev.Key.Equals(hotkey.Key))
        {
            return false;
        }

        if (heldModifiers != hotkey.Modifiers)
        {
            return false;
        }

        return true;
    }
}

/// <summary>
/// Tracks modifier-down state from the raw key stream. Independent of
/// binding suppression so it stays accurate even when modifiers are bound.
/// </summary>
public sealed class HotkeyModifierTracker
{
    private bool _shift, _ctrl, _alt, _win;

    public KeyModifiers Held =>
        (_shift ? KeyModifiers.Shift : 0) |
        (_ctrl ? KeyModifiers.Ctrl : 0) |
        (_alt ? KeyModifiers.Alt : 0) |
        (_win ? KeyModifiers.Win : 0);

    public void Observe(in RawEvent ev)
    {
        if (ev.Kind != RawEventKind.Key)
        {
            return;
        }

        var which = Classify(ev.Key);
        if (which == KeyModifiers.None)
        {
            return;
        }

        switch (which)
        {
            case KeyModifiers.Shift: _shift = ev.KeyDown; break;
            case KeyModifiers.Ctrl: _ctrl = ev.KeyDown; break;
            case KeyModifiers.Alt: _alt = ev.KeyDown; break;
            case KeyModifiers.Win: _win = ev.KeyDown; break;
        }
    }

    public void Reset()
    {
        _shift = _ctrl = _alt = _win = false;
    }

    /// <summary>Classify by physical scancode. Set 1 codes per common keyboards.</summary>
    public static KeyModifiers Classify(VirtualKey key)
    {
        // Non-extended set:
        //   0x2A LeftShift, 0x36 RightShift, 0x1D LeftCtrl, 0x38 LeftAlt
        // Extended set (E0-prefixed):
        //   0x1D RightCtrl, 0x38 RightAlt, 0x5B LeftWin, 0x5C RightWin
        if (!key.Extended)
        {
            return key.Scancode switch
            {
                0x2A or 0x36 => KeyModifiers.Shift,
                0x1D => KeyModifiers.Ctrl,
                0x38 => KeyModifiers.Alt,
                _ => KeyModifiers.None
            };
        }

        return key.Scancode switch
        {
            0x1D => KeyModifiers.Ctrl,
            0x38 => KeyModifiers.Alt,
            0x5B or 0x5C => KeyModifiers.Win,
            _ => KeyModifiers.None
        };
    }
}
