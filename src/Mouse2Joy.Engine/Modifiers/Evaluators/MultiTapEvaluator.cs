using Mouse2Joy.Persistence.Models;

namespace Mouse2Joy.Engine.Modifiers.Evaluators;

/// <summary>
/// Counts taps within a sliding window. Fires a pulse when TapCount taps
/// are detected within WindowSeconds of the first tap. Each individual tap
/// must satisfy MaxHoldSeconds (same definition as <see cref="TapEvaluator"/>).
///
/// <para>When <c>WaitForHigherTaps</c> is true, after the Nth tap fires
/// internally the pulse is held pending for WindowSeconds. If a new press
/// arrives during the wait and is itself a tap, the pending pulse is
/// canceled (a higher-count sibling MultiTap is taking over). If the
/// follow-up press is held past MaxHoldSeconds (clearly not a tap), the
/// pending pulse fires immediately. Long press from idle resets any
/// in-progress sequence.</para>
/// </summary>
internal sealed class MultiTapEvaluator : IModifierEvaluator
{
    private readonly MultiTapModifier _config;

    private bool _prevInput;
    private double _heldFor;
    private bool _holdInvalidated;
    private int _tapCount;
    private double _windowRemaining;
    private double _pulseRemaining;
    // Wait-flag state: a multi-tap goal has been reached but we're waiting
    // to confirm no higher-count tap follows on a sibling binding.
    private double _confirmWaitRemaining;

    public MultiTapEvaluator(MultiTapModifier config)
    {
        _config = config;
    }

    public Modifier Config => _config;

    public void Reset()
    {
        _prevInput = false;
        _heldFor = 0;
        _holdInvalidated = false;
        _tapCount = 0;
        _windowRemaining = 0;
        _pulseRemaining = 0;
        _confirmWaitRemaining = 0;
    }

    public Signal Evaluate(in Signal input, double dt)
    {
        var current = input.DigitalValue;
        var maxHold = _config.MaxHoldSeconds < 0 ? 0 : _config.MaxHoldSeconds;
        var window = _config.WindowSeconds < 0 ? 0 : _config.WindowSeconds;
        var goal = _config.TapCount < 1 ? 1 : _config.TapCount;
        var waitForHigher = _config.WaitForHigherTaps;

        // --- 1) Window decay for in-progress sequence.
        if (_tapCount > 0 && _windowRemaining > 0)
        {
            _windowRemaining -= dt;
            if (_windowRemaining <= 0)
            {
                _tapCount = 0;
                _windowRemaining = 0;
            }
        }

        // --- 2) Confirm-wait countdown. Pause while input is currently held —
        // we don't yet know if the new press is a tap (cancel) or overflow
        // (early-fire). This mirrors TapEvaluator's pause-during-press logic.
        if (_confirmWaitRemaining > 0 && !current)
        {
            _confirmWaitRemaining -= dt;
            if (_confirmWaitRemaining <= 0)
            {
                _confirmWaitRemaining = 0;
                _pulseRemaining = _config.PulseSeconds <= 0 ? double.Epsilon : _config.PulseSeconds;
            }
        }

        // --- 3) Edge handling. Don't cancel pending on rising edge — let
        // the press resolve as tap or overflow first.
        if (current && !_prevInput)
        {
            _heldFor = 0;
            _holdInvalidated = false;
        }
        else if (current)
        {
            _heldFor += dt;
            if (_heldFor > maxHold && !_holdInvalidated)
            {
                _holdInvalidated = true;
                // Long press: definitely not a tap. Effects:
                // a) Fire pending pulse early.
                if (waitForHigher && _confirmWaitRemaining > 0)
                {
                    _confirmWaitRemaining = 0;
                    _pulseRemaining = _config.PulseSeconds <= 0 ? double.Epsilon : _config.PulseSeconds;
                }
                // b) Reset any partial in-progress count — the long press
                //    breaks the sequence.
                _tapCount = 0;
                _windowRemaining = 0;
            }
        }
        else if (!current && _prevInput)
        {
            // Falling edge.
            if (!_holdInvalidated && _heldFor <= maxHold)
            {
                if (waitForHigher && _confirmWaitRemaining > 0)
                {
                    // We were already waiting on a completed N-tap from this
                    // modifier. A new tap arriving means a sibling higher-
                    // count binding is taking over — cancel pending pulse,
                    // and DON'T count this tap toward our own sequence (we
                    // already fired the pending Nth).
                    _confirmWaitRemaining = 0;
                }
                else
                {
                    _tapCount++;
                    if (_tapCount >= goal)
                    {
                        if (waitForHigher)
                        {
                            _confirmWaitRemaining = window <= 0 ? double.Epsilon : window;
                        }
                        else
                        {
                            _pulseRemaining = _config.PulseSeconds <= 0 ? double.Epsilon : _config.PulseSeconds;
                        }
                        _tapCount = 0;
                        _windowRemaining = 0;
                    }
                    else
                    {
                        _windowRemaining = window;
                    }
                }
            }
            _heldFor = 0;
            _holdInvalidated = false;
        }

        _prevInput = current;

        // --- 4) Pulse decay (read post-decay).
        if (_pulseRemaining > 0)
        {
            _pulseRemaining -= dt;
            if (_pulseRemaining < 0)
            {
                _pulseRemaining = 0;
            }
        }
        return Signal.Digital(_pulseRemaining > 0);
    }
}
