using System.Diagnostics;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Mouse2Joy.Engine.Hotkeys;
using Mouse2Joy.Engine.Mapping;
using Mouse2Joy.Engine.State;
using Mouse2Joy.Engine.Threading;
using Mouse2Joy.Persistence.Models;

namespace Mouse2Joy.Engine;

public enum ToggleAction { Soft, Hard }

public sealed class InputEngine : IEngineStateSource, IDisposable
{
    private readonly IInputBackend _input;
    private readonly IVirtualPad _pad;
    private readonly ILogger _logger;

    private readonly Channel<RawEvent> _events;
    private readonly OutputStateBuckets _buckets = new();
    private readonly Dictionary<(Stick, AxisComponent), double> _stickFinal = new();
    private readonly HotkeyModifierTracker _modTracker = new();

    private Profile _activeProfile = new() { Name = "" };
    private BindingResolver _resolver;
    private HotkeySettings _hotkeys = new(null, null, new Dictionary<string, HotkeyBinding>());

    private volatile int _mode = (int)EngineMode.Off;
    private EngineStateSnapshot _current = EngineStateSnapshot.Empty;
    private long _tickIndex;
    private int _rawDx, _rawDy;          // last-tick raw mouse delta (for overlay)

    private Thread? _tickThread;
    private CancellationTokenSource? _cts;

    public InputEngine(IInputBackend input, IVirtualPad pad, ILogger<InputEngine>? logger = null)
    {
        _input = input;
        _pad = pad;
        _logger = (ILogger?)logger ?? NullLogger.Instance;
        _events = Channel.CreateUnbounded<RawEvent>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
        _resolver = new BindingResolver(_activeProfile);

        _input.RawEventReceived += OnRawEvent;
        _input.SetSuppressionPredicate(ShouldSwallowFromBackend);
    }

    public EngineStateSnapshot Current => Volatile.Read(ref _current);

    public event Action<EngineStateSnapshot>? Tick;

    /// <summary>Raised when mode, profile, or pad-connection changes. Marshalled by the consumer.</summary>
    public event Action<EngineMode>? ModeChanged;

    public event Action<string>? ProfileChanged;

    public EngineMode Mode => (EngineMode)_mode;

    public Profile ActiveProfile => Volatile.Read(ref _activeProfile);

    public void SetActiveProfile(Profile profile)
    {
        var swapped = Interlocked.Exchange(ref _activeProfile, profile);
        _resolver.SetProfile(profile, _buckets);
        ProfileChanged?.Invoke(profile.Name);
        _logger.LogInformation("Active profile set to '{Name}' (was '{Prev}')", profile.Name, swapped.Name);
    }

    public void SetHotkeys(HotkeySettings settings)
    {
        _hotkeys = settings;
    }

    /// <summary>
    /// Bring the input backend up and start the tick loop in idle (Off) mode.
    /// Capture is on so hotkeys fire; pad is NOT connected and no input is suppressed.
    /// Call this once at app launch (when admin rights are available).
    /// </summary>
    public void StartCapture()
    {
        if (_tickThread is not null)
            return;
        _cts = new CancellationTokenSource();
        _input.StartCapture();
        _input.SetSuppressionMode(SuppressionMode.PassThrough);
        SetModeInternal(EngineMode.Off);

        _tickThread = new Thread(TickLoop)
        {
            Name = "Mouse2Joy.Engine.Tick",
            IsBackground = true,
            Priority = ThreadPriority.AboveNormal
        };
        _tickThread.Start(_cts.Token);
    }

    /// <summary>Connect the virtual pad and start emulating per the active profile. Idempotent.</summary>
    public void EnableEmulation()
    {
        if (Mode == EngineMode.Active) return;
        try
        {
            _pad.Connect();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect virtual pad");
            throw;
        }
        _input.SetSuppressionMode(SuppressionMode.SelectiveSuppress);
        SetModeInternal(EngineMode.Active);
    }

    /// <summary>
    /// Connect the virtual pad but keep input pass-through (no suppression). Lands the engine
    /// in <see cref="EngineMode.SoftMuted"/> from any starting state. This is the safe-by-default
    /// "armed" state used by the UI Activate button: the pad is online so the soft toggle hotkey
    /// (or a soft-mute toggle from the tray/UI) can flip into <see cref="EngineMode.Active"/>
    /// without the user being locked out of their mouse before they can reach the hotkey.
    /// Idempotent.
    /// </summary>
    public void EnterSoftMute()
    {
        if (Mode == EngineMode.SoftMuted) return;
        try
        {
            _pad.Connect();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect virtual pad");
            throw;
        }
        _buckets.ResetForIdleReport();
        _input.SetSuppressionMode(SuppressionMode.PassThrough);
        SetModeInternal(EngineMode.SoftMuted);
    }

    /// <summary>Disconnect the virtual pad and stop suppressing input. Capture stays on so hotkeys still fire.</summary>
    public void DisableEmulation()
    {
        if (Mode == EngineMode.Off) return;
        _buckets.ResetForIdleReport();
        try { _pad.Submit(XInputReport.Idle); } catch { /* ignore */ }
        try { _pad.Disconnect(); } catch { /* ignore */ }
        _input.SetSuppressionMode(SuppressionMode.PassThrough);
        SetModeInternal(EngineMode.Off);
    }

    /// <summary>Tear down everything. Capture stops, pad disconnects, tick thread joins. Call on app shutdown.</summary>
    public void Shutdown()
    {
        _cts?.Cancel();
        _tickThread?.Join(500);
        _tickThread = null;
        try { _input.SetSuppressionMode(SuppressionMode.PassThrough); } catch { /* ignore on shutdown */ }
        try { _input.StopCapture(); } catch { /* ignore */ }
        try { _pad.Disconnect(); } catch { /* ignore */ }
        SetModeInternal(EngineMode.Off);
    }

    public void RequestToggle(ToggleAction action)
    {
        switch (action)
        {
            case ToggleAction.Soft:
                // Soft transitions:
                //   Off       -> Active     (initial activation; mirrors Hard from Off)
                //   Active    -> SoftMuted  (mute mid-session, pad stays connected)
                //   SoftMuted -> Active     (engage after Activate-button arming, or after a mute)
                // Letting Soft engage from Off means a user who only set up a single soft hotkey
                // can drive the whole lifecycle from it — no need to reach for the Activate button
                // before the first hotkey works.
                if (Mode == EngineMode.Off)
                {
                    if (string.IsNullOrEmpty(_activeProfile.Name))
                    {
                        _logger.LogInformation("Soft hotkey pressed but no active profile is set; ignoring");
                        return;
                    }
                    try { EnableEmulation(); }
                    catch (Exception ex) { _logger.LogError(ex, "EnableEmulation from Off failed (Soft)"); }
                }
                else if (Mode == EngineMode.Active)
                {
                    _buckets.ResetForIdleReport();
                    _input.SetSuppressionMode(SuppressionMode.PassThrough);
                    SetModeInternal(EngineMode.SoftMuted);
                }
                else if (Mode == EngineMode.SoftMuted)
                {
                    _input.SetSuppressionMode(SuppressionMode.SelectiveSuppress);
                    SetModeInternal(EngineMode.Active);
                }
                break;

            case ToggleAction.Hard:
                // Hard flips Off <-> Active (jumping out of SoftMuted to Off is also valid).
                if (Mode == EngineMode.Off)
                {
                    if (string.IsNullOrEmpty(_activeProfile.Name))
                    {
                        _logger.LogInformation("Hard hotkey pressed but no active profile is set; ignoring");
                        return;
                    }
                    try { EnableEmulation(); }
                    catch (Exception ex) { _logger.LogError(ex, "EnableEmulation from Off failed"); }
                }
                else
                {
                    DisableEmulation();
                }
                break;
        }
    }

    /// <summary>Force the engine into Off regardless of current state. Used by the safety panic hotkey.</summary>
    public void Panic()
    {
        _logger.LogWarning("Panic hotkey invoked — forcing engine to Off");
        try { DisableEmulation(); }
        catch (Exception ex) { _logger.LogError(ex, "Panic DisableEmulation failed"); }
    }

    private void SetModeInternal(EngineMode mode)
    {
        Interlocked.Exchange(ref _mode, (int)mode);
        ModeChanged?.Invoke(mode);
    }

    private bool ShouldSwallowFromBackend(RawEvent ev)
    {
        // Hotkeys come first — they must fire in every mode (Off, Active, SoftMuted).
        // Modifier tracking is updated in the OnRawEvent handler so it's
        // already current by the time we're called (capture thread is single-threaded).
        if (HotkeyMatcher.Match(in ev, _hotkeys.Hard, _modTracker.Held)
            || HotkeyMatcher.Match(in ev, _hotkeys.Soft, _modTracker.Held))
            return true;
        if (_hotkeys.ProfileSwitch is not null)
        {
            foreach (var kv in _hotkeys.ProfileSwitch)
                if (HotkeyMatcher.Match(in ev, kv.Value, _modTracker.Held))
                    return true;
        }
        if (Mode != EngineMode.Active)
            return false;
        return _resolver.ShouldSwallow(in ev);
    }

    private void OnRawEvent(RawEvent ev)
    {
        // Update modifier tracking on the capture thread so the suppression
        // predicate (also called on the capture thread for the same event)
        // sees correct state.
        _modTracker.Observe(in ev);

        // Hotkey dispatch fires regardless of mode.
        if (HotkeyMatcher.Match(in ev, _hotkeys.Hard, _modTracker.Held))
        {
            _logger.LogInformation("Hard hotkey fired");
            RequestToggle(ToggleAction.Hard);
            return;
        }
        if (HotkeyMatcher.Match(in ev, _hotkeys.Soft, _modTracker.Held))
        {
            _logger.LogInformation("Soft hotkey fired");
            RequestToggle(ToggleAction.Soft);
            return;
        }
        if (_hotkeys.ProfileSwitch is not null)
        {
            foreach (var kv in _hotkeys.ProfileSwitch)
            {
                if (HotkeyMatcher.Match(in ev, kv.Value, _modTracker.Held))
                {
                    OnProfileSwitchHotkey?.Invoke(kv.Key);
                    return;
                }
            }
        }

        if (Mode != EngineMode.Active)
            return;

        _events.Writer.TryWrite(ev);
    }

    public event Action<string>? OnProfileSwitchHotkey;

    private void TickLoop(object? token)
    {
        var ct = (CancellationToken)token!;
        using var timer = new WaitableTickTimer();
        var sw = Stopwatch.StartNew();
        var lastTickTicks = sw.ElapsedTicks;
        var tickRate = Math.Max(60, _activeProfile.TickRateHz);
        var periodMs = 1000.0 / tickRate;

        while (!ct.IsCancellationRequested)
        {
            timer.WaitFor(periodMs);

            var nowTicks = sw.ElapsedTicks;
            var dt = (nowTicks - lastTickTicks) / (double)Stopwatch.Frequency;
            if (dt <= 0) dt = periodMs / 1000.0;
            lastTickTicks = nowTicks;

            var mode = Mode;
            if (mode == EngineMode.Off)
            {
                // No reports while pad is disconnected. Tick keeps spinning so the
                // capture+hotkey path stays warm and snapshots stay current for UI.
                // Drain any incoming events so the channel doesn't grow unbounded.
                while (_events.Reader.TryRead(out _)) { }
                _rawDx = 0; _rawDy = 0;

                var snapOff = new EngineStateSnapshot(
                    Mode: mode,
                    ProfileName: _activeProfile.Name,
                    LeftStickX: 0, LeftStickY: 0,
                    RightStickX: 0, RightStickY: 0,
                    LeftTrigger: 0, RightTrigger: 0,
                    Buttons: XInputButtons.None,
                    RawMouseDeltaX: 0, RawMouseDeltaY: 0,
                    TickIndex: ++_tickIndex);
                Volatile.Write(ref _current, snapOff);
                continue;
            }

            // Drain queued events (only in Active; SoftMuted dropped them upstream).
            int dx = 0, dy = 0;
            while (_events.Reader.TryRead(out var ev))
            {
                if (ev.Kind == RawEventKind.MouseMove)
                {
                    dx += ev.MouseDeltaX;
                    dy += ev.MouseDeltaY;
                }
                _resolver.Apply(in ev, _buckets);
            }
            _rawDx = dx;
            _rawDy = dy;

            if (mode == EngineMode.SoftMuted)
            {
                _buckets.ResetForIdleReport();
                _stickFinal.Clear();
                _pad.Submit(XInputReport.Idle);
            }
            else
            {
                _resolver.AdvanceTick(dt, _buckets, _stickFinal);
                var report = ReportBuilder.Build(_buckets, _stickFinal);
                try { _pad.Submit(in report); }
                catch (Exception ex) { _logger.LogWarning(ex, "Submit failed"); }
            }

            // Publish snapshot for overlay/UI.
            var snap = new EngineStateSnapshot(
                Mode: mode,
                ProfileName: _activeProfile.Name,
                LeftStickX: GetStick(Stick.Left, AxisComponent.X),
                LeftStickY: GetStick(Stick.Left, AxisComponent.Y),
                RightStickX: GetStick(Stick.Right, AxisComponent.X),
                RightStickY: GetStick(Stick.Right, AxisComponent.Y),
                LeftTrigger: GetTrigger(Trigger.Left),
                RightTrigger: GetTrigger(Trigger.Right),
                Buttons: ComposeButtons(),
                RawMouseDeltaX: _rawDx,
                RawMouseDeltaY: _rawDy,
                TickIndex: ++_tickIndex);
            Volatile.Write(ref _current, snap);

            // Adjust period if profile tick rate changed.
            var newRate = Math.Max(60, _activeProfile.TickRateHz);
            if (newRate != tickRate)
            {
                tickRate = newRate;
                periodMs = 1000.0 / tickRate;
            }
        }
    }

    private double GetStick(Stick s, AxisComponent a)
        => _stickFinal.TryGetValue((s, a), out var v) ? v : 0.0;

    private double GetTrigger(Trigger t)
        => _buckets.Triggers.TryGetValue(t, out var v) ? v : 0.0;

    private XInputButtons ComposeButtons()
    {
        var b = XInputButtons.None;
        foreach (var kv in _buckets.Buttons)
        {
            if (!kv.Value) continue;
            b |= kv.Key switch
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
        }
        foreach (var kv in _buckets.DPad)
        {
            if (!kv.Value) continue;
            b |= kv.Key switch
            {
                DPadDirection.Up => XInputButtons.DPadUp,
                DPadDirection.Down => XInputButtons.DPadDown,
                DPadDirection.Left => XInputButtons.DPadLeft,
                DPadDirection.Right => XInputButtons.DPadRight,
                _ => XInputButtons.None
            };
        }
        return b;
    }

    /// <summary>Raise the UI-cadence Tick event with the current snapshot. Intended to be called from a 60 Hz UI timer.</summary>
    public void RaiseUiTick()
    {
        Tick?.Invoke(Current);
    }

    public void Dispose()
    {
        Shutdown();
        _cts?.Dispose();
    }
}

public sealed record HotkeySettings(
    HotkeyBinding? Soft,
    HotkeyBinding? Hard,
    IReadOnlyDictionary<string, HotkeyBinding>? ProfileSwitch);
