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
    private readonly HitMarkerTiming _timing = CombatFeedbackSettings.Timing;
    private readonly bool _headshot = CombatFeedbackSettings.HeadshotCue, _kill = CombatFeedbackSettings.KillConfirmation;
    private readonly float _scale = CombatFeedbackSettings.MarkerScale;
    private readonly float _opacity = CombatFeedbackSettings.MarkerOpacity;
    private readonly float _animation = CombatFeedbackSettings.MarkerAnimation;
    private readonly HitMarkerPalette _palette = CombatFeedbackSettings.Palette;
    public void Dispose()
    {
        CombatFeedbackSettings.HitMarkers = _markers;
        CombatFeedbackSettings.Timing = _timing;
        CombatFeedbackSettings.HeadshotCue = _headshot;
        CombatFeedbackSettings.KillConfirmation = _kill;
        CombatFeedbackSettings.MarkerScale = _scale;
        CombatFeedbackSettings.MarkerOpacity = _opacity;
        CombatFeedbackSettings.MarkerAnimation = _animation;
        CombatFeedbackSettings.Palette = _palette;
    }
    private static readonly CombatActor Local = new(0, 100, 1), Enemy = new(1, 200, 1);
    private static readonly NetRosterEntry[] Roster = { new(0, 100, Hunter.Samus, 0, "Local"), new(1, 200, Hunter.Samus, 1, "Enemy") };
    private static CombatFeedback New()
    {
        CombatFeedbackSettings.HitMarkers = HitMarkerMode.Visual;
        CombatFeedbackSettings.Timing = HitMarkerTiming.Confirmed;
        CombatFeedbackSettings.HeadshotCue = CombatFeedbackSettings.KillConfirmation = true;
        var feedback = new CombatFeedback();
        feedback.Bind(1, Local, Roster);
        return feedback;
    }

    [Fact]
    public void PredictedMarkerPromotesWithoutSpeculativeAudio()
    {
        var f = New();
        CombatFeedbackSettings.Timing = HitMarkerTiming.Instant;
        f.PresentPredictedHit(Local, Enemy, 100);
        Assert.Equal(HitMarkerKind.Predicted, f.VisibleMarker(100));
        Assert.Equal(1u, f.State.MarkerSequence);
        Assert.Equal(0u, f.State.MarkerAudioSequence);
        Assert.Equal(HitMarkerKind.None, f.VisibleMarker(106));

        f.PresentPredictedHit(Local, Enemy, 110);
        Assert.True(f.Process(Damage(1, Local, Enemy)));
        Assert.Equal(HitMarkerKind.Hit, f.VisibleMarker(110));
        Assert.Equal(3u, f.State.MarkerSequence);
        Assert.Equal(1u, f.State.MarkerAudioSequence);
    }

    [Fact]
    public void MarkerPriorityAndDurationsPreserveStrongAuthoritativeCue()
    {
        var f = New();
        Assert.True(f.Process(Damage(1, Local, Enemy, flags: CombatEventFlags.Headshot)));
        Assert.Equal(HitMarkerKind.Headshot, f.VisibleMarker(115));
        uint sequence = f.State.MarkerSequence;
        Assert.True(f.Process(Damage(2, Local, Enemy)));
        Assert.Equal(sequence, f.State.MarkerSequence);
        Assert.True(f.Process(Kill(3, Local, Enemy)));
        Assert.Equal(HitMarkerKind.Kill, f.VisibleMarker(117));
        sequence = f.State.MarkerSequence;
        Assert.True(f.Process(Damage(4, Local, Enemy)));
        Assert.Equal(sequence, f.State.MarkerSequence);
        Assert.Equal(HitMarkerKind.Hit, f.State.MarkerAudioKind);
        Assert.Equal(4u, f.State.MarkerPulseSequence);
        Assert.Equal(HitMarkerKind.None, f.VisibleMarker(118));
    }

    [Fact]
    public void RapidHitsPulseIndependentlyAndCarryAuthoritativeVisualDetail()
    {
        var f = New();
        Assert.True(f.Process(Damage(10, Local, Enemy, amount: 12, health: 88,
            flags: CombatEventFlags.Charged)));
        Assert.Equal((ushort)12, f.State.MarkerDamage);
        Assert.Equal((ushort)88, f.State.MarkerHealth);
        Assert.Equal(CombatEventFlags.Charged, f.State.MarkerFlags);
        Assert.Equal((byte)1, f.State.MarkerBurst);

        Assert.True(f.Process(Damage(11, Local, Enemy, amount: 30, health: 58)));
        Assert.Equal((byte)2, f.State.MarkerBurst);
        Assert.Equal(2u, f.State.MarkerPulseSequence);
        Assert.Equal(2u, f.State.MarkerAudioSequence);
        Assert.NotEqual(0u, f.State.MarkerHapticIdentity);
    }

    [Fact]
    public void MarkerPresentationSettingsClampInvalidValues()
    {
        CombatFeedbackSettings.MarkerScale = -10;
        CombatFeedbackSettings.MarkerOpacity = 10;
        CombatFeedbackSettings.MarkerAnimation = float.NaN;
        Assert.Equal(.5f, CombatFeedbackSettings.MarkerScale);
        Assert.Equal(1f, CombatFeedbackSettings.MarkerOpacity);
        Assert.Equal(1f, CombatFeedbackSettings.MarkerAnimation);
    }
    private static CombatEvent Damage(uint id, CombatActor source, CombatActor target, ushort amount = 10, ushort health = 90,
        CombatEventFlags flags = 0) => new(id, 100, 1, CombatEventKind.Damage, 0, flags, source, target, health, amount,
            Vector3.Zero, Vector3.UnitZ, 0, 0, 0);
    private static KillEvent Kill(uint id, CombatActor source, CombatActor target, uint match = 1,
        KillEventFlags flags = 0, uint phase = 1)
        => new(id, 100, match, phase, source, target, 0, flags, ImmutableArray<CombatActor>.Empty);

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
        Assert.Equal(HitMarkerKind.None, f.VisibleMarker(116));
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

    [Fact]
    public void NoticesAreLocalIndependentReceiptBoundAndSettingGated()
    {
        var f = New();
        Assert.True(f.Process(Damage(1, Local, Enemy, flags: CombatEventFlags.Headshot)));
        Assert.Equal("HEADSHOT!", f.State.HeadshotNotice.Text);
        Assert.True(f.IsHeadshotNoticeVisible(139));
        Assert.False(f.IsHeadshotNoticeVisible(140));

        Assert.True(f.Process(Kill(2, Local, Enemy, flags: KillEventFlags.Headshot)));
        Assert.Equal("YOUR HEADSHOT KILLED Enemy!", f.State.KillNotice.Text);
        Assert.True(f.IsKillNoticeVisible(219));
        Assert.False(f.IsKillNoticeVisible(220));

        // A remote headshot/kill never becomes a local center-screen notice,
        // and a retransmit cannot refresh either independent receipt window.
        Assert.True(f.Process(Damage(3, Enemy, Local, flags: CombatEventFlags.Headshot)));
        Assert.False(f.Process(Damage(1, Local, Enemy, flags: CombatEventFlags.Headshot)));
        Assert.False(f.Process(Kill(2, Local, Enemy, flags: KillEventFlags.Headshot)));
        Assert.Equal(100u, f.State.HeadshotNotice.Tick);
        Assert.Equal(100u, f.State.KillNotice.Tick);

        CombatFeedbackSettings.HeadshotCue = false;
        CombatFeedbackSettings.KillConfirmation = false;
        Assert.False(f.IsHeadshotNoticeVisible(100));
        Assert.False(f.IsKillNoticeVisible(100));
    }

    [Fact]
    public void NoticesResetOnLifeAndPhaseTransitionsAndLethalHeadshotKeepsBoth()
    {
        var f = New();
        Assert.True(f.Process(Damage(1, Local, Enemy, health: 0,
            flags: CombatEventFlags.Headshot)));
        Assert.True(f.Process(Kill(2, Local, Enemy, flags: KillEventFlags.Headshot)));
        Assert.True(f.State.HeadshotNotice.IsValid);
        Assert.True(f.State.KillNotice.IsValid);

        f.Bind(1, Local, Roster, presentationTick: 100, phaseRevision: 2);
        Assert.False(f.State.HeadshotNotice.IsValid);
        Assert.False(f.State.KillNotice.IsValid);
        Assert.True(f.Process(Damage(3, Local, Enemy, flags: CombatEventFlags.Headshot)));
        f.Bind(1, Local with { Life = 2 }, Roster, presentationTick: 100, phaseRevision: 2);
        Assert.False(f.State.HeadshotNotice.IsValid);
        Assert.False(f.State.KillNotice.IsValid);
    }

    [Fact]
    public void ReplayTargetFenceCanSuppressLocalNoticeWithoutRejectingTheFact()
    {
        var f = New();
        Assert.True(f.Process(Damage(1, Local, Enemy, flags: CombatEventFlags.Headshot),
            allowLocalHitMarker: false));
        Assert.Equal(HitMarkerKind.None, f.VisibleMarker(100));
        Assert.False(f.State.HeadshotNotice.IsValid);
    }

    [Fact]
    public void LateKillFromPriorPhaseCannotRepopulateFeedback()
    {
        var f = New();
        f.Bind(1, Local, Roster, phaseRevision: 2);
        Assert.False(f.Process(Kill(1, Local, Enemy, phase: 1)));
        Assert.Equal(0, f.FeedCount);
        Assert.False(f.State.KillNotice.IsValid);
        Assert.True(f.Process(Kill(2, Local, Enemy, phase: 2)));
        Assert.True(f.State.KillNotice.IsValid);
    }

    [Fact]
    public void DeathPresentationGateOrdersAndDeduplicatesSnapshotAndKillFacts()
    {
        CombatActor actor = new(1, 200, 1);
        var gate = new DeathPresentationGate();

        // Joining after the death does not invent a cue. A matching reliable
        // kill event can confirm that first dead observation later.
        Assert.False(gate.ObserveSnapshot(actor, dead: true, altForm: false, tick: 10).IsValid);
        DeathPresentationCue cue = gate.ObserveKill(actor, 11);
        Assert.True(cue.IsValid);
        Assert.Equal(11u, cue.Tick);
        Assert.False(gate.ObserveKill(actor, 12).IsValid);
        Assert.False(gate.ObserveSnapshot(actor, dead: true, altForm: false, tick: 13).IsValid);

        gate.Reset();
        Assert.False(gate.ObserveSnapshot(actor, dead: false, altForm: false, tick: 20).IsValid);
        cue = gate.ObserveSnapshot(actor, dead: true, altForm: false, tick: 21);
        Assert.True(cue.IsValid);
        Assert.False(gate.ObserveSnapshot(actor, dead: true, altForm: false, tick: 22).IsValid);
        Assert.False(gate.ObserveKill(actor, 23).IsValid);

        gate.Reset();
        Assert.False(gate.ObserveSnapshot(actor, dead: false, altForm: true, tick: 30).IsValid);
        Assert.False(gate.ObserveKill(actor, 31).IsValid);
        cue = gate.ObserveSnapshot(actor, dead: true, altForm: true, tick: 32);
        Assert.True(cue.IsValid);
        Assert.Equal(31u, cue.Tick);
        Assert.True(cue.AltForm);

        gate.Reset();
        Assert.False(gate.ObserveSnapshot(actor, dead: false, altForm: false, tick: 33).IsValid);
        cue = gate.ObserveSnapshot(actor, dead: true, altForm: false, tick: 34,
            presentationAlreadyHandled: true);
        Assert.True(cue.IsValid);
        Assert.True(cue.EnginePresentationHandled);

        gate.Reset();
        Assert.False(gate.ObserveSnapshot(actor with { Life = 2 }, dead: true,
            altForm: false, tick: 40).IsValid);
    }

    [Fact]
    public void DeathPresentationBindingFencesLifeAndConnectionReplacement()
    {
        CombatActor victim = new(2, 200, 4);
        Assert.True(DeathPresentationBinding.Accepts(victim,
            rosterConnectionId: 200, rosterLife: 4,
            presentationActor: victim, gateActor: CombatActor.None,
            capturedActor: CombatActor.None));
        Assert.True(DeathPresentationBinding.Accepts(victim,
            rosterConnectionId: 200, rosterLife: 5,
            presentationActor: victim with { Life = 5 }, gateActor: victim,
            capturedActor: CombatActor.None));
        Assert.True(DeathPresentationBinding.Accepts(victim,
            rosterConnectionId: 200, rosterLife: 5,
            presentationActor: victim with { Life = 5 },
            gateActor: CombatActor.None, capturedActor: victim));

        Assert.False(DeathPresentationBinding.Accepts(victim,
            rosterConnectionId: 200, rosterLife: 5,
            presentationActor: victim with { Life = 5 },
            gateActor: victim with { Life = 5 },
            capturedActor: victim with { Life = 5 }));
        Assert.False(DeathPresentationBinding.Accepts(victim,
            rosterConnectionId: 201, rosterLife: 4,
            presentationActor: victim, gateActor: victim,
            capturedActor: victim));
        Assert.False(DeathPresentationBinding.Accepts(victim,
            rosterConnectionId: 200, rosterLife: 6,
            presentationActor: victim, gateActor: victim,
            capturedActor: victim));
    }

    [Fact]
    public void DuplicateCurrentFeedRejectionDoesNotSuppressBoundDeathPresentation()
    {
        CombatFeedback feedback = New();
        KillEvent kill = Kill(901, Local, Enemy);
        Assert.True(feedback.Process(kill));
        bool duplicateAcceptedByFeed = feedback.Process(kill);
        Assert.False(duplicateAcceptedByFeed);
        bool semanticCurrent = DeathPresentationRouting.IsSemanticCurrent(kill,
            currentMatchId: 1, currentPhaseRevision: 1);
        Assert.True(DeathPresentationRouting.ShouldObserveKill(
            duplicateAcceptedByFeed, actorBound: true, semanticCurrent));
        Assert.False(DeathPresentationRouting.ShouldObserveKill(
            combatFeedAccepted: true, actorBound: false, semanticCurrent: true));
    }

    [Fact]
    public void DeathPresentationRejectsWrongMatchAndStalePhaseSemantics()
    {
        KillEvent current = Kill(902, Local, Enemy, match: 10, phase: 4);
        Assert.True(DeathPresentationRouting.IsSemanticCurrent(current, 10, 4));
        Assert.False(DeathPresentationRouting.IsSemanticCurrent(current, 11, 4));
        Assert.False(DeathPresentationRouting.IsSemanticCurrent(current, 10, 5));
        Assert.False(DeathPresentationRouting.ShouldObserveKill(
            combatFeedAccepted: false, actorBound: true,
            semanticCurrent: DeathPresentationRouting.IsSemanticCurrent(
                current, 11, 4)));
        Assert.False(DeathPresentationRouting.ShouldObserveKill(
            combatFeedAccepted: false, actorBound: true,
            semanticCurrent: DeathPresentationRouting.IsSemanticCurrent(
                current, 10, 5)));
    }

    [Theory]
    [InlineData((byte)0)]
    [InlineData((byte)6)]
    public void DeathPresentationBindingAcceptsLocalAndRemoteVictims(byte slot)
    {
        var victim = new CombatActor(slot, (ulong)(700 + slot), 3);
        Assert.True(DeathPresentationBinding.Accepts(victim,
            victim.ConnectionId, victim.Life, victim,
            CombatActor.None, CombatActor.None));
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
