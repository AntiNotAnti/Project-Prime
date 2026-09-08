using System.Reflection;
using MphRead.Entities;
using OpenTK.Mathematics;
using Xunit;

namespace MphRead.Tests;

[Collection("Match baseline globals")]
public sealed class EntityStateIsolationTests
{
    [Fact]
    public void SameSlotTrailSurvivesOtherSceneConstructionAndPlayerReset()
    {
        using Scene first = new(headless: true);
        PlayerEntity player = first.Players[0];
        player._mbTrailMatrices[2] = Matrix4.CreateTranslation(1, 2, 3);
        player._mbTrailAlphas[2] = 0.75f;
        player._mbTrailIndex = 3;
        using Scene second = new(headless: true);
        second.Players[0]._mbTrailAlphas[2] = 0.25f;
        second.Players[0]._mbTrailMatrices[2] = Matrix4.Identity;
        second.Players.Reset();

        Assert.Equal(0.75f, player._mbTrailAlphas[2]);
        Assert.Equal(Matrix4.CreateTranslation(1, 2, 3), player._mbTrailMatrices[2]);
        Assert.Equal(3, player._mbTrailIndex);
        Assert.NotSame(player._mbTrailMatrices, second.Players[0]._mbTrailMatrices);
        Assert.NotSame(player._mbTrailAlphas, first.Players[1]._mbTrailAlphas);
    }

    [Fact]
    public void SecondPlatformPoolInitializationAndTeardownPreserveFirstProjectiles()
    {
        using Scene first = new(headless: true);
        BeamProjectileEntity[] original = first.PlatformBeams;
        BeamProjectileEntity projectile = original[0];
        using Scene second = new(headless: true);
        BeamProjectileEntity[] other = second.PlatformBeams;
        Assert.NotSame(original, other);
        Assert.Same(original, first.PlatformBeams);
        Assert.Same(projectile, first.PlatformBeams[0]);
        Assert.Same(first, projectile._scene);
        Assert.Same(second, other[0]._scene);

        second.ResetPlatformBeams();
        Assert.NotSame(other, second.PlatformBeams);
        Assert.Same(projectile, first.PlatformBeams[0]);
        Assert.Same(first, first.PlatformBeams[0]._scene);
    }

    [Fact]
    public void ItemRotationSequenceIsSceneLocalAndWraps()
    {
        using Scene first = new(headless: true);
        Assert.Equal(0f, first.NextItemRotation());
        using Scene second = new(headless: true);
        for (int i = 0; i < 16; i++)
            Assert.Equal((i % 8) * 45f, second.NextItemRotation());
        Assert.Equal(45f, first.NextItemRotation());
    }

    [Fact]
    public void RicochetEquipmentBelongsToEachProjectile()
    {
        using Scene first = new(headless: true);
        using Scene second = new(headless: true);
        FieldInfo field = typeof(BeamProjectileEntity).GetField("_ricochetEquip",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.NotNull(field);
        var equip = Assert.IsType<EquipInfo>(field.GetValue(first.PlatformBeams[0]));
        var other = Assert.IsType<EquipInfo>(field.GetValue(second.PlatformBeams[0]));
        equip.Beams = first.PlatformBeams;
        other.Beams = second.PlatformBeams;
        Assert.NotSame(equip, other);
        Assert.NotSame(equip, field.GetValue(first.PlatformBeams[1]));
        Assert.Same(first.PlatformBeams, equip.Beams);
    }
}
