using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.Network;

/// <summary>Gameplay content agreement; rendering/texture substitutions intentionally do not participate.</summary>
internal static class StockGameplayIdentity
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, (string Stamp, MapHash256 Hash)> Cache = new(StringComparer.Ordinal);
    internal static MapHash256 ForRoom(string room)
    {
        if (!Metadata.IsBuiltInRoom(room)) return default;
        var (metadata, _) = Metadata.GetRoomByName(room);
        if (metadata == null) throw new InvalidDataException("Unknown stock map: " + room);
        string root = metadata.FirstHunt ? Paths.FhFileSystem : Paths.FileSystem;
        string[] sources = new[] { metadata.CollisionPath, metadata.EntityPath, metadata.NodePath }
            .Where(path => path != null).Select(path => Paths.Combine(root, path!)).ToArray();
        string rules = JsonSerializer.Serialize(new
        {
            Contract = 1, metadata.Name, metadata.FirstHunt, metadata.Hybrid,
            metadata.KillHeight, metadata.NodeLayer, metadata.RoomNodeName, metadata.HasLimits,
            PlayerMin = new[] { metadata.PlayerMin.X, metadata.PlayerMin.Y, metadata.PlayerMin.Z },
            PlayerMax = new[] { metadata.PlayerMax.X, metadata.PlayerMax.Y, metadata.PlayerMax.Z }
        });
        return Compute(sources, rules);
    }

    // Kept separate for deterministic file fixtures and metadata contract testing.
    internal static MapHash256 Compute(IReadOnlyList<string> sources, string rules)
    {
        string key = string.Join("\n", sources.Select(Path.GetFullPath)) + "\n" + rules;
        var info = sources.Select(path => new FileInfo(path)).ToArray();
        if (info.Any(file => !file.Exists)) throw new InvalidDataException("Stock gameplay content is missing; repair game-file extraction.");
        string stamp = string.Join(";", info.Select(file => file.Length + ":" + file.LastWriteTimeUtc.Ticks));
        lock (Gate) if (Cache.TryGetValue(key, out var cached) && cached.Stamp == stamp) return cached.Hash;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes(rules));
        byte[] buffer = new byte[65536];
        Span<byte> length = stackalloc byte[8];
        foreach (string path in sources)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(length, stream.Length);
            hash.AppendData(length);
            int count; while ((count = stream.Read(buffer)) != 0) hash.AppendData(buffer.AsSpan(0, count));
        }
        // Do not cache a result spanning an external extraction change.
        var after = sources.Select(path => new FileInfo(path)).ToArray();
        string current = string.Join(";", after.Select(file => file.Exists ? file.Length + ":" + file.LastWriteTimeUtc.Ticks : "missing"));
        if (current != stamp) throw new IOException("Stock gameplay files changed during identity validation.");
        var digest = MapHash256.Read(hash.GetHashAndReset());
        lock (Gate) { if (Cache.Count >= 256) Cache.Clear(); Cache[key] = (stamp, digest); }
        return digest;
    }
}
