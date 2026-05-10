using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Mouse2Joy.Engine;

namespace Mouse2Joy.Input;

/// <summary>
/// Multiplexes Interception (for mouse — kernel-stack capture, full swallow
/// control) with Win32 WH_KEYBOARD_LL (for keyboard — sees both hardware and
/// SendInput-injected events). The engine sees a single IInputBackend.
///
/// The keyboard hook MUST be installed from a thread with a Win32 message pump
/// (WPF UI thread is the standard place). Pass the hook in already-installed
/// or call <see cref="InstallKeyboardHook"/> from the UI thread.
/// </summary>
public sealed class CompositeInputBackend : IInputBackend
{
    private readonly InterceptionInputBackend _mouse;
    private readonly LowLevelKeyboardBackend _keyboard;
    private readonly ILogger _logger;
    private bool _capturing;

    public CompositeInputBackend(
        InterceptionInputBackend mouse,
        LowLevelKeyboardBackend keyboard,
        ILogger<CompositeInputBackend>? logger = null)
    {
        _mouse = mouse;
        _keyboard = keyboard;
        _logger = (ILogger?)logger ?? NullLogger.Instance;
        _mouse.RawEventReceived += ev => RawEventReceived?.Invoke(ev);
        _keyboard.KeyEventReceived += ev => RawEventReceived?.Invoke(ev);
    }

    public bool IsAvailable => _mouse.IsAvailable && _keyboard.IsInstalled;

    public event Action<RawEvent>? RawEventReceived;

    public void StartCapture()
    {
        if (_capturing) return;
        _mouse.StartCapture();
        // Hook installation is the App's responsibility (must be UI thread).
        if (!_keyboard.IsInstalled)
            _logger.LogWarning("LowLevelKeyboardBackend hook is not installed. Call InstallKeyboardHook from the UI thread before StartCapture, or hotkeys will not fire.");
        _capturing = true;
    }

    public void StopCapture()
    {
        if (!_capturing) return;
        _mouse.StopCapture();
        // Don't uninstall the keyboard hook on StopCapture: it's lightweight
        // and we want it to survive engine restarts. Disposed in Dispose().
        _capturing = false;
    }

    public void SetSuppressionMode(SuppressionMode mode)
    {
        _mouse.SetSuppressionMode(mode);
        _keyboard.SetSuppressionEnabled(mode == SuppressionMode.SelectiveSuppress);
    }

    public void SetSuppressionPredicate(Func<RawEvent, bool> shouldSwallow)
    {
        _mouse.SetSuppressionPredicate(shouldSwallow);
        _keyboard.SetSuppressionPredicate(shouldSwallow);
    }

    public void Dispose()
    {
        try { _mouse.Dispose(); } catch { /* ignore */ }
        try { _keyboard.Dispose(); } catch { /* ignore */ }
    }
}
