using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Mouse2Joy.Persistence.Models;

namespace Mouse2Joy.UI.Controls;

public sealed class KeyCaptureBox : TextBox
{
    public static readonly DependencyProperty CapturedKeyProperty =
        DependencyProperty.Register(nameof(CapturedKey), typeof(VirtualKey), typeof(KeyCaptureBox),
            new FrameworkPropertyMetadata(VirtualKey.None,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnAnyChanged));

    public static readonly DependencyProperty CapturedModifiersProperty =
        DependencyProperty.Register(nameof(CapturedModifiers), typeof(KeyModifiers), typeof(KeyCaptureBox),
            new FrameworkPropertyMetadata(KeyModifiers.None,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnAnyChanged));

    public VirtualKey CapturedKey
    {
        get => (VirtualKey)GetValue(CapturedKeyProperty);
        set => SetValue(CapturedKeyProperty, value);
    }

    public KeyModifiers CapturedModifiers
    {
        get => (KeyModifiers)GetValue(CapturedModifiersProperty);
        set => SetValue(CapturedModifiersProperty, value);
    }

    public KeyCaptureBox()
    {
        IsReadOnly = true;
        IsReadOnlyCaretVisible = false;
        Text = "(click then press a key)";
        PreviewKeyDown += OnPreviewKeyDown;
    }

    private static void OnAnyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is KeyCaptureBox b) b.UpdateText();
    }

    private void UpdateText()
    {
        if (CapturedKey.IsNone)
        {
            Text = IsFocused ? "(press a key)" : "(none)";
            return;
        }
        var parts = new List<string>();
        if ((CapturedModifiers & KeyModifiers.Ctrl) != 0) parts.Add("Ctrl");
        if ((CapturedModifiers & KeyModifiers.Shift) != 0) parts.Add("Shift");
        if ((CapturedModifiers & KeyModifiers.Alt) != 0) parts.Add("Alt");
        if ((CapturedModifiers & KeyModifiers.Win) != 0) parts.Add("Win");
        parts.Add($"Sc:{CapturedKey.Scancode:X2}{(CapturedKey.Extended ? "(E0)" : "")}");
        Text = string.Join("+", parts);
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        UpdateText();
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        UpdateText();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        // Get scancode + extended flag from the WPF Key.
        var scan = (ushort)KeyInterop.VirtualKeyFromKey(key);
        // Map WPF Key -> Win32 VK code -> scancode via MapVirtualKey if needed.
        // For our purposes, map the most useful keys. This is a best-effort
        // capture path; physical scancode parity comes from the Interception
        // hook later. UI is just a prototype-of-binding tool.
        var (sc, ext) = MapScancode(key);
        var mods = KeyModifiers.None;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) mods |= KeyModifiers.Ctrl;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) mods |= KeyModifiers.Shift;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) mods |= KeyModifiers.Alt;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Windows)) mods |= KeyModifiers.Win;

        // Don't capture pure-modifier keys as the "main" key.
        if (IsModifierKey(key)) return;

        CapturedKey = new VirtualKey(sc, ext);
        CapturedModifiers = mods;
    }

    private static bool IsModifierKey(Key k) => k is
        Key.LeftCtrl or Key.RightCtrl or
        Key.LeftShift or Key.RightShift or
        Key.LeftAlt or Key.RightAlt or
        Key.LWin or Key.RWin;

    private static (ushort Sc, bool Ext) MapScancode(Key key) => key switch
    {
        Key.F1 => (0x3B, false),
        Key.F2 => (0x3C, false),
        Key.F3 => (0x3D, false),
        Key.F4 => (0x3E, false),
        Key.F5 => (0x3F, false),
        Key.F6 => (0x40, false),
        Key.F7 => (0x41, false),
        Key.F8 => (0x42, false),
        Key.F9 => (0x43, false),
        Key.F10 => (0x44, false),
        Key.F11 => (0x57, false),
        Key.F12 => (0x58, false),
        Key.A => (0x1E, false),
        Key.B => (0x30, false),
        Key.C => (0x2E, false),
        Key.D => (0x20, false),
        Key.E => (0x12, false),
        Key.F => (0x21, false),
        Key.G => (0x22, false),
        Key.H => (0x23, false),
        Key.I => (0x17, false),
        Key.J => (0x24, false),
        Key.K => (0x25, false),
        Key.L => (0x26, false),
        Key.M => (0x32, false),
        Key.N => (0x31, false),
        Key.O => (0x18, false),
        Key.P => (0x19, false),
        Key.Q => (0x10, false),
        Key.R => (0x13, false),
        Key.S => (0x1F, false),
        Key.T => (0x14, false),
        Key.U => (0x16, false),
        Key.V => (0x2F, false),
        Key.W => (0x11, false),
        Key.X => (0x2D, false),
        Key.Y => (0x15, false),
        Key.Z => (0x2C, false),
        Key.D0 => (0x0B, false),
        Key.D1 => (0x02, false),
        Key.D2 => (0x03, false),
        Key.D3 => (0x04, false),
        Key.D4 => (0x05, false),
        Key.D5 => (0x06, false),
        Key.D6 => (0x07, false),
        Key.D7 => (0x08, false),
        Key.D8 => (0x09, false),
        Key.D9 => (0x0A, false),
        Key.Space => (0x39, false),
        Key.Tab => (0x0F, false),
        Key.Enter => (0x1C, false),
        Key.Escape => (0x01, false),
        Key.Back => (0x0E, false),
        Key.Up => (0x48, true),
        Key.Down => (0x50, true),
        Key.Left => (0x4B, true),
        Key.Right => (0x4D, true),
        Key.Home => (0x47, true),
        Key.End => (0x4F, true),
        Key.PageUp => (0x49, true),
        Key.PageDown => (0x51, true),
        Key.Insert => (0x52, true),
        Key.Delete => (0x53, true),
        _ => ((ushort)KeyInterop.VirtualKeyFromKey(key), false)
    };
}
