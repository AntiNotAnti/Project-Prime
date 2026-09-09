using System;
using System.Collections.Generic;
using MphRead.Entities;

namespace MphRead.Mods.Network
{
    public sealed class ServerWorldEvents
    {
        public const int Capacity = 1024;
        private readonly WorldEvent[] _events = new WorldEvent[Capacity];
        private readonly Dictionary<EntityBase, uint> _identities = new(WorldPacket.Capacity);
        private uint _matchId, _nextEntityId = 1;
        private int _head, _count;
        public void Reset() { _head = _count = 0; _matchId = 0; _nextEntityId = 1; _identities.Clear(); }
        private void Bind(Scene scene)
        {
            if (_matchId == scene.Match.MatchId) return;
            Reset(); _matchId = scene.Match.MatchId;
        }
        public uint Identity(Scene scene, EntityBase entity)
        {
            Bind(scene);
            if (_identities.TryGetValue(entity, out uint id)) return id;
            if (_identities.Count >= WorldPacket.Capacity || _nextEntityId == 0)
                throw new InvalidOperationException("World entity identity budget exhausted.");
            _identities.Add(entity, id = _nextEntityId++); return id;
        }
        public void Forget(EntityBase entity) => _identities.Remove(entity);
        public void Publish(Scene scene, in WorldSignal signal, uint tick, uint eventId)
        {
            Bind(scene);
            if (scene.Match.Phase != MatchPhase.Playing || _matchId == 0) return;
            uint identity = signal.Subject == WorldSubjectKind.Match ? 0
                : signal.Entity == null ? throw new InvalidOperationException("World signal requires its subject.")
                : signal.Subject == WorldSubjectKind.Item ? Identity(scene, signal.Entity) : unchecked((uint)signal.Entity.Id);
            var value = new WorldEvent(eventId, tick, _matchId, scene.Match.PhaseRevision, signal.Subject,
                signal.Kind, signal.Team, identity, signal.Actor?.ServerCombatIdentity ?? CombatActor.None,
                signal.Position, signal.A, signal.B, signal.C);
            if (!value.IsValid) throw new InvalidOperationException("Invalid authoritative world signal.");
            if (TryTranslateSemantic(signal, identity, tick, _matchId, scene.Match.PhaseRevision, out MatchEvent semantic))
                scene.Match.SemanticEvents.Dispatch(semantic);
            if (_count == Capacity) throw new InvalidOperationException("Authoritative world event journal exhausted.");
            _events[(_head + _count++) % Capacity] = value;
        }
        public bool TryPeek(out WorldEvent value) { value = _count == 0 ? default : _events[_head]; return _count > 0; }
        public void Consume()
        {
            if (_count == 0) throw new InvalidOperationException("No pending world event.");
            _head = (_head + 1) % Capacity; _count--;
        }

        private static bool TryTranslateSemantic(in WorldSignal signal, uint identity, uint tick,
            uint matchId, uint phaseRevision, out MatchEvent value)
        {
            MatchEventKind? kind = signal.Kind switch
            {
                WorldSignalKind.FlagPickedUp => MatchEventKind.ObjectivePickedUp,
                WorldSignalKind.FlagDropped => MatchEventKind.ObjectiveDropped,
                WorldSignalKind.FlagCaptured => MatchEventKind.ObjectiveCaptured,
                WorldSignalKind.NodeCaptured => MatchEventKind.NodeCaptured,
                WorldSignalKind.PrimeChanged => MatchEventKind.PrimeChanged,
                WorldSignalKind.OvertimeStarted => MatchEventKind.OvertimeStarted,
                WorldSignalKind.MatchPoint => MatchEventKind.MatchPointReached,
                _ => null
            };
            if (!kind.HasValue)
            {
                value = default;
                return false;
            }

            CombatActor subject = signal.Actor?.ServerCombatIdentity ?? CombatActor.None;
            uint entityId = signal.Subject == WorldSubjectKind.Match ? 0 : identity;
            // WorldEvent.Id is a producer-local journal identity. Semantic IDs
            // belong exclusively to the match dispatcher so combat/world/lifecycle
            // producers cannot collide.
            value = new(0, tick, matchId, phaseRevision, kind.Value, subject,
                CombatActor.None, entityId, signal.Team, signal.A);
            return value.IsValid;
        }
    }
}
