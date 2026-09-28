using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MphRead.Mods.MapGen;

/// <summary>Resource ownership fence shared by live scenes, replay scenes and package publication.</summary>
public static class MapRuntimeUsage
{
    internal static readonly object Gate = new();
    private static readonly List<(WeakReference<Scene> Scene, string Room)> Readers = new();
    private static readonly Dictionary<string, int> Preparations = new(StringComparer.OrdinalIgnoreCase);
    internal static IDisposable AcquirePreparation(string room)
    {
        lock (Gate) { Preparations.TryGetValue(room, out int count); Preparations[room] = count + 1; }
        return new PreparationLease(room);
    }
    private sealed class PreparationLease(string room) : IDisposable
    {
        private bool _disposed;
        public void Dispose()
        {
            lock (Gate)
            {
                if (_disposed) return; _disposed = true;
                if (--Preparations[room] == 0) Preparations.Remove(room);
            }
        }
    }
    internal static void Track(Scene scene, string room)
    {
        lock (Gate)
        {
            Release(scene);
            Readers.Add((new(scene), room));
        }
    }
    internal static void Release(Scene scene)
    {
        lock (Gate) Readers.RemoveAll(entry => !entry.Scene.TryGetTarget(out var target) || ReferenceEquals(target, scene));
    }
    public static bool IsInUse(string room)
    {
        lock (Gate)
        {
            Readers.RemoveAll(entry => !entry.Scene.TryGetTarget(out _));
            return Preparations.ContainsKey(room) || Readers.Any(entry => entry.Room.Equals(room, StringComparison.OrdinalIgnoreCase));
        }
    }
    public static void RequireInstallationAllowed(string? room = null, bool initialJoin = false)
    {
        lock (Gate)
        {
            Readers.RemoveAll(entry => !entry.Scene.TryGetTarget(out _));
            if (Network.NetSession.Active && Network.NetSession.SessionPhase != Network.SessionPhase.Lobby
                && !(initialJoin && Network.NetSession.IsClient && Readers.Count == 0))
                throw new IOException("Return to the lobby before installing map content.");
            if (room == null ? (Readers.Count != 0 || Preparations.Count != 0) : IsInUse(room))
                throw new IOException("A scene is still using map content. Close it before installing this map.");
        }
    }
}
