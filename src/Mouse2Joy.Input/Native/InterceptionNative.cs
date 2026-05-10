using System.Runtime.InteropServices;

namespace Mouse2Joy.Input.Native;

/// <summary>
/// P/Invoke layer over the Oblitum Interception driver's user-mode DLL
/// (interception.dll). Original native API by Francisco Lopes, MPL-licensed.
/// We bind to the documented C ABI directly so we own the swallow/forward
/// decision in our hook loop.
/// </summary>
internal static class InterceptionNative
{
    public const int INTERCEPTION_MAX_KEYBOARD = 10;
    public const int INTERCEPTION_MAX_MOUSE = 10;
    public const int INTERCEPTION_MAX_DEVICE = INTERCEPTION_MAX_KEYBOARD + INTERCEPTION_MAX_MOUSE;

    public const ushort INTERCEPTION_FILTER_KEY_NONE = 0x0000;
    public const ushort INTERCEPTION_FILTER_KEY_ALL = 0xFFFF;

    public const ushort INTERCEPTION_FILTER_MOUSE_NONE = 0x0000;
    public const ushort INTERCEPTION_FILTER_MOUSE_ALL = 0xFFFF;

    [Flags]
    public enum KeyState : ushort
    {
        Down = 0x00,
        Up = 0x01,
        E0 = 0x02,
        E1 = 0x04,
        TermsrvSetLED = 0x08,
        TermsrvShadow = 0x10,
        TermsrvVKPacket = 0x20
    }

    [Flags]
    public enum MouseState : ushort
    {
        None = 0x0000,
        LeftButtonDown = 0x0001,
        LeftButtonUp = 0x0002,
        RightButtonDown = 0x0004,
        RightButtonUp = 0x0008,
        MiddleButtonDown = 0x0010,
        MiddleButtonUp = 0x0020,
        Button4Down = 0x0040,
        Button4Up = 0x0080,
        Button5Down = 0x0100,
        Button5Up = 0x0200,
        Wheel = 0x0400,
        HWheel = 0x0800
    }

    [Flags]
    public enum MouseFlags : ushort
    {
        MoveRelative = 0x000,
        MoveAbsolute = 0x001,
        VirtualDesktop = 0x002,
        AttributesChanged = 0x004,
        MoveNoCoalesce = 0x008,
        TermsrvSrcShadow = 0x100
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KeyStroke
    {
        public ushort Code;
        public KeyState State;
        public uint Information;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MouseStroke
    {
        public MouseState State;
        public MouseFlags Flags;
        public short Rolling;
        public int X;
        public int Y;
        public uint Information;
    }

    /// <summary>Discriminated wrapper that the native API uses for the stroke buffer. Layout-sized for the larger of the two (mouse).</summary>
    [StructLayout(LayoutKind.Explicit, Size = 16)]
    public struct Stroke
    {
        [FieldOffset(0)] public KeyStroke Key;
        [FieldOffset(0)] public MouseStroke Mouse;
    }

    public delegate int InterceptionPredicate(int device);

    private const string Dll = "interception.dll";

    [DllImport(Dll, EntryPoint = "interception_create_context", CallingConvention = CallingConvention.Cdecl)]
    public static extern nint CreateContext();

    [DllImport(Dll, EntryPoint = "interception_destroy_context", CallingConvention = CallingConvention.Cdecl)]
    public static extern void DestroyContext(nint context);

    [DllImport(Dll, EntryPoint = "interception_get_precedence", CallingConvention = CallingConvention.Cdecl)]
    public static extern int GetPrecedence(nint context, int device);

    [DllImport(Dll, EntryPoint = "interception_set_precedence", CallingConvention = CallingConvention.Cdecl)]
    public static extern void SetPrecedence(nint context, int device, int precedence);

    [DllImport(Dll, EntryPoint = "interception_get_filter", CallingConvention = CallingConvention.Cdecl)]
    public static extern ushort GetFilter(nint context, int device);

    [DllImport(Dll, EntryPoint = "interception_set_filter", CallingConvention = CallingConvention.Cdecl)]
    public static extern void SetFilter(nint context, InterceptionPredicate predicate, ushort filter);

    [DllImport(Dll, EntryPoint = "interception_wait", CallingConvention = CallingConvention.Cdecl)]
    public static extern int Wait(nint context);

    [DllImport(Dll, EntryPoint = "interception_wait_with_timeout", CallingConvention = CallingConvention.Cdecl)]
    public static extern int WaitWithTimeout(nint context, ulong milliseconds);

    [DllImport(Dll, EntryPoint = "interception_send", CallingConvention = CallingConvention.Cdecl)]
    public static extern int Send(nint context, int device, ref Stroke stroke, uint nstroke);

    [DllImport(Dll, EntryPoint = "interception_receive", CallingConvention = CallingConvention.Cdecl)]
    public static extern int Receive(nint context, int device, ref Stroke stroke, uint nstroke);

    [DllImport(Dll, EntryPoint = "interception_is_invalid", CallingConvention = CallingConvention.Cdecl)]
    public static extern int IsInvalid(int device);

    [DllImport(Dll, EntryPoint = "interception_is_keyboard", CallingConvention = CallingConvention.Cdecl)]
    public static extern int IsKeyboard(int device);

    [DllImport(Dll, EntryPoint = "interception_is_mouse", CallingConvention = CallingConvention.Cdecl)]
    public static extern int IsMouse(int device);

    public static bool IsKeyboardDevice(int device) => device > 0 && device <= INTERCEPTION_MAX_KEYBOARD;
    public static bool IsMouseDevice(int device) => device > INTERCEPTION_MAX_KEYBOARD && device <= INTERCEPTION_MAX_DEVICE;
}
