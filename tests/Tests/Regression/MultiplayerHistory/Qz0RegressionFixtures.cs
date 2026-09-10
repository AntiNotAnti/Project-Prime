using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using MphRead.Combat;
using MphRead.Entities;
using MphRead.Mods.Network;
using MphRead.Sound;
using OpenTK.Mathematics;

namespace MphRead.Tests;

/// <summary>Deterministic uint-wrap samples shared by clock, join, and snapshot regressions.</summary>
internal sealed class WraparoundClockFixture
{
    internal const int Seed = 0x514A_0001;
    private readonly Random _random = new(Seed);

    internal uint BeforeWrap { get; } = UInt32.MaxValue - 2;
    internal uint AfterWrap { get; } = 3;
    internal long SentAt { get; } = StopwatchTimestamp(seconds: 20);
    internal long RoundTrip { get; } = System.Diagnostics.Stopwatch.Frequency / 60;

    internal ulong NextNonce()
    {
        Span<byte> bytes = stackalloc byte[sizeof(ulong)];
        ulong value;
        do
        {
            _random.NextBytes(bytes);
            value = BinaryPrimitives.ReadUInt64LittleEndian(bytes);
        } while (value == 0);
        return value;
    }

    internal SnapshotPlayer Player(float x, ulong connectionId = 0x5100_0001) => new()
    {
        Slot = 0,
        ConnectionId = connectionId,
        Life = 1,
        Health = 100,
        Flags = SnapshotPlayerFlags.Active | SnapshotPlayerFlags.Spawned,
        Position = new Vector3(x, 0, 0),
        Speed = Vector3.UnitX,
        Aim = Vector3.UnitZ,
        Facing = Vector3.UnitZ,
        AvailableWeapons = 1
    };

    internal static long StopwatchTimestamp(int seconds)
        => checked(System.Diagnostics.Stopwatch.Frequency * seconds);
}

/// <summary>Stable full identities for slot-reuse, stale projectile, and assist regressions.</summary>
internal sealed class SlotReuseFixture
{
    internal const int Seed = 0x514A_0002;
    private readonly Random _random = new(Seed);

    internal CombatActor Original { get; } = new(2, 0x2002, 7);
    internal CombatActor ReplacementLife { get; } = new(2, 0x2002, 8);
    internal CombatActor ReplacementConnection { get; } = new(2, 0x3002, 1);
    internal CombatActor Victim { get; } = new(0, 0x1000, 4);
    internal CombatActor Killer { get; } = new(1, 0x1001, 3);

    internal uint NextTick(uint minimum = 1)
        => checked(minimum + (uint)_random.Next(1, 64));

    internal DamageContributionLedger LedgerWithOriginalContribution(int damage = 30)
    {
        var ledger = new DamageContributionLedger();
        ledger.Add(Victim, Original, NextTick(), damage, hostile: true);
        return ledger;
    }
}

/// <summary>Fixed traces and planes for moving-geometry history regressions.</summary>
internal sealed class MovingGeometryFixture
{
    internal const int Seed = 0x514A_0003;
    private readonly Random _random = new(Seed);

    internal Vector3 TraceStart { get; } = new(0, 0, -5);
    internal Vector3 TraceEnd { get; } = new(0, 0, 5);
    internal Vector4 DoorPlane { get; } = new(0, 0, 1, 2);
    internal Vector4 ForceFieldPlane { get; } = new(0, 0, 1, -1);

    internal uint NextEntityId() => checked((uint)_random.Next(1, Int32.MaxValue));

    internal DoorEntity Door(Scene scene)
    {
        var header = new EntityDataHeader((ushort)EntityType.Door,
            checked((short)(NextEntityId() % Int16.MaxValue)), Vector3.Zero,
            Vector3.UnitY, Vector3.UnitZ);
        var data = new DoorEntityData(header, "qz0_door", paletteId: 0,
            DoorType.Standard, connectorId: 0, targetLayerId: 0, locked: 0,
            outConnectorId: 0, outLoaderId: 0, entityFilename: null, roomName: null);
        var door = (DoorEntity)RuntimeHelpers.GetUninitializedObject(typeof(DoorEntity));
        var panel = ModelInstance();
        panel.AnimInfo.Index[0] = 0;
        panel.AnimInfo.Frame[0] = 1;
        panel.AnimInfo.FrameCount[0] = 2;
        panel.AnimInfo.Step[0] = 1;
        panel.AnimInfo.Flags[0] = AnimFlags.Ended | AnimFlags.NoLoop | AnimFlags.Reverse;
        Set(typeof(EntityBase), door, "<Type>k__BackingField", EntityType.Door);
        Set(typeof(EntityBase), door, "_scene", scene);
        Set(typeof(EntityBase), door, "_soundSource", new SoundSource(scene));
        Set(typeof(EntityBase), door, "_models", new List<ModelInstance> { panel, ModelInstance() });
        Set(typeof(DoorEntity), door, "_data", data);
        door.Flags = DoorFlags.Closed;
        return door;
    }

    private static ModelInstance ModelInstance()
    {
        var instance = (ModelInstance)RuntimeHelpers.GetUninitializedObject(typeof(ModelInstance));
        Set(typeof(ModelInstance), instance, "<AnimInfo>k__BackingField", new AnimationInfo());
        return instance;
    }

    private static void Set(Type owner, object target, string field, object value)
        => owner.GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);
}

/// <summary>Stable match epochs and replay records for cross-boundary regressions.</summary>
internal sealed class MatchBoundaryFixture
{
    internal const int Seed = 0x514A_0004;
    private readonly Random _random = new(Seed);

    internal uint MatchA { get; } = 0xA001;
    internal uint MatchB { get; } = 0xB001;
    internal uint PhaseA { get; } = 7;
    internal uint PhaseB { get; } = 1;
    internal uint BoundaryTick { get; } = 40;
    internal CombatActor ActorA { get; } = new(0, 0xA100, 2);

    internal byte[] MatchRecord(uint matchId, uint serverTick)
    {
        var packet = new MatchTransitionPacket(matchId, serverTick,
            MatchRules.CreateDefault(MatchMode.Battle, "qz0-boundary"));
        byte[] body = new byte[MatchTransitionPacket.Size];
        packet.Write(body);
        return Record(ReplayRecordKind.Match, body);
    }

    internal byte[] WorldEventRecord(uint matchId, uint phaseRevision, WorldSignalKind kind)
    {
        uint eventId = checked((uint)_random.Next(1, Int32.MaxValue));
        var value = new WorldEvent(eventId, eventId, matchId, phaseRevision,
            WorldSubjectKind.Flag, kind, 1, NextEntityId(), ActorA, Vector3.Zero);
        byte[] body = new byte[5 + WorldEvent.Size];
        BinaryPrimitives.WriteUInt32LittleEndian(body, matchId);
        body[4] = (byte)ReliableEventType.WorldEvent;
        value.Write(body.AsSpan(5));
        return Record(ReplayRecordKind.Event, body);
    }

    private uint NextEntityId() => checked((uint)_random.Next(1, Int32.MaxValue));

    private static byte[] Record(ReplayRecordKind kind, ReadOnlySpan<byte> body)
    {
        byte[] record = new byte[body.Length + 1];
        record[0] = (byte)kind;
        body.CopyTo(record.AsSpan(1));
        return record;
    }
}
