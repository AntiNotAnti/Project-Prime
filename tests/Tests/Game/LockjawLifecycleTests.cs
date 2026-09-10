using System.Reflection;
using MphRead.Entities;
using Xunit;

namespace MphRead.Tests;

public sealed class LockjawLifecycleTests
{
    [Fact]
    public void LifeResetFencesStaleExpiryAndReusesFirstIndex()
    {
        (Scene scene, PlayerEntity owner) = CreateOwner();
        BombEntity stale = Bomb(scene, owner);
        Assert.True(owner.TryRegisterLockjawBomb(stale));

        owner.ResetLockjawBombState();
        BombEntity current = Bomb(scene, owner);
        Assert.True(owner.TryRegisterLockjawBomb(current));
        stale.Destroy();

        Assert.Equal(1, owner.SyluxBombCount);
        Assert.Same(current, owner.SyluxBombs[0]);
        Assert.Equal(0, current.BombIndex);
    }

    [Fact]
    public void HunterSwitchResetsRegistryAndStaleBombCannotChangeNewState()
    {
        (Scene scene, PlayerEntity owner) = CreateOwner();
        BombEntity stale = Bomb(scene, owner);
        Assert.True(owner.TryRegisterLockjawBomb(stale));

        owner.ModSetHunter(Hunter.Samus);
        owner.ModSetHunter(Hunter.Sylux);
        BombEntity current = Bomb(scene, owner);
        Assert.True(owner.TryRegisterLockjawBomb(current));
        stale.Destroy();

        Assert.Equal(1, owner.SyluxBombCount);
        Assert.Same(current, owner.SyluxBombs[0]);
    }

    [Fact]
    public void CorruptFullNullRegistryRepairsAndAcceptsNewBomb()
    {
        (Scene scene, PlayerEntity owner) = CreateOwner();
        owner.SyluxBombCount = 3;

        Assert.False(owner.ValidateLockjawBombState());
        Assert.Equal(0, owner.SyluxBombCount);
        Assert.True(owner.TryRegisterLockjawBomb(Bomb(scene, owner)));
    }

    [Fact]
    public void PartialRegistryCompactsInOriginalOrderAndRepairsIndices()
    {
        (Scene scene, PlayerEntity owner) = CreateOwner();
        BombEntity first = Bomb(scene, owner);
        BombEntity second = Bomb(scene, owner);
        Assert.True(owner.TryRegisterLockjawBomb(first));
        Assert.True(owner.TryRegisterLockjawBomb(second));
        owner.SyluxBombs[1] = null;
        owner.SyluxBombs[2] = second;
        owner.SyluxBombCount = 3;
        second.BombIndex = 2;

        Assert.False(owner.ValidateLockjawBombState());
        Assert.Equal(2, owner.SyluxBombCount);
        Assert.Same(first, owner.SyluxBombs[0]);
        Assert.Same(second, owner.SyluxBombs[1]);
        Assert.Null(owner.SyluxBombs[2]);
        Assert.Equal(1, second.BombIndex);
    }

    [Fact]
    public void DuplicateWrongOwnerWrongTypeAndBadIndicesRepairDeterministically()
    {
        var scene = new Scene();
        PlayerEntity owner = scene.Players[0];
        PlayerEntity other = scene.Players[1];
        BombEntity valid = Bomb(scene, owner);
        Assert.True(owner.TryRegisterLockjawBomb(valid));
        valid.BombIndex = 2;
        owner.SyluxBombs[1] = valid;
        owner.SyluxBombs[2] = Bomb(scene, other);
        owner.SyluxBombCount = 3;

        Assert.False(owner.ValidateLockjawBombState());
        Assert.Equal(1, owner.SyluxBombCount);
        Assert.Same(valid, owner.SyluxBombs[0]);
        Assert.Equal(0, valid.BombIndex);

        BombEntity wrongType = Bomb(scene, owner, BombType.MorphBall);
        owner.SyluxBombs[1] = wrongType;
        owner.SyluxBombCount = 2;
        Assert.False(owner.ValidateLockjawBombState());
        Assert.Equal(-1, wrongType.BombIndex);
        Assert.Equal(1, owner.SyluxBombCount);
    }

    [Fact]
    public void UnregisterAndDestroyAreIdempotentWithoutUnderflow()
    {
        (Scene scene, PlayerEntity owner) = CreateOwner();
        BombEntity bomb = Bomb(scene, owner);
        Assert.True(owner.TryRegisterLockjawBomb(bomb));

        owner.UnregisterLockjawBomb(bomb);
        owner.UnregisterLockjawBomb(bomb);
        bomb.Destroy();
        bomb.Destroy();

        Assert.Equal(0, owner.SyluxBombCount);
        Assert.All(owner.SyluxBombs, Assert.Null);
    }

    [Fact]
    public void RegistryAcceptsAtMostThreeUniqueBombs()
    {
        (Scene scene, PlayerEntity owner) = CreateOwner();
        BombEntity[] bombs = { Bomb(scene, owner), Bomb(scene, owner), Bomb(scene, owner), Bomb(scene, owner) };

        Assert.True(owner.TryRegisterLockjawBomb(bombs[0]));
        Assert.True(owner.TryRegisterLockjawBomb(bombs[1]));
        Assert.True(owner.TryRegisterLockjawBomb(bombs[2]));
        Assert.False(owner.TryRegisterLockjawBomb(bombs[2]));
        Assert.False(owner.TryRegisterLockjawBomb(bombs[3]));
        Assert.Equal(3, owner.SyluxBombCount);
    }

    private static (Scene Scene, PlayerEntity Owner) CreateOwner()
    {
        var scene = new Scene();
        PlayerEntity owner = scene.Players[0];
        owner.ModSetHunter(Hunter.Sylux);
        return (scene, owner);
    }

    private static BombEntity Bomb(Scene scene, PlayerEntity owner, BombType type = BombType.Lockjaw)
    {
        var bomb = new BombEntity(scene);
        typeof(BombEntity).GetProperty(nameof(BombEntity.Owner), BindingFlags.Instance | BindingFlags.Public)!
            .SetValue(bomb, owner);
        typeof(BombEntity).GetProperty(nameof(BombEntity.BombType), BindingFlags.Instance | BindingFlags.Public)!
            .SetValue(bomb, type);
        return bomb;
    }
}
