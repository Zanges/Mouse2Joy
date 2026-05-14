using Mouse2Joy.Persistence.Models;

namespace Mouse2Joy.Engine.Mapping;

internal static class ReportBuilder
{
    public static XInputReport Build(
        OutputStateBuckets buckets,
        IReadOnlyDictionary<(Stick Stick, AxisComponent Axis), double> stickFinal)
    {
        short Lx = ScaleAxis(GetStick(stickFinal, Stick.Left, AxisComponent.X));
        short Ly = ScaleAxis(GetStick(stickFinal, Stick.Left, AxisComponent.Y));
        short Rx = ScaleAxis(GetStick(stickFinal, Stick.Right, AxisComponent.X));
        short Ry = ScaleAxis(GetStick(stickFinal, Stick.Right, AxisComponent.Y));

        byte Lt = ScaleTrigger(GetTrigger(buckets, Trigger.Left));
        byte Rt = ScaleTrigger(GetTrigger(buckets, Trigger.Right));

        XInputButtons buttons = XInputButtons.None;
        foreach (var kv in buckets.Buttons)
        {
            if (kv.Value)
            {
                buttons |= MapButton(kv.Key);
            }
        }

        foreach (var kv in buckets.DPad)
        {
            if (kv.Value)
            {
                buttons |= MapDPad(kv.Key);
            }
        }

        return new XInputReport(Lx, Ly, Rx, Ry, Lt, Rt, buttons);
    }

    private static double GetStick(IReadOnlyDictionary<(Stick, AxisComponent), double> map, Stick s, AxisComponent a)
        => map.TryGetValue((s, a), out var v) ? v : 0.0;

    private static double GetTrigger(OutputStateBuckets b, Trigger t)
        => b.Triggers.TryGetValue(t, out var v) ? v : 0.0;

    private static short ScaleAxis(double v)
    {
        if (v >= 1.0)
        {
            return short.MaxValue;
        }

        if (v <= -1.0)
        {
            return short.MinValue;
        }
        // Symmetric mapping into the signed 16-bit range. We use 32767 as the
        // half-range so that 0 stays at 0; the negative side reaches -32767
        // (one short of MinValue) which is fine for XInput.
        return (short)Math.Round(v * 32767.0);
    }

    private static byte ScaleTrigger(double v)
    {
        if (v <= 0)
        {
            return 0;
        }

        if (v >= 1.0)
        {
            return 255;
        }

        return (byte)Math.Round(v * 255.0);
    }

    private static XInputButtons MapButton(GamepadButton b) => b switch
    {
        GamepadButton.A => XInputButtons.A,
        GamepadButton.B => XInputButtons.B,
        GamepadButton.X => XInputButtons.X,
        GamepadButton.Y => XInputButtons.Y,
        GamepadButton.LeftBumper => XInputButtons.LeftShoulder,
        GamepadButton.RightBumper => XInputButtons.RightShoulder,
        GamepadButton.LeftStick => XInputButtons.LeftThumb,
        GamepadButton.RightStick => XInputButtons.RightThumb,
        GamepadButton.Back => XInputButtons.Back,
        GamepadButton.Start => XInputButtons.Start,
        GamepadButton.Guide => XInputButtons.Guide,
        _ => XInputButtons.None
    };

    private static XInputButtons MapDPad(DPadDirection d) => d switch
    {
        DPadDirection.Up => XInputButtons.DPadUp,
        DPadDirection.Down => XInputButtons.DPadDown,
        DPadDirection.Left => XInputButtons.DPadLeft,
        DPadDirection.Right => XInputButtons.DPadRight,
        _ => XInputButtons.None
    };
}
