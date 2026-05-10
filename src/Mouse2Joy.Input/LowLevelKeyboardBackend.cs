using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Mouse2Joy.Engine;
using Mouse2Joy.Persistence.Models;

namespace Mouse2Joy.Input;

/// <summary>
/// Keyboard capture via Win32 WH_KEYBOARD_LL low-level hook. Sees both
/// hardware keystrokes AND those injected via SendInput / keybd_event
/// (voice-to-keyboard tools, on-screen keyboards, accessibility software).
/// Interception only sees hardware events from the kernel keyboard stack,
/// so apps that depend on synthetic keystrokes need this backend.
///
/// Suppression is done by returning 1 from the hook callback, which prevents
/// the event from reaching subsequent hooks and the focused application.
///
/// The hook MUST be installed on a thread with a running Win32 message pump
/// (the WPF UI thread is the standard place). The callback runs synchronously
/// on that pump, so it must return quickly.
/// </summary>
public sealed class LowLevelKeyboardBackend : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int HC_ACTION = 0;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;
    private const uint LLKHF_EXTENDED = 0x01;
    private const uint LLKHF_INJECTED = 0x10;

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public nint dwExtraInfo;
    }

    private delegate nint HookProc(int nCode, nint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern nint SetWindowsHookExW(int idHook, HookProc lpfn, nint hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(nint hhk);

    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint GetModuleHandleW(string? lpModuleName);

    private readonly ILogger _logger;
    private HookProc? _proc;
    private nint _hook;
    private Stopwatch? _sw;

    private volatile int _suppressionEnabled = 0;
    private Func<RawEvent, bool> _shouldSwallow = _ => false;
    private readonly object _predGate = new();

    public event Action<RawEvent>? KeyEventReceived;

    public bool IsInstalled => _hook != 0;

    public LowLevelKeyboardBackend(ILogger<LowLevelKeyboardBackend>? logger = null)
    {
        _logger = (ILogger?)logger ?? NullLogger.Instance;
    }

    /// <summary>Install the hook. Must be called on a thread with a message pump.</summary>
    public void Install()
    {
        if (_hook != 0) return;
        _proc = HookCallback;
        // hMod can be the current module's handle for thread-specific hooks; for
        // global low-level hooks user32 expects it but ignores the actual module.
        var hMod = GetModuleHandleW(null);
        _hook = SetWindowsHookExW(WH_KEYBOARD_LL, _proc, hMod, 0);
        if (_hook == 0)
        {
            var err = Marshal.GetLastWin32Error();
            throw new InvalidOperationException($"SetWindowsHookEx(WH_KEYBOARD_LL) failed with Win32 error {err}");
        }
        _sw = Stopwatch.StartNew();
        _logger.LogInformation("Low-level keyboard hook installed");
    }

    public void SetSuppressionEnabled(bool enabled)
        => Interlocked.Exchange(ref _suppressionEnabled, enabled ? 1 : 0);

    public void SetSuppressionPredicate(Func<RawEvent, bool> shouldSwallow)
    {
        lock (_predGate) _shouldSwallow = shouldSwallow ?? (_ => false);
    }

    private nint HookCallback(int nCode, nint wParam, nint lParam)
    {
        if (nCode != HC_ACTION || _proc is null)
            return CallNextHookEx(_hook, nCode, wParam, lParam);

        try
        {
            var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            var msg = wParam.ToInt32();
            bool down = msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN;
            bool extended = (data.flags & LLKHF_EXTENDED) != 0;
            var key = new VirtualKey((ushort)data.scanCode, extended);
            var ev = RawEvent.ForKey(key, down, KeyModifiers.None, _sw?.ElapsedTicks ?? 0);

            // Notify upstream first so modifier tracking + hotkey dispatch see this event.
            KeyEventReceived?.Invoke(ev);

            // Suppression decision.
            if (_suppressionEnabled != 0)
            {
                bool swallow;
                Func<RawEvent, bool> pred;
                lock (_predGate) pred = _shouldSwallow;
                try { swallow = pred(ev); }
                catch { swallow = false; }
                if (swallow) return 1;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "WH_KEYBOARD_LL callback threw");
        }

        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_hook != 0)
        {
            try { UnhookWindowsHookEx(_hook); } catch { /* ignore */ }
            _hook = 0;
        }
        _proc = null;
    }
}
