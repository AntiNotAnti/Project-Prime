using System;
using System.Threading;

namespace MphRead.Mods.Settings;

/// <summary>Serialize preference writes with import/reset; old process values cannot overwrite a new disk generation.</summary>
internal static class SettingsPersistence
{
    internal static readonly object Gate = new();
    private static volatile bool _restartRequired;
    internal static bool RestartRequired { get => _restartRequired; private set => _restartRequired = value; }
    internal static IDisposable? BeginWrite()
    {
        Monitor.Enter(Gate);
        if (RestartRequired) { Monitor.Exit(Gate); return null; }
        return new WriteLease();
    }
    internal static void Replace(Action install)
    {
        lock (Gate)
        {
            bool previous = RestartRequired;
            RestartRequired = true;
            try { install(); }
            catch (AggregateException) { throw; } // Incomplete rollback: keep all old writers fenced off.
            catch { RestartRequired = previous; throw; }
        }
    }
    private sealed class WriteLease : IDisposable
    {
        private bool _disposed;
        public void Dispose() { if (!_disposed) { _disposed = true; Monitor.Exit(Gate); } }
    }
}
