using System;
using System.Collections.Immutable;
using MphRead.Combat;
using MphRead.Mods.Network;
using MphRead.Mods.Chat;
using OpenTK.Mathematics;
using Xunit;
namespace MphRead.Tests;
[Collection("Demo global state")]
public sealed class ObserverPresentationTests
{
    [Fact]
    public void SameMatchRoleRewindPurgesFutureCuesAndAcceptsHistoricalEvents()
    {
        var combat = new CombatFeedback(); var world = new WorldFeedback();
        var killer = new CombatActor(0,100,1); var victim = new CombatActor(1,200,1);
        combat.Bind(1,killer,ReadOnlySpan<NetRosterEntry>.Empty,600,3); world.Bind(1,3);
        var kill = new KillEvent(100,600,1,3,killer,victim,0,KillEventFlags.None,ImmutableArray<CombatActor>.Empty);
        Assert.True(combat.Process(kill));
        var objective = new WorldEvent(101,600,1,3,WorldSubjectKind.Match,WorldSignalKind.MatchPoint,0,0,CombatActor.None,Vector3.Zero);
        Assert.True(world.Process(objective,killer,600));
        // Ordinary life/local identity updates retain the shared kill feed.
        combat.Bind(1,killer with { Life=2 },ReadOnlySpan<NetRosterEntry>.Empty,600,3);
        Assert.Equal(1,combat.FeedCount);
        Assert.Equal(0u,CombatFeedback.Age(300,combat.FeedAt(0).Tick));
        ChatBox.Receive(new ChatPacket {Slot=0,Name="Player",Text="Live objective location",Kind=ChatPacket.KindSay});
        Assert.True(ChatBox.Visible);
        AuthoritativePlay.ResetRoleFeedback(combat,world);
        Assert.False(ChatBox.Visible);
        combat.Bind(1,CombatActor.None,ReadOnlySpan<NetRosterEntry>.Empty,300,3); world.Bind(1,3);
        Assert.Equal(0,combat.FeedCount); Assert.Empty(world.Message);
        Assert.True(combat.Process(kill with {Id=1,Tick=300}));
        Assert.True(world.Process(objective with {Id=2,Tick=300},CombatActor.None,300));
        ChatBox.Clear();
    }
}
