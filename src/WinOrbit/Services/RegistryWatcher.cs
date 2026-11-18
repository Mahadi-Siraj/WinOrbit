using System.Runtime.InteropServices;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace WinOrbit.Services;

/// <summary>
/// Wakes up when a value under one registry key is written, using Windows' own change notification
/// (RegNotifyChangeKeyValue) — the process sleeps until then; nothing is polled. The callback runs on a thread-pool
/// thread and may fire for writes that leave the value unchanged, so callers compare before acting.
/// </summary>
internal sealed class RegistryWatcher : IDisposable
{
    private const uint NotifyLastSet = 0x4, NotifyThreadAgnostic = 0x10000000;

    [DllImport("advapi32.dll")]
    private static extern int RegNotifyChangeKeyValue(SafeRegistryHandle key, bool watchSubtree, uint filter, SafeWaitHandle evt, bool asynchronous);

    private readonly RegistryKey _key;
    private readonly AutoResetEvent _signal = new(false);
    private readonly RegisteredWaitHandle _wait;
    private readonly Action _changed;
    private volatile bool _disposed;

    /// <param name="key">An open key; the watcher owns it and closes it on Dispose.</param>
    public RegistryWatcher(RegistryKey key, Action changed)
    {
        _key = key;
        _changed = changed;
        Arm();
        _wait = ThreadPool.RegisterWaitForSingleObject(_signal, (_, _) =>
        {
            if (_disposed) return;
            Arm();   // one notification per registration: re-arm first so a quick second write isn't missed
            try { _changed(); } catch { }
        }, null, Timeout.Infinite, false);
    }

    private void Arm()
    {
        // Thread-agnostic: the registration doesn't end when the thread that made it goes away.
        if (!_disposed) RegNotifyChangeKeyValue(_key.Handle, false, NotifyLastSet | NotifyThreadAgnostic, _signal.SafeWaitHandle, true);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _wait.Unregister(null);
        _key.Dispose();   // closing the key cancels the pending notification
        _signal.Dispose();
    }
}
