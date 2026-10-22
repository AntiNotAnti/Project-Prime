using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using MphRead.Mods.MatchEvents;
using MphRead.Mods.Network;

namespace MphRead.Mods.Replay;

internal sealed record ReplayContextEvent(uint Frame, string Kind, byte Actor, byte Target,
    int Score, string Label, string Provenance, ushort Generation = 0, ushort Life = 0);
internal sealed record ReplayContextResult(IReadOnlyList<ReplayContextEvent> Events, string SourceSha256,
    string Clock = "Visible delivery frame; authority/source clocks are never equated");

// Detached decoder only: no foreground world, map queries, render calls or saves.
internal static class ReplayContextAnalysis
{
    internal const uint MaximumFrames = 2 * 60 * 60 * 60;
    internal const int MaximumContexts = 2048;
    internal static ReplayContextResult Read(string path, CancellationToken cancellation)
    {
        using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        string hash = Convert.ToHexString(SHA256.HashData(source));
        var host = new PassiveReplaySessionHost(); using var reader = new ReplayPlaybackSession(host);
        if (!reader.JoinDetached(path, cancellation)) throw new InvalidDataException(reader.LastError);
        if (reader.LastFrame > MaximumFrames) throw new InvalidDataException("Context analysis is limited to two hours; analyze a shorter clip.");
        var index = new Index();
        // File-only launch certificates preserve authority-verified source and
        // pose. Ordinary authored input alone is never distance evidence.
        reader.FactRead += (_, raw) =>
        {
            cancellation.ThrowIfCancellationRequested();
            if (raw.Length > 0 && raw[0] == ReplayTimedShotFact.Tag && ReplayTimedShotFact.TryRead(raw, out var launch))
                index.Launch(launch);
        };
        uint scanned = 0;
        while (!reader.AtEnd)
        {
            cancellation.ThrowIfCancellationRequested(); reader.PumpFrame(); host.State.DiscardDetachedDrawPackets();
            if (reader.LastResult != ReplayOpenResult.Success) throw new InvalidDataException(reader.LastError);
            if (++scanned > MaximumFrames + 1) throw new InvalidDataException("Context analysis frame budget exceeded.");
            if (reader.IsWarming) continue;
            index.Observe(reader.CurrentFrame, host.State);
        }
        cancellation.ThrowIfCancellationRequested(); source.Position = 0;
        if (hash != Convert.ToHexString(SHA256.HashData(source))) throw new IOException("Replay changed during context analysis.");
        return new(index.Events.OrderBy(e => e.Frame).ThenBy(e => e.Kind, StringComparer.Ordinal)
            .ThenBy(e => e.Actor).ThenBy(e => e.Target).ToArray(), hash);
    }
    internal static IReadOnlyList<ReplayHighlight> Suggestions(string path, IReadOnlyList<ReplayEvent> annotations,
        uint duration, ReplayHighlightWeights? weights = null, CancellationToken cancellation = default)
        => Combine(ReplayStudio.Highlights(annotations, duration, weights, cancellation), Read(path, cancellation), duration, weights);
    internal static IReadOnlyList<ReplayHighlight> Combine(IReadOnlyList<ReplayHighlight> basic,
        ReplayContextResult context, uint duration, ReplayHighlightWeights? weights = null)
    {
        int weight = Math.Clamp((weights ?? new()).CombatPercent, 0, 1000);
        return basic.Concat(context.Events.Select(e => new ReplayHighlight(e.Frame > 240 ? e.Frame - 240 : 0,
                (uint)Math.Min(duration, (ulong)e.Frame + 180), e.Frame, ReplayHighlightKind.Context,
                e.Actor, e.Target, (int)Math.Clamp((long)e.Score * weight / 100, 0, 100), e.Label)))
            .Where(e => e.StartFrame < e.EndFrame && e.Score > 0).Distinct()
            .OrderByDescending(e => e.Score).ThenBy(e => e.FocusFrame).ThenBy(e => e.ActorSlot)
            .ThenBy(e => e.TargetSlot).ThenBy(e => e.Label, StringComparer.Ordinal).Take(MaximumContexts).ToArray();
    }
    internal static string Json(string path, IReadOnlyList<ReplayEvent> annotations, uint duration,
        ReplayHighlightWeights weights, CancellationToken cancellation)
    {
        var context = Read(path, cancellation);
        return JsonSerializer.Serialize(new { Version = 2, context.SourceSha256, context.Clock,
            Provenance = "Annotations plus verified semantic/shot/snapshot facts; unavailable context omitted",
            LongShotThresholdWorldUnits = 30, DurationFrames = duration, FramesPerSecond = 60, Weights = weights,
            Activity = ReplayStudio.Analytics(annotations, duration).DamageTimeline,
            Context = context.Events, Highlights = Combine(ReplayStudio.Highlights(annotations, duration, weights, cancellation), context, duration, weights)
        }, new JsonSerializerOptions { WriteIndented = true });
    }

    internal sealed class Index
    {
        private readonly List<ReplayContextEvent> _events = new();
        internal IReadOnlyList<ReplayContextEvent> Events => _events;
        private readonly HashSet<(ushort, ulong, uint)> _semantic = new();
        private readonly HashSet<(ShotKey, ushort, byte, ushort, ushort)> _shots = new();
        private readonly HashSet<(ushort, ulong, string, MatchSemanticActor, MatchSemanticActor, uint)> _contextIdentities = new();
        private readonly Dictionary<(byte, ushort), (byte, ushort)> _lastKiller = new();
        private readonly Dictionary<MatchSemanticActor, int> _streak = new();
        private readonly Dictionary<ShotKey, ReplayTimedShotFact> _launches = new();
        private readonly Queue<ShotKey> _launchOrder = new();
        private ushort _match; private ulong _epoch; private bool _fromStart, _firstKill;
        private string? _scoreRoster; private int? _leader; private uint? _scoreTick;
        private readonly Dictionary<int, int> _maximumDeficits = new();
        private void Add(uint frame, string kind, MatchSemanticActor actor, MatchSemanticActor target,
            int score, string label, string provenance)
        {
            if (!_contextIdentities.Add((_match, _epoch, kind, actor, target, kind == "LeadChange" ? frame : 0))) return;
            if (_events.Count == MaximumContexts) throw new InvalidDataException("Context candidate budget exceeded; analyze a shorter clip.");
            _events.Add(new(frame, kind, actor.Slot, target.Slot, score, label, provenance, actor.SlotGeneration, actor.Life));
        }
        private void Scope(ushort match, ulong epoch)
        {
            if (_match == match && _epoch == epoch) return;
            _match = match; _epoch = epoch; _fromStart = false; _firstKill = false;
            _lastKiller.Clear(); _streak.Clear(); _scoreRoster = null; _leader = null; _scoreTick = null; _maximumDeficits.Clear();
        }
        internal void Semantic(uint frame, MatchSemanticEvent fact)
        {
            Scope(fact.MatchId, fact.AuthorityEpoch);
            var identity = (fact.MatchId, fact.AuthorityEpoch, fact.EventId);
            if (_semantic.Contains(identity)) return;
            if (_semantic.Count == ReplayStudio.MaximumAnalysisEvents) throw new InvalidDataException("Semantic analysis event budget exceeded.");
            _semantic.Add(identity);
            if (fact.Type == MatchSemanticEventType.MatchStarted) { _fromStart = true; _firstKill = false; _streak.Clear(); _lastKiller.Clear(); return; }
            if (fact.Type is MatchSemanticEventType.PlayerSpawned or MatchSemanticEventType.PlayerSuicide)
            { _streak.Remove(fact.Actor); return; }
            if (fact.Type != MatchSemanticEventType.PlayerKilled || !fact.Actor.IsPlayer || !fact.Target.IsPlayer) return;
            _streak.Remove(fact.Target);
            if (fact.Actor.Slot == fact.Target.Slot
                || (fact.Flags & (MatchSemanticEventFlags.FriendlyFire | MatchSemanticEventFlags.Environment)) != 0) return;
            const string proof = "Canonical semantic kill with match/epoch/occupant/life identity";
            if (_fromStart && !_firstKill) Add(frame, "FirstBlood", fact.Actor, fact.Target, 78, "Verified first blood", proof + "; match start observed");
            _firstKill = true;
            if ((fact.Flags & MatchSemanticEventFlags.Headshot) != 0)
                Add(frame, "Headshot", fact.Actor, fact.Target, 80, "Verified headshot kill", proof);
            int streak = _streak.GetValueOrDefault(fact.Actor) + 1; _streak[fact.Actor] = streak;
            if (streak >= 3) Add(frame, "Streak", fact.Actor, fact.Target, Math.Min(95, 75 + streak * 3), $"Verified ≥{streak}-kill life streak", proof);
            var actor = (fact.Actor.Slot, fact.Actor.SlotGeneration); var target = (fact.Target.Slot, fact.Target.SlotGeneration);
            if (_lastKiller.TryGetValue(actor, out var prior) && prior == target)
            { Add(frame, "Revenge", fact.Actor, fact.Target, 84, "Verified revenge", proof + "; reciprocal occupants across lives"); _lastKiller.Remove(actor); }
            _lastKiller[target] = actor;
        }
        internal void Launch(ReplayTimedShotFact launch)
        {
            if (launch.Source.Source != ShotProofReason.Verified || launch.Source.Pose != ShotProofReason.Verified) return;
            if (_launches.TryGetValue(launch.Key, out var prior))
            { if (prior.Fire != launch.Fire) _launches[launch.Key] = default; return; }
            while (_launchOrder.Count >= 4096) _launches.Remove(_launchOrder.Dequeue());
            _launchOrder.Enqueue(launch.Key); _launches[launch.Key] = launch;
        }
        internal void Shot(uint frame, ReplayShotFact fact, ReplayReplicaState? state = null)
        {
            Scope(fact.MatchId, fact.AuthorityEpoch);
            if (!fact.Lethal || fact.ShooterSlot == fact.VictimSlot) return;
            var key = new ShotKey(fact.AuthorityEpoch, fact.MatchId, fact.ShooterSlot, fact.ShooterGeneration, fact.ShooterLifeId, fact.ShotId);
            var identity = (key, fact.DamageEventId, fact.VictimSlot, fact.VictimGeneration, fact.VictimLifeId);
            if (_shots.Contains(identity)) return;
            if (_shots.Count == ReplayStudio.MaximumAnalysisEvents) throw new InvalidDataException("Shot analysis budget exceeded.");
            _shots.Add(identity);
            var actor = new MatchSemanticActor(fact.ShooterSlot, fact.ShooterGeneration, fact.ShooterLifeId);
            var target = new MatchSemanticActor(fact.VictimSlot, fact.VictimGeneration, fact.VictimLifeId);
            if (_launches.TryGetValue(key, out var launch) && launch.Key == key && launch.IsValid
                && launch.Fire.SourceFrame == fact.LaunchFrame && launch.Fire.Weapon == fact.Weapon)
            {
                float distance = (launch.Fire.Origin - fact.ImpactPoint).Length;
                if (float.IsFinite(distance) && distance >= 30)
                    Add(frame, "LongShot", actor, target, 85, "Verified long shot (≥30 world units)",
                        "Authority-verified launch pose certificate joined to accepted lethal impact by full shot identity; native map units");
            }
            if (fact.Headshot) Add(frame, "Headshot", actor, target, 80, "Verified headshot kill", "Accepted lethal shot fact with explicit headshot flag");
            // Require the exact authority snapshot tick, complete occupant roster,
            // health and remaining-time facts. Never describe this as a victory.
            if (state == null || state.ServerTick != fact.ResolveTick
                || !state.TryGetPlayer(actor.Slot, out var shooter) || shooter.SlotGeneration != actor.SlotGeneration
                || shooter.LifeId != actor.Life || shooter.Health == 0
                || (shooter.Flags & (PlayerState.FlagActive | PlayerState.FlagSpawned | PlayerState.FlagSpectating))
                    != (PlayerState.FlagActive | PlayerState.FlagSpawned)) return;
            if (float.IsFinite(shooter.Speed.LengthSquared) && shooter.Speed.LengthSquared >= .04f)
                Add(frame, "FastMovement", actor, target, 76, "Fast-moving kill at resolve (≥12 world units/s)",
                    "Exact resolve-tick authority speed, 60Hz simulation units; no airborne or launch-motion inference");
            var mode = state.Configuration?.Match.Mode ?? (GameMode)(state.Match?.Mode ?? 0);
            if (mode is not (GameMode.Battle or GameMode.BattleTeams)
                || state.AuthorityWorld is not { } world || world.Tick != fact.ResolveTick
                || world.Phase != MatchState.InProgress || !float.IsFinite(world.MatchTime)
                || world.MatchTime is <= 0 or > 30 || shooter.Health > 25) return;
            int allies = 0, opponents = 0;
            for (int slot = 0; slot < 8; slot++)
            {
                var occupant = state.Occupant(slot); if (occupant.Generation == 0) continue;
                if (!state.TryGetPlayer(slot, out var p) || p.SlotGeneration != occupant.Generation) return;
                if (p.Health == 0 || (p.Flags & (PlayerState.FlagActive | PlayerState.FlagSpawned | PlayerState.FlagSpectating))
                    != (PlayerState.FlagActive | PlayerState.FlagSpawned)) continue;
                if (slot == actor.Slot || mode == GameMode.BattleTeams && p.Team == shooter.Team) allies++; else opponents++;
            }
            if (opponents >= 2 && opponents > allies)
                Add(frame, "ClutchContext", actor, target, 90, "Late low-health outnumbered kill",
                    "Exact resolve-tick authority health/roster/time snapshot; no win or clutch victory inferred");
        }
        internal void Observe(uint frame, ReplayReplicaState state)
        {
            if (state.Match is not { } match) return;
            Scope(match.MatchId, match.AuthorityEpoch);
            foreach (var fact in state.SemanticEvents.Events) Semantic(frame, fact.ToFact());
            foreach (var fact in state.ShotFacts) Shot(frame, fact, state);
            Scores(frame, state);
        }
        private void Scores(uint frame, ReplayReplicaState state)
        {
            if (_scoreTick == state.ServerTick || state.Match is not { } match) return;
            var mode = state.Configuration?.Match.Mode ?? (GameMode)match.Mode;
            if (mode is not (GameMode.Battle or GameMode.BattleTeams)) return;
            var players = new List<(byte Slot, ushort Generation, ushort Life, short Points, byte Team)>();
            for (byte slot = 0; slot < 8; slot++)
            {
                var occupant = state.Occupant(slot); if (occupant.Generation == 0) continue;
                if (!state.TryGetPlayer(slot, out var p) || p.SlotGeneration != occupant.Generation) return;
                players.Add((slot, p.SlotGeneration, p.LifeId, p.Points, p.Team));
            }
            if (players.Count < 2) return;
            string roster = String.Join(";", players.Select(p => $"{p.Slot}:{p.Generation}:{p.Team}"));
            var scores = mode == GameMode.Battle ? players.Select(p => ((int)p.Slot, (int)p.Points)).ToArray()
                : state.AuthorityWorld is { } world && world.Tick == state.ServerTick
                    ? players.Where(p => p.Team < world.TeamPoints.Length).Select(p => ((int)p.Team, world.TeamPoints[p.Team])).Distinct().ToArray()
                    : Array.Empty<(int, int)>();
            if (scores.Length < 2) return;
            // A world-score packet may arrive after its same-tick snapshot.
            // Do not cache a missing/partial score sample as if it was analyzed.
            _scoreTick = state.ServerTick;
            if (_scoreRoster != roster) _maximumDeficits.Clear();
            int maximum = scores.Max(p => p.Item2); var leaders = scores.Where(p => p.Item2 == maximum).ToArray();
            int? leader = leaders.Length == 1 ? leaders[0].Item1 : null;
            if (_scoreRoster == roster && _leader.HasValue && leader.HasValue && _leader != leader)
            {
                var actor = players.First(p => mode == GameMode.Battle ? p.Slot == leader : p.Team == leader);
                Add(frame, "LeadChange", new(actor.Slot, actor.Generation, actor.Life), MatchSemanticActor.None, 88,
                    mode == GameMode.Battle ? "Verified battle lead change" : "Verified team battle lead change",
                    "Complete stable roster plus authoritative mode-specific score snapshots; ties/baselines/roster changes omitted");
                if (_maximumDeficits.GetValueOrDefault(leader.Value) >= 3)
                    Add(frame, "Comeback", new(actor.Slot, actor.Generation, actor.Life), MatchSemanticActor.None, 92,
                        "Verified comeback lead after ≥3-point deficit", "Complete stable-roster battle score history; a lead, not a final win");
            }
            foreach (var score in scores) _maximumDeficits[score.Item1] = Math.Max(_maximumDeficits.GetValueOrDefault(score.Item1),
                (int)Math.Min(int.MaxValue, (long)maximum - score.Item2));
            _leader = leader ?? (_scoreRoster == roster ? _leader : null); _scoreRoster = roster;
        }
    }
}
