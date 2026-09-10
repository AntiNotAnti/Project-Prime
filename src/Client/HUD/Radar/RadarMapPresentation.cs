using System;
using System.Collections.Generic;
using MphRead.Entities;
using MphRead.Formats.Collision;

namespace MphRead.Hud.Radar;

public sealed class RadarMapPresentation
{
    private RoomEntity? _room;
    private CollisionSnapshot[] _snapshot = Array.Empty<CollisionSnapshot>();
    private TextureIdentity[] _textures = Array.Empty<TextureIdentity>();

    public RadarMapGeometry? Geometry { get; private set; }
    public IReadOnlyList<TextureIdentity> FloorTextures => _textures;

    public bool Ensure(ScenePresentation presentation, RoomEntity? room)
    {
        if (room == null)
        {
            Release(presentation);
            return false;
        }
        if (ReferenceEquals(_room, room) && Matches(room.RoomCollision)) return Geometry != null;
        Release(presentation);
        RadarMapGeometry geometry = RadarMapBuilder.Build(room);
        RadarMap map = RadarMapRasterizer.Rasterize(geometry);
        var textures = new TextureIdentity[map.Floors.Count];
        for (int i = 0; i < textures.Length; i++)
            textures[i] = presentation.CreateDynamicTextureIdentity(map.Floors[i].Pixels, map.Width, map.Height);
        _room = room;
        _snapshot = Snapshot(room.RoomCollision);
        _textures = textures;
        Geometry = geometry;
        return true;
    }

    public void InvalidateIfChanged(ScenePresentation presentation, RoomEntity? room)
    {
        if (_room != null && (!ReferenceEquals(_room, room) || room == null || !Matches(room.RoomCollision)))
            Release(presentation);
    }

    public void Release(ScenePresentation presentation)
    {
        for (int i = 0; i < _textures.Length; i++) presentation.ReleaseDynamicTexture(_textures[i]);
        _room = null;
        _snapshot = Array.Empty<CollisionSnapshot>();
        _textures = Array.Empty<TextureIdentity>();
        Geometry = null;
    }

    private bool Matches(IReadOnlyList<CollisionInstance> collisions)
    {
        if (collisions.Count != _snapshot.Length) return false;
        for (int i = 0; i < collisions.Count; i++)
        {
            CollisionInstance value = collisions[i];
            CollisionSnapshot old = _snapshot[i];
            if (!ReferenceEquals(value, old.Instance) || !ReferenceEquals(value.Info, old.Info)
                || value.Active != old.Active || value.Translation != old.Translation) return false;
        }
        return true;
    }

    private static CollisionSnapshot[] Snapshot(IReadOnlyList<CollisionInstance> collisions)
    {
        var result = new CollisionSnapshot[collisions.Count];
        for (int i = 0; i < result.Length; i++)
        {
            CollisionInstance value = collisions[i];
            result[i] = new CollisionSnapshot(value, value.Info, value.Active, value.Translation);
        }
        return result;
    }

    private readonly record struct CollisionSnapshot(CollisionInstance Instance, CollisionInfo Info,
        bool Active, OpenTK.Mathematics.Vector3 Translation);
}
