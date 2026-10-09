using MphRead.Mods.Multiplayer;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using MphRead.Effects;
using MphRead.Formats;
using MphRead.Formats.Collision;
using MphRead.Formats.Culling;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.Entities
{
    public class BeamProjectileEntity : EntityBase
    {
        internal long TrainingShotId { get; set; }
        public BeamFlags Flags { get; set; }
        internal bool EnhancedDirectHit;
        public byte EnhancedBounceCount;
        public bool EnhancedMicroSeeker;
        public bool EnhancedFullCharge, EnhancedSiegeRound;
        // True only for the three bomblets created by a Battlehammer airburst.
        // They are real projectiles, but may not themselves be manually burst.
        public bool BattlehammerClusterChild;
        /// <summary>
        /// The original fire event's world ACK, used only for historical timing.
        /// ModLaunchKey fences ModShotId by match, epoch and shooter lifecycle.
        /// Derived projectiles preserve both identity and timing.
        /// </summary>
        public uint ModLaunchFrame { get; set; }
        public uint ModShotId { get; set; }
        public ShotKey ModLaunchKey { get; internal set; }
        // Detached claim witnesses distinguish actual native pellets/children
        // and accumulate real simulation/catch-up travel rather than ACK age.
        internal uint ModWitnessComponent { get; private set; }
        internal uint ModPresentationComponent { get; private set; }
        internal uint ModClaimTravelFrames { get; private set; }
        private static int _nextWitnessComponent;

        // Replay impact reconciliation is presentation-only. It never changes
        // projectile collision/lifespan, so checkpoint/RNG/gameplay state stays
        // exactly where the replica simulation put it.
        private bool _replayImpactPending;
        private bool _replayImpactHidden;
        private ulong _replayImpactDrawFrame;
        private Vector3 _replayImpactPosition;

        // Spawn's firing phase must survive until a Shock Coil beam tests an enemy.
        public ulong ModContinuousPhase { get; set; }
        public bool ModHasSharedContinuousPhase { get; set; }
        public ushort ModLaunchMatch { get; set; }
        public ulong ModLaunchAuthority { get; set; }
        public ushort ModLaunchGeneration { get; set; }
        public ushort ModLaunchLife { get; set; }
        public BeamType Beam { get; set; }
        public BeamType BeamKind { get; set; }

        public Vector3 Velocity { get; set; }
        public Vector3 Acceleration { get; set; } // only used for gravity
        public Vector3 BackPosition { get; set; }
        public Vector3 SpawnPosition { get; set; }
        public Vector3[] PastPositions { get; } = new Vector3[10];

        public int DrawFuncId { get; set; }
        public float Age { get; set; }
        public float Lifespan { get; set; }

        public Vector3 Color { get; set; }
        public byte CollisionEffect { get; set; }
        public byte DamageDirType { get; set; }
        public byte SplashDamageType { get; set; }
        public float Homing { get; set; }

        public Vector3 Direction { get; set; }
        public Vector3 Right { get; set; }
        public Vector3 Up { get; set; }

        public float Damage { get; set; }
        public float HeadshotDamage { get; set; }
        public float SplashDamage { get; set; }
        public float SplashRadius { get; set; }
        public float MaxDistance { get; set; }
        public Affliction Afflictions { get; set; }

        public EntityBase? Owner { get; set; }
        public WeaponInfo? RicochetWeapon { get; set; }
        public EffectEntry? Effect { get; set; }
        public EffectEntry? MuzzleEffect { get; set; }
        private ushort _targetGeneration, _targetLife;
        private bool _targetLifeBound;
        public EntityBase? Target
        {
            get => field; // retain the checkpoint schema backing field
            set
            {
                _targetLifeBound = false;
                if (value is PlayerEntity player && !_scene.Services.IsReplica && NetSession.Active)
                {
                    if (!NetUnlagged.HistoricalTargetAvailable(player)) { field = null; return; }
                    _targetGeneration = NetPlayerLifecycle.Generation(player.SlotIndex);
                    _targetLife = NetPlayerLifecycle.Get(player.SlotIndex);
                    _targetLifeBound = true;
                }
                field = value;
            }
        }
        internal void ValidateHomingTarget()
        {
            if (_targetLifeBound && Target is PlayerEntity player
                && (!NetPlayerLifecycle.Matches(player.SlotIndex, _targetGeneration, _targetLife)
                    || Beam == BeamType.ShockCoil && (!player.ModIsInPlay || player.Flags2.TestFlag(PlayerFlags2.Spectating)))) Target = null;
        }

        internal Vector3 ModResolvedHitPoint { get; private set; }
        internal bool ModResolvedHitPointValid { get; private set; }

        private void TakePlayerDamageAt(PlayerEntity player, uint damage, DamageFlags flags,
            Vector3? direction, Vector3 impactPoint)
        {
            Vector3 priorPoint = ModResolvedHitPoint;
            bool priorValid = ModResolvedHitPointValid;
            ModResolvedHitPoint = impactPoint;
            ModResolvedHitPointValid = Single.IsFinite(impactPoint.X)
                && Single.IsFinite(impactPoint.Y) && Single.IsFinite(impactPoint.Z);
            try { player.TakeDamage(damage, flags, direction, this); }
            finally { ModResolvedHitPoint = priorPoint; ModResolvedHitPointValid = priorValid; }
        }

        private void TakePlayerDamageAt(PlayerEntity player, int damage, DamageFlags flags,
            Vector3? direction, Vector3 impactPoint)
            => TakePlayerDamageAt(player, (uint)Math.Max(0, damage), flags, direction, impactPoint);

        public EquipInfo? Equip { get; set; }

        internal float ModBalancedRangeDamage(float damage, Vector3 impactPosition)
        {
            if (!_scene.GameState.Multiplayer || !_scene.GameState.BalancedMode
                || Owner is not PlayerEntity || !BalancedModeRules.HasRangeDamageCurve(Beam))
            {
                return damage;
            }
            float distance = Vector3.Distance(impactPosition, SpawnPosition);
            return BalancedModeRules.ScaleRangeDamage(Beam, damage, distance);
        }

        internal static float ModBalancedProjectileSpeed(Scene scene, EntityBase owner,
            BeamType beam, bool charged, float speed)
        {
            if (!scene.GameState.Multiplayer || !scene.GameState.BalancedMode
                || owner is not PlayerEntity || !BalancedModeRules.HasProjectileSpeedTuning(beam))
            {
                return speed;
            }
            return BalancedModeRules.ScaleProjectileSpeed(beam, charged, speed);
        }

        internal static Vector3 ModSplashOrigin(Scene scene, BeamType beam,
            Vector3 projectilePosition, Vector3 impactPosition)
        {
            // Stock projectile splash historically samples the projectile's current
            // position. Balanced Battlehammer is different: terrain-impact clusters
            // are defined by the authoritative collision point. Sampling from the
            // pre-collision projectile position can put the explosion behind the
            // surface that was just hit, causing the LOS test to reject every nearby
            // target. Parent shells and cluster children therefore use the actual
            // impact point, while every stock/non-Battlehammer path is unchanged.
            if (scene.GameState.Multiplayer && scene.GameState.BalancedMode
                && beam == BeamType.Battlehammer
                && Single.IsFinite(impactPosition.X) && Single.IsFinite(impactPosition.Y)
                && Single.IsFinite(impactPosition.Z))
            {
                return impactPosition;
            }
            return projectilePosition;
        }

        internal static void ModBalancedHitTuning(Scene scene, EntityBase owner, BeamType beam,
            bool battlehammerCluster, ref int damage, ref int headshotDamage,
            ref int splashDamage, ref byte splashDamageType)
        {
            if (!scene.GameState.Multiplayer || !scene.GameState.BalancedMode
                || owner is not PlayerEntity)
            {
                return;
            }

            damage = (int)MathF.Round(
                BalancedModeRules.ScaleDirectHitDamage(beam, damage, battlehammerCluster),
                MidpointRounding.AwayFromZero);
            headshotDamage = (int)MathF.Round(
                BalancedModeRules.ScaleDirectHitDamage(beam, headshotDamage, battlehammerCluster),
                MidpointRounding.AwayFromZero);
            splashDamage = (int)MathF.Round(
                BalancedModeRules.ScaleSplashDamage(beam, splashDamage),
                MidpointRounding.AwayFromZero);
            if (BalancedModeRules.ForcesLinearSplashFalloff(beam))
            {
                splashDamageType = 0;
            }
        }

        public int DamageInterpolation { get; set; }
        public int SpeedInterpolation { get; set; }
        public float SpeedDecayTime { get; set; }
        public float Speed { get; set; }
        public float InitialSpeed { get; set; }
        public float FinalSpeed { get; set; }
        public float DamageDirMag { get; set; }
        public float RicochetLossH { get; set; }
        public float RicochetLossV { get; set; }
        public float CylinderRadius { get; set; }

        private readonly EquipInfo _ricochetEquip = new EquipInfo();

        private ModelInstance? _trailModel;
        private int _bindingId = 0;
        internal void ReplayBindResources()
        {
            if (_trailModel == null) { _bindingId = 0; return; }
            _scene.LoadModel(_trailModel.Model);
            Material material = _trailModel.Model.Materials[0];
            _bindingId = _scene.BindGetTexture(_trailModel.Model, material.TextureId, material.PaletteId, 0);
        }

        public BeamProjectileEntity(Scene scene) : base(EntityType.BeamProjectile, scene)
        {
        }

        public override void Initialize()
        {
            base.Initialize();
            // model will be loaded and bound by scene setup
            if (DrawFuncId == 0 || DrawFuncId == 3 || DrawFuncId == 6 || DrawFuncId == 7 || DrawFuncId == 10 || DrawFuncId == 12)
            {
                _trailModel = _scene.GetModelInstance("trail");
            }
            else if (DrawFuncId == 1 || DrawFuncId == 2)
            {
                _trailModel = _scene.GetModelInstance("electroTrail");
            }
            else if (DrawFuncId == 9)
            {
                _trailModel = _scene.GetModelInstance("arcWelder");
            }
            if (_trailModel != null)
            {
                Material material = _trailModel.Model.Materials[0];
                _bindingId = _scene.BindGetTexture(_trailModel.Model, material.TextureId, material.PaletteId, 0);
            }
        }

        public void Reposition(Vector3 offset)
        {
            SpawnPosition += offset;
            Position += offset;
            BackPosition += offset;
            for (int i = 0; i < PastPositions.Length; i++)
            {
                PastPositions[i] += offset;
            }
        }

        public override bool Process()
        {
            if (_scene.Services.IsReplica && _replayImpactPending
                && _scene.FrameCount >= _replayImpactDrawFrame)
            {
                _replayImpactPending = false;
                _replayImpactHidden = true;
            }
            ValidateHomingTarget();
            if (Lifespan <= 0)
            {
                return false;
            }
            Lifespan -= _scene.FrameTime;
            if (Flags.TestFlag(BeamFlags.Collided))
            {
                return true;
            }
            bool firstFrame = Age == 0;
            if (Flags.TestFlag(BeamFlags.Continuous) && Age > 0)
            {
                // avoid any frame time issues by just getting rid of continuous beams as soon as they're no longer being replaced
                // --> in game they stick around for a frame or two, but their lifespan will have already made it so they can't interact
                if (Flags.TestFlag(BeamFlags.Continuous) && Owner?.Type == EntityType.Player)
                {
                    // the game does this instead of PlayBeamHitSfx in the Lifespan <= 0 condition below
                    StopHomingSfx();
                    var playerOwner = (PlayerEntity)Owner;
                    playerOwner.StopContinuousBeamSfx(Beam);
                }
                return false;
            }
            if (ModClaimTravelFrames < uint.MaxValue) ModClaimTravelFrames++;
            Age += _scene.FrameTime;
            BackPosition = Position;
            // the game does this every other frame at 30 fps and keeps 5 past positions; we do it every other frame at 60 fps and keep 10,
            // and use only every other position to draw each trail segment, which results in the beam trail updating at the same frequency
            // (relative to the projectile) and having the same amount of smear as in the game
            // todo?: might need to revisit
            // --> observed homing missile trail flickering(?) when curved, and judicator trail getting more opaque on final collision
            if (_scene.FrameCount % 2 == 0)
            {
                for (int i = 9; i > 0; i--)
                {
                    PastPositions[i] = PastPositions[i - 1];
                }
                PastPositions[0] = Position;
            }
            if (Flags.TestFlag(BeamFlags.Homing) && Flags.TestFlag(BeamFlags.Continuous))
            {
                if (Target != null)
                {
                    Target.GetPosition(out Vector3 targetPos);
                    Position = targetPos;
                }
                else
                {
                    Velocity /= 4f;
                }
            }
            else
            {
                Position += Velocity;
                Velocity += Acceleration / 2; // todo: FPS stuff
                Debug.Assert(SpeedDecayTime >= 0);
                if (SpeedDecayTime > 0 && Age <= SpeedDecayTime)
                {
                    float magnitude = Velocity.Length;
                    if (magnitude > 0)
                    {
                        Speed = GetInterpolatedValue(SpeedInterpolation, InitialSpeed, FinalSpeed, Age / SpeedDecayTime);
                        Velocity *= Speed / magnitude;
                    }
                }
            }
            _soundSource.Update(Position, rangeIndex: Beam == BeamType.Missile ? 3 : 2);
            UpdateNodeRefVolume();
            if (Target != null)
            {
                for (int i = 0; i < _scene.MessageQueue.Count; i++)
                {
                    MessageInfo info = _scene.MessageQueue[i];
                    if (info.Message == Message.Destroyed && info.ExecuteFrame == _scene.FrameCount && info.Sender == Target)
                    {
                        Target = null;
                        break;
                    }
                }
            }
            if (!Flags.TestFlag(BeamFlags.Continuous) || firstFrame)
            {
                CheckCollision();
            }
            if (Flags.TestFlag(BeamFlags.Homing) && !Flags.TestFlag(BeamFlags.Continuous) && Target != null)
            {
                Target.GetPosition(out Vector3 targetPos);
                Vector3 acceleration = targetPos - Position;
                if (acceleration != Vector3.Zero)
                {
                    acceleration = acceleration.Normalized();
                }
                else
                {
                    acceleration = Vector3.UnitX;
                }
                acceleration *= Speed;
                if (Vector3.Dot(acceleration, Velocity) >= 0)
                {
                    acceleration -= Velocity;
                    float accelMag = acceleration.Length;
                    if (accelMag > Homing)
                    {
                        acceleration *= Homing / accelMag;
                    }
                    Velocity += acceleration;
                }
                else
                {
                    Target = null;
                }
            }
            if (Flags.TestFlag(BeamFlags.Charged)
                && (Beam != BeamType.Missile || Flags.TestFlag(BeamFlags.Homing)))
            {
                // only relevant for the affinity missile beeping sound in practice
                int sfx = Metadata.BeamSfx[(int)Beam, (int)BeamSfx.Homing];
                if (sfx != -1)
                {
                    _soundSource.PlaySfx(sfx, loop: true);
                }
            }
            if (Flags.TestFlag(BeamFlags.HasModel))
            {
                UpdateAnimFrames(_models[0]);
            }
            else if (Effect != null)
            {
                Effect.Transform(Position, Transform.ClearScale());
            }
            if (Lifespan <= 0)
            {
                CollisionResult colRes = default;
                colRes.Plane = new Vector4(-Direction);
                colRes.Position = Position;
                SpawnCollisionEffect(colRes, noSplat: true);
                OnCollision(colRes, colWith: null, enhancedImpact: false);
                if (!Flags.TestFlag(BeamFlags.Continuous) || (Owner?.Type) != EntityType.Player)
                {
                    // the alternative condition is handled above when the last continuous beam is removed
                    PlayBeamHitSfx();
                }
            }
            return true;
        }

        private void CheckCollision()
        {
            CollisionResult anyRes = default;
            EntityBase? colWith = null;
            bool noColEff = false;
            float minDist = 2f;
            if (MaxDistance > 0)
            {
                Vector3 frontTravel = Position - SpawnPosition;
                float dist = frontTravel.Length;
                if (dist >= MaxDistance)
                {
                    Vector3 backTravel = BackPosition - SpawnPosition;
                    float dot = Vector3.Dot(frontTravel.Normalized(), backTravel);
                    float pct = 1;
                    if (Fixed.ToFloat(Fixed.ToInt(dist)) != Fixed.ToFloat(Fixed.ToInt(dot)))
                    {
                        pct = (MaxDistance - dot) / (dist - dot);
                    }
                    if (pct < 2)
                    {
                        minDist = pct;
                        anyRes.Position = new Vector3(
                            BackPosition.X + (Position.X - BackPosition.X) * pct,
                            BackPosition.Y + (Position.Y - BackPosition.Y) * pct,
                            BackPosition.Z + (Position.Z - BackPosition.Z) * pct
                        );
                        if (DrawFuncId == 4)
                        {
                            // uncharged Magmaul
                            anyRes.Plane = Vector4.UnitY;
                        }
                        else
                        {
                            anyRes.Plane = new Vector4(-Direction);
                        }
                        noColEff = true;
                    }
                }
            }
            if (Flags.TestFlag(BeamFlags.SurfaceCollision))
            {
                CollisionResult colRes = default;
                if (CollisionDetection.CheckBetweenPoints(BackPosition, Position, TestFlags.Beams, _scene, ref colRes)
                    && colRes.Distance < minDist)
                {
                    float dot = Vector3.Dot(BackPosition, colRes.Plane.Xyz) - colRes.Plane.W;
                    if (dot >= 0)
                    {
                        minDist = colRes.Distance;
                        anyRes = colRes;
                    }
                }
                foreach (DoorEntity door in _scene.GetDoorEntities())
                {
                    if (door.Flags.TestFlag(DoorFlags.Open) || door.ConnectorInactive)
                    {
                        continue;
                    }
                    Vector3 doorFacing = door.FacingVector;
                    Vector3 lockPos = door.LockPosition;
                    var plane = new Vector4(doorFacing, 0);
                    if (Vector3.Dot(BackPosition - lockPos, doorFacing) < 0)
                    {
                        plane *= -1;
                    }
                    Vector3 wvec = plane.Xyz * (lockPos + 0.4f * plane.Xyz);
                    plane.W = wvec.X + wvec.Y + wvec.Z;
                    if (CollisionDetection.CheckCylinderIntersectPlane(BackPosition, Position, plane, ref colRes)
                        && colRes.Distance < minDist)
                    {
                        Vector3 between = colRes.Position - lockPos;
                        if (between.LengthSquared < door.RadiusSquared)
                        {
                            minDist = colRes.Distance;
                            anyRes = colRes;
                            colWith = door;
                            noColEff = false;
                            anyRes.Field0 = 0;
                            anyRes.Plane = plane;
                            anyRes.Flags = CollisionFlags.None;
                        }
                    }
                }
                foreach (ForceFieldEntity forceField in _scene.GetForceFieldEntities())
                {
                    // todo: some of these properties are compatible with the entity moving, some aren't
                    if (forceField.Active
                        && CollisionDetection.CheckCylinderIntersectPlane(BackPosition, Position, forceField.Plane, ref colRes)
                        && colRes.Distance < minDist)
                    {
                        Vector3 between = colRes.Position - forceField.Position;
                        float dot = Vector3.Dot(between, forceField.FieldUpVector);
                        if (dot <= forceField.Height && dot >= -forceField.Height)
                        {
                            dot = Vector3.Dot(between, forceField.FieldRightVector);
                            if (dot <= forceField.Width && dot >= -forceField.Width)
                            {
                                minDist = colRes.Distance;
                                anyRes = colRes;
                                colWith = forceField;
                                noColEff = false;
                                anyRes.Field0 = 0;
                                anyRes.Plane = forceField.Plane;
                            }
                        }
                    }
                }
            }
            Debug.Assert(Owner != null);
            if (Owner.Type != EntityType.EnemyInstance)
            {
                foreach (EnemyInstanceEntity enemy in _scene.GetEnemyInstanceEntities())
                {
                    CollisionResult res = default;
                    if (enemy.Flags.TestFlag(EnemyFlags.CollideBeam)
                        && CollisionDetection.CheckCylinderOverlapVolume(enemy.HurtVolume, BackPosition, Position, CylinderRadius, ref res))
                    {
                        if (Beam == BeamType.OmegaCannon && enemy.EnemyType == EnemyType.GoreaMeteor)
                        {
                            enemy.TakeDamage(500, this);
                        }
                        else if (res.Distance < minDist)
                        {
                            minDist = res.Distance;
                            anyRes = res;
                            colWith = enemy;
                            noColEff = false;
                        }
                    }
                }
            }
            bool hitHalfturret = false;
            // todo: visualize player collision (and rename some "pickup" fields)
            foreach (PlayerEntity player in _scene.GetPlayerEntities())
            {
                if (player.Health == 0 || player.Flags2.TestFlag(PlayerFlags2.Spectating))
                {
                    continue;
                }
                if (NetLog.Enabled)
                {
                    if (!_scene.Services.IsReplica) NetDamage.PlayerChecks[player.SlotIndex]++;
                }
                bool hasHalfturret = NetUnlagged.TryCollisionHalfturret(player, out Vector3 halfturretPosition);
                if ((Owner == player || hasHalfturret && Owner == player.Halfturret)
                    && (!Flags.TestFlag(BeamFlags.SelfDamage) || Age < 1 / 30f * 4))
                {
                    continue;
                }
                // An unavailable old-life target must not remain hittable at
                // its present position while the rest of the world is rewound.
                if (!_scene.Services.IsReplica && !NetUnlagged.CollisionTargetAvailable(player)) continue;
                var body = NetHistoricalTrace.CurrentBody(player);
                bool hitPlayer = NetHistoricalTrace.Intersect(body, BackPosition, Position, CylinderRadius, out var playerRes);
                if (player.ModHistoricalCollisionActive && body.Type == HistoricalBodyType.KandenChain)
                {
                    NetUnlagged.KandenHistoricalSegmentChecks++;
                    if (hitPlayer) NetUnlagged.KandenHistoricalSegmentHits++;
                }
                NetContinuousTargetDiagnostics.CollisionResult(this, player, hitPlayer);
                if (hitPlayer && playerRes.Distance < minDist)
                {
                    if (!_scene.Services.IsReplica) NetDamage.NotePlayerOverlap(Owner, player);
                    if (NetLog.Enabled)
                    {
                        if (!_scene.Services.IsReplica) NetDamage.PlayerOverlaps[player.SlotIndex]++;
                        if (!_scene.Services.IsReplica) NetDamage.PlayerAccepted[player.SlotIndex]++;
                    }
                    minDist = playerRes.Distance;
                    anyRes = playerRes;
                    colWith = player;
                    noColEff = false;
                    hitHalfturret = false;
                }
                else if (hitPlayer && NetLog.Enabled)
                {
                    if (!_scene.Services.IsReplica) NetDamage.PlayerOverlaps[player.SlotIndex]++;
                }
                // todo?: else wifi check
                if (hasHalfturret && Owner != player.Halfturret)
                {
                    CollisionResult turretRes = default;
                    float radius = CylinderRadius + 0.45f;
                    if (CollisionDetection.CheckCylinderOverlapSphere(BackPosition, Position, halfturretPosition,
                        radius, ref turretRes) && turretRes.Distance < minDist)
                    {
                        minDist = turretRes.Distance;
                        anyRes = turretRes;
                        colWith = player.Halfturret;
                        noColEff = false;
                        hitHalfturret = true;
                    }
                }
            }
            if (_scene.GameState.SinglePlayer)
            {
                foreach (BeamProjectileEntity other in _scene.GetBeamProjectileEntities())
                {
                    if (other.Owner == Owner || other.Flags.TestFlag(BeamFlags.Collided) || !other.Flags.TestFlag(BeamFlags.Destroyable)
                        || other.Owner?.Type == EntityType.EnemyInstance && Owner.Type == EntityType.EnemyInstance)
                    {
                        continue;
                    }
                    CollisionResult beamRes = default;
                    int radiusIndex = (int)(other.Flags & (BeamFlags.RadiusIndex1 | BeamFlags.RadiusIndex2)) >> 9;
                    float radius = Metadata.BeamRadiusValues[radiusIndex];
                    if (CollisionDetection.CheckCylinderOverlapSphere(BackPosition, Position, other.Position,
                        radius, ref beamRes) && beamRes.Distance < minDist)
                    {
                        minDist = beamRes.Distance;
                        anyRes = beamRes;
                        colWith = other;
                        noColEff = true;
                    }
                }
            }
            NetContinuousTargetDiagnostics.CollisionWinner(this, colWith, minDist);
            // Keep the native winning boundary, before impact/ricochet mutates
            // Position or reuses a projectile pool slot. This is claim evidence,
            // not another simulation or a damage decision.
            NetAttackPaths.Segment(this, minDist >= 0 && minDist <= 1 ? anyRes.Position : Position);
            if (minDist >= 0 && minDist <= 1)
            {
                float amt = Fixed.ToFloat(204);
                Position = new Vector3(
                    anyRes.Position.X + anyRes.Plane.X * amt,
                    anyRes.Position.Y + anyRes.Plane.Y * amt,
                    anyRes.Position.Z + anyRes.Plane.Z * amt
                );
                bool ricochet = true;
                if (DrawFuncId == 12)
                {
                    _soundSource.PlaySfx(SfxId.BIGEYE_ATTACK1C, noUpdate: true);
                }
                if (colWith != null)
                {
                    if (colWith.Type == EntityType.Player || colWith.Type == EntityType.Halfturret)
                    {
                        PlayerEntity player;
                        if (colWith.Type == EntityType.Player)
                        {
                            player = (PlayerEntity)colWith;
                        }
                        else
                        {
                            player = ((HalfturretEntity)colWith).Owner;
                        }
                        DamageFlags damageFlags = DamageFlags.NoDmgInvuln;
                        if (player.BeamEffectiveness[(int)Beam] == Effectiveness.Zero)
                        {
                            if (_scene.GameState.SinglePlayer && Owner == _scene.Players.Main)
                            {
                                Matrix4 transform = GetTransformMatrix(Vector3.UnitX, Vector3.UnitY, player.Position);
                                EffectEntry? effect = _scene.SpawnEffectGetEntry(115, transform); // ineffectivePsycho
                                if (effect != null)
                                {
                                    effect.SetReadOnlyField(0, 1); // radius
                                    _scene.DetachEffectEntry(effect, setExpired: false);
                                }
                            }
                        }
                        else
                        {
                            if (hitHalfturret)
                            {
                                damageFlags |= DamageFlags.Halfturret;
                            }
                            Vector3 damageDir = GetDamageDirection(anyRes.Position, player.Position);
                            float damage = 0;
                            uint wholeDamage = 0;
                            bool isHeadshot = false;
                            if (!player.ModCollisionIsAltForm && Beam != BeamType.ShockCoil
                                && anyRes.Position.Y - player.Position.Y >= Fixed.ToFloat(player.Values.MaxPickupHeight) - 0.3f)
                            {
                                if (Beam == BeamType.Imperialist)
                                {
                                    isHeadshot = true;
                                }
                                else
                                {
                                    // todo: it's kind of lame that you can't get headshots at this range
                                    Vector3 travel = Position - SpawnPosition;
                                    isHeadshot = travel.LengthSquared <= 15 * 15;
                                }
                            }
                            if (isHeadshot)
                            {
                                if (MaxDistance > 0)
                                {
                                    float pct = Vector3.Distance(Position, SpawnPosition) / MaxDistance;
                                    damage = GetInterpolatedValue(DamageInterpolation, HeadshotDamage, 0, pct);
                                }
                                else
                                {
                                    damage = HeadshotDamage;
                                }
                                if (MathF.Abs(Damage - HeadshotDamage) > 1 / 4096f)
                                {
                                    damageFlags |= DamageFlags.Headshot;
                                }
                            }
                            else
                            {
                                if (MaxDistance > 0)
                                {
                                    float pct = Vector3.Distance(Position, SpawnPosition) / MaxDistance;
                                    damage = GetInterpolatedValue(DamageInterpolation, Damage, 0, pct);
                                }
                                else
                                {
                                    damage = Damage;
                                }
                            }
                            damage = ModBalancedRangeDamage(damage, anyRes.Position);
                            wholeDamage = (uint)Math.Clamp(damage, 0, Int32.MaxValue);
                            NetContinuousTargetDiagnostics.CollisionResult(this, player, true, wholeDamage);
                            int targetHealthBefore = player.Health;
                            if (wholeDamage != 0)
                            {
                                EnhancedDirectHit = true;
                                try { TakePlayerDamageAt(player, wholeDamage, damageFlags, damageDir, anyRes.Position); }
                                finally { EnhancedDirectHit = false; }
                            }
                            int actualDamageDealt = Math.Max(0, targetHealthBefore - player.Health);
                            if (Flags.TestFlag(BeamFlags.LifeDrain) && Owner.Type == EntityType.Player)
                            {
                                var ownerPlayer = (PlayerEntity)Owner;
                                if (ownerPlayer != player && !ownerPlayer.IsPrimeHunter
                                    && !TeamRules.AreAllies(ownerPlayer.TeamIndex, player.TeamIndex))
                                {
                                    int before = ownerPlayer.Health;
                                    uint drainHeal = wholeDamage;
                                    if (_scene.GameState.Multiplayer && _scene.GameState.BalancedMode
                                        && ownerPlayer.Hunter == Hunter.Sylux
                                        && BalancedModeRules.IsAffinity(ownerPlayer.Hunter, BeamType.ShockCoil))
                                    {
                                        drainHeal = (uint)ownerPlayer.ModBalancedLifeDrainHeal(
                                            actualDamageDealt, ownerPlayer.HealthMax - ownerPlayer.Health);
                                    }
                                    // GainHealth checks if the player is alive
                                    ownerPlayer.GainHealth(drainHeal);
                                    // What it actually gained, not what it was
                                    // offered: the halfturret splits a heal in
                                    // two and a full tank takes none of it, and
                                    // a credit for health that was never
                                    // granted would float the bar above what
                                    // the authority is about to report. See
                                    // Mods.Network.NetHitPrediction.NoteDrain.
                                    if (!_scene.Services.IsReplica) Mods.Network.NetHitPrediction.NoteDrain(ownerPlayer,
                                        ownerPlayer.Health - before);
                                }
                            }
                            if (!player.IsMainPlayer || player.ModCollisionIsAltForm || player.IsMorphing)
                            {
                                SpawnCollisionEffect(anyRes, noSplat: true);
                            }
                        }
                        OnCollision(anyRes, colWith);
                        PlayBeamHitSfx();
                        ricochet = false;
                    }
                    else if (colWith.Type == EntityType.EnemyInstance)
                    {
                        var enemy = (EnemyInstanceEntity)colWith;
                        if (enemy.GetEffectiveness(Beam) == Effectiveness.Zero && (enemy.EnemyType == EnemyType.FireSpawn
                            || (enemy.Owner as EnemyInstanceEntity)?.EnemyType == EnemyType.FireSpawn))
                        {
                            // when ineffective + FireSpawn or HitZone owned by FireSpawn
                            Vector3 facing = enemy.Transform.Row2.Xyz.Normalized();
                            float w = Vector3.Dot(facing, enemy.Position + facing * Fixed.ToFloat(0x3800));
                            anyRes.Plane = new Vector4(facing, w);
                            float dot = Vector3.Dot(Position, facing);
                            anyRes.Position = new Vector3(
                                Position.X + facing.X * (dot - w),
                                Position.Y + facing.Y * (dot - w),
                                Position.Z + facing.Z * (dot - w)
                            );
                            ProcessRicochet(anyRes);
                        }
                        else
                        {
                            float damage = Damage;
                            if (MaxDistance > 0)
                            {
                                float pct = Vector3.Distance(Position, SpawnPosition) / MaxDistance;
                                damage = GetInterpolatedValue(DamageInterpolation, Damage, 0, pct);
                            }
                            damage = ModBalancedRangeDamage(damage, anyRes.Position);
                            if (damage > 0 && (Beam != BeamType.ShockCoil
                                || (ModHasSharedContinuousPhase ? ModContinuousPhase : _scene.FrameCount) % 2 == 0)) // todo: FPS stuff
                            {
                                enemy.TakeDamage((uint)damage, this);
                                SpawnCollisionEffect(anyRes, noSplat: true);
                            }
                            OnCollision(anyRes, colWith);
                            PlayBeamHitSfx();
                        }
                        ricochet = false;
                    }
                    else if (colWith.Type == EntityType.Door)
                    {
                        var door = (DoorEntity)colWith;
                        SpawnCollisionEffect(anyRes, noSplat: true);
                        OnCollision(anyRes, colWith);
                        PlayBeamHitSfx();
                        if (Owner?.Type == EntityType.Player)
                        {
                            var player = (PlayerEntity)Owner;
                            if (player.IsMainPlayer || _scene.CameraMode != CameraMode.Player) // skdebug
                            {
                                if (door.Flags.TestFlag(DoorFlags.Locked) && !door.Flags.TestFlag(DoorFlags.ShowLock))
                                {
                                    if (door.Data.PaletteId == (int)Beam)
                                    {
                                        door.Unlock(updateState: true, noLockAnimSfx: true);
                                    }
                                    else if (_scene.GameState.SinglePlayer)
                                    {
                                        // todo: handle messages like this
                                        _scene.SendMessage(Message.ShowWarning, this, null, 40, 90 * 2, 5 * 2); // todo: FPS stuff
                                    }
                                }
                                if (!_scene.GameState.InRoomTransition)
                                {
                                    door.Flags |= DoorFlags.ShotOpen;
                                }
                            }
                        }
                        ricochet = false;
                    }
                    else if (colWith.Type == EntityType.ForceField)
                    {
                        var forceField = (ForceFieldEntity)colWith;
                        if (!Flags.TestFlag(BeamFlags.Ricochet))
                        {
                            SpawnCollisionEffect(anyRes, noSplat: true);
                            OnCollision(anyRes, colWith);
                            PlayBeamHitSfx();
                            forceField.Lock?.LockHit(this);
                            ricochet = false;
                        }
                    }
                    else if (colWith.Type == EntityType.BeamProjectile)
                    {
                        var other = (BeamProjectileEntity)colWith;
                        if (Flags.TestFlag(BeamFlags.ForceEffect))
                        {
                            SpawnCollisionEffect(anyRes, noSplat: true);
                        }
                        OnCollision(anyRes, colWith);
                        PlayBeamHitSfx();
                        if (other.Flags.TestFlag(BeamFlags.ForceEffect))
                        {
                            other.SpawnCollisionEffect(anyRes, noSplat: true);
                        }
                        if (other.DrawFuncId == 12) // Slench tear
                        {
                            _soundSource.PlaySfx(SfxId.BIGEYE_ATTACK1C, noUpdate: true);
                            ItemType item = ItemType.None;
                            uint rand = _scene.Random.GetRandomInt2(100);
                            if (_scene.AreaId == 0) // Slench 1 (Alinos 1)
                            {
                                // 5% small missile, 5% medium health, 90% nothing
                                if (rand < 5)
                                {
                                    item = ItemType.HealthMedium;
                                }
                                else if (rand < 10)
                                {
                                    item = ItemType.MissileSmall;
                                }
                            }
                            else // Slench 2 (Arcterra 1), Slench 3 (Archives 2), Slench 4 (VDO 2)
                            {
                                // 50% small UA, 25% medium health, 25% nothing
                                if (rand < 25)
                                {
                                    item = ItemType.HealthMedium;
                                }
                                else if (rand < 75)
                                {
                                    item = ItemType.UASmall;
                                }
                            }
                            if (item != ItemType.None)
                            {
                                NodeRef nodeRef = _scene.GetNodeRefByPosition(other.Position);
                                ItemSpawnEntity.SpawnItemDrop(item, other.Position, nodeRef, chance: 100, _scene);
                            }
                        }
                        else if (other.DrawFuncId == 11) // Omega Cannon
                        {
                            _soundSource.PlaySfx(SfxId.GOREA_ATTACK3B, noUpdate: true);
                        }
                        else
                        {
                            _soundSource.PlaySfx(SfxId.LOB_GUN_HIT, noUpdate: true);
                        }
                        other.OnCollision(anyRes, this);
                        ricochet = false;
                    }
                }
                else
                {
                    // collided with room, platform, or object collision
                    bool reflected = anyRes.Flags.TestFlag(CollisionFlags.ReflectBeams);
                    if (anyRes.EntityCollision != null)
                    {
                        _scene.SendMessage(Message.BeamCollideWith, this, anyRes.EntityCollision.Entity, anyRes, 0);
                        anyRes.EntityCollision.Entity.CheckBeamReflection(ref reflected);
                    }
                    if ((!Flags.TestFlag(BeamFlags.Ricochet) && !reflected)
                        || DrawFuncId == 8 || anyRes.Terrain >= Terrain.Acid)
                    {
                        if (!noColEff || Flags.TestFlag(BeamFlags.ForceEffect))
                        {
                            bool noSplat = anyRes.Terrain == Terrain.Lava || anyRes.EntityCollision != null;
                            SpawnCollisionEffect(anyRes, noSplat);
                        }
                        if (RicochetWeapon != null)
                        {
                            PlayRicochetSfx();
                        }
                        else if (anyRes.Terrain <= Terrain.Lava)
                        {
                            PlayBeamHitSfx();
                        }
                        else
                        {
                            _soundSource.PlaySfx(SfxId.GENERIC_HIT, noUpdate: true);
                        }
                        TryBattlehammerImpactCluster(anyRes);
                        OnCollision(anyRes, colWith: null);
                        ricochet = false;
                    }
                }
                if (ricochet)
                {
                    ProcessRicochet(anyRes);
                }
                if (DrawFuncId == 8)
                {
                    SpawnSniperBeam();
                }
            }
        }

        private void ProcessRicochet(CollisionResult colRes)
        {
            Mods.EnhancedHunters.SpireEnhancement.Ricochet(this);
            float dot1 = Vector3.Dot(Velocity, colRes.Plane.Xyz);
            Velocity = new Vector3(
                (Velocity.X - 2 * colRes.Plane.X * dot1) * RicochetLossH,
                (Velocity.Y - 2 * colRes.Plane.Y * dot1) * RicochetLossV,
                (Velocity.Z - 2 * colRes.Plane.Z * dot1) * RicochetLossH
            );
            Speed = Velocity.Length;
            float dot2 = Vector3.Dot(Direction, colRes.Plane.Xyz);
            Direction = new Vector3(
                Direction.X - 2 * colRes.Plane.X * dot2,
                Direction.Y - 2 * colRes.Plane.Y * dot2,
                Direction.Z - 2 * colRes.Plane.Z * dot2
            );
            float dot3 = Vector3.Dot(colRes.Position, colRes.Plane.Xyz);
            float factor = 0.01f - (dot3 - colRes.Plane.W);
            BackPosition = Position = new Vector3(
                colRes.Position.X + colRes.Plane.X * factor,
                colRes.Position.Y + colRes.Plane.Y * factor,
                colRes.Position.Z + colRes.Plane.Z * factor
            );
            // hack to fix past positions after ricochet
            for (int i = 9; i > 0; i--)
            {
                PastPositions[i] = PastPositions[i - 1];
            }
            PastPositions[0] = Position;
            if (_scene.FrameCount % 2 == 0)
            {
                for (int i = 9; i > 0; i--)
                {
                    PastPositions[i] = PastPositions[i - 1];
                }
                PastPositions[0] = Position;
            }
            PlayRicochetSfx();
        }

        private void PlayRicochetSfx()
        {
            bool charged = Flags.TestFlag(BeamFlags.Charged);
            if (charged && Beam == BeamType.Magmaul)
            {
                PlayBeamHitSfx();
                return;
            }
            int sfx = Metadata.BeamSfx[(int)Beam, (int)BeamSfx.Ricochet];
            if (sfx != -1)
            {
                float amountA;
                if (Beam == BeamType.Judicator)
                {
                    amountA = _scene.Random.GetRandomInt1(0xFFFF);
                }
                else
                {
                    amountA = 0xFFFF * (Speed * 2) / Fixed.ToFloat(3300); // todo: FPS stuff
                }
                _soundSource.PlaySfx(sfx, noUpdate: true, amountA: amountA);
            }
        }

        private void StopHomingSfx()
        {
            int sfx = Metadata.BeamSfx[(int)Beam, (int)BeamSfx.Homing];
            if (sfx != -1)
            {
                _soundSource.StopSfx(sfx);
            }
        }

        private void PlayBeamHitSfx()
        {
            if (_scene.Services.IsReplica && _replayImpactHidden) return;
            StopHomingSfx();
            BeamSfx type = Flags.TestFlag(BeamFlags.Charged) ? BeamSfx.ChargeHit : BeamSfx.Hit;
            int sfx = Metadata.BeamSfx[(int)Beam, (int)type];
            if (sfx != -1)
            {
                _soundSource.PlaySfx(sfx, noUpdate: true);
            }
        }

        /// <summary>
        /// Balanced Mode Battlehammer terrain identity: a parent shell that
        /// commits into room geometry creates three short-hop submunitions.
        /// Player/direct hits deliberately skip the cluster and keep the full
        /// direct-hit reward.
        /// </summary>
        internal bool TryBattlehammerImpactCluster(CollisionResult colRes)
        {
            if (!_scene.GameState.Multiplayer || !_scene.GameState.BalancedMode
                || Beam != BeamType.Battlehammer || BattlehammerClusterChild
                || Owner is not PlayerEntity owner || Flags.TestFlag(BeamFlags.Collided)
                || colRes.Terrain > Terrain.Lava)
            {
                return false;
            }

            bool weavelAffinity = owner.Hunter == Hunter.Weavel
                && BalancedModeRules.IsAffinity(owner.Hunter, BeamType.Battlehammer);
            float powerScale = Damage > 0 ? Damage / 18f : 1f;
            powerScale = Math.Max(0, powerScale);

            // Terrain impact itself is pressure, not the payoff. The three
            // children own the area-control follow-up.
            Damage = 0;
            HeadshotDamage = 0;
            SplashDamage = 3f * powerScale;
            SplashRadius = 1.5f;
            SplashDamageType = 0;
            DamageDirType = 2;
            DamageDirMag = weavelAffinity ? 0.30f : 0.25f;

            WeaponInfo clusterWeapon = _scene.WeaponRules[(int)BeamType.Battlehammer];
            var clusterEquip = new EquipInfo(clusterWeapon, Equip!.Beams)
            {
                InfiniteAmmo = true
            };

            Vector3 normal = colRes.Plane.Xyz;
            if (!Single.IsFinite(normal.X) || !Single.IsFinite(normal.Y)
                || !Single.IsFinite(normal.Z) || normal.LengthSquared <= 0.0001f)
            {
                normal = Vector3.UnitY;
            }
            else
            {
                normal = normal.Normalized();
            }

            Vector3 forward = Direction - normal * Vector3.Dot(Direction, normal);
            if (forward.LengthSquared <= 0.0001f)
            {
                forward = Vector3.Cross(normal,
                    MathF.Abs(normal.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX);
            }
            forward = forward.Normalized();
            Vector3 right = Vector3.Cross(normal, forward).Normalized();
            Vector3 baseHop = (forward * 0.72f + normal * 0.48f).Normalized();
            Vector3 spawnPosition = colRes.Position + normal * 0.08f;

            ReadOnlySpan<float> spread = stackalloc float[] { -0.45f, 0f, 0.45f };
            BalancedModeTelemetry.NoteBattlehammerTerrainImpact(owner, spread.Length);
            for (int i = 0; i < spread.Length; i++)
            {
                Vector3 childDirection = (baseHop + right * spread[i]).Normalized();
                Spawn(owner, clusterEquip, spawnPosition, childDirection,
                    BeamSpawnFlags.NoMuzzle, NodeRef, _scene, parent: this,
                    battlehammerCluster: true, battlehammerClusterScale: powerScale);
            }
            return true;
        }

        public void OnCollision(CollisionResult colRes, EntityBase? colWith, bool enhancedImpact = true)
        {
            if (!Flags.TestFlag(BeamFlags.Collided))
            {
                Mods.EnhancedHunters.EnhancedHunterProjectiles.ScaleExplosion(this);
                if (enhancedImpact) Mods.EnhancedHunters.EnhancedHunterProjectiles.Impact(this, colRes, colWith);
            }
            if (Effect != null) // game also checks the HasModel flag, but it's either-or
            {
                _scene.DetachEffectEntry(Effect, setExpired: true);
                Effect = null;
            }
            if (SplashDamage > 0 && colRes.Terrain <= Terrain.Lava)
            {
                Debug.Assert(Equip != null);
                Debug.Assert(Owner != null);
                // note: when hitting halfturret, colWith has been replaced with the turret's owning player by this point
                Vector3 splashOrigin = ModSplashOrigin(_scene, Beam, Position, colRes.Position);
                int excludedVictim = colWith is PlayerEntity hitPlayer ? hitPlayer.SlotIndex
                    : colWith is HalfturretEntity hitTurret ? hitTurret.Owner.SlotIndex : -1;
                NetAttackPaths.Splash(this, splashOrigin, excludedVictim);
                CheckSplashDamage(colWith, splashOrigin);
                if (RicochetWeapon != null && (colWith == null || colWith.Type != EntityType.Player))
                {
                    Vector3 factor = Velocity * 7;
                    float dot = Vector3.Dot(colRes.Plane.Xyz, factor);
                    Vector3 spawnDir = new Vector3(
                        colRes.Plane.X + factor.X - colRes.Plane.X * 2 * dot,
                        colRes.Plane.Y + factor.Y - colRes.Plane.Y * 2 * dot,
                        colRes.Plane.Z + factor.Z - colRes.Plane.Z * 2 * dot
                    ).Normalized();
                    if (Owner.Type == EntityType.Player)
                    {
                        var player = (PlayerEntity)Owner;
                        if (player.IsBot && _scene.GameState.SinglePlayer && player.Hunter == Hunter.Spire)
                        {
                            int encounter = _scene.GameState.EncounterState[player.SlotIndex];
                            ushort damage = 3;
                            if (encounter == 2 || encounter == 0 && player.BotLevel > 0)
                            {
                                damage = 4;
                            }
                            _ricochetEquip.UnchargedDamage = damage;
                            _ricochetEquip.MinChargeDamage = damage;
                            _ricochetEquip.ChargedDamage = damage;
                            _ricochetEquip.HeadshotDamage = damage;
                            _ricochetEquip.MinChargeHeadshotDamage = damage;
                            _ricochetEquip.ChargedHeadshotDamage = damage;
                            _ricochetEquip.SplashDamage = damage;
                            _ricochetEquip.MinChargeSplashDamage = damage;
                            _ricochetEquip.ChargedSplashDamage = damage;
                            _ricochetEquip.DmgDirTypes[0] = 0;
                            _ricochetEquip.DmgDirTypes[1] = 0;
                        }
                    }
                    _ricochetEquip.Beams = Equip.Beams;
                    _ricochetEquip.Weapon = RicochetWeapon;
                    BeamSpawnFlags flags = BeamSpawnFlags.None;
                    if (Flags.TestFlag(BeamFlags.Charged))
                    {
                        flags |= BeamSpawnFlags.Charged;
                    }
                    Spawn(Owner, _ricochetEquip, colRes.Position, spawnDir, flags, NodeRef, _scene, parent: this);
                }
            }
            if (!Flags.TestFlag(BeamFlags.Continuous))
            {
                Flags |= BeamFlags.Collided;
                Lifespan = 4 * (1 / 30f); // todo: frame time stuff
                Velocity = Vector3.Zero;
            }
            if (Owner != null)
            {
                _scene.SendMessage(Message.Impact, this, Owner, colWith ?? (object)0, 0);
                StopHomingSfx();
            }
        }

        private void CheckSplashDamage(EntityBase? colWith, Vector3 splashOrigin)
        {
            foreach (PlayerEntity player in _scene.GetPlayerEntities())
            {
                if (player == colWith)
                {
                    continue;
                }

                void OmegaCannonFlash()
                {
                    if (Beam == BeamType.OmegaCannon && player == _scene.Players.Main)
                    {
                        _scene.SetFade(FadeType.FadeInWhite, 15 / 30f, overwrite: false);
                    }
                }

                if (player.Health > 0)
                {
                    if (!player.Flags2.TestFlag(PlayerFlags2.Halfturret) || Owner != player.Halfturret)
                    {
                        CollisionResult discard = default;
                        float dist = Vector3.Distance(player.Position, splashOrigin);
                        // todo?: wifi conditions
                        if (dist >= SplashRadius
                            || CollisionDetection.CheckBetweenPoints(splashOrigin, player.Position, TestFlags.Beams, _scene, ref discard))
                        {
                            OmegaCannonFlash();
                        }
                        else
                        {
                            Vector3 damageDir = GetDamageDirection(splashOrigin, player.Position);
                            float ratio = dist / SplashRadius;
                            int damage = (int)ModBalancedRangeDamage(
                                GetInterpolatedValue(SplashDamageType, SplashDamage, 0, ratio), splashOrigin);
                            // ReplayShotFact owns the explosion centre, not the
                            // victim position. Several victims may share this one
                            // splash fact origin without dragging the projectile
                            // toward each body independently.
                            TakePlayerDamageAt(player, damage, DamageFlags.NoDmgInvuln, damageDir, splashOrigin);
                            if (Owner != null)
                            {
                                _scene.SendMessage(Message.Impact, this, Owner, player, 0);
                                StopHomingSfx();
                            }
                        }
                    }
                }
                else
                {
                    OmegaCannonFlash();
                }
            }
            foreach (EnemyInstanceEntity enemy in _scene.GetEnemyInstanceEntities())
            {
                if (enemy == colWith || !enemy.Flags.TestFlag(EnemyFlags.CollideBeam))
                {
                    continue;
                }
                CollisionResult res = default;
                float dist = Vector3.Distance(enemy.Position, splashOrigin);
                if (dist < SplashRadius
                    && !CollisionDetection.CheckBetweenPoints(splashOrigin, enemy.Position, TestFlags.Beams, _scene, ref res))
                {
                    float damage = ModBalancedRangeDamage(
                        GetInterpolatedValue(SplashDamageType, SplashDamage, 0, dist / SplashRadius), splashOrigin);
                    enemy.TakeDamage((uint)Math.Clamp(damage, 0, Int32.MaxValue), this);
                    if (Owner != null)
                    {
                        _scene.SendMessage(Message.Impact, this, Owner, enemy, 0);
                        StopHomingSfx();
                    }
                }
            }
        }

        internal static float GetInterpolatedValue(int type, float value1, float value2, float ratio)
        {
            if (type == 3)
            {
                // binary
                return ratio > 1 ? value2 : value1;
            }
            ratio = Math.Clamp(ratio, 0, 1);
            if (type == 0)
            {
                // lerp
                return value1 + (value2 - value1) * ratio;
            }
            if (type == 1)
            {
                // sin 1
                return value1 + (value2 - value1) * ((MathF.Sin(MathHelper.DegreesToRadians(270 - 180 * ratio)) + 1) / 2);
            }
            if (type == 2)
            {
                // sin 2
                return value1 + (value2 - value1) * (MathF.Sin(MathHelper.DegreesToRadians(270 - 90 * ratio)) + 1);
            }
            return 0;
        }

        private ImpactDrawEndpoint _liveImpactEndpoint;
        internal bool ModSupportsLiveTrail => DrawFuncId is 0 or 1 or 2 or 3 or 6 or 7 or 10 or 12;
        internal Vector3 ModLiveTrailOrigin => DrawFuncId is 0 or 1 ? BackPosition
            : DrawFuncId is 6 or 12 ? PastPositions[8] : PastPositions[2];
        internal void ModSetLiveDrawPoint(in LiveCombatImpact impact, Vector3 point)
        {
            if (ModSupportsLiveTrail && ModReplayIdentityMatches(ModLaunchKey, ModShotId, Beam, impact.Fact)
                && ModPresentationComponent == impact.Presentation.Component)
                _liveImpactEndpoint = new(ModLaunchKey, ModPresentationComponent, _scene.FrameCount, point);
        }
        private bool LiveEndpointAllowed => NetCombatFactPublisher.LiveEnabled && NetSession.Role == NetRole.Client
            && !_scene.Services.IsReplica && !DemoPlayback.IsActive
            && ModLaunchKey == ShotKey.For(ModLaunchKey.ShooterSlot, ModShotId);
        private Vector3 LiveDrawPoint => LiveEndpointAllowed
            && _liveImpactEndpoint.TryPoint(ModLaunchKey, ModPresentationComponent, _scene.FrameCount, out var point)
                ? point : Position;
        private Vector3 LiveTrailTip => LiveEndpointAllowed
            && _liveImpactEndpoint.TryPoint(ModLaunchKey, ModPresentationComponent, _scene.FrameCount, out var point)
                ? point : PastPositions[0];

        public override void GetDrawInfo()
        {
            if (Mods.ThumbnailMode.SuppressCombatPresentation) return;
            if (_scene.Services.IsReplica && _replayImpactHidden) return;

            bool anchored = _scene.Services.IsReplica && _replayImpactPending
                && _replayImpactDrawFrame == _scene.FrameCount;
            Vector3 position = Position, back = BackPosition, past0 = PastPositions[0];
            bool auditLive = NetImpactDiagnostics.Enabled && NetCombatFactPublisher.LiveEnabled && !_scene.Services.IsReplica;
            Vector3 velocity = Velocity; float lifespan = Lifespan; var flags = Flags;
            Span<Vector3> history = auditLive ? stackalloc Vector3[PastPositions.Length] : Span<Vector3>.Empty;
            if (auditLive) PastPositions.AsSpan().CopyTo(history);
            if (anchored)
            {
                // Let the visible projectile/tracer terminate exactly where the
                // authority says it did for this one draw. The simulation copy
                // underneath keeps flying untouched and is hidden afterwards.
                Position = _replayImpactPosition;
                PastPositions[0] = _replayImpactPosition;
            }
            try
            {
                if (DrawFuncId == 0) Draw00();
                else if (DrawFuncId == 1) Draw01();
                else if (DrawFuncId == 2) Draw02();
                else if (DrawFuncId == 3) Draw03();
                else if (DrawFuncId == 6 || DrawFuncId == 12) Draw06();
                else if (DrawFuncId == 7) Draw07();
                else if (DrawFuncId == 9) Draw09();
                else if (DrawFuncId == 10) Draw10();
                else if (DrawFuncId == 17) Draw17();
            }
            finally
            {
                if (anchored)
                {
                    Position = position;
                    BackPosition = back;
                    PastPositions[0] = past0;
                }
                if (auditLive)
                {
                    NetImpactDiagnostics.DrawInvariantChecks++;
                    if (Position != position || BackPosition != back || Velocity != velocity || Lifespan != lifespan
                        || Flags != flags || !history.SequenceEqual(PastPositions))
                    {
                        NetImpactDiagnostics.DrawInvariantFailures++;
                        throw new InvalidOperationException("Live projectile drawing changed simulation state.");
                    }
                }
            }
        }

        // Power Beam
        private void Draw00()
        {
            if (!Flags.TestFlag(BeamFlags.Collided))
            {
                _scene.AddSingleParticle(SingleType.Fuzzball, LiveDrawPoint, Color, alpha: 1, scale: 1 / 4f);
            }
            DrawTrail1(Fixed.ToFloat(122));
        }

        // uncharged Volt Driver
        private void Draw01()
        {
            DrawTrail1(Fixed.ToFloat(614));
        }

        // charged Volt Driver
        private void Draw02()
        {
            DrawTrail2(Fixed.ToFloat(1024), 5);
        }

        // non-affinity Judicator
        private void Draw03()
        {
            if (!Flags.TestFlag(BeamFlags.Collided))
            {
                base.GetDrawInfo();
            }
            DrawTrail2(Fixed.ToFloat(204), 5);
        }

        // enemy tear/Judicator
        private void Draw06()
        {
            if (!Flags.TestFlag(BeamFlags.Collided))
            {
                _scene.AddSingleParticle(SingleType.Fuzzball, LiveDrawPoint, Vector3.One, alpha: 1, scale: 1 / 4f);
            }
            DrawTrail3(Fixed.ToFloat(204));
        }

        // Missile
        private void Draw07()
        {
            if (!Flags.TestFlag(BeamFlags.Collided))
            {
                _scene.AddSingleParticle(SingleType.Fuzzball, LiveDrawPoint, Vector3.One, alpha: 1, scale: 1 / 4f);
            }
            DrawTrail2(Fixed.ToFloat(204), 5);
        }

        // Shock Coil
        private void Draw09()
        {
            if (!Flags.TestFlag(BeamFlags.Collided))
            {
                if (Target != null)
                {
                    DrawTrail4(height: 0.15f, range: 0.5f, segments: 10);
                }
                else if (Owner == _scene.Players.Main)
                {
                    DrawTrail4(height: 0.025f, range: 0.35f, segments: 5);
                }
            }
        }

        // Battlehammer
        private void Draw10()
        {
            DrawTrail2(Fixed.ToFloat(81), 2);
        }

        // green energy beam
        private void Draw17()
        {
            if (!Flags.TestFlag(BeamFlags.Collided))
            {
                base.GetDrawInfo();
            }
        }

        private void DrawTrail1(float height)
        {
            Debug.Assert(_trailModel != null);
            Texture texture = _trailModel.Model.Recolors[0].Textures[0];
            float uvS = (texture.Width - (1 / 16f)) / texture.Width;
            float uvT = (texture.Height - (1 / 16f)) / texture.Height;
            Vector3[] uvsAndVerts = ArrayPool<Vector3>.Shared.Rent(8);
            uvsAndVerts[0] = Vector3.Zero;
            uvsAndVerts[1] = new Vector3(LiveDrawPoint.X - BackPosition.X, LiveDrawPoint.Y - BackPosition.Y - height, LiveDrawPoint.Z - BackPosition.Z);
            uvsAndVerts[2] = new Vector3(0, uvT, 0);
            uvsAndVerts[3] = new Vector3(LiveDrawPoint.X - BackPosition.X, height + LiveDrawPoint.Y - BackPosition.Y, LiveDrawPoint.Z - BackPosition.Z);
            uvsAndVerts[4] = new Vector3(uvS, 0, 0);
            uvsAndVerts[5] = new Vector3(0, -height, 0);
            uvsAndVerts[6] = new Vector3(uvS, uvT, 0);
            uvsAndVerts[7] = new Vector3(0, height, 0);
            Material material = _trailModel.Model.Materials[0];
            float alpha = Math.Clamp(Lifespan * 30 * 8, 0, 31) / 31;
            _scene.AddRenderItem(RenderItemType.TrailSingle, alpha, _scene.GetNextPolygonId(), Color, material.XRepeat, material.YRepeat,
                material.ScaleS, material.ScaleT, Matrix4.CreateTranslation(BackPosition), uvsAndVerts, _bindingId);
        }

        private void DrawTrail2(float height, int segments)
        {
            Debug.Assert(_trailModel != null);
            if (segments < 2)
            {
                return;
            }
            if (segments > PastPositions.Length / 2)
            {
                segments = PastPositions.Length / 2;
            }
            int count = 4 * segments;
            Texture texture = _trailModel.Model.Recolors[0].Textures[0];
            float uvT = (texture.Height - (1 / 16f)) / texture.Height;
            Vector3[] uvsAndVerts = ArrayPool<Vector3>.Shared.Rent(count);
            for (int i = 0; i < segments; i++)
            {
                float uvS = 0;
                if (i > 0)
                {
                    uvS = (texture.Width / (float)(segments - 1) * i - (1 / 16f)) / texture.Width;
                }
                Vector3 vec = i == 0 ? Vector3.Zero : PastPositions[i * 2] - LiveTrailTip;
                uvsAndVerts[4 * i] = new Vector3(uvS, 0, 0);
                uvsAndVerts[4 * i + 1] = new Vector3(vec.X, vec.Y - height, vec.Z);
                uvsAndVerts[4 * i + 2] = new Vector3(uvS, uvT, 0);
                uvsAndVerts[4 * i + 3] = new Vector3(vec.X, vec.Y + height, vec.Z);
            }
            Material material = _trailModel.Model.Materials[0];
            float alpha = Math.Clamp(Lifespan * 30 * 8, 0, 31) / 31;
            _scene.AddRenderItem(RenderItemType.TrailMulti, alpha, _scene.GetNextPolygonId(), Color, material.XRepeat, material.YRepeat,
                material.ScaleS, material.ScaleT, Matrix4.CreateTranslation(LiveTrailTip), uvsAndVerts, _bindingId, trailCount: count);
        }

        private void DrawTrail3(float height)
        {
            Debug.Assert(_trailModel != null);
            Texture texture = _trailModel.Model.Recolors[0].Textures[0];
            float uvS2 = (texture.Width - (1 / 16f)) / texture.Width;
            float uvT2 = (texture.Height / 4f - (1 / 16f)) / texture.Height;
            Vector3[] uvsAndVerts = ArrayPool<Vector3>.Shared.Rent(8);
            uvsAndVerts[0] = Vector3.Zero;
            uvsAndVerts[1] = new Vector3(0, -height, 0);
            uvsAndVerts[2] = new Vector3(0, uvT2, 0);
            uvsAndVerts[3] = new Vector3(0, height, 0);
            uvsAndVerts[4] = new Vector3(uvS2, 0, 0);
            uvsAndVerts[5] = new Vector3(
                PastPositions[8].X - LiveTrailTip.X,
                PastPositions[8].Y - LiveTrailTip.Y - height,
                PastPositions[8].Z - LiveTrailTip.Z
            );
            uvsAndVerts[6] = new Vector3(uvS2, uvT2, 0);
            uvsAndVerts[7] = new Vector3(PastPositions[8].X - LiveTrailTip.X,
                PastPositions[8].Y - LiveTrailTip.Y + height,
                PastPositions[8].Z - LiveTrailTip.Z
            );
            Material material = _trailModel.Model.Materials[0];
            float alpha = Math.Clamp(Lifespan * 30 * 8, 0, 31) / 31;
            _scene.AddRenderItem(RenderItemType.TrailSingle, alpha, _scene.GetNextPolygonId(), Color, material.XRepeat, material.YRepeat,
                material.ScaleS, material.ScaleT, Matrix4.CreateTranslation(LiveTrailTip), uvsAndVerts, _bindingId);
        }

        private void DrawTrail4(float height, float range, int segments)
        {
            Debug.Assert(_trailModel != null);
            if (segments < 2)
            {
                return;
            }
            int count = 4 * segments;

            int frames = (int)_scene.LiveFrames / 2;
            uint rng = (uint)(frames + (int)(Position.X * 4096));
            int index = frames & 15;
            float halfRange = range / 2;
            Vector3 vec = Position - PastPositions[8];
            Texture texture = _trailModel.Model.Recolors[0].Textures[0];
            float uvT = (texture.Height - (1 / 16f)) / texture.Height;
            Vector3[] uvsAndVerts = ArrayPool<Vector3>.Shared.Rent(count);
            for (int i = 0; i < segments; i++)
            {
                float uvS = 0;
                int factor = index + i;
                if (factor > 0)
                {
                    uvS = (2 * texture.Width / (float)(segments - 1) * factor - (1 / 16f)) / texture.Width;
                }

                float pct = (float)i / (segments - 1);

                // todo?: not sure if dividing by 4 is strictly correct here
                float x = vec.X * pct + Velocity.X / 4 * pct * (1 - pct);
                float y = vec.Y * pct + Velocity.Y / 4 * pct * (1 - pct);
                float z = vec.Z * pct + Velocity.Z / 4 * pct * (1 - pct);

                if (i > 0 && i < segments - 1)
                {
                    x += Rng.CallRng(ref rng, (uint)Fixed.ToInt(range)) / 4096f - halfRange;
                    y += Rng.CallRng(ref rng, (uint)Fixed.ToInt(range)) / 4096f - halfRange;
                    z += Rng.CallRng(ref rng, (uint)Fixed.ToInt(range)) / 4096f - halfRange;
                }

                uvsAndVerts[4 * i] = new Vector3(uvS, 0, 0);
                uvsAndVerts[4 * i + 1] = new Vector3(x, y - height, z);
                uvsAndVerts[4 * i + 2] = new Vector3(uvS, uvT, 0);
                uvsAndVerts[4 * i + 3] = new Vector3(x, y + height, z);
            }

            Material material = _trailModel.Model.Materials[0];
            _scene.AddRenderItem(RenderItemType.TrailMulti, alpha: 1, _scene.GetNextPolygonId(), Color, material.XRepeat, material.YRepeat,
                material.ScaleS, material.ScaleT, Matrix4.CreateTranslation(PastPositions[8]), uvsAndVerts, _bindingId, trailCount: count);
        }

        protected override Matrix4 GetModelTransform(ModelInstance inst, int index)
        {
            if (DrawFuncId == 3)
            {
                Matrix4 transform = GetTransformMatrix(Direction, Up);
                transform.Row3.Xyz = Position;
                return transform;
            }
            if (DrawFuncId == 17)
            {
                Matrix4 transform = Transform;
                float scale = Vector3.Distance(Position, BackPosition);
                transform.Row2.Xyz *= scale;
                transform.Row3.Xyz = BackPosition;
                return transform;
            }
            return base.GetModelTransform(inst, index);
        }

        public override void Destroy()
        {
            _soundSource.StopAllSfx();
            _replayImpactPending = _replayImpactHidden = false;
            _replayImpactDrawFrame = 0;
            _liveImpactEndpoint = default;
            _replayImpactPosition = default;
            Lifespan = 0;
            if (Effect != null)
            {
                _scene.DetachEffectEntry(Effect, setExpired: true);
                Effect = null;
            }
            if (MuzzleEffect != null)
            {
                if (Flags.TestFlag(BeamFlags.DestroyMuzzle))
                {
                    _scene.UnlinkEffectEntry(MuzzleEffect);
                }
                MuzzleEffect = null;
            }
            Owner = null;
            Effect = null;
            Target = null;
            RicochetWeapon = null;
            Equip = null;
            _trailModel = null;
            base.Destroy();
        }

        private static BeamProjectileEntity ChooseBeamSlot(EquipInfo equip, EntityBase owner)
        {
            if (equip.Weapon.Flags.TestFlag(WeaponFlags.Continuous))
            {
                for (int i = 0; i < equip.Beams.Length; i++)
                {
                    BeamProjectileEntity beam = equip.Beams[i];
                    if (beam.Flags.TestFlag(BeamFlags.Continuous) && beam.BeamKind == equip.Weapon.BeamKind
                        && beam.Owner == owner && beam.Lifespan < equip.Weapon.UnchargedLifespan)
                    {
                        return beam;
                    }
                }
            }
            for (int i = 0; i < equip.Beams.Length; i++)
            {
                BeamProjectileEntity beam = equip.Beams[i];
                if (beam.Lifespan <= 0)
                {
                    return beam;
                }
                if (beam.Flags.TestFlag(BeamFlags.Continuous) && beam.BeamKind == equip.Weapon.BeamKind
                    && beam.Owner == owner && beam.Lifespan < equip.Weapon.UnchargedLifespan)
                {
                    return beam;
                }
            }
            return equip.Beams[^1];
        }

        public static BeamResultFlags Spawn(EntityBase owner, EquipInfo equip, Vector3 position, Vector3 direction,
            BeamSpawnFlags spawnFlags, NodeRef nodeRef, Scene scene, BeamProjectileEntity? parent = null,
            bool enhancedMicro = false, bool battlehammerCluster = false, float battlehammerClusterScale = 1f)
        {
            bool finitePosition = Single.IsFinite(position.X) && Single.IsFinite(position.Y)
                && Single.IsFinite(position.Z);
            bool finiteDirection = Single.IsFinite(direction.X) && Single.IsFinite(direction.Y)
                && Single.IsFinite(direction.Z);
            if (!finitePosition || !finiteDirection || direction.LengthSquared <= 0.0000001f)
            {
                // Never put invalid geometry into the projectile pool. Collision
                // math is intentionally branch-heavy and a NaN segment can make
                // ordinary "outside" comparisons fail open against many actors.
                Mods.DebugLog.Line("combat",
                    $"rejected non-finite beam spawn owner={owner.Id} beam={equip.Weapon.Beam}");
                return BeamResultFlags.NoSpawn;
            }
            if (!scene.Services.IsReplica && NetSession.Active && parent != null && !NetPlayerLifecycle.CurrentProjectile(parent))
                return BeamResultFlags.NoSpawn;
            PlayerEntity? turretOwner = parent == null && !scene.Services.IsReplica ? (owner as HalfturretEntity)?.Owner : null;
            if (turretOwner != null)
            {
                if (!NetFireEvents.CanFireTurret(turretOwner)) return BeamResultFlags.NoSpawn;
                NetFireEvents.Begin(turretOwner, position, direction, turret: true);
                if (NetFireEvents.TryAuthoredPose(turretOwner, out Vector3 admittedOrigin,
                    out Vector3 admittedDirection, out _))
                { position = admittedOrigin; direction = admittedDirection.Normalized(); }
            }
            BeamResultFlags result = BeamResultFlags.Spawned;
            WeaponInfo weapon = equip.Weapon;
            bool charged = false;
            float chargePct = 0;
            if (weapon.Flags.TestFlag(WeaponFlags.CanCharge))
            {
                // todo: FPS stuff
                if (weapon.Flags.TestFlag(WeaponFlags.PartialCharge))
                {
                    if (equip.ChargeLevel >= weapon.MinCharge * 2) // todo: FPS stuff
                    {
                        charged = true;
                        // todo: FPS stuff
                        chargePct = (equip.ChargeLevel - weapon.MinCharge * 2) / (float)(weapon.FullCharge * 2 - weapon.MinCharge * 2);
                    }
                }
                else if (equip.ChargeLevel >= weapon.FullCharge * 2) // todo: FPS stuff
                {
                    charged = true;
                    chargePct = 1;
                }
            }
            float GetAmount(int unchargedAmt, int minChargeAmt, int fullChargeAmt)
            {
                return chargePct <= 0 ? unchargedAmt : minChargeAmt + ((fullChargeAmt - minChargeAmt) * chargePct);
            }
            NetTargetIdentity syncedHomingTarget = default;
            if (scene.Services.PlayerReplication.Active && charged && weapon.Beam == BeamType.VoltDriver
                && weapon.Afflictions[1].TestFlag(Affliction.Disrupt) && owner is PlayerEntity homingOwner)
            {
                // Consume at the attempt, not after the beam is allocated: a
                // no-ammo release must not leak its target into the next shot.
                syncedHomingTarget = homingOwner.ModConsumePendingHomingTarget();
            }
            int cost = (int)GetAmount(weapon.AmmoCost, weapon.MinChargeCost, weapon.ChargeCost);
            ulong phase = scene.FrameCount;
            bool sharedPhase = false;
            bool freshContinuousTick = true;
            if (weapon.Flags.TestFlag(WeaponFlags.Continuous) && owner is PlayerEntity firingPlayer)
            {
                if (parent == null && NetFireEvents.TryAcceptedContinuousPhase(firingPlayer, out ulong acceptedPhase,
                    out bool acceptedFresh))
                {
                    phase = acceptedPhase;
                    sharedPhase = true;
                    freshContinuousTick = acceptedFresh;
                }
                else if (scene.Services.IsReplica
                    && NetFireEvents.TryTiming(firingPlayer, out FireEvent replayFire)
                    && replayFire.Kind == FireEventKind.ContinuousTick)
                {
                    // The repeated FireEvent owns the phase of this exact pulse.
                    // A recovered carrier may contain a newer continuous tick, so
                    // replay must not re-read that newer packet after scheduling
                    // the older shot back onto its source frame.
                    phase = replayFire.ContinuousPhase;
                    sharedPhase = true;
                    freshContinuousTick = true;
                }
                else
                {
                    int slot = firingPlayer.SlotIndex;
                    var replication = scene.Services.PlayerReplication;
                    bool hasIntent = replication.TryGetIntent(slot, out var intent);
                    phase = scene.WeaponPhase.Resolve(slot, scene.FrameCount,
                        replication.Active && !firingPlayer.IsBot,
                        replication.LocalSlot >= 0 && slot == replication.LocalSlot,
                        replication.Frame, hasIntent, intent.Frame, replication.IntentAge(slot),
                        intent.HasContinuousFireTick ? intent.ContinuousFireTick : 0,
                        out sharedPhase, out freshContinuousTick,
                        receivedBeforeStep: !scene.Services.IsReplica && NetSession.Role == NetRole.Server);
                }
            }
            if (weapon.Flags.TestFlag(WeaponFlags.Continuous))
            {
                // todo?: figure out what the intent behind this actually is
                // game's cycle for Shock Coil (10): 0 0 0 1 0 0 1 0 0 1 0 0 1 0 0 0
                //    our cycle for Shock Coil (10): 0 0 0 0 0 0 1 0 0 0 0 0 1 0 0 0 0 0 1 0 0 0 0 0 1 0 0 0 0 0 0 0
                // game's cycle for green beam (15): 0 0 1 0 1 0 1 0 1 0 1 0 1 0 1 0 0 1 0 1 0 1 0 1 0 1 0 1 0 1 0 0
                //    our cycle for green beam (15): 0 0 0 0 1 0 0 0 1 0 0 0 1 0 0 0 1 0 0 0 1 0 0 0 1 0 0 0 1 0 0 0
                //                                   0 0 1 0 0 0 1 0 0 0 1 0 0 0 1 0 0 0 1 0 0 0 1 0 0 0 1 0 0 0 0 0
                cost = ContinuousWeaponPhase.Amount(cost, phase, damage: false);
                if (!freshContinuousTick) cost = 0;
            }
            int ammo = equip.Ammo;
            if (ammo >= 0 && cost > ammo)
            {
                return BeamResultFlags.NoSpawn;
            }
            equip.Ammo = ammo - cost;
            EffectEntry? muzzleEffect = null;
            if (!spawnFlags.TestFlag(BeamSpawnFlags.NoMuzzle))
            {
                byte effectId = weapon.MuzzleEffects[charged ? 1 : 0];
                if (effectId != 255)
                {
                    Debug.Assert(effectId >= 3);
                    Vector3 effUp = direction;
                    Vector3 effFacing = GetCrossVector(effUp);
                    Matrix4 transform = GetTransformMatrix(effFacing, effUp);
                    transform.Row3.Xyz = position;
                    // the game does this by spawning a CBeamEffect, but that's unncessary for muzzle effects
                    if (spawnFlags.TestFlag(BeamSpawnFlags.DestroyMuzzle))
                    {
                        muzzleEffect = scene.SpawnEffectGetEntry(effectId - 3, transform);
                    }
                    else
                    {
                        scene.SpawnEffect(effectId - 3, transform);
                    }
                }
            }
            uint inheritedTravel = parent?.ModClaimTravelFrames ?? 0;
            int projectiles = (int)GetAmount(weapon.Projectiles, weapon.MinChargeProjectiles, weapon.ChargedProjectiles);
            if (projectiles <= 0)
            {
                return result;
            }

            bool balancedNoxusFreezeProjectile = scene.GameState.Multiplayer
                && scene.GameState.BalancedMode && charged
                && owner is PlayerEntity noxusOwner && noxusOwner.Hunter == Hunter.Noxus
                && weapon.Beam == BeamType.Judicator
                && BalancedModeRules.IsAffinity(noxusOwner.Hunter, weapon.Beam);
            bool instantAoe = !balancedNoxusFreezeProjectile
                && ((charged && weapon.Flags.TestFlag(WeaponFlags.AoeCharged))
                    || (!charged && weapon.Flags.TestFlag(WeaponFlags.AoeUncharged)));

            BeamFlags flags = BeamFlags.None;
            // todo: FPS stuff
            float speed = GetAmount(weapon.UnchargedSpeed, weapon.MinChargeSpeed, weapon.ChargedSpeed) / 4096f / 2;
            float finalSpeed = GetAmount(weapon.UnchargedFinalSpeed, weapon.MinChargeFinalSpeed, weapon.ChargedFinalSpeed) / 4096f / 2;
            speed = ModBalancedProjectileSpeed(scene, owner, weapon.Beam, charged, speed);
            finalSpeed = ModBalancedProjectileSpeed(scene, owner, weapon.Beam, charged, finalSpeed);
            if (balancedNoxusFreezeProjectile)
            {
                // Match the regular Balanced Judicator's modernized travel
                // speed. The affinity trade is control, not an unavoidable cone.
                speed = 10240f / 4096f / 2f;
                finalSpeed = speed;
            }
            if (battlehammerCluster)
            {
                // Short hop, not a second full-range mortar shell.
                speed *= 0.65f;
                finalSpeed *= 0.65f;
            }
            float speedDecayTime = weapon.SpeedDecayTimes[charged ? 1 : 0] * (1 / 30f);
            ushort speedInterpolation = weapon.SpeedInterpolations[charged ? 1 : 0];
            float gravity = GetAmount(weapon.UnchargedGravity, weapon.MinChargeGravity, weapon.ChargedGravity) / 4096f;
            Vector3 acceleration = new Vector3(0, gravity, 0) / 2;
            float homing = GetAmount(weapon.UnchargedHoming, weapon.MinChargeHoming, weapon.ChargedHoming) / 4096f / 2;
            if (homing > 0)
            {
                flags |= BeamFlags.Homing;
            }
            if (charged || spawnFlags.TestFlag(BeamSpawnFlags.Charged))
            {
                flags |= BeamFlags.Charged;
            }
            if ((charged && weapon.Flags.TestFlag(WeaponFlags.RicochetCharged))
                || (!charged && weapon.Flags.TestFlag(WeaponFlags.RicochetUncharged)))
            {
                flags |= BeamFlags.Ricochet;
            }
            if ((charged && weapon.Flags.TestFlag(WeaponFlags.SelfDamageCharged))
                || (!charged && weapon.Flags.TestFlag(WeaponFlags.SelfDamageUncharged)))
            {
                flags |= BeamFlags.SelfDamage;
            }
            if ((charged && weapon.Flags.TestFlag(WeaponFlags.ForceEffectCharged))
                || (!charged && weapon.Flags.TestFlag(WeaponFlags.ForceEffectUncharged)))
            {
                flags |= BeamFlags.ForceEffect;
            }
            if ((charged && weapon.Flags.TestFlag(WeaponFlags.DestroyableCharged))
                || (!charged && weapon.Flags.TestFlag(WeaponFlags.DestroyableUncharged)))
            {
                flags |= BeamFlags.Destroyable;
            }
            if ((charged && weapon.Flags.TestFlag(WeaponFlags.LifeDrainCharged))
                || (!charged && weapon.Flags.TestFlag(WeaponFlags.LifeDrainUncharged)))
            {
                flags |= BeamFlags.LifeDrain;
            }
            byte drawFuncId = equip.DrawFuncIds[charged ? 1 : 0];
            if (drawFuncId == 255)
            {
                drawFuncId = weapon.DrawFuncIds[charged ? 1 : 0];
            }
            ushort colorValue = weapon.Colors[charged ? 1 : 0];
            float red = ((colorValue >> 0) & 0x1F) / 31f;
            float green = ((colorValue >> 5) & 0x1F) / 31f;
            float blue = ((colorValue >> 10) & 0x1F) / 31f;
            var color = new Vector3(red, green, blue);
            byte colEffect = weapon.CollisionEffects[charged ? 1 : 0];
            byte dmgDirType = equip.DmgDirTypes[charged ? 1 : 0];
            if (dmgDirType == 255)
            {
                dmgDirType = weapon.DmgDirTypes[charged ? 1 : 0];
            }
            float dmgDirMag = GetAmount(weapon.UnchargedDmgDirMag, weapon.MinChargeDmgDirMag, weapon.ChargedDmgDirMag) / 4096f;
            int damage = (int)GetAmount(equip.UnchargedDamage, equip.MinChargeDamage, equip.ChargedDamage);
            int hsDamage = (int)GetAmount(equip.HeadshotDamage, equip.MinChargeHeadshotDamage, equip.ChargedHeadshotDamage);
            int splashDmg = (int)GetAmount(equip.SplashDamage, equip.MinChargeSplashDamage, equip.ChargedSplashDamage);
            float splashRadius = GetAmount(weapon.UnchargedSplashRadius, weapon.MinChargeSplashRadius, weapon.ChargedSplashRadius) / 4096f;
            byte splashDmgType = weapon.SplashDamageTypes[charged ? 1 : 0];
            if (spawnFlags.TestFlag(BeamSpawnFlags.DoubleDamage))
            {
                damage *= 2;
                hsDamage *= 2;
                splashDmg *= 2;
            }
            else if (spawnFlags.TestFlag(BeamSpawnFlags.PrimeHunter))
            {
                damage = 150 * damage / 100;
                hsDamage = 150 * hsDamage / 100;
                splashDmg = 150 * splashDmg / 100;
            }
            bool scopedImperialist = equip.Zoomed;
            if (weapon.Beam == BeamType.Imperialist && owner is PlayerEntity playerOwner
                && NetFireEvents.TryScopedAtFire(playerOwner, out bool scopedAtFire))
            {
                // A recovered fire event can arrive in a newer carrier after the
                // owner has already unscoped. Damage follows the state authored
                // with the shot; the puppet's current zoom/presentation does not.
                scopedImperialist = scopedAtFire;
            }
            if (Features.HalfDamageUnscoped && weapon.Beam == BeamType.Imperialist && !scopedImperialist)
            {
                // The optional unscoped penalty applies to ordinary and head
                // damage. Shot-time scope, rather than the newer carrier or
                // camera FOV blend, controls both native values.
                damage /= 2;
                hsDamage /= 2;
            }
            // todo?: it's kind of lame that double damage doesn't affect Shock Coil
            if (weapon.Flags.TestFlag(WeaponFlags.Continuous))
            {
                // todo: this is the same as the ammo calculation but with ge instead of gt
                // note: previously the frame count partiy check was part of the condition below, but that assumed the base value
                // was zero after the division by 32, which is true for Shock Coil but not e.g. platform green energy beams,
                // so we need those to hit every other frame to match the DPS from the game
                damage = ContinuousWeaponPhase.Amount(damage, phase, damage: true);
                if (!freshContinuousTick) damage = 0;
            }
            if (Cheats.QuadrupleDamage)
            {
                damage *= 4;
                hsDamage *= 4;
                splashDmg *= 4;
            }
            // Balanced Mode Battlehammer rework. Outside Balanced Mode the
            // metadata table is used untouched, restoring stock MPH behavior.
            if (scene.GameState.Multiplayer && scene.GameState.BalancedMode
                && weapon.Beam == BeamType.Battlehammer)
            {
                bool weavelAffinity = owner is PlayerEntity bhOwner
                    && bhOwner.Hunter == Hunter.Weavel
                    && BalancedModeRules.IsAffinity(bhOwner.Hunter, BeamType.Battlehammer);
                // Weavel equips the stock Battlehammer metadata in Balanced
                // Mode, so both affinity and non-affinity shells share the same
                // damage/ammo/cadence budget. Utility lives in cluster geometry.
                float powerScale = damage / 12f;
                damage = (int)MathF.Round(14f * powerScale);
                hsDamage = damage;
                splashDmg = (int)MathF.Round(6f * powerScale);
                splashRadius = 1.75f;
                splashDmgType = 0;
                dmgDirType = 2;
                dmgDirMag = weavelAffinity ? 0.24f : 0.20f;

                if (battlehammerCluster)
                {
                    float scale = Math.Max(0, battlehammerClusterScale);
                    // Hit tuning below converts 3 direct to 5 and 4 splash to 3.
                    damage = hsDamage = (int)MathF.Round(3f * scale);
                    splashDmg = (int)MathF.Round(4f * scale);
                    splashRadius = 1.15f * (weavelAffinity
                        ? BalancedModeRules.WeavelClusterRadiusMultiplier : 1f);
                    splashDmgType = 0;
                    dmgDirType = 2;
                    dmgDirMag = 0.18f * (weavelAffinity
                        ? BalancedModeRules.WeavelClusterKnockbackMultiplier : 1f);
                }
            }

            // Accuracy should beat floor spam. Apply this after charge,
            // affinity and powerup damage have already been authored so those
            // multipliers keep their existing semantics.
            ModBalancedHitTuning(scene, owner, weapon.Beam, battlehammerCluster,
                ref damage, ref hsDamage, ref splashDmg, ref splashDmgType);
            ushort damageInterpolation = weapon.DamageInterpolations[charged ? 1 : 0];
            float maxDist = GetAmount(weapon.UnchargedDistance, weapon.MinChargeDistance, weapon.ChargedDistance) / 4096f;
            Affliction afflictions = weapon.Afflictions[charged ? 1 : 0];
            float cylinderRadius = GetAmount(weapon.UnchargedCylRadius, weapon.MinChargeCylRadius, weapon.ChargedCylRadius) / 4096f;
            float lifespan = GetAmount(weapon.UnchargedLifespan, weapon.MinChargeLifespan, weapon.ChargedLifespan) * (1 / 30f);
            if (balancedNoxusFreezeProjectile)
            {
                maxDist = 12f;
                lifespan = 1f;
                splashDmg = 0;
                splashRadius = 0;
            }
            if (battlehammerCluster)
            {
                lifespan = 0.40f;
            }
            if (weapon.Flags.TestFlag(WeaponFlags.Continuous))
            {
                flags |= BeamFlags.Continuous;
            }
            if (weapon.Flags.TestFlag(WeaponFlags.SurfaceCollision))
            {
                flags |= BeamFlags.SurfaceCollision;
            }
            uint radiusIndex = (((uint)weapon.Flags) >> (charged ? 26 : 24)) & 3; // bits 24/25 or 26/27
            flags = (BeamFlags)((ushort)flags | (radiusIndex << 9)); // bits 9/10
            float ricochetLossH = GetAmount(weapon.UnchargedRicochetLossH, weapon.MinChargeRicochetLossH, weapon.ChargedRicochetLossH) / 4096f;
            float ricochetLossV = GetAmount(weapon.UnchargedRicochetLossV, weapon.MinChargeRicochetLossV, weapon.ChargedRicochetLossV) / 4096f;
            int maxSpread = (int)GetAmount(weapon.UnchargedSpread, weapon.MinChargeSpread, weapon.ChargedSpread);
            WeaponInfo? ricochetWeapon = charged ? weapon.ChargedRicochetWeapon : weapon.UnchargedRicochetWeapon;
            Vector3 dirVec = direction;
            Vector3 rightVec;
            if (dirVec.X != 0 || dirVec.Z != 0)
            {
                rightVec = new Vector3(dirVec.Z, 0, -dirVec.X).Normalized();
            }
            else
            {
                rightVec = Vector3.UnitX;
            }
            Vector3 upVec = Vector3.Cross(dirVec, rightVec).Normalized();
            Vector3 velocity = Vector3.Zero;
            if (maxSpread <= 0)
            {
                velocity = direction * speed;
            }
            for (int i = 0; i < projectiles; i++)
            {
                BeamProjectileEntity beam = ChooseBeamSlot(equip, owner);
                if (beam.Lifespan > 0 && !beam.Flags.TestFlag(BeamFlags.Collided))
                {
                    CollisionResult colRes = default;
                    colRes.Position = beam.Position;
                    colRes.Plane = new Vector4(-beam.Direction);
                    beam.RicochetWeapon = null;
                    beam.OnCollision(colRes, colWith: null, enhancedImpact: false);
                }
                beam.Destroy();
                scene.RemoveEntity(beam);
                beam._models.Clear();
                if (!charged)
                {
                    equip.SmokeLevel += weapon.SmokeShotAmount;
                    if (equip.SmokeLevel > weapon.SmokeStart)
                    {
                        equip.SmokeLevel = weapon.SmokeStart;
                    }
                }
                beam.Owner = owner;
                beam.TrainingShotId = parent?.TrainingShotId ?? scene.AimTrainer?.CurrentShotId ?? 0;
                beam.EnhancedDirectHit = false;
                beam.EnhancedBounceCount = parent?.EnhancedBounceCount ?? 0;
                beam.EnhancedMicroSeeker = enhancedMicro;
                beam.EnhancedFullCharge = chargePct >= 1;
                beam.EnhancedSiegeRound = false;
                beam.BattlehammerClusterChild = battlehammerCluster;
                beam.ModContinuousPhase = phase;
                beam.ModHasSharedContinuousPhase = sharedPhase;
                beam.ModLaunchFrame = beam.ModShotId = 0;
                beam.ModLaunchMatch = 0;
                beam.ModLaunchAuthority = 0;
                beam.ModLaunchGeneration = beam.ModLaunchLife = 0;
                beam.ModLaunchKey = default;
                if (!scene.Services.IsReplica)
                {
                    NetPlayerLifecycle.StampProjectile(beam, parent);
                }
                else if (parent != null && parent.ModShotId != 0)
                {
                    beam.ModLaunchFrame = parent.ModLaunchFrame;
                    beam.ModShotId = parent.ModShotId;
                    beam.ModLaunchMatch = parent.ModLaunchMatch;
                    beam.ModLaunchAuthority = parent.ModLaunchAuthority;
                    beam.ModLaunchGeneration = parent.ModLaunchGeneration;
                    beam.ModLaunchLife = parent.ModLaunchLife;
                    beam.ModLaunchKey = parent.ModLaunchKey;
                }
                else
                {
                    PlayerEntity? replayShooter = owner as PlayerEntity
                        ?? (owner as HalfturretEntity)?.Owner;
                    if (replayShooter != null && scene.ReplayPoses?.TryActiveShotIdentity(
                        replayShooter, owner is HalfturretEntity,
                        out ShotKey replayKey, out uint replaySourceFrame) == true)
                    {
                        beam.ModLaunchFrame = replaySourceFrame;
                        beam.ModShotId = replayKey.ShotId;
                        beam.ModLaunchMatch = replayKey.MatchId;
                        beam.ModLaunchAuthority = replayKey.AuthorityEpoch;
                        beam.ModLaunchGeneration = replayKey.Generation;
                        beam.ModLaunchLife = replayKey.LifeId;
                        beam.ModLaunchKey = replayKey;
                    }
                }
                beam.Beam = weapon.Beam;
                beam.BeamKind = weapon.BeamKind;
                if (!scene.Services.IsReplica && NetLog.Enabled) NetShotDiagnostics.Trace("spawn", beam.ModLaunchKey, beam.Beam);
                beam.Flags = flags;
                if (!scene.Services.IsReplica && NetSession.IsServer && parent == null && beam.ModShotId != 0)
                    NetImpactDiagnostics.NativeEmission((byte)beam.Beam, charged);
                beam.NodeRef = nodeRef;
                beam.Age = 0;
                beam.ModClaimTravelFrames = inheritedTravel;
                uint component = 0;
                if (!scene.Services.IsReplica && NetSession.IsAuthority)
                {
                    component = unchecked((uint)System.Threading.Interlocked.Increment(ref _nextWitnessComponent));
                    if (component == 0) component = unchecked((uint)System.Threading.Interlocked.Increment(ref _nextWitnessComponent));
                }
                beam.ModWitnessComponent = component;
                beam.ModPresentationComponent = ImpactPresentationRules.Component(parent?.ModPresentationComponent ?? 0, i, parent != null);
                beam.InitialSpeed = beam.Speed = speed;
                beam.FinalSpeed = finalSpeed;
                beam.SpeedDecayTime = speedDecayTime;
                beam.SpeedInterpolation = speedInterpolation;
                beam.Homing = homing;
                beam.DrawFuncId = drawFuncId;
                beam.Color = color;
                // btodo: load all collision effects, splat effects, etc. in room setup
                beam.CollisionEffect = colEffect;
                beam.DamageDirType = dmgDirType;
                beam.SplashDamageType = splashDmgType;
                beam.DamageDirMag = dmgDirMag;
                // Balanced range damage is measured from the original firing
                // point. Magmaul can ricochet, so carrying its parent's origin
                // prevents a wall bounce from resetting a long-range shot into
                // the close-range damage bonus.
                beam.SpawnPosition = scene.GameState.BalancedMode && parent != null
                    && parent.Beam == weapon.Beam && BalancedModeRules.HasRangeDamageCurve(weapon.Beam)
                    ? parent.SpawnPosition : position;
                beam.BackPosition = beam.Position = position;
                for (int j = 0; j < 10; j++)
                {
                    beam.PastPositions[j] = position;
                }
                beam.Direction = dirVec;
                beam.Right = rightVec;
                beam.Up = upVec;
                beam.Damage = damage;
                scene.AimTrainer?.NoteProjectile(beam);
                beam.HeadshotDamage = hsDamage;
                beam.SplashDamage = splashDmg;
                beam.SplashRadius = splashRadius;
                beam.DamageInterpolation = damageInterpolation;
                beam.MaxDistance = maxDist;
                beam.Afflictions = afflictions;
                beam.CylinderRadius = cylinderRadius;
                beam.Lifespan = lifespan;
                beam.RicochetLossH = ricochetLossH;
                beam.RicochetLossV = ricochetLossV;
                beam.RicochetWeapon = ricochetWeapon;
                beam.Equip = equip;
                if (owner.Type == EntityType.Player)
                {
                    var ownerPlayer = (PlayerEntity)owner;
                    scene.GameState.BeamDamageMax[ownerPlayer.SlotIndex] += damage;
                }
                if (instantAoe)
                {
                    beam.SpawnIceWave(weapon, chargePct);
                    // we don't actually "spawn" the beam projectile
                    beam.Velocity = beam.Acceleration = Vector3.Zero;
                    beam.Flags = BeamFlags.Collided;
                    beam.Lifespan = 0;
                    beam.Destroy();
                    return result;
                }
                if (maxSpread > 0)
                {
                    float angle1 = MathHelper.DegreesToRadians(scene.Random.GetRandomInt2((uint)maxSpread) / 4096f);
                    float angle2 = MathHelper.DegreesToRadians(scene.Random.GetRandomInt2(0x168000) / 4096f);
                    float sin1 = MathF.Sin(angle1);
                    float cos1 = MathF.Cos(angle1);
                    float sin2 = MathF.Sin(angle2);
                    float cos2 = MathF.Cos(angle2);
                    velocity.X = direction.X * cos1 + (beam.Up.X * cos2 + beam.Right.X * sin2) * sin1;
                    velocity.Y = direction.Y * cos1 + (beam.Up.Y * cos2 + beam.Right.Y * sin2) * sin1;
                    velocity.Z = direction.Z * cos1 + (beam.Up.Z * cos2 + beam.Right.Z * sin2) * sin1;
                    velocity *= beam.Speed;
                }
                beam.Velocity = velocity;
                beam.Acceleration = acceleration;
                // A beam comes off a free list and keeps whatever transform the
                // last one left on it until the draw pass computes a new one.
                // Only draw functions 3 and 17 set one here, so every other
                // weapon draws its first frame at its predecessor's position --
                // and across a death that predecessor belongs to the previous
                // life, which is the "phantom shots from where I died".
                // Harmless for 17, which overwrites this with the same thing.
                Matrix4 spawnTransform = GetTransformMatrix(beam.Direction, beam.Up);
                spawnTransform.Row3.Xyz = position;
                beam.Transform = spawnTransform;
                // The beam object is pooled. Re-base render history after its
                // new spawn transform is complete so a nearby reuse cannot
                // blend from the previous projectile's final frame.
                beam.ModResetDrawState();
                if (beam.DrawFuncId == 3)
                {
                    beam.Flags |= BeamFlags.HasModel;
                    beam._models.Add(scene.GetModelInstance("iceShard"));
                }
                else if (beam.DrawFuncId == 17)
                {
                    beam.Flags |= BeamFlags.HasModel;
                    ModelInstance model = scene.GetModelInstance("energyBeam");
                    model.SetAnimation(0);
                    beam._models.Add(model);
                    Matrix4 transform = GetTransformMatrix(beam.Direction, beam.Up);
                    transform.Row3.Xyz = position;
                    beam.Transform = transform;
                    model.AnimInfo.Frame[0] = (int)scene.FrameCount / 2 % model.AnimInfo.FrameCount[0];
                }
                else
                {
                    int effectId = Metadata.BeamDrawEffects[beam.DrawFuncId];
                    if (effectId != 0)
                    {
                        Vector3 effUp = beam.Direction;
                        Vector3 effFacing = GetCrossVector(effUp);
                        Matrix4 transform = GetTransformMatrix(effFacing, effUp);
                        transform.Row3.Xyz = beam.Position;
                        beam.Effect = scene.SpawnEffectGetEntry(effectId, transform);
                        beam.Effect?.SetElementExtension(true);
                    }
                }
                if (spawnFlags.TestFlag(BeamSpawnFlags.DestroyMuzzle))
                {
                    beam.MuzzleEffect = muzzleEffect;
                    beam.Flags |= BeamFlags.DestroyMuzzle;
                }
                Debug.Assert(beam.Target == null);
                if (beam.Flags.TestFlag(BeamFlags.Homing))
                {
                    bool syncedTargetHandled = ApplySyncedPlayerHomingTarget(
                        beam, equip, scene, syncedHomingTarget);
                    if (!syncedTargetHandled && CheckHomingTargets(beam, equip, scene))
                    {
                        result |= BeamResultFlags.Homing;
                    }
                    if (!scene.Services.IsReplica && beam.Beam == BeamType.ShockCoil && owner.Type == EntityType.Player)
                    {
                        var ownerPlayer = (PlayerEntity)owner;
                        if ((scene.GameState.Multiplayer || !ownerPlayer.IsBot) && ownerPlayer.ShockCoilTarget == beam.Target
                            && phase % 2 == 0) // todo: FPS stuff
                        {
                            // todo: FPS stuff
                            ushort timer = ownerPlayer.ShockCoilTimer;
                            if (timer >= 120 * 2)
                            {
                                beam.Damage += 4;
                                beam.SplashDamage += 4;
                                beam.HeadshotDamage += 4;
                            }
                            else
                            {
                                beam.Damage += timer / (30 * 2);
                                beam.SplashDamage += timer / (30 * 2);
                                beam.HeadshotDamage += timer / (30 * 2);
                            }
                        }
                    }
                }
                // A continuous homing beam with no target does nothing at all,
                // and says nothing about it: no hit, no miss, no entry in any
                // count this file already keeps. Recorded here, at the one
                // moment the answer is known.
                if (!scene.Services.IsReplica && beam.Beam == BeamType.ShockCoil && owner.Type == EntityType.Player)
                {
                    NetDamage.ShockCoilSpawned++;
                    if (beam.Target != null)
                    {
                        NetDamage.ShockCoilAcquired++;
                    }
                }
                if (!scene.Services.IsReplica && NetSession.Active && weapon.Flags.TestFlag(WeaponFlags.Continuous))
                    NetShotDiagnostics.Continuous(beam, cost);
                beam._soundSource.Update(beam.Position, rangeIndex: 0);
                scene.AddEntity(beam);
                Mods.EnhancedHunters.EnhancedHunterProjectiles.Spawned(beam, parent);
            }
            if (turretOwner != null) NetFireEvents.Commit(turretOwner);
            return result;
        }

        private static readonly IReadOnlyList<EntityType> _homingTargetTypes = new EntityType[5]
        {
            EntityType.Player,
            EntityType.Halfturret,
            EntityType.EnemyInstance,
            EntityType.Door,
            EntityType.Platform
        };

        internal static EntityBase? ModFindNonContinuousHomingTarget(EntityBase owner, EquipInfo equip,
            Vector3 position, Vector3 direction, Scene scene)
        {
            if (direction.LengthSquared < 0.000001f)
            {
                return null;
            }
            Vector3 aim = direction.Normalized();
            WeaponInfo weapon = equip.Weapon;
            float tolerance = Fixed.ToFloat(equip.HomingTolerance);
            float curDiv = tolerance;
            float range = Fixed.ToFloat(weapon.HomingRange);
            EntityBase? target = null;
            for (int i = 0; i < _homingTargetTypes.Count; i++)
            {
                EntityType type = _homingTargetTypes[i];
                if (type == EntityType.EnemyInstance
                    && (owner.Type == EntityType.EnemyInstance || owner.Type == EntityType.Platform))
                {
                    continue;
                }
                foreach (EntityBase entity in scene.Entities)
                {
                    if (entity.Type != type || entity == owner || !entity.GetTargetable())
                    {
                        continue;
                    }
                    bool tryTarget = false;
                    if (type == EntityType.Player)
                    {
                        var player = (PlayerEntity)entity;
                        tryTarget = owner.Type != EntityType.Player
                            || !TeamRules.AreAllies(player.TeamIndex, ((PlayerEntity)owner).TeamIndex);
                    }
                    else if (type == EntityType.Halfturret)
                    {
                        var halfturret = (HalfturretEntity)entity;
                        tryTarget = owner.Type != EntityType.Player
                            || halfturret.Owner != owner
                            && !TeamRules.AreAllies(halfturret.Owner.TeamIndex, ((PlayerEntity)owner).TeamIndex);
                    }
                    else if (type == EntityType.EnemyInstance)
                    {
                        EnemyFlags flags = ((EnemyInstanceEntity)entity).Flags;
                        tryTarget = flags.TestFlag(EnemyFlags.CollideBeam)
                            && !flags.TestFlag(EnemyFlags.NoHomingNc);
                    }
                    else if (type == EntityType.Platform)
                    {
                        tryTarget = ((PlatformEntity)entity).Flags.TestFlag(PlatformFlags.BeamTarget);
                    }
                    else
                    {
                        tryTarget = true;
                    }
                    if (!tryTarget)
                    {
                        continue;
                    }
                    entity.GetPosition(out Vector3 targetPosition);
                    Vector3 between = targetPosition - position;
                    float distSqr = between.LengthSquared;
                    if (distSqr <= 0 || distSqr > range * range)
                    {
                        continue;
                    }
                    float div = Vector3.Dot(between, aim) / MathF.Sqrt(distSqr);
                    if (div >= curDiv)
                    {
                        curDiv = div;
                        target = entity;
                    }
                }
            }
            return target;
        }

        private static bool ApplySyncedPlayerHomingTarget(BeamProjectileEntity beam, EquipInfo equip,
            Scene scene, NetTargetIdentity encodedTarget)
        {
            if (!encodedTarget.IsSupplied)
            {
                return false;
            }

            // A valid marker with no slot means the owner saw no target.
            int slot = encodedTarget.Slot;
            if (slot < 0)
            {
                beam.Target = null;
                return true;
            }

            // Do not trust a client-supplied slot by itself. Re-run the normal
            // selector in the authority's rewound world and only accept the
            // owner's identity when that world independently considers the
            // same player the best eligible target. A disagreement suppresses
            // homing instead of silently bending toward a different player.
            EntityBase? candidate = ModFindNonContinuousHomingTarget(
                beam.Owner!, equip, beam.Position, beam.Velocity, scene);
            beam.Target = candidate is PlayerEntity player && player.SlotIndex == slot
                && NetPlayerLifecycle.Matches(slot, encodedTarget.Generation, encodedTarget.LifeId)
                ? candidate
                : null;
            return true;
        }

        private static bool CheckHomingTargets(BeamProjectileEntity beam, EquipInfo equip, Scene scene)
        {
            if (!beam.Flags.TestFlag(BeamFlags.Continuous))
            {
                beam.Target = ModFindNonContinuousHomingTarget(
                    beam.Owner!, equip, beam.Position, beam.Velocity, scene);
                return false;
            }

            var trace = new NetContinuousTargetDiagnostics.Evaluation();
            bool synchronized = NetContinuousTargeting.Resolve(beam, equip, scene, ref trace);
            if (synchronized && beam.Target is PlayerEntity)
            {
                NetContinuousTargeting.Record(beam, equip, scene, true, ref trace);
                return true;
            }
            bool result = false;
            WeaponInfo weapon = equip.Weapon;
            Debug.Assert(beam.Owner != null);
            float tolerance = Fixed.ToFloat(equip.HomingTolerance);
            float curDiv = tolerance;
            for (int i = 0; i < _homingTargetTypes.Count; i++)
            {
                EntityType type = _homingTargetTypes[i];
                if (synchronized && type == EntityType.Player) continue;
                if (type == EntityType.EnemyInstance
                    && (beam.Owner.Type == EntityType.EnemyInstance || beam.Owner.Type == EntityType.Platform))
                {
                    continue;
                }
                foreach (EntityBase entity in scene.Entities)
                {
                    if (entity.Type != type || entity == beam.Owner || !entity.GetTargetable())
                    {
                        continue;
                    }
                    // One predicate for local player selection and historical proposal validation.
                    if (type == EntityType.Player && beam.Owner is PlayerEntity && beam.Beam == BeamType.ShockCoil)
                    {
                        var player = (PlayerEntity)entity;
                        var candidate = new NetContinuousTargetDiagnostics.Evaluation();
                        if (NetContinuousTargeting.EvaluatePlayer(beam, equip, player, null,
                            player.IsMorphing, player.TeamIndex, ref candidate) == ContinuousTargetRejection.None
                            && (candidate.Dot > curDiv || candidate.Dot == curDiv
                                && (beam.Target is not PlayerEntity previous || player.SlotIndex < previous.SlotIndex)))
                        {
                            curDiv = candidate.Dot; beam.Target = player; result = true;
                        }
                        continue;
                    }
                    bool tryTarget = false;
                    if (type == EntityType.Player)
                    {
                        var player = (PlayerEntity)entity;
                        if (beam.Owner.Type != EntityType.Player)
                        {
                            tryTarget = true;
                        }
                        else
                        {
                            var ownerPlayer = (PlayerEntity)beam.Owner;
                            tryTarget = !TeamRules.AreAllies(player.TeamIndex, ownerPlayer.TeamIndex);
                        }
                    }
                    else if (type == EntityType.Halfturret)
                    {
                        var halfturret = (HalfturretEntity)entity;
                        if (beam.Owner.Type != EntityType.Player)
                        {
                            tryTarget = true;
                        }
                        else
                        {
                            var ownerPlayer = (PlayerEntity)beam.Owner;
                            tryTarget = halfturret.Owner != ownerPlayer
                                && !TeamRules.AreAllies(halfturret.Owner.TeamIndex, ownerPlayer.TeamIndex);
                        }
                    }
                    else if (type == EntityType.EnemyInstance)
                    {
                        var enemy = (EnemyInstanceEntity)entity;
                        EnemyFlags flags = enemy.Flags;
                        if (flags.TestFlag(EnemyFlags.CollideBeam)
                            && (!flags.TestFlag(EnemyFlags.NoHomingNc) || beam.Flags.TestFlag(BeamFlags.Continuous))
                            && (!flags.TestFlag(EnemyFlags.NoHomingCo) || !beam.Flags.TestFlag(BeamFlags.Continuous)))
                        {
                            tryTarget = true;
                        }
                    }
                    else if (type == EntityType.Platform)
                    {
                        var platform = (PlatformEntity)entity;
                        tryTarget = platform.Flags.TestFlag(PlatformFlags.BeamTarget);
                    }
                    else // if (type == EntityType.Door)
                    {
                        tryTarget = true;
                    }
                    if (tryTarget)
                    {
                        entity.GetPosition(out Vector3 position);
                        Vector3 between = position - beam.Position;
                        float distSqr = Vector3.Dot(between, between);
                        float range = Fixed.ToFloat(weapon.HomingRange);
                        if ((weapon.Flags.TestFlag(WeaponFlags.Continuous) && beam.BeamKind != BeamType.Platform
                            || distSqr <= range * range) && distSqr > 0)
                        {
                            float dist = MathF.Sqrt(distSqr);
                            Debug.Assert(beam.Velocity != Vector3.Zero);
                            float dot = Vector3.Dot(between, beam.Velocity.Normalized());
                            float div1 = dot / dist;
                            if (div1 >= curDiv)
                            {
                                if (weapon.Flags.TestFlag(WeaponFlags.Continuous))
                                {
                                    bool canTarget = false;
                                    if (type == EntityType.Player)
                                    {
                                        var player = (PlayerEntity)entity;
                                        if (!player.ModCollisionIsAltForm && !player.IsMorphing || div1 >= Fixed.ToFloat(4006))
                                        {
                                            canTarget = true;
                                        }
                                    }
                                    else
                                    {
                                        canTarget = true;
                                    }
                                    if (canTarget)
                                    {
                                        float div2 = Math.Min(dist / range, 1);
                                        if (div1 >= tolerance + div2 * (Fixed.ToFloat(4094) - tolerance))
                                        {
                                            curDiv = div1;
                                            beam.Target = entity;
                                            result = true;
                                        }
                                    }
                                }
                                else
                                {
                                    curDiv = div1;
                                    beam.Target = entity;
                                }
                            }
                        }
                    }
                }
            }
            NetContinuousTargeting.Record(beam, equip, scene, synchronized, ref trace);
            return result;
        }

        private void SpawnIceWave(WeaponInfo weapon, float chargePct)
        {
            float angle = chargePct <= 0
                ? weapon.UnchargedSpread
                : weapon.MinChargeSpread + ((weapon.ChargedSpread - weapon.MinChargeSpread) * chargePct);
            angle /= 4096f;
            Debug.Assert(angle == 60);
            NetAttackPaths.IceWave(this, angle);
            CheckIceWaveCollision(angle);
            Mods.EnhancedHunters.EnhancedHunterProjectiles.IceWaveFloor(this);
            Vector3 up = Direction;
            Vector3 facing;
            if (up.X != 0 || up.Z != 0)
            {
                var temp = Vector3.Cross(Vector3.UnitY, up);
                facing = Vector3.Cross(up, temp).Normalized();
            }
            else
            {
                var temp = Vector3.Cross(Vector3.UnitX, up);
                facing = Vector3.Cross(up, temp).Normalized();
            }
            Matrix4 transform = Matrix4.CreateScale(MaxDistance) * GetTransformMatrix(facing, up);
            transform.Row3.Xyz = Position;
            var ent = BeamEffectEntity.Create(new BeamEffectEntityData(type: 0, noSplat: false, transform), _scene);
            if (ent != null)
            {
                _scene.AddEntity(ent);
            }
        }

        // todo: visualize (also shadow freeze bug)
        private void CheckIceWaveCollision(float angle)
        {
            float angleCos = MathF.Cos(MathHelper.DegreesToRadians(angle));
            foreach (PlayerEntity player in _scene.GetPlayerEntities())
            {
                if (player == Owner || player.Health == 0)
                {
                    continue;
                }
                CheckIceWaveCollision(player, player.Position, angleCos, halfturret: false);
                if (NetUnlagged.TryCollisionHalfturret(player, out Vector3 turretPosition))
                {
                    CheckIceWaveCollision(player, turretPosition, angleCos, halfturret: true);
                }
            }
        }

        private void CheckIceWaveCollision(PlayerEntity player, Vector3 position, float angleCos, bool halfturret)
        {
            // bug: the beam's up vector is factored out in order to do a lateral distance check (where "lateral" is relative to
            // the beam's orientation), but that same stripped vector is used for the angle check below. the result is that instead
            // of checking in a 60 degree cone, a 60 degree wedge of a cylinder with infinite height is checked (again, where
            // "height" is rleative to the beam) -- this results in the shadow freeze glitch
            // fix: use the normalized between vector with all its components in the dot product check
            //
            // That fix is what GameState.ShadowFreeze switches on. Off, the
            // full vector goes into both checks and the wave freezes what is
            // in front of it; on -- the default, and the cartridge -- the
            // flattened one does, and it freezes anybody at any height who
            // happens to be within 60 degrees on the map. A rule rather than a
            // preference, and held by the server, because the machine
            // resolving the shot is the one that decides who it hit.
            if (ModIceWaveContains(Position, Direction, Up, MaxDistance, position, angleCos,
                _scene.GameState.ShadowFreeze))
            {
                Vector3 dir = GetDamageDirection(Position, player.Position);
                DamageFlags flags = DamageFlags.NoDmgInvuln;
                if (halfturret)
                {
                    flags |= DamageFlags.Halfturret;
                }
                EnhancedDirectHit = true;
                try { TakePlayerDamageAt(player, (int)Damage, flags, dir, player.Position); }
                finally { EnhancedDirectHit = false; }
            }
        }

        /// <summary>
        /// The cartridge's charged affinity-Judicator ice-wave volume, exposed for
        /// controller feedback and regression checks. It is intentionally the same
        /// math used by <see cref="CheckIceWaveCollision(PlayerEntity, Vector3, float, bool)"/>.
        /// </summary>
        internal static bool ModShadowFreezeWouldHit(Vector3 origin, Vector3 direction,
            float maxDistance, Vector3 target)
        {
            if (!Single.IsFinite(origin.X) || !Single.IsFinite(origin.Y) || !Single.IsFinite(origin.Z)
                || !Single.IsFinite(direction.X) || !Single.IsFinite(direction.Y)
                || !Single.IsFinite(direction.Z) || !Single.IsFinite(target.X)
                || !Single.IsFinite(target.Y) || !Single.IsFinite(target.Z)
                || !Single.IsFinite(maxDistance) || maxDistance <= 0
                || direction.LengthSquared < .000001f)
            {
                return false;
            }

            direction = direction.Normalized();
            Vector3 right;
            if (direction.X != 0 || direction.Z != 0)
            {
                right = new Vector3(direction.Z, 0, -direction.X).Normalized();
            }
            else
            {
                right = Vector3.UnitX;
            }
            Vector3 up = Vector3.Cross(direction, right).Normalized();
            float angleCos = MathF.Cos(MathHelper.DegreesToRadians(60));
            return ModIceWaveContains(origin, direction, up, maxDistance, target,
                angleCos, shadowFreeze: true);
        }

        internal static bool ModIceWaveContains(Vector3 origin, Vector3 direction, Vector3 up,
            float maxDistance, Vector3 target, float angleCos, bool shadowFreeze)
        {
            Vector3 full = target - origin;
            Vector3 between = full;
            float dot = Vector3.Dot(between, up);
            between += up * -dot;
            float mag = between.Length;

            // With the compatibility rule off, both halves of the test are
            // three-dimensional. With it on, the beam-local Up component is
            // stripped from distance and angle, preserving the cartridge bug.
            float reach = shadowFreeze ? mag : full.Length;
            if (!(reach < maxDistance && reach > 0))
            {
                return false;
            }
            Vector3 toward = shadowFreeze ? between / mag : full / reach;
            return Vector3.Dot(toward, direction) > angleCos;
        }

        private Vector3 GetDamageDirection(Vector3 beamPos, Vector3 targetPos)
            => ModDamageDirection(DamageDirType, DamageDirMag, Velocity, beamPos, targetPos);
        internal static Vector3 ModDamageDirection(byte type, float magnitude, Vector3 velocity, Vector3 beamPos, Vector3 targetPos)
        {
            if (type == 1)
            {
                // multiply velocity (or unit Y if not moving) by magnitude -- unused?
                Vector3 direction = Vector3.UnitY;
                if (velocity != Vector3.Zero)
                {
                    direction = velocity.Normalized();
                }
                return direction * magnitude;
            }
            if (type == 2)
            {
                // normalize vector between, halve Y, minimum 0.03 Y (or 0.03 Y if not moving), multiply by magnitude
                Vector3 direction = targetPos - beamPos;
                if (direction != Vector3.Zero)
                {
                    direction = direction.Normalized();
                    direction.Y /= 2;
                    if (direction.Y < 0.03f)
                    {
                        direction.Y = 0.03f;
                    }
                }
                else
                {
                    direction = new Vector3(0, 0.03f, 0);
                }
                return direction * magnitude;
            }
            if (type == 3)
            {
                // normalize horizontal vector between, multiply by magnitude
                Vector3 direction = (targetPos - beamPos).WithY(0);
                if (direction != Vector3.Zero)
                {
                    direction = direction.Normalized();
                    direction *= magnitude;
                }
                return direction;
            }
            if (type == 4)
            {
                //unit Y multiplied by magnitude -- unused?
                return new Vector3(0, magnitude, 0);
            }
            return Vector3.Zero;
        }

        internal bool ModReplayMatches(in ReplayShotFact fact)
            => _scene.Services.IsReplica
                && ModReplayIdentityMatches(ModLaunchKey, ModShotId, Beam, fact);

        internal static bool ModReplayIdentityMatches(in ShotKey key, uint shotId,
            BeamType beam, in ReplayShotFact fact)
        {
            if (shotId == 0 || shotId != fact.ShotId || beam != (BeamType)fact.Weapon)
                return false;
            return key.AuthorityEpoch == fact.AuthorityEpoch
                && key.MatchId == fact.MatchId
                && key.ShooterSlot == fact.ShooterSlot
                && key.Generation == fact.ShooterGeneration
                && key.LifeId == fact.ShooterLifeId;
        }

        internal float ModReplayImpactDistanceSquared(Vector3 point)
        {
            float a = (Position - point).LengthSquared;
            float b = (BackPosition - point).LengthSquared;
            return Math.Min(a, b);
        }

        internal void ModPresentReplayImpact(in ReplayShotFact fact,
            bool terminateProjectile, bool spawnEffect)
        {
            if (!_scene.Services.IsReplica) return;
            _replayImpactPosition = fact.ImpactPoint;
            // ReplaySceneServices calls this in AfterSimulation. StepReplica
            // increments FrameCount after that hook and drawing sees the
            // incremented value, so the corrected endpoint belongs to +1.
            _replayImpactDrawFrame = _scene.FrameCount + 1;
            if (terminateProjectile)
            {
                _replayImpactPending = true;
                _replayImpactHidden = false;
            }
            if (spawnEffect) PlayBeamHitSfx();
        }

        internal static Vector3 ModReplayImpactColor(Scene scene,
            in ReplayShotFact fact)
        {
            WeaponInfo weapon = scene.WeaponRules[fact.Weapon];
            ushort packed = weapon.Colors[0];
            float red = ((packed >> 0) & 0x1F) / 31f;
            float green = ((packed >> 5) & 0x1F) / 31f;
            float blue = ((packed >> 10) & 0x1F) / 31f;
            return fact.Headshot ? new Vector3(1f, 0.72f, 0.28f)
                : new Vector3(red, green, blue);
        }

        private void SpawnCollisionEffect(CollisionResult colRes, bool noSplat)
        {
            if (_scene.Services.IsReplica && _replayImpactHidden) return;
            if (CollisionEffect != 255)
            {
                if (_scene.Players.PlayerCount > 2 && CollisionEffect == 4)
                {
                    // powerBeam (4 - 3 = 1)
                    noSplat = true;
                }
                var spawnPos = new Vector3(
                    colRes.Position.X + colRes.Plane.X / 8,
                    colRes.Position.Y + colRes.Plane.Y / 8,
                    colRes.Position.Z + colRes.Plane.Z / 8
                );
                Vector3 up = Beam == BeamType.Imperialist ? -Direction : colRes.Plane.Xyz;
                if (colRes.EntityCollision != null)
                {
                    spawnPos = Matrix.Vec3MultMtx4(spawnPos, colRes.EntityCollision.Inverse1);
                    up = Matrix.Vec3MultMtx3(up, colRes.EntityCollision.Inverse1);
                }
                Vector3 facing = GetCrossVector(up);
                Matrix4 transform = GetTransformMatrix(facing, up);
                transform.Row3.Xyz = spawnPos;
                // the game uses BeamKind against "511" bits which accomplish the same thing as this terrain type check
                if (!_scene.GameState.SinglePlayer || colRes.Terrain <= Terrain.Lava)
                {
                    int beforeElements = _scene.ModEffectElementCount;
                    var ent = BeamEffectEntity.Create(
                        new BeamEffectEntityData(CollisionEffect, noSplat, transform, colRes.EntityCollision), _scene);
                    if (ent != null)
                    {
                        if (SplashDamage > 0)
                        {
                            ent.Scale = new Vector3(SplashRadius);
                        }
                        _scene.AddEntity(ent);
                    }
                    // Particle-only effects return no entity. Count a native cue only
                    // when the effect pool actually accepted it.
                    if (ent != null || _scene.ModEffectElementCount > beforeElements)
                        NetLiveImpactPresenter.NoteNativeImpact(this, colRes.Position);
                }
                // there are actually effect IDs to cover platform/enemy beams in these arrays (although most are 255)
                byte splatEffect = _terSplat1P[(int)BeamKind][(int)colRes.Terrain];
                if (_scene.GameState.SinglePlayer && splatEffect != 255)
                {
                    splatEffect += 3;
                    var ent = BeamEffectEntity.Create(
                        new BeamEffectEntityData(splatEffect, noSplat, transform, colRes.EntityCollision), _scene);
                    if (ent != null)
                    {
                        _scene.AddEntity(ent);
                    }
                }
            }
        }

        public void SpawnDamageEffect(Effectiveness effectiveness)
        {
            if (effectiveness != Effectiveness.Normal && effectiveness != Effectiveness.Double)
            {
                return;
            }
            int effectId = 0;
            Matrix4 transform = GetTransformMatrix(Vector3.UnitX, Vector3.UnitY);
            transform.Row3.Xyz = Position;
            if (effectiveness == Effectiveness.Double)
            {
                // 20 - sprEffectivePB
                // 21 - sprEffectiveElectric
                // 22 - sprEffectiveMsl
                // 23 - sprEffectiveJack
                // 24 - sprEffectiveSniper
                // 25 - sprEffectiveIce
                // 26 - sprEffectiveMortar
                // 27 - sprEffectiveGhost
                // 28 - sniperCol (unintended)
                effectId = (int)Beam + 20;
            }
            else if (_scene.GameState.SinglePlayer)
            {
                // 12 - effectiveHitPB
                // 13 - effectiveHitElectric
                // 14 - effectiveHitMsl
                // 15 - effectiveHitJack
                // 16 - effectiveHitSniper
                // 17 - effectiveHitIce
                // 18 - effectiveHitMortar
                // 19 - effectiveHitGhost
                // 20 - sprEffectivePB (unintended)
                effectId = (int)Beam + 12;
            }
            else
            {
                // 154 - mpEffectivePB
                // 155 - mpEffectiveElectric
                // 156 - mpEffectiveMsl
                // 157 - mpEffectiveJack
                // 158 - mpEffectiveSniper
                // 159 - mpEffectiveIce
                // 160 - mpEffectiveMortar
                // 161 - mpEffectiveGhost
                // 162 - pipeTricity (unintended)
                effectId = (int)Beam + 154;
            }
            if (effectId > 0)
            {
                int beforeElements = _scene.ModEffectElementCount;
                _scene.SpawnEffect(effectId, transform);
                if (_scene.ModEffectElementCount > beforeElements)
                    NetLiveImpactPresenter.NoteNativeImpact(this, Position);
            }
        }

        private static readonly IReadOnlyList<IReadOnlyList<byte>> _terSplat1P
            = new List<IReadOnlyList<byte>>()
            {
                // metal, orange holo, green holo, blue holo, ice, snow, sand, rock, lava, acid, Gorea, unknown
                new List<byte>() { 255, 99, 121, 122, 123, 126, 125, 124, 100, 142, 141, 140 }, // Power Beam
                new List<byte>() { 255, 99, 121, 122, 123, 126, 125, 124, 100, 142, 141, 140 }, // Volt Driver
                new List<byte>() { 255, 255, 255, 255, 255, 255, 255, 255, 255, 142, 141, 140 }, // Missile
                new List<byte>() { 255, 99, 121, 122, 123, 126, 125, 124, 100, 142, 141, 140 }, // Battlehammer
                new List<byte>() { 255, 99, 121, 122, 123, 126, 125, 124, 100, 142, 141, 140 }, // Imperialist
                new List<byte>() { 255, 99, 121, 122, 123, 126, 125, 124, 100, 142, 141, 140 }, // Judicator
                new List<byte>() { 255, 255, 255, 255, 255, 255, 255, 255, 255, 142, 141, 140 }, // Magmaul
                new List<byte>() { 255, 255, 255, 255, 255, 255, 255, 255, 255, 142, 141, 140 }, // Shock Coil
                new List<byte>() { 255, 255, 255, 255, 255, 255, 255, 255, 255, 142, 141, 140 }, // Omega Cannon
                new List<byte>() { 255, 255, 255, 255, 255, 255, 255, 255, 255, 142, 141, 140 }, // Platform
                new List<byte>() { 255, 255, 255, 255, 255, 255, 255, 255, 255, 142, 141, 140 } // Enemy
            };

        private void SpawnSniperBeam()
        {
            // following what the game does, but this should always be the same as SpawnPosition
            Vector3 spawnPos = PastPositions[8];
            Vector3 up = Position - spawnPos;
            float magnitude = up.Length;
            if (magnitude > 0)
            {
                up.Normalize();
                Vector3 facing = GetCrossVector(up);
                Matrix4 transform = GetTransformMatrix(facing, up);
                transform.Row3.Xyz = spawnPos;
                var ent = BeamEffectEntity.Create(new BeamEffectEntityData(type: 1, noSplat: false, transform), _scene);
                if (ent != null)
                {
                    ent.Scale = new Vector3(1, magnitude, 1);
                    _scene.AddEntity(ent);
                }
            }
        }

        private static Vector3 GetCrossVector(Vector3 up)
        {
            if (up.Z <= Fixed.ToFloat(-3686) || up.Z >= Fixed.ToFloat(3686))
            {
                return Vector3.Cross(Vector3.UnitX, up).Normalized();
            }
            return Vector3.Cross(Vector3.UnitZ, up).Normalized();
        }
    }

    [Flags]
    public enum BeamFlags : ushort
    {
        None = 0x0,
        Collided = 0x1,
        Charged = 0x2,
        Homing = 0x4,
        Ricochet = 0x8,
        SelfDamage = 0x10,
        ForceEffect = 0x20,
        Continuous = 0x40,
        Destroyable = 0x80,
        HasModel = 0x100,
        RadiusIndex1 = 0x200, // pair with bit 10: index 0-3 of radius for enemy beam collision with player beams
        RadiusIndex2 = 0x400,
        LifeDrain = 0x800,
        SurfaceCollision = 0x1000,
        DestroyMuzzle = 0x2000 // viewer only
    }

    [Flags]
    public enum BeamSpawnFlags : byte
    {
        None = 0x0,
        DoubleDamage = 0x1,
        Charged = 0x2,
        NoMuzzle = 0x4,
        PrimeHunter = 0x8,
        DestroyMuzzle = 0x10 // viewer only
    }

    [Flags]
    public enum BeamResultFlags : byte
    {
        NoSpawn = 0x0,
        Spawned = 0x1,
        Homing = 0x2 // for continuous only
    }
}
