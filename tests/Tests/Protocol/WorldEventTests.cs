using System;
using System.Runtime.CompilerServices;
using MphRead.Entities;
using MphRead.Mods.Network;
using OpenTK.Mathematics;
using Xunit;

namespace MphRead.Tests;

[Collection("Match baseline globals")]
public sealed class WorldEventTests
{
    [Theory]
    [InlineData(WorldSignalKind.PickupConsumed, WorldSubjectKind.Item, true)]
    [InlineData(WorldSignalKind.PickupRespawned, WorldSubjectKind.Spawner, false)]
    [InlineData(WorldSignalKind.FlagPickedUp, WorldSubjectKind.Flag, true)]
    [InlineData(WorldSignalKind.FlagDropped, WorldSubjectKind.Flag, true)]
    [InlineData(WorldSignalKind.FlagReset, WorldSubjectKind.Flag, false)]
    [InlineData(WorldSignalKind.FlagCaptured, WorldSubjectKind.Flag, true)]
    [InlineData(WorldSignalKind.NodeCaptured, WorldSubjectKind.Node, true)]
    [InlineData(WorldSignalKind.NodeContested, WorldSubjectKind.Node, false)]
    [InlineData(WorldSignalKind.PrimeChanged, WorldSubjectKind.Match, true)]
    [InlineData(WorldSignalKind.DefenderStateChanged, WorldSubjectKind.Node, false)]
    [InlineData(WorldSignalKind.OvertimeStarted, WorldSubjectKind.Match, false)]
    [InlineData(WorldSignalKind.MatchPoint, WorldSubjectKind.Match, false)]
    public void KindSpecificWorldFactsRoundTripAndRejectReservedBytes(WorldSignalKind kind, WorldSubjectKind subject, bool actor)
    {
        var value = new WorldEvent(1, 2, 3, 4, subject, kind, kind is WorldSignalKind.PickupRespawned or WorldSignalKind.OvertimeStarted ? (byte)255 : (byte)0,
            subject == WorldSubjectKind.Match ? 0u : 10u, actor ? new CombatActor(0, 100, 1) : CombatActor.None, Vector3.UnitY, kind == WorldSignalKind.OvertimeStarted ? 1u : 0u);
        byte[] bytes = new byte[WorldEvent.Size]; value.Write(bytes);
        Assert.Equal(64, bytes.Length);
        Assert.True(WorldEvent.TryRead(bytes, out var read)); Assert.Equal(value, read);
        foreach (int offset in new[] { 19, 37, 38, 39 })
        { bytes[offset] = 1; Assert.False(WorldEvent.TryRead(bytes, out _)); bytes[offset] = 0; }
        bytes[16] = 0; Assert.False(WorldEvent.TryRead(bytes, out _));
        Assert.False(WorldEvent.TryRead(bytes.AsSpan(0, 63), out _));
    }
    private sealed class Signals : ISceneServices
    {
        public int Count;
        public void PublishWorldSignal(Scene scene, in WorldSignal signal) { Assert.Equal(WorldSignalKind.PickupConsumed, signal.Kind); Count++; }
    }
    [Fact]
    public void PickupMutationPublishesOnceAndJournalIdentityNeverRecyclesWithinMatch()
    {
        using var state = new MatchBaselineTests.State();
        var signals = new Signals(); state.Scene.Services = signals;
        var item = (ItemInstanceEntity)RuntimeHelpers.GetUninitializedObject(typeof(ItemInstanceEntity));
        item._scene = state.Scene; item.DespawnTimer = -1;
        item.OnPickedUp(state.Players[0]); item.OnPickedUp(state.Players[0]);
        Assert.Equal(1, signals.Count); Assert.Equal(0, item.DespawnTimer);
        var journal = new ServerWorldEvents(); state.Scene.Match.MatchId = 1;
        uint id = journal.Identity(state.Scene, item);
        Assert.Equal(id, journal.Identity(state.Scene, item));
        journal.Forget(item);
        Assert.True(journal.Identity(state.Scene, item) > id);
        var signal = new WorldSignal(WorldSignalKind.PickupRespawned, WorldSubjectKind.Spawner, state.Players[0], null, 255, default);
        journal.Publish(state.Scene, signal, 1, 1); Assert.False(journal.TryPeek(out _));
        state.Scene.Match.Phase = MatchPhase.Playing; state.Scene.Match.PhaseRevision = 1;
        journal.Publish(state.Scene, signal, 2, 2); Assert.True(journal.TryPeek(out var value));
        Assert.Equal(2u, value.Id); journal.Consume(); Assert.False(journal.TryPeek(out _));
    }
}
