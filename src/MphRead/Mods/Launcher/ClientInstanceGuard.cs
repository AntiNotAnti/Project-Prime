using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using MphRead.Mods.Update;

namespace MphRead.Mods.Launcher;

/// <summary>
/// Prevent two interactive clients from the same installation overlapping while
/// the first one is still draining native resources. Dedicated servers, thumbnail
/// workers and headless tools never acquire this lease.
/// </summary>
internal static class ClientInstanceGuard
{
    internal sealed class Lease : IDisposable
    {
        private Mutex? _mutex;
        private IDisposable? _installation;
        internal Lease(Mutex mutex, IDisposable? installation = null) { _mutex = mutex; _installation = installation; }
        public void Dispose()
        {
            Mutex? mutex = Interlocked.Exchange(ref _mutex, null);
            if (mutex == null) return;
            try { mutex.ReleaseMutex(); }
            catch (ApplicationException) { }
            mutex.Dispose();
            Interlocked.Exchange(ref _installation, null)?.Dispose();
        }
    }

    private static readonly object Gate = new();
    private static Lease? _processLease;

    /// <summary>
    /// Acquire once for the interactive process and keep ownership through
    /// Program's native audio cleanup. Releasing when the shell window vanished
    /// was too early: a rapid relaunch could still collide with the old audio
    /// backend for the last few seconds of process teardown.
    /// </summary>
    internal static bool TryAcquireForProcess(TimeSpan timeout, bool studio = false)
    {
        lock (Gate)
        {
            if (_processLease != null) return true;
            try
            {
                var mutex = new Mutex(false, DesktopInstallationUse.MutexName(AppContext.BaseDirectory, studio));
                bool owned;
                try { owned = mutex.WaitOne(timeout); }
                catch (AbandonedMutexException) { owned = true; }
                if (!owned)
                {
                    mutex.Dispose();
                    DebugLog.Line("startup",
                        "another interactive Project Prime client is still running; duplicate launch suppressed");
                    return false;
                }
                IDisposable? installation = null;
                try
                {
                    if (OperatingSystem.IsWindows())
                    {
                        ReleaseInstallation.Recover(AppContext.BaseDirectory);
                        try { installation = DesktopInstallationUse.Acquire(AppContext.BaseDirectory); }
                        catch (UnauthorizedAccessException) { } // Read-only installations cannot update in place.
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    mutex.ReleaseMutex(); mutex.Dispose();
                    DebugLog.Line("startup", "installation update is in progress or requires recovery: " + ex.Message);
                    return false;
                }
                _processLease = new Lease(mutex, installation);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                or PlatformNotSupportedException)
            {
                // Instance coordination is an optimization and safety rail, not a
                // reason to make an otherwise valid platform unable to launch.
                DebugLog.Line("startup",
                    "single-client coordination unavailable: " + ex.Message);
                return true;
            }
        }
    }

    internal static void ReleaseProcess()
    {
        Lease? lease;
        lock (Gate)
        {
            lease = _processLease;
            _processLease = null;
        }
        lease?.Dispose();
    }
}
