using System;
using System.Collections.Generic;
using MphRead.Entities;

namespace MphRead.Hud.Radar;

public sealed class RadarMapPresentation
{
    private const int RasterVersion = 1;
    private const int RasterResolution = 256;
    private const int MaximumCacheEntries = 8;
    private const long MaximumCacheBytes = 8L * 1024 * 1024;
    private static readonly object CacheLock = new();
    private static readonly Dictionary<RadarMapCacheKey, LinkedListNode<RadarMapCacheEntry>> CpuCache = new();
    private static readonly LinkedList<RadarMapCacheEntry> CpuCacheLru = new();
    private static long _cpuCacheBytes;

    private RoomEntity? _room;
    private ulong _roomRevision;
    private TextureIdentity[] _fillTextures = Array.Empty<TextureIdentity>();
    private TextureIdentity[] _outlineTextures = Array.Empty<TextureIdentity>();

    public RadarMapGeometry? Geometry { get; private set; }
    public IReadOnlyList<TextureIdentity> FillTextures => _fillTextures;
    public IReadOnlyList<TextureIdentity> OutlineTextures => _outlineTextures;
    public int GeometryBuildCount { get; private set; }
    public int GeometryInvalidationCount { get; private set; }
    public int CpuCacheHitCount { get; private set; }

    public bool Ensure(ScenePresentation presentation, RoomEntity? room)
    {
        if (room == null)
        {
            Release(presentation);
            return false;
        }

        // RoomEntity owns the revision. This is the steady-state path and is
        // intentionally only an identity/revision comparison: collision lists
        // are not scanned once the current presentation is valid.
        ulong revision = room.RadarGeometryRevision;
        if (ReferenceEquals(_room, room) && _roomRevision == revision)
            return Geometry != null;

        Release(presentation);
        int fingerprint = RadarMapBuilder.ComputeCollisionFingerprint(room.RoomCollision);
        string roomName = room.Meta?.Name ?? string.Empty;
        string contentIdentity = ContentEnvironment.CurrentMatchContent
            ?.MatchContentIdentity
            ?? ContentEnvironment.GetContentIdentity().ContentHash;
        var key = new RadarMapCacheKey(contentIdentity, room.RoomId, roomName,
            fingerprint, RasterVersion, RasterResolution);
        if (!TryGetCpuMaps(key, out RadarMapCacheEntry? cached))
        {
            RadarMapGeometry geometry = RadarMapBuilder.Build(room);
            RadarMap fill = RadarMapRasterizer.Rasterize(geometry, RasterResolution,
                RadarMapRasterLayers.Fill);
            RadarMap outline = RadarMapRasterizer.Rasterize(geometry, RasterResolution,
                RadarMapRasterLayers.Outline);
            cached = new RadarMapCacheEntry(key, geometry, fill, outline,
                EstimateBytes(geometry, fill, outline));
            StoreCpuMaps(cached);
            GeometryBuildCount++;
        }
        else
        {
            CpuCacheHitCount++;
        }

        RadarMapCacheEntry maps = cached!;
        _fillTextures = CreateTextures(presentation, maps.Fill);
        _outlineTextures = CreateTextures(presentation, maps.Outline);
        _room = room;
        _roomRevision = revision;
        Geometry = maps.Geometry;
        return true;
    }

    public void InvalidateIfChanged(ScenePresentation presentation, RoomEntity? room)
    {
        if (_room != null && (!ReferenceEquals(_room, room) || room == null
            || _roomRevision != room.RadarGeometryRevision))
        {
            Release(presentation);
        }
    }

    public void Release(ScenePresentation presentation)
    {
        bool hadPresentation = _room != null || Geometry != null
            || _fillTextures.Length > 0 || _outlineTextures.Length > 0;
        for (int i = 0; i < _fillTextures.Length; i++)
            presentation.ReleaseDynamicTexture(_fillTextures[i]);
        for (int i = 0; i < _outlineTextures.Length; i++)
            presentation.ReleaseDynamicTexture(_outlineTextures[i]);
        _room = null;
        _roomRevision = 0;
        _fillTextures = Array.Empty<TextureIdentity>();
        _outlineTextures = Array.Empty<TextureIdentity>();
        Geometry = null;
        if (hadPresentation) GeometryInvalidationCount++;
    }

    private static TextureIdentity[] CreateTextures(ScenePresentation presentation,
        RadarMap map)
    {
        var textures = new TextureIdentity[map.Floors.Count];
        for (int i = 0; i < textures.Length; i++)
        {
            textures[i] = presentation.CreateDynamicTextureIdentity(map.Floors[i].Pixels,
                map.Width, map.Height);
        }
        return textures;
    }

    private static bool TryGetCpuMaps(RadarMapCacheKey key,
        out RadarMapCacheEntry? entry)
    {
        lock (CacheLock)
        {
            if (!CpuCache.TryGetValue(key, out LinkedListNode<RadarMapCacheEntry>? node))
            {
                entry = null;
                return false;
            }
            CpuCacheLru.Remove(node);
            CpuCacheLru.AddFirst(node);
            entry = node.Value;
            return true;
        }
    }

    private static void StoreCpuMaps(RadarMapCacheEntry entry)
    {
        if (entry.Bytes <= 0 || entry.Bytes > MaximumCacheBytes) return;
        lock (CacheLock)
        {
            if (CpuCache.Remove(entry.Key, out LinkedListNode<RadarMapCacheEntry>? old))
            {
                CpuCacheLru.Remove(old);
                _cpuCacheBytes -= old.Value.Bytes;
            }
            LinkedListNode<RadarMapCacheEntry> node = CpuCacheLru.AddFirst(entry);
            CpuCache[entry.Key] = node;
            _cpuCacheBytes += entry.Bytes;
            while (CpuCache.Count > MaximumCacheEntries || _cpuCacheBytes > MaximumCacheBytes)
            {
                LinkedListNode<RadarMapCacheEntry>? last = CpuCacheLru.Last;
                if (last == null) break;
                CpuCacheLru.RemoveLast();
                CpuCache.Remove(last.Value.Key);
                _cpuCacheBytes -= last.Value.Bytes;
            }
        }
    }

    private static int EstimateBytes(RadarMapGeometry geometry, RadarMap fill,
        RadarMap outline)
    {
        long bytes = 64;
        bytes += EstimateGeometryBytes(geometry);
        bytes += EstimateMapPixels(fill);
        bytes += EstimateMapPixels(outline);
        return bytes > int.MaxValue ? int.MaxValue : (int)bytes;
    }

    private static long EstimateGeometryBytes(RadarMapGeometry geometry)
    {
        long bytes = 64;
        for (int i = 0; i < geometry.Floors.Count; i++)
        {
            RadarFloorBand floor = geometry.Floors[i];
            bytes += 48 + (long)floor.Polygons.Count * 32;
            for (int j = 0; j < floor.Polygons.Count; j++)
                bytes += 32 + (long)floor.Polygons[j].Points.Count * 8;
        }
        return bytes;
    }

    private static long EstimateMapPixels(RadarMap map)
    {
        long bytes = 32;
        for (int i = 0; i < map.Floors.Count; i++)
            bytes += 32 + (long)map.Floors[i].Pixels.Count * 4;
        return bytes;
    }

    private readonly record struct RadarMapCacheKey(string ContentIdentity,
        int RoomId, string RoomName, int CollisionFingerprint,
        int RasterVersion, int Resolution);

    private sealed record RadarMapCacheEntry(RadarMapCacheKey Key,
        RadarMapGeometry Geometry, RadarMap Fill, RadarMap Outline, int Bytes);
}
