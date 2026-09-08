using System;
using System.Collections.Immutable;
using MphRead.Combat;
using MphRead.Entities;
using OpenTK.Mathematics;

namespace MphRead.Mods.Network
{
    /// <summary>Single simulation owner collects resolved facts in a fixed journal. No packet callback mutates it.</summary>
    public sealed class ServerCombat : ICombatAuthority
    {
        private Scene? _scene;
        public void BindScene(Scene scene)
        {
            if (_scene != null && !ReferenceEquals(_scene, scene))
                throw new InvalidOperationException("Combat authority already belongs to another scene.");
            _scene = scene;
        }
        public uint? CollisionTick => CatchUp.CollisionTick;
        public void EnqueueCatchUp(BeamProjectileEntity beam, bool inherited) => CatchUp.Enqueue(beam, inherited);
        public bool TryGetHomingTarget(EntityBase entity, uint tick, CombatActor expected,
            out Vector3 position, out CombatActor identity)
            => HistoricalHomingTarget.TryGet(this, entity, tick, expected, out position, out identity);
        bool ICombatAuthority.IsStaleActor(in CombatActor actor) => _scene != null && actor.IsValid
            && (actor.Slot >= _scene.Players.Count
                || _scene.Players[actor.Slot].ServerCombatIdentity.ConnectionId != actor.ConnectionId);
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
        private readonly DamageContributionLedger[] _damage = new DamageContributionLedger[8];
        private readonly KillEvent[] _kills = new KillEvent[Capacity];
        private int _killHead, _killCount;
        private readonly CombatEvent[] _events = new CombatEvent[Capacity];
        private readonly InputCommand[] _commands = new InputCommand[8];
        private readonly double[] _rtt = new double[8];
        private int _head, _count;
        private uint _nextId;
        private readonly uint _initialSpreadSeed;
        private uint _spreadSeed;
        public ServerWorldEvents World { get; } = new();
        internal uint NextPresentationId() => _nextId++;
        public LagCompensationHistory History { get; } = new();
        public ProjectileCatchUp CatchUp { get; }
        public bool LagCompEnabled { get; }
        public bool ProjectileCatchUpEnabled { get; }
        public ServerCombat(bool lagCompEnabled = true, bool projectileCatchUpEnabled = true, uint? spreadSeed = null)
        {
            for (int i = 0; i < _damage.Length; i++) _damage[i] = new();
            _initialSpreadSeed = _spreadSeed = spreadSeed ?? Rng.Rng2StartValue;
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
        public void BeginTick(uint tick) { Tick = tick; }
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
            World.Reset();
            foreach (var ledger in _damage) ledger.Reset();
            _killHead = _killCount = 0;
            Array.Clear(_kills);
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
        public CombatShot CaptureAttribution(EntityBase owner)
        {
            BindScene(owner._scene);
            CombatShot shot = CaptureAttribution(GetActor(owner));
            return owner is HalfturretEntity ? shot with { SourceAltForm = true } : shot;
        }

        internal CombatShot CaptureAttribution(CombatActor actor)
        {
            if (!actor.IsValid) return default;
            InputCommand command = _commands[actor.Slot];
            return new(actor, command.Sequence, Tick, command.ViewServerTick, Tick, 0)
            { SourceAltForm = _scene?.Players[actor.Slot].IsAltForm == true };
        }

        public CombatShot CaptureShot(EntityBase owner, in BeamMechanics mechanics)
        {
            BindScene(owner._scene);
            CombatShot shot = CaptureShot(GetActor(owner), mechanics);
            return owner is HalfturretEntity ? shot with { SourceAltForm = true } : shot;
        }

        internal CombatShot CaptureShot(CombatActor actor, in BeamMechanics mechanics)
        {
            CombatShot shot = CaptureAttribution(actor) with { SourceWeapon = (byte)mechanics.Beam };
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
        public bool TryPeekKill(out KillEvent value)
        {
            value = _killCount == 0 ? default : _kills[_killHead];
            return _killCount > 0;
        }
        public void ConsumeKill()
        {
            if (_killCount == 0) throw new InvalidOperationException("No pending kill.");
            _kills[_killHead] = default;
            _killHead = (_killHead + 1) % Capacity;
            _killCount--;
        }
        public void NoteHealing(PlayerEntity player, int amount)
            => _damage[player.SlotIndex].Heal(player.ServerCombatIdentity, amount);

        public void NoteSpawn(PlayerEntity player)
        {
            CombatActor actor = player.ServerCombatIdentity;
            if (!actor.IsValid) return;
            _damage[player.SlotIndex].Reset(actor);
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
            if (flags.TestFlag(DamageFlags.Burn) && victim.CombatBurnSource.IsValid)
            {
                shot = victim.CombatBurnSource;
                if (weapon == BeamType.None && shot.SourceWeapon <= 10) weapon = (BeamType)shot.SourceWeapon;
            }
            CombatActor actor = shot.IsValid ? shot.Actor : CombatActor.None;
            CombatEventFlags eventFlags = 0;
            if (flags.TestFlag(DamageFlags.Headshot)) eventFlags |= CombatEventFlags.Headshot;
            if (flags.TestFlag(DamageFlags.Burn)) eventFlags |= CombatEventFlags.Burn;
            if (flags.TestFlag(DamageFlags.Deathalt)) eventFlags |= CombatEventFlags.Deathalt;
            if (flags.TestFlag(DamageFlags.NoSfx)) eventFlags |= CombatEventFlags.Silent;
            if (shot.Affinity) eventFlags |= CombatEventFlags.Affinity;
            ushort amount = (ushort)Math.Clamp(previousHealth - victim.Health, 0, UInt16.MaxValue);
            CombatEvent value = new(0, Tick, shot.CommandSequence, CombatEventKind.Damage,
                weapon == BeamType.None ? (byte)255 : (byte)weapon, eventFlags, actor, target,
                (ushort)Math.Clamp(victim.Health, 0, UInt16.MaxValue), amount, victim.Position,
                direction ?? Vector3.Zero, frozen, burn, disrupt);
            bool sameConnection = actor.IsValid && actor.Slot < victim._scene.Players.Count
                && victim._scene.Players[actor.Slot].ServerCombatIdentity.ConnectionId == actor.ConnectionId;
            bool currentActor = sameConnection && victim._scene.Players[actor.Slot].ServerCombatIdentity == actor;
            bool teamDamage = sameConnection && victim._scene.Match.Rules.Teams
                && victim._scene.Players[actor.Slot].TeamIndex == victim.TeamIndex;
            if (sameConnection && actor.Slot != target.Slot && !teamDamage && amount > 0)
            {
                PlayerMatchStats stats = victim._scene.Match.Players[actor.Slot];
                stats.DamageDealt = (int)Math.Min(int.MaxValue, (long)stats.DamageDealt + amount);
            }
            _damage[target.Slot].Add(target, actor, Tick, amount, currentActor && !teamDamage);
            if (amount > 0) TryRecord(value);
            if (previousHealth > 0 && victim.Health == 0)
            {
                if (sameConnection && actor.Slot != target.Slot && !teamDamage)
                {
                    PlayerMatchStats stats = victim._scene.Match.Players[actor.Slot];
                    if (shot.SourceAltForm) { if (stats.AltFormKills < int.MaxValue) stats.AltFormKills++; }
                    else if (stats.BipedKills < int.MaxValue) stats.BipedKills++;
                }
                TryRecord(value with { Kind = CombatEventKind.Death });
                RecordKill(victim, sameConnection ? actor : CombatActor.None, value, teamDamage,
                    weapon != BeamType.None ? KillSourceKind.Beam : source is BombEntity ? KillSourceKind.Bomb
                    : source is PlayerEntity or HalfturretEntity && attacker != null && !flags.TestFlag(DamageFlags.Death)
                        ? KillSourceKind.Alt : KillSourceKind.Environment);
            }
            if (afflictionChanged) TryRecord(value with { Kind = CombatEventKind.Affliction, Amount = 0 });
        }
        private void RecordKill(PlayerEntity victim, CombatActor killer, in CombatEvent damage, bool teamDamage, KillSourceKind sourceKind)
        {
            MatchRuntime match = victim._scene.Match;
            Span<CombatActor> candidates = stackalloc CombatActor[8];
            int count = teamDamage ? 0 : _damage[victim.SlotIndex].Collect(killer, Tick,
                match.Rules.AssistMinimumDamage, (uint)match.Rules.AssistWindowTicks, candidates);
            var assists = ImmutableArray.CreateBuilder<CombatActor>();
            for (int i = 0; i < count; i++)
            {
                CombatActor actor = candidates[i];
                if (actor.Slot >= victim._scene.Players.Count || victim._scene.Players[actor.Slot].ServerCombatIdentity != actor
                    || (match.Rules.Teams && victim._scene.Players[actor.Slot].TeamIndex == victim.TeamIndex)) continue;
                assists.Add(actor);
                if (match.Players[actor.Slot].Assists < int.MaxValue) match.Players[actor.Slot].Assists++;
            }
            _damage[victim.SlotIndex].Reset();
            KillEventFlags flags = 0;
            if ((damage.Flags & CombatEventFlags.Headshot) != 0) flags |= KillEventFlags.Headshot;
            if ((damage.Flags & CombatEventFlags.Burn) != 0) flags |= KillEventFlags.Burn;
            if ((damage.Flags & CombatEventFlags.Deathalt) != 0) flags |= KillEventFlags.Deathalt;
            if ((damage.Flags & CombatEventFlags.Affinity) != 0) flags |= KillEventFlags.Affinity;
            bool suicide = killer.IsValid && killer.Slot == damage.Target.Slot
                && killer.ConnectionId == damage.Target.ConnectionId;
            if (suicide) flags |= KillEventFlags.Suicide;
            if (teamDamage && !suicide) flags |= KillEventFlags.TeamKill;
            var value = new KillEvent(_nextId++, Tick, match.MatchId, match.PhaseRevision,
                killer, damage.Target, damage.Weapon, flags, assists.ToImmutable(), sourceKind);
            // Diagnostic scenes may not own a network match identity.
            if (match.MatchId == 0) return;
            if (!value.IsValid) throw new InvalidOperationException("Invalid authoritative kill attribution.");
            if (_killCount == Capacity) throw new InvalidOperationException("Authoritative kill journal exhausted.");
            _kills[(_killHead + _killCount++) % Capacity] = value;
        }

    }
}
