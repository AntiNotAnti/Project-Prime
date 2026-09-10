using System;
using MphRead.Mods.Network;
using OpenTK.Mathematics;
using Xunit;

namespace MphRead.Tests;

public sealed class PredictedSelfImpulseTests
{
    private static readonly CombatActor Actor = new(0, 50, 3);

    [Fact]
    public void SharedImpulsePreservesBipedAndAltFormRules()
    {
        Assert.Equal(new Vector3(1, .25f, 3),
            DamageImpulse.Apply(new Vector3(0, .2f, 2), new Vector3(1, .2f, 1), false, false));
        Assert.Equal(new Vector3(1, -.2f, 3),
            DamageImpulse.Apply(new Vector3(0, .2f, 2), new Vector3(1, -.4f, 1), false, false));
        Assert.Equal(new Vector3(.4f, .2f, 2.4f),
            DamageImpulse.Apply(new Vector3(0, .2f, 2), new Vector3(1, 9, 1), true, false));
        Assert.Equal(new Vector3(0, .2f, 2),
            DamageImpulse.Apply(new Vector3(0, .2f, 2), Vector3.One, false, true));
    }

    [Fact]
    public void PredictedImpulseIsConsumedWithoutDoubleApplication()
    {
        var ledger = New();
        CombatShot shot = Shot(4);
        Assert.True(ledger.TryPredict(shot, Vector3.Zero, new Vector3(1, .1f, 0),
            altForm: false, out Vector3 predicted));
        Assert.Equal(new Vector3(1, .1f, 0), predicted);
        Assert.True(ledger.ApplyAuthoritative(Damage(1, 4, 10, new Vector3(1, .1f, 0)),
            predicted, altForm: false, out Vector3 confirmed));
        Assert.Equal(predicted, confirmed);
        Assert.False(ledger.TryPredict(shot, confirmed, Vector3.UnitX, false, out _));
        Assert.Equal(1, ledger.Metrics.Confirmed);
        Assert.Equal(2, ledger.Metrics.DuplicatePrevented);
    }

    [Fact]
    public void ReliableDamageBeforeSnapshotAppliesImpulseOnce()
    {
        var ledger = New();
        ledger.NoteAppliedSnapshot(9);
        Assert.True(ledger.ApplyAuthoritative(Damage(1, 5, 10, Vector3.UnitX),
            new Vector3(0, 0, 1), false, out Vector3 speed));
        Assert.Equal(new Vector3(1, 0, 1), speed);
        Assert.Equal(1, ledger.Metrics.AuthoritativeApplied);
    }

    [Fact]
    public void AppliedSnapshotAtOrAfterDamageKeepsCurrentVelocity()
    {
        var ledger = New();
        Vector3 currentSpeed = new(.75f, .4f, 3);
        ledger.NoteAppliedSnapshot(11);
        Assert.True(ledger.ApplyAuthoritative(Damage(1, 5, 10, Vector3.UnitX),
            currentSpeed, false, out Vector3 speed));
        Assert.Equal(currentSpeed, speed);
    }

    [Fact]
    public void AuthoritativeFirstRetiresIdentityBeforeLocalCollision()
    {
        var ledger = New();
        Assert.True(ledger.ApplyAuthoritative(Damage(1, 5, 10, Vector3.UnitX),
            Vector3.Zero, false, out Vector3 speed));
        Assert.False(ledger.TryPredict(Shot(5), speed, Vector3.UnitX, false, out _));
        Assert.Equal(1, ledger.Metrics.AuthoritativeApplied);
        Assert.Equal(1, ledger.Metrics.DuplicatePrevented);
    }

    [Theory]
    [InlineData((ushort)0, (ushort)0)]
    [InlineData((ushort)90, (ushort)1)]
    public void LethalOrFrozenAuthoritativeDamageDoesNotInventImpulse(
        ushort health, ushort frozenTicks)
    {
        var ledger = New();
        CombatEvent value = Damage(1, 5, 10, Vector3.UnitX) with
        {
            Health = health,
            FrozenTicks = frozenTicks
        };
        Assert.True(ledger.ApplyAuthoritative(value, Vector3.UnitZ, false, out Vector3 speed));
        Assert.Equal(Vector3.UnitZ, speed);
        Assert.Equal(0, ledger.Metrics.AuthoritativeApplied);
        Assert.False(ledger.TryPredict(Shot(5), speed, Vector3.UnitX, false, out _));
    }

    [Fact]
    public void CorrectionBeforePredictionIsNotCounted()
    {
        var ledger = New();
        ledger.NoteCorrection(4, hard: true);
        Assert.Equal(0, ledger.Metrics.CorrectionSamples);
    }

    [Fact]
    public void BombJumpIsSeparateAndDeduplicatedByIdentity()
    {
        var ledger = New();
        Assert.True(ledger.NoteBombJump(Shot(7), 1.25f));
        Assert.False(ledger.NoteBombJump(Shot(7), 1.25f));
        Assert.Equal(1, ledger.Metrics.BombJumpsPredicted);
        Assert.Equal(1.25, ledger.Metrics.MeanImpulseMagnitude);
        Assert.Equal(1, ledger.Metrics.DuplicatePrevented);
        Assert.Equal(0, ledger.Metrics.Pending);
    }

    [Fact]
    public void RemoteAndRemoteTargetDamageCannotEnterSelfLedger()
    {
        var ledger = New();
        CombatActor remote = new(1, 60, 2);
        Assert.False(ledger.TryPredict(new CombatShot(remote, 8, 0, 0, 0, 0),
            Vector3.Zero, Vector3.UnitX, false, out _));
        Assert.False(ledger.ApplyAuthoritative(Damage(1, 8, 10, Vector3.UnitX,
            Actor, remote), Vector3.Zero, false, out _));
        Assert.Equal(0, ledger.Metrics.Predicted);
        Assert.Equal(0, ledger.Metrics.AuthoritativeApplied);
    }

    [Fact]
    public void ExpiryAndEpochResetAreBoundedAndDoNotCrossLives()
    {
        var ledger = New();
        Assert.True(ledger.TryPredict(Shot(UInt32.MaxValue), Vector3.Zero, Vector3.UnitX,
            false, out _));
        for (int i = 0; i < PredictedSelfImpulse.LifetimeFrames; i++) ledger.Advance();
        Assert.Equal(1, ledger.Metrics.Expired);
        ledger.SetContext(2, Actor with { Life = 4 });
        Assert.False(ledger.ApplyAuthoritative(Damage(2, UInt32.MaxValue, 10, Vector3.UnitX,
            Actor, Actor), Vector3.Zero, false, out _));
        Assert.Equal(1, ledger.Metrics.Expired);
    }

    [Fact]
    public void WarmedPredictionAndConfirmationAllocateNothing()
    {
        var ledger = New();
        ledger.TryPredict(Shot(1), Vector3.Zero, Vector3.UnitX, false, out _);
        ledger.ApplyAuthoritative(Damage(1, 1, 2, Vector3.UnitX), Vector3.UnitX, false, out _);
        long before = GC.GetAllocatedBytesForCurrentThread();
        bool accepted = true;
        for (uint i = 10; i < 26; i++)
        {
            accepted &= ledger.TryPredict(Shot(i), Vector3.Zero, Vector3.UnitX, false, out Vector3 speed);
            accepted &= ledger.ApplyAuthoritative(Damage(i, i, 2, Vector3.UnitX), speed, false, out _);
            ledger.Advance();
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(accepted);
        Assert.Equal(0, allocated);
    }

    private static PredictedSelfImpulse New()
    {
        var value = new PredictedSelfImpulse { Enabled = true };
        value.SetContext(1, Actor);
        return value;
    }

    private static CombatShot Shot(uint command) => new(Actor, command, 0, 0, 0, 0);

    private static CombatEvent Damage(uint id, uint command, uint tick, Vector3 direction,
        CombatActor? actor = null, CombatActor? target = null) => new(id, tick, command,
        CombatEventKind.Damage, (byte)BeamType.Missile, 0, actor ?? Actor, target ?? Actor,
        90, 10, Vector3.Zero, direction, 0, 0, 0);
}
