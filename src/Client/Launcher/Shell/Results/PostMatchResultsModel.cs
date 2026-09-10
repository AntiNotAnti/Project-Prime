using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using FruityPrime.Server.Shared;
using MphRead.Mods.Launcher;

namespace MphRead.Mods.Launcher.Gui;

/// <summary>Outcome that can be proven from the frozen authority result.</summary>
public enum PostMatchOutcome
{
    Unknown,
    Victory,
    Defeat,
    Draw
}

/// <summary>One compact, immutable row in the results scoreboard.</summary>
public sealed record PostMatchScoreRow(
    int Rank,
    int Slot,
    string Name,
    Hunter Hunter,
    int TeamIndex,
    int Score,
    int Kills,
    int Deaths,
    int Assists,
    int Damage,
    int HeadshotKills,
    int LongestKillStreak,
    string ObjectiveSummary,
    bool IsLocal,
    bool IsBot,
    int Standing,
    int TeamStanding);

/// <summary>
/// Presentation projection of an immutable match result. It deliberately has
/// no live gameplay references and does not fill in counters absent from the
/// result supplied by the authority.
/// </summary>
public sealed record PostMatchResultsModel
{
    public string MapKey { get; init; } = "";
    public MatchMode Mode { get; init; }
    public string ModeLabel { get; init; } = "Match";
    public PostMatchOutcome Outcome { get; init; }
    public string OutcomeLabel { get; init; } = "RESULTS UNAVAILABLE";
    public string? EndReasonLabel { get; init; }
    public ImmutableArray<PostMatchScoreRow> Scoreboard { get; init; } = [];
    public int ActivePlayers { get; init; }
    public int? LocalSlot { get; init; }
    public bool HasAuthoritativeResult { get; init; }
    public bool HasLocalPlayer => Scoreboard.Any(row => row.IsLocal);
    public string? LeaderName => Scoreboard.IsDefaultOrEmpty ? null : Scoreboard[0].Name;

    public bool HasScoreboard => !Scoreboard.IsDefaultOrEmpty;
}

public enum PostMatchBallotState
{
    Loading,
    Waiting,
    Open,
    Voted,
    Closed,
    Resolved,
    Paused,
    Ended
}

/// <summary>One bounded Node-owned ballot option as shown by the results UI.</summary>
public sealed record PostMatchBallotOption(
    byte Id,
    LobbyVoteChoice Choice,
    string MapKey,
    MatchMode Mode,
    int Votes,
    string Label,
    string Description,
    bool IsOwnVote,
    bool IsLeading,
    bool IsResolved);

/// <summary>
/// Immutable ballot projection. Totals, leading state and deadlines come only
/// from <see cref="NodeRoundSnapshot"/>; no local vote count is inferred.
/// </summary>
public sealed record PostMatchBallotModel
{
    public static readonly PostMatchBallotModel Loading = new()
    {
        State = PostMatchBallotState.Loading,
        StatusText = "Waiting for the lobby…",
        LeadingText = "Loading vote options…"
    };

    public PostMatchBallotState State { get; init; }
    public ImmutableArray<PostMatchBallotOption> Options { get; init; } = [];
    public byte OwnVote { get; init; }
    public PostMatchBallotOption? OwnVoteOption { get; init; }
    public PostMatchBallotOption? LeadingOption { get; init; }
    public int LeadingVotes { get; init; }
    public bool IsTied { get; init; }
    public TimeSpan? TimeRemaining { get; init; }
    public bool HasAuthoritativeDeadline { get; init; }
    public string StatusText { get; init; } = "Waiting for the next ballot…";
    public string LeadingText { get; init; } = "No vote options are available.";
    public bool CanVote => State == PostMatchBallotState.Open && OwnVote == 0
        && !Options.IsDefaultOrEmpty;

    public static PostMatchBallotModel From(NodeRoundSnapshot? round, DateTimeOffset now)
    {
        if (round == null) return Loading;

        ImmutableArray<LobbyVoteEntry> source = round.Options.IsDefault
            ? ImmutableArray<LobbyVoteEntry>.Empty : round.Options;
        if (source.Length > 8) source = source[..8];
        if (source.IsEmpty && round.ResolvedOption is { } resolved)
            source = ImmutableArray.Create(resolved);

        byte resolvedId = round.ResolvedOption?.Id ?? 0;
        var options = ImmutableArray.CreateBuilder<PostMatchBallotOption>(source.Length);
        int leadingVotes = 0;
        for (int i = 0; i < source.Length; i++)
            leadingVotes = Math.Max(leadingVotes, Math.Max(0, source[i].Votes));

        int leadingCount = 0;
        PostMatchBallotOption? leading = null;
        PostMatchBallotOption? own = null;
        for (int i = 0; i < source.Length; i++)
        {
            LobbyVoteEntry entry = source[i];
            int votes = Math.Max(0, entry.Votes);
            bool isLeading = leadingVotes > 0 && votes == leadingVotes;
            var option = new PostMatchBallotOption(entry.Id, entry.Choice, entry.MapKey,
                entry.Mode, votes, Label(entry), Description(entry),
                entry.Id == round.OwnVote, isLeading, entry.Id == resolvedId);
            options.Add(option);
            if (isLeading)
            {
                leading ??= option;
                leadingCount++;
            }
            if (option.IsOwnVote) own = option;
        }

        TimeSpan? remaining = round.VoteDeadline is { } deadline
            ? deadline > now ? deadline - now : TimeSpan.Zero : null;
        PostMatchBallotState state = ResolveState(round, source, remaining, now);
        string status = ResolveStatus(round, own, remaining, state);
        string leadText = ResolveLeadingText(leading, leadingCount, leadingVotes);
        return new PostMatchBallotModel
        {
            State = state,
            Options = options.MoveToImmutable(),
            OwnVote = round.OwnVote,
            OwnVoteOption = own,
            LeadingOption = leading,
            LeadingVotes = leadingVotes,
            IsTied = leadingCount > 1,
            TimeRemaining = remaining,
            HasAuthoritativeDeadline = round.VoteDeadline.HasValue,
            StatusText = status,
            LeadingText = leadText
        };
    }

    public static string CountdownText(TimeSpan? remaining)
    {
        if (remaining == null) return "Voting time is not available.";
        int seconds = Math.Max(0, (int)Math.Ceiling(remaining.Value.TotalSeconds));
        return seconds == 0 ? "Voting closed." : $"Voting closes in {seconds}s";
    }

    public static string Label(LobbyVoteEntry entry) => entry.Choice switch
    {
        LobbyVoteChoice.Rematch => "Rematch",
        LobbyVoteChoice.NextMap => "Next map",
        LobbyVoteChoice.ReturnToLobby => "Return to lobby",
        LobbyVoteChoice.Map when !string.IsNullOrWhiteSpace(entry.MapKey) => entry.MapKey,
        _ => $"Option {entry.Id.ToString(CultureInfo.InvariantCulture)}"
    };

    public static string Description(LobbyVoteEntry entry) => entry.Choice switch
    {
        LobbyVoteChoice.Rematch => "Play this map again",
        LobbyVoteChoice.ReturnToLobby => "Return to the lobby together",
        LobbyVoteChoice.NextMap or LobbyVoteChoice.Map => string.IsNullOrWhiteSpace(entry.MapKey)
            ? "Continue to the next match"
            : $"{entry.MapKey} · {ModeLabel(entry.Mode)}",
        _ => "Continue to the next match"
    };

    public static string ModeLabel(MatchMode mode) => mode switch
    {
        MatchMode.Battle => "Battle",
        MatchMode.TeamBattle => "Team Battle",
        MatchMode.Survival => "Survival",
        MatchMode.TeamSurvival => "Team Survival",
        MatchMode.Capture => "Capture",
        MatchMode.Bounty => "Bounty",
        MatchMode.TeamBounty => "Team Bounty",
        MatchMode.Nodes => "Nodes",
        MatchMode.TeamNodes => "Team Nodes",
        MatchMode.Defender => "Defender",
        MatchMode.TeamDefender => "Team Defender",
        MatchMode.PrimeHunter => "Prime Hunter",
        _ => mode.ToString()
    };

    private static PostMatchBallotState ResolveState(NodeRoundSnapshot round,
        ImmutableArray<LobbyVoteEntry> options, TimeSpan? remaining, DateTimeOffset now)
    {
        if (round.TournamentEnded) return PostMatchBallotState.Ended;
        if (round.Paused) return PostMatchBallotState.Paused;
        if (round.ResolvedOption != null) return PostMatchBallotState.Resolved;
        if (options.IsEmpty || round.VoteDeadline == null) return PostMatchBallotState.Waiting;
        if (remaining <= TimeSpan.Zero || now >= round.VoteDeadline.Value) return PostMatchBallotState.Closed;
        return round.OwnVote != 0 ? PostMatchBallotState.Voted : PostMatchBallotState.Open;
    }

    private static string ResolveStatus(NodeRoundSnapshot round,
        PostMatchBallotOption? own, TimeSpan? remaining, PostMatchBallotState state)
    {
        if (state == PostMatchBallotState.Ended) return "Tournament ended.";
        if (state == PostMatchBallotState.Paused) return "Tournament paused.";
        if (state == PostMatchBallotState.Resolved && round.ResolvedOption is { } resolved)
            return resolved.Choice == LobbyVoteChoice.ReturnToLobby ? "Returning to lobby…"
                : $"Selected: {Label(resolved)} · Preparing next round…";
        if (state == PostMatchBallotState.Closed) return "Voting closed.";
        if (state == PostMatchBallotState.Waiting)
            return round.Options.IsEmpty ? "Waiting for the next ballot…" : "Waiting for the vote window…";
        if (state == PostMatchBallotState.Voted)
            return own == null
                ? $"Your vote is locked. {CountdownText(remaining)}"
                : $"Your vote: {own.Label}. Vote locked. {CountdownText(remaining)}";
        return CountdownText(remaining);
    }

    private static string ResolveLeadingText(PostMatchBallotOption? leading,
        int leadingCount, int leadingVotes)
    {
        if (leading == null || leadingVotes <= 0) return "No votes yet.";
        return leadingCount > 1
            ? $"Tied for lead at {leadingVotes} votes"
            : $"Leading: {leading.Label} · {leadingVotes} votes";
    }
}

public static class PostMatchResultsBuilder
{
    public static PostMatchResultsModel Build(MatchResultsSnapshot? snapshot, int localSlot = -1)
    {
        if (snapshot == null)
            return new PostMatchResultsModel();

        MatchResult result = snapshot.Result;
        var rows = ImmutableArray.CreateBuilder<PostMatchScoreRow>();
        var seen = new HashSet<int>();
        ImmutableArray<int> slots = result.ResultSlots.IsDefault
            ? ImmutableArray<int>.Empty : result.ResultSlots;
        for (int i = 0; i < slots.Length; i++)
        {
            int slot = slots[i];
            if (slot < 0 || slot >= result.Players.Length || !seen.Add(slot)) continue;
            PlayerMatchResult player = result.Players[slot];
            if (!player.Active) continue;
            int rank = player.Standings >= 0 && player.Standings < 8
                ? player.Standings + 1 : rows.Count + 1;
            rows.Add(new PostMatchScoreRow(rank, player.Slot, player.Nickname,
                player.Hunter, player.TeamIndex, player.Points, player.Kills,
                player.Deaths, player.Assists, player.DamageDealt,
                player.HeadshotKills, player.LongestKillStreak,
                ObjectiveSummary(result.Rules.Mode, player),
                player.Slot == localSlot && localSlot >= 0, player.IsBot,
                player.Standings, player.TeamStanding));
            if (rows.Count == 8) break;
        }

        ImmutableArray<PostMatchScoreRow> scoreboard = rows.ToImmutable();
        int? resolvedLocalSlot = localSlot is >= 0 and < 8 ? localSlot : null;
        PostMatchOutcome outcome = ResolveOutcome(result, scoreboard, localSlot);
        string mapKey = !string.IsNullOrWhiteSpace(result.Rules.RoomKey)
            ? result.Rules.RoomKey : snapshot.MapKey;
        return new PostMatchResultsModel
        {
            MapKey = mapKey,
            Mode = result.Rules.Mode,
            ModeLabel = PostMatchBallotModel.ModeLabel(result.Rules.Mode),
            Outcome = outcome,
            OutcomeLabel = OutcomeLabel(outcome, hasResult: true),
            EndReasonLabel = EndReasonLabel(result.EndReason),
            Scoreboard = scoreboard,
            ActivePlayers = result.ActivePlayers,
            LocalSlot = resolvedLocalSlot,
            HasAuthoritativeResult = true
        };
    }

    public static string OutcomeLabel(PostMatchOutcome outcome, bool hasResult)
        => !hasResult ? "RESULTS UNAVAILABLE" : outcome switch
        {
            PostMatchOutcome.Victory => "VICTORY",
            PostMatchOutcome.Defeat => "DEFEAT",
            PostMatchOutcome.Draw => "DRAW",
            _ => "MATCH COMPLETE"
        };

    public static string? EndReasonLabel(MatchEndReason reason) => reason switch
    {
        MatchEndReason.TimeLimit => "Time limit",
        MatchEndReason.ScoreGoal => "Score goal",
        MatchEndReason.ObjectiveTimeGoal => "Objective goal",
        MatchEndReason.Survival => "Last hunter standing",
        MatchEndReason.Forced => "Match ended by host",
        MatchEndReason.CompletionMessage => "Completion message",
        MatchEndReason.InvalidTeams => "Invalid team setup",
        _ => null
    };

    private static PostMatchOutcome ResolveOutcome(MatchResult result,
        ImmutableArray<PostMatchScoreRow> rows, int localSlot)
    {
        if (result.EndReason == MatchEndReason.InvalidTeams || rows.IsEmpty)
            return PostMatchOutcome.Unknown;
        PostMatchScoreRow? local = rows.FirstOrDefault(row => row.Slot == localSlot);
        if (local == null) return PostMatchOutcome.Unknown;
        PostMatchScoreRow first = rows[0];
        bool teamMode = result.Rules.Mode.IsTeamMode();
        if (teamMode)
            return ResolveTeamOutcome(result, rows, local);

        int winningStanding = first.Standing;
        int localStanding = local.Standing;
        if (winningStanding < 0 || localStanding < 0) return PostMatchOutcome.Unknown;
        if (localStanding > winningStanding) return PostMatchOutcome.Defeat;
        if (localStanding < winningStanding) return PostMatchOutcome.Unknown;
        return rows.Count(row => row.Standing == winningStanding) > 1
            ? PostMatchOutcome.Draw : PostMatchOutcome.Victory;
    }

    private static PostMatchOutcome ResolveTeamOutcome(MatchResult result,
        ImmutableArray<PostMatchScoreRow> rows, PostMatchScoreRow local)
    {
        var teams = rows.GroupBy(row => row.TeamIndex).ToArray();
        if (teams.Length < 2 || teams.Any(team => team.Key is < 0 or > 7
                || team.Select(row => row.Standing).Distinct().Count() != 1
                || team.Any(row => row.Standing is < 0 or > 7)))
            return PostMatchOutcome.Unknown;

        var teamResults = new List<(int TeamIndex, TeamMatchResult Result, int Standing)>();
        foreach (var team in teams)
        {
            if (!TryGetTeamResult(result.Teams, team.Key, out TeamMatchResult teamResult))
                return PostMatchOutcome.Unknown;
            teamResults.Add((team.Key, teamResult, team.First().Standing));
        }

        // MatchLogic writes the team rank into player Standings. TeamStanding
        // is the intra-team display ordering, and can legitimately differ
        // between teammates. ResultSlots can also carry a deterministic
        // display tie-break, so those fields are not used to decide a tied
        // team result.
        int topStanding = teamResults.Min(team => team.Standing);
        var topTeams = teamResults.Where(team => team.Standing == topStanding).ToArray();
        if (!StandingsAgreeWithTeamMetrics(result.Rules.Mode, teamResults))
            return PostMatchOutcome.Unknown;
        if (topTeams.Length > 1)
            return PostMatchOutcome.Draw;

        int winningTeam = topTeams[0].TeamIndex;
        return local.TeamIndex == winningTeam
            ? PostMatchOutcome.Victory : PostMatchOutcome.Defeat;
    }

    private static bool TryGetTeamResult(ImmutableArray<TeamMatchResult> results,
        int teamIndex, out TeamMatchResult team)
    {
        if (!results.IsDefault)
        {
            foreach (TeamMatchResult candidate in results)
            {
                if (candidate.TeamIndex == teamIndex)
                {
                    team = candidate;
                    return true;
                }
            }
        }
        team = null!;
        return false;
    }

    private static bool StandingsAgreeWithTeamMetrics(MatchMode mode,
        List<(int TeamIndex, TeamMatchResult Result, int Standing)> teams)
    {
        for (int i = 0; i < teams.Count; i++)
        {
            for (int j = i + 1; j < teams.Count; j++)
            {
                if (!TryCompareTeams(mode, teams[i].Result, teams[j].Result, out int metrics))
                    return false;
                int standings = teams[i].Standing < teams[j].Standing ? 1
                    : teams[i].Standing > teams[j].Standing ? -1 : 0;
                if (metrics != standings) return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Mirrors the authority's team comparator for the immutable team totals.
    /// A positive value means the left team places higher. Unsupported or
    /// non-finite authority data is rejected instead of being ordered locally.
    /// </summary>
    private static bool TryCompareTeams(MatchMode mode, TeamMatchResult left,
        TeamMatchResult right, out int comparison)
    {
        comparison = 0;
        if (!float.IsFinite(left.Time) || !float.IsFinite(right.Time)) return false;
        float leftTime = (mode is MatchMode.Survival or MatchMode.TeamSurvival)
            && left.Time == -1 ? float.MaxValue : left.Time;
        float rightTime = (mode is MatchMode.Survival or MatchMode.TeamSurvival)
            && right.Time == -1 ? float.MaxValue : right.Time;
        switch (mode)
        {
            case MatchMode.TeamBattle:
                comparison = CompareHigher(left.Points, right.Points);
                if (comparison == 0) comparison = CompareLower(left.Deaths, right.Deaths);
                return true;
            case MatchMode.TeamSurvival:
                comparison = CompareHigher(leftTime, rightTime);
                if (comparison == 0) comparison = CompareLower(left.Deaths, right.Deaths);
                return true;
            case MatchMode.TeamDefender:
                comparison = CompareHigher(leftTime, rightTime);
                if (comparison == 0) comparison = CompareHigher(left.Kills, right.Kills);
                return true;
            case MatchMode.Capture:
            case MatchMode.TeamNodes:
                comparison = CompareHigher(left.Points, right.Points);
                if (comparison == 0) comparison = CompareHigher(left.Kills, right.Kills);
                return true;
            case MatchMode.TeamBounty:
                // MatchLogic.CompareTeams currently returns 0 for this mode;
                // preserve that observed authority behavior in presentation.
                comparison = 0;
                return true;
            default:
                return false;
        }
    }

    private static int CompareHigher(int left, int right)
        => left > right ? 1 : left < right ? -1 : 0;

    private static int CompareHigher(float left, float right)
        => left > right ? 1 : left < right ? -1 : 0;

    private static int CompareLower(int left, int right)
        => left < right ? 1 : left > right ? -1 : 0;

    private static string ObjectiveSummary(MatchMode mode, PlayerMatchResult player)
    {
        return mode switch
        {
            MatchMode.Bounty or MatchMode.TeamBounty or MatchMode.Capture
                => $"Octoliths {player.OctolithScores} scored · {player.OctolithDrops} dropped · {player.OctolithStops} stopped",
            MatchMode.Nodes or MatchMode.TeamNodes or MatchMode.Defender or MatchMode.TeamDefender
                => $"Nodes {player.NodesCaptured} captured · {player.NodesLost} lost",
            MatchMode.PrimeHunter => PrimeTimeSummary(player),
            _ => $"Suicides {player.Suicides} · team kills {player.FriendlyKills}"
        };
    }

    private static string PrimeTimeSummary(PlayerMatchResult player)
    {
        string time = player.Time < 0 ? "Prime time unavailable" : $"Prime time {player.Time:0.0}s";
        return $"{time} · {player.KillsAsPrime} kills · {player.PrimesKilled} stopped";
    }
}
