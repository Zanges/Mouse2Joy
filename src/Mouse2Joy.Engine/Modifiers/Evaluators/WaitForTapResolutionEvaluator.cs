using Mouse2Joy.Persistence.Models;

namespace Mouse2Joy.Engine.Modifiers.Evaluators;

/// <summary>
/// Plain Digital→Digital passthrough that delays firing on a short press
/// to give sibling Tap/MultiTap bindings a chance to claim the input. See
/// <see cref="WaitForTapResolutionModifier"/> for full semantics.
///
/// State machine:
/// <list type="bullet">
///   <item><b>Idle</b>: no input. Output = false.</item>
///   <item><b>Pressing</b>: input held within MaxHoldSeconds, no decision yet. Output = false.</item>
///   <item><b>Passthrough</b>: hold exceeded MaxHoldSeconds. Output mirrors input until release, then back to Idle.</item>
///   <item><b>Waiting</b>: tap detected (released within MaxHoldSeconds). Output = false; pending pulse will fire if WaitSeconds elapses with no follow-up press, OR a follow-up long press confirms early.</item>
///   <item><b>Pulsing</b>: pending fire confirmed. Output = true for PulseSeconds.</item>
/// </list>
/// </summary>
internal sealed class WaitForTapResolutionEvaluator : IModifierEvaluator
{
    private readonly WaitForTapResolutionModifier _config;

    private bool _prevInput;
    private double _heldFor;
    private bool _holdInvalidated;   // true once the current press exceeded MaxHoldSeconds
    private bool _passthroughActive; // long-held press is mirroring as passthrough
    private double _waitRemaining;   // > 0 means a wait timer is running
    private bool _pendingFire;       // true when wait expiry should fire the pulse; false when wait expiry should silently clear (suppression mode)
    private double _pulseRemaining;

    public WaitForTapResolutionEvaluator(WaitForTapResolutionModifier config)
    {
        _config = config;
    }

    public Modifier Config => _config;

    public void Reset()
    {
        _prevInput = false;
        _heldFor = 0;
        _holdInvalidated = false;
        _passthroughActive = false;
        _waitRemaining = 0;
        _pendingFire = false;
        _pulseRemaining = 0;
    }

    public Signal Evaluate(in Signal input, double dt)
    {
        var current = input.DigitalValue;
        var maxHold = _config.MaxHoldSeconds < 0 ? 0 : _config.MaxHoldSeconds;

        // Wait countdown. Pause while input is currently held — we don't
        // yet know if the new press is a tap (cancel/suppress) or overflow
        // (early-fire if pending, otherwise just passthrough).
        if (_waitRemaining > 0 && !current)
        {
            _waitRemaining -= dt;
            if (_waitRemaining <= 0)
            {
                var fired = _pendingFire;
                _waitRemaining = 0;
                _pendingFire = false;
                if (fired && _config.PulseSeconds > 0)
                {
                    _pulseRemaining = _config.PulseSeconds;
                }
            }
        }

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
                // Long press unambiguously rules out further taps. If a fire
                // was pending (pendingFire=true), confirm it now. If we were
                // already in suppression mode (pendingFire=false), clear silently.
                if (_waitRemaining > 0)
                {
                    var fired = _pendingFire;
                    _waitRemaining = 0;
                    _pendingFire = false;
                    if (fired && _config.PulseSeconds > 0)
                    {
                        _pulseRemaining = _config.PulseSeconds;
                    }
                }
                // Engage passthrough regardless of pending state.
                _passthroughActive = true;
            }
        }
        else if (!current && _prevInput)
        {
            // Falling edge.
            if (!_holdInvalidated && _heldFor <= maxHold)
            {
                var w = _config.WaitSeconds;
                if (_waitRemaining > 0)
                {
                    // A wait was already in flight when this press began —
                    // this release confirms the user is multi-tapping. Switch
                    // to suppression mode and refresh the wait so it covers
                    // the full window from THIS release.
                    _waitRemaining = w <= 0 ? double.Epsilon : w;
                    _pendingFire = false;
                }
                else
                {
                    // Fresh tap: arm wait with pendingFire=true.
                    _waitRemaining = w <= 0 ? double.Epsilon : w;
                    _pendingFire = true;
                }
            }
            _heldFor = 0;
            _holdInvalidated = false;
            _passthroughActive = false;
        }

        _prevInput = current;

        // Pulse decay.
        if (_pulseRemaining > 0)
        {
            _pulseRemaining -= dt;
            if (_pulseRemaining < 0)
            {
                _pulseRemaining = 0;
            }
        }

        var pulseActive = _pulseRemaining > 0;
        var passthrough = _passthroughActive && current;
        return Signal.Digital(pulseActive || passthrough);
    }
}
