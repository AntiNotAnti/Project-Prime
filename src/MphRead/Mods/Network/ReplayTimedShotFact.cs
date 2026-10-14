using System;
using System.IO;
using OpenTK.Mathematics;

namespace MphRead.Mods.Network;

// Recorded observations only. These channels do not authorize replica damage
// or replace the existing authored-fire/paid-result presentation contracts.
internal readonly record struct ReplayTimedShotChannels(float Body, float Head, float Splash,
    bool Balanced, byte Effects, byte DirectOnlyEffects, bool Present);

internal readonly record struct ReplayTimedShotFact(ShotKey Key, FireEvent Fire, uint Room,
    uint AuthorityFrame, TimedShotSource Lane, TimedGrantProofMode CaptureMode,
    ShotSourceEvidence Source, TimedGrantAnswer LedgerAnswer, TimedGrantKnowledge Knowledge,
    TimedShotFunding? Funding, TimedShotUnavailable Unavailable, int Components,
    ReplayTimedShotChannels Channels)
{
    // File-only tag: not a PacketType, never sent or accepted on a live wire.
    // 253/254/255 already own construction, semantic and quiet-frame records.
    internal const byte Tag = 252, Version = 1;
    internal const int Size = 216;
    internal static ReplayTimedShotFact From(in TimedShotCertificate value, TimedGrantProofMode mode)
        => new(value.Key, CanonicalFire(value.Fire), value.Room, value.AuthorityFrame, value.Lane, mode,
            value.Source, value.LedgerAnswer, value.Knowledge, value.Funding, value.Unavailable,
            value.Components, new(value.Damage.Body, value.Damage.Head, value.Damage.Splash,
                value.Damage.Balanced, value.Damage.Effects, value.Damage.DirectOnlyEffects, value.Damage.Present));
    private static FireEvent CanonicalFire(in FireEvent value)
    {
        // Reuse the existing replay/wire FireEvent precision. Source clocks,
        // lifecycle, hints and native channels are not inferred or rewritten.
        Span<byte> bytes = stackalloc byte[FireEvent.Size]; value.Write(bytes); return FireEvent.Read(bytes);
    }

    private static bool KeyValid(in ShotKey key, bool shot)
        => key.AuthorityEpoch != 0 && key.MatchId != 0 && (uint)key.ShooterSlot < 8
            && key.Generation != 0 && key.LifeId != 0 && (shot ? key.ShotId != 0 : key.ShotId == 0);
    private static bool Channel(float value) => float.IsFinite(value) && value >= 0 && (double)value <= int.MaxValue;
    internal bool IsValid
    {
        get
        {
            const byte effects = HitClaimPacket.FlagFrozen | HitClaimPacket.FlagBurning | HitClaimPacket.FlagDisrupted;
            if (!KeyValid(Key, shot: true) || Key.ShotId != Fire.ShotId || Key.LifeId != Fire.SourceLife
                || !NetTimedShotModifiers.Ordinary(Fire.Weapon)
                || Fire.Kind is FireEventKind.ContinuousTick or FireEventKind.TurretFire
                || !Enum.IsDefined(Fire.Kind) || !Fire.HasPose
                || !Sane(Fire.Origin) || !Sane(Fire.Direction) || !Sane(Fire.Aim) || !Sane(Fire.View)
                || !float.IsFinite(Fire.Reticle.X) || !float.IsFinite(Fire.Reticle.Y)
                || Fire.Direction.LengthSquared < .000001f
                || Lane is not (TimedShotSource.NativeInput or TimedShotSource.AcceptedFire)
                || CaptureMode is not (TimedGrantProofMode.Shadow or TimedGrantProofMode.Enforce)
                || !Enum.IsDefined(Source.Source) || !Enum.IsDefined(Source.Pose)
                || Source.AmmoType is < 0 or > 1 || Source.Cost < 0 || Source.Cooldown < 0
                || !Enum.IsDefined(LedgerAnswer.Knowledge) || !Enum.IsDefined(Knowledge)
                || !Enum.IsDefined(Unavailable) || Components is < 1 or > ushort.MaxValue
                || !Channels.Present || !Channel(Channels.Body) || !Channel(Channels.Head) || !Channel(Channels.Splash)
                || (Channels.Effects & ~effects) != 0 || (Channels.DirectOnlyEffects & ~Channels.Effects) != 0)
                return false;
            if (Funding is { } funding)
            {
                if (funding.Sequence == 0 || funding.Sequence != LedgerAnswer.Sequence
                    || LedgerAnswer.Knowledge != TimedGrantKnowledge.Granted || Knowledge != TimedGrantKnowledge.Granted
                    || !KeyValid(funding.Life, shot: false) || funding.Life != (Key with { ShotId = 0 })
                    || funding.Type != TimedPowerUp.DoubleDamage || Room == 0
                    || funding.Spawn.Epoch != Key.AuthorityEpoch || funding.Spawn.Match != Key.MatchId
                    || funding.Spawn.Room != Room || funding.Spawn.SpawnId < -1 || funding.Spawn.Generation == 0
                    || unchecked(funding.ExpiresAtFrame - funding.ContactFrame) != 1800) return false;
            }
            else if (Knowledge == TimedGrantKnowledge.Granted) return false;
            return true;
        }
    }
    private static bool Sane(Vector3 vector) => float.IsFinite(vector.X) && float.IsFinite(vector.Y) && float.IsFinite(vector.Z);
    private static void WriteKey(BinaryWriter writer, in ShotKey key)
    {
        writer.Write(key.AuthorityEpoch); writer.Write(key.MatchId); writer.Write((byte)key.ShooterSlot);
        writer.Write(key.Generation); writer.Write(key.LifeId); writer.Write(key.ShotId);
    }
    private static ShotKey ReadKey(BinaryReader reader)
        => new(reader.ReadUInt64(), reader.ReadUInt16(), reader.ReadByte(), reader.ReadUInt16(), reader.ReadUInt16(), reader.ReadUInt32());
    internal byte[] Packet()
    {
        if (!IsValid) throw new InvalidDataException("Invalid recorded timed shot certificate.");
        using var stream = new MemoryStream(Size); using var writer = new BinaryWriter(stream);
        writer.Write(Tag); writer.Write(Version); WriteKey(writer, Key);
        Span<byte> fire = stackalloc byte[FireEvent.Size]; Fire.Write(fire); writer.Write(fire);
        writer.Write(Room); writer.Write(AuthorityFrame); writer.Write((byte)Lane); writer.Write((byte)CaptureMode);
        writer.Write((byte)Source.Source); writer.Write((byte)Source.Pose);
        writer.Write((byte)(Source.DoubleHintAgrees is null ? 0 : Source.DoubleHintAgrees.Value ? 2 : 1));
        writer.Write(Source.AmmoType); writer.Write(Source.Cost); writer.Write(Source.Cooldown);
        writer.Write((byte)LedgerAnswer.Knowledge); writer.Write(LedgerAnswer.Sequence); writer.Write((byte)Knowledge);
        writer.Write(Funding.HasValue);
        var funding = Funding.GetValueOrDefault();
        writer.Write(funding.Sequence); writer.Write(funding.Spawn.Epoch); writer.Write(funding.Spawn.Match);
        writer.Write(funding.Spawn.Room); writer.Write(funding.Spawn.SpawnId); writer.Write(funding.Spawn.Generation);
        WriteKey(writer, funding.Life); writer.Write((byte)funding.Type);
        writer.Write(funding.ContactFrame); writer.Write(funding.SourceContactFrame);
        writer.Write(funding.ExpiresAtFrame); writer.Write(funding.ObservationAuthorityFrame);
        writer.Write((byte)Unavailable); writer.Write((ushort)Components);
        writer.Write(Channels.Body); writer.Write(Channels.Head); writer.Write(Channels.Splash);
        writer.Write(Channels.Balanced); writer.Write(Channels.Effects); writer.Write(Channels.DirectOnlyEffects); writer.Write(Channels.Present);
        writer.Flush();
        if (stream.Length != Size) throw new InvalidDataException("Timed shot schema size differs from its fixed contract.");
        return stream.ToArray();
    }
    internal static bool TryRead(ReadOnlySpan<byte> packet, out ReplayTimedShotFact fact)
    {
        fact = default;
        if (packet.Length != Size || packet[0] != Tag || packet[1] != Version) return false;
        try
        {
            using var stream = new MemoryStream(packet.ToArray(), writable: false); using var reader = new BinaryReader(stream);
            reader.ReadByte(); reader.ReadByte(); var key = ReadKey(reader);
            var fire = FireEvent.Read(reader.ReadBytes(FireEvent.Size));
            uint room = reader.ReadUInt32(), authority = reader.ReadUInt32();
            var lane = (TimedShotSource)reader.ReadByte(); var mode = (TimedGrantProofMode)reader.ReadByte();
            var source = (ShotProofReason)reader.ReadByte(); var pose = (ShotProofReason)reader.ReadByte();
            byte hint = reader.ReadByte(); if (hint > 2) return false;
            var evidence = new ShotSourceEvidence(source, pose, hint == 0 ? null : hint == 2,
                reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());
            var answer = new TimedGrantAnswer((TimedGrantKnowledge)reader.ReadByte(), reader.ReadUInt64());
            var knowledge = (TimedGrantKnowledge)reader.ReadByte();
            byte hasFunding = reader.ReadByte(); if (hasFunding > 1) return false;
            var funding = new TimedShotFunding(reader.ReadUInt64(),
                new(reader.ReadUInt64(), reader.ReadUInt16(), reader.ReadUInt32(), reader.ReadInt32(), reader.ReadUInt32()),
                ReadKey(reader), (TimedPowerUp)reader.ReadByte(), reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt32());
            if (hasFunding == 0 && funding != default) return false;
            var unavailable = (TimedShotUnavailable)reader.ReadByte(); int components = reader.ReadUInt16();
            float body = reader.ReadSingle(), head = reader.ReadSingle(), splash = reader.ReadSingle();
            byte balanced = reader.ReadByte(), effects = reader.ReadByte(), direct = reader.ReadByte(), present = reader.ReadByte();
            if (balanced > 1 || present > 1 || stream.Position != stream.Length) return false;
            var value = new ReplayTimedShotFact(key, fire, room, authority, lane, mode, evidence, answer,
                knowledge, hasFunding == 0 ? null : funding, unavailable, components,
                new(body, head, splash, balanced == 1, effects, direct, present == 1));
            if (!value.IsValid || !value.Packet().AsSpan().SequenceEqual(packet)) return false;
            fact = value; return true;
        }
        catch (Exception ex) when (ex is EndOfStreamException or ArgumentException or OverflowException) { return false; }
    }
}
