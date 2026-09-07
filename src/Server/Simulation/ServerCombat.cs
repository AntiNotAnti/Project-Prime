using System;
using MphRead.Entities;
using OpenTK.Mathematics;

namespace MphRead.Mods.Network
{
    /// <summary>Single simulation owner collects resolved facts in a fixed journal. No packet callback mutates it.</summary>
    public sealed class ServerCombat : ICombatAuthority
    {
        public uint? CollisionTick => CatchUp.CollisionTick;
        public void EnqueueCatchUp(BeamProjectileEntity beam, bool inherited) => CatchUp.Enqueue(beam, inherited);
        public bool TryGetHomingTarget(EntityBase entity, uint tick, CombatActor expected,
            out Vector3 position, out CombatActor identity)
            => HistoricalHomingTarget.TryGet(this, entity, tick, expected, out position, out identity);
        bool ICombatAuthority.IsStaleActor(in CombatActor actor) => actor.IsValid
            && (actor.Slot >= PlayerEntity.Players.Count
                || PlayerEntity.Players[actor.Slot].ServerCombatIdentity.ConnectionId != actor.ConnectionId);
        bool ICombatAuthority.IsStaleSource(EntityBase? source)
        {
            CombatShot shot = source switch
            {
                BeamProjectileEntity beam => beam.CombatShot,
                BombEntity bomb => bomb.CombatShot,
                _ => default
            };
            return ((ICombatAuthority)this).IsStaleActor(shot.Actor);
        }
        public const int Capacity = 1024;
        [ThreadStatic] private static ServerCombat? _current;
        public static ServerCombat? Current => _current;
        private readonly CombatEvent[] _events = new CombatEvent[Capacity];
        private readonly InputCommand[] _commands = new InputCommand[8];
        private readonly double[] _rtt = new double[8];
        private int _head, _count;
        private uint _nextId;
        private readonly uint _initialSpreadSeed;
        private uint _spreadSeed;
        public LagCompensationHistory History { get; } = new();
        public ProjectileCatchUp CatchUp { get; }
        public bool LagCompEnabled { get; }
        public bool ProjectileCatchUpEnabled { get; }
        public ServerCombat(bool lagCompEnabled = true, bool projectileCatchUpEnabled = true, uint? spreadSeed = null)
        {
            _initialSpreadSeed = _spreadSeed = spreadSeed ?? Rng.Rng2;
            LagCompEnabled = lagCompEnabled;
            ProjectileCatchUpEnabled = lagCompEnabled && projectileCatchUpEnabled;
            CatchUp = new ProjectileCatchUp(this);
        }
        // Only accepted spreading root shots advance this match-owned stream.
        // Damage and effects still consume the ordinary gameplay RNG independently.
        public uint NextSpreadSeed()
        {
            Rng.CallRng(ref _spreadSeed, 0);
            return _spreadSeed;
        }
        public LagCompensationMode GetMode(in BeamMechanics mechanics)
        {
            if (!LagCompEnabled) return LagCompensationMode.None;
            var mode = LagCompensationPolicy.GetMode(mechanics);
            return (mode is LagCompensationMode.ProjectileCatchUp or LagCompensationMode.HomingProjectileCatchUp) && !ProjectileCatchUpEnabled
                ? LagCompensationMode.None : mode;
        }
        public uint Tick { get; private set; }
        public int Count => _count;
        public long Dropped { get; private set; }
        public long ShotsConsidered { get; private set; }
        public long ShotsEligible { get; private set; }
        public long ShotsRewound { get; private set; }
        public long ShotsClamped { get; private set; }
        // Both samples include every eligible root beam action, including zero
        // rewind. Future/ambiguous requests contribute zero requested ticks.
        public NetSample RequestedRewindTicks;
        public NetSample ValidatedRewindTicks;
        public Scope Enter(uint tick) { Tick = tick; return new Scope(this); }
        public void SetCommand(int slot, in InputCommand command, double rttMs = 0)
        {
            if ((uint)slot >= 8) throw new ArgumentOutOfRangeException(nameof(slot));
            _commands[slot] = command; _rtt[slot] = rttMs;
        }
        public InputCommand GetCommand(int slot) => _commands[slot];
        public int CopyPending(Span<CombatEvent> destination)
        {
            int count = Math.Min(destination.Length, _count);
            for (int i = 0; i < count; i++) destination[i] = _events[(_head + i) % Capacity];
            return count;
        }
        public void Consume(int count)
        {
            if (count < 0 || count > _count) throw new ArgumentOutOfRangeException(nameof(count));
            _head = (_head + count) % Capacity; _count -= count;
        }
        public void Reset()
        {
            _head = _count = 0; _nextId = 0; Dropped = 0;
            _spreadSeed = _initialSpreadSeed;
            Array.Clear(_commands); Array.Clear(_rtt); History.Clear(); CatchUp.Clear();
            ShotsConsidered = ShotsEligible = ShotsRewound = ShotsClamped = 0;
            RequestedRewindTicks = ValidatedRewindTicks = default;
        }
        public bool TryRecord(in CombatEvent value)
        {
            if (!value.IsValid) return false;
            if (_count == Capacity) { Dropped++; return false; }
            _events[(_head + _count++) % Capacity] = value with { Id = _nextId++, Tick = Tick };
            return true;
        }
        private static CombatActor GetActor(EntityBase owner)
            => (owner as PlayerEntity ?? (owner as HalfturretEntity)?.Owner)?.ServerCombatIdentity ?? default;

        // Contact damage and bombs need immutable attribution, not a new timed
        // beam action. They must not alter shot counters or resolve rewind.
        public CombatShot CaptureAttribution(EntityBase owner) => CaptureAttribution(GetActor(owner));

        internal CombatShot CaptureAttribution(CombatActor actor)
        {
            if (!actor.IsValid) return default;
            InputCommand command = _commands[actor.Slot];
            return new(actor, command.Sequence, Tick, command.ViewServerTick, Tick, 0);
        }

        public CombatShot CaptureShot(EntityBase owner, in BeamMechanics mechanics)
            => CaptureShot(GetActor(owner), mechanics);

        internal CombatShot CaptureShot(CombatActor actor, in BeamMechanics mechanics)
        {
            CombatShot shot = CaptureAttribution(actor);
            if (!shot.IsValid) return default;
            ShotsConsidered++;
            LagCompensationMode mode = GetMode(mechanics);
            if (mode == LagCompensationMode.None) return shot;
            ShotsEligible++;
            LagCompensationTime time = LagCompensationPolicy.ResolveTick(Tick, shot.ViewServerTick, _rtt[actor.Slot]);
            uint requested = unchecked(Tick - shot.ViewServerTick);
            RequestedRewindTicks.Record(requested < 0x80000000u ? requested : 0);
            ValidatedRewindTicks.Record(time.RewindTicks);
            if (time.RewindTicks > 0) ShotsRewound++;
            if (time.Clamped) ShotsClamped++;
            return shot with { ActionServerTick = time.Tick, RewindTicks = time.RewindTicks, Mode = mode };
        }
        // A failed historical identity lookup is not permission to test a
        // replacement's live collider. The completed current endpoint is explicit.
        public bool TryGetPlayerCollider(PlayerEntity player, in CombatShot shot, out LagCompensationState state)
        {
            uint queryTick = CatchUp.CollisionTick ?? (shot.Mode == LagCompensationMode.HistoricalTrace
                && shot.RewindTicks > 0 ? shot.GetHistoricalTick(Tick) : Tick);
            CombatActor identity = player.ServerCombatIdentity;
            if (queryTick == Tick)
            {
                state = LagCompensationState.Capture(player, identity.ConnectionId, identity.Life);
                return state.CanBeHit;
            }
            return History.TryGet(player.SlotIndex, queryTick, identity.ConnectionId, identity.Life, out state)
                && state.CanBeHit;
        }

        public void NoteShot(in CombatShot shot, BeamType weapon, bool charged, Vector3 position, Vector3 direction, ushort chargeLevel = 0, bool affinity = false, uint spreadSeed = 0)
        {
            if (!shot.IsValid) return;
            TryRecord(new(0, Tick, shot.CommandSequence, CombatEventKind.Shot, (byte)weapon,
                (charged ? CombatEventFlags.Charged : 0) | (affinity ? CombatEventFlags.Affinity : 0),
                shot.Actor, CombatActor.None, 0, 0, position, direction, 0, 0, 0, chargeLevel, spreadSeed));
        }
        public void NoteBomb(in CombatShot shot, BombType type, Vector3 position, Vector3 facing)
        {
            if (!shot.IsValid) return;
            TryRecord(new(0, Tick, shot.CommandSequence, CombatEventKind.Bomb, (byte)type,
                0, shot.Actor, CombatActor.None, 0, 0, position, facing, 0, 0, 0));
        }
        public void NoteSpawn(PlayerEntity player)
        {
            CombatActor actor = player.ServerCombatIdentity;
            if (!actor.IsValid) return;
            TryRecord(new(0, Tick, 0, CombatEventKind.Spawn, 255, 0, actor, actor,
                (ushort)player.Health, 0, player.Position, player.FacingVector, 0, 0, 0));
        }
        public void NoteDamage(PlayerEntity victim, EntityBase? source, PlayerEntity? attacker, BeamType weapon,
            DamageFlags flags, Vector3? direction, int previousHealth, ushort frozen, ushort burn, ushort disrupt,
            bool afflictionChanged)
        {
            CombatActor target = victim.ServerCombatIdentity;
            if (!target.IsValid) return;
            CombatShot shot = source switch
            {
                BeamProjectileEntity beam => beam.CombatShot,
                BombEntity bomb => bomb.CombatShot,
                _ => attacker == null ? default : CaptureAttribution(attacker)
            };
            if (flags.TestFlag(DamageFlags.Burn) && victim.CombatBurnSource.IsValid) shot = victim.CombatBurnSource;
            CombatActor actor = shot.IsValid ? shot.Actor : CombatActor.None;
            CombatEventFlags eventFlags = 0;
            if (flags.TestFlag(DamageFlags.Headshot)) eventFlags |= CombatEventFlags.Headshot;
            if (flags.TestFlag(DamageFlags.Burn)) eventFlags |= CombatEventFlags.Burn;
            if (flags.TestFlag(DamageFlags.Deathalt)) eventFlags |= CombatEventFlags.Deathalt;
            if (flags.TestFlag(DamageFlags.NoSfx)) eventFlags |= CombatEventFlags.Silent;
            ushort amount = (ushort)Math.Clamp(previousHealth - victim.Health, 0, UInt16.MaxValue);
            CombatEvent value = new(0, Tick, shot.CommandSequence, CombatEventKind.Damage,
                weapon == BeamType.None ? (byte)255 : (byte)weapon, eventFlags, actor, target,
                (ushort)Math.Clamp(victim.Health, 0, UInt16.MaxValue), amount, victim.Position,
                direction ?? Vector3.Zero, frozen, burn, disrupt);
            if (amount > 0) TryRecord(value);
            if (previousHealth > 0 && victim.Health == 0) TryRecord(value with { Kind = CombatEventKind.Death });
            if (afflictionChanged) TryRecord(value with { Kind = CombatEventKind.Affliction, Amount = 0 });
        }
        public static bool IsStaleActor(in CombatActor actor)
            => _current != null && actor.IsValid && (actor.Slot >= PlayerEntity.Players.Count
                || PlayerEntity.Players[actor.Slot].ServerCombatIdentity.ConnectionId != actor.ConnectionId);

        public static bool IsStaleSource(EntityBase? source)
        {
            if (_current == null) return false;
            CombatShot shot = source switch
            {
                BeamProjectileEntity beam => beam.CombatShot,
                BombEntity bomb => bomb.CombatShot,
                _ => default
            };
            if (!shot.IsValid) return false;
            // A projectile can outlive a death; it cannot transfer to a replacement connection.
            return IsStaleActor(shot.Actor);
        }
        public readonly struct Scope : IDisposable
        {
            private readonly ServerCombat? _previous;
            internal Scope(ServerCombat current) { _previous = _current; _current = current; }
            public void Dispose() { _current = _previous; }
        }
    }
}
