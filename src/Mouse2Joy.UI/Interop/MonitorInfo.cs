using System.Runtime.InteropServices;
using System.Windows;

namespace Mouse2Joy.UI.Interop;

/// <summary>
/// One physical monitor in the desktop. <see cref="Index"/> is the position in the
/// list returned by <see cref="MonitorEnumerator.Enumerate"/> — primary first, then
/// the rest in <c>EnumDisplayMonitors</c> order. This is the value persisted as
/// <c>WidgetConfig.MonitorIndex</c>.
/// </summary>
/// <param name="Index">Position in the enumeration; 0 = primary.</param>
/// <param name="DeviceName">Win32 \\.\DISPLAYn device name (informational).</param>
/// <param name="BoundsPx">Monitor bounds in physical pixels (rcMonitor).</param>
/// <param name="WorkingAreaPx">Bounds minus reserved areas like the taskbar (rcWork).</param>
/// <param name="DpiX">Per-monitor X DPI; 96 means no scaling.</param>
/// <param name="DpiY">Per-monitor Y DPI.</param>
/// <param name="IsPrimary">True for the primary monitor.</param>
public sealed record MonitorInfo(
    int Index,
    string DeviceName,
    Rect BoundsPx,
    Rect WorkingAreaPx,
    double DpiX,
    double DpiY,
    bool IsPrimary)
{
    /// <summary>Bounds in WPF DIPs (physical pixels / DPI scale). Use for <see cref="System.Windows.Window.Left"/>/<see cref="System.Windows.Window.Top"/>.</summary>
    public Rect BoundsDip => new(
        BoundsPx.X * 96.0 / DpiX,
        BoundsPx.Y * 96.0 / DpiY,
        BoundsPx.Width * 96.0 / DpiX,
        BoundsPx.Height * 96.0 / DpiY);
}

public static class MonitorEnumerator
{
    // MONITOR_DEFAULTTOPRIMARY is kept as documentation of the Win32 flag
    // set even though we don't currently use MonitorFromPoint.
#pragma warning disable IDE0051
    private const uint MONITOR_DEFAULTTOPRIMARY = 1;
#pragma warning restore IDE0051
    private const int MONITORINFOF_PRIMARY = 1;
    private const int MDT_EFFECTIVE_DPI = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public int dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    private delegate bool MonitorEnumProc(nint hMonitor, nint hdc, ref RECT lprcMonitor, nint dwData);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(nint hdc, nint lprcClip, MonitorEnumProc lpfnEnum, nint dwData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(nint hMonitor, ref MONITORINFOEX lpmi);

    // GetDpiForMonitor is in shcore.dll (Win 8.1+). Mouse2Joy already requires Win 10/11
    // (Interception kernel driver) so we don't need a fallback for older OSes.
    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(nint hMonitor, int dpiType, out uint dpiX, out uint dpiY);

    /// <summary>
    /// Enumerate currently-connected monitors. Primary monitor is always at index 0;
    /// subsequent monitors appear in OS enumeration order. Result list is fresh on each
    /// call — call again after a <c>SystemEvents.DisplaySettingsChanged</c>.
    /// </summary>
    public static IReadOnlyList<MonitorInfo> Enumerate()
    {
        var raw = new List<(nint hMon, MONITORINFOEX mi)>();
        EnumDisplayMonitors(0, 0, (nint hMonitor, nint _, ref RECT _, nint _) =>
        {
            var mi = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
            if (GetMonitorInfo(hMonitor, ref mi))
            {
                raw.Add((hMonitor, mi));
            }

            return true;
        }, 0);

        // Sort: primary first, then by left edge, then by top edge — gives a stable order
        // for non-primary monitors that doesn't depend on Win32 enumeration whim.
        var ordered = raw
            .OrderByDescending(t => (t.mi.dwFlags & MONITORINFOF_PRIMARY) != 0)
            .ThenBy(t => t.mi.rcMonitor.Left)
            .ThenBy(t => t.mi.rcMonitor.Top)
            .ToList();

        var result = new List<MonitorInfo>(ordered.Count);
        for (int i = 0; i < ordered.Count; i++)
        {
            var (hMon, mi) = ordered[i];
            // Probing per-monitor DPI; if shcore call fails (shouldn't on Win10+), fall back to 96.
            uint dpiX = 96, dpiY = 96;
            try
            {
                // GetDpiForMonitor returns S_OK (0) on success; non-zero HRESULT
                // means the out params are undefined, so we fall through with
                // the 96/96 defaults set above.
                if (GetDpiForMonitor(hMon, MDT_EFFECTIVE_DPI, out var ox, out var oy) == 0)
                {
                    dpiX = ox;
                    dpiY = oy;
                }
            }
            catch { /* fall through with 96 */ }

            var bounds = new Rect(mi.rcMonitor.Left, mi.rcMonitor.Top,
                mi.rcMonitor.Right - mi.rcMonitor.Left, mi.rcMonitor.Bottom - mi.rcMonitor.Top);
            var work = new Rect(mi.rcWork.Left, mi.rcWork.Top,
                mi.rcWork.Right - mi.rcWork.Left, mi.rcWork.Bottom - mi.rcWork.Top);
            result.Add(new MonitorInfo(
                Index: i,
                DeviceName: mi.szDevice,
                BoundsPx: bounds,
                WorkingAreaPx: work,
                DpiX: dpiX,
                DpiY: dpiY,
                IsPrimary: (mi.dwFlags & MONITORINFOF_PRIMARY) != 0));
        }
        return result;
    }
}
