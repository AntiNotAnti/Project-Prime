using MphRead.Entities;
using MphRead.Formats;
using MphRead.Mods.Network;
using OpenTK.Mathematics;
using Xunit;

namespace MphRead.Tests;

public sealed class AltFormTransitionTests
{
    [Theory]
    [InlineData(Hunter.Samus)]
    [InlineData(Hunter.Kanden)]
    [InlineData(Hunter.Trace)]
    [InlineData(Hunter.Sylux)]
    [InlineData(Hunter.Noxus)]
    [InlineData(Hunter.Spire)]
    [InlineData(Hunter.Weavel)]
    public void GroundedFormTransitionsPreserveWorldBottom(Hunter hunter)
    {
        Vector3 origin = new(12.5f, 3.25f, -8.75f);
        CollisionVolume biped = PlayerEntity.PlayerVolumes[(int)hunter, 0];
        CollisionVolume alt = PlayerEntity.PlayerVolumes[(int)hunter, 2];

        AssertGroundedTransition(origin, biped, alt);
        AssertGroundedTransition(origin, alt, biped);
    }

    [Theory]
    [InlineData(Hunter.Samus)]
    [InlineData(Hunter.Kanden)]
    [InlineData(Hunter.Trace)]
    [InlineData(Hunter.Sylux)]
    [InlineData(Hunter.Noxus)]
    [InlineData(Hunter.Spire)]
    [InlineData(Hunter.Weavel)]
    public void AirborneFormTransitionsPreserveSphereCenter(Hunter hunter)
    {
        Vector3 origin = new(-3.5f, 11.25f, 5.75f);
        CollisionVolume biped = PlayerEntity.PlayerVolumes[(int)hunter, 0];
        CollisionVolume alt = PlayerEntity.PlayerVolumes[(int)hunter, 2];

        AssertAirborneTransition(origin, biped, alt);
        AssertAirborneTransition(origin, alt, biped);
    }

    [Fact]
    public void GuardianIsTheCataloguedBipedOnlyBoundary()
    {
        Assert.False(PlayerEntity.SupportsAltForm(Hunter.Guardian));
        Assert.Equal(Hunter.Guardian,
            Metadata.PlayerValues[(int)Hunter.Guardian].Hunter);
        for (Hunter hunter = Hunter.Samus; hunter <= Hunter.Weavel; hunter++)
        {
            Assert.True(PlayerEntity.SupportsAltForm(hunter));
        }
    }

    [Fact]
    public void OnlyFloorContactPreservesBottomDuringFormSwitch()
    {
        Assert.True(PlayerEntity.ShouldPreserveFormBottom(
            PlayerFlags1.Standing));
        Assert.True(PlayerEntity.ShouldPreserveFormBottom(
            PlayerFlags1.StandingPrevious));
        Assert.False(PlayerEntity.ShouldPreserveFormBottom(
            PlayerFlags1.Grounded));
        Assert.False(PlayerEntity.ShouldPreserveFormBottom(PlayerFlags1.None));
    }

    [Fact]
    public void SnapshotFormChangeOnlyTracksFinalAltForm()
    {
        Assert.False(PlayerEntity.HasSnapshotFormChanged(false,
            SnapshotPlayerFlags.Morphing));
        Assert.False(PlayerEntity.HasSnapshotFormChanged(false,
            SnapshotPlayerFlags.Unmorphing));
        Assert.False(PlayerEntity.HasSnapshotFormChanged(true,
            SnapshotPlayerFlags.AltForm | SnapshotPlayerFlags.Morphing));
        Assert.True(PlayerEntity.HasSnapshotFormChanged(false,
            SnapshotPlayerFlags.AltForm | SnapshotPlayerFlags.Morphing));
        Assert.True(PlayerEntity.HasSnapshotFormChanged(true,
            SnapshotPlayerFlags.Unmorphing));
    }

    [Fact]
    public void ReplicatedAltAttackIsGenericAcrossOfficialHunters()
    {
        for (Hunter hunter = Hunter.Samus; hunter <= Hunter.Weavel; hunter++)
        {
            bool supported = hunter is Hunter.Trace or Hunter.Spire or Hunter.Weavel;
            Assert.Equal(supported, PlayerEntity.SupportsReplicatedAltAttack(hunter));
            Assert.Equal(supported, PlayerEntity.ShouldCaptureReplicatedAltAttack(
                hunter, alive: true, altForm: true, altAttack: true));
            Assert.Equal(supported, PlayerEntity.ShouldApplyReplicatedAltAttack(
                spawned: true, health: 1, hunter: hunter,
                flags: SnapshotPlayerFlags.AltForm | SnapshotPlayerFlags.SpireAltAttack));
            Assert.Equal(supported,
                PlayerEntity.ShouldReconcileReplicatedAltAttack(hunter,
                    predicted: false, newLife: false));
        }

        Assert.False(PlayerEntity.ShouldCaptureReplicatedAltAttack(
            Hunter.Guardian, alive: true, altForm: true, altAttack: true));
        Assert.False(PlayerEntity.ShouldApplyReplicatedAltAttack(
            spawned: true, health: 1, hunter: Hunter.Guardian,
            flags: SnapshotPlayerFlags.AltForm | SnapshotPlayerFlags.SpireAltAttack));
        Assert.False(PlayerEntity.ShouldCaptureReplicatedAltAttack(
            Hunter.Samus, alive: true, altForm: false, altAttack: true));
        Assert.False(PlayerEntity.ShouldApplyReplicatedAltAttack(
            spawned: true, health: 0, hunter: Hunter.Samus,
            flags: SnapshotPlayerFlags.AltForm | SnapshotPlayerFlags.SpireAltAttack));
        Assert.False(PlayerEntity.ShouldReconcileReplicatedAltAttack(
            Hunter.Noxus, predicted: false, newLife: false));
    }

    [Fact]
    public void SnapshotFormSwitchDecisionStartsOnlyTransitionEdges()
    {
        Assert.True(PlayerEntity.ShouldStartSnapshotFormSwitch(false, false,
            false, SnapshotPlayerFlags.Morphing));
        Assert.False(PlayerEntity.ShouldStartSnapshotFormSwitch(false, true,
            false, SnapshotPlayerFlags.Morphing));
        Assert.True(PlayerEntity.ShouldStartSnapshotFormSwitch(true, false,
            false, SnapshotPlayerFlags.Unmorphing));
        Assert.False(PlayerEntity.ShouldStartSnapshotFormSwitch(true, false,
            true, SnapshotPlayerFlags.Unmorphing));
        Assert.False(PlayerEntity.ShouldStartSnapshotFormSwitch(false, false,
            false, SnapshotPlayerFlags.None));

        Assert.False(PlayerEntity.ShouldForceSnapshotForm(false,
            SnapshotPlayerFlags.Morphing));
        Assert.True(PlayerEntity.ShouldForceSnapshotForm(false,
            SnapshotPlayerFlags.AltForm));
        Assert.True(PlayerEntity.ShouldForceSnapshotForm(true,
            SnapshotPlayerFlags.None));
    }

    [Fact]
    public void GenericAltAttackBitRoundTripsWithoutChangingSnapshotLayout()
    {
        SnapshotPlayer source = new()
        {
            Slot = 0,
            Hunter = Hunter.Trace,
            TeamIndex = 0,
            Weapon = 0,
            Flags = SnapshotPlayerFlags.Active | SnapshotPlayerFlags.Spawned
                | SnapshotPlayerFlags.AltForm | SnapshotPlayerFlags.SpireAltAttack,
            Health = 100,
            Life = 1,
            ConnectionId = 1,
            Position = Vector3.Zero,
            Speed = Vector3.Zero,
            Aim = -Vector3.UnitZ,
            Facing = -Vector3.UnitZ,
            AvailableWeapons = 1
        };
        byte[] bytes = new byte[SnapshotPlayer.Size];

        source.Write(bytes);

        Assert.True(SnapshotPlayer.TryRead(bytes, out SnapshotPlayer parsed));
        Assert.Equal(source, parsed);
        Assert.True(PlayerEntity.ShouldApplyReplicatedAltAttack(
            spawned: true, health: parsed.Health, hunter: parsed.Hunter,
            flags: parsed.Flags));
    }

    private static void AssertGroundedTransition(Vector3 origin,
        CollisionVolume previous, CollisionVolume next)
    {
        Vector3 result = PlayerEntity.ResolveFormOrigin(origin, previous, next,
            grounded: true);
        Vector3 oldCenter = origin + previous.SpherePosition;
        Vector3 newCenter = result + next.SpherePosition;

        Assert.Equal(oldCenter.X, newCenter.X, precision: 5);
        Assert.Equal(oldCenter.Z, newCenter.Z, precision: 5);
        Assert.Equal(oldCenter.Y - previous.SphereRadius,
            newCenter.Y - next.SphereRadius, precision: 5);
    }

    private static void AssertAirborneTransition(Vector3 origin,
        CollisionVolume previous, CollisionVolume next)
    {
        Vector3 result = PlayerEntity.ResolveFormOrigin(origin, previous, next,
            grounded: false);
        Vector3 oldCenter = origin + previous.SpherePosition;
        Vector3 newCenter = result + next.SpherePosition;

        Assert.Equal(oldCenter.X, newCenter.X, precision: 5);
        Assert.Equal(oldCenter.Y, newCenter.Y, precision: 5);
        Assert.Equal(oldCenter.Z, newCenter.Z, precision: 5);
    }
}
