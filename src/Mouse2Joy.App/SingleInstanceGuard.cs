namespace Mouse2Joy.App;

internal sealed class SingleInstanceGuard : IDisposable
{
    private readonly Mutex _mutex;
    public bool IsFirstInstance { get; }

    public SingleInstanceGuard(string name)
    {
        _mutex = new Mutex(initiallyOwned: true, name, out var created);
        IsFirstInstance = created;
    }

    public void Dispose()
    {
        try { if (IsFirstInstance) _mutex.ReleaseMutex(); } catch { /* ignore */ }
        _mutex.Dispose();
    }
}
