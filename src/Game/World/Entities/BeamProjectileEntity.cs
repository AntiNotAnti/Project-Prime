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
        internal CombatShot CombatShot { get; private set; }
        private uint? _spreadSeed;
        internal uint Generation { get; private set; }
        internal bool CatchUpPending { get; set; }
        internal BeamMechanics Mechanics { get; private set; }
        internal LagCompensationMode TimingMode { get; private set; }
        public BeamFlags Flags { get; set; }
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
        public EntityBase? Target { get; set; }
        private CombatActor _homingTargetIdentity;
        public EquipInfo? Equip { get; set; }

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

        private static readonly EquipInfo _ricochetEquip = new EquipInfo();
        internal ModelInstance? _trailModel;

        public BeamProjectileEntity(Scene scene) : base(EntityType.BeamProjectile, scene)
        {
        }

        public override void Initialize()
        {
            base.Initialize();
            if (_scene.IsHeadless) return;
            // model will be loaded and bound by scene setup
            if (DrawFuncId == 0 || DrawFuncId == 3 || DrawFuncId == 6 || DrawFuncId == 7 || DrawFuncId == 10 || DrawFuncId == 12)
            {
                _trailModel = Read.GetModelInstance("trail");
            }
            else if (DrawFuncId == 1 || DrawFuncId == 2)
            {
                _trailModel = Read.GetModelInstance("electroTrail");
            }
            else if (DrawFuncId == 9)
            {
                _trailModel = Read.GetModelInstance("arcWelder");
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

        public override bool Process() => CatchUpPending || ProcessCore();
        private bool _catchUpCollision;
        internal bool ProcessCatchUpStep(out bool collided)
        {
            _catchUpCollision = false;
            bool alive = ProcessCore();
            collided = _catchUpCollision;
            return alive;
        }
        internal void RemoveAfterCatchUp()
        {
            _scene.SendMessage(Message.Destroyed, this, null, 0, 0, delay: 1);
            Destroy();
            _scene.RemoveEntity(this);
        }
        private bool ProcessCore()
        {
            uint generation = Generation;
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
            // Current-frame destruction cannot erase a player/turret before
            // its historical lifetime ends. Per-tick identity/history governs
            // past steering; the current endpoint keeps normal messages.
            bool historicalHomingTarget = CombatShot.IsValid && TimingMode == LagCompensationMode.HomingProjectileCatchUp
                && (Target is PlayerEntity or HalfturretEntity) && _scene.Services.Combat is ICombatAuthority targetCombat
                && targetCombat.CollisionTick is uint targetTick && targetTick != targetCombat.Tick;
            if (Target != null && !historicalHomingTarget)
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
                if (Generation != generation) return true;
            }
            if (Flags.TestFlag(BeamFlags.Homing) && !Flags.TestFlag(BeamFlags.Continuous) && Target != null)
            {
                Vector3 targetPos;
                if (CombatShot.IsValid && TimingMode == LagCompensationMode.HomingProjectileCatchUp
                    && _scene.Services.Combat is ICombatAuthority combat)
                {
                    if (!combat.TryGetHomingTarget(Target, combat.CollisionTick ?? combat.Tick,
                        _homingTargetIdentity, out targetPos, out _)) Target = null;
                }
                else Target.GetPosition(out targetPos);
                if (Target != null)
                {
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
                OnCollision(colRes, colWith: null);
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
                    if (door.Flags.TestFlag(DoorFlags.Open))
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
            if (Owner.Type != EntityType.ForceFieldLock)
            {
                foreach (ForceFieldLockEntity fieldLock in _scene.GetForceFieldLockEntities())
                {
                    CollisionResult res = default;
                    if (CollisionDetection.CheckCylinderOverlapVolume(fieldLock.HurtVolume, BackPosition, Position, CylinderRadius, ref res))
                    {
                        if (res.Distance < minDist)
                        {
                            minDist = res.Distance;
                            anyRes = res;
                            colWith = fieldLock;
                            noColEff = false;
                        }
                    }
                }
            }
            bool hitHalfturret = false;
            bool historicalHit = false;
            LagCompensationState historicalTarget = default;
            ICombatAuthority? combat = _scene.Services.Combat;
            // todo: visualize player collision (and rename some "pickup" fields)
            foreach (PlayerEntity player in _scene.GetPlayerEntities())
            {
                if (player.Health == 0 || player.Flags2.TestFlag(PlayerFlags2.Spectating))
                {
                    continue;
                }
                _scene.Services.CountPlayerCheck(player.SlotIndex);
                LagCompensationState history = default;
                bool historical = combat != null && CombatShot.IsValid;
                if (historical && !combat!.TryGetPlayerCollider(player, CombatShot, out history)) continue;
                bool hasHalfturret = historical ? history.HasHalfturret
                    : player.Hunter == Hunter.Weavel && player.Flags2.TestFlag(PlayerFlags2.Halfturret);
                if ((Owner == player || hasHalfturret && Owner == player.Halfturret)
                    && (!Flags.TestFlag(BeamFlags.SelfDamage) || Age < 1 / 30f * 4))
                {
                    continue;
                }
                bool hitPlayer = false;
                CollisionResult playerRes = default;
                float radii = player.Volume.SphereRadius + CylinderRadius;
                if (historical)
                {
                    hitPlayer = history.CheckPlayer(BackPosition, Position, CylinderRadius, ref playerRes);
                }
                else if (player.IsAltForm)
                {
                    if (player.Hunter == Hunter.Kanden)
                    {
                        if (CollisionDetection.CheckCylinderOverlapSphere(BackPosition, Position,
                            player.KandenSegPos[2], 1.6f, ref playerRes))
                        {
                            if (CollisionDetection.CheckCylinderOverlapSphere(BackPosition, Position,
                                player.Volume.SpherePosition, radii, ref playerRes)
                                || CollisionDetection.CheckCylinderOverlapSphere(BackPosition, Position,
                                    player.KandenSegPos[1], radii, ref playerRes)
                                || CollisionDetection.CheckCylinderOverlapSphere(BackPosition, Position,
                                    player.KandenSegPos[2], radii, ref playerRes)
                                || CollisionDetection.CheckCylinderOverlapSphere(BackPosition, Position,
                                    player.KandenSegPos[3], radii, ref playerRes))
                            {
                                hitPlayer = true;
                            }
                        }
                    }
                    else
                    {
                        if (CollisionDetection.CheckCylinderOverlapSphere(BackPosition, Position,
                            player.Volume.SpherePosition, radii, ref playerRes))
                        {
                            hitPlayer = true;
                        }
                    }
                }
                else
                {
                    float minY = Fixed.ToFloat(player.Values.MinPickupHeight);
                    Vector3 playerBottom = player.Position.AddY(minY);
                    float dot = Fixed.ToFloat(player.Values.MaxPickupHeight) - minY;
                    if (CollisionDetection.CheckCylindersOverlap(BackPosition, Position, playerBottom, Vector3.UnitY,
                        dot, radii, ref playerRes))
                    {
                        hitPlayer = true;
                    }
                }
                if (hitPlayer && playerRes.Distance < minDist)
                {
                    _scene.Services.NotePlayerOverlap(Owner, player);
                    _scene.Services.CountPlayerOverlap(player.SlotIndex);
                    _scene.Services.CountPlayerAccepted(player.SlotIndex);
                    minDist = playerRes.Distance;
                    anyRes = playerRes;
                    colWith = player;
                    noColEff = false;
                    hitHalfturret = false;
                    historicalHit = historical;
                    historicalTarget = history;
                }
                else if (hitPlayer)
                {
                    _scene.Services.CountPlayerOverlap(player.SlotIndex);
                }
                // todo?: else wifi check
                if (hasHalfturret && player.Flags2.TestFlag(PlayerFlags2.Halfturret) && Owner != player.Halfturret)
                {
                    CollisionResult turretRes = default;
                    float radius = CylinderRadius + 0.45f;
                    bool hitTurret = historical
                        ? history.CheckHalfturret(BackPosition, Position, CylinderRadius, ref turretRes)
                        : CollisionDetection.CheckCylinderOverlapSphere(BackPosition, Position, player.Halfturret.Position, radius, ref turretRes);
                    if (hitTurret && turretRes.Distance < minDist)
                    {
                        minDist = turretRes.Distance;
                        anyRes = turretRes;
                        colWith = player.Halfturret;
                        noColEff = false;
                        hitHalfturret = true;
                        historicalHit = historical;
                        historicalTarget = history;
                    }
                }
            }
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
                        if (player.BeamEffectiveness[(int)Beam] != Effectiveness.Zero)
                        {
                            if (hitHalfturret)
                            {
                                damageFlags |= DamageFlags.Halfturret;
                            }
                            Vector3 damageDir = GetDamageDirection(anyRes.Position, historicalHit ? historicalTarget.Position : player.Position);
                            float damage = 0;
                            uint wholeDamage = 0;
                            bool isHeadshot = false;
                            bool headshotHeight = historicalHit ? historicalTarget.IsHeadshot(anyRes.Position)
                                : !player.IsAltForm && anyRes.Position.Y - player.Position.Y >= Fixed.ToFloat(player.Values.MaxPickupHeight) - 0.3f;
                            if (headshotHeight && Beam != BeamType.ShockCoil)
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
                            wholeDamage = (uint)Math.Clamp(damage, 0, Int32.MaxValue);
                            if (wholeDamage != 0)
                            {
                                player.TakeDamage(wholeDamage, damageFlags, damageDir, this);
                            }
                            if (!_scene.Services.IsReplica && _scene.Services.Combat?.IsStaleSource(this) != true
                                && Flags.TestFlag(BeamFlags.LifeDrain) && Owner.Type == EntityType.Player)
                            {
                                var ownerPlayer = (PlayerEntity)Owner;
                                if (!ownerPlayer.IsPrimeHunter && ownerPlayer.TeamIndex != player.TeamIndex)
                                {
                                    // GainHealth checks if the player is alive
                                    ownerPlayer.GainHealth(wholeDamage);
                                }
                            }
                            if (!player.IsMainPlayer || player.IsAltForm || player.IsMorphing)
                            {
                                SpawnCollisionEffect(anyRes, noSplat: true);
                            }
                        }
                        OnCollision(anyRes, colWith);
                        PlayBeamHitSfx();
                        ricochet = false;
                    }
                    else if (colWith.Type == EntityType.ForceFieldLock)
                    {
                        var fieldLock = (ForceFieldLockEntity)colWith;
                        float damage = Damage;
                        if (MaxDistance > 0)
                        {
                            float pct = Vector3.Distance(Position, SpawnPosition) / MaxDistance;
                            damage = GetInterpolatedValue(DamageInterpolation, Damage, 0, pct);
                        }
                        if (damage > 0 && (Beam != BeamType.ShockCoil || _scene.FrameCount % 2 == 0)) // todo: FPS stuff
                        {
                            if (!_scene.Services.IsReplica) fieldLock.TakeDamage((uint)damage, this);
                            SpawnCollisionEffect(anyRes, noSplat: true);
                        }
                        OnCollision(anyRes, colWith);
                        PlayBeamHitSfx();
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
                            if (!_scene.Services.IsReplica && (player.IsMainPlayer || !_scene.ControlsPlayer)) // skdebug
                            {
                                if (door.Flags.TestFlag(DoorFlags.Locked) && !door.Flags.TestFlag(DoorFlags.ShowLock))
                                {
                                    if (door.Data.PaletteId == (int)Beam)
                                    {
                                        door.Unlock(updateState: true, noLockAnimSfx: true);
                                    }
                                }
                                if (!_scene.InRoomTransition)
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
                            if (!_scene.Services.IsReplica) forceField.Lock?.LockHit(this);
                            ricochet = false;
                        }
                    }
                }
                else
                {
                    // collided with room, platform, or object collision
                    bool reflected = anyRes.Flags.TestFlag(CollisionFlags.ReflectBeams);
                    if (anyRes.EntityCollision != null)
                    {
                        if (!_scene.Services.IsReplica)
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
                    amountA = Rng.GetRandomInt1(0xFFFF);
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
            StopHomingSfx();
            BeamSfx type = Flags.TestFlag(BeamFlags.Charged) ? BeamSfx.ChargeHit : BeamSfx.Hit;
            int sfx = Metadata.BeamSfx[(int)Beam, (int)type];
            if (sfx != -1)
            {
                _soundSource.PlaySfx(sfx, noUpdate: true);
            }
        }

        public void OnCollision(CollisionResult colRes, EntityBase? colWith)
        {
            _catchUpCollision = true;
            uint generation = Generation;
            bool impactSent = false;
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
                CheckSplashDamage(colWith);
                if (RicochetWeapon != null && (colWith == null || colWith.Type != EntityType.Player))
                {
                    Vector3 factor = Velocity * 7;
                    float dot = Vector3.Dot(colRes.Plane.Xyz, factor);
                    Vector3 spawnDir = new Vector3(
                        colRes.Plane.X + factor.X - colRes.Plane.X * 2 * dot,
                        colRes.Plane.Y + factor.Y - colRes.Plane.Y * 2 * dot,
                        colRes.Plane.Z + factor.Z - colRes.Plane.Z * 2 * dot
                    ).Normalized();
                    _ricochetEquip.Beams = Equip.Beams;
                    _ricochetEquip.Weapon = RicochetWeapon;
                    BeamSpawnFlags flags = BeamSpawnFlags.None;
                    if (Flags.TestFlag(BeamFlags.Charged))
                    {
                        flags |= BeamSpawnFlags.Charged;
                    }
                    // Mark the old generation before a child can recycle this
                    // slot; otherwise recycling would apply its splash again.
                    if (_scene.Services.Combat != null)
                    {
                        Flags |= BeamFlags.Collided;
                        // The old generation must emit its impact before the
                        // shared slot can become a child with a different owner.
                        if (!_scene.Services.IsReplica)
                            _scene.SendMessage(Message.Impact, this, Owner, colWith ?? (object)0, 0);
                        StopHomingSfx();
                        impactSent = true;
                    }
                    Spawn(Owner, _ricochetEquip, colRes.Position, spawnDir, flags, NodeRef, _scene, CombatShot, _spreadSeed);
                    if (Generation != generation) return;
                }
            }
            if (!Flags.TestFlag(BeamFlags.Continuous))
            {
                Flags |= BeamFlags.Collided;
                Lifespan = 4 * (1 / 30f); // todo: frame time stuff
                Velocity = Vector3.Zero;
            }
            if (!impactSent && Owner != null)
            {
                if (!_scene.Services.IsReplica)
                    _scene.SendMessage(Message.Impact, this, Owner, colWith ?? (object)0, 0);
                StopHomingSfx();
            }
        }

        private void CheckSplashDamage(EntityBase? colWith)
        {
            foreach (PlayerEntity player in _scene.GetPlayerEntities())
            {
                if (player == colWith)
                {
                    continue;
                }

                void OmegaCannonFlash()
                {
                    if (Beam == BeamType.OmegaCannon && player == PlayerEntity.Main)
                    {
                        _scene.SetFade(FadeType.FadeInWhite, 15 / 30f, overwrite: false);
                    }
                }

                if (player.Health > 0)
                {
                    if (!player.Flags2.TestFlag(PlayerFlags2.Halfturret) || Owner != player.Halfturret)
                    {
                        CollisionResult discard = default;
                        Vector3 targetPosition = player.Position;
                        if (_scene.Services.Combat is {} combat && CombatShot.IsValid)
                        {
                            if (!combat.TryGetPlayerCollider(player, CombatShot, out LagCompensationState collider)) continue;
                            targetPosition = collider.Position;
                        }
                        float dist = Vector3.Distance(targetPosition, Position);
                        // todo?: wifi conditions
                        if (dist >= SplashRadius
                            || CollisionDetection.CheckBetweenPoints(Position, targetPosition, TestFlags.Beams, _scene, ref discard))
                        {
                            OmegaCannonFlash();
                        }
                        else
                        {
                            Vector3 damageDir = GetDamageDirection(Position, targetPosition);
                            float ratio = dist / SplashRadius;
                            int damage = (int)GetInterpolatedValue(SplashDamageType, SplashDamage, 0, ratio);
                            player.TakeDamage(damage, DamageFlags.NoDmgInvuln, damageDir, this);
                            if (Owner != null)
                            {
                                if (!_scene.Services.IsReplica) _scene.SendMessage(Message.Impact, this, Owner, player, 0);
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
            foreach (ForceFieldLockEntity fieldLock in _scene.GetForceFieldLockEntities())
            {
                if (fieldLock == colWith)
                {
                    continue;
                }
                CollisionResult res = default;
                float dist = Vector3.Distance(fieldLock.Position, Position);
                if (dist < SplashRadius
                    && !CollisionDetection.CheckBetweenPoints(Position, fieldLock.Position, TestFlags.Beams, _scene, ref res))
                {
                    float damage = GetInterpolatedValue(SplashDamageType, SplashDamage, 0, dist / SplashRadius);
                    if (!_scene.Services.IsReplica) fieldLock.TakeDamage((uint)damage, this);
                    if (Owner != null)
                    {
                        if (!_scene.Services.IsReplica) _scene.SendMessage(Message.Impact, this, Owner, fieldLock, 0);
                        StopHomingSfx();
                    }
                }
            }
        }

        private float GetInterpolatedValue(int type, float value1, float value2, float ratio)
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

        protected internal override Matrix4 GetModelTransform(ModelInstance inst, int index)
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
            _homingTargetIdentity = default;
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
            BeamSpawnFlags spawnFlags, NodeRef nodeRef, Scene scene, CombatShot? inheritedShot = null, uint? spreadSeed = null)
        {
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
            int cost = (int)GetAmount(weapon.AmmoCost, weapon.MinChargeCost, weapon.ChargeCost);
            if (weapon.Flags.TestFlag(WeaponFlags.Continuous))
            {
                // todo?: figure out what the intent behind this actually is
                // game's cycle for Shock Coil (10): 0 0 0 1 0 0 1 0 0 1 0 0 1 0 0 0
                //    our cycle for Shock Coil (10): 0 0 0 0 0 0 1 0 0 0 0 0 1 0 0 0 0 0 1 0 0 0 0 0 1 0 0 0 0 0 0 0
                // game's cycle for green beam (15): 0 0 1 0 1 0 1 0 1 0 1 0 1 0 1 0 0 1 0 1 0 1 0 1 0 1 0 1 0 1 0 0
                //    our cycle for green beam (15): 0 0 0 0 1 0 0 0 1 0 0 0 1 0 0 0 1 0 0 0 1 0 0 0 1 0 0 0 1 0 0 0
                //                                   0 0 1 0 0 0 1 0 0 0 1 0 0 0 1 0 0 0 1 0 0 0 1 0 0 0 1 0 0 0 0 0
                if (scene.FrameCount % 2 == 0)
                {
                    ulong bits = (ulong)(cost & 31);
                    cost /= 32;
                    if (scene.FrameCount % 2 == 0 && bits != 0 && ((bits * (scene.FrameCount / 2)) & 31) > 32 - bits) // todo: FPS stuff
                    {
                        cost++;
                    }
                }
                else
                {
                    cost = 0;
                }
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
            int projectiles = (int)GetAmount(weapon.Projectiles, weapon.MinChargeProjectiles, weapon.ChargedProjectiles);
            if (projectiles <= 0)
            {
                return result;
            }

            bool instantAoe = (charged && weapon.Flags.TestFlag(WeaponFlags.AoeCharged))
                || (!charged && weapon.Flags.TestFlag(WeaponFlags.AoeUncharged));

            BeamFlags flags = BeamFlags.None;
            // todo: FPS stuff
            float speed = GetAmount(weapon.UnchargedSpeed, weapon.MinChargeSpeed, weapon.ChargedSpeed) / 4096f / 2;
            float finalSpeed = GetAmount(weapon.UnchargedFinalSpeed, weapon.MinChargeFinalSpeed, weapon.ChargedFinalSpeed) / 4096f / 2;
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
            if (weapon.Beam == BeamType.Imperialist && !equip.Zoomed)
            {
                damage /= 2;
                hsDamage /= 2;
                splashDmg /= 2;
            }
            // todo?: it's kind of lame that double damage doesn't affect Shock Coil
            if (weapon.Flags.TestFlag(WeaponFlags.Continuous))
            {
                // todo: this is the same as the ammo calculation but with ge instead of gt
                // note: previously the frame count partiy check was part of the condition below, but that assumed the base value
                // was zero after the division by 32, which is true for Shock Coil but not e.g. platform green energy beams,
                // so we need those to hit every other frame to match the DPS from the game
                if (scene.FrameCount % 2 == 0)
                {
                    ulong bits = (ulong)(damage & 31);
                    damage /= 32;
                    if (bits != 0 && ((bits * (scene.FrameCount / 2)) & 31) >= 32 - bits) // todo: FPS stuff
                    {
                        damage++;
                    }
                }
                else
                {
                    damage = 0;
                }
            }
            if (Cheats.QuadrupleDamage)
            {
                damage *= 4;
                hsDamage *= 4;
                splashDmg *= 4;
            }
            ushort damageInterpolation = weapon.DamageInterpolations[charged ? 1 : 0];
            float maxDist = GetAmount(weapon.UnchargedDistance, weapon.MinChargeDistance, weapon.ChargedDistance) / 4096f;
            Affliction afflictions = weapon.Afflictions[charged ? 1 : 0];
            float cylinderRadius = GetAmount(weapon.UnchargedCylRadius, weapon.MinChargeCylRadius, weapon.ChargedCylRadius) / 4096f;
            float lifespan = GetAmount(weapon.UnchargedLifespan, weapon.MinChargeLifespan, weapon.ChargedLifespan) * (1 / 30f);
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
            var mechanics = new BeamMechanics(weapon.Beam, weapon.BeamKind, flags.TestFlag(BeamFlags.Continuous),
                instantAoe, homing, speed, lifespan);
            CombatShot combatShot = inheritedShot ?? scene.Services.Combat?.CaptureShot(owner, mechanics) ?? default;
            if (!spreadSeed.HasValue && scene.Services.Combat != null && maxSpread > 0)
            {
                // Preserve the ordinary gameplay stream's advance. Root spread
                // uses a separate match stream so impact timing cannot change
                // future aim samples; pellets and children retain their seed.
                Rng.GetRandomInt2(0);
                spreadSeed = inheritedShot.HasValue ? 0u : scene.Services.Combat.NextSpreadSeed();
            }
            var spread = new BeamSpread(spreadSeed.GetValueOrDefault());
            if (!inheritedShot.HasValue)
            {
                scene.Services.Combat?.NoteShot(combatShot, weapon.Beam, charged, position, direction, equip.ChargeLevel,
                    (int)weapon.Beam is >= 0 and <= 8 && ReferenceEquals(weapon, Weapons.Current[(int)weapon.Beam + 9]),
                    spreadSeed.GetValueOrDefault());
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
                    beam.OnCollision(colRes, colWith: null);
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
                beam.Generation++;
                beam.CatchUpPending = false;
                beam.Mechanics = mechanics;
                beam.TimingMode = scene.Services.Combat?.GetMode(mechanics) ?? LagCompensationMode.None;
                // Verified player homing variants are root actions. No retail
                // multiplayer ricochet child homes; future child metadata needs
                // its own acquisition-time validation before enabling catch-up.
                if (inheritedShot.HasValue && beam.TimingMode == LagCompensationMode.HomingProjectileCatchUp)
                    beam.TimingMode = LagCompensationMode.None;
                beam.Owner = owner;
                beam.CombatShot = combatShot;
                beam._spreadSeed = spreadSeed;
                beam.Beam = weapon.Beam;
                beam.BeamKind = weapon.BeamKind;
                beam.Flags = flags;
                beam.NodeRef = nodeRef;
                beam.Age = 0;
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
                beam.SpawnPosition = beam.BackPosition = beam.Position = position;
                for (int j = 0; j < 10; j++)
                {
                    beam.PastPositions[j] = position;
                }
                beam.Direction = dirVec;
                beam.Right = rightVec;
                beam.Up = upVec;
                beam.Damage = damage;
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
                    if (!scene.Services.IsReplica) scene.Match.Players[ownerPlayer.SlotIndex].BeamDamageMax += damage;
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
                    velocity = spreadSeed.HasValue
                        ? spread.Next(direction, beam.Up, beam.Right, (uint)maxSpread, beam.Speed)
                        : BeamSpread.Velocity(direction, beam.Up, beam.Right, beam.Speed,
                            Rng.GetRandomInt2((uint)maxSpread), Rng.GetRandomInt2(0x168000));
                }
                beam.Velocity = velocity;
                beam.Acceleration = acceleration;
                if (beam.DrawFuncId == 3)
                {
                    beam.Flags |= BeamFlags.HasModel;
                    beam._models.Add(Read.GetModelInstance("iceShard"));
                }
                else if (beam.DrawFuncId == 17)
                {
                    beam.Flags |= BeamFlags.HasModel;
                    ModelInstance model = Read.GetModelInstance("energyBeam");
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
                    if (CheckHomingTargets(beam, equip, scene))
                    {
                        result |= BeamResultFlags.Homing;
                    }
                    if (beam.Beam == BeamType.ShockCoil && owner.Type == EntityType.Player)
                    {
                        var ownerPlayer = (PlayerEntity)owner;
                        if (ownerPlayer.ShockCoilTarget == beam.Target
                            && scene.FrameCount % 2 == 0) // todo: FPS stuff
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
                beam._soundSource.Update(beam.Position, rangeIndex: 0);
                scene.AddEntity(beam);
                scene.Services.Combat?.EnqueueCatchUp(beam, inheritedShot.HasValue);
            }
            return result;
        }

        private static readonly IReadOnlyList<EntityType> _homingTargetTypes = new EntityType[5]
        {
            EntityType.Player,
            EntityType.Halfturret,
            EntityType.ForceFieldLock,
            EntityType.Door,
            EntityType.Platform
        };

        private static bool CheckHomingTargets(BeamProjectileEntity beam, EquipInfo equip, Scene scene)
        {
            bool result = false;
            WeaponInfo weapon = equip.Weapon;
            Debug.Assert(beam.Owner != null);
            float tolerance = Fixed.ToFloat(equip.HomingTolerance);
            float curDiv = tolerance;
            for (int i = 0; i < _homingTargetTypes.Count; i++)
            {
                EntityType type = _homingTargetTypes[i];
                if (type == EntityType.ForceFieldLock
                    && (beam.Owner.Type == EntityType.ForceFieldLock || beam.Owner.Type == EntityType.Platform))
                {
                    continue;
                }
                bool historicalTurrets = type == EntityType.Halfturret && beam.CombatShot.IsValid
                    && beam.TimingMode == LagCompensationMode.HomingProjectileCatchUp
                    && scene.Services.Combat is ICombatAuthority current && beam.CombatShot.ActionServerTick != current.Tick;
                foreach (EntityBase candidate in scene.Entities)
                {
                    EntityBase entity = candidate;
                    if (historicalTurrets)
                    {
                        // At most eight owner-bound candidates. A turret may
                        // exist in history after it left the current entity list.
                        if (candidate is not PlayerEntity player) continue;
                        entity = player.Halfturret;
                    }
                    if (entity.Type != type || entity == beam.Owner)
                    {
                        continue;
                    }
                    Vector3 position;
                    CombatActor targetIdentity = default;
                    if (beam.CombatShot.IsValid && beam.TimingMode == LagCompensationMode.HomingProjectileCatchUp
                        && scene.Services.Combat is ICombatAuthority combat)
                    {
                        if (!combat.TryGetHomingTarget(entity, beam.CombatShot.ActionServerTick,
                            default, out position, out targetIdentity)) continue;
                    }
                    else
                    {
                        if (!entity.GetTargetable()) continue;
                        entity.GetPosition(out position);
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
                            tryTarget = player.TeamIndex != ownerPlayer.TeamIndex;
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
                            tryTarget = halfturret.Owner.TeamIndex != ownerPlayer.TeamIndex;
                        }
                    }
                    else if (type == EntityType.ForceFieldLock)
                    {
                        var fieldLock = (ForceFieldLockEntity)entity;
                        tryTarget = fieldLock.Health > 0;
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
                                        if (!player.IsAltForm && !player.IsMorphing || div1 >= Fixed.ToFloat(4006))
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
                                    beam._homingTargetIdentity = targetIdentity;
                                }
                            }
                        }
                    }
                }
            }
            return result;
        }

        private void SpawnIceWave(WeaponInfo weapon, float chargePct)
        {
            float angle = chargePct <= 0
                ? weapon.UnchargedSpread
                : weapon.MinChargeSpread + ((weapon.ChargedSpread - weapon.MinChargeSpread) * chargePct);
            angle /= 4096f;
            Debug.Assert(angle == 60);
            CheckIceWaveCollision(angle);
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
                if (player.Flags2.TestFlag(PlayerFlags2.Halfturret))
                {
                    CheckIceWaveCollision(player, player.Halfturret.Position, angleCos, halfturret: true);
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
            Vector3 between = position - Position;
            float dot = Vector3.Dot(between, Up);
            between += Up * -dot;
            float mag = between.Length;
            if (mag < MaxDistance)
            {
                between /= mag;
                if (Vector3.Dot(between, Direction) > angleCos)
                {
                    Vector3 dir = GetDamageDirection(Position, player.Position);
                    DamageFlags flags = DamageFlags.NoDmgInvuln;
                    if (halfturret)
                    {
                        flags |= DamageFlags.Halfturret;
                    }
                    player.TakeDamage((int)Damage, flags, dir, this);
                }
            }
        }

        private Vector3 GetDamageDirection(Vector3 beamPos, Vector3 targetPos)
        {
            if (DamageDirType == 1)
            {
                // multiply velocity (or unit Y if not moving) by magnitude -- unused?
                Vector3 direction = Vector3.UnitY;
                if (Velocity != Vector3.Zero)
                {
                    direction = Velocity.Normalized();
                }
                return direction * DamageDirMag;
            }
            if (DamageDirType == 2)
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
                return direction * DamageDirMag;
            }
            if (DamageDirType == 3)
            {
                // normalize horizontal vector between, multiply by magnitude
                Vector3 direction = (targetPos - beamPos).WithY(0);
                if (direction != Vector3.Zero)
                {
                    direction = direction.Normalized();
                    direction *= DamageDirMag;
                }
                return direction;
            }
            if (DamageDirType == 4)
            {
                //unit Y multiplied by magnitude -- unused?
                return new Vector3(0, DamageDirMag, 0);
            }
            return Vector3.Zero;
        }

        private void SpawnCollisionEffect(CollisionResult colRes, bool noSplat)
        {
            if (CollisionEffect != 255)
            {
                if (PlayerEntity.PlayerCount > 2 && CollisionEffect == 4)
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
                _scene.SpawnEffect(effectId, transform);
            }
        }

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
        RadiusIndex1 = 0x200, // pair with bit 10: index 0-3 of radius for fieldLock beam collision with player beams
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
