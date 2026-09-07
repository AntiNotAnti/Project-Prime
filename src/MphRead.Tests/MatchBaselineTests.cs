using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using MphRead.Entities;
using MphRead.Formats;
using Xunit;

namespace MphRead.Tests;

[CollectionDefinition("Match baseline globals", DisableParallelization = true)]
public sealed class MatchBaselineCollection { }

// R0 characterization: intentionally preserves current quirks, not desired future rules.
[Collection("Match baseline globals")]
public sealed class MatchBaselineTests
{
    [Fact]
    public void ModeNumbersAndTeamClassificationAreStable()
    {
        Assert.Equal(new byte[] { 0, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15 },
            Enum.GetValues<GameMode>().Select(mode => (byte)mode));
        Assert.Equal(new[] { GameMode.BattleTeams, GameMode.SurvivalTeams, GameMode.Capture,
            GameMode.BountyTeams, GameMode.NodesTeams, GameMode.DefenderTeams },
            Enum.GetValues<GameMode>().Where(mode => mode.IsTeamMode()));
        Assert.Equal("None,SinglePlayer,Battle,BattleTeams,Survival,SurvivalTeams,Capture,Bounty,BountyTeams,Nodes,NodesTeams,Defender,DefenderTeams,PrimeHunter,Unknown15",
            String.Join(',', Enum.GetNames<GameMode>()));
    }

    [Fact]
    public void MultiplayerModeToEntityLayerConversionPreservesFourPlayerAndExpandedSlotBehavior()
    {
        int[] two = { 0, 3, 15, 15, 12, 8, 11, 4, 7, 14, 14, 0, 13 };
        int[] three = { 1, 3, 15, 15, 12, 9, 11, 5, 7, 14, 14, 1, 13 };
        int[] four = { 2, 3, 15, 15, 12, 10, 11, 6, 7, 14, 14, 2, 13 };
        var modes = Enumerable.Range(3, 13).Select(value => (GameMode)value).ToArray();
        Assert.Equal(two, modes.Select(mode => Metadata.GetMultiplayerEntityLayer(mode, 2)));
        Assert.Equal(three, modes.Select(mode => Metadata.GetMultiplayerEntityLayer(mode, 3)));
        Assert.Equal(four, modes.Select(mode => Metadata.GetMultiplayerEntityLayer(mode, 4)));
        Assert.Equal(two, modes.Select(mode => Metadata.GetMultiplayerEntityLayer(mode, 8)));
    }

    [Theory]
    [InlineData(GameMode.Battle, false, true)]
    [InlineData(GameMode.BattleTeams, false, true)]
    [InlineData(GameMode.Survival, true, true)]
    [InlineData(GameMode.SurvivalTeams, true, true)]
    [InlineData(GameMode.Capture, false, false)]
    [InlineData(GameMode.Bounty, false, false)]
    [InlineData(GameMode.BountyTeams, false, false)]
    [InlineData(GameMode.Nodes, false, false)]
    [InlineData(GameMode.NodesTeams, false, false)]
    [InlineData(GameMode.Defender, true, false)]
    [InlineData(GameMode.DefenderTeams, true, false)]
    [InlineData(GameMode.PrimeHunter, true, false)]
    public void PlayerRankingUsesModeScoreThenDeathsOrKills(GameMode mode, bool timed, bool deaths)
    {
        using var state = new State();
        MatchRuntime match = state.Configure(mode);
        match.Points[0] = match.Points[1] = 3;
        match.Time[0] = match.Time[1] = 20;
        Assert.Equal(0, match.Logic.ComparePlayers(0, 1));
        if (deaths) { match.Deaths[1] = 2; }
        else { match.Kills[0] = 2; }
        Assert.Equal(1, match.Logic.ComparePlayers(0, 1));
        Assert.Equal(-1, match.Logic.ComparePlayers(1, 0));
        if (timed) { match.Time[1] = 21; }
        else { match.Points[1] = 4; }
        Assert.Equal(-1, match.Logic.ComparePlayers(0, 1));
        if (mode is GameMode.Survival or GameMode.SurvivalTeams)
        {
            match.Time[0] = -1;
            Assert.Equal(1, match.Logic.ComparePlayers(0, 1));
        }
    }

    [Theory]
    [InlineData(GameMode.BattleTeams, false, true)]
    [InlineData(GameMode.SurvivalTeams, true, true)]
    [InlineData(GameMode.Capture, false, false)]
    [InlineData(GameMode.NodesTeams, false, false)]
    [InlineData(GameMode.DefenderTeams, true, false)]
    public void TeamRankingUsesModeScoreAndTiebreak(GameMode mode, bool timed, bool deaths)
    {
        using var state = new State();
        MatchRuntime match = state.Configure(mode);
        Assert.Equal(0, match.Logic.CompareTeams(0, 1));
        if (deaths) { match.TeamDeaths[1] = 1; }
        else { match.TeamKills[0] = 1; }
        Assert.Equal(1, match.Logic.CompareTeams(0, 1));
        if (timed) { match.TeamTime[1] = 10; }
        else { match.TeamPoints[1] = 10; }
        Assert.Equal(-1, match.Logic.CompareTeams(0, 1));
    }

    [Fact]
    public void BountyTeamsCurrentlyComparesAllTeamsAsTied()
    {
        using var state = new State();
        MatchRuntime match = state.Configure(GameMode.BountyTeams);
        match.TeamPoints[0] = 100;
        match.TeamKills[0] = 50;
        match.TeamTime[0] = 30;
        match.TeamDeaths[1] = 20;
        Assert.Equal(0, match.Logic.CompareTeams(0, 1));
        Assert.Equal(0, match.Logic.CompareTeams(1, 0));
    }

    [Theory]
    [InlineData(GameMode.Battle)] [InlineData(GameMode.BattleTeams)]
    [InlineData(GameMode.Capture)] [InlineData(GameMode.Bounty)]
    [InlineData(GameMode.BountyTeams)] [InlineData(GameMode.Nodes)] [InlineData(GameMode.NodesTeams)]
    public void PointModesDispatchAndClampOnlyTheWinningActiveTeam(GameMode mode)
    {
        using var state = new State();
        MatchRuntime match = state.Configure(mode);
        match.Flow.Setup(); // No intro camera or active players during setup.
        state.Activate(0, 0);
        match.ApplyRules(match.Rules.With(scoreGoal: 5));
        match.TeamPoints[1] = 20; // An inactive team's score cannot end the round.
        match.Logic.ProcessMode();
        Assert.True(match.MatchTime > 0);
        match.TeamPoints[0] = 4;
        match.Logic.ProcessMode();
        Assert.True(match.MatchTime > 0);
        match.TeamPoints[0] = 8;
        match.Logic.ProcessMode();
        Assert.Equal(0, match.MatchTime);
        Assert.Equal(5, match.TeamPoints[0]);
        Assert.Equal(20, match.TeamPoints[1]);
        match.MatchTime = 30;
        match.ApplyRules(match.Rules.With(scoreGoal: 0));
        match.Logic.ProcessMode();
        Assert.Equal(30, match.MatchTime);
    }

    [Theory]
    [InlineData(GameMode.Defender)] [InlineData(GameMode.DefenderTeams)]
    public void DefenderDispatchEndsAtTimeGoalWithoutClamping(GameMode mode)
    {
        using var state = new State();
        MatchRuntime match = state.Configure(mode);
        match.Flow.Setup();
        state.Activate(0, 0);
        match.TeamTime[0] = 89;
        match.Logic.ProcessMode();
        Assert.Equal(900, match.MatchTime);
        match.TeamTime[0] = 91;
        match.Logic.ProcessMode();
        Assert.Equal(0, match.MatchTime);
        Assert.Equal(91, match.TeamTime[0]);
    }

    [Fact]
    public void UpdateStateAggregatesInitialSlotsButOnlyRanksActiveSlotsAndKeepsCompetitionTies()
    {
        using var state = new State();
        MatchRuntime match = state.Configure(GameMode.Battle);
        state.Activate(0, 0); state.Activate(1, 1); state.Activate(2, 2);
        state.Players[3].LoadFlags = LoadFlags.Initial;
        state.Players[3].TeamIndex = 0;
        match.Points[0] = match.Points[1] = 5;
        match.Points[2] = 3; match.Points[3] = 7;
        match.Kills[3] = 4; match.Deaths[3] = 2;
        match.Logic.UpdateState();
        Assert.Equal(12, match.TeamPoints[0]);
        Assert.Equal(4, match.TeamKills[0]);
        Assert.Equal(2, match.TeamDeaths[0]);
        Assert.Equal(3, match.ActivePlayers);
        Assert.Equal(new[] { 0, 1, 2 }, match.ResultSlots.Take(3));
        Assert.Equal(new[] { 0, 0, 2 }, match.Standings.Take(3));
        Assert.Equal(PlayerEntity.SlotCapacity - 1, match.Standings[3]);
        match.Logic.UpdateState();
        Assert.Equal(12, match.TeamPoints[0]); // Recomputed, not accumulated each frame.
    }

    [Fact]
    public void TeamStandingsCurrentlyWritesFinalRankToActiveCountIndexInsteadOfSparseSlot()
    {
        using var state = new State();
        MatchRuntime match = state.Configure(GameMode.BattleTeams);
        state.Activate(0, 0); state.Activate(3, 1);
        match.Points[0] = 5; match.Points[3] = 1;
        match.Logic.UpdateState();
        Assert.Equal(new[] { 0, 3 }, match.ResultSlots.Take(2));
        Assert.Equal(0, match.Standings[0]);
        Assert.Equal(1, match.Standings[1]); // Current index/slot mismatch is deliberately frozen for R0.
        Assert.Equal(PlayerEntity.SlotCapacity - 1, match.Standings[3]);
    }

    [Theory]
    [InlineData(GameMode.Survival)] [InlineData(GameMode.SurvivalTeams)]
    public void SurvivalDispatchCountsSpareLivesAndMarksTheRemainingSurvivor(GameMode mode)
    {
        using var state = new State();
        MatchRuntime match = state.Configure(mode);
        match.Flow.Setup();
        state.Activate(0, 0); state.Activate(1, 1);
        Scene scene = Clock(state.Scene, 0.5f);
        match.TeamDeaths[0] = match.TeamDeaths[1] = 2;
        match.Logic.ProcessMode(); // Dead players still have their final life at equality.
        Assert.Equal(900, match.MatchTime);
        Assert.Equal(0.5f, match.Time[0]);
        match.TeamDeaths[1] = 3;
        match.Logic.ProcessMode();
        Assert.Equal(0, match.MatchTime);
        Assert.Equal(-1, match.Time[0]);
        Assert.Equal(0.5f, match.Time[1]);
    }

    [Fact]
    public void PrimeDispatchAccumulatesHolderTimeAndDropsAnInactiveHolder()
    {
        using var state = new State();
        MatchRuntime match = state.Configure(GameMode.PrimeHunter);
        match.Flow.Setup();
        state.Activate(0, 0);
        Clock(state.Scene, 0.5f); // Frame 1 avoids the independent periodic damage path.
        match.PrimeHunter = -1;
        match.Logic.ProcessMode();
        Assert.Equal(0, match.Time[0]);
        match.PrimeHunter = 0;
        match.Time[0] = 89.5f;
        match.Logic.ProcessMode();
        Assert.Equal(90, match.Time[0]);
        Assert.Equal(0, match.MatchTime);
        state.Players[0].LoadFlags = LoadFlags.Initial;
        match.Logic.ProcessMode();
        Assert.Equal(-1, match.PrimeHunter);
        Assert.Equal(90, match.Time[0]);
    }

    [Fact]
    public void SurvivalAggregationCurrentlyIgnoresNegativeSurvivorSentinel()
    {
        using var state = new State();
        MatchRuntime match = state.Configure(GameMode.SurvivalTeams);
        state.Activate(0, 0); state.Activate(1, 1);
        match.Time[0] = -1; match.Time[1] = 20;
        match.Logic.UpdateState();
        Assert.Equal(0, match.TeamTime[0]);
        Assert.Equal(20, match.TeamTime[1]);
        Assert.Equal(1, match.Logic.ComparePlayers(0, 1));
        Assert.Equal(-1, match.Logic.CompareTeams(0, 1));
    }

    [Fact]
    public void MatchClockClampsAtZeroAndPreservesUnlimitedSentinel()
    {
        using var state = new State();
        MatchRuntime match = state.Configure(GameMode.Battle);
        Clock(state.Scene, 0.5f);
        match.MatchTime = 0.25f;
        match.Flow.UpdateTime();
        Assert.Equal(0, match.MatchTime);
        match.MatchTime = -1;
        match.Flow.UpdateTime();
        Assert.Equal(-1, match.MatchTime);
    }

    private static Scene Clock(Scene scene, float delta)
    {
        typeof(Scene).GetField("_frameTime", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(scene, delta);
        typeof(Scene).GetField("_frameCount", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(scene, 1UL);
        return scene;
    }

    [Fact]
    public void RotationProgressResetClearsLifecycleFlagsButPreservesScoresRulesAndClock()
    {
        using var state = new State();
        MatchRuntime match = state.Configure(GameMode.Battle);
        match.LegacyState = MatchState.Ending;
        match.ForceEndGame = true;
        match.ApplyRules(match.Rules.With(scoreGoal: 17));
        match.MatchTime = 33; match.Points[0] = 9;
        foreach (string name in new[] { "_tempoChanged", "_stateChanged" }) { SetMember(match.Flow, name, true); }
        foreach (string name in new[] { "_matchEndTime", "_lastAlarmTime" }) { SetMember(match.Flow, name, 12f); }
        SetMember(match.Flow, "_nextAlarmIndex", 3);
        match.Flow.ResetProgress();
        Assert.Equal(MatchState.InProgress, match.LegacyState);
        Assert.False(match.ForceEndGame);
        Assert.Equal(17, match.Rules.LegacyPointGoal); Assert.Equal(33, match.MatchTime); Assert.Equal(9, match.Points[0]);
        foreach (string name in new[] { "_tempoChanged", "_stateChanged" }) { Assert.Equal(false, GetMember(match.Flow, name)); }
        foreach (string name in new[] { "_matchEndTime", "_lastAlarmTime" }) { Assert.Equal(0f, GetMember(match.Flow, name)); }
        Assert.Equal(0, GetMember(match.Flow, "_nextAlarmIndex"));
    }

    private static MemberInfo Member(object owner, string name)
    {
        Type type = owner.GetType();
        return (MemberInfo?)type.GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMemberException(type.FullName, name);
    }

    private static object? GetMember(object owner, string name) => Member(owner, name) switch
    {
        PropertyInfo property => property.GetValue(owner),
        FieldInfo field => field.GetValue(owner),
        _ => throw new InvalidOperationException($"Unsupported member kind: {name}")
    };

    private static void SetMember(object owner, string name, object value)
    {
        switch (Member(owner, name))
        {
            case PropertyInfo property:
                property.SetValue(owner, value);
                break;
            case FieldInfo field:
                field.SetValue(owner, value);
                break;
            default:
                throw new InvalidOperationException($"Unsupported member kind: {name}");
        }
    }

    // No data, sound, save files or graphics initialization. Restore every touched global even on assertion failure.
    internal sealed class State : IDisposable
    {
        private readonly List<(FieldInfo Field, object? Value)> _values = new();
        private readonly List<(Array Original, Array Copy)> _arrays = new();
        public PlayerEntity[] Players { get; } = PlayerEntity._players;
        private readonly PlayerEntity[] _players;
        private readonly int _count = PlayerEntity.PlayerCount, _main = PlayerEntity.MainPlayerIndex;
        private readonly CameraSequence? _intro = CameraSequence.Intro;
        private readonly bool _serverMode = Read.ServerMode;
        private readonly byte _saveSlot = Menu.SaveSlot;
        private readonly int _previousSaveSlot = Menu.PreviousSaveSlot;
        private readonly SaveWhen _neededSave = Menu.NeededSave;
        private Scene? _scene;
        public Scene Scene => _scene ?? throw new ObjectDisposedException(nameof(State));

        public State()
        {
            _players = (PlayerEntity[])Players.Clone();
            try
            {
                // Snapshot and clear the static fields before constructing a
                // fresh scene. Its runtime arrays are scene-owned, while the
                // remaining fields are process-wide save and transition state
                // that must be restored after the scene is gone.
                foreach (FieldInfo field in typeof(GameState).GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    object? value = field.GetValue(null);
                    if (value is Array array) { _arrays.Add((array, (Array)array.Clone())); Array.Clear(array); }
                    else if (!field.IsInitOnly && !field.IsLiteral) { _values.Add((field, value)); }
                }
                Array.Clear(Players);
                // Reset() reads and commits the selected save. Slot zero is
                // the no-file slot; restore the caller's selection in Dispose.
                Menu.SaveSlot = 0;
                _scene = Scene.CreateHeadless();
                for (int i = 0; i < Players.Length; i++)
                {
                    Players[i] = (PlayerEntity)RuntimeHelpers.GetUninitializedObject(typeof(PlayerEntity));
                    Players[i].TeamIndex = i;
                }
                PlayerEntity.PlayerCount = 0; PlayerEntity.MainPlayerIndex = 0;
                CameraSequence.Intro = null;
            }
            catch
            {
                Dispose();
                throw;
            }
        }
        public void Activate(int slot, int team)
        {
            Players[slot].TeamIndex = team;
            Players[slot].LoadFlags = LoadFlags.Initial | LoadFlags.Active;
            PlayerEntity.PlayerCount++;
        }

        public MatchRuntime Configure(GameMode mode)
        {
            GameState.Mode = mode;
            MatchRuntime match = Scene.Match;
            match.ApplyRules(MatchRules.CreateDefault(mode.ToMatchMode(), "match-baseline"));
            return match;
        }

        public void Dispose()
        {
            Scene? scene = _scene;
            _scene = null;
            try
            {
                // Close the test scene before restoring the fields captured
                // above; teardown may still touch process-wide state.
                scene?.CloseHeadless();
            }
            finally
            {
                foreach (var entry in _arrays) { Array.Copy(entry.Copy, entry.Original, entry.Copy.Length); }
                foreach (var entry in _values) { entry.Field.SetValue(null, entry.Value); }
                Array.Copy(_players, Players, Players.Length);
                PlayerEntity.PlayerCount = _count; PlayerEntity.MainPlayerIndex = _main;
                CameraSequence.Intro = _intro;
                Read.ServerMode = _serverMode;
                Menu.SaveSlot = _saveSlot;
                Menu.PreviousSaveSlot = _previousSaveSlot;
                Menu.NeededSave = _neededSave;
            }
        }
    }
}
