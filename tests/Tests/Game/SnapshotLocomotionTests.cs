using System;
using MphRead.Entities;
using MphRead.Mods.Network;
using OpenTK.Mathematics;
using Xunit;

namespace MphRead.Tests;

public sealed class SnapshotLocomotionTests
{
    private static SnapshotPlayer Player(Vector3 speed, Vector3? facing = null) => new()
    {
        Slot = 1,
        Hunter = Hunter.Samus,
        TeamIndex = 0,
        Weapon = 0,
        Flags = SnapshotPlayerFlags.Active | SnapshotPlayerFlags.Spawned
            | SnapshotPlayerFlags.Grounded,
        Health = 100,
        Life = 1,
        ConnectionId = 2,
        Position = Vector3.Zero,
        Speed = speed,
        Aim = -Vector3.UnitZ,
        Facing = facing ?? Vector3.UnitZ
    };

    public static TheoryData<Vector3, Vector3, PlayerAnimation> Directions => new()
    {
        { Vector3.UnitZ, Vector3.UnitZ, PlayerAnimation.WalkForward },
        { -Vector3.UnitZ, Vector3.UnitZ, PlayerAnimation.WalkBackward },
        { -Vector3.UnitX, Vector3.UnitZ, PlayerAnimation.WalkRight },
        { Vector3.UnitX, Vector3.UnitZ, PlayerAnimation.WalkLeft },
        { Vector3.UnitX, Vector3.UnitX, PlayerAnimation.WalkForward },
        { -Vector3.UnitX, Vector3.UnitX, PlayerAnimation.WalkBackward },
        { Vector3.UnitZ, Vector3.UnitX, PlayerAnimation.WalkRight },
        { -Vector3.UnitZ, Vector3.UnitX, PlayerAnimation.WalkLeft },
        { new Vector3(-1, 0, 1), Vector3.UnitZ, PlayerAnimation.WalkForward }
    };

    [Theory]
    [MemberData(nameof(Directions))]
    public void GroundedSnapshotSelectsDirectionalWalk(Vector3 speed, Vector3 facing,
        PlayerAnimation expected)
    {
        SnapshotPlayer state = Player(speed, facing);

        Assert.Equal(expected, PlayerEntity.DeriveSnapshotBipedAnimation(state, local: false));
    }

    [Fact]
    public void RemoteLocomotionUsesPresentedVelocityInsteadOfNewestStateSpeed()
    {
        SnapshotPlayer state = Player(Vector3.Zero);
        state.Speed = -Vector3.UnitZ;

        Assert.Equal(PlayerAnimation.WalkForward,
            PlayerEntity.DeriveRemoteBipedAnimation(state, Vector3.UnitZ));
    }

    [Fact]
    public void GroundedSnapshotStopsBelowHorizontalSpeedThreshold()
    {
        SnapshotPlayer stopped = Player(Vector3.Zero);
        SnapshotPlayer vertical = Player(new Vector3(0, 10, 0));
        SnapshotPlayer belowThreshold = Player(new Vector3(0.005f, 0, 0));

        Assert.Equal(PlayerAnimation.Idle,
            PlayerEntity.DeriveSnapshotBipedAnimation(stopped, local: false));
        Assert.Equal(PlayerAnimation.Idle,
            PlayerEntity.DeriveSnapshotBipedAnimation(vertical, local: false));
        Assert.Equal(PlayerAnimation.Idle,
            PlayerEntity.DeriveSnapshotBipedAnimation(belowThreshold, local: false));
    }

    [Fact]
    public void StatefulLocomotionUsesSeparateStartAndStopThresholds()
    {
        var resolver = new RemoteLocomotionHysteresis();
        SnapshotPlayer state = Player(Vector3.Zero);

        Assert.Equal(PlayerAnimation.Idle,
            resolver.Resolve(state, new Vector3(0, 0, 0.015f)));
        Assert.Equal(PlayerAnimation.WalkForward,
            resolver.Resolve(state, new Vector3(0, 0, 0.03f)));
        Assert.Equal(PlayerAnimation.WalkForward,
            resolver.Resolve(state, new Vector3(0, 0, 0.015f)));
        Assert.Equal(PlayerAnimation.Idle,
            resolver.Resolve(state, new Vector3(0, 0, 0.005f)));
        Assert.Equal(PlayerAnimation.Idle,
            resolver.Resolve(state, new Vector3(0, 0, 0.015f)));
    }

    [Fact]
    public void StatefulLocomotionUsesAnAxisSwitchMargin()
    {
        var resolver = new RemoteLocomotionHysteresis();
        SnapshotPlayer state = Player(Vector3.Zero);

        Assert.Equal(PlayerAnimation.WalkForward,
            resolver.Resolve(state, new Vector3(-1.0f, 0, 1.0f)));
        Assert.Equal(PlayerAnimation.WalkForward,
            resolver.Resolve(state, new Vector3(-1.05f, 0, 1.0f)));
        Assert.Equal(PlayerAnimation.WalkRight,
            resolver.Resolve(state, new Vector3(-1.2f, 0, 1.0f)));
        Assert.Equal(PlayerAnimation.WalkRight,
            resolver.Resolve(state, new Vector3(-1.0f, 0, 0.95f)));
        Assert.Equal(PlayerAnimation.WalkForward,
            resolver.Resolve(state, new Vector3(-1.0f, 0, 1.2f)));
    }

    [Fact]
    public void StatefulLocomotionReversalsAreImmediate()
    {
        var resolver = new RemoteLocomotionHysteresis();
        SnapshotPlayer state = Player(Vector3.Zero);

        Assert.Equal(PlayerAnimation.WalkForward,
            resolver.Resolve(state, Vector3.UnitZ));
        Assert.Equal(PlayerAnimation.WalkBackward,
            resolver.Resolve(state, -Vector3.UnitZ));
        Assert.Equal(PlayerAnimation.WalkLeft,
            resolver.Resolve(state, Vector3.UnitX));
        Assert.Equal(PlayerAnimation.WalkRight,
            resolver.Resolve(state, -Vector3.UnitX));
    }

    [Fact]
    public void InvalidLocomotionStateResetsDirectionHistory()
    {
        var resolver = new RemoteLocomotionHysteresis();
        SnapshotPlayer state = Player(Vector3.Zero);

        Assert.Equal(PlayerAnimation.WalkForward,
            resolver.Resolve(state, Vector3.UnitZ));
        SnapshotPlayer morphing = state;
        morphing.Flags |= SnapshotPlayerFlags.Morphing;
        Assert.Equal(PlayerAnimation.None,
            resolver.Resolve(morphing, Vector3.UnitZ));
        Assert.Equal(PlayerAnimation.Idle,
            resolver.Resolve(state, new Vector3(0, 0, 0.015f)));
        Assert.Equal(PlayerAnimation.WalkBackward,
            resolver.Resolve(state, -Vector3.UnitZ));
    }

    public static TheoryData<SnapshotPlayer> ExcludedStates
    {
        get
        {
            SnapshotPlayer inactive = Player(Vector3.UnitZ);
            inactive.Flags &= ~SnapshotPlayerFlags.Active;
            SnapshotPlayer unspawned = Player(Vector3.UnitZ);
            unspawned.Flags &= ~SnapshotPlayerFlags.Spawned;
            SnapshotPlayer dead = Player(Vector3.UnitZ);
            dead.Health = 0;
            SnapshotPlayer airborne = Player(Vector3.UnitZ);
            airborne.Flags &= ~SnapshotPlayerFlags.Grounded;
            SnapshotPlayer invalidFacing = Player(Vector3.UnitZ, Vector3.UnitY);
            SnapshotPlayer nonFiniteFacing = Player(Vector3.UnitZ,
                new Vector3(float.NaN, 0, 1));
            SnapshotPlayer nonFiniteSpeed = Player(new Vector3(float.PositiveInfinity, 0, 0));
            return new TheoryData<SnapshotPlayer>
            {
                inactive,
                unspawned,
                dead,
                airborne,
                WithFlag(SnapshotPlayerFlags.AltForm),
                WithFlag(SnapshotPlayerFlags.Morphing),
                WithFlag(SnapshotPlayerFlags.Unmorphing),
                WithFlag(SnapshotPlayerFlags.Frozen),
                WithFlag(SnapshotPlayerFlags.Spectating),
                WithFlag(SnapshotPlayerFlags.WaitingForMatch),
                invalidFacing,
                nonFiniteFacing,
                nonFiniteSpeed
            };
        }
    }

    [Theory]
    [MemberData(nameof(ExcludedStates))]
    public void InvalidOrNonBipedSnapshotDoesNotOverrideAnimation(SnapshotPlayer state)
    {
        Assert.Equal(PlayerAnimation.None,
            PlayerEntity.DeriveSnapshotBipedAnimation(state, local: false));
    }

    [Fact]
    public void LocalSnapshotNeverOverridesAnimation()
    {
        SnapshotPlayer state = Player(Vector3.UnitZ);

        Assert.Equal(PlayerAnimation.None,
            PlayerEntity.DeriveSnapshotBipedAnimation(state, local: true));
    }

    [Theory]
    [InlineData(PlayerAnimation.Spawn)]
    [InlineData(PlayerAnimation.Morph)]
    [InlineData(PlayerAnimation.Unmorph)]
    [InlineData(PlayerAnimation.LandNeutral)]
    [InlineData(PlayerAnimation.DamageFront)]
    [InlineData(PlayerAnimation.Turn)]
    [InlineData(PlayerAnimation.Flourish)]
    public void ActiveNoLoopAnimationIsNotInterrupted(PlayerAnimation current)
    {
        Assert.False(PlayerEntity.CanApplySnapshotBipedAnimation(current, AnimFlags.NoLoop));
        Assert.True(PlayerEntity.CanApplySnapshotBipedAnimation(current,
            AnimFlags.NoLoop | AnimFlags.Ended));
    }

    [Theory]
    [InlineData(PlayerAnimation.Idle)]
    [InlineData(PlayerAnimation.WalkForward)]
    [InlineData(PlayerAnimation.WalkBackward)]
    [InlineData(PlayerAnimation.WalkLeft)]
    [InlineData(PlayerAnimation.WalkRight)]
    public void SnapshotLocomotionCanReplaceItsOwnState(PlayerAnimation current)
    {
        Assert.True(PlayerEntity.CanApplySnapshotBipedAnimation(current, AnimFlags.NoLoop));
    }

    [Fact]
    public void SnapshotWireContractIsUnchanged()
    {
        Assert.Equal(14, NetHeader.Version);
        Assert.Equal(96, SnapshotPlayer.Size);
        Assert.Equal(794, SnapshotPacket.MaxSize);
        Assert.Equal(818, SnapshotPacket.MaxSize + NetHeader.Size);
        Assert.Equal(834, SnapshotPacket.MaxSize + NetHeader.Size + NetAuthentication.TagSize);
    }

    private static SnapshotPlayer WithFlag(SnapshotPlayerFlags flag)
    {
        SnapshotPlayer state = Player(Vector3.UnitZ);
        state.Flags |= flag;
        return state;
    }
}
