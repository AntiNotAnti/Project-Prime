using System;

namespace MphRead
{
    /// <summary>The server's tick-based phase owner. World simulation is permitted
    /// only in Playing; loading/session readiness remains network-owned.</summary>
    public sealed class MatchLifecycle
    {
        public const uint CountdownTicks = 3 * 60;
        public const uint EndingTicks = 3 * 60;
        public const uint IntermissionTicks = 5 * 60;
        private readonly MatchRuntime _match;
        private bool _endingScheduled;
        private uint? _durationTicks;
        public bool RotationDue { get; private set; }

        public MatchLifecycle(MatchRuntime match, uint tick = 0)
        {
            _match = match ?? throw new ArgumentNullException(nameof(match));
            _durationTicks = ConfiguredDurationTicks(match.Rules);
            _match.UsesServerLifecycle = true;
            SetPhase(MatchPhase.WaitingForPlayers, tick);
            _match.MatchTime = ConfiguredMatchTime;
        }

        private float ConfiguredMatchTime => _match.Rules.TimeLimit.HasValue
            ? (float)_match.Rules.TimeLimit.Value.TotalSeconds : -1;

        public void AdvanceBeforeStep(uint tick, bool eligible, Action resetForCountdown)
        {
            switch (_match.Phase)
            {
                case MatchPhase.WaitingForPlayers:
                    if (eligible)
                    {
                        _durationTicks = ConfiguredDurationTicks(_match.Rules);
                        // Publish Countdown only after the reset succeeds.
                        resetForCountdown();
                        _endingScheduled = RotationDue = false;
                        _match.MatchTime = ConfiguredMatchTime;
                        SetPhase(MatchPhase.Countdown, tick, CountdownTicks);
                    }
                    break;
                case MatchPhase.Countdown:
                    if (!eligible)
                    {
                        SetPhase(MatchPhase.WaitingForPlayers, tick);
                    }
                    else if (DeadlineReached(tick, _match.PhaseEndTick))
                    {
                        uint? length = _durationTicks;
                        _match.Period = MatchPeriod.Regulation;
                        _match.PeriodStartTick = tick;
                        SetPhase(MatchPhase.Playing, tick, length);
                        _match.MatchTime = length.HasValue ? length.Value / 60f : -1;
                    }
                    break;
                case MatchPhase.Playing:
                    if (_match.Period != MatchPeriod.Regulation) { break; }
                    // A score/objective/explicit completion may have already set zero.
                    if (_match.MatchTime != 0 && _match.HasPhaseDeadline)
                    {
                        _match.MatchTime = DeadlineReached(tick, _match.PhaseEndTick)
                            ? 0 : unchecked(_match.PhaseEndTick - tick) / 60f;
                    }
                    break;
                case MatchPhase.Ending:
                    if (!_endingScheduled) { ObserveCompletion(tick); }
                    if (DeadlineReached(tick, _match.PhaseEndTick))
                    {
                        SetPhase(MatchPhase.Intermission, tick, IntermissionTicks);
                    }
                    break;
                case MatchPhase.Intermission:
                    RotationDue = DeadlineReached(tick, _match.PhaseEndTick);
                    break;
                default:
                    throw new InvalidOperationException("Unknown match phase.");
            }
        }

        public void ObserveCompletion(uint tick)
        {
            if (_match.Phase == MatchPhase.Ending && !_endingScheduled)
            {
                _endingScheduled = true;
                SetPhase(MatchPhase.Ending, tick, EndingTicks);
            }
        }

        private void SetPhase(MatchPhase phase, uint tick, uint? duration = null)
        {
            _match.Phase = phase;
            _match.PhaseStartTick = tick;
            _match.PhaseEndTick = duration.HasValue ? unchecked(tick + duration.Value) : 0;
            _match.HasPhaseDeadline = duration.HasValue;
            uint revision = unchecked(_match.PhaseRevision + 1);
            _match.PhaseRevision = revision == 0 ? 1 : revision;
            if (_match.MatchId != 0)
            {
                MatchEventKind? kind = phase switch
                {
                    MatchPhase.Countdown => MatchEventKind.CountdownStarted,
                    MatchPhase.Playing => MatchEventKind.MatchStarted,
                    MatchPhase.Ending => MatchEventKind.MatchEnded,
                    _ => null
                };
                if (kind.HasValue)
                {
                    byte winningTeam = phase == MatchPhase.Ending ? WinningTeam() : (byte)255;
                    _match.SemanticEvents.Dispatch(new(0, tick, _match.MatchId, _match.PhaseRevision,
                        kind.Value, MphRead.Mods.Network.CombatActor.None, MphRead.Mods.Network.CombatActor.None,
                        Team: winningTeam));
                }
            }
        }

        private byte WinningTeam()
        {
            // Result is the authoritative, tie-broken outcome. If this
            // transition occurs before result capture, leave the team unknown;
            // clients must not guess Victory from MatchEnded alone.
            if (_match.Result is not { } result || result.ResultSlots.IsDefaultOrEmpty)
                return 255;
            int slot = result.ResultSlots[0];
            if ((uint)slot >= (uint)result.Players.Length) return 255;
            int team = result.Players[slot].TeamIndex;
            return (uint)team < 8 ? (byte)team : (byte)255;
        }

        public static void ValidateRules(MatchRules rules)
        {
            ArgumentNullException.ThrowIfNull(rules);
            _ = ConfiguredDurationTicks(rules);
            EnhancedHuntersRankingPolicy.Validate(rules);
        }

        private static uint? ConfiguredDurationTicks(MatchRules rules) => rules.TimeLimit.HasValue
            ? DurationTicks(rules.TimeLimit.Value) : null;

        private static uint DurationTicks(TimeSpan duration)
        {
            double ticks = Math.Ceiling(duration.TotalSeconds * 60);
            if (ticks < 0 || ticks > Int32.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(duration), "Match duration must fit the tick comparison window.");
            }
            return (uint)ticks;
        }

        private static bool DeadlineReached(uint tick, uint deadline) => unchecked((int)(tick - deadline)) >= 0;
    }

    /// <summary>V1 competitive policy kept in one replaceable boundary.</summary>
    public static class EnhancedHuntersRankingPolicy
    {
        public static void Validate(MatchRules rules)
        {
            if (rules.EnhancedHunters
                && (rules.RulesetPreset != RulesetPreset.Custom
                    || rules.RankingEligibility != RankingEligibility.Unranked))
            {
                throw new ArgumentException(
                    "Enhanced Hunters is an unranked custom ruleset in protocol 23.",
                    nameof(rules));
            }
        }
    }
}
