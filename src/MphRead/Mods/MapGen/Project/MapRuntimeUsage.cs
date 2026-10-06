using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MphRead.Mods.MapGen;

/// <summary>Resource ownership fence shared by live scenes, replay scenes and package publication.</summary>
public static class MapRuntimeUsage
{
    internal static readonly object Gate = new();
    private static readonly List<(WeakReference<Scene> Scene, string Room, MapPublicationLease Lease)> Readers = new();
    private static readonly Dictionary<string, int> Preparations = new(StringComparer.OrdinalIgnoreCase);
    // Policy-only tools can exercise ownership before extracted game paths exist.
    // Actual publication still requires its configured runtime root.
    private static string LeaseRuntimeRoot => string.IsNullOrWhiteSpace(CustomRooms.RuntimePublicationRoot) ? AppContext.BaseDirectory : CustomRooms.RuntimePublicationRoot;
    internal static IDisposable AcquirePreparation(string room)
    {
        lock (Gate)
        {
            if (StudioReplay.StudioReplayResources.Current?.Room(room) != null) return PrivatePreparation.Instance;
            var lease = MapPublicationLease.AcquireReader(LeaseRuntimeRoot, CustomRooms.RuntimeNamespace, room);
            Preparations.TryGetValue(room, out int count); Preparations[room] = count + 1;
            return new PreparationLease(room, lease);
        }
    }
    // The scoped historical room reads its immutable private package, whose
    // lifetime belongs to the replay cache. It does not read game runtime bytes.
    private sealed class PrivatePreparation : IDisposable
    {
        internal static readonly PrivatePreparation Instance = new();
        public void Dispose() { }
    }
    private sealed class PreparationLease(string room, MapPublicationLease lease) : IDisposable
    {
        private bool _disposed;
        public void Dispose()
        {
            lock (Gate)
            {
                if (_disposed) return; _disposed = true;
                if (--Preparations[room] == 0) Preparations.Remove(room);
                lease.Dispose();
            }
        }
    }
    internal static void Track(Scene scene, string room)
    {
        lock (Gate)
        {
            Release(scene);
            if (StudioReplay.StudioReplayResources.Current?.Room(room) != null) return;
            var lease = MapPublicationLease.AcquireReader(LeaseRuntimeRoot, CustomRooms.RuntimeNamespace, room);
            Readers.Add((new(scene), room, lease));
        }
    }
    internal static void Release(Scene scene)
    {
        lock (Gate) RemoveReaders(scene);
    }
    private static void RemoveReaders(Scene? scene = null)
    {
        for (int i = Readers.Count - 1; i >= 0; i--)
        {
            var entry = Readers[i];
            if (entry.Scene.TryGetTarget(out var target) && !ReferenceEquals(target, scene)) continue;
            entry.Lease.Dispose();
            Readers.RemoveAt(i);
        }
    }
    public static bool IsInUse(string room)
    {
        lock (Gate)
        {
            RemoveReaders();
            return Preparations.ContainsKey(room) || Readers.Any(entry => entry.Room.Equals(room, StringComparison.OrdinalIgnoreCase));
        }
    }
    public static void RequireInstallationAllowed(string? room = null, bool initialJoin = false)
    {
        lock (Gate)
        {
            RemoveReaders();
            if (Network.NetSession.Active && Network.NetSession.SessionPhase != Network.SessionPhase.Lobby
                && !(initialJoin && Network.NetSession.IsClient && Readers.Count == 0))
                throw new IOException("Return to the lobby before installing map content.");
            if (room == null ? (Readers.Count != 0 || Preparations.Count != 0) : IsInUse(room))
                throw new IOException("A scene is still using map content. Close it before installing this map.");
        }
    }
}
