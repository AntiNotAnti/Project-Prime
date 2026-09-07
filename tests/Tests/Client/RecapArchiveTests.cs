using System.Collections.Immutable;
using MphRead.Combat;
using MphRead.Mods.Network;
using OpenTK.Mathematics;
using Xunit;
namespace MphRead.Tests.Client;
public class RecapArchiveTests
{
    private static readonly CombatActor Enemy = new(1, 200, 1);
    private static readonly NetRosterEntry[] Roster = { new(0, 100, Hunter.Samus, 0, "Local"), new(1, 200, Hunter.Samus, 1, "Original") };
    private static CombatEvent Damage(uint id, CombatActor local, ushort health = 0) => new(id, id, 1, CombatEventKind.Damage,
        0, 0, Enemy, local, health, 20, Vector3.Zero, Vector3.UnitZ, 0, 0, 0);
    [Fact]
    public void CompletedLifeSurvivesRespawnAndLateKillUsesOldIdentityAndSource()
    {
        var f = new CombatFeedback(); var first = new CombatActor(0, 100, 1);
        f.Bind(1, first, Roster);
        f.Process(Damage(1, first));
        f.Bind(1, first with { Life = 2 }, Roster);
        Assert.Equal(0, f.History.Count);
        Assert.Equal(1, f.Recaps.Count);
        f.Process(new KillEvent(2, 1, 1, 1, Enemy, first, 255, 0, ImmutableArray<CombatActor>.Empty, KillSourceKind.Bomb));
        Assert.Equal("Bomb  20 damage", f.Recaps[0].Final);
        Assert.Contains("Original", f.Recaps[0].Heading);
        Assert.False(f.State.Dead);
        Assert.Equal(first, f.Recaps[0].Life);
    }
    [Fact]
    public void DamageBeforeDelayedDeathIsRetainedWithoutInventingADeath()
    {
        var f = new CombatFeedback(); var first = new CombatActor(0, 100, 1);
        f.Bind(1, first, Roster);
        f.Process(Damage(1, first, 80));
        f.Bind(1, first with { Life = 2 }, Roster);
        Assert.Equal("Previous life damage", f.Recaps[0].Heading);
        f.Process(Damage(2, first));
        Assert.Equal(2, f.Recaps[0].Count);
        Assert.Equal("Eliminated by Original", f.Recaps[0].Heading);
        Assert.False(f.Process(Damage(2, first)));
        Assert.Equal(2, f.Recaps[0].Count);
    }
    [Fact]
    public void ArchiveBoundsOldLivesAndClearsOnSessionOrMatchChange()
    {
        var f = new CombatFeedback();
        for (uint life = 1; life <= 30; life++)
        {
            var actor = new CombatActor(0, 100, life);
            f.Bind(1, actor, Roster); f.Process(Damage(life, actor));
        }
        Assert.Equal(RecapArchive.Capacity, f.Recaps.Count);
        Assert.Equal(15u, f.Recaps[0].Life.Life);
        f.Bind(1, new CombatActor(0, 300, 30), Roster);
        Assert.Equal(0, f.Recaps.Count);
        f.Process(Damage(31, f.Local));
        f.Bind(2, f.Local, Roster);
        Assert.Equal(0, f.Recaps.Count);
    }
}
