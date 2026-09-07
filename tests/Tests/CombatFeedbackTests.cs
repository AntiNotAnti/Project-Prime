using System;
using System.Collections.Immutable;
using MphRead.Combat;
using MphRead.Mods.Network;
using OpenTK.Mathematics;
using Xunit;

namespace MphRead.Tests;

[Collection("Match baseline globals")]
public class CombatFeedbackTests : IDisposable
{
    private readonly HitMarkerMode _markers = CombatFeedbackSettings.HitMarkers;
    private readonly bool _headshot = CombatFeedbackSettings.HeadshotCue, _kill = CombatFeedbackSettings.KillConfirmation;
    public void Dispose()
    {
        CombatFeedbackSettings.HitMarkers = _markers;
        CombatFeedbackSettings.HeadshotCue = _headshot;
        CombatFeedbackSettings.KillConfirmation = _kill;
    }
    private static readonly CombatActor Local = new(0, 100, 1), Enemy = new(1, 200, 1);
    private static readonly NetRosterEntry[] Roster = { new(0, 100, Hunter.Samus, 0, "Local"), new(1, 200, Hunter.Samus, 1, "Enemy") };
    private static CombatFeedback New()
    {
        CombatFeedbackSettings.HitMarkers = HitMarkerMode.Visual;
        CombatFeedbackSettings.HeadshotCue = CombatFeedbackSettings.KillConfirmation = true;
        var feedback = new CombatFeedback();
        feedback.Bind(1, Local, Roster);
        return feedback;
    }
    private static CombatEvent Damage(uint id, CombatActor source, CombatActor target, ushort amount = 10, ushort health = 90,
        CombatEventFlags flags = 0) => new(id, 100, 1, CombatEventKind.Damage, 0, flags, source, target, health, amount,
            Vector3.Zero, Vector3.UnitZ, 0, 0, 0);
    private static KillEvent Kill(uint id, CombatActor source, CombatActor target, uint match = 1, KillEventFlags flags = 0)
        => new(id, 100, match, 1, source, target, 0, flags, ImmutableArray<CombatActor>.Empty);

    [Fact]
    public void MarkerRequiresPositiveNonSilentAuthoritativeDamage()
    {
        var f = New();
        f.Process(Damage(1, Local, Enemy, amount: 0));
        Assert.Equal(HitMarkerKind.None, f.VisibleMarker(100));
        f.Process(Damage(2, Local, Enemy, flags: CombatEventFlags.Silent));
        Assert.Equal(HitMarkerKind.None, f.VisibleMarker(100));
        f.Process(Damage(3, Local, Enemy, flags: CombatEventFlags.Headshot));
        Assert.Equal(HitMarkerKind.Headshot, f.VisibleMarker(100));
        Assert.Equal(HitMarkerKind.None, f.VisibleMarker(112));
    }

    [Fact]
    public void ReorderedKillUsesSharedIdWindowWithoutDuplicateConfirmation()
    {
        var f = New();
        Assert.True(f.Process(Damage(2, Local, Enemy)));
        Assert.True(f.Process(Kill(1, Local, Enemy)));
        uint sequence = f.State.MarkerSequence;
        Assert.False(f.Process(Kill(1, Local, Enemy)));
        Assert.False(f.Process(Damage(2, Local, Enemy)));
        Assert.Equal(sequence, f.State.MarkerSequence);
        Assert.Equal(1, f.FeedCount);
        Assert.Equal(HitMarkerKind.Kill, f.VisibleMarker(100));
    }

    [Fact]
    public void NewLifeAndReusedSlotCannotReceiveOldAttribution()
    {
        var f = New();
        f.Process(Damage(1, Enemy, Local));
        Assert.Equal(1, f.History.Count);
        CombatActor next = Local with { Life = 2 };
        f.Bind(1, next, Roster);
        f.Process(Damage(2, Local, Enemy));
        f.Process(Damage(3, Enemy, Local));
        Assert.Equal(0, f.History.Count);
        Assert.Equal(HitMarkerKind.None, f.VisibleMarker(100));
        f.Bind(1, next with { ConnectionId = 900 }, Roster);
        f.Process(Kill(4, next, Enemy));
        Assert.Equal(HitMarkerKind.None, f.VisibleMarker(100));
    }

    [Fact]
    public void HistoryFeedAndOldDedupWindowAreBounded()
    {
        var f = New();
        for (uint i = 1; i <= 600; i++) f.Process(Damage(i, Enemy, Local));
        Assert.Equal(DamageHistory.Capacity, f.History.Count);
        Assert.False(f.Process(Damage(1, Enemy, Local)));
        for (uint i = 601; i <= 620; i++) f.Process(Kill(i, Enemy, Local));
        Assert.Equal(CombatFeedback.FeedCapacity, f.FeedCount);
        Assert.False(f.Process(Kill(621, Enemy, Local, match: 2)));
        Assert.True(f.State.Dead);
        f.Bind(2, Local, Roster);
        Assert.Equal(0, f.FeedCount);
        Assert.Equal(0, f.History.Count);
        Assert.False(f.State.Dead);
    }

    [Fact]
    public void FeedNamesRemainBoundToConnectionAfterSlotReuse()
    {
        var f = New();
        f.Bind(1, Local, new NetRosterEntry[] { new(1, 999, Hunter.Samus, 1, "Replacement") });
        f.Process(Kill(1, Enemy, Local));
        Assert.Contains("Enemy", f.FeedAt(0).Text);
        Assert.DoesNotContain("Replacement", f.FeedAt(0).Text);
    }

    [Fact]
    public void DelayedConfirmationGetsReceiptLifetimeAndKillQueueIsIndependent()
    {
        var f = New();
        f.Bind(1, Local, Roster, presentationTick: 1000);
        for (uint id = 2; id < 650; id++) f.Process(Damage(id, Local, Enemy));
        Assert.Equal(HitMarkerKind.Hit, f.VisibleMarker(1000));
        Assert.True(f.Process(Kill(1, Local, Enemy)));
        Assert.Equal(HitMarkerKind.Kill, f.VisibleMarker(1000));
        Assert.Equal(HitMarkerKind.None, f.VisibleMarker(1018));
        Assert.Equal(1000u, f.FeedAt(0).Tick);
    }

    [Fact]
    public void CueSettingsDoNotRemoveAuthoritativeHistory()
    {
        var f = New();
        CombatFeedbackSettings.HeadshotCue = false;
        f.Process(Damage(1, Local, Enemy, flags: CombatEventFlags.Headshot));
        Assert.Equal(HitMarkerKind.Hit, f.VisibleMarker(100));
        CombatFeedbackSettings.HitMarkers = HitMarkerMode.Off;
        Assert.Equal(HitMarkerKind.None, f.VisibleMarker(100));
        f.Process(Damage(2, Enemy, Local));
        Assert.Equal(1, f.History.Count);
        CombatFeedbackSettings.HitMarkers = HitMarkerMode.Visual;
        CombatFeedbackSettings.KillConfirmation = false;
        f.Process(Kill(3, Local, Enemy));
        Assert.Equal(HitMarkerKind.Hit, f.VisibleMarker(100));
    }

    [Theory]
    [InlineData(0, 0)] [InlineData(45, 1)] [InlineData(90, 2)] [InlineData(135, 3)]
    [InlineData(180, 4)] [InlineData(225, 5)] [InlineData(270, 6)] [InlineData(315, 7)]
    [InlineData(67.49f, 1)] [InlineData(67.51f, 2)] [InlineData(112.49f, 2)] [InlineData(112.51f, 3)]
    [InlineData(157.49f, 3)] [InlineData(157.51f, 4)] [InlineData(202.49f, 4)] [InlineData(202.51f, 5)]
    [InlineData(247.49f, 5)] [InlineData(247.51f, 6)] [InlineData(292.49f, 6)] [InlineData(292.51f, 7)]
    [InlineData(22.49f, 0)] [InlineData(22.51f, 1)] [InlineData(337.49f, 7)] [InlineData(337.51f, 0)]
    public void DamageUsesEightHorizontalSectors(float degrees, int expected)
    {
        float angle = MathHelper.DegreesToRadians(degrees);
        Vector3 direction = new(MathF.Sin(angle), 9, MathF.Cos(angle));
        Assert.Equal(expected, CombatFeedback.DamageSector(direction, Vector3.UnitZ, Vector3.UnitX));
        Assert.Equal(expected, CombatFeedback.DamageSector(direction, new Vector3(0, .99f, .1f), Vector3.UnitX));
    }

    [Fact]
    public void UnknownDirectionHasNoInventedAttackerLookup()
    {
        Assert.Equal(-1, CombatFeedback.DamageSector(Vector3.Zero, Vector3.UnitZ, Vector3.UnitX));
        Assert.Equal(-1, CombatFeedback.DamageSector(new(float.NaN, 0, 1), Vector3.UnitZ, Vector3.UnitX));
    }
}
