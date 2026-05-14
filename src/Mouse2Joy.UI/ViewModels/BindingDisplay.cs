using System.Runtime.InteropServices;
using System.Text;
using Mouse2Joy.Persistence.Models;

namespace Mouse2Joy.UI.ViewModels;

/// <summary>
/// Pure display formatters for <see cref="Binding"/> fields. Used by the
/// Profiles tab's bindings table to render <see cref="InputSource"/> and
/// <see cref="OutputTarget"/> as user-readable strings instead of the raw
/// record <c>ToString()</c> output.
/// </summary>
public static class BindingDisplay
{
    public static string FormatSource(InputSource src) => src switch
    {
        MouseAxisSource ma => ma.Axis switch
        {
            MouseAxis.X => "Mouse X axis",
            MouseAxis.Y => "Mouse Y axis",
            _ => $"Mouse axis {ma.Axis}"
        },
        MouseButtonSource mb => mb.Button switch
        {
            MouseButton.Left => "Left click",
            MouseButton.Right => "Right click",
            MouseButton.Middle => "Middle click",
            MouseButton.X1 => "Mouse X1",
            MouseButton.X2 => "Mouse X2",
            _ => $"Mouse {mb.Button}"
        },
        MouseScrollSource ms => ms.Direction switch
        {
            ScrollDirection.Up => "Scroll up",
            ScrollDirection.Down => "Scroll down",
            _ => $"Scroll {ms.Direction}"
        },
        KeySource ks => ks.Key.IsNone ? "(unset)" : FormatKey(ks.Key),
        _ => src.ToString() ?? "(unknown)"
    };

    public static string FormatTarget(OutputTarget tgt) => tgt switch
    {
        StickAxisTarget sa => $"{StickName(sa.Stick)} Stick {AxisName(sa.Component)}",
        TriggerTarget tt => $"{TriggerName(tt.Trigger)} Trigger",
        ButtonTarget bt => FormatButton(bt.Button),
        DPadTarget dp => $"D-Pad {DPadName(dp.Direction)}",
        _ => tgt.ToString() ?? "(unknown)"
    };

    public static string FormatAuto(InputSource src, OutputTarget tgt)
        => $"{FormatSource(src)} → {FormatTarget(tgt)}";

    private static string StickName(Stick s) => s switch
    {
        Stick.Left => "Left",
        Stick.Right => "Right",
        _ => s.ToString()
    };

    private static string AxisName(AxisComponent c) => c switch
    {
        AxisComponent.X => "X",
        AxisComponent.Y => "Y",
        _ => c.ToString()
    };

    private static string TriggerName(Trigger t) => t switch
    {
        Trigger.Left => "Left",
        Trigger.Right => "Right",
        _ => t.ToString()
    };

    private static string DPadName(DPadDirection d) => d switch
    {
        DPadDirection.Up => "Up",
        DPadDirection.Down => "Down",
        DPadDirection.Left => "Left",
        DPadDirection.Right => "Right",
        _ => d.ToString()
    };

    private static string FormatButton(GamepadButton b) => b switch
    {
        GamepadButton.A => "Button A",
        GamepadButton.B => "Button B",
        GamepadButton.X => "Button X",
        GamepadButton.Y => "Button Y",
        GamepadButton.LeftBumper => "Left Bumper",
        GamepadButton.RightBumper => "Right Bumper",
        GamepadButton.LeftStick => "Left Stick (click)",
        GamepadButton.RightStick => "Right Stick (click)",
        GamepadButton.Back => "Back",
        GamepadButton.Start => "Start",
        GamepadButton.Guide => "Guide",
        _ => b.ToString()
    };

    /// <summary>
    /// Resolve a scancode + extended flag to a locale-appropriate key name via
    /// the Win32 GetKeyNameTextW API. Falls back to a hex scancode display
    /// (e.g. "Sc:1E", "Sc:48 (E0)") on failure so the row never shows blank.
    /// </summary>
    private static string FormatKey(VirtualKey key)
    {
        // lParam encoding for GetKeyNameText: bits 16-23 = scancode, bit 24 = extended.
        var lParam = ((int)key.Scancode) << 16;
        if (key.Extended)
        {
            lParam |= 1 << 24;
        }

        var buf = new StringBuilder(64);
        var len = GetKeyNameTextW(lParam, buf, buf.Capacity);
        if (len > 0)
        {
            var name = buf.ToString();
            if (!string.IsNullOrWhiteSpace(name))
            {
                return name;
            }
        }
        return $"Sc:{key.Scancode:X2}{(key.Extended ? " (E0)" : "")}";
    }

    // StringBuilder marshalling is fine here: this is a one-shot call on
    // the UI thread per binding-row formatter, not a hot path. The CA1838
    // perf advice doesn't justify pulling in unsafe blocks.
#pragma warning disable CA1838
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetKeyNameTextW")]
    private static extern int GetKeyNameTextW(int lParam, StringBuilder lpString, int cchSize);
#pragma warning restore CA1838
}
