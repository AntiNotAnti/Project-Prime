using System;
using MphRead.Entities;
using MphRead.Mods.Network;
using OpenTK.Mathematics;
using Xunit;

namespace MphRead.Tests;

public sealed class PredictedHitFeedbackTests
{
    private static readonly CombatActor Local = new(0, 10, 1);
    private static readonly CombatActor Target = new(1, 20, 1);

    [Fact]
    public void ExactIdentityConfirmsAndReportsMonotonicTiming()
    {
        var feedback = New();
        feedback.ObserveShot(Shot(7));
        feedback.Advance();
        Assert.True(feedback.ObserveDamageAttempt(Shot(7), Target, 2, continuous: false));
        feedback.Advance();
        feedback.Advance();
        Assert.True(feedback.Confirm(Damage(1, 7, Local, Target, 2)));

        HitPredictionMetrics metrics = feedback.Metrics;
        Assert.Equal(1, metrics.Predicted);
        Assert.Equal(1, metrics.Confirmed);
        Assert.Equal(0, metrics.Denied);
        Assert.Equal(0, metrics.Pending);
        Assert.Equal(1, metrics.ShotToPredictionFrames);
        Assert.Equal(3, metrics.ShotToConfirmationFrames);
        Assert.Equal(2, metrics.PredictionLeadFrames);
        Assert.Equal(1d, metrics.ConfirmationRate);
    }

    [Fact]
    public void MatchActorTargetAndWeaponFenceConfirmation()
    {
        var feedback = New();
        feedback.ObserveShot(Shot(9));
        Assert.True(feedback.ObserveDamageAttempt(Shot(9), Target, 2, false));
        Assert.False(feedback.Confirm(Damage(1, 9, Local, Target with { Life = 2 }, 2)));
        Assert.False(feedback.Confirm(Damage(2, 9, Local with { ConnectionId = 11 }, Target, 2)));
        Assert.False(feedback.Confirm(Damage(3, 9, Local, Target, 3)));
        Assert.False(feedback.Confirm(Damage(3, 9, Local, Target, 3, CombatEventFlags.Headshot)));
        Assert.Equal(1, feedback.Metrics.AuthoritativeHeadshotsUnpredicted);
        Assert.Equal(1, feedback.Metrics.Pending);

        feedback.SetContext(2, Local);
        Assert.False(feedback.Confirm(Damage(4, 9, Local, Target, 2)));
        Assert.Equal(1, feedback.Metrics.Predicted);
        Assert.Equal(0, feedback.Metrics.Denied);
    }

    [Fact]
    public void ExpiryDeniesOnceAndRetiredIdentityPreventsRepeatedCollision()
    {
        var feedback = New();
        Assert.True(feedback.ObserveDamageAttempt(Shot(12), Target, 1, false));
        for (int i = 0; i < PredictedHitFeedback.MaxLifetimeFrames; i++) feedback.Advance();
        Assert.Equal(1, feedback.Metrics.Denied);
        Assert.False(feedback.ObserveDamageAttempt(Shot(12), Target, 1, false));
        Assert.Equal(1, feedback.Metrics.DuplicatePrevented);
    }

    [Fact]
    public void CurrentShotAttributionCannotCrossLifeBoundary()
    {
        var feedback = New();
        CombatShot shot = Shot(18);
        feedback.ObserveShot(shot);
        Assert.True(feedback.OwnsCurrentShot(shot));
        feedback.SetContext(1, Local with { Life = 2 });
        Assert.False(feedback.OwnsCurrentShot(shot));
    }

    [Fact]
    public void ZeroAndWrappedCommandSequencesRemainValid()
    {
        var feedback = New();
        Assert.True(feedback.ObserveDamageAttempt(Shot(0), Target, 1, false));
        Assert.True(feedback.Confirm(Damage(1, 0, Local, Target, 1)));
        Assert.True(feedback.ObserveDamageAttempt(Shot(UInt32.MaxValue), Target, 1, false));
        Assert.True(feedback.Confirm(Damage(2, UInt32.MaxValue, Local, Target, 1)));
        Assert.Equal(2, feedback.Metrics.Confirmed);
    }

    [Fact]
    public void AuthoritativeHeadshotAndKillPromotionsAreCountedOnlyAfterPrediction()
    {
        var feedback = New();
        Assert.True(feedback.ObserveDamageAttempt(Shot(20), Target, 1, false));
        Assert.True(feedback.Confirm(Damage(1, 20, Local, Target, 1,
            CombatEventFlags.Headshot)));
        Assert.True(feedback.ObserveDamageAttempt(Shot(21), Target, 1, false));
        Assert.True(feedback.Confirm(Damage(2, 21, Local, Target, 1, health: 0)));
        Assert.Equal(1, feedback.Metrics.HeadshotPromotions);
        Assert.Equal(1, feedback.Metrics.KillPromotions);
    }

    [Fact]
    public void HeadshotClassificationSeparatesAgreementDowngradeAndPromotion()
    {
        var feedback = New();
        Assert.True(feedback.ObserveDamageAttempt(Shot(30), Target, 1, DamageFlags.Headshot, false));
        Assert.True(feedback.Confirm(Damage(1, 30, Local, Target, 1)));
        Assert.True(feedback.ObserveDamageAttempt(Shot(31), Target, 1, DamageFlags.Headshot, false));
        Assert.True(feedback.Confirm(Damage(2, 31, Local, Target, 1, CombatEventFlags.Headshot)));
        Assert.True(feedback.ObserveDamageAttempt(Shot(32), Target, 1, DamageFlags.None, false));
        Assert.True(feedback.Confirm(Damage(3, 32, Local, Target, 1, CombatEventFlags.Headshot)));

        HitPredictionMetrics metrics = feedback.Metrics;
        Assert.Equal(2, metrics.PredictedHeadshots);
        Assert.Equal(1, metrics.ConfirmedHeadshots);
        Assert.Equal(1, metrics.HeadshotsDowngraded);
        Assert.Equal(1, metrics.HeadshotsPromoted);
        Assert.Equal(2, metrics.AuthoritativeHeadshotCues);
        Assert.Equal(1d / 2d, metrics.HeadshotAgreementRate);
        Assert.Equal(1d / 2d, metrics.HeadshotDowngradeRate);
        Assert.Equal(1d / 2d, metrics.HeadshotPromotionRate);
    }

    [Fact]
    public void HeadshotExpiryAndUnpredictedAuthorityAreCountedSeparately()
    {
        var feedback = New();
        Assert.True(feedback.ObserveDamageAttempt(Shot(33), Target, 1, DamageFlags.Headshot, false));
        for (int i = 0; i < PredictedHitFeedback.MaxLifetimeFrames; i++) feedback.Advance();
        Assert.Equal(1, feedback.Metrics.HeadshotsDenied);

        Assert.False(feedback.Confirm(Damage(4, 34, Local, Target, 1, CombatEventFlags.Headshot)));
        Assert.Equal(1, feedback.Metrics.AuthoritativeHeadshotsUnpredicted);
        Assert.Null(feedback.Metrics.HeadshotAgreementRate);
        Assert.Null(feedback.Metrics.HeadshotDowngradeRate);
        Assert.Null(feedback.Metrics.HeadshotPromotionRate);
    }

    [Fact]
    public void DirectConfirmDoesNotPretendToDeduplicateEvents()
    {
        var feedback = New();
        Assert.True(feedback.ObserveDamageAttempt(Shot(35), Target, 1, DamageFlags.Headshot, false));
        CombatEvent value = Damage(5, 35, Local, Target, 1, CombatEventFlags.Headshot);
        Assert.True(feedback.Confirm(value));
        Assert.False(feedback.Confirm(value));
        Assert.Equal(1, feedback.Metrics.ConfirmedHeadshots);
        Assert.Equal(1, feedback.Metrics.AuthoritativeHeadshotsUnpredicted);
    }

    [Fact]
    public void ContinuousContactIsRateLimitedAndNeverUsesPendingSlots()
    {
        var feedback = New();
        for (int i = 0; i < 100; i++)
            feedback.ObserveDamageAttempt(Shot(4), Target, (byte)BeamType.ShockCoil, true);
        Assert.Equal(1, feedback.Metrics.ContinuousPredicted);
        Assert.Equal(0, feedback.Metrics.Pending);
        for (int i = 0; i < PredictedHitFeedback.ContinuousPulseFrames; i++) feedback.Advance();
        Assert.True(feedback.ObserveDamageAttempt(Shot(4), Target, (byte)BeamType.ShockCoil, true));
        Assert.True(feedback.Confirm(Damage(1, 4, Local, Target, (byte)BeamType.ShockCoil)));
        Assert.True(feedback.Confirm(Damage(2, 4, Local, Target, (byte)BeamType.ShockCoil)));
        Assert.Equal(2, feedback.Metrics.ContinuousPredicted);
        Assert.Equal(2, feedback.Metrics.ContinuousConfirmed);
        Assert.True(feedback.Confirm(Damage(3, 4, Local, Target, (byte)BeamType.ShockCoil)));
        Assert.Equal(1, feedback.Metrics.ContinuousAuthoritativeUnpredicted);
    }

    [Fact]
    public void ContinuousConfirmationCannotConsumeArbitrarilyOldPulses()
    {
        var feedback = New();
        for (int i = 0; i < PredictedHitFeedback.MaxLifetimeFrames * 3; i++)
        {
            feedback.ObserveDamageAttempt(Shot(4), Target, (byte)BeamType.ShockCoil, true);
            feedback.Advance();
        }
        HitPredictionMetrics before = feedback.Metrics;
        Assert.InRange(before.ContinuousPending, 1, 16);
        Assert.True(before.ContinuousDenied > 0);

        int recent = before.ContinuousPending;
        for (int i = 0; i < recent + 5; i++)
            feedback.Confirm(Damage((uint)(i + 1), 4, Local, Target, (byte)BeamType.ShockCoil));
        Assert.Equal(recent, feedback.Metrics.ContinuousConfirmed);
        Assert.Equal(5, feedback.Metrics.ContinuousAuthoritativeUnpredicted);
    }

    [Fact]
    public void WarmedInsertionConfirmationAndExpiryAllocateNothing()
    {
        var feedback = New();
        for (uint i = 0; i < 8; i++)
        {
            feedback.ObserveDamageAttempt(Shot(i), Target, 1, false);
            feedback.Confirm(Damage(i + 1, i, Local, Target, 1));
        }
        long before = GC.GetAllocatedBytesForCurrentThread();
        bool accepted = true;
        for (uint i = 100; i < 116; i++)
        {
            accepted &= feedback.ObserveDamageAttempt(Shot(i), Target, 1, false);
            accepted &= feedback.Confirm(Damage(i + 1, i, Local, Target, 1));
            feedback.Advance();
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(accepted);
        Assert.Equal(0, allocated);
    }

    private static PredictedHitFeedback New()
    {
        var feedback = new PredictedHitFeedback();
        feedback.SetContext(1, Local);
        return feedback;
    }

    private static CombatShot Shot(uint command) => new(Local, command, 0, 0, 0, 0);

    private static CombatEvent Damage(uint id, uint command, CombatActor actor,
        CombatActor target, byte weapon, CombatEventFlags flags = 0, ushort health = 90)
        => new(id, 50, command, CombatEventKind.Damage,
            weapon, flags, actor, target, health, 10, Vector3.Zero, Vector3.UnitZ, 0, 0, 0);
}
