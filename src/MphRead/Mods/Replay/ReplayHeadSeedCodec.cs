using System;
using System.IO;
using MphRead.Mods.CustomWeapons;
using MphRead.Mods.Network;
using MphRead.Mods.Render;
using OpenTK.Mathematics;

namespace MphRead.Mods.Replay;

// Value-only, bounded custom-world appendix. No type names, pointers, delegates,
// local event domain or allocation counts supplied by the archive are admitted.
internal sealed class ReplayHeadSeedCodec
{
    internal const int MaximumBytes = 128 * 1024;
    private readonly ReplayReplicaState _state;
    private readonly Scene? _scene;
    private readonly object[] _actors = new object[8];
    internal uint Frame { get; }
    internal ReplayHeadSeedCodec(ReplayReplicaState state, uint frame, Scene? scene = null)
    { _state = state; Frame = frame; _scene = scene; for (int i = 0; i < 8; i++) _actors[i] = new object(); }
    internal static bool Boolean(BinaryReader reader)
    { byte value = reader.ReadByte(); return value <= 1 ? value != 0 : throw new InvalidDataException("Noncanonical head-history boolean."); }
    internal uint Past(BinaryReader reader)
    { uint value = reader.ReadUInt32(); return value <= Frame ? value : throw new InvalidDataException("Future head-history clock."); }
    internal static Vector3 Vector(BinaryReader reader, bool direction = false)
    {
        var value = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z)
            || MathF.Abs(value.X) > 1_000_000 || MathF.Abs(value.Y) > 1_000_000 || MathF.Abs(value.Z) > 1_000_000
            || direction && value.LengthSquared > 1.001f)
            throw new InvalidDataException("Invalid head-history vector.");
        return value;
    }
    internal static void Vector(BinaryWriter writer, Vector3 value)
    { writer.Write(value.X); writer.Write(value.Y); writer.Write(value.Z); }
    internal void Victim(BinaryWriter writer, HeadHitVictim victim)
    {
        writer.Write(victim.Actor != null);
        if (victim.Actor == null) { if (victim != default) throw new InvalidDataException("Unbound head-history identity."); return; }
        if (_scene == null || victim.Slot >= 8 || !ReferenceEquals(victim.Actor, _scene.Players.Items[victim.Slot])
            || victim.Scene != _scene.LifetimeGeneration
            || victim.Timeline != ((_scene.Services as ReplaySceneServices)?.Session.Transport.SeekGeneration ?? -1))
            throw new InvalidDataException("Foreign head-history owner.");
        writer.Write(victim.Slot); writer.Write(victim.Match); writer.Write(victim.Epoch);
        writer.Write(victim.Generation); writer.Write(victim.Life);
    }
    internal HeadHitVictim Victim(BinaryReader reader, int? slot = null)
    {
        if (!Boolean(reader)) return default;
        byte actor = reader.ReadByte(); ushort match = reader.ReadUInt16(); ulong epoch = reader.ReadUInt64();
        ushort generation = reader.ReadUInt16(); uint life = reader.ReadUInt32();
        if (actor >= 8 || slot.HasValue && actor != slot || life > ushort.MaxValue
            || (match == 0) != (epoch == 0) || life != 0 && (match == 0 || generation == 0)
            || match != 0 && (_state.Match is not { } current || current.MatchId != match || current.AuthorityEpoch != epoch)
            || slot.HasValue && life != 0 && !_state.MatchesLife(actor, generation, (ushort)life))
            throw new InvalidDataException("Invalid head-history victim identity.");
        return new(_scene?.LifetimeGeneration ?? 1, _scene == null ? _actors[actor] : _scene.Players.Items[actor],
            actor, match, epoch, generation, life, (_scene?.Services as ReplaySceneServices)?.Session.Transport.SeekGeneration ?? 0);
    }
    internal static void Event(BinaryWriter writer, HeadHitEvent value)
    {
        writer.Write((byte)value.Domain);
        if (value == default) return;
        if (!value.IsValid || value.Domain == HeadHitDomain.Local) throw new InvalidDataException("Invalid head-history event.");
        if (value.Domain == HeadHitDomain.Native)
        {
            var key = value.Native;
            writer.Write(key.AuthorityEpoch); writer.Write(key.MatchId); writer.Write(key.ShooterSlot);
            writer.Write(key.Generation); writer.Write(key.LifeId); writer.Write(key.ShotId);
        }
        else
        {
            var key = value.Custom; var action = key.Action; var owner = action.Owner;
            writer.Write(owner.MatchId); writer.Write(owner.AuthorityEpoch); writer.Write(owner.Slot);
            writer.Write(owner.Generation); writer.Write(owner.Life);
            Span<byte> bytes = stackalloc byte[32]; action.Manifest.Write(bytes); writer.Write(bytes);
            writer.Write(action.Weapon.Value); writer.Write(action.Sequence);
            writer.Write(key.Victim.Slot); writer.Write(key.Victim.Generation); writer.Write(key.Victim.Life);
            writer.Write(key.Component); writer.Write((byte)key.Channel);
            value.Bootstrap.Write(bytes); writer.Write(bytes);
        }
        writer.Write(value.DamageEvent);
    }
    internal HeadHitEvent Event(BinaryReader reader, HeadHitVictim victim)
    {
        byte domain = reader.ReadByte(); if (domain == 0) return default;
        ShotKey native = default; CustomPaymentKey custom = default; WorldBootstrapIdentity bootstrap = default;
        if (domain == (byte)HeadHitDomain.Native)
            native = new(reader.ReadUInt64(), reader.ReadUInt16(), reader.ReadInt32(), reader.ReadUInt16(), reader.ReadUInt16(), reader.ReadUInt32());
        else if (domain == (byte)HeadHitDomain.Custom)
        {
            var owner = new WeaponOwnerContext(reader.ReadUInt16(), reader.ReadUInt64(), reader.ReadByte(), reader.ReadUInt16(), reader.ReadUInt16());
            var manifest = WeaponDigest.Read(Fixed(reader, 32)); var weapon = new WeaponRuntimeId(reader.ReadUInt16());
            uint sequence = reader.ReadUInt32();
            var target = new CustomVictimIdentity(reader.ReadByte(), reader.ReadUInt16(), reader.ReadUInt16());
            custom = new(new(owner, manifest, weapon, sequence), target, reader.ReadUInt32(), (CustomImpactChannel)reader.ReadByte());
            if (!WorldBootstrapIdentity.TryRead(Fixed(reader, 32), out bootstrap)) throw new InvalidDataException("Invalid head-history bootstrap.");
        }
        else throw new InvalidDataException("Unsupported head-history event domain.");
        var value = new HeadHitEvent((HeadHitDomain)domain, native, custom, bootstrap, 0, reader.ReadUInt16());
        if (!victim.IsValid || !value.IsValid
            || domain == 1 && (native.MatchId != victim.Match || native.AuthorityEpoch != victim.Epoch)
            || domain == 2 && (custom.Action.Owner.MatchId != victim.Match || custom.Action.Owner.AuthorityEpoch != victim.Epoch
                || custom.Victim.Slot != victim.Slot || custom.Victim.Generation != victim.Generation || custom.Victim.Life != victim.Life
                || custom.Action.Manifest != _state.CustomWeapons!.Context.Bootstrap.Packages.MechanicsDigest
                || bootstrap.Start != _state.CustomWeapons.Context.Bootstrap.World.Start
                || custom.Action.Weapon != _state.CustomWeapons.Context.Validation.Manifest.Rows[0].RuntimeId))
            throw new InvalidDataException("Head-history event differs from its victim or manifest.");
        return value;
    }
    internal void Fact(BinaryWriter writer, HeadHitFact fact)
    { Victim(writer, fact.Victim); Event(writer, fact.Event); writer.Write(fact.ResolveWorld); Vector(writer, fact.Direction); }
    internal HeadHitFact Fact(BinaryReader reader)
    {
        var victim = Victim(reader); var key = Event(reader, victim); uint at = reader.ReadUInt32(); var direction = Vector(reader, true);
        var value = new HeadHitFact(victim, key, at, direction);
        if (!key.IsValid ? value != default : (ulong)at > (ulong)Frame + HeadHitTrack.MaximumAge)
            throw new InvalidDataException("Invalid pending head-history fact.");
        return value;
    }
    internal static byte[] Fixed(BinaryReader reader, int count)
    { var value = reader.ReadBytes(count); return value.Length == count ? value : throw new InvalidDataException("Truncated head-history seed."); }
}
