namespace Mouse2Joy.Engine;

public interface IVirtualPad : IDisposable
{
    bool IsConnected { get; }

    /// <summary>Plug the virtual pad in. Throws if the underlying bus is unavailable.</summary>
    void Connect();

    /// <summary>Unplug the virtual pad. Idempotent.</summary>
    void Disconnect();

    /// <summary>Submit a single XInput report. Cheap; called from the engine tick thread.</summary>
    void Submit(in XInputReport report);
}
