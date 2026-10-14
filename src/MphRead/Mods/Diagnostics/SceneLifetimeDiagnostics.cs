using System;
using System.Collections.Generic;
using System.Threading;

namespace MphRead.Mods.Diagnostics;

internal enum SceneLifetimeRole { Live, HeadlessLive, Replica, SideScene, ReplayLab }
internal enum SceneLifetimeStage { Constructed, Loading, Loaded, CleanupStarted, CleanupComplete, VisualResourcesReleased, CompatibilityReleased }
[Flags]
internal enum SceneFacadeRoots { None = 0, GameState = 1, Players = 2, Random = 4 }

// Counts cover the named scene fields only. Null external counts mean untracked,
// not zero; roots describe current facade identity, not a managed GC-root dump.
internal readonly record struct SceneLifetimeSnapshot(long Generation, SceneLifetimeRole Role,
    SceneLifetimeStage Stage, SceneFacadeRoots Facades, int Players, int Entities,
    int OwnedTextures, int MainPrograms, int ModelLeases, int PendingTextureDecodes,
    bool DecoderCancellationRequested, int? ExternalLeases, int? ExternalCallbacks);

// Diagnostic callers opt in on the scene/context thread. The bounded value log
// owns no Scene, player, service, native handle, observer callback or weak target.
internal static class SceneLifetimeDiagnostics
{
    private static long _generation;
    [ThreadStatic] private static Observation? _current;
    internal static long NextGeneration() => Interlocked.Increment(ref _generation);
    internal static bool Enabled => _current != null;
    internal static Observation Begin(int maximumRecords = 256)
    {
        if (maximumRecords < 1 || maximumRecords > 4096)
            throw new ArgumentOutOfRangeException(nameof(maximumRecords));
        var observation = new Observation(maximumRecords, _current);
        _current = observation;
        return observation;
    }
    internal static void Record(SceneLifetimeSnapshot snapshot) => _current?.Record(snapshot);

    internal sealed class Observation : IDisposable
    {
        private readonly int _thread = Environment.CurrentManagedThreadId;
        private readonly Observation? _previous;
        private readonly List<SceneLifetimeSnapshot> _snapshots = new();
        private bool _disposed;
        internal int MaximumRecords { get; }
        internal int DroppedRecords { get; private set; }
        internal IReadOnlyList<SceneLifetimeSnapshot> Snapshots => _snapshots;
        internal Observation(int maximumRecords, Observation? previous)
        { MaximumRecords = maximumRecords; _previous = previous; }
        internal void Record(SceneLifetimeSnapshot snapshot)
        {
            if (_snapshots.Count < MaximumRecords) _snapshots.Add(snapshot);
            else if (DroppedRecords < int.MaxValue) DroppedRecords++;
        }
        public void Dispose()
        {
            if (_disposed) return;
            if (Environment.CurrentManagedThreadId != _thread || !ReferenceEquals(_current, this))
                throw new InvalidOperationException("Dispose lifecycle observations in order on their owning scene thread.");
            _current = _previous;
            _disposed = true;
        }
    }
}
