using System;
using MphRead.Combat;
using MphRead.Mods.Network;

namespace MphRead;

/// <summary>
/// Deterministic presentation director. It consumes an immutable observation
/// context and only chooses a focus; camera movement remains camera-owned.
/// </summary>
public sealed class BroadcastDirector
{
    public const uint CadenceTicks = 6; // 10 Hz on the fixed 60 Hz timeline.
    public const uint MinimumShotTicks = 150; // 2.5 seconds.
    public const uint MaximumShotTicks = 480; // 8 seconds.
    public const uint RecentTargetTicks = 600;
    public const int SwitchThreshold = 25;

    private readonly uint[] _recentPlayers = new uint[8];
    private readonly int[] _recentObjectives = new int[16];
    private readonly uint[] _recentObjectiveTicks = new uint[16];
    private int _recentObjectiveHead;
    private bool _hasTick;
    private bool _hasMatch;
    private uint _matchId;
    private uint _lastTick, _lastEvaluationTick, _shotStartedTick;
    private bool _manualLock;

    public BroadcastFocus Focus { get; private set; }
    public bool IsManualLock => _manualLock;

    public void Reset()
    {
        Array.Clear(_recentPlayers);
        Array.Clear(_recentObjectives);
        Array.Clear(_recentObjectiveTicks);
        _recentObjectiveHead = 0;
        _hasTick = false;
        _hasMatch = false;
        _matchId = 0;
        _lastTick = _lastEvaluationTick = _shotStartedTick = 0;
        _manualLock = false;
        Focus = BroadcastFocus.None;
    }

    public bool Lock(BroadcastFocus focus, ObservationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        BeginContext(context);
        if (!context.IsValidFocus(focus)) return false;
        SwitchTo(focus, context.DeliveredTick);
        _manualLock = true;
        return true;
    }

    public BroadcastFocus Resume(ObservationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _manualLock = false;
        _lastEvaluationTick = unchecked(context.DeliveredTick - CadenceTicks);
        return Update(context);
    }

    public BroadcastFocus Update(ObservationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        BeginContext(context);
        uint tick = context.DeliveredTick;
        if (_hasTick && Sequence32.IsNewer(_lastTick, tick))
        {
            Reset();
            _hasMatch = true;
            _matchId = context.MatchId;
        }
        _lastTick = tick;

        if (_manualLock)
        {
            if (context.IsValidFocus(Focus)) { _hasTick = true; return Focus; }
            _manualLock = false;
            Focus = BroadcastFocus.None;
        }

        if (_hasTick && !Elapsed(tick, _lastEvaluationTick, CadenceTicks)) return Focus;
        _hasTick = true;
        _lastEvaluationTick = tick;

        Candidate best = Best(context, exclude: BroadcastFocus.None);
        if (!best.Focus.Equals(default) && !context.IsValidFocus(Focus))
        {
            SwitchTo(best.Focus, tick);
            return Focus;
        }
        if (best.Focus.Equals(default))
        {
            Focus = BroadcastFocus.None;
            return Focus;
        }
        if (best.Focus == Focus) return Focus;

        uint shotAge = unchecked(tick - _shotStartedTick);
        Candidate current = Score(context, Focus);
        bool eventOverride = best.Override && best.Score >= current.Score + SwitchThreshold;
        if (shotAge < MinimumShotTicks && !eventOverride) return Focus;
        if (shotAge >= MaximumShotTicks)
        {
            Candidate alternate = Best(context, Focus);
            if (!alternate.Focus.Equals(default)) best = alternate;
        }
        if (best.Focus != Focus && (shotAge >= MaximumShotTicks
            || eventOverride || best.Score >= current.Score + SwitchThreshold))
            SwitchTo(best.Focus, tick);
        return Focus;
    }

    private void BeginContext(ObservationContext context)
    {
        if (_hasMatch && _matchId != context.MatchId) Reset();
        _hasMatch = true;
        _matchId = context.MatchId;
    }

    internal int Interest(ObservationContext context, BroadcastFocus focus)
        => Score(context, focus).Score;

    private Candidate Best(ObservationContext context, BroadcastFocus exclude)
    {
        Candidate best = default;
        foreach (ObservationPlayer player in context.Players)
        {
            if (!player.Selectable) continue;
            Candidate candidate = Score(context, BroadcastFocus.Player(player.Slot));
            if (candidate.Focus == exclude) continue;
            if (Better(candidate, best)) best = candidate;
        }
        foreach (ObservationObjective objective in context.Objectives)
        {
            Candidate candidate = Score(context, BroadcastFocus.Objective(objective.EntityId));
            if (candidate.Focus == exclude) continue;
            if (Better(candidate, best)) best = candidate;
        }
        return best;
    }

    private Candidate Score(ObservationContext context, BroadcastFocus focus)
    {
        if (focus.Kind == BroadcastFocusKind.Player)
        {
            if (!context.TryGetPlayer(focus.Id, out ObservationPlayer player) || !player.Selectable)
                return default;
            int score = 100;
            bool eventOverride = false;
            if (player.CarriesObjective) { score += 115; eventOverride = true; }
            if (player.IsPrime) score += 85;
            bool activeFight = false;
            foreach (CombatEvent value in context.CombatEvents)
            {
                if (!Recent(context.DeliveredTick, value.Tick, 120)) continue;
                if (value.Actor.IsValid && value.Actor.Slot == player.Slot
                    || value.Target.IsValid && value.Target.Slot == player.Slot)
                {
                    activeFight = true;
                    score += value.Kind == CombatEventKind.Damage ? 35 : 15;
                }
            }
            if (activeFight && player.Health is > 0 and <= 25) score += 35;
            int nearbyOpponents = 0;
            foreach (ObservationPlayer other in context.Players)
                if (other.Selectable && other.Team != player.Team
                    && (other.Position - player.Position).LengthSquared <= 12 * 12)
                    nearbyOpponents++;
            score += Math.Min(nearbyOpponents, 3) * 15;
            if (context.IsOvertime) score += player.CarriesObjective || player.IsPrime ? 45 : 10;
            if (context.IsMatchPoint) score += player.CarriesObjective ? 50 : 15;
            foreach (MatchAward award in context.Awards)
            {
                if (award.Subject.Slot != player.Slot || !Recent(context.DeliveredTick, award.Tick, 300)) continue;
                score += award.Priority;
                if (award.Kind is MatchAwardKind.DoubleKill or MatchAwardKind.TripleKill)
                    eventOverride = true;
            }
            foreach (MatchEvent value in context.SemanticEvents)
            {
                if (!Recent(context.DeliveredTick, value.Tick, 180)) continue;
                if (value.Subject.IsValid && value.Subject.Slot == player.Slot)
                {
                    score += value.Kind switch
                    {
                        MatchEventKind.ObjectivePickedUp => 120,
                        MatchEventKind.ObjectiveCaptured or MatchEventKind.ObjectiveDefended => 95,
                        MatchEventKind.PrimeChanged => 90,
                        MatchEventKind.PlayerKilled => 65,
                        _ => 20
                    };
                    eventOverride |= value.Kind is MatchEventKind.ObjectivePickedUp
                        or MatchEventKind.ObjectiveCaptured or MatchEventKind.PrimeChanged;
                }
            }
            foreach (KillFeedEntry value in context.CombatFeedback)
            {
                if (Recent(context.DeliveredTick, value.Tick, 120)
                    && value.Killer.IsValid && value.Killer.Slot == player.Slot) score += 35;
            }
            uint recent = _recentPlayers[player.Slot];
            if (recent != 0 && Recent(context.DeliveredTick, recent, RecentTargetTicks)) score -= 35;
            return new(focus, score, eventOverride);
        }

        if (focus.Kind == BroadcastFocusKind.Objective
            && context.TryGetObjective(focus.Id, out ObservationObjective objective))
        {
            int score = 70;
            bool eventOverride = false;
            if (objective.Contested) { score += 175; eventOverride = true; }
            if (objective.HasCarrier) score -= 50; // Follow the carrier, not the abandoned prop camera.
            if (!objective.AtBase && objective.Kind == ObservationObjectiveKind.Flag) score += 25;
            if (context.IsOvertime) score += 45;
            if (context.IsMatchPoint) score += 40;
            foreach (WorldEvent value in context.WorldFeedback)
            {
                if (value.EntityId != objective.EntityId || !Recent(context.DeliveredTick, value.Tick, 180)) continue;
                score += value.Kind switch
                {
                    WorldSignalKind.NodeContested => 150,
                    WorldSignalKind.FlagDropped => 110,
                    WorldSignalKind.FlagPickedUp => 90,
                    WorldSignalKind.FlagCaptured or WorldSignalKind.NodeCaptured => 80,
                    _ => 20
                };
                eventOverride |= value.Kind is WorldSignalKind.NodeContested
                    or WorldSignalKind.FlagDropped;
            }
            foreach (MatchEvent value in context.SemanticEvents)
            {
                if (value.EntityId != (uint)objective.EntityId
                    || !Recent(context.DeliveredTick, value.Tick, 180)) continue;
                score += 80;
                eventOverride = true;
            }
            for (int index = 0; index < _recentObjectives.Length; index++)
                if (_recentObjectives[index] == objective.EntityId
                    && _recentObjectiveTicks[index] != 0
                    && Recent(context.DeliveredTick, _recentObjectiveTicks[index], RecentTargetTicks))
                { score -= 35; break; }
            return new(focus, score, eventOverride);
        }
        return default;
    }

    private void SwitchTo(BroadcastFocus focus, uint tick)
    {
        if (Focus.Kind == BroadcastFocusKind.Player && (uint)Focus.Id < _recentPlayers.Length)
            _recentPlayers[Focus.Id] = tick == 0 ? 1 : tick;
        else if (Focus.Kind == BroadcastFocusKind.Objective)
        {
            _recentObjectives[_recentObjectiveHead] = Focus.Id;
            _recentObjectiveTicks[_recentObjectiveHead] = tick == 0 ? 1 : tick;
            _recentObjectiveHead = (_recentObjectiveHead + 1) % _recentObjectives.Length;
        }
        Focus = focus;
        _shotStartedTick = tick;
    }

    private static bool Better(Candidate candidate, Candidate current)
    {
        if (candidate.Focus.Kind == BroadcastFocusKind.None) return false;
        if (current.Focus.Kind == BroadcastFocusKind.None) return true;
        if (candidate.Score != current.Score) return candidate.Score > current.Score;
        if (candidate.Focus.Kind != current.Focus.Kind)
            return candidate.Focus.Kind < current.Focus.Kind;
        return candidate.Focus.Id < current.Focus.Id;
    }

    private static bool Elapsed(uint now, uint then, uint duration)
        => !Sequence32.IsNewer(then, now) && unchecked(now - then) >= duration;

    private static bool Recent(uint now, uint then, uint duration)
        => !Sequence32.IsNewer(then, now) && unchecked(now - then) < duration;

    private readonly record struct Candidate(BroadcastFocus Focus, int Score, bool Override);
}
