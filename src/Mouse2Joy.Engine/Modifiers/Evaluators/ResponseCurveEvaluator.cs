using Mouse2Joy.Persistence.Models;

namespace Mouse2Joy.Engine.Modifiers.Evaluators;

internal sealed class ResponseCurveEvaluator : IModifierEvaluator
{
    private readonly ResponseCurveModifier _config;

    public ResponseCurveEvaluator(ResponseCurveModifier config)
    {
        _config = config;
    }

    public Modifier Config => _config;
    public void Reset() { }

    public Signal Evaluate(in Signal input, double dt)
    {
        var x = input.ScalarValue;
        if (double.IsNaN(x)) return Signal.ZeroScalar;
        var sign = Math.Sign(x);
        var a = Math.Abs(x);
        if (a > 1.0) a = 1.0;
        var n = _config.Exponent <= 0 ? 1.0 : _config.Exponent;
        var shaped = Math.Pow(a, n);
        return Signal.Scalar(sign * shaped);
    }
}
