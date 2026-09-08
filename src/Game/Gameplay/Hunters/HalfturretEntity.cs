using System;
using System.Diagnostics;
using MphRead.Effects;
using MphRead.Formats;
using MphRead.Formats.Culling;
using OpenTK.Mathematics;

namespace MphRead.Entities
{
    public class HalfturretEntity : DynamicLightEntityBase
    {
        public PlayerEntity Owner { get; }
        private EntityBase? _target = null;
        public EntityBase? Target => _target;
        public NodeData3? ClosestNode { get; set; } = null;

        private int _health = 0;
        internal ushort _timeSinceDamage = UInt16.MaxValue;
        private ushort _timeSinceFrozen = 0;
        internal ushort _freezeTimer = 0;
        private ushort _burnTimer = 0;
        private EffectEntry? _burnEffect = null;

        public int Health { get => _health; set => _health = value; }
        public ushort TimeSinceDamage { get => _timeSinceDamage; set => _timeSinceDamage = value; }

        private float _ySpeed = 0;
        private bool _grounded = false;
        internal Vector3 _aimVector;
        private ushort _targetTimer = 0; // todo: is this always zero?
        private ushort _cooldownTimer = 0;
        private float _cooldownFactor = 1.5f;

        public EquipInfo EquipInfo { get; } = new EquipInfo();

        internal Node _baseNode = null!;
        internal Node _baseNodeParent = null!;
        internal ModelInstance _altIceModel = null!;

        public HalfturretEntity(PlayerEntity owner, Scene scene) : base(EntityType.Halfturret, scene)
        {
            Owner = owner;
        }

        public void Create()
        {
            ModelInstance inst = SetUpModel("WeavelAlt_Turret_lod0");
            _baseNode = inst.Model.GetNodeByName("TurretBase")!;
            _baseNodeParent = inst.Model.Nodes[_baseNode.ParentIndex];
            if (!_scene.IsHeadless)
            {
                _altIceModel = SetUpModel("alt_ice");
            }
        }

        public override void Initialize()
        {
            Recolor = Owner.Recolor;
            base.Initialize();
            float minY = Fixed.ToFloat(Owner.Values.MinPickupHeight);
            Vector3 position = Owner.Position.AddY(minY + 0.45f);
            var facing = new Vector3(Owner.Field70, 0, Owner.Field74);
            Transform = GetTransformMatrix(facing, Vector3.UnitY, position);
            _aimVector = facing;
            NodeRef = Owner.NodeRef;
            int health = Owner.Health;
            if (health > 1)
            {
                _health = health / 2;
                Owner.Health -= _health;
            }
            else
            {
                _health = 1;
            }
            _grounded = Owner.Flags1.TestFlag(PlayerFlags1.Standing);
            EquipInfo.Beams = Owner.EquipInfo.Beams;
            EquipInfo.Weapon = Weapons.Current[3]; // non-affinity Battlehammer
            _models[0].SetAnimation(1, AnimFlags.NoLoop);
            _light1Vector = Owner.Light1Vector;
            _light1Color = Owner.Light1Color;
            _light2Vector = Owner.Light2Vector;
            _light2Color = Owner.Light2Color;
        }

        public override void GetVectors(out Vector3 position, out Vector3 up, out Vector3 facing)
        {
            position = Position;
            up = Vector3.UnitY;
            facing = FacingVector;
        }

        public void Reposition(Vector3 offset, NodeRef nodeRef)
        {
            Position += offset;
            _target = null;
            _targetTimer = 0;
            NodeRef = nodeRef;
        }

        public void OnTakeDamage(EntityBase attacker, uint damage)
        {
            _target = attacker;
            _targetTimer = 30 * 2; // todo: FPS stuff
            _cooldownFactor -= 61 * damage;
            if (_cooldownFactor < 0.7f)
            {
                _cooldownFactor = 0.7f;
            }
        }

        public void OnFrozen()
        {
            // todo: FPS stuff
            if (_timeSinceFrozen > 60 * 2)
            {
                _freezeTimer = 75 * 2;
            }
            else if (_freezeTimer < 15 * 2)
            {
                _freezeTimer = 15 * 2;
            }
        }

        public void OnSetOnFire()
        {
            _burnTimer = 150 * 2; // todo: FPS stuff
            if (_burnEffect != null)
            {
                _scene.UnlinkEffectEntry(_burnEffect);
                _burnEffect = null;
            }
            _burnEffect = _scene.SpawnEffectGetEntry(187, FacingVector.WithY(0), Vector3.UnitY, Position); // flamingAltForm
            _burnEffect?.SetElementExtension(true);
        }

        public override bool Process()
        {
            if (_health == 0 || !Owner.Flags2.TestFlag(PlayerFlags2.Halfturret))
            {
                return false;
            }
            if (_burnTimer > 0)
            {
                _burnTimer--;
                if (_burnTimer % (8 * 2) == 0) // todo: FPS stuff
                {
                    Owner.TakeDamage(1, DamageFlags.NoSfx | DamageFlags.Burn | DamageFlags.NoDmgInvuln | DamageFlags.Halfturret,
                        direction: null, Owner.BurnedBy);
                }
                if (_burnEffect != null)
                {
                    Vector3 facing = FacingVector;
                    facing = new Vector3(facing.X, 0, facing.Z);
                    _burnEffect.Transform(facing, Vector3.UnitY, Position);
                }
            }
            else if (_burnEffect != null)
            {
                _scene.UnlinkEffectEntry(_burnEffect);
                _burnEffect = null;
            }
            if (_freezeTimer == 0)
            {
                base.Process();
                if (_targetTimer > 0)
                {
                    _targetTimer--;
                }
                else
                {
                    _target = null;
                }
                if (_cooldownFactor < 1.5f)
                {
                    _cooldownFactor = Math.Min(_cooldownFactor + 0.015f / 2, 1.5f); // todo: FPS stuff
                }
                else if (_cooldownFactor > 1.5f)
                {
                    _cooldownFactor = Math.Max(_cooldownFactor - 0.015f / 2, 1.5f); // todo: FPS stuff
                }
                if (_target == null)
                {
                    float minDistSqr = 15 * 15;
                    foreach (PlayerEntity player in _scene.GetPlayerEntities())
                    {
                        if (player == Owner || player.Health == 0 || player.TeamIndex == Owner.TeamIndex || player.CurAlpha < 6 / 31f)
                        {
                            continue;
                        }
                        Vector3 between = player.Position - Position;
                        float distSqr = between.LengthSquared;
                        if (distSqr < minDistSqr)
                        {
                            minDistSqr = distSqr;
                            _target = player;
                        }
                    }
                }
                if (_target != null)
                {
                    Vector3 muzzlePos = Position.AddY(0.4f);
                    _cooldownTimer = 1; // not being used

                    UpdateAim(muzzlePos, _target.Position, EquipInfo, out _aimVector);
                    float cooldown = EquipInfo.Weapon.ShotCooldown * _cooldownFactor;
                    if (cooldown < 7.5f)
                    {
                        cooldown = 7;
                    }
                    if (Owner.TimeSinceShot >= cooldown * 2 && _cooldownTimer < 60 * 2) // todo: FPS stuff
                    {
                        BeamSpawnFlags flags = Owner.DoubleDamage ? BeamSpawnFlags.DoubleDamage : BeamSpawnFlags.None;
                        BeamResultFlags result = BeamProjectileEntity.Spawn(this, EquipInfo, muzzlePos, _aimVector, flags, NodeRef, _scene);
                        if (result != BeamResultFlags.NoSpawn)
                        {
                            _models[0].SetAnimation(0, AnimFlags.NoLoop);
                            Owner.TimeSinceShot = 0;
                        }
                    }
                }
            }
            else
            {
                _freezeTimer--;
                _timeSinceFrozen = 0;
            }
            if (_timeSinceFrozen != UInt16.MaxValue)
            {
                _timeSinceFrozen++;
            }
            if (_timeSinceDamage != UInt16.MaxValue)
            {
                _timeSinceDamage++;
            }
            if (Owner == _scene.LocalPlayer)
            {
                string message = Text.Strings.GetHudMessage(233); // turret energy: %d
                Owner.QueueHudMessage(128, 150, 1 / 1000f, 0, message.Replace("%d", _health.ToString()));
            }
            if (!_grounded)
            {
                // future: it would be cool to have the halfturret move with platforms, etc.
                Vector3 prevPos = Position;
                _ySpeed -= 0.02f / 2; // todo: FPS stuff
                Position = Position.AddY(_ySpeed / 2); // todo: FPS stuff
                var results = new CollisionResult[1];
                if (CollisionDetection.CheckSphereBetweenPoints(prevPos, Position, 0.45f, limit: 1,
                    includeOffset: false, TestFlags.None, _scene, results) > 0)
                {
                    CollisionResult result = results[0];
                    float dot = Vector3.Dot(Position, result.Plane.Xyz) - result.Plane.W;
                    dot = 0.45f - dot;
                    Position = result.Position + result.Plane.Xyz * dot;
                    _ySpeed = 0;
                    _grounded = true;
                    // todo?: wifi stuff
                }
                UpdateLightSources(Position);
                NodeRef = _scene.UpdateNodeRef(NodeRef, prevPos, Position);
                ClosestNode = null;
            }
            // todo?: wifi stuff
            Debug.Assert(_scene.Room != null);
            if (Position.Y < _scene.Room.Meta.KillHeight)
            {
                Die();
            }
            return true;
        }

        public void ResetGroundedState()
        {
            _grounded = false;
        }

        public static bool UpdateAim(Vector3 muzzlePos, Vector3 targetPos, EquipInfo equipInfo, out Vector3 aimVector)
        {
            WeaponInfo weapon = equipInfo.Weapon;
            float chargePct = 0;
            // todo: FPS stuff
            if (weapon.Flags.TestFlag(WeaponFlags.CanCharge) && equipInfo.ChargeLevel >= weapon.MinCharge * 2)
            {
                chargePct = (equipInfo.ChargeLevel - weapon.MinCharge * 2) / (weapon.FullCharge * 2 - weapon.MinCharge * 2);
            }
            aimVector = targetPos - muzzlePos;
            float hMagSqr = aimVector.X * aimVector.X + aimVector.Z * aimVector.Z;
            float hMag = MathF.Sqrt(hMagSqr);
            // todo?: seems okay without FPS stuff here, but we need to confirm
            float uncSpeed = Fixed.ToFloat(weapon.UnchargedSpeed);
            float speed = (Fixed.ToFloat(weapon.MinChargeSpeed) - uncSpeed) * chargePct;
            float uncGravity = Fixed.ToFloat(weapon.UnchargedGravity);
            float gravity = (Fixed.ToFloat(weapon.MinChargeGravity) - uncGravity) * chargePct;
            float v23 = hMagSqr * (uncGravity + gravity) / ((uncSpeed + speed) * (uncSpeed + speed));
            float v24 = v23 / 2;
            bool result = true;
            if (v24 >= 1 / 4096f || v24 <= -1 / 4096f) // v24 != 0
            {
                if ((Fixed.ToInt(v23) & 1) == 1)
                {
                    v23 -= 1 / 4096f;
                }
                float v26 = hMagSqr - 4 * v24 * (v24 - aimVector.Y);
                if (v26 > 0)
                {
                    aimVector.Y = (MathF.Sqrt(v26) - hMag) / v23 * hMag;
                }
                else if (v26 > -1 / 4096f) // v26 == 0
                {
                    aimVector.Y = -hMag / v23 * hMag;
                }
                else // v26 < 0
                {
                    aimVector.Y = hMag;
                    result = false;
                }
            }
            if (aimVector != Vector3.Zero)
            {
                aimVector = aimVector.Normalized();
            }
            else
            {
                aimVector = Vector3.UnitX;
            }
            return result;
        }

        public void Die()
        {
            Owner.OnHalfturretDied();
            if (_health > 0)
            {
                _health = 0;
                _scene.SpawnEffect(216, Vector3.UnitX, Vector3.UnitY, Position); // deathAlt
            }
        }

        public override void Destroy()
        {
            if (_burnEffect != null)
            {
                _scene.UnlinkEffectEntry(_burnEffect);
                _burnEffect = null;
            }
            base.Destroy();
        }

    }
}
