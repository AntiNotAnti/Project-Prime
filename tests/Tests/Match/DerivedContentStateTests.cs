using System;
using MphRead.Entities;
using Xunit;

namespace MphRead.Tests;

[Collection("Match baseline globals")]
public sealed class DerivedContentStateTests
{
    [Fact]
    public void MultiplayerWeaponSelectorCannotBeReassigned()
    {
        Assert.Same(Weapons.WeaponsMP, Weapons.Current);
        Assert.Null(typeof(Weapons).GetProperty(nameof(Weapons.Current))!.SetMethod);
    }

    [Fact]
    public void CollisionVolumesAreReadOnlyCopiesAndWarmupPreservesIdentity()
    {
        var table = PlayerEntity.PlayerVolumes;
        var before = table[(int)Hunter.Samus, 0];
        var copy = before;
        copy = default;
        PlayerEntity.GeneratePlayerVolumes();
        Assert.Same(table, PlayerEntity.PlayerVolumes);
        Assert.Equal(before, table[(int)Hunter.Samus, 0]);
        Assert.NotEqual(copy, table[(int)Hunter.Samus, 0]);
        Assert.Null(typeof(PlayerEntity.PlayerCollisionVolumes).GetProperty("Item")!.SetMethod);
    }

    [Fact]
    public void RuntimeRomDescriptorsHaveNoMutableProperties()
    {
        var descriptor = RuntimeData.GetFontModel(Ver.AMHE0)!;
        Assert.NotNull(descriptor);
        foreach (var property in typeof(RuntimeData.RomDataValues).GetProperties())
            Assert.Null(property.SetMethod);
        Assert.Same(descriptor, RuntimeData.GetFontModel(Ver.AMHE0));
    }
}
