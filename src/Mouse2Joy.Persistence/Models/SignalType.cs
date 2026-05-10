namespace Mouse2Joy.Persistence.Models;

/// <summary>
/// The kind of signal that flows along a binding's modifier chain at a given
/// point. Sources produce one of these; targets accept one of these; each
/// modifier declares its accepted-in and produced-out type.
/// </summary>
public enum SignalType
{
    /// <summary>Boolean (down/up). Produced by mouse buttons, keys, and scroll events.</summary>
    Digital,

    /// <summary>Signed mouse-count delta accumulated within a tick. Produced by mouse-axis sources.</summary>
    Delta,

    /// <summary>Continuous value in [-1, 1]. Most modifiers consume and produce Scalar.</summary>
    Scalar
}
