using System;
using System.IO;
using System.Reflection;
using MphRead.Entities;
using MphRead.Mods.Network;
using OpenTK.Mathematics;
using Xunit;

namespace MphRead.Tests;

[Collection("Match baseline globals")]
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

    [Trait("RequiresGameContent", "true")]
    [Fact]
    public void ActualDeathThenRespawnClearsRegisteredLockjawBombs()
    {
        using var content = OpenContent();
        using var simulation = new ServerSimulation(new MatchRules(
            MatchMode.Battle, "MP1 SANCTORUS", maxPlayers: 1));
        Scene scene = simulation.Scene;
        scene.Match.Phase = MatchPhase.Playing;
        PlayerEntity owner = scene.Players[0];
        owner.ServerActivate(0x601, Hunter.Sylux, team: 0);
        scene.Players.ActiveCount = 1;

        BombEntity bomb = BombEntity.Spawn(owner,
            Matrix4.CreateTranslation(owner.Position), scene)!;
        Assert.True(owner.TryRegisterLockjawBomb(bomb));
        Assert.Equal(1, owner.SyluxBombCount);

        owner.TakeDamage((uint)owner.HealthMax,
            DamageFlags.Death | DamageFlags.NoDmgInvuln, null, null);
        Assert.Equal(0, owner.Health);
        Assert.Equal(1, owner.SyluxBombCount);

        // Drive the production PlayerProcess caller: a held fire input is the
        // normal death-screen respawn edge in a non-replica match. This keeps
        // the assertion separate from the direct registry reset tests above:
        // a caller can preserve the generation fence while still forgetting
        // to invoke it on the real respawn path.
        owner.RespawnTimer = 1;
        owner.Controls.Shoot.IsDown = true;
        scene.StepHeadlessFrame(advanceMatch: false);

        Assert.True(owner.Health > 0);
        Assert.Equal(0, owner.SyluxBombCount);
        Assert.All(owner.SyluxBombs, Assert.Null);
        Assert.Equal(-1, bomb.BombIndex);
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

    private static IDisposable OpenContent()
    {
        string? configured = Environment.GetEnvironmentVariable("GAME_DATA_DIRECTORY");
        string[] starts = configured is null
            ? new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory }
            : new[] { configured, Directory.GetCurrentDirectory(), AppContext.BaseDirectory };
        foreach (string start in starts)
        {
            DirectoryInfo? directory = new(Path.GetFullPath(start));
            while (directory != null)
            {
                string candidate = Path.Combine(directory.FullName, "AMHE1");
                if (File.Exists(Path.Combine(candidate, "_bin", "arm9.bin"))
                    && Directory.Exists(Path.Combine(candidate, "models"))
                    && Directory.Exists(Path.Combine(candidate, "levels")))
                {
                    IDisposable context = ServerContent.PreserveContext("AMHE1");
                    try
                    {
                        ServerContent.Open(candidate, "AMHE1");
                        return context;
                    }
                    catch
                    {
                        context.Dispose();
                        throw;
                    }
                }
                directory = directory.Parent;
            }
        }
        throw new DirectoryNotFoundException("AMHE1 extracted content was not found.");
    }
}
