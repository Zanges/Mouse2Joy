using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Mouse2Joy.UI.Interop;

/// <summary>
/// P/Invoke helpers for click-through / always-on-top / no-activate window styles
/// used by the overlay window.
/// </summary>
public static class WindowStyles
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_LAYERED = 0x00080000;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(nint hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(nint hWnd, int nIndex, int dwNewLong);

    public static void MakeOverlay(Window window, bool clickThrough)
    {
        var helper = new WindowInteropHelper(window);
        var hwnd = helper.Handle;
        if (hwnd == 0)
        {
            return;
        }

        var ex = GetWindowLong(hwnd, GWL_EXSTYLE);
        ex |= WS_EX_LAYERED | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
        if (clickThrough)
        {
            ex |= WS_EX_TRANSPARENT;
        }
        else
        {
            ex &= ~WS_EX_TRANSPARENT;
        }

        // SetWindowLong returns the previous value, or 0 on failure (use
        // GetLastError to distinguish). For the overlay, a failure means
        // the click-through style didn't apply -- log via Debug since this
        // module has no ILogger dependency by design.
        var previous = SetWindowLong(hwnd, GWL_EXSTYLE, ex);
        if (previous == 0 && Marshal.GetLastWin32Error() != 0)
        {
            System.Diagnostics.Debug.WriteLine($"SetWindowLong(GWL_EXSTYLE) failed: {Marshal.GetLastWin32Error()}");
        }
    }
}
