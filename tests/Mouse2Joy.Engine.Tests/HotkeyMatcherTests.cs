using FluentAssertions;
using Mouse2Joy.Engine.Hotkeys;
using Mouse2Joy.Persistence.Models;

namespace Mouse2Joy.Engine.Tests;

public class HotkeyMatcherTests
{
    private static readonly VirtualKey F12 = new(0x58, false);
    private static readonly VirtualKey LeftShift = new(0x2A, false);

    [Fact]
    public void Matches_on_keydown_with_correct_modifiers()
    {
        var hk = new HotkeyBinding(F12, KeyModifiers.Shift);
        var ev = RawEvent.ForKey(F12, true, KeyModifiers.None, 0);
        HotkeyMatcher.Match(in ev, hk, KeyModifiers.Shift).Should().BeTrue();
    }

    [Fact]
    public void Does_not_match_on_keyup()
    {
        var hk = new HotkeyBinding(F12, KeyModifiers.None);
        var ev = RawEvent.ForKey(F12, false, KeyModifiers.None, 0);
        HotkeyMatcher.Match(in ev, hk, KeyModifiers.None).Should().BeFalse();
    }

    [Fact]
    public void Rejects_extra_modifiers()
    {
        var hk = new HotkeyBinding(F12, KeyModifiers.Shift);
        var ev = RawEvent.ForKey(F12, true, KeyModifiers.None, 0);
        HotkeyMatcher.Match(in ev, hk, KeyModifiers.Shift | KeyModifiers.Ctrl).Should().BeFalse();
    }

    [Fact]
    public void Rejects_wrong_scancode()
    {
        var hk = new HotkeyBinding(F12, KeyModifiers.None);
        var other = new VirtualKey(0x57, false); // F11
        var ev = RawEvent.ForKey(other, true, KeyModifiers.None, 0);
        HotkeyMatcher.Match(in ev, hk, KeyModifiers.None).Should().BeFalse();
    }

    [Fact]
    public void Null_or_unassigned_hotkey_never_matches()
    {
        var ev = RawEvent.ForKey(F12, true, KeyModifiers.None, 0);
        HotkeyMatcher.Match(in ev, null, KeyModifiers.None).Should().BeFalse();
        HotkeyMatcher.Match(in ev, new HotkeyBinding(VirtualKey.None, KeyModifiers.None), KeyModifiers.None).Should().BeFalse();
    }

    [Fact]
    public void Modifier_tracker_tracks_left_shift_down_and_up()
    {
        var t = new HotkeyModifierTracker();
        t.Held.Should().Be(KeyModifiers.None);
        t.Observe(RawEvent.ForKey(LeftShift, true, KeyModifiers.None, 0));
        t.Held.Should().Be(KeyModifiers.Shift);
        t.Observe(RawEvent.ForKey(LeftShift, false, KeyModifiers.None, 0));
        t.Held.Should().Be(KeyModifiers.None);
    }

    [Fact]
    public void Modifier_tracker_classifies_extended_keys()
    {
        // Right Ctrl (E0 1D) and right Alt (E0 38)
        HotkeyModifierTracker.Classify(new VirtualKey(0x1D, true)).Should().Be(KeyModifiers.Ctrl);
        HotkeyModifierTracker.Classify(new VirtualKey(0x38, true)).Should().Be(KeyModifiers.Alt);
        HotkeyModifierTracker.Classify(new VirtualKey(0x5B, true)).Should().Be(KeyModifiers.Win);
    }
}
