using System;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.Telemetry
{
    /// <summary>Single simulation owner; fixed storage and one-second route sampling.</summary>
    public sealed class TelemetryCollector
    {
        private readonly TelemetryEvent[] _events;
        private readonly SnapshotPlayer[] _players = new SnapshotPlayer[8];
        private readonly CombatEvent?[] _initialSpawns = new CombatEvent?[8];
        private readonly uint[] _epochs = new uint[8];
        private readonly CombatActor[] _actors = new CombatActor[8];
        private readonly Guid _id = Guid.NewGuid();
        private readonly MatchRules _rules;
        private readonly uint _matchId;
        private uint _start, _end;
        private bool _started, _completed;
        private int _count, _dropped;
        public int DroppedEvents => _dropped;
        public long AwardsRecorded { get; private set; }
        public long AwardsDropped { get; private set; }
        public long SemanticEventsRecorded { get; private set; }
        public long SemanticEventsDropped { get; private set; }
        // MatchAwardKind is a one-based wire enum. Keep the telemetry array
        // zero-based so every plan-defined kind has one unambiguous slot.
        private readonly long[] _awardsByKind = new long[8];
        public ReadOnlySpan<long> AwardsByKind => _awardsByKind;

        public TelemetryCollector(MatchRules rules, uint matchId, uint start, int capacity = MatchTelemetry.MaxEvents)
        {
            if (capacity is < 1 or > MatchTelemetry.MaxEvents) throw new ArgumentOutOfRangeException(nameof(capacity));
            _rules = rules; _matchId = matchId; _start = start;
            _events = new TelemetryEvent[capacity];
        }

        public void Sample(uint tick, ReadOnlySpan<SnapshotPlayer> players, bool playing = true, Scene? scene = null)
        {
            if (_completed) return;
            if (!_started && playing) { _started = true; _start = tick; }
            Array.Clear(_players);
            int seen = 0;
            foreach (ref readonly SnapshotPlayer player in players)
            {
                if (player.Slot >= 8) continue;
                seen |= 1 << player.Slot;
                var actor = new CombatActor(player.Slot, player.ConnectionId, player.Life);
                if (actor != _actors[player.Slot]) { _epochs[player.Slot]++; _actors[player.Slot] = actor; }
                _players[player.Slot] = player;
                if (playing && tick % 60 == 0 && player.Health > 0)
                    Add(At(tick, TelemetryKind.Position, player.Slot, player.Life, player.Position));
            }
            // Missing actors must not inherit zeroed snapshot metadata. Epoch
            // counters survive absence, but attribution requires a current sample.
            for (int slot = 0; slot < _actors.Length; slot++)
                if ((seen & (1 << slot)) == 0) _actors[slot] = CombatActor.None;
            if (_started && scene != null)
                for (int slot = 0; slot < _initialSpawns.Length; slot++)
                {
                    if (_initialSpawns[slot] is { } initial && initial.Target == _actors[slot])
                        Combat(initial with { Tick = tick }, scene);
                    _initialSpawns[slot] = null;
                }
        }

        public void Combat(in CombatEvent value, Scene scene)
        {
            if (value.Kind is not (CombatEventKind.Spawn or CombatEventKind.Damage)) return;
            if (!_started)
            {
                if (value.Kind == CombatEventKind.Spawn && value.Target.Slot < 8) _initialSpawns[value.Target.Slot] = value;
                return;
            }
            TelemetryEvent entry = AtActor(value.Tick, value.Kind == CombatEventKind.Spawn ? TelemetryKind.Spawn : TelemetryKind.Damage,
                value.Target, value.Position) with
                { Weapon = value.Weapon, Value = value.Amount, OtherSlot = value.Actor.Slot,
                    OtherHunter = value.Actor.Slot < 8 && _actors[value.Actor.Slot] == value.Actor ? (byte)_players[value.Actor.Slot].Hunter : (byte)255 };
            if (value.Kind == CombatEventKind.Spawn)
            {
                int enemies = 0, visible = 0;
                float distance = 0;
                foreach (PlayerEntity player in scene.GetPlayerEntities())
                {
                    if (player.SlotIndex == value.Target.Slot || player.Health <= 0 || !player.LoadFlags.TestFlag(LoadFlags.Active)
                        || _rules.Teams && player.TeamIndex == entry.Team) continue;
                    float separation = (player.Position - value.Position).Length;
                    if (!float.IsFinite(separation)) continue;
                    distance += separation;
                    enemies++;
                    CollisionResult hit = default;
                    if (!CollisionDetection.CheckBetweenPoints(player.Position.AddY(.5f), value.Position.AddY(.5f),
                        TestFlags.None, scene, ref hit)) visible++;
                }
                entry = entry with { EnemyDistance = enemies == 0 ? null : distance / enemies, VisibleEnemies = visible };
            }
            Add(entry);
        }

        public void Kill(in KillEvent value)
        {
            if (!_started || _completed) return;
            // Kill payloads carry identity but no position. Never fabricate an
            // origin heatmap point when its victim no longer has a current sample.
            if (value.Victim.Slot >= 8 || _actors[value.Victim.Slot] != value.Victim)
            { if (_dropped < int.MaxValue) _dropped++; return; }
            if (value.Victim.Slot < 8)
                Add(AtActor(value.Tick, TelemetryKind.Death, value.Victim, _players[value.Victim.Slot].Position)
                    with { Weapon = value.Weapon, OtherSlot = value.Killer.Slot, Value = (int)value.Flags });
            if (value.Killer.IsValid && !value.IsSuicide)
                Add(AtActor(value.Tick, TelemetryKind.Kill, value.Killer, _players[value.Victim.Slot].Position)
                    with { Weapon = value.Weapon, OtherSlot = value.Victim.Slot, Value = (int)value.Flags });
        }

        public void World(in WorldEvent value) => Add(AtActor(value.Tick, TelemetryKind.World, value.Actor, value.Position)
            with { Team = value.Team, Value = (int)value.Kind, Subject = value.EntityId, Weapon = (byte)value.A });

        /// <summary>Records the raw server semantic fact. No award is
        /// recomputed from kill/world snapshots and the counters are bounded
        /// to the eight plan-defined kinds.</summary>
        public void Award(in MatchAward value)
        {
            int kindIndex = (int)value.Kind - (int)MatchAwardKind.FirstHunt;
            if (!value.IsValid || (uint)kindIndex >= (uint)_awardsByKind.Length)
            {
                if (AwardsDropped < long.MaxValue) AwardsDropped++;
                if (_dropped < int.MaxValue) _dropped++;
                return;
            }
            byte team = value.Subject.Slot < 8 && _actors[value.Subject.Slot] == value.Subject
                ? _players[value.Subject.Slot].TeamIndex : (byte)255;
            int before = _count;
            Add(new TelemetryEvent(value.Tick, TelemetryKind.Award, value.Subject.Slot, value.Subject.Life,
                0, 0, 0, team, 255, 255, (int)value.Kind, value.SourceEventId,
                value.Target.IsValid ? value.Target.Slot : (byte)255));
            if (_count == before)
            {
                if (AwardsDropped < long.MaxValue) AwardsDropped++;
                return;
            }
            if (AwardsRecorded < long.MaxValue) AwardsRecorded++;
            if (_awardsByKind[kindIndex] < long.MaxValue) _awardsByKind[kindIndex]++;
        }

        /// <summary>Records the normalized match fact independently of the
        /// existing low-level combat/world telemetry. Semantic IDs remain the
        /// correlation key for awards and replay metadata.</summary>
        public void Semantic(in MatchEvent value)
        {
            if (!value.IsValid || value.Id == 0)
            {
                if (SemanticEventsDropped < long.MaxValue) SemanticEventsDropped++;
                if (_dropped < int.MaxValue) _dropped++;
                return;
            }
            if (_completed || _count == _events.Length)
            {
                if (SemanticEventsDropped < long.MaxValue) SemanticEventsDropped++;
                if (_dropped < int.MaxValue) _dropped++;
                return;
            }
            _events[_count++] = new TelemetryEvent(value.Tick, TelemetryKind.MatchSemantic,
                value.Subject.IsValid ? value.Subject.Slot : (byte)255,
                value.Subject.IsValid ? value.Subject.Life : 0,
                0, 0, 0, value.Team, 255, (byte)value.Flags,
                (int)value.Kind, value.EntityId,
                value.Target.IsValid ? value.Target.Slot : (byte)255,
                SemanticId: value.Id);
            if (SemanticEventsRecorded < long.MaxValue) SemanticEventsRecorded++;
        }

        private TelemetryEvent AtActor(uint tick, TelemetryKind kind, CombatActor actor, Vector3 position)
        {
            TelemetryEvent entry = At(tick, kind, actor.Slot, actor.Life, position);
            return actor.Slot < 8 && _actors[actor.Slot] == actor ? entry
                : entry with { Life = 0, Team = 255, Hunter = 255 };
        }

        private TelemetryEvent At(uint tick, TelemetryKind kind, byte slot, uint life, Vector3 position)
            => new(tick, kind, slot, slot < 8 && _actors[slot].Life == life ? _epochs[slot] : 0, position.X, position.Y, position.Z,
                slot < 8 && _actors[slot].Life == life ? _players[slot].TeamIndex : (byte)255,
                slot < 8 && _actors[slot].Life == life ? (byte)_players[slot].Hunter : (byte)255);

        private void Add(TelemetryEvent entry)
        {
            if (!_started || _completed) return;
            if (!float.IsFinite(entry.X) || !float.IsFinite(entry.Y) || !float.IsFinite(entry.Z))
            { if (_dropped < int.MaxValue) _dropped++; return; }
            if (_count == _events.Length) { if (_dropped < int.MaxValue) _dropped++; return; }
            _events[_count++] = entry;
        }

        public void CommitTick(uint tick, bool completed)
        {
            if (_started && !_completed) { _end = tick; _completed = completed; }
        }

        // Called once at rotation/shutdown, never per tick. The worker owns this copy.
        public MatchTelemetry Complete(uint tick, bool completed, Guid? reportId = null)
            => new(MatchTelemetry.CurrentFormat, reportId is { } id && id != Guid.Empty ? id : _id, _rules.RoomKey, _rules.Mode, _matchId, _start, _completed ? _end : tick,
                _completed && completed, _dropped, _events.AsSpan(0, _count).ToArray());
    }
}
