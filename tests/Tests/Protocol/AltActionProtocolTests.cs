using System;
using System.Buffers.Binary;
using MphRead.Entities;
using MphRead.Mods.Network;
using OpenTK.Mathematics;
using Xunit;

namespace MphRead.Tests;

public sealed class AltActionProtocolTests
{
    [Theory]
    [InlineData(Hunter.Trace, AltActionPhase.Active, 0)]
    [InlineData(Hunter.Weavel, AltActionPhase.Active, 4)]
    [InlineData(Hunter.Spire, AltActionPhase.Active, 0)]
    [InlineData(Hunter.Guardian, AltActionPhase.Active, 0)]
    [InlineData(Hunter.Noxus, AltActionPhase.Charging, 7)]
    [InlineData(Hunter.Noxus, AltActionPhase.Active, 3)]
    [InlineData(Hunter.Trace, AltActionPhase.Recovery, 9)]
    public void Protocol25AltActionRoundTrips(Hunter hunter,
        AltActionPhase phase, ushort ticks)
    {
        SnapshotPlayer source = Player(hunter,
            new AltActionState(phase, ticks));
        byte[] bytes = new byte[SnapshotPlayer.Size];

        source.Write(bytes);

        Assert.True(SnapshotPlayer.TryRead(bytes, out SnapshotPlayer parsed));
        Assert.Equal(source.AltAction, parsed.AltAction);
        Assert.Equal((byte)phase, bytes[112]);
        Assert.Equal(ticks, BinaryPrimitives.ReadUInt16LittleEndian(
            bytes.AsSpan(113)));
        Assert.Equal((phase != AltActionPhase.None),
            (parsed.Flags & SnapshotPlayerFlags.AltAttack) != 0);
    }

    [Fact]
    public void Protocol25RejectsMalformedPhaseAndContextCombinations()
    {
        SnapshotPlayer valid = Player(Hunter.Trace,
            new AltActionState(AltActionPhase.Active, 0));
        byte[] bytes = new byte[SnapshotPlayer.Size];

        valid.Write(bytes);
        bytes[112] = 4;
        Assert.False(SnapshotPlayer.TryRead(bytes, out _));

        valid.Write(bytes);
        bytes[112] = (byte)AltActionPhase.None;
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(113), 1);
        bytes[4] &= unchecked((byte)~(ushort)SnapshotPlayerFlags.AltAttack);
        bytes[5] &= unchecked((byte)~((ushort)SnapshotPlayerFlags.AltAttack >> 8));
        Assert.False(SnapshotPlayer.TryRead(bytes, out _));

        valid.Write(bytes);
        bytes[112] = (byte)AltActionPhase.Active;
        bytes[4] &= unchecked((byte)~(ushort)SnapshotPlayerFlags.AltAttack);
        bytes[5] &= unchecked((byte)~((ushort)SnapshotPlayerFlags.AltAttack >> 8));
        Assert.False(SnapshotPlayer.TryRead(bytes, out _));

        valid.Write(bytes);
        bytes[1] = (byte)Hunter.Samus;
        Assert.False(SnapshotPlayer.TryRead(bytes, out _));

        valid.Write(bytes);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(6), 0);
        Assert.False(SnapshotPlayer.TryRead(bytes, out _));

        valid.Write(bytes);
        bytes[4] &= unchecked((byte)~(ushort)SnapshotPlayerFlags.AltForm);
        Assert.False(SnapshotPlayer.TryRead(bytes, out _));

        valid.Write(bytes);
        bytes[112] = (byte)AltActionPhase.None;
        Assert.False(SnapshotPlayer.TryRead(bytes, out _));
    }

    [Fact]
    public void Protocol24ReplayMapsLegacyAltAttackWithoutGrowingItsRecord()
    {
        SnapshotPlayer source = Player(Hunter.Spire,
            new AltActionState(AltActionPhase.Active, 0));
        byte[] current = new byte[SnapshotPacket.HeaderSize + SnapshotPlayer.Size];
        new SnapshotPacket(4, 7, 9, 0, false, 1, 2).Write(current,
            new[] { source });
        byte[] historical = new byte[SnapshotPacket.HeaderSize
            + SnapshotPlayer.LegacySize];
        current.AsSpan(0, SnapshotPacket.HeaderSize).CopyTo(historical);
        current.AsSpan(SnapshotPacket.HeaderSize, SnapshotPlayer.LegacySize)
            .CopyTo(historical.AsSpan(SnapshotPacket.HeaderSize));
        SnapshotPlayer[] players = new SnapshotPlayer[8];

        Assert.True(Protocol24ReplayCodec.TryReadSnapshot(historical, players,
            out _, out int count));
        Assert.Equal(1, count);
        Assert.Equal(new AltActionState(AltActionPhase.Active, 0),
            players[0].AltAction);
        Assert.Equal(SnapshotPlayer.LegacySize,
            historical.Length - SnapshotPacket.HeaderSize);
    }

    [Theory]
    [InlineData(Hunter.Trace, true, AltActionPhase.Active, 0)]
    [InlineData(Hunter.Weavel, true, AltActionPhase.Active, 0)]
    [InlineData(Hunter.Spire, true, AltActionPhase.Active, 0)]
    [InlineData(Hunter.Guardian, true, AltActionPhase.Active, 0)]
    [InlineData(Hunter.Noxus, false, AltActionPhase.Charging, 4)]
    [InlineData(Hunter.Noxus, true, AltActionPhase.Active, 0)]
    [InlineData(Hunter.Samus, true, AltActionPhase.None, 0)]
    public void AuthorityResolverUsesExactActionSource(Hunter hunter,
        bool active, AltActionPhase expectedPhase, ushort expectedTicks)
    {
        AltActionState state = PlayerEntity.ResolveAltActionState(hunter,
            alive: true, altForm: true, altAttack: active,
            altAttackTime: hunter == Hunter.Noxus ? (ushort)(active ? 10 : 5) : (ushort)0,
            startupTicks: 10);

        Assert.Equal(new AltActionState(expectedPhase, expectedTicks), state);
    }

    [Fact]
    public void NoxusChargingProgressIsAuthoredTimerProgressNotSnapshotCadence()
    {
        AltActionState first = PlayerEntity.ResolveAltActionState(Hunter.Noxus,
            alive: true, altForm: true, altAttack: false,
            altAttackTime: 7, startupTicks: 20);
        AltActionState repeated = PlayerEntity.ResolveAltActionState(Hunter.Noxus,
            alive: true, altForm: true, altAttack: false,
            altAttackTime: 7, startupTicks: 20);

        Assert.Equal(new AltActionState(AltActionPhase.Charging, 6), first);
        Assert.Equal(first, repeated);
    }

    [Fact]
    public void PoseEpochChangesOnlyAtPhaseOrActionDiscontinuitiesAndInterpolationStaysSmooth()
    {
        Assert.True(PlayerEntity.ShouldAdvanceAltActionPoseEpoch(
            AltActionState.None, new AltActionState(AltActionPhase.Active, 0)));
        Assert.False(PlayerEntity.ShouldAdvanceAltActionPoseEpoch(
            new AltActionState(AltActionPhase.Active, 0),
            new AltActionState(AltActionPhase.Active, 17)));
        Assert.True(PlayerEntity.ShouldAdvanceAltActionPoseEpoch(
            new AltActionState(AltActionPhase.Active, 17),
            new AltActionState(AltActionPhase.Active, 2)));

        var history = new SnapshotInterpolation(delayTicks: 0);
        SnapshotPlayer before = Player(Hunter.Spire,
            new AltActionState(AltActionPhase.Active, 1));
        SnapshotPlayer after = Player(Hunter.Spire,
            new AltActionState(AltActionPhase.Active, 8));
        after.Position = new Vector3(10, 0, 0);
        Assert.True(history.Add(new SnapshotPacket(100, 100, 1, 0, false, 0, 0),
            new[] { before }, 100));
        Assert.True(history.Add(new SnapshotPacket(110, 110, 1, 0, false, 0, 0),
            new[] { after }, 110));

        Assert.True(history.TrySample(0, 105, out SnapshotPlayer sampled));
        Assert.Equal(5, sampled.Position.X);
    }

    [Fact]
    public void AuthoritativePhaseClockAdvancesPerSimulationTickAndSaturates()
    {
        AltActionState state = PlayerEntity.AdvanceAltActionClock(
            AltActionState.None, AltActionPhase.Active);
        Assert.Equal(new AltActionState(AltActionPhase.Active, 0), state);
        state = PlayerEntity.AdvanceAltActionClock(state,
            AltActionPhase.Active);
        Assert.Equal(new AltActionState(AltActionPhase.Active, 1), state);
        state = PlayerEntity.AdvanceAltActionClock(
            new AltActionState(AltActionPhase.Active, ushort.MaxValue),
            AltActionPhase.Active);
        Assert.Equal(ushort.MaxValue, state.Ticks);
        Assert.Equal(AltActionState.None, PlayerEntity.AdvanceAltActionClock(
            state, AltActionPhase.None));
    }

    [Fact]
    public void MidActionPresentationFrameUsesAuthoritativeElapsedTicks()
    {
        Assert.Equal(0, PlayerEntity.ResolveAltActionPresentationFrame(
            new AltActionState(AltActionPhase.Active, 0), 20));
        Assert.Equal(6, PlayerEntity.ResolveAltActionPresentationFrame(
            new AltActionState(AltActionPhase.Active, 12), 20));
        Assert.Equal(19, PlayerEntity.ResolveAltActionPresentationFrame(
            new AltActionState(AltActionPhase.Active, ushort.MaxValue), 20));
    }

    [Fact]
    public void NoxusPresentationSeekIsDeterministicAndMonotonic()
    {
        int first = PlayerEntity.ResolveNoxusAltPresentationFrame(
            new AltActionState(AltActionPhase.Charging, 2), 20, 10);
        int second = PlayerEntity.ResolveNoxusAltPresentationFrame(
            new AltActionState(AltActionPhase.Charging, 8), 20, 10);
        int atStartup = PlayerEntity.ResolveNoxusAltPresentationFrame(
            new AltActionState(AltActionPhase.Charging, 10), 20, 10);
        int active = PlayerEntity.ResolveNoxusAltPresentationFrame(
            new AltActionState(AltActionPhase.Active, 0), 20, 10);

        Assert.InRange(first, 0, 19);
        Assert.InRange(second, first, 19);
        Assert.Equal(19, atStartup);
        Assert.Equal(19, active);
        Assert.Equal(second, PlayerEntity.ResolveNoxusAltPresentationFrame(
            new AltActionState(AltActionPhase.Charging, 8), 20, 10));
    }

    private static SnapshotPlayer Player(Hunter hunter, AltActionState action)
    {
        SnapshotPlayerFlags flags = SnapshotPlayerFlags.Active
            | SnapshotPlayerFlags.Spawned | SnapshotPlayerFlags.AltForm;
        if (action.Phase != AltActionPhase.None)
            flags |= SnapshotPlayerFlags.AltAttack;
        return new SnapshotPlayer
        {
            Slot = 0,
            Hunter = hunter,
            TeamIndex = 0,
            Weapon = 0,
            Flags = flags,
            Health = 100,
            Life = 1,
            ConnectionId = 1,
            Position = Vector3.Zero,
            Speed = Vector3.Zero,
            Aim = -Vector3.UnitZ,
            Facing = -Vector3.UnitZ,
            AvailableWeapons = 1,
            EnhancedTargetSlot = 255,
            AltAction = action
        };
    }
}
