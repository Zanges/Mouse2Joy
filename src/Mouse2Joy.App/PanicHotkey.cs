using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace Mouse2Joy.App;

/// <summary>
/// Independent safety hotkey that uses Win32 RegisterHotKey on a hidden
/// message-only window. Lives outside the Interception capture path so it
/// fires even when the engine is disabled, wedged, or crashed (as long as
/// the app process itself is still running and Windows isn't being
/// completely held by a fullscreen game).
///
/// Default combo: Ctrl + Shift + F12. Fixed (not user-configurable) so a
/// mis-bind can't lock the user out of the safety net.
/// </summary>
internal sealed class PanicHotkey : IDisposable
{
    private const int WM_HOTKEY = 0x0312;

    // MOD_ALT kept as documentation of the RegisterHotKey flag set; we
    // currently only combine Ctrl+Shift.
#pragma warning disable IDE0051
    private const uint MOD_ALT = 0x0001;
#pragma warning restore IDE0051
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_NOREPEAT = 0x4000;

    // Win32 virtual-key for F12.
    private const uint VK_F12 = 0x7B;
    private const int HotkeyId = 0xB00B;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(nint hWnd, int id);

    private readonly Action _onPanic;
    private HwndSource? _source;

    public PanicHotkey(Action onPanic) { _onPanic = onPanic; }

    public bool Register()
    {
        // Message-only parent (HWND_MESSAGE = -3). Means the window never appears
        // on screen, in Alt+Tab, or in the taskbar — but it still receives messages.
        var p = new HwndSourceParameters("Mouse2Joy.PanicHotkey")
        {
            ParentWindow = new nint(-3),
            WindowStyle = 0,
        };
        _source = new HwndSource(p);
        _source.AddHook(WndProc);
        var ok = RegisterHotKey(_source.Handle, HotkeyId, MOD_CONTROL | MOD_SHIFT | MOD_NOREPEAT, VK_F12);
        return ok;
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            try { _onPanic(); } catch { /* swallow — panic must not throw */ }
            handled = true;
        }
        return 0;
    }

    public void Dispose()
    {
        if (_source is null)
        {
            return;
        }

        try { UnregisterHotKey(_source.Handle, HotkeyId); } catch { /* ignore */ }
        try { _source.RemoveHook(WndProc); } catch { /* ignore */ }
        try { _source.Dispose(); } catch { /* ignore */ }
        _source = null;
    }
}
