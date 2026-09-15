using System;
using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using MphRead.Combat;
using MphRead.Entities;
using MphRead.Formats;
using MphRead;
using MphRead.Runtime.HistoricalCollision;
using OpenTK.Mathematics;

namespace MphRead.Mods.Network
{
    /// <summary>Single simulation owner collects resolved facts in a fixed journal. No packet callback mutates it.</summary>
    public sealed class ServerCombat : ICombatAuthority
    {
        private Scene? _scene;
        private HistoricalCollisionQueryEngine? _historicalCollision;
        private CombatShot _lastDiagnosticShot;
        private Vector3 _lastDiagnosticStart;
        private Vector3 _lastDiagnosticEnd;
        private bool _hasDiagnosticPath;
        public HistoricalCollisionRegistry HistoricalCollisionRegistry { get; }
        public DynamicCollisionHistory DynamicCollisionHistory { get; }

        public void BindScene(Scene scene)
        {
            if (_scene != null && !ReferenceEquals(_scene, scene))
                throw new InvalidOperationException("Combat authority already belongs to another scene.");
            _scene = scene;
            HistoricalCollisionRegistry.BindScene(scene);
        }

        /// <summary>
        /// Fixed registration occurs only after the room has finished loading.
        /// No registration is performed from a packet callback or a tick.
        /// </summary>
        public void InitializeHistoricalCollision(Scene scene)
        {
            BindScene(scene);
            if (_historicalCollision != null) return;
            HistoricalCollisionRegistry.RegisterScene();
            _historicalCollision = new HistoricalCollisionQueryEngine(scene,
                HistoricalCollisionRegistry, DynamicCollisionHistory, this);
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
        private readonly byte[] _rewindPresentationDelay = new byte[8];
        private int _head, _count;
        private uint _nextId;
        private readonly uint _initialSpreadSeed;
        private uint _spreadSeed;
        public ServerWorldEvents World { get; } = new();
        // Producer-local journal identity for combat/world diagnostics. This
        // must never be used as a MatchEvent semantic ID; those come from the
        // match-owned dispatcher.
        internal uint NextPresentationId() => _nextId++;
        public LagCompensationHistory History { get; } = new();
        public ProjectileCatchUp CatchUp { get; }
        public bool LagCompEnabled { get; }
        public bool ProjectileCatchUpEnabled { get; }
        /// <summary>
        /// QZ1 dynamic geometry remains opt-in until the QZ1-E WAN gate. The
        /// fixed history may still be recorded for diagnostics while disabled.
        /// </summary>
        public bool HistoricalDynamicCollisionEnabled { get; }
        public ServerCombat(bool lagCompEnabled = true, bool projectileCatchUpEnabled = true,
            uint? spreadSeed = null, bool historicalDynamicCollisionEnabled = false)
        {
            for (int i = 0; i < _damage.Length; i++) _damage[i] = new();
            _initialSpreadSeed = _spreadSeed = spreadSeed ?? Rng.Rng2StartValue;
            LagCompEnabled = lagCompEnabled;
            ProjectileCatchUpEnabled = lagCompEnabled && projectileCatchUpEnabled;
            HistoricalDynamicCollisionEnabled = lagCompEnabled && historicalDynamicCollisionEnabled;
            HistoricalCollisionRegistry = new HistoricalCollisionRegistry();
            DynamicCollisionHistory = new DynamicCollisionHistory(HistoricalCollisionRegistry);
            CatchUp = new ProjectileCatchUp(this);
            ReserveLagCompensationSamples();
        }

        private void ReserveLagCompensationSamples()
        {
            RequestedRewindTicks.Reserve();
            ValidatedRewindTicks.Reserve();
            ClampPositionError.Reserve();
            ClampVerticalError.Reserve();
            ClampHorizontalError.Reserve();
            for (int i = 0; i < ClampWeaponMetricCount; i++)
            {
                _clampPositionErrorByWeapon[i].Reserve();
                _clampVerticalErrorByWeapon[i].Reserve();
                _clampHorizontalErrorByWeapon[i].Reserve();
            }
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
        public long DynamicHistoryRecords => DynamicCollisionHistory.Records;
        public long DynamicHistoryQueries => DynamicCollisionHistory.Queries;
        public long DynamicHistoryMissing => DynamicCollisionHistory.Missing;
        public long HistoricalDoorQueries { get; private set; }
        public long HistoricalForceFieldQueries { get; private set; }
        public long HistoricalObjectQueries { get; private set; }
        public long HistoricalPlatformQueries { get; private set; }
        public long HistoricalGeometryMissing { get; private set; }
        public long HistoricalGeometryChangedOutcome { get; private set; }
        // Both samples include every eligible root beam action, including zero
        // rewind. Future/ambiguous requests contribute zero requested ticks.
        public NetSample RequestedRewindTicks;
        public NetSample ValidatedRewindTicks;
        private const int ClampWeaponMetricCount = (int)BeamType.OmegaCannon + 1;
        private const float HeadshotBandThreshold = 0.3f;
        private readonly NetSample[] _clampPositionErrorByWeapon = new NetSample[ClampWeaponMetricCount];
        private readonly NetSample[] _clampVerticalErrorByWeapon = new NetSample[ClampWeaponMetricCount];
        private readonly NetSample[] _clampHorizontalErrorByWeapon = new NetSample[ClampWeaponMetricCount];
        public NetSample ClampPositionError;
        public NetSample ClampVerticalError;
        public NetSample ClampHorizontalError;
        public long ClampHistoryUnavailable { get; private set; }
        public long ClampRequestedHistoryMissing { get; private set; }
        public long ClampServedHistoryMissing { get; private set; }
        public long ClampFutureRequests { get; private set; }
        public long ClampVerticalErrorOverHeadshotBand { get; private set; }
        public long ClampTotalErrorOverPlayerRadius { get; private set; }
        public ReadOnlySpan<NetSample> ClampPositionErrorByWeapon => _clampPositionErrorByWeapon;
        public ReadOnlySpan<NetSample> ClampVerticalErrorByWeapon => _clampVerticalErrorByWeapon;
        public ReadOnlySpan<NetSample> ClampHorizontalErrorByWeapon => _clampHorizontalErrorByWeapon;
        public void BeginTick(uint tick) { Tick = tick; }
        public void SetCommand(int slot, in InputCommand command, double rttMs = 0,
            byte rewindPresentationDelayTicks = NetworkTimingProfile.CompatibilityPresentationDelayTicks)
        {
            if ((uint)slot >= 8) throw new ArgumentOutOfRangeException(nameof(slot));
            if (rewindPresentationDelayTicks is < NetworkTimingProfile.MinimumPresentationDelayTicks
                or > NetworkTimingProfile.MaximumPresentationDelayTicks)
                throw new ArgumentOutOfRangeException(nameof(rewindPresentationDelayTicks));
            _commands[slot] = command; _rtt[slot] = rttMs;
            _rewindPresentationDelay[slot] = rewindPresentationDelayTicks;
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
            Array.Clear(_commands); Array.Clear(_rtt); Array.Clear(_rewindPresentationDelay); History.Clear(); CatchUp.Clear();
            DynamicCollisionHistory.Clear();
            _lastDiagnosticShot = default;
            _lastDiagnosticStart = _lastDiagnosticEnd = Vector3.Zero;
            _hasDiagnosticPath = false;
            ShotsConsidered = ShotsEligible = ShotsRewound = ShotsClamped = 0;
            HistoricalDoorQueries = HistoricalForceFieldQueries = HistoricalObjectQueries = HistoricalPlatformQueries = 0;
            HistoricalGeometryMissing = HistoricalGeometryChangedOutcome = 0;
            ClampHistoryUnavailable = ClampRequestedHistoryMissing = ClampServedHistoryMissing = 0;
            ClampFutureRequests = ClampVerticalErrorOverHeadshotBand = 0;
            ClampTotalErrorOverPlayerRadius = 0;
            RequestedRewindTicks.Clear();
            ValidatedRewindTicks.Clear();
            ClampPositionError.Clear();
            ClampVerticalError.Clear();
            ClampHorizontalError.Clear();
            for (int i = 0; i < ClampWeaponMetricCount; i++)
            {
                _clampPositionErrorByWeapon[i].Clear();
                _clampVerticalErrorByWeapon[i].Clear();
                _clampHorizontalErrorByWeapon[i].Clear();
            }
            ReserveLagCompensationSamples();
        }

        internal void NoteHistoricalQuery(HistoricalColliderKind kind)
        {
            switch (kind)
            {
                case HistoricalColliderKind.Door: HistoricalDoorQueries++; break;
                case HistoricalColliderKind.ForceField: HistoricalForceFieldQueries++; break;
                case HistoricalColliderKind.Object: HistoricalObjectQueries++; break;
                case HistoricalColliderKind.Platform: HistoricalPlatformQueries++; break;
            }
        }

        internal void NoteHistoricalMissing(HistoricalColliderId identity)
            => HistoricalGeometryMissing++;

        public bool ShouldUseHistoricalCollision(in CombatShot shot)
            => HistoricalDynamicCollisionEnabled && _historicalCollision != null
                && !HistoricalCollisionRegistry.RegistrationOverflowed && shot.IsValid
                && (CatchUp.CollisionTick.HasValue || shot.ActionServerTick != Tick);

        public bool TryGetHistoricalBeamCollision(Vector3 start, Vector3 end, in CombatShot shot,
            out HistoricalCollisionResult result)
        {
            result = HistoricalCollisionResult.None;
            if (shot.IsValid)
            {
                // The host diagnostic command reports the most recent
                // server-observed historical projectile. This is a bounded
                // presentation fact, never an input to collision resolution.
                _lastDiagnosticShot = shot;
                _lastDiagnosticStart = start;
                _lastDiagnosticEnd = end;
                _hasDiagnosticPath = true;
            }
            if (!ShouldUseHistoricalCollision(shot)) return false;
            uint queryTick = CatchUp.CollisionTick ?? shot.GetHistoricalTick(Tick);
            var query = new HistoricalCollisionQuery(start, end, TestFlags.Beams);
            // Catch-up includes the completed current tick before its history
            // sample is recorded. Query current registered geometry directly
            // at that boundary so a normal current-tick step is not reported
            // as missing historical data.
            bool hit = queryTick == Tick
                ? _historicalCollision!.TryQueryCurrent(query, out result)
                : _historicalCollision!.TryQuery(query, queryTick, out result);
            // The current query is a side-effect-free comparison only. Combat
            // resolution is performed once, from the historical result above.
            if (queryTick != Tick)
            {
                _historicalCollision.TryQueryCurrent(query, out HistoricalCollisionResult current);
                if (ChangedNearest(result, current)) HistoricalGeometryChangedOutcome++;
            }
            return hit;
        }

        public bool TryResolveHistoricalCollider(in HistoricalCollisionResult result,
            out EntityBase entity)
        {
            if (result.ColliderKind is HistoricalColliderKind.StaticRoom or HistoricalColliderKind.None
                || !result.ColliderId.IsValid)
            {
                entity = null!;
                return false;
            }
            return HistoricalCollisionRegistry.TryResolveCurrent(result.ColliderId, out entity);
        }

        public int CopyHistoricalDiagnosticSnapshot(uint tick,
            Span<HistoricalCollisionDiagnostic> destination, out bool truncated)
            => DynamicCollisionHistory.CopyDiagnosticSnapshot(tick, destination, out truncated);

        /// <summary>
        /// Copies a bounded, side-effect-free server snapshot for
        /// <c>netdebug lagcomp-history</c> and <c>netdebug lagcomp-dynamic</c>.
        /// The caller owns both buffers; this method never changes collision or
        /// query metrics and never creates a gameplay packet.
        /// </summary>
        public HistoricalCollisionDebugFrame CopyHistoricalDebugSnapshot(in CombatShot shot,
            Vector3 projectileStart, Vector3 projectileEnd,
            Span<HistoricalPlayerVolumeDiagnostic> players,
            Span<HistoricalCollisionDiagnostic> dynamic,
            out int playerCount, out int dynamicCount)
        {
            uint queryTick = CatchUp.CollisionTick
                ?? (shot.IsValid ? shot.GetHistoricalTick(Tick) : Tick);
            uint rewindTicks = unchecked(Tick - queryTick);
            if (rewindTicks >= 0x80000000u) rewindTicks = 0;
            Span<LagCompensationState> historicalPlayers = stackalloc LagCompensationState[LagCompensationHistory.PlayerCapacity];
            int historicalPlayerCount = History.CopyDiagnosticSnapshot(queryTick, historicalPlayers,
                out bool historyTruncated);
            playerCount = Math.Min(players.Length, historicalPlayerCount);
            for (int i = 0; i < playerCount; i++)
            {
                LagCompensationState player = historicalPlayers[i];
                players[i] = new HistoricalPlayerVolumeDiagnostic(player.Slot, player.CanBeHit,
                    player.AltForm, player.Position, player.SpherePosition, player.SphereRadius,
                    player.MinPickupHeight, player.MaxPickupHeight);
            }
            bool playersTruncated = historyTruncated || historicalPlayerCount > playerCount;
            dynamicCount = DynamicCollisionHistory.CopyDiagnosticSnapshot(queryTick, dynamic,
                out bool dynamicTruncated);
            return new HistoricalCollisionDebugFrame(Tick, queryTick, rewindTicks,
                projectileStart, projectileEnd, HistoricalDynamicCollisionEnabled,
                HistoricalCollisionRegistry.RegistrationOverflowed, playerCount,
                dynamicCount, playersTruncated || dynamicTruncated);
        }

        /// <summary>
        /// Builds the fixed-size server-to-client diagnostic payload requested
        /// by an authenticated Node admin command. The client supplies no
        /// command, tick, path, or collider selection.
        /// </summary>
        public int WriteHistoricalDebugPacket(uint matchId, string command, Span<byte> destination)
        {
            string normalized = command.Trim().ToLowerInvariant();
            HistoricalCollisionDebugMode mode = normalized switch
            {
                "lagcomp-history" or "netdebug lagcomp-history" => HistoricalCollisionDebugMode.History,
                "lagcomp-dynamic" or "netdebug lagcomp-dynamic" => HistoricalCollisionDebugMode.Dynamic,
                _ => throw new ArgumentException("Expected netdebug lagcomp-history or netdebug lagcomp-dynamic.", nameof(command))
            };
            Span<HistoricalPlayerVolumeDiagnostic> players = stackalloc HistoricalPlayerVolumeDiagnostic[HistoricalCollisionDebugPacket.MaxPlayers];
            Span<HistoricalCollisionDiagnostic> colliders = stackalloc HistoricalCollisionDiagnostic[HistoricalCollisionDebugPacket.MaxColliders];
            CombatShot shot = _hasDiagnosticPath ? _lastDiagnosticShot : default;
            Vector3 start = _hasDiagnosticPath ? _lastDiagnosticStart : Vector3.Zero;
            Vector3 end = _hasDiagnosticPath ? _lastDiagnosticEnd : Vector3.Zero;
            HistoricalCollisionDebugFrame frame = CopyHistoricalDebugSnapshot(shot, start, end,
                players, colliders, out int playerCount, out int dynamicCount);
            BoundedPercentileSnapshot clampPosition = ClampPositionError.Percentiles;
            BoundedPercentileSnapshot clampVertical = ClampVerticalError.Percentiles;
            var metrics = new HistoricalCollisionDebugMetrics(DynamicHistoryRecords,
                DynamicHistoryQueries, DynamicHistoryMissing, HistoricalDoorQueries,
                HistoricalForceFieldQueries, HistoricalPlatformQueries,
                HistoricalGeometryChangedOutcome,
                (int)Math.Min(UInt16.MaxValue, ClampPositionError.Count),
                (float)clampPosition.P95, (float)clampPosition.P99,
                (float)ClampPositionError.Max, (float)clampVertical.P95,
                (float)ClampVerticalError.Max);
            return HistoricalCollisionDebugPacket.Write(destination, matchId, mode, frame,
                mode == HistoricalCollisionDebugMode.History ? players[..playerCount] : ReadOnlySpan<HistoricalPlayerVolumeDiagnostic>.Empty,
                mode == HistoricalCollisionDebugMode.Dynamic ? colliders[..dynamicCount] : ReadOnlySpan<HistoricalCollisionDiagnostic>.Empty,
                _hasDiagnosticPath, metrics);
        }

        /// <summary>
        /// Formats the bounded server facts consumed by a future developer
        /// visualization. Accepted commands are exactly
        /// <c>netdebug lagcomp-history</c> and <c>netdebug lagcomp-dynamic</c>
        /// (the <c>netdebug</c> prefix may be omitted for host tooling).
        /// </summary>
        public string NetDebug(string command, in CombatShot shot,
            Vector3 projectileStart, Vector3 projectileEnd)
        {
            string normalized = command.Trim().ToLowerInvariant();
            bool history = normalized is "lagcomp-history" or "netdebug lagcomp-history";
            bool dynamic = normalized is "lagcomp-dynamic" or "netdebug lagcomp-dynamic";
            if (!history && !dynamic)
                throw new ArgumentException("Expected netdebug lagcomp-history or netdebug lagcomp-dynamic.", nameof(command));

            // The output is intentionally bounded independently of the
            // registry cap. Diagnostic commands are never on the simulation
            // tick and may allocate their temporary buffers.
            var players = new HistoricalPlayerVolumeDiagnostic[LagCompensationHistory.PlayerCapacity];
            var colliders = new HistoricalCollisionDiagnostic[HistoricalCollisionRegistry.DefaultHardColliderCap];
            HistoricalCollisionDebugFrame frame = CopyHistoricalDebugSnapshot(shot,
                projectileStart, projectileEnd, players, colliders, out int playerCount,
                out int dynamicCount);
            var output = new StringBuilder(4096);
            output.Append("netdebug ").Append(history ? "lagcomp-history" : "lagcomp-dynamic")
                .Append(" current_tick=").Append(frame.CurrentTick)
                .Append(" query_tick=").Append(frame.QueryTick)
                .Append(" rewind_ticks=").Append(frame.RewindTicks)
                .Append(" dynamic_enabled=").Append(frame.HistoricalDynamicEnabled)
                .Append(" registry_overflow=").Append(frame.RegistryOverflowed)
                .Append(" truncated=").Append(frame.Truncated)
                .Append(" dynamic_history_records=").Append(DynamicHistoryRecords)
                .Append(" dynamic_history_queries=").Append(DynamicHistoryQueries)
                .Append(" dynamic_history_missing=").Append(DynamicHistoryMissing)
                .Append(" historical_door_queries=").Append(HistoricalDoorQueries)
                .Append(" historical_force_field_queries=").Append(HistoricalForceFieldQueries)
                .Append(" historical_platform_queries=").Append(HistoricalPlatformQueries)
                .Append(" historical_geometry_changed_outcome=").Append(HistoricalGeometryChangedOutcome)
                .Append(" requested_rewind_p50/p95/p99=").Append(Percentiles(RequestedRewindTicks))
                .Append(" validated_rewind_p50/p95/p99=").Append(Percentiles(ValidatedRewindTicks))
                .Append(" clamped_shots=").Append(ShotsClamped)
                .Append(" clamp_error_p50/p95/p99/max=").Append(Percentiles(ClampPositionError, includeMax: true))
                .Append(" clamp_vertical_p95/max=").Append(Percentiles(ClampVerticalError, p95Only: true, includeMax: true))
                .Append(" clamp_history_unavailable=").Append(ClampHistoryUnavailable)
                .Append(" path_start=").Append(Vector3Text(projectileStart))
                .Append(" path_end=").Append(Vector3Text(projectileEnd))
                .AppendLine();
            HitRegistrationAuthorityDiagnostic authority
                = ServerHitRegistrationDiagnostics.Capture(this);
            var hitRegistration = new HitRegistrationDiagnosticSnapshot(
                HitRegistrationDiagnosticScope.Window,
                HitRegistrationClientDiagnostic.Unavailable,
                authority,
                HitRegistrationFeedbackDiagnostic.Unavailable,
                RttMs: null,
                JitterMs: null);
            output.AppendLine(HitRegistrationDiagnosticFormatter.Format(in hitRegistration));
            if (history)
            {
                output.Append("players=").Append(playerCount).AppendLine();
                for (int i = 0; i < playerCount; i++)
                {
                    HistoricalPlayerVolumeDiagnostic player = players[i];
                    output.Append("player slot=").Append(player.Slot)
                        .Append(" can_be_hit=").Append(player.CanBeHit)
                        .Append(" alt_form=").Append(player.AltForm)
                        .Append(" position=").Append(Vector3Text(player.Position))
                        .Append(" sphere=").Append(Vector3Text(player.SpherePosition))
                        .Append(" radius=").Append(NumberText(player.SphereRadius))
                        .Append(" pickup_y=").Append(NumberText(player.MinPickupHeight))
                        .Append("..").Append(NumberText(player.MaxPickupHeight)).AppendLine();
                }
            }
            else
            {
                output.Append(" dynamic=").Append(dynamicCount).AppendLine();
                for (int i = 0; i < dynamicCount; i++) AppendDynamic(output, colliders[i]);
            }
            if (output.Length > 16_384)
            {
                output.Length = 16_300;
                output.AppendLine("...");
                output.Append("output_truncated=true");
            }
            return output.ToString();

            static string NumberText(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);
            static string Vector3Text(Vector3 value)
                => $"({NumberText(value.X)},{NumberText(value.Y)},{NumberText(value.Z)})";
            static string Vector4Text(Vector4 value)
                => $"({NumberText(value.X)},{NumberText(value.Y)},{NumberText(value.Z)},{NumberText(value.W)})";
            static string Percentiles(NetSample sample, bool p95Only = false, bool includeMax = false)
            {
                if (sample.Count == 0) return "n/a";
                BoundedPercentileSnapshot values = sample.Percentiles;
                if (p95Only) return includeMax
                    ? $"{values.P95:0.###}/{values.Max:0.###}"
                    : $"{values.P95:0.###}";
                return includeMax
                    ? $"{values.P50:0.###}/{values.P95:0.###}/{values.P99:0.###}/{values.Max:0.###}"
                    : $"{values.P50:0.###}/{values.P95:0.###}/{values.P99:0.###}";
            }
            static void AppendDynamic(StringBuilder output, HistoricalCollisionDiagnostic diagnostic)
            {
                HistoricalCollisionState state = diagnostic.State;
                output.Append("dynamic kind=").Append(diagnostic.ColliderKind)
                    .Append(" id=").Append(diagnostic.ColliderId.EntityId)
                    .Append(" generation=").Append(diagnostic.ColliderId.Generation)
                    .Append(" active=").Append(state.Active)
                    .Append(" blocking=").Append(state.Blocking);
                switch (diagnostic.ColliderKind)
                {
                    case HistoricalColliderKind.Door:
                        output.Append(" facing=").Append(Vector3Text(state.Facing))
                            .Append(" plane_position=").Append(Vector3Text(state.Position))
                            .Append(" radius2=").Append(NumberText(state.RadiusSquared));
                        break;
                    case HistoricalColliderKind.ForceField:
                        output.Append(" plane=").Append(Vector4Text(state.Plane))
                            .Append(" position=").Append(Vector3Text(state.Position))
                            .Append(" up=").Append(Vector3Text(state.Up))
                            .Append(" right=").Append(Vector3Text(state.Right))
                            .Append(" width=").Append(NumberText(state.Width))
                            .Append(" height=").Append(NumberText(state.Height));
                        break;
                    case HistoricalColliderKind.Object:
                    case HistoricalColliderKind.Platform:
                        output.Append(" bounds_min=").Append(Vector3Text(state.BoundsMin))
                            .Append(" bounds_max=").Append(Vector3Text(state.BoundsMax));
                        break;
                }
                output.AppendLine();
            }
        }

        private static bool ChangedNearest(in HistoricalCollisionResult historical,
            in HistoricalCollisionResult current)
            => historical.Hit != current.Hit
                || historical.Hit && (historical.ColliderKind != current.ColliderKind
                    || historical.ColliderId != current.ColliderId);
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
            byte presentationDelay = _rewindPresentationDelay[actor.Slot] == 0
                ? NetworkTimingProfile.Compatibility.PresentationDelayTicks
                : _rewindPresentationDelay[actor.Slot];
            LagCompensationTime time = LagCompensationPolicy.ResolveTick(Tick,
                shot.ViewServerTick, _rtt[actor.Slot], presentationDelay);
            uint requested = unchecked(Tick - shot.ViewServerTick);
            RequestedRewindTicks.Record(requested < 0x80000000u ? requested : 0);
            ValidatedRewindTicks.Record(time.RewindTicks);
            if (time.RewindTicks > 0) ShotsRewound++;
            if (time.Clamped) ShotsClamped++;
            if (time.Clamped && (_scene != null || History.HasRecords))
                RecordClampedSpatialError(actor, shot.ViewServerTick, time.Tick, shot.SourceWeapon);
            return shot with { ActionServerTick = time.Tick, RewindTicks = time.RewindTicks, Mode = mode };
        }

        /// <summary>
        /// Measures the geometry difference introduced by a rewind clamp. This
        /// path reads history only; it never moves an actor or participates in
        /// the authoritative collision decision.
        /// </summary>
        private void RecordClampedSpatialError(in CombatActor shooter, uint requestedTick,
            uint servedTick, byte weapon)
        {
            bool requestedFuture = IsFutureTick(requestedTick);
            bool servedFuture = IsFutureTick(servedTick);
            if (requestedFuture) ClampFutureRequests++;
            if (servedFuture && !requestedFuture) ClampFutureRequests++;
            bool hasSceneCandidates = _scene != null;
            for (int slot = 0; slot < LagCompensationHistory.PlayerCapacity; slot++)
            {
                if (slot == shooter.Slot) continue;

                CombatActor candidate;
                if (hasSceneCandidates)
                {
                    PlayerEntity? player = _scene!.Players[slot];
                    if (player == null) continue;
                    candidate = player.ServerCombatIdentity;
                    if (!candidate.IsValid || candidate == shooter || player.Health <= 0
                        || player.Flags2.TestFlag(PlayerFlags2.Spectating)) continue;
                }
                else
                {
                    // Test/diagnostic callers without a bound scene can still
                    // compare two identity-fenced history cells.
                    LagCompensationState requestedRaw = default;
                    LagCompensationState servedRaw = default;
                    bool requestedRawFound = !requestedFuture
                        && History.TryGetDiagnostic(slot, requestedTick,
                            out requestedRaw);
                    bool servedRawFound = History.TryGetDiagnostic(slot, servedTick,
                        out servedRaw);
                    LagCompensationState identityRaw = requestedRawFound ? requestedRaw : servedRaw;
                    if ((!requestedRawFound && !servedRawFound)
                        || requestedRawFound && servedRawFound
                        && (requestedRaw.ConnectionId != servedRaw.ConnectionId
                            || requestedRaw.LifeId != servedRaw.LifeId)
                        || !identityRaw.CanBeHit)
                        continue;
                    candidate = new CombatActor((byte)slot, identityRaw.ConnectionId, identityRaw.LifeId);
                }

                LagCompensationState requested = default;
                bool requestedFound = !requestedFuture
                    && TryGetDiagnosticCandidate(candidate, requestedTick, out requested);
                if (!requestedFound)
                {
                    // A future/ambiguous request is a timing classification,
                    // not a missing history cell. Keep it out of the
                    // unavailable denominator and report it once per shot.
                    if (requestedFuture) continue;
                    ClampHistoryUnavailable++;
                    ClampRequestedHistoryMissing++;
                    continue;
                }
                bool servedFound = TryGetDiagnosticCandidate(candidate, servedTick,
                    out LagCompensationState served);
                if (!servedFound)
                {
                    if (servedFuture) continue;
                    ClampHistoryUnavailable++;
                    ClampServedHistoryMissing++;
                    continue;
                }
                if (!requested.CanBeHit || !served.CanBeHit) continue;

                Vector3 delta = requested.Position - served.Position;
                float horizontal = MathF.Sqrt(delta.X * delta.X + delta.Z * delta.Z);
                float vertical = MathF.Abs(delta.Y);
                float total = delta.Length;
                ClampPositionError.Record(total);
                ClampVerticalError.Record(vertical);
                ClampHorizontalError.Record(horizontal);
                if (vertical > HeadshotBandThreshold) ClampVerticalErrorOverHeadshotBand++;
                if (served.SphereRadius > 0 && total > served.SphereRadius)
                    ClampTotalErrorOverPlayerRadius++;
                if (weapon < ClampWeaponMetricCount)
                {
                    _clampPositionErrorByWeapon[weapon].Record(total);
                    _clampVerticalErrorByWeapon[weapon].Record(vertical);
                    _clampHorizontalErrorByWeapon[weapon].Record(horizontal);
                }
            }
        }

        private bool TryGetDiagnosticCandidate(in CombatActor candidate, uint tick,
            out LagCompensationState state)
        {
            if (_scene != null && tick == Tick)
            {
                PlayerEntity? player = _scene.Players[candidate.Slot];
                if (player != null && player.ServerCombatIdentity == candidate)
                {
                    state = LagCompensationState.Capture(player, candidate.ConnectionId, candidate.Life);
                    return true;
                }
                state = default;
                return false;
            }
            return History.TryGetDiagnostic(candidate.Slot, tick, candidate.ConnectionId,
                candidate.Life, out state);
        }

        private bool IsFutureTick(uint tick)
            => tick != Tick && unchecked(tick - Tick) <= 0x80000000u;
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
        public void NoteEnhancedEffect(in CombatShot shot, BeamType weapon,
            CombatEventFlags flags, Vector3 position)
        {
            if (!shot.IsValid || flags != CombatEventFlags.LingeringHeat) return;
            TryRecord(new(0, Tick, shot.CommandSequence, CombatEventKind.Effect,
                (byte)weapon, flags, shot.Actor, CombatActor.None, 0, 0,
                position, Vector3.Zero, 0, 0, 0));
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
            if (_scene != null && _scene.Match.MatchId != 0)
            {
                _scene.Match.SemanticEvents.Dispatch(new(0, Tick, _scene.Match.MatchId,
                    _scene.Match.PhaseRevision, MatchEventKind.PlayerSpawned, actor, CombatActor.None,
                    Flags: player.IsBot ? MatchEventFlags.Bot : MatchEventFlags.None));
            }
        }
        public void NoteDamage(PlayerEntity victim, EntityBase? source, PlayerEntity? attacker, BeamType weapon,
            DamageFlags flags, Vector3? direction, int previousHealth, ushort frozen, ushort burn, ushort disrupt,
            bool afflictionChanged, int absorbedOvercharge = 0)
        {
            CombatActor target = victim.ServerCombatIdentity;
            if (!target.IsValid) return;
            CombatShot shot = source switch
            {
                BeamProjectileEntity beam => beam.CombatShot,
                BombEntity bomb => bomb.CombatShot,
                SpireScorchPatchEntity scorch => scorch.CombatShot,
                _ => attacker == null ? default : CaptureAttribution(attacker)
            };
            if (flags.TestFlag(DamageFlags.Burn) && victim.CombatBurnSource.IsValid)
            {
                shot = victim.CombatBurnSource;
                if (weapon == BeamType.None && shot.SourceWeapon <= 10) weapon = (BeamType)shot.SourceWeapon;
            }
            CombatActor actor = shot.IsValid ? shot.Actor : CombatActor.None;
            // Capture objective/defense facts before the kill path clears or
            // mutates combat state. These are authoritative source facts, not
            // client snapshot inference.
            bool wasObjectiveCarrier = victim.OctolithFlag != null;
            bool wasDefendingObjective = TryGetDefendedObjective(victim, actor, out uint defendedObjectiveId);
            CombatEventFlags eventFlags = 0;
            if (flags.TestFlag(DamageFlags.Headshot)) eventFlags |= CombatEventFlags.Headshot;
            if (flags.TestFlag(DamageFlags.Burn)) eventFlags |= CombatEventFlags.Burn;
            if (flags.TestFlag(DamageFlags.Deathalt)) eventFlags |= CombatEventFlags.Deathalt;
            if (flags.TestFlag(DamageFlags.NoSfx)) eventFlags |= CombatEventFlags.Silent;
            if (shot.Affinity) eventFlags |= CombatEventFlags.Affinity;
            if (flags.TestFlag(DamageFlags.Concussive)) eventFlags |= CombatEventFlags.Concussive;
            if (absorbedOvercharge > 0) eventFlags |= CombatEventFlags.OverchargeAbsorb;
            ushort amount = (ushort)Math.Clamp(previousHealth - victim.Health + absorbedOvercharge, 0, UInt16.MaxValue);
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
                    wasObjectiveCarrier, wasDefendingObjective, defendedObjectiveId,
                    weapon != BeamType.None ? KillSourceKind.Beam : source is BombEntity ? KillSourceKind.Bomb
                    : source is PlayerEntity or HalfturretEntity && attacker != null && !flags.TestFlag(DamageFlags.Death)
                        ? KillSourceKind.Alt : KillSourceKind.Environment);
            }
            if (afflictionChanged) TryRecord(value with { Kind = CombatEventKind.Affliction, Amount = 0 });
        }
        private void RecordKill(PlayerEntity victim, CombatActor killer, in CombatEvent damage, bool teamDamage,
            bool wasObjectiveCarrier, bool wasDefendingObjective, uint defendedObjectiveId,
            KillSourceKind sourceKind)
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
            MatchEventFlags semanticFlags = MatchEventFlags.None;
            if (suicide) semanticFlags |= MatchEventFlags.Suicide;
            if (teamDamage && !suicide) semanticFlags |= MatchEventFlags.TeamKill;
            if (!killer.IsValid) semanticFlags |= MatchEventFlags.EnvironmentKill;
            if (victim.IsPrimeHunter) semanticFlags |= MatchEventFlags.PrimeTarget;
            if (wasObjectiveCarrier) semanticFlags |= MatchEventFlags.ObjectiveCarrier;
            if (wasDefendingObjective) semanticFlags |= MatchEventFlags.DefendingObjective;
            if (killer.IsValid && killer.Slot < victim._scene.Players.Count && victim._scene.Players[killer.Slot].IsBot)
                semanticFlags |= MatchEventFlags.Bot;
            // KillEvent.Id remains local to the combat journal. The semantic
            // dispatcher assigns the global match event ID.
            match.SemanticEvents.Dispatch(new(0, value.Tick, value.MatchId, value.PhaseRevision,
                MatchEventKind.PlayerKilled, killer, value.Victim,
                Team: victim.TeamIndex < 8 ? (byte)victim.TeamIndex : (byte)255, Flags: semanticFlags));
            if (wasDefendingObjective && killer.IsValid)
            {
                match.SemanticEvents.Dispatch(new(0, value.Tick, value.MatchId, value.PhaseRevision,
                    MatchEventKind.ObjectiveDefended, killer, value.Victim, defendedObjectiveId,
                    Team: victim.TeamIndex < 8 ? (byte)victim.TeamIndex : (byte)255,
                    Flags: semanticFlags & MatchEventFlags.Bot));
            }
            foreach (CombatActor assist in assists)
            {
                MatchEventFlags assistFlags = victim._scene.Players[assist.Slot].IsBot
                    ? MatchEventFlags.Bot : MatchEventFlags.None;
                match.SemanticEvents.Dispatch(new(0, value.Tick, value.MatchId, value.PhaseRevision,
                    MatchEventKind.PlayerAssisted, assist, value.Victim,
                    Team: victim.TeamIndex < 8 ? (byte)victim.TeamIndex : (byte)255,
                    Flags: assistFlags));
            }
            if (_killCount == Capacity) throw new InvalidOperationException("Authoritative kill journal exhausted.");
            _kills[(_killHead + _killCount++) % Capacity] = value;
        }

        private static bool TryGetDefendedObjective(PlayerEntity victim, CombatActor killer, out uint entityId)
        {
            entityId = 0;
            if (!killer.IsValid || victim._scene.Match.Rules.Mode is not (MatchMode.Defender or MatchMode.TeamDefender)
                || killer.Slot >= victim._scene.Players.Count)
                return false;
            PlayerEntity attacker = victim._scene.Players[killer.Slot];
            if (attacker.ServerCombatIdentity != killer || attacker.TeamIndex == victim.TeamIndex)
                return false;
            foreach (NodeDefenseEntity node in victim._scene.GetNodeDefenseEntities())
            {
                if (node.Volume.TestPoint(victim.Volume.SpherePosition)
                    && node.Volume.TestPoint(attacker.Volume.SpherePosition))
                {
                    entityId = unchecked((uint)node.Id);
                    return entityId != 0;
                }
            }
            return false;
        }

    }
}
