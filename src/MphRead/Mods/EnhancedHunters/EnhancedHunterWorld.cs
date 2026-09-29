using System;
using System.Buffers.Binary;
using MphRead.Entities;
using MphRead.Formats;
using OpenTK.Mathematics;

namespace MphRead.Mods.EnhancedHunters;
public enum EnhancedZoneType : byte { None, IcePatch, MagmaPool }
public struct EnhancedZone
{
    public EnhancedZoneType Type;
    public byte OwnerSlot, Flags;
    public ushort OwnerLifeId, OwnerGeneration, RemainingFrames;
    public Vector3 Position;
    public float Radius;
}

/// <summary>Scene-owned bounded zones; replicas consume values and never resolve damage.</summary>
public sealed class EnhancedHunterWorld
{
    public const int Capacity = 16, ZoneSize = 24;
    public EnhancedZone[] Zones { get; } = new EnhancedZone[Capacity];
    private ulong _lastFrame = ulong.MaxValue;
    private Vector3[][]? _vertices = CreateVertices();
    private Vector3[][]? _indicators = CreateVertices();
    private static Vector3[][] CreateVertices()
    {
        var result = new Vector3[Capacity][];
        for (int i = 0; i < Capacity; i++) result[i] = new Vector3[24];
        return result;
    }
    public void Reset() { Array.Clear(Zones); _lastFrame = ulong.MaxValue; }
    internal int Count(PlayerEntity owner, EnhancedZoneType type)
    {
        int count = 0;
        foreach (var z in Zones) if (Owned(owner, z) && z.Type == type) count++;
        return count;
    }
    internal static bool Owned(PlayerEntity owner, EnhancedZone z) => z.Type != EnhancedZoneType.None
        && z.OwnerSlot == owner.SlotIndex && z.OwnerLifeId == EnhancedHunters.Life(owner)
        && z.OwnerGeneration == EnhancedHunters.Generation(owner);
    internal static bool Contains(EnhancedZone z, Vector3 position) => z.Type != EnhancedZoneType.None
        && MathF.Abs(position.Y - z.Position.Y) <= 1.5f
        && (position.Xz - z.Position.Xz).LengthSquared <= z.Radius * z.Radius;
    internal void Add(PlayerEntity owner, EnhancedZoneType type, Vector3 position)
    {
        if (!EnhancedHunters.Authority(owner)) return;
        int empty = -1, oldest = -1, own = 0;
        for (int i = 0; i < Capacity; i++)
        {
            if (Zones[i].Type == EnhancedZoneType.None) { if (empty < 0) empty = i; }
            else if (Owned(owner, Zones[i]) && Zones[i].Type == type)
            { own++; if (oldest < 0 || Zones[i].RemainingFrames < Zones[oldest].RemainingFrames) oldest = i; }
        }
        int slot = own >= EnhancedHunterTuning.ZonesPerOwner ? oldest : empty;
        if (slot < 0) return;
        Zones[slot] = new EnhancedZone { Type = type, OwnerSlot = (byte)owner.SlotIndex,
            OwnerLifeId = EnhancedHunters.Life(owner), OwnerGeneration = EnhancedHunters.Generation(owner),
            Position = position, Radius = type == EnhancedZoneType.IcePatch ? 1.8f : 1.6f,
            RemainingFrames = (ushort)(type == EnhancedZoneType.IcePatch ? 105 : 135) };
        EnhancedHunterTelemetry.Event(owner.Hunter, type == EnhancedZoneType.IcePatch ? "ice-patches" : "magma-pools");
    }
    internal bool Detonate(PlayerEntity owner, Vector3 impact)
    {
        if (!EnhancedHunters.Authority(owner)) return false;
        for (int i = 0; i < Capacity; i++)
        {
            var z = Zones[i];
            if (z.Type != EnhancedZoneType.MagmaPool || !Owned(owner, z) || !Contains(z, impact)) continue;
            Zones[i] = default;
            foreach (var target in owner.OwningScene.Players.Items)
            {
                Vector3 delta = target.Position - z.Position;
                if (delta.LengthSquared > 16 || !EnhancedHunters.Visible(owner.OwningScene, z.Position.AddY(.1f), target.Position.AddY(.4f))) continue;
                if (target == owner)
                {
                    if (Contains(z, owner.Position))
                    {
                        EnhancedHunterMovement.PushOwner(owner, Vector3.UnitY * .3f + owner.FacingVector.WithY(0) * .12f);
                        EnhancedHunterTelemetry.Event(owner.Hunter, "volcanic-jumps");
                    }
                }
                else EnhancedHunters.Bonus(owner, target, 10,
                    (delta.LengthSquared > .001f ? delta.Normalized() : Vector3.UnitY) * .2f);
            }
            EnhancedHunterTelemetry.Event(owner.Hunter, "pool-detonations");
            return true;
        }
        return false;
    }
    internal void Tick(Scene scene)
    {
        if (_lastFrame == scene.FrameCount) return;
        _lastFrame = scene.FrameCount;
        if (!scene.GameState.EnhancedHunters) { Reset(); return; }
        if (scene.Services.IsReplica || scene.Services.PlayerReplication.Active && !scene.Services.PlayerReplication.IsAuthority) return;
        for (int i = 0; i < Capacity; i++)
        {
            var zone = Zones[i];
            if (zone.Type == EnhancedZoneType.None) continue;
            var owner = scene.Players.Items[zone.OwnerSlot];
            if (!EnhancedHunters.Alive(owner) || !Owned(owner, zone) || zone.RemainingFrames == 0)
            { Zones[i] = default; continue; }
            zone.RemainingFrames--;
            if (zone.RemainingFrames == 0) { Zones[i] = default; continue; }
            if (zone.Type == EnhancedZoneType.MagmaPool && zone.RemainingFrames % 18 == 0)
                foreach (var target in scene.Players.Items)
                    if (EnhancedHunters.Hostile(owner, target) && Contains(zone, target.Position)
                        && EnhancedHunters.Visible(scene, zone.Position.AddY(.1f), target.Position.AddY(.4f)))
                        EnhancedHunters.Bonus(owner, target, 2, Vector3.Zero);
            Zones[i] = zone;
        }
    }
    internal void Movement(PlayerEntity player)
    {
        foreach (var z in Zones)
        {
            if (!Contains(z, player.Position)) continue;
            if (z.Type == EnhancedZoneType.IcePatch && player.Hunter != Hunter.Noxus && player.Flags1.TestFlag(PlayerFlags1.Standing))
                player.Speed = new(player.Speed.X * .7f + player.PrevSpeed.X * .3f,
                    player.Speed.Y, player.Speed.Z * .7f + player.PrevSpeed.Z * .3f);
        }
    }
    internal void Draw(Scene scene)
    {
        if (!scene.GameState.EnhancedHunters) return;
        for (int i = 0; i < Capacity; i++)
        {
            var z = Zones[i]; if (z.Type == EnhancedZoneType.None) continue;
            var vertices = (_vertices ??= CreateVertices())[i];
            for (int j = 0; j < vertices.Length; j++)
            {
                float angle = j * MathF.Tau / vertices.Length;
                vertices[j] = z.Position + new Vector3(MathF.Cos(angle) * z.Radius, .025f, MathF.Sin(angle) * z.Radius);
            }
            var color = z.Type == EnhancedZoneType.IcePatch ? new Vector4(.3f, .75f, 1, .38f)
                : new Vector4(1, .18f + .08f * MathF.Sin((float)scene.FrameCount * .15f), .025f, .55f);
            scene.AddRenderItem(CullingMode.Neither, scene.GetNextPolygonId(), color, RenderItemType.Ngon, vertices, noLines: true);
        }
    }
    internal void DrawIndicators(Scene scene)
    {
        if (!scene.GameState.EnhancedHunters) return;
        foreach (var owner in scene.Players.Items)
        {
            if (!EnhancedHunters.Alive(owner)) continue;
            var s = owner.EnhancedState;
            var target = EnhancedHunters.Target(owner);
            bool core = owner.Hunter == Hunter.Weavel && owner.IsAltForm && s.TimerB > 0;
            if (!core && (target == null || owner.Hunter is not (Hunter.Kanden or Hunter.Noxus or Hunter.Sylux))) continue;
            Vector3 center = core ? owner.Halfturret.Position.AddY(.5f) : target!.Position.AddY(.6f);
            var vertices = (_indicators ??= CreateVertices())[owner.SlotIndex];
            float radius = .3f + .025f * MathF.Sin((float)scene.FrameCount * .25f);
            for (int j = 0; j < vertices.Length; j++)
            {
                float angle = j * MathF.Tau / vertices.Length;
                vertices[j] = center + new Vector3(MathF.Cos(angle) * radius, MathF.Sin(angle) * radius, 0);
            }
            Vector4 color = owner.Hunter switch
            {
                Hunter.Kanden => new(1, .85f, .15f, .25f), Hunter.Noxus => new(.3f, .85f, 1, .25f),
                Hunter.Sylux => new(.3f, 1, .35f, .25f + s.ValueA / 500f), _ => new(1, .4f, .15f, .35f)
            };
            scene.AddRenderItem(CullingMode.Neither, scene.GetNextPolygonId(), color, RenderItemType.Ngon, vertices, noLines: false);
        }
    }
    public int Write(Span<byte> bytes)
    {
        int count = 0;
        foreach (var z in Zones)
        {
            if (z.Type == EnhancedZoneType.None) continue;
            var b = bytes.Slice(1 + count++ * ZoneSize, ZoneSize);
            b.Clear(); b[0] = (byte)z.Type; b[1] = z.OwnerSlot;
            BinaryPrimitives.WriteUInt16LittleEndian(b[2..], z.OwnerLifeId);
            BinaryPrimitives.WriteUInt16LittleEndian(b[4..], z.OwnerGeneration);
            BinaryPrimitives.WriteSingleLittleEndian(b[6..], z.Position.X);
            BinaryPrimitives.WriteSingleLittleEndian(b[10..], z.Position.Y);
            BinaryPrimitives.WriteSingleLittleEndian(b[14..], z.Position.Z);
            BinaryPrimitives.WriteUInt16LittleEndian(b[18..], (ushort)MathF.Round(z.Radius * 256));
            BinaryPrimitives.WriteUInt16LittleEndian(b[20..], z.RemainingFrames);
            b[22] = z.Flags;
        }
        bytes[0] = (byte)count; return 1 + count * ZoneSize;
    }
    public static bool Validate(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty) return true; // historical recordings have no extension
        if (bytes[0] > Capacity || bytes.Length != 1 + bytes[0] * ZoneSize) return false;
        for (int i = 0; i < bytes[0]; i++)
        {
            var b = bytes.Slice(1 + i * ZoneSize, ZoneSize);
            if (b[0] is < 1 or > 2 || b[1] >= 8 || b[22] != 0 || b[23] != 0
                || BinaryPrimitives.ReadUInt16LittleEndian(b[18..]) is 0 or > 1024
                || BinaryPrimitives.ReadUInt16LittleEndian(b[20..]) > 135) return false;
            for (int at = 6; at <= 14; at += 4)
                if (!float.IsFinite(BinaryPrimitives.ReadSingleLittleEndian(b[at..]))) return false;
        }
        return true;
    }
    public void Read(ReadOnlySpan<byte> bytes)
    {
        if (!Validate(bytes)) throw new ArgumentException("Invalid enhanced world state.");
        Array.Clear(Zones);
        if (bytes.IsEmpty) return;
        for (int i = 0; i < bytes[0]; i++)
        {
            var b = bytes.Slice(1 + i * ZoneSize, ZoneSize);
            Zones[i] = new EnhancedZone { Type = (EnhancedZoneType)b[0], OwnerSlot = b[1],
                OwnerLifeId = BinaryPrimitives.ReadUInt16LittleEndian(b[2..]),
                OwnerGeneration = BinaryPrimitives.ReadUInt16LittleEndian(b[4..]),
                Position = new(BinaryPrimitives.ReadSingleLittleEndian(b[6..]), BinaryPrimitives.ReadSingleLittleEndian(b[10..]), BinaryPrimitives.ReadSingleLittleEndian(b[14..])),
                Radius = BinaryPrimitives.ReadUInt16LittleEndian(b[18..]) / 256f,
                RemainingFrames = BinaryPrimitives.ReadUInt16LittleEndian(b[20..]), Flags = b[22] };
        }
    }
}
