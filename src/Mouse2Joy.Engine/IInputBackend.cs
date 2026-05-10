namespace Mouse2Joy.Engine;

public enum SuppressionMode
{
    /// <summary>Forward all real input to the OS as normal.</summary>
    PassThrough,

    /// <summary>Drop events that match an active binding source on the active profile; forward the rest.</summary>
    SelectiveSuppress
}

public interface IInputBackend : IDisposable
{
    /// <summary>True if the underlying driver is available and a context has been created.</summary>
    bool IsAvailable { get; }

    void StartCapture();
    void StopCapture();

    /// <summary>Toggle whether matching events are swallowed or passed through. Cheap; expected to flip on toggle.</summary>
    void SetSuppressionMode(SuppressionMode mode);

    /// <summary>Replace the predicate used to decide which events should be swallowed in <see cref="SuppressionMode.SelectiveSuppress"/>.</summary>
    void SetSuppressionPredicate(Func<RawEvent, bool> shouldSwallow);

    /// <summary>Raised on the backend's capture thread for every observed event (after suppression decision is made).</summary>
    event Action<RawEvent>? RawEventReceived;
}
