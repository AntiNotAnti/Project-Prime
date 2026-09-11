using System;
using MphRead.Entities;
using OpenTK.Mathematics;

namespace MphRead.Mods.Network
{
    /// <summary>
    /// Authoritative identity for a client-side projectile presentation
    /// candidate. MatchId is the owning presentation epoch; Actor, command,
    /// and shot index are the server-owned identity. Local entity references
    /// are deliberately not part of this type.
    /// </summary>
    public readonly record struct ProjectilePresentationIdentity(
        uint MatchId, CombatActor Actor, uint CommandSequence, ushort ShotIndex)
    {
        public bool IsValid => MatchId != 0 && Actor.IsValid;
    }

    /// <summary>Bounded counters and correction aggregates for one presentation epoch.</summary>
    public readonly record struct ProjectilePresentationMeasurementSnapshot(
        long PredictedShotCreated,
        long AuthoritativeShotObserved,
        long AuthoritativeMissileShotObserved,
        long AuthoritativeJudicatorShotObserved,
        long AuthoritativeShotMatched,
        long VisualDuplicate,
        double VisualCorrectionDistance,
        double VisualCorrectionAngle,
        long AdoptionCandidateMiss,
        long CorrectionSamples,
        long CorrectionAngleSamples,
        double VisualCorrectionDistanceMax,
        double VisualCorrectionAngleMax,
        int PendingCandidates)
    {
        public double MatchRate => PredictedShotCreated == 0
            ? 0
            : AuthoritativeShotMatched / (double)PredictedShotCreated;

        public double MeanCorrectionDistance => CorrectionSamples == 0
            ? 0
            : VisualCorrectionDistance / CorrectionSamples;

        public double MeanCorrectionAngle => CorrectionAngleSamples == 0
            ? 0
            : VisualCorrectionAngle / CorrectionAngleSamples;

    }

    /// <summary>
    /// Measurement-only registry for local predicted projectile presentation.
    ///
    /// This registry never adopts, moves, collides, damages, or otherwise
    /// owns a projectile. It records an immutable spawn observation and later
    /// compares it with the authoritative Shot presentation fact. All storage
    /// is fixed-size and intended to run on the game thread.
    /// </summary>
    public sealed class ProjectilePresentationMeasurement
    {
        public const int DefaultCapacity = 128;
        public const int DefaultWindowFrames = 30;
        public const int MaxCapacity = 256;
        public const int MaxWindowFrames = 120;
        public const int MaxVisualsPerShot = 16;

        private readonly PendingCandidate[] _pending;
        private readonly OrdinalEntry[] _predictedOrdinals;
        private readonly OrdinalEntry[] _authoritativeOrdinals;
        private readonly RetiredEntry[] _retired;
        private readonly SeenIdEntry[] _seenAuthoritativeIds;
        private readonly AuthorityIdentityEntry[] _authorityIdentities;
        private readonly int _capacity;
        private readonly int _windowFrames;
        private int _pendingCount;
        private int _retiredCursor;
        private int _seenIdCursor;
        private int _authorityIdentityCursor;
        private long _clock;
        private uint _matchId;
        private CombatActor _localActor;

        public ProjectilePresentationMeasurement(
            int capacity = DefaultCapacity, int windowFrames = DefaultWindowFrames)
        {
            _capacity = Math.Clamp(capacity, 1, MaxCapacity);
            _windowFrames = Math.Clamp(windowFrames, 1, MaxWindowFrames);
            _pending = new PendingCandidate[_capacity];
            // A command sequence should have one root Shot event. The extra
            // room handles deterministic multishot/child event experiments
            // without making the registry unbounded.
            int ordinalCapacity = Math.Max(16, _capacity);
            _predictedOrdinals = new OrdinalEntry[ordinalCapacity];
            _authoritativeOrdinals = new OrdinalEntry[ordinalCapacity];
            _retired = new RetiredEntry[_capacity];
            _seenAuthoritativeIds = new SeenIdEntry[Math.Max(16, _capacity)];
            _authorityIdentities = new AuthorityIdentityEntry[Math.Max(16, _capacity)];
        }

        public uint MatchId => _matchId;
        public CombatActor LocalActor => _localActor;
        public int Capacity => _capacity;
        public int WindowFrames => _windowFrames;
        public ProjectilePresentationMeasurementSnapshot Metrics => new(
            PredictedShotCreated, AuthoritativeShotObserved, AuthoritativeMissileShotObserved,
            AuthoritativeJudicatorShotObserved,
            AuthoritativeShotMatched, VisualDuplicate,
            VisualCorrectionDistance, VisualCorrectionAngle, AdoptionCandidateMiss,
            CorrectionSamples, CorrectionAngleSamples, VisualCorrectionDistanceMax,
            VisualCorrectionAngleMax, _pendingCount);

        public long PredictedShotCreated { get; private set; }
        public long AuthoritativeShotObserved { get; private set; }
        public long AuthoritativeMissileShotObserved { get; private set; }
        public long AuthoritativeJudicatorShotObserved { get; private set; }
        public long AuthoritativeShotMatched { get; private set; }
        public long VisualDuplicate { get; private set; }
        public double VisualCorrectionDistance { get; private set; }
        public double VisualCorrectionAngle { get; private set; }
        public long AdoptionCandidateMiss { get; private set; }
        public long CorrectionSamples { get; private set; }
        public long CorrectionAngleSamples { get; private set; }
        public double VisualCorrectionDistanceMax { get; private set; }
        public double VisualCorrectionAngleMax { get; private set; }

        /// <summary>
        /// Starts a new match/slot/life presentation epoch. Pending candidates
        /// are intentionally discarded without being counted as misses: a
        /// stale prediction must not be attributed to a reused slot or life.
        /// </summary>
        public void SetContext(uint matchId, in CombatActor localActor)
        {
            if (_matchId == matchId && _localActor == localActor)
                return;
            Clear();
            _matchId = matchId;
            _localActor = localActor;
        }

        /// <summary>Clears all candidates, identity history, and per-epoch metrics.</summary>
        public void Clear()
        {
            Array.Clear(_pending);
            Array.Clear(_predictedOrdinals);
            Array.Clear(_authoritativeOrdinals);
            Array.Clear(_retired);
            Array.Clear(_seenAuthoritativeIds);
            Array.Clear(_authorityIdentities);
            _pendingCount = 0;
            _retiredCursor = 0;
            _seenIdCursor = 0;
            _authorityIdentityCursor = 0;
            _clock = 0;
            _matchId = 0;
            _localActor = CombatActor.None;
            PredictedShotCreated = 0;
            AuthoritativeShotObserved = 0;
            AuthoritativeMissileShotObserved = 0;
            AuthoritativeJudicatorShotObserved = 0;
            AuthoritativeShotMatched = 0;
            VisualDuplicate = 0;
            VisualCorrectionDistance = 0;
            VisualCorrectionAngle = 0;
            AdoptionCandidateMiss = 0;
            CorrectionSamples = 0;
            CorrectionAngleSamples = 0;
            VisualCorrectionDistanceMax = 0;
            VisualCorrectionAngleMax = 0;
        }

        /// <summary>
        /// Advances the bounded observation window by one presentation frame.
        /// An unmatched candidate expiring is a rejected/missing prediction.
        /// </summary>
        public void Advance()
        {
            _clock++;
            for (int i = _pendingCount - 1; i >= 0; i--)
            {
                ref PendingCandidate candidate = ref _pending[i];
                if (_clock - candidate.CreatedClock < _windowFrames)
                    continue;
                if (!candidate.Matched)
                {
                    AdoptionCandidateMiss++;
                    Retire(candidate.Identity, matched: false);
                }
                else
                {
                    Retire(candidate.Identity, matched: true);
                }
                RemovePendingAt(i);
            }
        }

        /// <summary>
        /// Records one predicted root shot from a client-only spawn observation.
        /// The visualCount allows multishot weapons to be measured as one
        /// command-owned candidate because the current Shot wire fact has no
        /// per-pellet index.
        /// </summary>
        public bool RecordPredictedShot(CombatActor actor, uint commandSequence, byte weapon,
            Vector3 position, Vector3 direction, int visualCount = 1)
        {
            if (_matchId == 0 || actor != _localActor || !actor.IsValid
                || !Finite(position) || !Finite(direction) || visualCount <= 0)
                return false;

            // A repeated NoteFired observation for one command must not move
            // the candidate to a new ordinal. One command owns one root
            // identity; multiple local visuals are represented by visualCount.
            if (ContainsCommand(_pending, _pendingCount, actor, commandSequence)
                || ContainsCommand(_retired, actor, commandSequence))
                return false;

            ushort shotIndex = NextOrdinal(_predictedOrdinals, actor, commandSequence);
            ProjectilePresentationIdentity identity = new(_matchId, actor, commandSequence, shotIndex);
            if (FindPending(identity) >= 0 || ContainsRetired(identity))
                return false;

            if (_pendingCount == _capacity)
            {
                // The registry is deliberately bounded. Evict the oldest
                // observation and account for it as a missing candidate.
                int oldest = FindOldestPending();
                PendingCandidate evicted = _pending[oldest];
                if (!evicted.Matched)
                {
                    AdoptionCandidateMiss++;
                    Retire(evicted.Identity, matched: false);
                }
                else
                {
                    Retire(evicted.Identity, matched: true);
                }
                RemovePendingAt(oldest);
            }

            _pending[_pendingCount++] = new PendingCandidate
            {
                Identity = identity,
                Weapon = weapon,
                Position = position,
                Direction = direction,
                VisualCount = Math.Min(visualCount, MaxVisualsPerShot),
                CreatedClock = _clock,
                Matched = false,
                VisualSeen = false
            };
            PredictedShotCreated++;
            return true;
        }

        /// <summary>
        /// Scans the local scene immediately after the existing NoteFired hook.
        /// Only age-zero, live beams owned by the firing player are copied into
        /// an immutable observation. No projectile reference or local object
        /// identifier is retained as authority.
        /// </summary>
        public int ObservePredictedShot(PlayerEntity shooter, CombatActor actor,
            out CombatShot shot)
        {
            shot = default;
            if (shooter.Scene is not Scene scene)
                return 0;

            BeamProjectileEntity? first = null;
            int count = 0;
            foreach (BeamProjectileEntity beam in scene.GetBeamProjectileEntities())
            {
                if (!ReferenceEquals(beam.Owner, shooter) || beam.AgeTicks != 0
                    || beam.Lifespan <= 0 || beam.Flags.TestFlag(BeamFlags.Collided))
                    continue;
                first ??= beam;
                count++;
                if (count == MaxVisualsPerShot)
                    break;
            }
            if (first is null || !first.CombatShot.IsValid
                || first.CombatShot.Actor != actor)
                return 0;

            shot = first.CombatShot;
            return RecordPredictedShot(actor, shot.CommandSequence, (byte)first.Beam,
                first.Position, first.Direction, count) ? count : 0;
        }

        /// <summary>
        /// Observes one authoritative Shot presentation fact. The event ID is
        /// used only to ignore duplicate delivery; identity matching remains
        /// actor/life + command sequence + deterministic shot ordinal.
        /// </summary>
        public bool RecordAuthoritativeShot(in CombatEvent value)
        {
            if (value.Kind != CombatEventKind.Shot || !value.IsValid
                || value.Actor != _localActor || _matchId == 0)
                return false;
            if (!RememberAuthoritativeId(value.Id))
                return false;

            // These counters confirm the weapon ID carried by the production
            // authoritative Shot fact. They are measurement-only and do not
            // influence matching, presentation, or gameplay behavior.
            AuthoritativeShotObserved++;
            if (value.Weapon == (byte)BeamType.Missile)
                AuthoritativeMissileShotObserved++;
            if (value.Weapon == (byte)BeamType.Judicator)
                AuthoritativeJudicatorShotObserved++;

            ushort shotIndex = NextOrdinal(_authoritativeOrdinals, value.Actor, value.CommandSequence);
            ProjectilePresentationIdentity identity = new(_matchId, value.Actor,
                value.CommandSequence, shotIndex);
            RememberAuthorityIdentity(value.Id, identity);

            if (ContainsRetired(identity))
                return false;
            int index = FindPending(identity);
            if (index < 0)
            {
                AdoptionCandidateMiss++;
                Retire(identity, matched: false);
                return false;
            }

            ref PendingCandidate candidate = ref _pending[index];
            if (candidate.Weapon != 255 && candidate.Weapon != value.Weapon)
            {
                // A weapon switch/authoritative rejection must not match a
                // visually similar shot that happens to reuse the command.
                AdoptionCandidateMiss++;
                Retire(candidate.Identity, matched: false);
                RemovePendingAt(index);
                return false;
            }

            if (candidate.Matched)
                return false;
            candidate.Matched = true;
            AuthoritativeShotMatched++;

            float distance = (candidate.Position - value.Position).Length;
            if (Single.IsFinite(distance))
            {
                VisualCorrectionDistance += distance;
                VisualCorrectionDistanceMax = Math.Max(VisualCorrectionDistanceMax, distance);
                CorrectionSamples++;
            }

            if (TryAngleDegrees(candidate.Direction, value.Direction, out double angle))
            {
                VisualCorrectionAngle += angle;
                VisualCorrectionAngleMax = Math.Max(VisualCorrectionAngleMax, angle);
                CorrectionAngleSamples++;
            }
            return true;
        }

        /// <summary>
        /// Called by the existing presentation path immediately before it
        /// would spawn an authoritative visual. A local predicted event is
        /// expected to pass visualWillSpawn=false because that path already
        /// suppresses the echoed local Shot. Passing true is a measurement of
        /// a duplicate, never an instruction to adopt or correct a projectile.
        /// </summary>
        public bool ObserveAuthoritativeVisual(in CombatEvent value, bool visualWillSpawn)
        {
            if (!visualWillSpawn || value.Kind != CombatEventKind.Shot
                || value.Actor != _localActor || _matchId == 0)
                return false;
            if (!TryFindAuthorityIdentity(value.Id, out ProjectilePresentationIdentity identity))
                return false;
            int index = FindPending(identity);
            if (index < 0)
                return false;
            ref PendingCandidate candidate = ref _pending[index];
            if (!candidate.Matched || candidate.VisualSeen || candidate.VisualCount <= 0)
                return false;
            candidate.VisualSeen = true;
            VisualDuplicate++;
            return true;
        }

        private static bool Finite(Vector3 value)
            => Single.IsFinite(value.X) && Single.IsFinite(value.Y) && Single.IsFinite(value.Z);

        private static bool TryAngleDegrees(Vector3 first, Vector3 second, out double angle)
        {
            angle = 0;
            float firstLength = first.Length;
            float secondLength = second.Length;
            if (!Single.IsFinite(firstLength) || !Single.IsFinite(secondLength)
                || firstLength <= 0.0001f || secondLength <= 0.0001f)
                return false;
            float dot = Math.Clamp(Vector3.Dot(first / firstLength, second / secondLength), -1f, 1f);
            angle = Math.Acos(dot) * 180 / Math.PI;
            return Double.IsFinite(angle);
        }

        private static bool ContainsCommand(PendingCandidate[] entries, int count,
            CombatActor actor, uint commandSequence)
        {
            for (int i = 0; i < count; i++)
            {
                if (entries[i].Identity.Actor == actor
                    && entries[i].Identity.CommandSequence == commandSequence)
                    return true;
            }
            return false;
        }

        private bool ContainsCommand(RetiredEntry[] entries, CombatActor actor, uint commandSequence)
        {
            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i].Used && entries[i].Identity.Actor == actor
                    && entries[i].Identity.CommandSequence == commandSequence)
                    return true;
            }
            return false;
        }

        private int FindPending(in ProjectilePresentationIdentity identity)
        {
            for (int i = 0; i < _pendingCount; i++)
                if (_pending[i].Identity == identity)
                    return i;
            return -1;
        }

        private bool ContainsRetired(in ProjectilePresentationIdentity identity)
        {
            for (int i = 0; i < _retired.Length; i++)
                if (_retired[i].Used && _retired[i].Identity == identity)
                    return true;
            return false;
        }

        private void Retire(in ProjectilePresentationIdentity identity, bool matched)
        {
            for (int i = 0; i < _retired.Length; i++)
            {
                if (_retired[i].Used && _retired[i].Identity == identity)
                {
                    _retired[i].Matched |= matched;
                    return;
                }
            }
            _retired[_retiredCursor] = new RetiredEntry { Used = true, Identity = identity, Matched = matched };
            _retiredCursor = (_retiredCursor + 1) % _retired.Length;
        }

        private int FindOldestPending()
        {
            int oldest = 0;
            for (int i = 1; i < _pendingCount; i++)
                if (_pending[i].CreatedClock < _pending[oldest].CreatedClock)
                    oldest = i;
            return oldest;
        }

        private void RemovePendingAt(int index)
        {
            _pendingCount--;
            if (index < _pendingCount)
                _pending[index] = _pending[_pendingCount];
            _pending[_pendingCount] = default;
        }

        private static ushort NextOrdinal(OrdinalEntry[] entries, CombatActor actor, uint commandSequence)
        {
            int free = -1;
            int oldest = 0;
            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i].Used && entries[i].Actor == actor && entries[i].CommandSequence == commandSequence)
                {
                    ushort result = entries[i].Next;
                    entries[i].Next = result == UInt16.MaxValue ? result : (ushort)(result + 1);
                    return result;
                }
                if (!entries[i].Used && free < 0) free = i;
                if (entries[i].Used && entries[i].Next < entries[oldest].Next) oldest = i;
            }
            int index = free >= 0 ? free : oldest;
            entries[index] = new OrdinalEntry
            {
                Used = true, Actor = actor, CommandSequence = commandSequence, Next = 1
            };
            return 0;
        }

        private bool RememberAuthoritativeId(uint id)
        {
            for (int i = 0; i < _seenAuthoritativeIds.Length; i++)
                if (_seenAuthoritativeIds[i].Used && _seenAuthoritativeIds[i].Id == id)
                    return false;
            _seenAuthoritativeIds[_seenIdCursor] = new SeenIdEntry { Used = true, Id = id };
            _seenIdCursor = (_seenIdCursor + 1) % _seenAuthoritativeIds.Length;
            return true;
        }

        private void RememberAuthorityIdentity(uint id, in ProjectilePresentationIdentity identity)
        {
            _authorityIdentities[_authorityIdentityCursor] = new AuthorityIdentityEntry
            {
                Used = true, Id = id, Identity = identity
            };
            _authorityIdentityCursor = (_authorityIdentityCursor + 1) % _authorityIdentities.Length;
        }

        private bool TryFindAuthorityIdentity(uint id, out ProjectilePresentationIdentity identity)
        {
            for (int i = 0; i < _authorityIdentities.Length; i++)
            {
                if (_authorityIdentities[i].Used && _authorityIdentities[i].Id == id)
                {
                    identity = _authorityIdentities[i].Identity;
                    return true;
                }
            }
            identity = default;
            return false;
        }

        private struct PendingCandidate
        {
            public ProjectilePresentationIdentity Identity;
            public byte Weapon;
            public Vector3 Position;
            public Vector3 Direction;
            public int VisualCount;
            public long CreatedClock;
            public bool Matched;
            public bool VisualSeen;
        }

        private struct OrdinalEntry
        {
            public bool Used;
            public CombatActor Actor;
            public uint CommandSequence;
            public ushort Next;
        }

        private struct RetiredEntry
        {
            public bool Used;
            public ProjectilePresentationIdentity Identity;
            public bool Matched;
        }

        private struct SeenIdEntry
        {
            public bool Used;
            public uint Id;
        }

        private struct AuthorityIdentityEntry
        {
            public bool Used;
            public uint Id;
            public ProjectilePresentationIdentity Identity;
        }

    }
}
