using System;
using System.Collections.Generic;
using MphRead.Entities;
using Xunit;

namespace MphRead.Tests;

[Collection("Match baseline globals")]
public sealed class WorldSignalTransitionTests
{
    private sealed class Signals : ISceneServices
    {
        public List<WorldSignal> Values { get; } = new();
        public void PublishWorldSignal(Scene scene, in WorldSignal signal) => Values.Add(signal);
    }
    [Fact]
    public void MatchPointPublishesForEachChangedTeamWithoutRepeatedPollingCues()
    {
        using var state = new MatchBaselineTests.State();
        MatchRuntime match = state.Configure(GameMode.Battle);
        state.Activate(0, 0); state.Activate(1, 1);
        match.ApplyRules(match.Rules.With(scoreGoal: 7));
        var signals = new Signals(); state.Scene.Services = signals;
        match.Players[0].Points = match.Players[1].Points = 6;
        match.Logic.UpdateState(); match.Logic.UpdateState();
        Assert.Equal(2, signals.Values.Count);
        Assert.All(signals.Values, s => Assert.Equal(WorldSignalKind.MatchPoint, s.Kind));
        Assert.Equal(0, signals.Values[0].Team); Assert.Equal(1, signals.Values[1].Team);
    }
    [Fact]
    public void OvertimePublishesOnlyAtFirstPeriodTransition()
    {
        using var state = new MatchBaselineTests.State();
        MatchRuntime match = state.Configure(GameMode.Battle);
        state.Activate(0, 0); state.Activate(1, 1);
        state.Scene.InsertEntity(state.Players[0]); state.Scene.InsertEntity(state.Players[1]);
        match.ApplyRules(match.Rules.With(overtimePolicy: OvertimePolicy.ModeDefault));
        match.Phase = MatchPhase.Playing; match.MatchTime = 0;
        var signals = new Signals(); state.Scene.Services = signals;
        MatchOvertime.Evaluate(state.Scene, true, null);
        MatchOvertime.Evaluate(state.Scene, false, null);
        Assert.Single(signals.Values);
        Assert.Equal(WorldSignalKind.OvertimeStarted, signals.Values[0].Kind);
        Assert.Equal((uint)match.Period, signals.Values[0].A);
    }
    [Theory]
    [InlineData(false, WorldSignalKind.NodeContested)]
    [InlineData(true, WorldSignalKind.DefenderStateChanged)]
    public void CapturedOwnerDisconnectEmitsClearOnlyOnce(bool defender, WorldSignalKind expected)
    {
        using var state = new MatchBaselineTests.State();
        state.Configure(GameMode.Battle); state.Activate(0, 0);
        var signals = new Signals(); state.Scene.Services = signals;
        var node = (NodeDefenseEntity)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(NodeDefenseEntity));
        void Set(Type type, string name, object value) => type.GetField(name,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(node, value);
        Set(typeof(EntityBase), "_scene", state.Scene);
        Set(typeof(EntityBase), "_transform", OpenTK.Mathematics.Matrix4.Identity);
        Set(typeof(NodeDefenseEntity), "_occupiedBy", new bool[8]);
        Set(typeof(NodeDefenseEntity), "_previousOccupiedBy", new bool[8]);
        Set(typeof(NodeDefenseEntity), "_capturedPlayer", state.Players[0]);
        Set(typeof(NodeDefenseEntity), "_defender", defender);
        Set(typeof(NodeDefenseEntity), "_contested", true);
        node._currentTeam = 0;
        node.ReleaseServerPlayer(state.Players[0]); node.ReleaseServerPlayer(state.Players[0]);
        WorldSignal signal = Assert.Single(signals.Values);
        Assert.Equal(expected, signal.Kind); Assert.Equal(255, signal.Team); Assert.Equal(0u, signal.A);
        Assert.Equal(NodeDefenseEntity.NeutralTeam, node.CurrentTeam); Assert.False(node.Contested);
    }
}
