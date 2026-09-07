using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using MphRead.Entities;
using MphRead.Identity;
using MphRead.Mods.Network;

namespace MphRead.Reporting;

/// <summary>Simulation-owner history. Capture before slot clearing; no disk/HTTP work.</summary>
public sealed class MatchParticipantLedger
{
    private sealed class Participant
    {
        public Guid Id = Guid.NewGuid();
        public PlayerId? PlayerId;
        public ParticipantKind Kind;
        public string Name = "";
        public bool Started;
        public ulong Connection;
        public uint Played;
        public uint LastPlayedExclusive;
        public bool HasPlayed;
        public MatchReportMetrics Metrics = null!;
        public MatchReportMetrics? PriorCounters;
        public readonly List<MatchParticipationSpan> Spans = new();
        public bool Connected;
        public ParticipantOutcome? TerminalOutcome;
        public ParticipantOutcomeReason OutcomeReason;
    }
    private readonly Participant?[] _slots = new Participant?[8];
    private readonly List<Participant> _participants = new();
    private readonly Guid _server, _incarnation;
    private readonly string _build;
    private readonly Func<DateTimeOffset> _utc;
    private Guid _match;
    private DateTimeOffset _started;
    private uint _played;
    public Guid MatchId => _match;
    public bool Started { get; private set; }
    public bool CapacityAvailable => _participants.Count < MatchReportV1.MaximumParticipants - 8
        && !_participants.Exists(p => p.Spans.Count >= 255);
    private string? _tournamentId, _roundId;
    private Guid? _replayId;
    public void ConfigureRoundIdentity(string? tournamentId, string? roundId, Guid? replayId)
    {
        if (Started) throw new InvalidOperationException("Round identity is frozen when play begins.");
        if ((tournamentId == null) != (roundId == null) || tournamentId != null && !MatchReportV1.ValidExternalId(tournamentId)
            || roundId != null && !MatchReportV1.ValidExternalId(roundId) || replayId == Guid.Empty)
            throw new ArgumentException("Invalid tournament identity.");
        _tournamentId = tournamentId; _roundId = roundId; _replayId = replayId;
    }
    public MatchReportV1? Report { get; private set; }
    public string? Failure { get; private set; }
    public MatchParticipantLedger(Guid server, Guid incarnation, string build, Func<DateTimeOffset>? utc = null)
    { _server = server; _incarnation = incarnation; _build = build; _utc = utc ?? (() => DateTimeOffset.UtcNow); }

    public void BeginPlaying(Scene scene, ServerNetwork network, uint tick)
    {
        if (Report != null) return;
        bool first = !Started;
        if (first) { Started = true; _match = Guid.NewGuid(); _started = _utc().ToUniversalTime(); }
        foreach (ServerPeer? peer in network.Peers)
            if (peer is { WaitingForNextMatch: false } && peer.Connection.State == NetConnectionState.Playing)
            { Activate(scene, peer, tick, first); }
    }
    public void RecordPlayedStep(uint tick)
    {
        if (!Started || Report != null) return;
        _played++;
        foreach (Participant? participant in _slots)
            if (participant is { Connected: true })
            {
                participant.Played++; participant.LastPlayedExclusive = unchecked(tick + 1); participant.HasPlayed = true;
                int last = participant.Spans.Count - 1;
                participant.Spans[last] = participant.Spans[last] with { PlayedTicks = participant.Spans[last].PlayedTicks + 1 };
            }
    }
    public void Activate(Scene scene, ServerPeer peer, uint tick, bool starting = false)
    {
        if (!Started || Report != null || peer.WaitingForNextMatch) return;
        Participant? current = _slots[peer.Slot];
        if (current?.Connection == peer.Connection.Id && current.Connected) return;
        Participant? returning = null;
        if (peer.PlayerId.HasValue)
            returning = _participants.Find(p => !p.Connected && p.PlayerId == peer.PlayerId);
        else if (peer.ReturningParticipant)
            returning = _participants.Find(p => !p.Connected && p.Connection == peer.ReturningFromConnectionId);
        // An authoritative forfeit is terminal. A later admission of the same
        // account cannot turn it back into the official participant.
        if (returning?.TerminalOutcome == ParticipantOutcome.Forfeited) return;
        if (returning != null && !peer.ReturningParticipant) returning.PriorCounters = returning.Metrics;
        if (returning == null)
        {
            if (_participants.Count == MatchReportV1.MaximumParticipants) { Failure = "Participant ledger capacity exhausted"; return; }
            returning = new Participant { PlayerId = peer.IsBot ? null : peer.PlayerId, Kind = peer.IsBot ? ParticipantKind.Bot : peer.PlayerId.HasValue ? ParticipantKind.RegisteredHuman : ParticipantKind.Guest,
                Name = peer.Name, Started = starting };
            _participants.Add(returning);
        }
        if (returning.Spans.Count == 256) { Failure = "Participant interval capacity exhausted"; return; }
        for (int slot = 0; slot < _slots.Length; slot++) if (ReferenceEquals(_slots[slot], returning)) _slots[slot] = null;
        returning.Connection = peer.Connection.Id; returning.Connected = true;
        returning.Spans.Add(new(tick, null, null, peer.Slot, peer.Hunter, peer.TeamIndex));
        returning.Metrics = Merge(returning.PriorCounters, Snapshot(scene, peer.Slot, returning.Name));
        _slots[peer.Slot] = returning;
    }
    public void ActivateBot(Scene scene, BotParticipant bot, uint tick)
    {
        if (!Started || Report != null || _slots[bot.Slot] is { Connected: true } current && current.Connection == bot.Identity) return;
        if (_participants.Count == MatchReportV1.MaximumParticipants) { Failure = "Participant ledger capacity exhausted"; return; }
        var participant = new Participant { Kind = ParticipantKind.Bot, Name = bot.Name, Connection = bot.Identity,
            Connected = true, Started = _played == 0 };
        participant.Spans.Add(new(tick, null, null, bot.Slot, bot.Hunter, bot.TeamIndex));
        participant.Metrics = Snapshot(scene, bot.Slot, bot.Name);
        _participants.Add(participant); _slots[bot.Slot] = participant;
    }
    public void Leave(Scene scene, ServerPeer peer, ParticipantExitReason reason, uint tick)
        => LeaveSlot(scene, peer.Slot, tick, reason, peer.Connection.Id);
    public void LeaveSlot(Scene scene, int slot, uint tick, ParticipantExitReason reason = ParticipantExitReason.Disconnected, ulong connection = 0)
    {
        Participant? participant = _slots[slot];
        if (Report != null || participant == null || connection != 0 && participant.Connection != connection) return;
        if (participant.Connected)
        {
            participant.Metrics = Merge(participant.PriorCounters, Snapshot(scene, slot, participant.Name));
            participant.Connected = false;
            int last = participant.Spans.Count - 1;
            participant.Spans[last] = participant.Spans[last] with { LeftTick = tick, ExitReason = reason };
        }
        else if (reason != ParticipantExitReason.ReconnectGraceExpired) return;

        if (participant.Started && reason == ParticipantExitReason.ExplicitLeave)
        {
            participant.TerminalOutcome = ParticipantOutcome.Forfeited;
            participant.OutcomeReason = ParticipantOutcomeReason.ExplicitLeave;
        }
        else if (participant.Started && reason == ParticipantExitReason.ReconnectGraceExpired)
        {
            participant.TerminalOutcome = ParticipantOutcome.Forfeited;
            participant.OutcomeReason = ParticipantOutcomeReason.ReconnectGraceExpired;
        }
        else if (!participant.Started && reason is ParticipantExitReason.ExplicitLeave or ParticipantExitReason.ReconnectGraceExpired)
        {
            participant.TerminalOutcome = ParticipantOutcome.Departed;
            participant.OutcomeReason = ParticipantOutcomeReason.DepartedOutsideOfficialRoster;
        }
    }
    public MatchReportV1? Complete(Scene scene, uint tick)
    {
        if (Report != null || !Started || scene.Match.Result is not { } result || Failure != null) return Report;
        var participants = ImmutableArray.CreateBuilder<MatchReportParticipant>(_participants.Count);
        foreach (Participant participant in _participants)
        {
            ParticipantOutcome outcome;
            ParticipantOutcomeReason outcomeReason;
            if (participant.Connected)
            {
                int last = participant.Spans.Count - 1;
                var span = participant.Spans[last];
                participant.Metrics = Merge(participant.PriorCounters, Convert(result.Players[span.Slot]));
                participant.Spans[last] = span with { LeftTick = participant.HasPlayed && Sequence32.IsNewer(participant.LastPlayedExclusive, tick)
                    ? participant.LastPlayedExclusive : tick, ExitReason = ParticipantExitReason.Completed };
                outcome = ParticipantOutcome.Finished;
                outcomeReason = ParticipantOutcomeReason.Completed;
            }
            else if (participant.TerminalOutcome.HasValue)
            {
                outcome = participant.TerminalOutcome.Value;
                outcomeReason = participant.OutcomeReason;
            }
            else if (participant.Started)
            {
                outcome = ParticipantOutcome.Forfeited;
                outcomeReason = ParticipantOutcomeReason.MatchCompletedWhileDisconnected;
            }
            else
            {
                outcome = ParticipantOutcome.Departed;
                outcomeReason = ParticipantOutcomeReason.DepartedOutsideOfficialRoster;
            }
            participants.Add(new(participant.Id, participant.PlayerId, participant.Kind, participant.Name, participant.Started,
                outcome, participant.Played, participant.Spans.ToImmutableArray(), participant.Metrics, outcomeReason));
        }
        DateTimeOffset ended = _utc().ToUniversalTime();
        if (ended < _started) ended = _started;
        Report = new(MatchReportV1.CurrentSchema, _match, result.MatchId, _server, _incarnation, _build,
            NetHeader.Version, null, MatchTrustClass.Community, result.Rules.RulesetPreset.ToString(), result.Rules.RulesetPreset == RulesetPreset.Duel ? "Duel" : null, result.Rules, _started,
            ended, _played, result.EndReason, participants.MoveToImmutable(), _tournamentId, _roundId, _replayId);
        return Report;
    }
    private static MatchReportMetrics Merge(MatchReportMetrics? prior, MatchReportMetrics current)
    {
        if (prior == null) return current;
        var beams = ImmutableArray.CreateBuilder<int>(9);
        for (int i = 0; i < 9; i++) beams.Add(checked(prior.BeamKills[i] + current.BeamKills[i]));
        return current with
        {
            Points = checked(prior.Points + current.Points), Kills = checked(prior.Kills + current.Kills),
            Deaths = checked(prior.Deaths + current.Deaths), Assists = checked(prior.Assists + current.Assists),
            DamageDealt = checked(prior.DamageDealt + current.DamageDealt), LongestKillStreak = Math.Max(prior.LongestKillStreak, current.LongestKillStreak),
            HeadshotKills = checked(prior.HeadshotKills + current.HeadshotKills), Suicides = checked(prior.Suicides + current.Suicides),
            FriendlyKills = checked(prior.FriendlyKills + current.FriendlyKills), OctolithScores = checked(prior.OctolithScores + current.OctolithScores),
            OctolithDrops = checked(prior.OctolithDrops + current.OctolithDrops), OctolithStops = checked(prior.OctolithStops + current.OctolithStops),
            NodesCaptured = checked(prior.NodesCaptured + current.NodesCaptured), NodesLost = checked(prior.NodesLost + current.NodesLost),
            KillsAsPrime = checked(prior.KillsAsPrime + current.KillsAsPrime), PrimesKilled = checked(prior.PrimesKilled + current.PrimesKilled),
            ModeTimeSeconds = current.ModeTimeSeconds < 0 ? current.ModeTimeSeconds : Math.Max(0, prior.ModeTimeSeconds) + current.ModeTimeSeconds,
            BeamDamageMax = checked(prior.BeamDamageMax + current.BeamDamageMax), BeamDamageDealt = checked(prior.BeamDamageDealt + current.BeamDamageDealt),
            DamageCount = checked(prior.DamageCount + current.DamageCount), AltDamageCount = checked(prior.AltDamageCount + current.AltDamageCount),
            BipedKills = prior.BipedKills.HasValue && current.BipedKills.HasValue ? checked(prior.BipedKills.Value + current.BipedKills.Value) : null,
            AltFormKills = prior.AltFormKills.HasValue && current.AltFormKills.HasValue ? checked(prior.AltFormKills.Value + current.AltFormKills.Value) : null,
            BeamKills = beams.MoveToImmutable()
        };
    }
    private static MatchReportMetrics Snapshot(Scene scene, int slot, string name)
        => Convert(new PlayerMatchResult(scene.Match, slot, PlayerEntity.Players[slot], name));
    public static MatchReportMetrics Convert(PlayerMatchResult p) => new(p.Standings, p.TeamStanding, p.Points,
        p.Kills, p.Deaths, p.Assists, p.DamageDealt, p.LongestKillStreak, p.HeadshotKills, p.Suicides,
        p.FriendlyKills, p.OctolithScores, p.OctolithDrops, p.OctolithStops, p.NodesCaptured, p.NodesLost,
        p.KillsAsPrime, p.PrimesKilled, p.Time, p.BeamDamageMax, p.BeamDamageDealt, p.DamageCount,
        p.AltDamageCount, p.KillStreak, p.BeamKills, p.BipedKills, p.AltFormKills);
}
