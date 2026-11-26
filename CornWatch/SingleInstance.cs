namespace CornWatch;

// One CornWatch per signed-in user. A second launch doesn't start another copy: it signals
// the running one to show its window and exits. Everything CornWatch writes is per-user
// (HKCU Run key, %AppData%), so Local\ (per session) names are enough.
internal sealed class SingleInstance : IDisposable
{
    private const string MutexName = @"Local\CornSystems.CornWatch.SingleInstance";
    private const string ShowEventName = @"Local\CornSystems.CornWatch.ShowWindow";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _showEvent;
    private RegisteredWaitHandle? _wait;

    private SingleInstance(Mutex mutex, EventWaitHandle showEvent)
    {
        _mutex = mutex;
        _showEvent = showEvent;
    }

    // Returns the instance guard when this is the first copy, otherwise null.
    public static SingleInstance? TryAcquire()
    {
        try
        {
            var mutex = new Mutex(true, MutexName, out var isNew);
            if (!isNew)
            {
                mutex.Dispose();
                return null;
            }
            return new SingleInstance(mutex, new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName));
        }
        catch (UnauthorizedAccessException)
        {
            // The mutex exists but was created by an elevated ("Run as administrator") copy,
            // which a normal launch can't open. That copy is still running.
            return null;
        }
    }

    // Asks the running copy to show its window. False if it couldn't be reached
    // (e.g. it is elevated and this launch isn't), so the caller can explain instead.
    public static bool SignalExisting()
    {
        try
        {
            if (!EventWaitHandle.TryOpenExisting(ShowEventName, out var ev)) return false;
            using (ev)
            {
                // Let the running copy take the foreground; otherwise Windows only flashes its taskbar button.
                NativeMethods.AllowSetForegroundWindow(NativeMethods.ASFW_ANY);
                return ev.Set();
            }
        }
        catch (Exception ex)
        {
            SessionLog.Write("SINGLE_INSTANCE_SIGNAL", ex);
            return false;
        }
    }

    // Calls onShow (on a thread-pool thread) each time another launch asks for the window.
    public void ListenForShowRequests(Action onShow) =>
        _wait = ThreadPool.RegisterWaitForSingleObject(_showEvent, (_, _) => onShow(), null, Timeout.Infinite, executeOnlyOnce: false);

    public void Dispose()
    {
        _wait?.Unregister(null);
        _showEvent.Dispose();
        try { _mutex.ReleaseMutex(); }
        catch (Exception ex) { SessionLog.Write("SINGLE_INSTANCE_RELEASE", ex); }
        _mutex.Dispose();
    }
}
