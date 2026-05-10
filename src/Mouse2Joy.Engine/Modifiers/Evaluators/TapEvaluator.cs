using Mouse2Joy.Persistence.Models;

namespace Mouse2Joy.Engine.Modifiers.Evaluators;

/// <summary>
/// Detects a "tap" — a press-then-release where the held duration is less
/// than MaxHoldSeconds. On a tap, the output pulses true for PulseSeconds
/// (or one tick if PulseSeconds &lt;= 0). Held longer than MaxHoldSeconds is
/// not a tap; the release is silently absorbed.
///
/// <para>When <c>WaitForHigherTaps</c> is true, the modifier behaves
/// differently:</para>
/// <list type="bullet">
///   <item>After a successful tap (release within MaxHoldSeconds), the
///         pulse is held pending for ConfirmWaitSeconds. If a new press
///         arrives within that window, the pending pulse is canceled (a
///         sibling MultiTap binding is taking over).</item>
///   <item>Early-exit: if the new press is held past MaxHoldSeconds, it
///         can't be a tap, so the pending pulse fires immediately. The
///         long press itself then mirrors as Digital passthrough until
///         release ("passthrough on overflow").</item>
///   <item>Long press from idle (no pending tap): same passthrough — once
///         held past MaxHoldSeconds, output mirrors input. Release cleans
///         up to idle.</item>
/// </list>
/// </summary>
internal sealed class TapEvaluator : IModifierEvaluator
{
    private readonly TapModifier _config;

    private bool _prevInput;
    private double _heldFor;
    private double _pulseRemaining;
    // Hold has exceeded MaxHoldSeconds for the current press → late release
    // is silently absorbed (Wait flag off) or passthrough engaged (Wait flag on).
    private bool _holdInvalidated;

    // Wait-flag state (only meaningful when _config.WaitForHigherTaps is true).
    private double _confirmWaitRemaining; // > 0 means a wait timer is running
    private bool _pendingFire;             // true: wait expiry fires; false: suppression mode (multi-press in progress, fire silently consumed)
    private bool _passthroughActive;       // a long-held press is currently mirroring as passthrough

    public TapEvaluator(TapModifier config)
    {
        _config = config;
    }

    public Modifier Config => _config;

    public void Reset()
    {
        _prevInput = false;
        _heldFor = 0;
        _pulseRemaining = 0;
        _holdInvalidated = false;
        _confirmWaitRemaining = 0;
        _pendingFire = false;
        _passthroughActive = false;
    }

    public Signal Evaluate(in Signal input, double dt)
    {
        var current = input.DigitalValue;
        var maxHold = _config.MaxHoldSeconds < 0 ? 0 : _config.MaxHoldSeconds;
        var waitForHigher = _config.WaitForHigherTaps;

        // --- 1) Confirm-wait countdown. Pause while input is held.
        if (_confirmWaitRemaining > 0 && !current)
        {
            _confirmWaitRemaining -= dt;
            if (_confirmWaitRemaining <= 0)
            {
                var fired = _pendingFire;
                _confirmWaitRemaining = 0;
                _pendingFire = false;
                if (fired && _config.PulseSeconds > 0)
                    _pulseRemaining = _config.PulseSeconds;
            }
        }

        // --- 2) Edge handling.
        if (current && !_prevInput)
        {
            // Rising edge. Don't change _pendingFire yet — the press's
            // resolution decides:
            //   - Falling within MaxHold (tap)   → user is multi-tapping,
            //                                       set pendingFire=false.
            //   - Held past MaxHold (overflow)   → user is NOT multi-tapping,
            //                                       pendingFire stays true
            //                                       and fires early.
            _heldFor = 0;
            _holdInvalidated = false;
            _passthroughActive = false;
        }
        else if (current)
        {
            _heldFor += dt;
            if (_heldFor > maxHold && !_holdInvalidated)
            {
                _holdInvalidated = true;
                if (waitForHigher)
                {
                    // Long press: not a tap. Confirm any pending fire
                    // (pendingFire=true) — overflow rules out further taps.
                    // If suppression mode (pendingFire=false), clear silently.
                    if (_confirmWaitRemaining > 0)
                    {
                        var fired = _pendingFire;
                        _confirmWaitRemaining = 0;
                        _pendingFire = false;
                        if (fired && _config.PulseSeconds > 0)
                            _pulseRemaining = _config.PulseSeconds;
                    }
                    // Engage passthrough — output mirrors input until release.
                    _passthroughActive = true;
                }
            }
        }
        else if (!current && _prevInput)
        {
            // Falling edge.
            if (!_holdInvalidated && _heldFor <= maxHold)
            {
                if (waitForHigher)
                {
                    var wait = _config.ConfirmWaitSeconds;
                    if (_confirmWaitRemaining > 0)
                    {
                        // A wait was already in flight when this press began —
                        // this release confirms multi-press. Switch to
                        // suppression mode and refresh the wait window from
                        // this release.
                        _confirmWaitRemaining = wait <= 0 ? double.Epsilon : wait;
                        _pendingFire = false;
                    }
                    else
                    {
                        // Fresh tap: arm wait with pendingFire=true.
                        _confirmWaitRemaining = wait <= 0 ? double.Epsilon : wait;
                        _pendingFire = true;
                    }
                }
                else
                {
                    // Original behavior: fire immediately on release.
                    // PulseSeconds=0 collapses to a single-tick pulse here
                    // (the no-Wait path has no passthrough, so PulseSeconds=0
                    // would otherwise make the modifier inert — not useful).
                    _pulseRemaining = _config.PulseSeconds <= 0 ? double.Epsilon : _config.PulseSeconds;
                }
            }
            // Reset hold tracking; passthrough drops with !current.
            _heldFor = 0;
            _holdInvalidated = false;
            _passthroughActive = false;
        }

        _prevInput = current;

        // --- 3) Pulse decay (read post-decay).
        if (_pulseRemaining > 0)
        {
            _pulseRemaining -= dt;
            if (_pulseRemaining < 0) _pulseRemaining = 0;
        }

        // --- 4) Compose output: pulse OR passthrough.
        var pulseActive = _pulseRemaining > 0;
        var passthrough = waitForHigher && _passthroughActive && current;
        return Signal.Digital(pulseActive || passthrough);
    }
}
