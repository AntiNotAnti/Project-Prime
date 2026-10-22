using System;
using System.Collections.Generic;
using MphRead.Entities;
using MphRead.Mods.Network;
using MphRead.Mods.MatchEvents;

namespace MphRead.Mods.Replay
{
    /// <summary>
    /// Presentation-only automatic camera selection. The score is deliberately
    /// derived from replay annotations and already-simulated world state; it never
    /// feeds back into packets or simulation.
    /// </summary>
    internal static class ReplayDirector
    {
        private const uint MinimumHoldFrames = 3 * 60;
        private const uint MajorEventHoldFrames = 60;
        private const uint EventLookbackFrames = 4 * 60;
        private const float SwitchMargin = 12;
        private const float MajorEventSwitchMargin = 20;

        private readonly record struct ReplayDirectorCandidate(int Slot, float Score,
            string Reason, bool MajorEvent);

        private static uint _lastSwitchFrame;
        private static uint _lastFrame;
        private static long _seekGeneration = -1;
        private static int _eventStart;
        private static int _eventEnd;
        private static int _currentSlot = -1;
        private static float _currentScore;
        private static int _previousSlot = -1;
        private static uint _previousSwitchFrame;
        private static readonly float[] EventScores = new float[RosterPacket.MaxSlots];
        private static readonly string?[] EventReasons = new string?[RosterPacket.MaxSlots];
        private static readonly bool[] EventMajor = new bool[RosterPacket.MaxSlots];

        public static int CurrentSlot => _currentSlot;
        public static float CurrentScore => _currentScore;
        public static string Reason { get; private set; } = "idle";

        public static void Reset()
        {
            _lastSwitchFrame = 0;
            _lastFrame = 0;
            _seekGeneration = -1;
            _eventStart = 0;
            _eventEnd = 0;
            _currentSlot = -1;
            _currentScore = 0;
            _previousSlot = -1;
            _previousSwitchFrame = 0;
            Array.Clear(EventScores);
            Array.Clear(EventReasons);
            Array.Clear(EventMajor);
            Reason = "idle";
        }

        internal static (float Actor, float Target, string Reason, bool Major) SemanticInterest(MatchSemanticEventType type)
            => type switch
            {
                MatchSemanticEventType.PlayerKilled => (70, 34, "recent kill", true),
                MatchSemanticEventType.ObjectiveCaptured or MatchSemanticEventType.ObjectivePickedUp
                    or MatchSemanticEventType.ObjectiveDefended or MatchSemanticEventType.NodeCaptured
                    or MatchSemanticEventType.PrimeChanged => (78, 0, "objective pressure", true),
                MatchSemanticEventType.Headshot => (40, 15, "headshot exchange", false),
                MatchSemanticEventType.PlayerAssisted => (24, 0, "recent assist", false),
                _ => (0, 0, "quiet", false)
            };
        private static void AddCanonicalInterest(ReplayReplicaState state)
        {
            foreach (var fact in state.SemanticEvents.Events)
            {
                if (fact.Tick > state.ServerTick || state.ServerTick - fact.Tick > EventLookbackFrames) continue;
                var interest = SemanticInterest(fact.Type);
                float age = 1 - (state.ServerTick - fact.Tick) / (float)EventLookbackFrames;
                void Add(byte slot, ushort generation, float value, bool major)
                {
                    if (slot >= EventScores.Length || value <= 0 || state.Occupant(slot).Generation != generation) return;
                    value *= age;
                    if (value >= EventScores[slot]) EventReasons[slot] = interest.Reason;
                    EventScores[slot] += value; EventMajor[slot] |= major;
                }
                Add(fact.ActorSlot, fact.ActorGeneration, interest.Actor, interest.Major);
                Add(fact.TargetSlot, fact.TargetGeneration, interest.Target, false);
            }
        }

        public static void Tick(Scene scene)
        {
            if (!ReplayCamera.Director || !DemoPlayback.IsActive)
                return;

            uint frame = ReplayController.CurrentFrame;
            long seekGeneration = DemoPlayback.Session.Transport.SeekGeneration;
            if (_seekGeneration != seekGeneration || frame < _lastFrame) Reset();
            _seekGeneration = seekGeneration;
            IReadOnlyList<ReplayEvent> events = DemoPlayback.Events;
            if (frame < _lastFrame || _eventEnd > events.Count)
            {
                _eventStart = 0;
                _eventEnd = 0;
            }
            _lastFrame = frame;
            while (_eventEnd < events.Count && events[_eventEnd].Frame <= frame)
                _eventEnd++;
            uint cutoff = frame > EventLookbackFrames ? frame - EventLookbackFrames : 0;
            while (_eventStart < _eventEnd && events[_eventStart].Frame < cutoff)
                _eventStart++;

            Array.Clear(EventScores);
            Array.Clear(EventReasons);
            Array.Clear(EventMajor);
            bool canonical = scene.Services is ReplaySceneServices replica && replica.State.SemanticEvents.LatestEvent != 0;
            if (canonical) AddCanonicalInterest(((ReplaySceneServices)scene.Services).State);
            for (int i = _eventStart; i < _eventEnd; i++)
            {
                ReplayEvent e = events[i];
                if (canonical && e.Type is ReplayEventType.Kill or ReplayEventType.FlagCapture
                    or ReplayEventType.NodeCapture or ReplayEventType.PrimeChanged or ReplayEventType.Objective) continue;
                float age = 1 - (frame - e.Frame) / (float)Math.Max(1u, EventLookbackFrames);
                void Add(byte slot, float value, string reason)
                {
                    if (slot >= EventScores.Length || value <= 0) return;
                    if (value >= EventScores[slot])
                        EventReasons[slot] = reason;
                    EventScores[slot] += value;
                }

                switch (e.Type)
                {
                    case ReplayEventType.Kill:
                        Add(e.ActorSlot, 70 * age, "recent kill");
                        Add(e.TargetSlot, 34 * age, "kill aftermath");
                        if (e.ActorSlot < EventMajor.Length) EventMajor[e.ActorSlot] = true;
                        break;
                    case ReplayEventType.FlagCapture:
                        case ReplayEventType.NodeCapture:
                        case ReplayEventType.PrimeChanged:
                        case ReplayEventType.Objective:
                        Add(e.ActorSlot, 78 * age, "objective pressure");
                        if (e.ActorSlot < EventMajor.Length) EventMajor[e.ActorSlot] = true;
                        break;
                    case ReplayEventType.Damage:
                        float damage = Math.Min(26, Math.Max(0, e.Value) * 0.22f) * age;
                        Add(e.ActorSlot, damage, "damage exchange");
                        Add(e.TargetSlot, damage * 0.5f, "under pressure");
                        break;
                    case ReplayEventType.ScoreChanged:
                        Add(e.ActorSlot, 22 * age, "score change");
                        break;
                }
            }

            int bestSlot = -1;
            float bestScore = float.MinValue;
            string bestReason = "quiet";
            bool bestMajor = false;
            ReplayDirectorCandidate current = default;
            var candidates = new ReplayDirectorCandidate[RosterPacket.MaxSlots];
            Array.Fill(candidates, new ReplayDirectorCandidate(-1, float.MinValue, "unavailable", false));

            foreach (PlayerEntity player in scene.GetPlayerEntities())
            {
                if (!player.LoadFlags.TestFlag(LoadFlags.Active)
                    || !player.LoadFlags.TestFlag(LoadFlags.Spawned)
                    || player.Health <= 0)
                    continue;

                float score = 4;
                string reason = "active";
                if (player.Health <= 25)
                {
                    score += 18;
                    reason = "low health";
                }

                int nearbyEnemies = 0;
                float closest = float.MaxValue;
                foreach (PlayerEntity other in scene.GetPlayerEntities())
                {
                    if (other == player || other.Health <= 0
                        || !other.LoadFlags.TestFlag(LoadFlags.Spawned)
                        || (scene.GameState.Teams && other.TeamIndex == player.TeamIndex))
                        continue;
                    float distance = (other.Position - player.Position).Length;
                    closest = Math.Min(closest, distance);
                    if (distance <= 12) nearbyEnemies++;
                }
                if (nearbyEnemies > 0)
                {
                    score += Math.Min(30, nearbyEnemies * 9);
                    reason = nearbyEnemies > 1 ? "multi-player fight" : "duel";
                    if (closest <= 5) score += 8;
                }

                if (player.SlotIndex >= 0 && player.SlotIndex < EventScores.Length)
                {
                    score += EventScores[player.SlotIndex];
                    if (EventReasons[player.SlotIndex] is string eventReason)
                        reason = eventReason;
                }

                if (DemoPlayback.LastFrame > frame && DemoPlayback.LastFrame - frame <= 60 * 60)
                    score *= 1.12f;

                float selectionScore = score;
                if (player.SlotIndex == _previousSlot && frame >= _previousSwitchFrame
                    && frame - _previousSwitchFrame < 120) selectionScore -= 8;
                if (selectionScore > bestScore)
                {
                    bestScore = selectionScore;
                    bestSlot = player.SlotIndex;
                    bestReason = reason;
                    bestMajor = player.SlotIndex >= 0 && player.SlotIndex < EventMajor.Length
                        && EventMajor[player.SlotIndex];
                }
                if ((uint)player.SlotIndex < (uint)candidates.Length)
                    candidates[player.SlotIndex] = new(player.SlotIndex, score, reason, EventMajor[player.SlotIndex]);
            }

            if ((uint)_currentSlot < (uint)candidates.Length)
            {
                current = candidates[_currentSlot];
                if (current.Slot >= 0) { _currentScore = current.Score; Reason = current.Reason; }
            }

            if (bestSlot < 0) { _currentSlot = -1; _currentScore = 0; Reason = "unavailable"; return; }

            bool currentValid = _currentSlot >= 0 && current.Slot == _currentSlot;
            uint hold = frame < _lastSwitchFrame ? uint.MaxValue : frame - _lastSwitchFrame;
            bool majorOverride = bestSlot != _currentSlot && bestMajor && hold >= MajorEventHoldFrames
                && bestScore >= _currentScore + MajorEventSwitchMargin;
            bool holdExpired = hold >= MinimumHoldFrames;
            bool clearlyBetter = bestSlot != _currentSlot && bestScore >= _currentScore + SwitchMargin;

            if (_currentSlot < 0 || !currentValid || majorOverride || (holdExpired && clearlyBetter))
            {
                _previousSlot = _currentSlot;
                _previousSwitchFrame = frame;
                _currentSlot = bestSlot;
                _currentScore = candidates[bestSlot].Score;
                _lastSwitchFrame = frame;
                Reason = bestReason;
                SpectatorMode.Watch(bestSlot);

                // Pick a readable shot while leaving authored tracks alone.
                if (!ReplayCamera.PlayTrack)
                {
                    if (bestReason.Contains("objective", StringComparison.Ordinal))
                        ReplayCamera.SetDirectorMode(ReplayCameraMode.Orbit);
                    else if (bestReason is "duel" or "multi-player fight" or "damage exchange")
                        ReplayCamera.SetDirectorMode(ReplayCameraMode.Chase);
                    else
                        ReplayCamera.SetDirectorMode(ReplayCameraMode.FirstPerson);
                }
            }
            else if (bestSlot == _currentSlot)
            {
                _currentScore = candidates[bestSlot].Score;
                Reason = bestReason;
            }
        }
    }
}
