using System;
using System.Diagnostics;
using MphRead.Formats.Culling;
using MphRead.Formats;
using MphRead.Effects;
using OpenTK.Mathematics;

namespace MphRead.Entities
{
    public sealed class ForceFieldLockEntity : EntityBase
    {
        private Vector3 _vec1;
        private Vector3 _vec2;
        private Vector3 _fieldPosition;
        private Vector3 _targetPosition;
        private readonly ForceFieldEntity _forceField;
        private byte _shotFrames = 0;
        private EquipInfo? _equipInfo;
        private int _ammo = -1;
        private Vector3 _ownSpeed; // todo: revisit this?
        internal ushort _health = 1;
        internal ushort _timeSinceDamage = 510;
        private CollisionVolume _hurtVolumeInit;
        private CollisionVolume _hurtVolume;
        private Vector3 _prevPos;
        private Vector3 _speed;
        private readonly Effectiveness[] BeamEffectiveness = new Effectiveness[9];
        public ushort Health => _health;
        public CollisionVolume HurtVolume => _hurtVolume;
        public ForceFieldEntity Owner => _forceField;
        // The original damage flag remains enabled until removal, including same-frame lethal hits.
        public bool CanTakeAltAttackDamage => _forceField.Data.Type == 8;

        public Effectiveness GetEffectiveness(BeamType beam) => BeamEffectiveness[(int)beam];
        public void SetHealth(ushort health) => _health = health;
        public override bool GetTargetable() => _health != 0;
        public override void GetPosition(out Vector3 position) => position = _hurtVolume.GetCenter();
        public override void GetVectors(out Vector3 position, out Vector3 up, out Vector3 facing)
        {
            position = _hurtVolume.GetCenter(); up = UpVector; facing = FacingVector;
        }
        public override void Destroy()
        {
            _soundSource.StopAllSfx(force: true);
            base.Destroy();
        }
        public override bool Process()
        {
            if (_timeSinceDamage < 510) { _timeSinceDamage++; }
            if (_health == 0)
            {
                _scene.SendMessage(Message.Destroyed, this, _forceField, 0, 0);
                return false;
            }
            _prevPos = Position;
            Position += _speed;
            _hurtVolume = CollisionVolume.Transform(_hurtVolumeInit, Transform);
            _soundSource.Update(Position, 4);
            UpdateNodeRefVolume();
            foreach (PlayerEntity player in _scene.GetPlayerEntities())
            {
                if (player.Health == 0) { continue; }
                bool hit = player.CheckAltAttackHitForceField1(this);
                if (!hit) { hit = player.CheckAltAttackHitForceField2(this); }
                CollisionResult result = default;
                if (!hit && CollisionDetection.CheckVolumesOverlap(player.Volume, HurtVolume, ref result))
                {
                    player.HandleCollision(result);
                }
            }
            ProcessLock();
            _hurtVolume = CollisionVolume.Transform(_hurtVolumeInit, Transform);
            if (NodeRef != NodeRef.None) { NodeRef = _scene.UpdateNodeRef(NodeRef, _prevPos, Position); }
            // The lock advances animation here and in ProcessLock, matching the original wrapper.
            return base.Process();
        }
        public bool CheckHitByBomb(BombEntity bomb)
        {
            if ((Position - bomb.Position).LengthSquared > bomb.Radius * bomb.Radius) { return false; }
            TakeDamage(bomb.EnemyDamage, bomb);
            _scene.SendMessage(Message.Impact, bomb, bomb.Owner, this, 0);
            return true;
        }
        public void TakeDamage(uint damage, EntityBase? source)
        {
            BeamProjectileEntity? beam = source as BeamProjectileEntity;
            if (beam?.Owner is ForceFieldLockEntity) { return; }
            Effectiveness effectiveness = beam == null ? Effectiveness.Normal : GetEffectiveness(beam.Beam);
            bool unaffected = effectiveness == Effectiveness.Zero || source is BombEntity && _forceField.Data.Type != 8;
            bool dead = false;
            bool doubleDead = false;
            if (!unaffected)
            {
                if (beam?.Owner is PlayerEntity && damage == 0) { damage = 1; }
                if (damage >= _health)
                {
                    dead = true; doubleDead = _health == 0; _health = 0;
                }
                else { _health -= (ushort)damage; }
            }
            if (_health > 0)
            {
                if (beam != null) { LockHit(beam); }
            }
            else { _scene.SendMessage(Message.Unlock, this, _forceField, 0, 0); }
            if (unaffected)
            {
                if (effectiveness == Effectiveness.Zero)
                {
                    Matrix4 transform = GetTransformMatrix(Vector3.UnitX, Vector3.UnitY);
                    transform.Row3.Xyz = _hurtVolume.GetCenter();
                    EffectEntry? effect = _scene.SpawnEffectGetEntry(115, transform);
                    if (effect != null)
                    {
                        effect.SetReadOnlyField(0, 0.5f);
                        _scene.DetachEffectEntry(effect, setExpired: false);
                    }
                }
                return;
            }
            if (doubleDead && _scene.Features.Bugfixes.NoDoubleEnemyDeath) { return; }
            beam?.SpawnDamageEffect(effectiveness);
            if (dead)
            {
                _soundSource.StopAllSfx();
                PlayLockSfx(Metadata.ForceFieldLockDeathSfx, noUpdate: true);
                _scene.SpawnEffect(77, Transform.ClearScale());
            }
            else
            {
                _timeSinceDamage = 0;
                PlayLockSfx(Metadata.ForceFieldLockDamageSfx, noUpdate: false);
            }
        }
        private void PlayLockSfx(int sfx, bool noUpdate)
        {
            if (sfx == -1) { return; }
            float recency = -1;
            bool sourceOnly = false;
            if ((sfx & 0x20000) != 0) { recency = Single.MaxValue; sourceOnly = true; }
            else if ((sfx & 0x80000) != 0) { recency = 0; }
            _soundSource.PlaySfx(sfx & ~0xA0000, noUpdate: noUpdate, recency: recency, sourceOnly: sourceOnly);
        }

        public ForceFieldLockEntity(ForceFieldEntity forceField, NodeRef nodeRef, Scene scene)
            : base(EntityType.ForceFieldLock, nodeRef, scene)
        {
            _forceField = forceField;
        }

        public override void Initialize()
        {
            base.Initialize();
            Vector3 position = _forceField.Data.Header.Position.ToFloatVector();
            _fieldPosition = position;
            _vec1 = _forceField.Data.Header.UpVector.ToFloatVector();
            _vec2 = _forceField.Data.Header.FacingVector.ToFloatVector();
            position += _vec2 * Fixed.ToFloat(409);
            SetTransform(_vec2, _vec1, position);
            _health = 1;

            _hurtVolumeInit = new CollisionVolume(Vector3.Zero, 0.5f);
            ClearEffectiveness();
            switch (_forceField.Data.Type)
            {
            case 0:
                SetEffectiveness(BeamType.PowerBeam, Effectiveness.Normal);
                break;
            case 1:
                SetEffectiveness(BeamType.VoltDriver, Effectiveness.Normal);
                break;
            case 2:
                SetEffectiveness(BeamType.Missile, Effectiveness.Normal);
                break;
            case 3:
                SetEffectiveness(BeamType.Battlehammer, Effectiveness.Normal);
                break;
            case 4:
                SetEffectiveness(BeamType.Imperialist, Effectiveness.Normal);
                break;
            case 5:
                SetEffectiveness(BeamType.Judicator, Effectiveness.Normal);
                break;
            case 6:
                SetEffectiveness(BeamType.Magmaul, Effectiveness.Normal);
                break;
            case 7:
                SetEffectiveness(BeamType.ShockCoil, Effectiveness.Normal);
                break;
            case 8:
                break;
            }
            SetUpModel("ForceFieldLock");
            Recolor = _forceField.Recolor;
            _equipInfo = new EquipInfo(Weapons.ForceFieldLockWeapons[(int)_forceField.Data.Type], _scene.GetForceFieldLockProjectiles());
            _equipInfo.GetAmmo = () => _ammo;
            _equipInfo.SetAmmo = (newAmmo) => _ammo = newAmmo;
            _prevPos = Position;
        }

        private void ClearEffectiveness()
        {
            for (int i = 0; i < BeamEffectiveness.Length; i++)
            {
                BeamEffectiveness[i] = Effectiveness.Zero;
            }
        }

        private void SetEffectiveness(BeamType type, Effectiveness effectiveness)
        {
            int index = (int)type;
            Debug.Assert(index < BeamEffectiveness.Length);
            BeamEffectiveness[index] = effectiveness;
        }

        private void ProcessLock()
        {
            // this is called twice per tick, so the animation plays twice as fast
            if (Active)
            {
                for (int i = 0; i < _models.Count; i++)
                {
                    UpdateAnimFrames(_models[i]);
                }
            }
            if (Vector3.Dot(_scene.ViewPosition - _fieldPosition, _vec2) < 0)
            {
                _vec2 *= -1;
                Vector3 position = _fieldPosition + _vec2 * Fixed.ToFloat(409);
                SetTransform(_vec2, _vec1, position);
                _prevPos = Position;
            }
            if (_models[0].AnimInfo.Flags[0].TestFlag(AnimFlags.Ended))
            {
                if (_shotFrames > 0)
                {
                    _shotFrames--;
                    Debug.Assert(_equipInfo != null);
                    Vector3 spawnDir = (_targetPosition - Position).Normalized();
                    Vector3 spawnPos = Position + spawnDir * 0.1f;
                    BeamProjectileEntity.Spawn(this, _equipInfo, spawnPos, spawnDir, BeamSpawnFlags.None, NodeRef, _scene);
                }
                if (_shotFrames == 0)
                {
                    _models[0].SetAnimation(0);
                }
            }
            float width = _forceField.Width - 0.3f;
            float height = _forceField.Height - 0.3f;
            Vector3 between = Position - _fieldPosition;
            float rightPct = Vector3.Dot(between, _forceField.FieldRightVector) / width;
            float upPct = Vector3.Dot(between, _forceField.FieldUpVector) / height;
            // percentage of the lock's distance toward the "bounding oval"
            float pct = rightPct * rightPct + upPct * upPct;
            if (pct >= 1)
            {
                float dot1 = Vector3.Dot(between, _forceField.FieldFacingVector);
                between = (between - _forceField.FieldFacingVector * dot1).Normalized();
                float dot2 = Vector3.Dot(_ownSpeed, between) * 2;
                _ownSpeed -= between * dot2;
                float inv = 1 / MathF.Sqrt(pct);
                float rf = rightPct * inv * width;
                float uf = upPct * inv * height;
                Position = new Vector3(
                    _fieldPosition.X + _forceField.FieldRightVector.X * rf + _forceField.FieldUpVector.X * uf,
                    _fieldPosition.Y + _forceField.FieldRightVector.Y * rf + _forceField.FieldUpVector.Y * uf,
                    _fieldPosition.Z + _forceField.FieldRightVector.Z * rf + _forceField.FieldUpVector.Z * uf
                );
            }
            float magSqr = _ownSpeed.X * _ownSpeed.X + _ownSpeed.Y * _ownSpeed.Y + _ownSpeed.Z * _ownSpeed.Z;
            if (magSqr <= 0.0004f)
            {
                if (_shotFrames == 0)
                {
                    if (_models[0].AnimInfo.Index[0] == 1)
                    {
                        if (_models[0].AnimInfo.Frame[0] >= 10)
                        {
                            float randRight = _scene.Random.GetRandomInt2(0x666) / 4096f - 0.2f;
                            float randUp = _scene.Random.GetRandomInt2(0x666) / 4096f - 0.2f;
                            _ownSpeed = new Vector3(
                                _forceField.FieldUpVector.X * randUp + _forceField.FieldRightVector.X * randRight,
                                _forceField.FieldUpVector.Y * randUp + _forceField.FieldRightVector.Y * randRight,
                                _forceField.FieldUpVector.Z * randUp + _forceField.FieldRightVector.Z * randRight
                            );
                        }
                    }
                    else
                    {
                        _models[0].SetAnimation(1, AnimFlags.NoLoop);
                    }
                }
            }
            else if (_scene.FrameCount % 2 == 0) // todo: FPS stuff
            {
                _ownSpeed *= Fixed.ToFloat(3973);
            }
            _speed = _ownSpeed / 2; // todo: FPS stuff
        }

        public void LockHit(EntityBase source)
        {
            var beam = (BeamProjectileEntity)source;
            if (_shotFrames == 0 && GetEffectiveness(beam.Beam) == Effectiveness.Zero && beam.Owner != null && beam.Owner == _scene.LocalPlayer)
            {
                _shotFrames = _forceField.Data.Type == 7 ? (byte)SimTicks.Hz : (byte)1;
                beam.Owner.GetPosition(out _targetPosition);
                _models[0].SetAnimation(2, AnimFlags.NoLoop);
            }
        }
    }
}
