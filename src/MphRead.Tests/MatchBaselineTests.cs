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
            Enum.GetValues<GameMode>().Where(GameState.IsTeamMode));
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
        GameState.Mode = mode;
        GameState.Points[0] = GameState.Points[1] = 3;
        GameState.Time[0] = GameState.Time[1] = 20;
        Assert.Equal(0, Compare("ComparePlayers"));
        if (deaths) { GameState.Deaths[1] = 2; }
        else { GameState.Kills[0] = 2; }
        Assert.Equal(1, Compare("ComparePlayers"));
        Assert.Equal(-1, Compare("ComparePlayers", 1, 0));
        if (timed) { GameState.Time[1] = 21; }
        else { GameState.Points[1] = 4; }
        Assert.Equal(-1, Compare("ComparePlayers"));
        if (mode is GameMode.Survival or GameMode.SurvivalTeams)
        {
            GameState.Time[0] = -1;
            Assert.Equal(1, Compare("ComparePlayers"));
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
        GameState.Mode = mode;
        Assert.Equal(0, Compare("CompareTeams"));
        if (deaths) { GameState.TeamDeaths[1] = 1; }
        else { GameState.TeamKills[0] = 1; }
        Assert.Equal(1, Compare("CompareTeams"));
        if (timed) { GameState.TeamTime[1] = 10; }
        else { GameState.TeamPoints[1] = 10; }
        Assert.Equal(-1, Compare("CompareTeams"));
    }

    [Fact]
    public void BountyTeamsCurrentlyComparesAllTeamsAsTied()
    {
        using var state = new State();
        GameState.Mode = GameMode.BountyTeams;
        GameState.TeamPoints[0] = 100;
        GameState.TeamKills[0] = 50;
        GameState.TeamTime[0] = 30;
        GameState.TeamDeaths[1] = 20;
        Assert.Equal(0, Compare("CompareTeams"));
        Assert.Equal(0, Compare("CompareTeams", 1, 0));
    }

    [Theory]
    [InlineData(GameMode.Battle)] [InlineData(GameMode.BattleTeams)]
    [InlineData(GameMode.Capture)] [InlineData(GameMode.Bounty)]
    [InlineData(GameMode.BountyTeams)] [InlineData(GameMode.Nodes)] [InlineData(GameMode.NodesTeams)]
    public void PointModesDispatchAndClampOnlyTheWinningActiveTeam(GameMode mode)
    {
        using var state = new State();
        GameState.Mode = mode;
        GameState.Setup(null!); // No intro camera or active players during setup.
        state.Activate(0, 0);
        GameState.PointGoal = 5;
        GameState.TeamPoints[1] = 20; // An inactive team's score cannot end the round.
        GameState.ModeState(null!);
        Assert.True(GameState.MatchTime > 0);
        GameState.TeamPoints[0] = 4;
        GameState.ModeState(null!);
        Assert.True(GameState.MatchTime > 0);
        GameState.TeamPoints[0] = 8;
        GameState.ModeState(null!);
        Assert.Equal(0, GameState.MatchTime);
        Assert.Equal(5, GameState.TeamPoints[0]);
        Assert.Equal(20, GameState.TeamPoints[1]);
        GameState.MatchTime = 30;
        GameState.PointGoal = 0;
        GameState.ModeState(null!);
        Assert.Equal(30, GameState.MatchTime);
    }

    [Theory]
    [InlineData(GameMode.Defender)] [InlineData(GameMode.DefenderTeams)]
    public void DefenderDispatchEndsAtTimeGoalWithoutClamping(GameMode mode)
    {
        using var state = new State();
        GameState.Mode = mode;
        GameState.Setup(null!);
        state.Activate(0, 0);
        GameState.TeamTime[0] = 89;
        GameState.ModeState(null!);
        Assert.Equal(900, GameState.MatchTime);
        GameState.TeamTime[0] = 91;
        GameState.ModeState(null!);
        Assert.Equal(0, GameState.MatchTime);
        Assert.Equal(91, GameState.TeamTime[0]);
    }

    [Fact]
    public void UpdateStateAggregatesInitialSlotsButOnlyRanksActiveSlotsAndKeepsCompetitionTies()
    {
        using var state = new State();
        GameState.Mode = GameMode.Battle;
        state.Activate(0, 0); state.Activate(1, 1); state.Activate(2, 2);
        state.Players[3].LoadFlags = LoadFlags.Initial;
        state.Players[3].TeamIndex = 0;
        GameState.Points[0] = GameState.Points[1] = 5;
        GameState.Points[2] = 3; GameState.Points[3] = 7;
        GameState.Kills[3] = 4; GameState.Deaths[3] = 2;
        GameState.UpdateState();
        Assert.Equal(12, GameState.TeamPoints[0]);
        Assert.Equal(4, GameState.TeamKills[0]);
        Assert.Equal(2, GameState.TeamDeaths[0]);
        Assert.Equal(3, GameState.ActivePlayers);
        Assert.Equal(new[] { 0, 1, 2 }, GameState.ResultSlots.Take(3));
        Assert.Equal(new[] { 0, 0, 2 }, GameState.Standings.Take(3));
        Assert.Equal(PlayerEntity.SlotCapacity - 1, GameState.Standings[3]);
        GameState.UpdateState();
        Assert.Equal(12, GameState.TeamPoints[0]); // Recomputed, not accumulated each frame.
    }

    [Fact]
    public void TeamStandingsCurrentlyWritesFinalRankToActiveCountIndexInsteadOfSparseSlot()
    {
        using var state = new State();
        GameState.Mode = GameMode.BattleTeams; GameState.Teams = true;
        state.Activate(0, 0); state.Activate(3, 1);
        GameState.Points[0] = 5; GameState.Points[3] = 1;
        GameState.UpdateState();
        Assert.Equal(new[] { 0, 3 }, GameState.ResultSlots.Take(2));
        Assert.Equal(0, GameState.Standings[0]);
        Assert.Equal(1, GameState.Standings[1]); // Current index/slot mismatch is deliberately frozen for R0.
        Assert.Equal(PlayerEntity.SlotCapacity - 1, GameState.Standings[3]);
    }

    [Theory]
    [InlineData(GameMode.Survival)] [InlineData(GameMode.SurvivalTeams)]
    public void SurvivalDispatchCountsSpareLivesAndMarksTheRemainingSurvivor(GameMode mode)
    {
        using var state = new State();
        GameState.Mode = mode;
        GameState.Setup(null!);
        state.Activate(0, 0); state.Activate(1, 1);
        Scene scene = Clock(0.5f);
        GameState.TeamDeaths[0] = GameState.TeamDeaths[1] = 2;
        GameState.ModeState(scene); // Dead players still have their final life at equality.
        Assert.Equal(900, GameState.MatchTime);
        Assert.Equal(0.5f, GameState.Time[0]);
        GameState.TeamDeaths[1] = 3;
        GameState.ModeState(scene);
        Assert.Equal(0, GameState.MatchTime);
        Assert.Equal(-1, GameState.Time[0]);
        Assert.Equal(0.5f, GameState.Time[1]);
    }

    [Fact]
    public void PrimeDispatchAccumulatesHolderTimeAndDropsAnInactiveHolder()
    {
        using var state = new State();
        GameState.Mode = GameMode.PrimeHunter;
        GameState.Setup(null!);
        state.Activate(0, 0);
        Scene scene = Clock(0.5f); // Frame 1 avoids the independent periodic damage path.
        GameState.PrimeHunter = -1;
        GameState.ModeState(scene);
        Assert.Equal(0, GameState.Time[0]);
        GameState.PrimeHunter = 0;
        GameState.Time[0] = 89.5f;
        GameState.ModeState(scene);
        Assert.Equal(90, GameState.Time[0]);
        Assert.Equal(0, GameState.MatchTime);
        state.Players[0].LoadFlags = LoadFlags.Initial;
        GameState.ModeState(scene);
        Assert.Equal(-1, GameState.PrimeHunter);
        Assert.Equal(90, GameState.Time[0]);
    }

    [Fact]
    public void SurvivalAggregationCurrentlyIgnoresNegativeSurvivorSentinel()
    {
        using var state = new State();
        GameState.Mode = GameMode.SurvivalTeams; GameState.Teams = true;
        state.Activate(0, 0); state.Activate(1, 1);
        GameState.Time[0] = -1; GameState.Time[1] = 20;
        GameState.UpdateState();
        Assert.Equal(0, GameState.TeamTime[0]);
        Assert.Equal(20, GameState.TeamTime[1]);
        Assert.Equal(1, Compare("ComparePlayers"));
        Assert.Equal(-1, Compare("CompareTeams"));
    }

    [Fact]
    public void MatchClockClampsAtZeroAndPreservesUnlimitedSentinel()
    {
        using var state = new State();
        Scene scene = Clock(0.5f);
        GameState.MatchTime = 0.25f;
        GameState.UpdateTime(scene);
        Assert.Equal(0, GameState.MatchTime);
        GameState.MatchTime = -1;
        GameState.UpdateTime(scene);
        Assert.Equal(-1, GameState.MatchTime);
    }

    private static Scene Clock(float delta)
    {
        var scene = (Scene)RuntimeHelpers.GetUninitializedObject(typeof(Scene));
        typeof(Scene).GetField("_frameTime", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(scene, delta);
        typeof(Scene).GetField("_frameCount", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(scene, 1UL);
        return scene;
    }

    [Fact]
    public void RotationProgressResetClearsLifecycleFlagsButPreservesScoresRulesAndClock()
    {
        using var state = new State();
        GameState.MatchState = MatchState.Ending;
        GameState.ForceEndGame = true;
        GameState.PointGoal = 17; GameState.MatchTime = 33; GameState.Points[0] = 9;
        foreach (string name in new[] { "_tempoChanged", "_stateChanged" }) { SetMember(name, true); }
        foreach (string name in new[] { "_matchEndTime", "_lastAlarmTime" }) { SetMember(name, 12f); }
        SetMember("_nextAlarmIndex", 3);
        GameState.ResetMatchProgress();
        Assert.Equal(MatchState.InProgress, GameState.MatchState);
        Assert.False(GameState.ForceEndGame);
        Assert.Equal(17, GameState.PointGoal); Assert.Equal(33, GameState.MatchTime); Assert.Equal(9, GameState.Points[0]);
        foreach (string name in new[] { "_tempoChanged", "_stateChanged" }) { Assert.Equal(false, GetMember(name)); }
        foreach (string name in new[] { "_matchEndTime", "_lastAlarmTime" }) { Assert.Equal(0f, GetMember(name)); }
        Assert.Equal(0, GetMember("_nextAlarmIndex"));
    }

    private static MemberInfo Member(string name)
    {
        return (MemberInfo?)typeof(GameState).GetProperty(name,
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? typeof(GameState).GetField(name, BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new MissingMemberException(typeof(GameState).FullName, name);
    }

    private static object? GetMember(string name) => Member(name) switch
    {
        PropertyInfo property => property.GetValue(null),
        FieldInfo field => field.GetValue(null),
        _ => throw new InvalidOperationException($"Unsupported member kind: {name}")
    };

    private static void SetMember(string name, object value)
    {
        switch (Member(name))
        {
            case PropertyInfo property:
                property.SetValue(null, value);
                break;
            case FieldInfo field:
                field.SetValue(null, value);
                break;
            default:
                throw new InvalidOperationException($"Unsupported member kind: {name}");
        }
    }

    private static int Compare(string method, int first = 0, int second = 1) => (int)typeof(GameState)
        .GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new object[] { first, second })!;

    // No data, sound, save files or graphics initialization. Restore every touched global even on assertion failure.
    private sealed class State : IDisposable
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

        public State()
        {
            _players = (PlayerEntity[])Players.Clone();
            try
            {
                // Snapshot and clear the static fields before binding a fresh
                // scene. The runtime arrays are scene-owned in R3, while the
                // old fields still include process-wide save and transition
                // state that must be restored after the scene is gone.
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
                GameState.Teams = false; GameState.PointGoal = 1000; GameState.MatchTime = 60;
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
        public void Dispose()
        {
            Scene? scene = _scene;
            _scene = null;
            try
            {
                // Unbind the test owner before restoring the fields captured
                // above; otherwise restoration would target its discarded
                // runtime arrays.
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
