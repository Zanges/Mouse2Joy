using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Mouse2Joy.Engine.Threading;

/// <summary>
/// High-resolution periodic wait. Tries the WaitableTimerEx Win32 API for
/// sub-15 ms accuracy on Windows 10+; falls back to a Stopwatch-corrected
/// Thread.Sleep loop on older OSes or if the API call fails.
/// </summary>
public sealed class WaitableTickTimer : IDisposable
{
    private const uint TIMER_ALL_ACCESS = 0x1F0003;
    private const uint CREATE_WAITABLE_TIMER_HIGH_RESOLUTION = 0x00000002;
    private const uint INFINITE = 0xFFFFFFFF;

    private readonly nint _handle;
    private readonly bool _useApi;

    public WaitableTickTimer()
    {
        try
        {
            _handle = CreateWaitableTimerExW(0, null, CREATE_WAITABLE_TIMER_HIGH_RESOLUTION, TIMER_ALL_ACCESS);
            _useApi = _handle != 0;
        }
        catch
        {
            _useApi = false;
            _handle = 0;
        }
    }

    /// <summary>Block the calling thread for ~<paramref name="periodMs"/> milliseconds.</summary>
    public void WaitFor(double periodMs)
    {
        if (_useApi && _handle != 0)
        {
            // SetWaitableTimer takes 100ns intervals as a negative LARGE_INTEGER for relative delay.
            long due = -(long)(periodMs * 10_000.0);
            if (SetWaitableTimer(_handle, ref due, 0, 0, 0, false))
            {
                WaitForSingleObject(_handle, INFINITE);
                return;
            }
        }

        // Fallback: spin-corrected sleep. We never want to busy-wait, so floor
        // the sleep at 1 ms and accept some jitter.
        var sw = Stopwatch.StartNew();
        var target = TimeSpan.FromMilliseconds(periodMs);
        while (sw.Elapsed < target)
        {
            var remaining = target - sw.Elapsed;
            if (remaining.TotalMilliseconds > 2)
                Thread.Sleep(1);
            else
                Thread.Yield();
        }
    }

    public void Dispose()
    {
        if (_handle != 0)
            CloseHandle(_handle);
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern nint CreateWaitableTimerExW(nint timerAttributes, string? name, uint flags, uint desiredAccess);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWaitableTimer(nint hTimer, ref long pDueTime, int lPeriod, nint pfnCompletionRoutine, nint lpArgToCompletionRoutine, [MarshalAs(UnmanagedType.Bool)] bool fResume);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(nint hHandle, uint dwMilliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint hObject);
}
