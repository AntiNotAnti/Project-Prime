using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

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
        private readonly bool _owned;
        internal Lease(Mutex? mutex, bool owned = true)
        {
            _mutex = mutex;
            _owned = owned;
        }
        public void Dispose()
        {
            Mutex? mutex = Interlocked.Exchange(ref _mutex, null);
            if (mutex == null) return;
            if (_owned)
            {
                try { mutex.ReleaseMutex(); }
                catch (ApplicationException) { }
            }
            mutex.Dispose();
        }
    }

    internal static Lease? TryAcquire(TimeSpan timeout)
    {
        try
        {
            using var sha = SHA256.Create();
            string root = Path.GetFullPath(AppContext.BaseDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .ToUpperInvariant();
            string suffix = Convert.ToHexString(
                sha.ComputeHash(Encoding.UTF8.GetBytes(root))).Substring(0, 16);
            var mutex = new Mutex(false, "ProjectPrime.InteractiveClient." + suffix);
            bool owned;
            try { owned = mutex.WaitOne(timeout); }
            catch (AbandonedMutexException) { owned = true; }
            if (!owned)
            {
                mutex.Dispose();
                DebugLog.Line("startup",
                    "another interactive Project Prime client is still running; duplicate launch suppressed");
                return null;
            }
            return new Lease(mutex);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or PlatformNotSupportedException)
        {
            // Instance coordination is an optimization and safety rail, not a
            // reason to make an otherwise valid platform unable to launch.
            DebugLog.Line("startup", "single-client coordination unavailable: " + ex.Message);
            return new Lease(null, owned: false);
        }
    }
}
