using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Mouse2Joy.Engine;
using Mouse2Joy.Input.Native;
using Mouse2Joy.Persistence.Models;

namespace Mouse2Joy.Input;

/// <summary>
/// Captures every keyboard + mouse event via Interception. For each event,
/// either calls Send (forward to OS) or doesn't (swallow). The decision is
/// driven by <see cref="SetSuppressionPredicate"/> + the current
/// <see cref="SuppressionMode"/>.
/// </summary>
public sealed class InterceptionInputBackend : IInputBackend
{
    private readonly ILogger _logger;
    private nint _context;
    private Thread? _captureThread;
    private volatile bool _running;

    private volatile int _mode = (int)SuppressionMode.PassThrough;
    private Func<RawEvent, bool> _shouldSwallow = _ => false;
    private readonly object _predicateGate = new();

    // Hold references to the predicate delegates we hand to the native layer
    // so the GC doesn't collect them.
    private InterceptionNative.InterceptionPredicate? _kbPredicate;
    private InterceptionNative.InterceptionPredicate? _msPredicate;

    public event Action<RawEvent>? RawEventReceived;

    public bool IsAvailable { get; private set; }

    public InterceptionInputBackend(ILogger<InterceptionInputBackend>? logger = null)
    {
        _logger = (ILogger?)logger ?? NullLogger.Instance;
    }

    public void StartCapture()
    {
        if (_running) return;

        _context = InterceptionNative.CreateContext();
        if (_context == 0)
        {
            IsAvailable = false;
            throw new InvalidOperationException("Interception context could not be created. Is the driver installed and is the process running as administrator?");
        }
        IsAvailable = true;

        // We only filter mouse devices. Keyboards are handled by
        // LowLevelKeyboardBackend so that synthetic SendInput keystrokes
        // (voice-to-keyboard, on-screen keyboards) are also captured —
        // Interception only sees hardware events from the kernel keyboard stack.
        _kbPredicate = _ => 0;
        _msPredicate = d => InterceptionNative.IsMouseDevice(d) ? 1 : 0;
        InterceptionNative.SetFilter(_context, _kbPredicate, InterceptionNative.INTERCEPTION_FILTER_KEY_NONE);
        InterceptionNative.SetFilter(_context, _msPredicate, InterceptionNative.INTERCEPTION_FILTER_MOUSE_ALL);

        _running = true;
        _captureThread = new Thread(CaptureLoop)
        {
            Name = "Mouse2Joy.Input.Capture",
            IsBackground = true,
            Priority = ThreadPriority.Highest
        };
        _captureThread.Start();
        _logger.LogInformation("Interception capture started");
    }

    public void StopCapture()
    {
        _running = false;
        // Disable filters so any in-flight wait unblocks promptly.
        try
        {
            if (_context != 0 && _kbPredicate is not null && _msPredicate is not null)
            {
                InterceptionNative.SetFilter(_context, _kbPredicate, InterceptionNative.INTERCEPTION_FILTER_KEY_NONE);
                InterceptionNative.SetFilter(_context, _msPredicate, InterceptionNative.INTERCEPTION_FILTER_MOUSE_NONE);
            }
        }
        catch { /* ignore */ }

        _captureThread?.Join(500);
        _captureThread = null;

        if (_context != 0)
        {
            try { InterceptionNative.DestroyContext(_context); } catch { /* ignore */ }
            _context = 0;
        }
        _logger.LogInformation("Interception capture stopped");
    }

    public void SetSuppressionMode(SuppressionMode mode)
    {
        Interlocked.Exchange(ref _mode, (int)mode);
    }

    public void SetSuppressionPredicate(Func<RawEvent, bool> shouldSwallow)
    {
        lock (_predicateGate)
            _shouldSwallow = shouldSwallow ?? (_ => false);
    }

    public void Dispose()
    {
        StopCapture();
    }

    private void CaptureLoop()
    {
        var stroke = default(InterceptionNative.Stroke);
        var sw = Stopwatch.StartNew();
        while (_running)
        {
            int device;
            try
            {
                device = InterceptionNative.WaitWithTimeout(_context, 100);
            }
            catch
            {
                Thread.Sleep(5);
                continue;
            }

            if (device == 0)
                continue;
            if (InterceptionNative.Receive(_context, device, ref stroke, 1) <= 0)
                continue;

            // Mouse-only backend: keyboard strokes are handled by the
            // LowLevelKeyboardBackend so that synthetic input is also captured.
            if (!InterceptionNative.IsMouseDevice(device))
            {
                ForwardStroke(device, ref stroke);
                continue;
            }

            // A single mouse stroke can carry move + buttons + wheel
            // simultaneously, so synthesize multiple RawEvents and ask the
            // suppression predicate about each. If ANY part matches a
            // suppressing binding we drop the whole stroke.
            bool swallow = false;
            var ticks = sw.ElapsedTicks;
            foreach (var ev in ProjectMouse(stroke.Mouse, ticks))
            {
                RawEventReceived?.Invoke(ev);
                if (ShouldSwallow(in ev))
                {
                    swallow = true;
                    // keep iterating so upstream (modifier tracking etc.) sees every event
                }
            }

            if (!swallow)
                ForwardStroke(device, ref stroke);
        }
    }

    private bool ShouldSwallow(in RawEvent ev)
    {
        if ((SuppressionMode)_mode == SuppressionMode.PassThrough)
            return false;
        Func<RawEvent, bool> pred;
        lock (_predicateGate)
            pred = _shouldSwallow;
        try { return pred(ev); }
        catch { return false; }
    }

    private void ForwardStroke(int device, ref InterceptionNative.Stroke stroke)
    {
        try { InterceptionNative.Send(_context, device, ref stroke, 1); }
        catch (Exception ex) { _logger.LogWarning(ex, "Send failed for device {Device}", device); }
    }

    private static IEnumerable<RawEvent> ProjectMouse(InterceptionNative.MouseStroke ms, long ticks)
    {
        if (ms.X != 0 || ms.Y != 0)
            yield return RawEvent.ForMouseMove(ms.X, ms.Y, ticks);

        var s = ms.State;
        if ((s & InterceptionNative.MouseState.LeftButtonDown) != 0)
            yield return RawEvent.ForMouseButton(MouseButton.Left, true, KeyModifiers.None, ticks);
        if ((s & InterceptionNative.MouseState.LeftButtonUp) != 0)
            yield return RawEvent.ForMouseButton(MouseButton.Left, false, KeyModifiers.None, ticks);
        if ((s & InterceptionNative.MouseState.RightButtonDown) != 0)
            yield return RawEvent.ForMouseButton(MouseButton.Right, true, KeyModifiers.None, ticks);
        if ((s & InterceptionNative.MouseState.RightButtonUp) != 0)
            yield return RawEvent.ForMouseButton(MouseButton.Right, false, KeyModifiers.None, ticks);
        if ((s & InterceptionNative.MouseState.MiddleButtonDown) != 0)
            yield return RawEvent.ForMouseButton(MouseButton.Middle, true, KeyModifiers.None, ticks);
        if ((s & InterceptionNative.MouseState.MiddleButtonUp) != 0)
            yield return RawEvent.ForMouseButton(MouseButton.Middle, false, KeyModifiers.None, ticks);
        if ((s & InterceptionNative.MouseState.Button4Down) != 0)
            yield return RawEvent.ForMouseButton(MouseButton.X1, true, KeyModifiers.None, ticks);
        if ((s & InterceptionNative.MouseState.Button4Up) != 0)
            yield return RawEvent.ForMouseButton(MouseButton.X1, false, KeyModifiers.None, ticks);
        if ((s & InterceptionNative.MouseState.Button5Down) != 0)
            yield return RawEvent.ForMouseButton(MouseButton.X2, true, KeyModifiers.None, ticks);
        if ((s & InterceptionNative.MouseState.Button5Up) != 0)
            yield return RawEvent.ForMouseButton(MouseButton.X2, false, KeyModifiers.None, ticks);

        if ((s & InterceptionNative.MouseState.Wheel) != 0 && ms.Rolling != 0)
        {
            var dir = ms.Rolling > 0 ? ScrollDirection.Up : ScrollDirection.Down;
            int clicks = Math.Abs(ms.Rolling) / 120;
            if (clicks == 0) clicks = 1;
            yield return RawEvent.ForMouseScroll(dir, clicks, KeyModifiers.None, ticks);
        }
    }
}
