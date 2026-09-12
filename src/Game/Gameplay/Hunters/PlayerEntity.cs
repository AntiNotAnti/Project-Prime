using System;
using System.Collections.Generic;
using System.Diagnostics;
using MphRead.Effects;
using MphRead.Formats;
using MphRead.Formats.Culling;
using MphRead.Mods.Network;
using MphRead.Text;
using OpenTK.Mathematics;

namespace MphRead.Entities
{
    [Flags]
    public enum LoadFlags : byte
    {
        None = 0,
        Connected = 0x1,
        WasConnected = 0x2,
        Disconnected = 0x4,
        Initial = 0x8,
        Unknown4 = 0x10,
        Active = 0x20,
        SlotActive = 0x40,
        Spawned = 0x80
    }

    public class AvailableArray
    {
        private readonly bool[] _array = new bool[9];

        public void ClearAll()
        {
            for (int i = 0; i < _array.Length; i++)
            {
                _array[i] = false;
            }
        }

        public void SetAll()
        {
            for (int i = 0; i < _array.Length; i++)
            {
                _array[i] = true;
            }
        }

        public void Set(ushort value)
        {
            for (int i = 0; i < 8; i++)
            {
                _array[i] = (value & (1 << i)) != 0;
            }
        }

        public void CopyFrom(AvailableArray source)
        {
            for (int i = 0; i < _array.Length; i++)
            {
                _array[i] = source[i];
            }
        }

        public bool this[int key]
        {
            get => _array[key];
            set => _array[key] = value;
        }

        public bool this[BeamType key]
        {
            get => _array[(int)key];
            set => _array[(int)key] = value;
        }
    }

    public enum PlayerAnimation : sbyte
    {
        None = -1,
        Morph = 0,
        Flourish = 1,
        WalkForward = 2,
        Unmorph = 3,
        DamageBack = 4,
        DamageFront = 5,
        DamageLeft = 6,
        DamageRight = 7,
        Idle = 8,
        LandNeutral = 9,
        LandLeft = 10,
        LandRight = 11,
        JumpNeutral = 12,
        JumpBack = 13,
        JumpForward = 14,
        JumpLeft = 15,
        JumpRight = 16,
        Unused17 = 17,
        WalkBackward = 18,
        Spawn = 19,
        WalkLeft = 20,
        WalkRight = 21,
        Turn = 22,
        Charge = 23,
        ChargeShoot = 24,
        Shoot = 25
    }

    public enum GunAnimation : byte
    {
        FullCharge = 0,
        ChargeShot = 1,
        Charging = 2,
        Idle = 3,
        Switch = 4,
        FullChargeMissile = 5,
        ChargingMissile = 6,
        MissileClose = 7,
        MissileOpen = 8,
        Unknown9 = 9,
        MissileShot = 10,
        UpDown = 11,
        Shot = 12
    }

    public enum TraceAltAnim : byte
    {
        Idle = 0,
        Attack = 1,
        MoveLeft = 2,
        MoveForward = 3,
        MoveRight = 4,
        MoveBackward = 5
    }

    public enum WeavelAltAnim : byte
    {
        Idle = 0,
        Attack = 1,
        MoveLeft = 2,
        MoveForward = 3,
        MoveRight = 4,
        Turn = 5,
        MoveBackward = 6
    }

    public enum KandenAltAnim : byte
    {
        Idle = 0,
        TailOut = 1,
        TailIn = 2
    }

    public enum NoxusAltAnim : byte
    {
        Extend = 0
    }

    public enum SyluxAltAnim : byte
    {
        Idle = 0
    }

    public enum SpireAltAnim : byte
    {
        Attack = 0
    }

    public partial class PlayerEntity : DynamicLightEntityBase
    {
        internal readonly ModelInstance[] _bipedModelLods = new ModelInstance[2];
        internal ModelInstance _bipedModel1 = null!; // legs
        internal ModelInstance _bipedModel2 = null!; // torso
        public ModelInstance BipedModel2 => _bipedModel2;
        internal ModelInstance _altModel = null!;
        internal ModelInstance _gunModel = null!;
        internal ModelInstance _gunSmokeModel = null!;
        internal ModelInstance _doubleDmgModel = null!;
        public ModelInstance DoubleDamageModel => _doubleDmgModel;
        internal ModelInstance _altIceModel = null!;
        internal ModelInstance _bipedIceModel = null!;
        internal ModelInstance _trailModel = null!;
        internal ModelInstance _octolithSimpleModel = null!;
        internal readonly Matrix4[] _bipedIceTransforms = new Matrix4[19];

        // todo?: could save space with a union
        private readonly Node?[] _spireAltNodes = new Node?[4];
        internal Vector3 _spireRockPosL; // positions after animation
        internal Vector3 _spireRockPosR;
        internal Vector3 _spireAltFacing;
        internal Vector3 _spireAltUp;
        private readonly Vector3[] _spireAltVecs = new Vector3[16];
        private readonly Vector3[] _kandenSegPos = new Vector3[5];
        internal readonly Matrix4[] _kandenSegMtx = new Matrix4[5];
        public IReadOnlyList<Vector3> KandenSegPos => _kandenSegPos;
        public byte SyluxBombCount { get; set; } = 0;
        public BombEntity?[] SyluxBombs { get; } = new BombEntity?[3];
        private uint _lockjawBombGeneration;

        internal void ResetLockjawBombState()
        {
            for (int i = 0; i < SyluxBombs.Length; i++)
            {
                BombEntity? bomb = SyluxBombs[i];
                if (bomb != null && ReferenceEquals(bomb.Owner, this))
                {
                    bomb.BombIndex = -1;
                }
                SyluxBombs[i] = null;
            }
            SyluxBombCount = 0;
            _lockjawBombGeneration = unchecked(_lockjawBombGeneration + 1);
        }

        internal bool TryRegisterLockjawBomb(BombEntity bomb)
        {
            ArgumentNullException.ThrowIfNull(bomb);
            ValidateLockjawBombState();
            if (SyluxBombCount >= SyluxBombs.Length
                || bomb.BombType != BombType.Lockjaw
                || !ReferenceEquals(bomb.Owner, this)
                || !ReferenceEquals(bomb.Scene, _scene)
                || !bomb.IsLive)
            {
                return false;
            }
            for (int i = 0; i < SyluxBombCount; i++)
            {
                if (ReferenceEquals(SyluxBombs[i], bomb))
                {
                    return false;
                }
            }
            int index = SyluxBombCount;
            SyluxBombs[index] = bomb;
            bomb.BombIndex = index;
            bomb.LockjawRegistryGeneration = _lockjawBombGeneration;
            SyluxBombCount = checked((byte)(index + 1));
            return true;
        }

        internal void UnregisterLockjawBomb(BombEntity bomb)
        {
            ArgumentNullException.ThrowIfNull(bomb);
            ValidateLockjawBombState();
            int index = bomb.BombIndex;
            if (index < 0 || index >= SyluxBombCount
                || !ReferenceEquals(SyluxBombs[index], bomb)
                || bomb.LockjawRegistryGeneration != _lockjawBombGeneration)
            {
                return;
            }
            int count = SyluxBombCount;
            for (int i = index; i < count - 1; i++)
            {
                BombEntity moved = SyluxBombs[i + 1]!;
                SyluxBombs[i] = moved;
                moved.BombIndex = i;
            }
            SyluxBombs[count - 1] = null;
            SyluxBombCount = checked((byte)(count - 1));
            bomb.BombIndex = -1;
        }

        internal bool ValidateLockjawBombState()
        {
            int originalCount = SyluxBombCount;
            bool valid = originalCount <= SyluxBombs.Length;
            int count = 0;
            for (int read = 0; read < SyluxBombs.Length; read++)
            {
                BombEntity? bomb = SyluxBombs[read];
                if (bomb == null)
                {
                    if (read < originalCount) valid = false;
                    continue;
                }
                bool duplicate = false;
                for (int i = 0; i < count; i++)
                {
                    if (ReferenceEquals(SyluxBombs[i], bomb))
                    {
                        duplicate = true;
                        break;
                    }
                }
                bool accepted = !duplicate
                    && bomb.BombType == BombType.Lockjaw
                    && ReferenceEquals(bomb.Owner, this)
                    && ReferenceEquals(bomb.Scene, _scene)
                    && bomb.IsLive
                    && bomb.LockjawRegistryGeneration == _lockjawBombGeneration;
                if (!accepted)
                {
                    valid = false;
                    if (!duplicate && ReferenceEquals(bomb.Owner, this)) bomb.BombIndex = -1;
                    continue;
                }
                if (read != count || bomb.BombIndex != count) valid = false;
                SyluxBombs[count] = bomb;
                bomb.BombIndex = count;
                count++;
            }
            for (int i = count; i < SyluxBombs.Length; i++)
            {
                SyluxBombs[i] = null;
            }
            if (originalCount != count) valid = false;
            SyluxBombCount = checked((byte)count);
            return valid;
        }

        internal bool IsRegisteredLockjawBomb(BombEntity bomb)
        {
            int index = bomb.BombIndex;
            return index >= 0 && index < SyluxBombCount && index < SyluxBombs.Length
                && ReferenceEquals(SyluxBombs[index], bomb)
                && bomb.LockjawRegistryGeneration == _lockjawBombGeneration;
        }

        internal BombEntity[] GetRegisteredLockjawBombs()
        {
            ValidateLockjawBombState();
            var result = new BombEntity[SyluxBombCount];
            for (int i = 0; i < result.Length; i++) result[i] = SyluxBombs[i]!;
            return result;
        }

        public const int SlotCapacity = 8;
        public Scene Scene => _scene;
        public bool IsMainPlayer => ReferenceEquals(this, _scene.LocalPlayer) && _scene.ControlsPlayer;

        private const int UA = 0;
        private const int Missiles = 1;

        internal int _healthMax = 0;
        internal int _health = 0;
        private int _healthRecovery = 0;
        private bool _tickedHealthRecovery = false; // used to update health every other frame
        internal readonly int[] _ammoMax = new int[2];
        internal readonly int[] _ammo = new int[2];
        private readonly int[] _ammoRecovery = new int[2];
        private readonly bool[] _tickedAmmoRecovery = new bool[2];
        public int Health { get => _health; set => _health = value; }
        public int HealthMax => _healthMax;
        private readonly BeamType[] _weaponSlots = new BeamType[3];
        private BeamType _pendingAutoEquipWeapon = BeamType.None;
        internal readonly AvailableArray _availableWeapons = new AvailableArray();
        public AvailableArray AvailableWeapons => _availableWeapons;
        private readonly AvailableArray _availableCharges = new AvailableArray();
        internal AbilityFlags _abilities;
        internal readonly BeamProjectileEntity[] _beams;
        public EquipInfo EquipInfo { get; } = new EquipInfo();
        private WeaponInfo EquipWeapon => EquipInfo.Weapon;
        public BeamType CurrentWeapon { get; private set; }
        public BeamType PreviousWeapon { get; private set; }
        public BeamType WeaponSelection { get; internal set; }
        public readonly Effectiveness[] BeamEffectiveness = new Effectiveness[9];
        public GunAnimation GunAnimation { get; private set; }
        private ushort _bombCooldown = 0;
        private ushort _bombRefillTimer = 0;
        internal byte _bombAmmo = 0;
        private ushort _bombOveruse = 0;
        private ushort _boostCharge = 0;
        private ushort _boostDamage = 0;
        internal ushort _altAttackCooldown = 0;
        private ushort _altAttackTime = 0;
        private float _altSpinSpeed = 0;

        public Team Team { get; set; } = Team.None;
        public int TeamIndex { get; set; } = -1;
        public int SlotIndex { get; private set; }
        public PlayerMatchStats MatchStats => _scene.Match.Players[SlotIndex];
        public bool IsBot { get; set; }
        public LoadFlags LoadFlags { get; set; }
        public Hunter Hunter { get; private set; }
        public PlayerValues Values { get; private set; }
        public bool IsPrimeHunter => SlotIndex == _scene.Match.PrimeHunter;

        internal const int _mbTrailSegments = 9 * 2;
        internal readonly Matrix4[] _mbTrailMatrices = new Matrix4[_mbTrailSegments];
        internal readonly float[] _mbTrailAlphas = new float[_mbTrailSegments];
        internal int _mbTrailIndex;
        internal Matrix4 _modelTransform = Matrix4.Identity;

        // Render-only generation used to fence skeletal pose history across
        // slot/life/form transitions.  It is deliberately independent of the
        // per-tick locomotion animation updates.
        private uint _presentationPoseEpoch;
        internal uint PresentationPoseEpoch => _presentationPoseEpoch;
        internal void AdvancePresentationPoseEpoch()
            => _presentationPoseEpoch = unchecked(_presentationPoseEpoch + 1);

        // todo: visualize
        internal CollisionVolume _volumeUnxf; // todo: names
        internal CollisionVolume _volume;
        public CollisionVolume Volume => _volume;

        internal Vector3 _facingVector;
        internal Vector3 _upVector;
        public override Vector3 FacingVector => _facingVector;
        public override Vector3 UpVector => _upVector;
        internal Vector3 _gunVec1; // facing? (aim?)
        internal Vector3 _gunVec2; // right? (turn?)
        internal Vector3 _aimPosition;
        private float _gunViewBob = 0;
        private float _walkViewBob = 0;
        internal Vector3 _muzzlePos;
        internal Vector3 _gunDrawPos;

        internal Vector3 _aimVec;

        // something alt form angle related
        internal float _field70 = 0;
        internal float _field74 = 0;
        public float Field70 => _field70;
        public float Field74 => _field74;
        private float _field78 = 0;
        private float _field7C = 0;
        private float _field80 = 0;
        private float _field84 = 0;

        private float _aimY = 0;
        private float _field40C = 0; // view sway percentage
        private Vector3 _field410;
        private Vector3 _field41C;
        private Vector3 _field428;

        private ushort _timeIdle = 0;
        private byte _crushBits = 0;
        private Vector3 _field4E8; // stores gun vec 2
        private float _altTiltX = 0;
        private float _altTiltZ = 0;
        private float _altSpinRot = 0;
        private float _altWobble = 0;
        private byte _field551 = 0;
        private byte _field552 = 0;
        private byte _field553 = 0;
        private float _viewTiltAngleH = 0;
        private float _viewTiltAngleV = 0;
        internal bool _field6D0 = false; //  todo: unused?
        public bool Field6D0 => _field6D0; //  todo: unused?
        private float _altRollFbX = 0; // set from other fields when entering alt form
        private float _altRollFbZ = 0;
        private float _altRollLrX = 0;
        private float _altRollLrZ = 0;

        private HalfturretEntity _halfturret = null!;
        public HalfturretEntity Halfturret => _halfturret;
        private EntityBase? _field35C = null;
        public MorphCameraEntity? MorphCamera { get; set; }
        public OctolithFlagEntity? OctolithFlag { get; set; }
        private JumpPadEntity? _lastJumpPad = null;
        private EntityBase? _burnedBy = null;
        public EntityBase? BurnedBy => _burnedBy;
        private EntityBase? _lastTarget = null;
        private EntityBase? _shockCoilTarget = null;
        // Shock Coil's shot animation is a held state. Keep this separate
        // from GunAnimation so entering it does not restart the authored
        // material/texture tracks every simulation tick.
        private bool _shockCoilAnimationActive;
        public EntityBase? ShockCoilTarget => _shockCoilTarget;

        public bool IsAltForm => Flags1.TestFlag(PlayerFlags1.AltForm);
        public bool IsMorphing => Flags1.TestFlag(PlayerFlags1.Morphing);
        public bool IsUnmorphing => Flags1.TestFlag(PlayerFlags1.Unmorphing);
        public PlayerFlags1 Flags1 { get; private set; }
        public PlayerFlags2 Flags2 { get; internal set; }
        public Vector3 Speed { get; set; }
        public Vector3 Acceleration { get; set; }
        public Vector3 PrevSpeed { get; set; }
        public Vector3 PrevPosition { get; set; }
        public Vector3 IdlePosition { get; private set; }
        private ushort _accelerationTimer = 0;
        private float _hSpeedCap = 0;
        internal float _hSpeedMag = 0; // todo: all FPS stuff with speed
        private Vector2 _analogMovement;
        private Vector2 _digitalMovementBeforeAnalog;
        private Vector2 _digitalRollBeforeAnalog;
        private InputButtons _digitalMovementButtonsBeforeAnalog;
        private InputButtons _digitalRollButtonsBeforeAnalog;
        private InputButtons _digitalMovementPressedBeforeAnalog;
        private InputButtons _digitalRollPressedBeforeAnalog;
        private bool _analogMovementPresent;
        internal bool AnalogMovementPresent => _analogMovementPresent;
        internal Vector2 AnalogMovement => _analogMovement;
        internal Vector2 DigitalMovementBeforeAnalog => _digitalMovementBeforeAnalog;
        internal Vector2 DigitalRollBeforeAnalog => _digitalRollBeforeAnalog;
        internal InputButtons DigitalMovementButtonsBeforeAnalog => _digitalMovementButtonsBeforeAnalog;
        internal InputButtons DigitalRollButtonsBeforeAnalog => _digitalRollButtonsBeforeAnalog;
        internal InputButtons DigitalMovementPressedBeforeAnalog => _digitalMovementPressedBeforeAnalog;
        internal InputButtons DigitalRollPressedBeforeAnalog => _digitalRollPressedBeforeAnalog;

        /// <summary>
        /// Publishes the optional radial controller sample for this input
        /// tick. The digital intent is captured before controller binds are
        /// ORed in, so keyboard/touch/bot movement keeps its existing path.
        /// </summary>
        internal void SetAnalogMovement(Vector2 movement, Vector2 digitalBeforeAnalog,
            Vector2 digitalRollBeforeAnalog, InputButtons digitalButtons = InputButtons.None,
            InputButtons digitalRollButtons = InputButtons.None,
            InputButtons digitalPressed = InputButtons.None,
            InputButtons digitalRollPressed = InputButtons.None)
        {
            if (!AnalogMovementCodec.TryQuantize(movement,
                out sbyte moveX, out sbyte moveY)
                || !AnalogMovementCodec.TryDecode(moveX, moveY, present: true,
                    out Vector2 decodedMovement))
            {
                ClearAnalogMovement();
                return;
            }
            // Use the same rounded value that is sent over the wire so a
            // predicted local tick and its authoritative replay share the
            // exact movement vector.
            _analogMovement = decodedMovement;
            _digitalMovementBeforeAnalog = new Vector2(
                Math.Clamp(digitalBeforeAnalog.X, -1, 1),
                Math.Clamp(digitalBeforeAnalog.Y, -1, 1));
            _digitalRollBeforeAnalog = new Vector2(
                Math.Clamp(digitalRollBeforeAnalog.X, -1, 1),
                Math.Clamp(digitalRollBeforeAnalog.Y, -1, 1));
            _digitalMovementButtonsBeforeAnalog = digitalButtons;
            _digitalRollButtonsBeforeAnalog = digitalRollButtons;
            _digitalMovementPressedBeforeAnalog = digitalPressed;
            _digitalRollPressedBeforeAnalog = digitalRollPressed;
            _analogMovementPresent = true;
        }

        internal void SetAnalogMovement(Vector2 movement, Vector2 digitalBeforeAnalog)
            => SetAnalogMovement(movement, digitalBeforeAnalog, digitalBeforeAnalog);

        internal void ClearAnalogMovement()
        {
            _analogMovement = Vector2.Zero;
            _digitalMovementBeforeAnalog = Vector2.Zero;
            _digitalRollBeforeAnalog = Vector2.Zero;
            _digitalMovementButtonsBeforeAnalog = InputButtons.None;
            _digitalRollButtonsBeforeAnalog = InputButtons.None;
            _digitalMovementPressedBeforeAnalog = InputButtons.None;
            _digitalRollPressedBeforeAnalog = InputButtons.None;
            _analogMovementPresent = false;
        }

        /// <summary>
        /// Combines a digital axis with an optional analog axis. A held
        /// digital component is never weakened by an opposing fractional pad
        /// value; same-direction values are bounded to the existing full
        /// acceleration. With no digital component, the radial value is used
        /// directly for fine movement.
        /// </summary>
        internal static float ResolveAnalogMovementAxis(float digitalAxis,
            float analogAxis, bool analogPresent)
        {
            if (!analogPresent || !float.IsFinite(digitalAxis)
                || !float.IsFinite(analogAxis))
            {
                return digitalAxis;
            }
            digitalAxis = Math.Clamp(digitalAxis, -1, 1);
            analogAxis = Math.Clamp(analogAxis, -1, 1);
            if (MathF.Abs(digitalAxis) <= 0.0001f)
            {
                return analogAxis;
            }
            float combined = digitalAxis + analogAxis;
            if (MathF.Abs(combined) < MathF.Abs(digitalAxis))
            {
                return digitalAxis;
            }
            return Math.Clamp(combined, -1, 1);
        }

        internal float ResolveMovementAxis(float digitalAxis, float analogAxis,
            bool horizontal, bool roll = false)
        {
            if (!_analogMovementPresent) return digitalAxis;
            Vector2 digital = roll ? _digitalRollBeforeAnalog : _digitalMovementBeforeAnalog;
            return ResolveAnalogMovementAxis(horizontal ? digital.X : digital.Y,
                analogAxis, analogPresent: true);
        }

        private float _gravity = 0;
        private int _slipperiness = 0; // from stand_ter_flags
        internal Terrain _standTerrain; // from stand_ter_flags
        private bool _terrainDamage = false; // from touch_ter_flags

        private PlayerAnimation Biped1Anim => (PlayerAnimation)_bipedModel1.AnimInfo.Index[0];
        private PlayerAnimation Biped2Anim => (PlayerAnimation)_bipedModel2.AnimInfo.Index[0];
        private int Biped1Frame => _bipedModel1.AnimInfo.Frame[0];
        private int Biped2Frame => _bipedModel2.AnimInfo.Frame[0];
        private int Biped1FrameCount => _bipedModel1.AnimInfo.FrameCount[0];
        private int Biped2FrameCount => _bipedModel2.AnimInfo.FrameCount[0];
        private AnimFlags Biped1Flags
        {
            get => _bipedModel1.AnimInfo.Flags[0];
            set => _bipedModel1.AnimInfo.Flags[0] = value;
        }
        private AnimFlags Biped2Flags
        {
            get => _bipedModel2.AnimInfo.Flags[0];
            set => _bipedModel2.AnimInfo.Flags[0] = value;
        }

        private ushort _jumpPadControlLock = 0;
        private ushort _jumpPadControlLockMin = 0;
        private ushort _timeSinceJumpPad = 0;
        private Vector3 _jumpPadAccel;

        public const ushort RespawnTime = 3 * SimTicks.Hz;

        private ushort _autofireCooldown = 0;
        private ushort _powerBeamAutofire = 0;
        internal ushort _timeSinceInput = 0;
        private ushort _timeSinceShot = 0;
        public ushort TimeSinceShot { get => _timeSinceShot; set => _timeSinceShot = value; }
        internal ushort _timeSinceDamage = 0;
        internal ushort _timeSincePickup = 0;
        internal ushort _timeSinceHeal = 0;
        internal ushort _respawnTimer = 0;
        public ushort RespawnTimer { get => _respawnTimer; set => _respawnTimer = value; }
        private ushort _damageInvulnTimer = 0;
        private ushort _spawnInvulnTimer = 0;
        internal ushort _camSwitchTimer = 0;
        internal ushort _doubleDmgTimer = 0;
        public bool DoubleDamage => _doubleDmgTimer > 0;
        internal ushort _cloakTimer = 0;
        private ushort _deathaltTimer = 0;
        private ushort _frozenTimer = 0;
        internal ushort _frozenGfxTimer = 0;
        internal bool _drawIceLayer = false;
        internal ushort _disruptedTimer = 0;
        private ushort _burnTimer = 0;
        private ushort _timeSinceFrozen = 0;
        private ushort _timeSinceDead = 0;
        private ushort _hidingTimer = 0;
        private ushort _timeStanding = 0;
        private ushort _timeSinceStanding = 0;
        private ushort _timeSinceGrounded = 0;
        internal ushort _timeBeforeLanding = 0;
        private Vector3 _fieldC0;
        private ushort _field449 = 0;
        private float _field44C = 0; // basically landing speed/force?
        private ushort _timeSinceHitTarget = 0;
        private ushort _shockCoilTimer = 0;
        public ushort ShockCoilTimer => _shockCoilTimer;
        private ushort _timeSinceMorphCamera = 0;
        private ushort _horizColTimer = 0;

        private EffectEntry? _deathaltEffect = null;
        private EffectEntry? _doubleDmgEffect = null;
        private EffectEntry? _burnEffect = null;
        private EffectEntry? _furlEffect = null;
        private EffectEntry? _boostEffect = null;
        internal EffectEntry? _muzzleEffect = null;
        internal EffectEntry? _chargeEffect = null;

        internal float _curAlpha = 1;
        public float CurAlpha => _curAlpha;
        private float _targetAlpha = 1;
        internal float _smokeAlpha = 0;

        // debug/viewer
        public bool IgnoreItemPickups { get; set; }
        public Vector3? ForcedSpawnPos { get; set; }

        internal PlayerEntity(int slotIndex, Scene scene) : base(EntityType.Player, scene)
        {
            SlotIndex = slotIndex;
            _beams = SceneSetup.CreateBeamList(16, scene); // in-game: 5
            AiData = new PlayerAiData(this);
        }

        internal void PrepareSlot(Hunter hunter, int recolor)
        {
            PlayerEntity player = this;
            player._aimAssist.Reset();
            player.ResetLockjawBombState();
            if (player.Hunter != hunter) player.AdvancePresentationPoseEpoch();
            player.Hunter = hunter;
            player.Recolor = recolor;
            player.ResetRemoteLocomotion();
            if (player.IsBot)
            {
                // todo: update controls
            }
            player.LoadFlags |= LoadFlags.SlotActive;
            player.LoadFlags &= ~LoadFlags.Spawned;
            player.CreateHalfturret();
        }

        public void CreateHalfturret()
        {
            if (_halfturret == null)
            {
                _halfturret = new HalfturretEntity(this, _scene);
                _halfturret.Create();
                _scene.InitEntity(_halfturret);
            }
        }

        // for spawning after boss intro movie
        public bool ReloadInit { get; set; }

        public override void Initialize()
        {
            ResetLockjawBombState();
            Vector3 prevPos = _position;
            Vector3 prevUp = _upVector;
            Vector3 prevFacing = _facingVector;
            NodeRef prevNodeRef = NodeRef;
            int prevHealth = _health;
            _models.Clear();
            _bipedModelLods[0] = Read.GetModelInstance(Metadata.HunterModels[Hunter][0]);
            _bipedModelLods[1] = Read.GetModelInstance(Metadata.HunterModels[Hunter][1]);
            _bipedModel1 = Read.GetModelInstance(Metadata.HunterModels[Hunter][0]);
            _bipedModel2 = Read.GetModelInstance(Metadata.HunterModels[Hunter][0]);
            _altModel = Read.GetModelInstance(Metadata.HunterModels[Hunter][2]);
            _gunModel = Read.GetModelInstance(Metadata.HunterModels[Hunter][3]);
            _models.Add(_bipedModel1);
            _models.Add(_bipedModel2);
            _models.Add(_altModel);
            _models.Add(_gunModel);
            Presentation?.LoadPresentationModels();
            base.Initialize();
            EquipInfo.Beams = _beams;
            Values = Metadata.PlayerValues[(int)Hunter];
            InitializePresentation();
            _healthMax = 2 * Values.EnergyTank - 1;
            _ammoMax[UA] = _ammoMax[Missiles] = Values.MpAmmoCap;
            InitializeWeapon();
            _pendingAutoEquipWeapon = BeamType.None;
            _availableWeapons[BeamType.PowerBeam] = true;
            TryEquipWeapon(BeamType.PowerBeam, silent: true);
            _facingVector = -Vector3.UnitZ;
            _upVector = Vector3.UnitY;
            SetTransform(_facingVector, _upVector, Vector3.Zero);
            Speed = Vector3.Zero;
            _gunVec1 = -Vector3.UnitZ;
            _gunVec2 = -Vector3.UnitX;
            _volumeUnxf = PlayerVolumes[(int)Hunter, 0];
            _volume = CollisionVolume.Move(_volumeUnxf, Position);
            _aimPosition = (Position + _gunVec1 * Fixed.ToFloat(Values.AimDistance)).AddY(Fixed.ToFloat(Values.AimYOffset));
            _aimY = 0;
            _gunViewBob = 0;
            _walkViewBob = 0;
            _health = 0;
            Flags2 |= PlayerFlags2.HideModel;
            ClearAnalogMovement();
            _field35C = null;
            EquipInfo.ChargeLevel = 0;
            _timeSinceShot = 255;
            _timeSinceDamage = 255;
            _timeSincePickup = 255;
            _timeSinceHeal = 255;
            _timeSinceStanding = 0;
            _field449 = 0;
            _respawnTimer = 0;
            _timeSinceDead = 0;
            _field551 = 255;
            _field552 = 0;
            _field553 = 0;
            _bombCooldown = 0;
            _bombRefillTimer = 0;
            _bombAmmo = 3;
            _damageInvulnTimer = 0;
            _spawnInvulnTimer = 0;
            _abilities = AbilityFlags.None;
            if (TeamIndex == -1)
            {
                TeamIndex = SlotIndex;
            }
            _field4E8 = Vector3.Zero;
            _modelTransform = Matrix4.Identity;
            _camSwitchTimer = (ushort)SimTicks.From30HzFrames(Values.CamSwitchTime);
            CameraInfo.Reset();
            CameraInfo.Position = Position;
            CameraInfo.UpVector = Vector3.UnitY;
            CameraInfo.Target = Position + _facingVector;
            CameraInfo.NodeRef = NodeRef.None;
            NodeRef = NodeRef.None;
            _timeIdle = 0;
            _timeSinceInput = 0;
            _field40C = 0;
            _doubleDmgTimer = 0;
            OctolithFlag = null;
            ResetMorphBallTrail();
            // todo?: point module
            if (Hunter == Hunter.Spire)
            {
                _spireAltNodes[0] = _altModel.Model.GetNodeByName("L_Rock01");
                _spireAltNodes[1] = _altModel.Model.GetNodeByName("R_Rock01");
                _spireAltNodes[2] = _altModel.Model.GetNodeByName("R_POS_ROT"); // parent of 0
                _spireAltNodes[3] = _altModel.Model.GetNodeByName("R_POS_ROT_1"); // parent of 1
            }
            else
            {
                _spireAltNodes[0] = null;
                _spireAltNodes[1] = null;
                _spireAltNodes[2] = null;
                _spireAltNodes[3] = null;
            }
            for (int i = 0; i < _bipedIceTransforms.Length; i++)
            {
                _bipedIceTransforms[i] = Matrix4.Identity;
            }
            if (ReloadInit)
            {
                // the game preserves a node name string across movie load, and there is functionality to specify it
                // when starting the movie, but that value is ignored and rmMain is always set to be preserved instead
                // on the other hand, we could be reloading a room without rmMain, so fall back to the previous value
                NodeRef nodeRef = _scene.GetNodeRefByName("rmMain");
                if (nodeRef == NodeRef.None)
                {
                    nodeRef = prevNodeRef;
                }
                Spawn(prevPos, prevFacing, prevUp, nodeRef, respawn: false);
                _health = prevHealth;
                Flags1 |= PlayerFlags1.Grounded;
                Flags1 |= PlayerFlags1.GroundedPrevious;
                ReloadInit = false;
            }
        }

        public void Spawn(Vector3 pos, Vector3 facing, Vector3 up, NodeRef nodeRef, bool respawn)
        {
            _aimAssist.Reset();
            Input.ClearBoostIntents();
            ResetLockjawBombState();
            AdvancePresentationPoseEpoch();
            LoadFlags |= LoadFlags.Spawned;
            if (IsMainPlayer)
            {
                UpdateDoubleDamageSfx(index: 0, play: false);
                UpdateCloakSfx(index: 0, play: false);
            }
            _abilities = AbilityFlags.AltForm;
            if (Hunter == Hunter.Samus)
            {
                _abilities |= AbilityFlags.Bombs;
                _abilities |= AbilityFlags.Boost;
            }
            else if (Hunter == Hunter.Kanden)
            {
                _abilities |= AbilityFlags.Bombs;
                for (int i = 0; i < _kandenSegPos.Length; i++)
                {
                    _kandenSegPos[i] = Vector3.Zero;
                }
            }
            else if (Hunter == Hunter.Trace)
            {
                _abilities |= AbilityFlags.TraceAltAttack;
            }
            else if (Hunter == Hunter.Sylux)
            {
                _abilities |= AbilityFlags.Bombs;
            }
            else if (Hunter == Hunter.Noxus)
            {
                _abilities |= AbilityFlags.NoxusAltAttack;
            }
            else if (Hunter == Hunter.Spire)
            {
                _abilities |= AbilityFlags.SpireAltAttack;
                _spireRockPosL = pos;
                _spireRockPosR = pos;
                _spireAltFacing = Vector3.UnitY;
                _spireAltUp = Vector3.UnitX;
                for (int i = 0; i < _spireAltVecs.Length; i++)
                {
                    _spireAltVecs[i] = Vector3.Zero;
                }
            }
            else if (Hunter == Hunter.Weavel)
            {
                _abilities |= AbilityFlags.WeavelAltAttack;
            }
            _health = Values.EnergyTank - 1;
            ClearAnalogMovement();
            // todo?: a lot of this doesn't need to be set at all in create/init when it gets set every time you spawn anyway
            _availableWeapons.ClearAll();
            _availableCharges.ClearAll();
            InitializeWeapon();
            // todo: much of this is the same as what's done in init, so we could use a common method
            EquipInfo.InfiniteAmmo = false;
            EquipInfo.ChargeLevel = 0;
            EquipInfo.SmokeLevel = 0;
            _doubleDmgTimer = 0;
            _cloakTimer = 0;
            _deathaltTimer = 0;
            _pendingAutoEquipWeapon = BeamType.None;
            PreviousWeapon = BeamType.PowerBeam;
            TryEquipWeapon(BeamType.PowerBeam, silent: true);
            Metadata.LoadEffectiveness(0x2AAAA, BeamEffectiveness);
            _frozenTimer = 0;
            _timeSinceFrozen = 255;
            _frozenGfxTimer = 0;
            _drawIceLayer = false;
            _hidingTimer = 0;
            _curAlpha = 1;
            _targetAlpha = 1;
            _disruptedTimer = 0;
            _burnedBy = null;
            _burnTimer = 0;
            _hSpeedCap = Fixed.ToFloat(Values.WalkSpeedCap); // todo: FPS stuff?
            Speed = Vector3.Zero;
            if (respawn)
            {
                pos = pos.AddY(1);
            }
            _upVector = up;
            _facingVector = facing;
            SetTransform(_facingVector, _upVector, pos);
            PrevPosition = Position;
            IdlePosition = Position;
            _gunVec2 = Vector3.Cross(up, facing).Normalized();
            _gunVec1 = facing;
            float hMag = MathF.Sqrt(facing.X * facing.X + facing.Z * facing.Z);
            _field70 = facing.X / hMag;
            _field74 = facing.Z / hMag;
            _field78 = _field74;
            _field7C = -_field70;
            _field80 = _field70;
            _field84 = _field74;
            _aimPosition = (Position + _gunVec1 * Fixed.ToFloat(Values.AimDistance)).AddY(Fixed.ToFloat(Values.AimYOffset));
            Acceleration = Vector3.Zero;
            _accelerationTimer = 0;
            _aimY = 0;
            _buttonAimX = 0;
            _buttonAimY = 0;
            NodeRef = nodeRef;
            _gunViewBob = 0;
            _walkViewBob = 0;
            if (IsMainPlayer && _scene.CameraSequences.Current?.IsIntro == true)
            {
                _scene.CameraSequences.Current.End();
            }
            CameraInfo.Reset();
            CameraInfo.Position = Position;
            CameraInfo.UpVector = Vector3.UnitY;
            CameraInfo.Target = Position + facing;
            CameraInfo.Fov = Fixed.ToFloat(Values.NormalFov) * 2;
            CameraInfo.NodeRef = NodeRef;
            SwitchCamera(CameraType.First, facing);
            _camSwitchTimer = (ushort)SimTicks.From30HzFrames(Values.CamSwitchTime);
            _viewTiltAngleH = 0;
            _viewTiltAngleV = 0;
            UpdateCameraFirst();
            CameraInfo.Update(_scene);
            _gunDrawPos = Fixed.ToFloat(Values.FieldB8) * facing
                + CameraInfo.Position
                + Fixed.ToFloat(Values.FieldB0) * _gunVec2
                + Fixed.ToFloat(Values.FieldB4) * up;
            _aimVec = _aimPosition - _gunDrawPos;
            _timeSinceInput = 0;
            Flags1 = PlayerFlags1.Standing | PlayerFlags1.StandingPrevious | PlayerFlags1.CanTouchBoost;
            Flags2 = PlayerFlags2.NoShotsFired;
            _volumeUnxf = PlayerVolumes[(int)Hunter, 0];
            _volume = CollisionVolume.Move(_volumeUnxf, Position);
            _field35C = null;
            _timeSinceShot = 255;
            _timeSinceDamage = 255;
            _timeSincePickup = 255;
            _timeSinceHeal = 255;
            _timeSinceStanding = 0;
            _timeStanding = 0;
            _field449 = 0;
            _respawnTimer = 0;
            _timeSinceDead = 0;
            _field551 = 255;
            _field552 = 0;
            _field553 = 0;
            _bombCooldown = 0;
            _bombOveruse = 0;
            _bombRefillTimer = 0;
            _bombAmmo = 3;
            _damageInvulnTimer = 0;
            _spawnInvulnTimer = (ushort)SimTicks.From30HzFrames(Values.SpawnInvulnerability);
            _boostCharge = 0;
            _altAttackCooldown = 0;
            _field4E8 = Vector3.Zero;
            _modelTransform = Matrix4.Identity;
            _timeSinceMorphCamera = UInt16.MaxValue;
            SetBipedAnimation(PlayerAnimation.Spawn, AnimFlags.None);
            _altModel.SetAnimation(0, AnimFlags.Paused);
            SetGunAnimation(GunAnimation.Idle, AnimFlags.NoLoop);
            if (!_scene.IsHeadless)
            {
                _gunSmokeModel.SetAnimation(0);
            }
            _smokeAlpha = 0;
            MorphCamera = null;
            OctolithFlag = null;
            ResetMorphBallTrail();
            _soundSource.StopAllSfx();
            if (IsMainPlayer)
            {
                _soundSource.Update(Position, rangeIndex: -1);
            }
            else
            {
                int rangeIndex = 1;
                _soundSource.Update(Position, rangeIndex);
                UpdateNodeRefVolume();
            }
            if (respawn)
            {
                PlayHunterSfx(HunterSfx.Spawn);
            }
            _lastJumpPad = null;
            _jumpPadControlLock = 0;
            _jumpPadControlLockMin = 0;
            _timeSinceJumpPad = UInt16.MaxValue; // the game doesn't do this
            SpawnPresentation();
            _altRollFbX = CameraInfo.Field48;
            _altRollFbZ = CameraInfo.Field4C;
            _altRollLrX = CameraInfo.Field50;
            _altRollLrZ = CameraInfo.Field54;
            ResetPresentationLighting();
            NoteServerCombatSpawn();
            Controls.ClearPressed();
            if (IsBot)
            {
                AiData.InitializeAtSpawn();
            }
            // the player clears the enemy spawner reference here, using a global array to track them
            _lastTarget = null;
            if (respawn)
            {
                // spawnEffectMP or spawnEffect
                int effectId = _scene.Players.ActiveCount > 2 && !_scene.Features.MaxPlayerDetail ? 33 : 31;
                _scene.SpawnEffect(effectId, Vector3.UnitX, Vector3.UnitY, Position);
            }
        }



        public void ResetReferences()
        {
            // Field35C and point module are also reset here
            MorphCamera = null;
            _lastJumpPad = null;
            OctolithFlag = null;
            _lastTarget = null;
        }

        public override void GetPosition(out Vector3 position)
        {
            position = Position.AddY(IsAltForm ? 0 : 0.5f);
        }

        public override void GetVectors(out Vector3 position, out Vector3 up, out Vector3 facing)
        {
            position = Position.AddY(IsAltForm ? 0 : 0.5f);
            up = _upVector;
            facing = _facingVector;
        }

        public override bool GetTargetable()
        {
            return _health != 0;
        }




        private void SetBiped1Animation(PlayerAnimation anim, AnimFlags animFlags)
        {
            SetBipedAnimation(anim, animFlags, setBiped1: true, setBiped2: false, setIfMorphing: false);
        }

        private void SetBiped2Animation(PlayerAnimation anim, AnimFlags animFlags)
        {
            SetBipedAnimation(anim, animFlags, setBiped1: false, setBiped2: true, setIfMorphing: false);
        }

        private void SetBipedAnimation(PlayerAnimation anim, AnimFlags animFlags, bool setBiped1 = true,
            bool setBiped2 = true, bool setIfMorphing = true)
        {
            if (setIfMorphing || !IsMorphing)
            {
                if (setBiped2 && (setIfMorphing || !IsUnmorphing))
                {
                    _bipedModel2.SetAnimation((int)anim, animFlags);
                }
                if (setBiped1)
                {
                    _bipedModel1.SetAnimation((int)anim, animFlags);
                }
            }
        }

        public void Teleport(Vector3 position, Vector3 facing, NodeRef nodeRef)
        {
            // Teleport is an explicit presentation discontinuity even when
            // the destination happens to be close to the old position.
            AdvancePresentationPoseEpoch();
            _soundSource.PlaySfx(SfxId.TELEPORT_OUT, noUpdate: true);
            Reposition(position, facing, nodeRef);
            if (IsAltForm || IsMorphing || IsUnmorphing)
            {
                ResumeOwnCamera();
                CameraInfo.Update(_scene);
            }
        }

        public void Reposition(Vector3 position, Vector3 facing, NodeRef nodeRef)
        {
            _gunVec1 = facing;
            _facingVector = facing;
            SetTransform(facing, _upVector, position);
            if (nodeRef != NodeRef.None)
            {
                NodeRef = nodeRef;
                CameraInfo.NodeRef = nodeRef;
            }
        }

        public void Reposition(Vector3 offset, NodeRef nodeRef)
        {
            Position += offset;
            PrevPosition += offset;
            _aimPosition += offset;
            _gunDrawPos += offset;
            _muzzlePos += offset;
            _field544 += offset;
            CameraInfo camInfo = CameraInfo;
            camInfo.Position += offset;
            camInfo.PrevPosition += offset;
            camInfo.Target += offset;
            _volume = CollisionVolume.Move(_volumeUnxf, Position);
            for (int i = 0; i < _beams.Length; i++)
            {
                _beams[i].Reposition(offset);
            }
            NodeRef = nodeRef;
            camInfo.NodeRef = nodeRef;
            if (Flags2.TestFlag(PlayerFlags2.Halfturret))
            {
                Halfturret.Reposition(offset, nodeRef);
            }
        }

        public void BlockFormSwitch()
        {
            Flags2 |= PlayerFlags2.NoFormSwitch;
        }

        public void SetBipedStuck(bool stuck)
        {
            if (stuck)
            {
                Flags2 |= PlayerFlags2.BipedStuck;
            }
            else
            {
                Flags2 &= ~PlayerFlags2.BipedStuck;
            }
        }

        // todo: visualize
        public bool CheckHitByBomb(BombEntity bomb, bool halfturret)
        {
            if (bomb.Owner == this
                && (!bomb.Flags.TestFlag(BombFlags.Exploding) && !bomb.Flags.TestFlag(BombFlags.Exploded) || halfturret))
            {
                return false;
            }
            bool hit = false;
            Vector3 between;
            if (halfturret)
            {
                between = Halfturret.Position - bomb.Position;
            }
            else
            {
                between = Volume.SpherePosition - bomb.Position;
            }
            float distSqr = between.LengthSquared;
            float hitRadiusSqr = Fixed.ToFloat(Values.BombSelfRadiusSquared);
            if (bomb.Owner == this)
            {
                if (distSqr <= hitRadiusSqr && between.Y > -Volume.SphereRadius)
                {
                    hit = true;
                    float ySpeed = Fixed.ToFloat(Values.BombJumpSpeed);
                    if (!_scene.Services.IsReplica || _scene.Services.PredictBombJump(this, bomb, ySpeed))
                    {
                        if (Speed.Y < ySpeed) Speed = Speed.WithY(ySpeed);
                    }
                }
            }
            else if (distSqr <= bomb.Radius * bomb.Radius)
            {
                hit = true;
                DamageFlags flags = DamageFlags.NoDmgInvuln;
                if (halfturret)
                {
                    flags |= DamageFlags.Halfturret;
                }
                TakeDamage(bomb.Damage, flags, null, bomb);
                _scene.SendMessage(Message.Impact, bomb, bomb.Owner, this, 0); // the game doesn't set anything as sender
            }
            if (hit)
            {
                float shake = (hitRadiusSqr - distSqr) / hitRadiusSqr * 0.1f;
                CameraInfo.SetShake(shake);
            }
            return hit;
        }

        public void OnHalfturretDied()
        {
            Flags2 &= ~PlayerFlags2.Halfturret;
        }

        private void ResetMorphBallTrail()
        {
            for (int i = 0; i < _mbTrailSegments; i++)
            {
                _mbTrailAlphas[i] = 0;
            }
            _mbTrailIndex = 0;
        }

        private void UpdateMorphBallTrail()
        {
            for (int i = 0; i < _mbTrailSegments; i++)
            {
                float alpha = _mbTrailAlphas[i] - 3 / 31f / 2; // todo: FPS stuff
                if (alpha < 0)
                {
                    alpha = 0;
                }
                _mbTrailAlphas[i] = alpha;
            }
            if (IsAltForm)
            {
                Vector3 row0 = _modelTransform.Row0.Xyz;
                if (Vector3.Dot(Vector3.UnitY, row0) < 0.5f && _hSpeedMag >= Fixed.ToFloat(1269))
                {
                    Vector3 cross = Vector3.Cross(row0, Vector3.UnitY).Normalized();
                    var cross2 = Vector3.Cross(cross, row0);
                    int index = _mbTrailIndex;
                    _mbTrailAlphas[index] = 25 / 31f;
                    _mbTrailMatrices[index] = new Matrix4(
                        row0.X, row0.Y, row0.Z, 0,
                        cross2.X, cross2.Y, cross2.Z, 0,
                        cross.X, cross.Y, cross.Z, 0,
                        Position.X, Position.Y, Position.Z, 1
                    );
                    _mbTrailIndex = (index + 1) % _mbTrailSegments;
                }
            }
        }

        private void InitializeWeapon()
        {
            _availableWeapons.ClearAll();
            _availableCharges.ClearAll();
            WeaponInfo missileInfo = Weapons.Current[(int)BeamType.Missile];
            _availableWeapons[BeamType.PowerBeam] = true;
            _availableWeapons[BeamType.Missile] = true;
            _availableCharges[BeamType.PowerBeam] = true;
            _availableCharges[BeamType.Missile] = true;
            _weaponSlots[0] = BeamType.PowerBeam;
            _weaponSlots[1] = BeamType.Missile;
            _weaponSlots[2] = BeamType.None;
            _ammo[UA] = _ammo[Missiles] = 0;
            _ammo[missileInfo.AmmoType] = 10 * missileInfo.AmmoCost;
            if (IsMainPlayer)
            {
                // todo: set values for HUD graphics, probably
            }
        }

        private bool TryEquipWeapon(BeamType beam, bool silent = false, bool debug = false,
            bool suppressFailureSound = false)
        {
            int index = (int)beam;
            if (index < 0 || index >= 9)
            {
                return false;
            }
            WeaponInfo info = Weapons.Current[(int)beam];
            byte ammoType = info.AmmoType;
            if (debug && _scene.Features.Cheats.FreeWeaponSelect)
            {
                _availableWeapons[beam] = true;
                _availableCharges[beam] = true;
                _ammo[info.AmmoType] = _ammoMax[info.AmmoType];
            }
            bool hasAmmo = beam == BeamType.PowerBeam || _ammo[ammoType] >= info.AmmoCost || _ammo[ammoType] == -1;
            if (!silent && (!hasAmmo || !_availableWeapons[beam] || GunAnimation == GunAnimation.UpDown))
            {
                if (IsMainPlayer && !suppressFailureSound)
                {
                    _soundSource.PlayFreeSfx(SfxId.BEAM_SWITCH_FAIL);
                    if (!hasAmmo)
                    {
                        ShowNoAmmoMessage();
                    }
                }
                return false;
            }
            StopBeamChargeSfx(CurrentWeapon);
            UpdateZoom(false);
            PreviousWeapon = CurrentWeapon;
            CurrentWeapon = WeaponSelection = beam;
            if (beam == Weapons.GetAffinityBeam(Hunter))
            {
                EquipInfo.Weapon = Weapons.Current[(int)beam + 9];
            }
            else
            {
                EquipInfo.Weapon = Weapons.Current[(int)beam];
            }
            EquipInfo.ChargeLevel = 0;
            EquipInfo.SmokeLevel = 0;
            EquipInfo.GetAmmo = () => _ammo[ammoType];
            EquipInfo.SetAmmo = (newAmmo) => _ammo[ammoType] = newAmmo;
            _timeSinceInput = 0;
            if (!silent)
            {
                if (IsMainPlayer && !IsAltForm && beam != BeamType.Missile)
                {
                    int sfx = Metadata.HunterSfx[(int)Hunter, (int)HunterSfx.BeamSwitch];
                    if (sfx != -1)
                    {
                        _soundSource.PlayFreeSfx(sfx);
                    }
                }
                if (beam == BeamType.Missile)
                {
                    SetGunAnimation(GunAnimation.MissileOpen, AnimFlags.NoLoop);
                }
                else if (PreviousWeapon == BeamType.Missile)
                {
                    SetGunAnimation(GunAnimation.MissileClose, AnimFlags.NoLoop);
                }
                else
                {
                    CurrentWeapon = WeaponSelection = PreviousWeapon;
                    SetGunAnimation(GunAnimation.Switch, AnimFlags.NoLoop);
                    CurrentWeapon = WeaponSelection = beam;
                }
            }
            if (beam != BeamType.PowerBeam && beam != BeamType.Missile)
            {
                UpdateAffinityWeaponSlot(beam);
            }
            if (IsMainPlayer)
            {
                HudOnWeaponSwitch(beam, animate: !silent);
                // the game update the bottom screen weapon HUD objects here
            }
            return true;
        }

        private void TryApplyPendingAutoEquip()
        {
            BeamType pending = _pendingAutoEquipWeapon;
            if (pending == BeamType.None) return;
            if (CurrentWeapon == pending)
            {
                _pendingAutoEquipWeapon = BeamType.None;
                return;
            }
            if (Health <= 0 || IsAltForm || IsMorphing || IsUnmorphing
                || GunAnimation == GunAnimation.UpDown)
            {
                return;
            }
            if (!_availableWeapons[pending])
            {
                _pendingAutoEquipWeapon = BeamType.None;
                return;
            }
            if (TryEquipWeapon(pending, suppressFailureSound: true))
            {
                _pendingAutoEquipWeapon = BeamType.None;
            }
        }

        public void UpdateZoom(bool zoom)
        {
            if (IsMainPlayer && EquipInfo.Zoomed != zoom)
            {
                _soundSource.PlayFreeSfx(zoom ? SfxId.SNIPER_ZOOM_IN : SfxId.SNIPER_ZOOM_OUT);
                if (CurrentWeapon == BeamType.Imperialist)
                {
                    HudOnZoom(zoom);
                }
            }
            EquipInfo.Zoomed = zoom;
        }

        private void UpdateAffinityWeaponSlot(BeamType beam, int slot = 2)
        {
            Debug.Assert(slot == 2);
            if (_weaponSlots[slot] == BeamType.OmegaCannon && beam != BeamType.OmegaCannon)
            {
                _availableCharges[BeamType.OmegaCannon] = false;
                _availableWeapons[BeamType.OmegaCannon] = false;
            }
            _weaponSlots[slot] = beam;
            // the game updates the bottom screen weapon icon here
        }

        private void UnequipOmegaCannon()
        {
            if (CurrentWeapon == BeamType.OmegaCannon)
            {
                _availableCharges[BeamType.OmegaCannon] = false;
                _availableWeapons[BeamType.OmegaCannon] = false;
                int priority = 0;
                BeamType nextBeam = BeamType.None;
                // find weapon to switch to (skip PB and Missiles)
                for (int i = 1; i < 9; i++)
                {
                    if (i != 2 && _availableWeapons[i])
                    {
                        WeaponInfo info = Weapons.Current[i];
                        if (info.Priority > priority && _ammo[info.AmmoType] >= info.AmmoCost)
                        {
                            priority = info.Priority;
                            nextBeam = (BeamType)i;
                        }
                    }
                }
                UpdateAffinityWeaponSlot(nextBeam);
                if (nextBeam == BeamType.None)
                {
                    TryEquipWeapon(BeamType.PowerBeam);
                }
                else
                {
                    TryEquipWeapon(nextBeam);
                }
            }
        }

        private void SetGunAnimation(GunAnimation anim, AnimFlags animFlags = AnimFlags.None)
        {
            if (anim != GunAnimation.Shot || CurrentWeapon != BeamType.ShockCoil)
            {
                _shockCoilAnimationActive = false;
            }
            GunAnimation = anim;
            int animId = Metadata.GunAnimationIds[(int)Hunter, (int)anim, 0];
            SetFlags setFlags = SetFlags.Texture | SetFlags.Texcoord | SetFlags.Material | SetFlags.Unused | SetFlags.Node;
            _gunModel.SetAnimation(animId, slot: 0, setFlags, animFlags);
            animId = Metadata.GunAnimationIds[(int)Hunter, (int)anim, (int)CurrentWeapon + 1];
            if (animId >= 0)
            {
                setFlags &= ~SetFlags.Node;
                if (Hunter == Hunter.Sylux)
                {
                    setFlags &= ~SetFlags.Texcoord;
                }
                _gunModel.SetAnimation(animId, slot: 1, setFlags, animFlags);
            }
            GunAnimationPresentation(anim);
            if (anim == GunAnimation.FullChargeMissile || anim == GunAnimation.ChargingMissile || anim == GunAnimation.MissileClose
                || anim == GunAnimation.MissileOpen || anim == GunAnimation.Unknown9 || anim == GunAnimation.MissileShot)
            {
                Flags1 |= PlayerFlags1.GunOpenAnimation;
            }
            else
            {
                Flags1 &= ~PlayerFlags1.GunOpenAnimation;
            }
        }

        internal static bool ShouldPlayShockCoilAnimation(BeamType weapon, bool shooting, int ammo,
            int ammoCost, int health, bool altForm, bool morphing, bool unmorphing)
        {
            if (weapon != BeamType.ShockCoil || !shooting || health <= 0
                || altForm || morphing || unmorphing)
            {
                return false;
            }
            return ammo == -1 || ammo >= ammoCost;
        }

        private void UpdateGunAnimation()
        {
            bool shockCoilFiring = ShouldPlayShockCoilAnimation(CurrentWeapon,
                Flags2.TestFlag(PlayerFlags2.Shooting), EquipInfo.Ammo, EquipWeapon.AmmoCost,
                _health, IsAltForm, IsMorphing, IsUnmorphing);
            if (shockCoilFiring)
            {
                if (!_shockCoilAnimationActive || GunAnimation != GunAnimation.Shot)
                {
                    _shockCoilAnimationActive = true;
                    SetGunAnimation(GunAnimation.Shot, AnimFlags.None);
                }
                return;
            }
            if (_shockCoilAnimationActive)
            {
                _shockCoilAnimationActive = false;
                if (GunAnimation == GunAnimation.Shot)
                {
                    SetGunAnimation(GunAnimation.Idle, AnimFlags.NoLoop);
                }
                return;
            }
            if (_timeSinceInput == 0)
            {
                if (GunAnimation == GunAnimation.UpDown && _gunModel.AnimInfo.Flags[0].TestFlag(AnimFlags.Reverse))
                {
                    _gunModel.AnimInfo.Flags[0] &= ~AnimFlags.Reverse;
                    _gunModel.AnimInfo.Flags[0] &= ~AnimFlags.Ended;
                }
            }
            else if (!_scene.Features.NoIdleSway && _timeSinceInput >= (ulong)Values.GunIdleTime * SimTicks.TicksPer30HzFrame)
            {
                if (GunAnimation != GunAnimation.UpDown)
                {
                    if (CurrentWeapon != BeamType.Missile || GunAnimation == GunAnimation.MissileClose)
                    {
                        SetGunAnimation(GunAnimation.UpDown, AnimFlags.NoLoop | AnimFlags.Reverse);
                    }
                    else
                    {
                        SetGunAnimation(GunAnimation.MissileClose, AnimFlags.NoLoop);
                    }
                }
                return;
            }
            if (GunAnimation == GunAnimation.UpDown)
            {
                if (!_gunModel.AnimInfo.Flags[0].TestFlag(AnimFlags.Ended))
                {
                    return;
                }
                if (CurrentWeapon == BeamType.Missile)
                {
                    SetGunAnimation(GunAnimation.MissileOpen, AnimFlags.NoLoop);
                }
            }
            if (Flags1.TestFlag(PlayerFlags1.ShotUncharged))
            {
                if (!Flags1.TestFlag(PlayerFlags1.ShotMissile))
                {
                    SetGunAnimation(GunAnimation.Shot, AnimFlags.NoLoop);
                    return;
                }
                if (!Flags1.TestFlag(PlayerFlags1.GunOpenAnimation))
                {
                    SetGunAnimation(GunAnimation.Unknown9, AnimFlags.NoLoop);
                    return;
                }
                SetGunAnimation(GunAnimation.MissileShot, AnimFlags.NoLoop);
                return;
            }
            if (Flags1.TestFlag(PlayerFlags1.ShotCharged))
            {
                if (Flags1.TestFlag(PlayerFlags1.ShotMissile))
                {
                    SetGunAnimation(GunAnimation.MissileShot, AnimFlags.NoLoop);
                    return;
                }
                SetGunAnimation(GunAnimation.ChargeShot, AnimFlags.NoLoop);
                return;
            }
            if (EquipInfo.ChargeLevel >= SimTicks.From30HzFrames(EquipInfo.Weapon.MinCharge))
            {
                if ((GunAnimation == GunAnimation.Charging || GunAnimation == GunAnimation.ChargingMissile)
                    && _scene.FrameCount != 0 && _scene.FrameCount % 2 == 0) // todo: FPS stuff
                {
                    int frame = (_gunModel.AnimInfo.FrameCount[0] - 1) * (EquipInfo.ChargeLevel / 2 - EquipInfo.Weapon.MinCharge)
                        / (EquipInfo.Weapon.FullCharge - EquipInfo.Weapon.MinCharge); // todo: FPS stuff
                    _gunModel.AnimInfo.Frame[0] = frame;
                }
                if (CurrentWeapon == BeamType.Missile)
                {
                    if (GunAnimation != GunAnimation.ChargingMissile && GunAnimation != GunAnimation.FullChargeMissile)
                    {
                        SetGunAnimation(GunAnimation.ChargingMissile, AnimFlags.NoLoop);
                    }
                    else if (GunAnimation != GunAnimation.FullChargeMissile && _gunModel.AnimInfo.Flags[0].TestFlag(AnimFlags.Ended))
                    {
                        SetGunAnimation(GunAnimation.FullChargeMissile);
                    }
                }
                else if (GunAnimation != GunAnimation.Charging && GunAnimation != GunAnimation.FullCharge)
                {
                    SetGunAnimation(GunAnimation.Charging, AnimFlags.NoLoop);
                }
                else if (GunAnimation != GunAnimation.FullCharge && _gunModel.AnimInfo.Flags[0].TestFlag(AnimFlags.Ended))
                {
                    SetGunAnimation(GunAnimation.FullCharge);
                }
                return;
            }
            if ((GunAnimation == GunAnimation.Charging || GunAnimation == GunAnimation.ChargingMissile)
                && !_gunModel.AnimInfo.Flags[0].TestFlag(AnimFlags.Reverse))
            {
                _gunModel.AnimInfo.Flags[0] |= AnimFlags.Reverse;
                return;
            }
            if (GunAnimation == GunAnimation.FullCharge)
            {
                SetGunAnimation(GunAnimation.Idle, AnimFlags.NoLoop);
                return;
            }
            if (GunAnimation == GunAnimation.FullChargeMissile)
            {
                SetGunAnimation(GunAnimation.MissileOpen, AnimFlags.NoLoop);
                _gunModel.AnimInfo.Frame[0] = _gunModel.AnimInfo.FrameCount[0] - 1;
                return;
            }
            if (_gunModel.AnimInfo.Flags[0].TestFlag(AnimFlags.Ended))
            {
                if (!Flags1.TestFlag(PlayerFlags1.GunOpenAnimation) || GunAnimation == GunAnimation.MissileClose)
                {
                    SetGunAnimation(GunAnimation.Idle, AnimFlags.NoLoop);
                }
                else if (CurrentWeapon == BeamType.Missile && WeaponSelection != BeamType.Missile)
                {
                    SetGunAnimation(GunAnimation.MissileClose, AnimFlags.NoLoop);
                }
            }
        }

        public void TakeDamage(int damage, DamageFlags flags, Vector3? direction, EntityBase? source)
        {
            TakeDamage((uint)damage, flags, direction, source);
        }

        public void TakeDamage(uint damage, DamageFlags flags, Vector3? direction, EntityBase? source)
        {
            _scene.Services.ObserveDamageAttempt(this, damage, flags, direction, source);
            if (_scene.Services.SuppressDamage(this) || (_scene.Services.Combat?.IsStaleSource(source) == true)
                || flags.TestFlag(DamageFlags.Burn) && (_scene.Services.Combat?.IsStaleActor(CombatBurnSource.Actor) == true))
            {
                return;
            }
            if (_health == 0)
            {
                return;
            }
            if (IsMainPlayer && _scene.CameraSequences.Current?.BlockInput == true)
            {
                if (!flags.TestFlag(DamageFlags.Death))
                {
                    return;
                }
                _scene.SpecialEntities.CancelCameraSequence();
            }
            if (_spawnInvulnTimer > 0 && !flags.TestFlag(DamageFlags.Death) && !flags.TestFlag(DamageFlags.IgnoreInvuln))
            {
                return;
            }
            if (IsBot && AiData.Flags2.TestFlag(AiFlags2.Bit13))
            {
                return;
            }
            if (!flags.TestAny(DamageFlags.Death | DamageFlags.IgnoreInvuln | DamageFlags.NoDmgInvuln))
            {
                if (_damageInvulnTimer > 0)
                {
                    return;
                }
                _damageInvulnTimer = (ushort)SimTicks.From30HzFrames(Values.DamageInvuln);
            }
            PlayerEntity? attacker = null;
            bool fromHalfturret = false;
            BombEntity? bomb = null;
            BeamProjectileEntity? beam = null;
            if (source != null)
            {
                if (source.Type == EntityType.BeamProjectile)
                {
                    beam = (BeamProjectileEntity)source;
                    Effectiveness effectiveness = BeamEffectiveness[(int)beam.Beam];
                    if (effectiveness == Effectiveness.Zero)
                    {
                        return;
                    }
                    if (beam.Owner?.Type == EntityType.Player)
                    {
                        attacker = (PlayerEntity)beam.Owner;
                    }
                    else if (beam.Owner?.Type == EntityType.Halfturret)
                    {
                        attacker = ((HalfturretEntity)beam.Owner).Owner;
                        fromHalfturret = true;
                    }
                    if (damage > 0)
                    {
                        damage = (uint)(damage * Metadata.DamageMultipliers[(int)effectiveness]);
                        if (damage == 0)
                        {
                            damage = 1;
                        }
                    }
                    if (!IsMainPlayer)
                    {
                        beam.SpawnDamageEffect(effectiveness);
                    }
                }
                else if (source.Type == EntityType.Player)
                {
                    attacker = (PlayerEntity)source;
                    if (attacker._doubleDmgTimer > 0)
                    {
                        damage *= 2;
                    }
                }
                else if (source.Type == EntityType.Bomb)
                {
                    bomb = (BombEntity)source;
                    attacker = bomb.Owner;
                }
            }
            bool ignoreDamage = false;
            if (_scene.Match.Rules.Teams && !_scene.Match.Rules.FriendlyFire
                && attacker != null && attacker != this && attacker.TeamIndex == TeamIndex)
            {
                ignoreDamage = true;
                damage = 0;
            }
            if (!ignoreDamage && flags.TestFlag(DamageFlags.Headshot) && attacker != null && attacker == _scene.LocalPlayer) // todo: and not on wifi
            {
                int messageId = 228; // HEADSHOT!
                QueueHudMessage(128, 70, 20 / (float)SimTicks.LegacyHz, 0, messageId);
            }
            if (attacker != null && attacker != this && beam != null)
            {
                // bugfix?: since double damage was already applied above,
                // it's possible to increase efficiency with double damage hits
                _scene.Match.Players[attacker.SlotIndex].BeamDamageDealt = Math.Min(
                    _scene.Match.Players[attacker.SlotIndex].BeamDamageDealt + (int)damage,
                    _scene.Match.Players[attacker.SlotIndex].BeamDamageMax
                );
            }
            if (damage > 0)
            {
                damage = (uint)(damage * Metadata.DamageLevels[_scene.Match.Rules.DamageLevel]);
                if (damage == 0)
                {
                    damage = 1;
                }
            }
            if (Flags2.TestFlag(PlayerFlags2.Halfturret) && attacker != null && !ignoreDamage)
            {
                _halfturret.OnTakeDamage(attacker, damage);
            }
            if (flags.TestFlag(DamageFlags.Halfturret) && !ignoreDamage) // todo?: and either main player or not wifi
            {
                uint turretDamage;
                if (_health > Halfturret.Health)
                {
                    turretDamage = damage - damage / 2;
                }
                else
                {
                    turretDamage = damage / 2;
                }
                if (_halfturret.Health <= turretDamage)
                {
                    _halfturret.Die();
                }
                else
                {
                    _halfturret.Health -= (int)turretDamage;
                }
                damage -= turretDamage;
                if (_health <= damage)
                {
                    damage = (uint)(_health - 1);
                }
                _halfturret.TimeSinceDamage = 0;
                if (IsBot)
                {
                    AiData.DamageFromHalfturret = turretDamage;
                }
            }
            if (IsBot && source != null)
            {
                AiData.OnTakeDamage((int)damage, source, attacker);
            }
            // todo?: something for wifi
            // else...
            int combatPreviousHealth = _health;
            ushort combatFrozen = _frozenTimer, combatBurn = _burnTimer, combatDisrupt = _disruptedTimer;
            _scene.Services.NoteDamage(this, attacker, beam?.Beam ?? BeamType.None, flags, direction);
            bool dead = false;
            if (_health <= damage || flags.TestFlag(DamageFlags.Death))
            {
                dead = true;
            }
            // todo?: something for wifi
            if (attacker != null)
            {
                if (attacker == _scene.LocalPlayer)
                {
                    _scene.LocalPlayer.UpdateOpponent(SlotIndex);
                }
                if (attacker != this)
                {
                    _scene.Match.Players[attacker.SlotIndex].DamageCount++;
                    attacker._hidingTimer = 0;
                    _hidingTimer = 0;
                }
            }
            if (dead)
            {
                // todo?: the game encodes the beam in the damage flags for wifi stuff
                BeamType beamType = BeamType.Platform;
                if (beam != null)
                {
                    beamType = beam.Beam;
                }
                else if (_scene.Services.ReplayBeam != BeamType.None)
                {
                    // Replaying a kill the authority resolved: the beam entity
                    // only ever existed on its machine, so the weapon for the
                    // banner comes over the wire instead of being guessed.
                    beamType = _scene.Services.ReplayBeam;
                }
                if (source == attacker || fromHalfturret || bomb != null)
                {
                    // halfturret, bomb, or direct hit by player (not beam)
                    // --> also if there's no source and no attacker
                    flags |= DamageFlags.FromAlt;
                }
                _scene.SendMessage(Message.Destroyed, this, null, 0, 0, delay: 1);
                if (Flags2.TestFlag(PlayerFlags2.Halfturret))
                {
                    _halfturret.Die();
                }
                _healthRecovery = 0;
                _ammoRecovery[0] = 0;
                _ammoRecovery[1] = 0;
                EquipInfo.ChargeLevel = 0;
                CameraInfo.Shake = 0;
                _doubleDmgTimer = 0;
                _deathaltTimer = 0;
                _cloakTimer = 0;
                Flags2 &= ~PlayerFlags2.Cloaking;
                _scene.Match.Players[SlotIndex].KillStreak = 0;
                if (IsMainPlayer)
                {
                    // todo: license info
                    HudEndDisrupted();
                    if (_frozenGfxTimer > 0)
                    {
                        _drawIceLayer = false;
                    }
                }
                _frozenTimer = 0;
                _frozenGfxTimer = 0;
                _disruptedTimer = 0;
                _burnTimer = 0;
                if (_furlEffect != null)
                {
                    _scene.UnlinkEffectEntry(_furlEffect);
                    _furlEffect = null;
                }
                if (_boostEffect != null)
                {
                    _scene.UnlinkEffectEntry(_boostEffect);
                    _boostEffect = null;
                }
                if (_burnEffect != null)
                {
                    if (_burnEffect.EffectId == 188) // flamingGun
                    {
                        _scene.UnlinkEffectEntry(_burnEffect);
                    }
                    else
                    {
                        _scene.DetachEffectEntry(_burnEffect, setExpired: false);
                    }
                    _burnEffect = null;
                }
                if (_chargeEffect != null)
                {
                    _scene.UnlinkEffectEntry(_chargeEffect);
                    _chargeEffect = null;
                }
                if (_muzzleEffect != null)
                {
                    _scene.UnlinkEffectEntry(_muzzleEffect);
                    _muzzleEffect = null;
                }
                if (_doubleDmgEffect != null)
                {
                    _scene.UnlinkEffectEntry(_doubleDmgEffect);
                    _doubleDmgEffect = null;
                }
                if (_deathaltEffect != null)
                {
                    _scene.UnlinkEffectEntry(_deathaltEffect);
                    _deathaltEffect = null;
                }
                if (_health > 0)
                {
                    _soundSource.StopAllSfx(force: true);
                    if (IsMainPlayer)
                    {
                        // the game stops the unused weapon alarm SFX here
                        UpdateDoubleDamageSfx(index: 0, play: false);
                        UpdateCloakSfx(index: 0, play: false);
                        _soundSource.StopFreeSfxScripts();
                        PlayHunterSfx(HunterSfx.Death);
                    }
                    else
                    {
                        PlayHunterSfx(HunterSfx.Death);
                    }
                    StopBeamChargeSfx(CurrentWeapon);
                }
                if (IsBot && AiData.Flags1)
                {
                    _health = AiData.HealthThreshold + 1;
                    AiData.Flags2 |= AiFlags2.Bit13;
                }
                else
                {
                    _pendingAutoEquipWeapon = BeamType.None;
                    _health = 0;
                }
                UpdateZoom(false);
                // the game stops the boost charge SFX here, but that SFX is empty
                _boostCharge = 0;
                _scene.Match.Players[SlotIndex].Deaths++;
                _scene.SpawnDirector.RecordDeath(Position);
                if (this == _scene.LocalPlayer && beamType == BeamType.OmegaCannon)
                {
                    ShowDeathFade();
                }
                Speed = Vector3.Zero;
                _respawnTimer = RespawnTime;
                _timeSinceDead = 0;
                if (attacker != null)
                {
                    if (IsMainPlayer)
                    {
                        if (attacker == this)
                        {
                            QueueHudMessage(128, 70, 140, 90 / (float)SimTicks.LegacyHz, 2, 235); // YOU SELF-DESTRUCTED!
                        }
                        else
                        {
                            // todo: update license
                            string nickname = _scene.Roster.Nicknames[attacker.SlotIndex];
                            // %s's HEADSHOT KILLED YOU! / %s KILLED YOU!
                            string message = Strings.GetHudMessage(flags.TestFlag(DamageFlags.Headshot) ? 236 : 237);
                            QueueHudMessage(128, 70, 140, 90 / (float)SimTicks.LegacyHz, 2, message.Replace("%s", nickname));
                        }
                        string? killedBy = null;
                        if (flags.TestFlag(DamageFlags.Deathalt))
                        {
                            killedBy = Strings.GetHudMessage(250); // DEATHALT
                        }
                        else if (flags.TestFlag(DamageFlags.Burn))
                        {
                            killedBy = Strings.GetHudMessage(251); // MAGMAUL BURN
                        }
                        else if (fromHalfturret)
                        {
                            killedBy = _altAttackNames[(int)attacker.Hunter];
                        }
                        else if (beamType <= BeamType.OmegaCannon)
                        {
                            killedBy = _weaponNames[(int)beamType];
                        }
                        else if (source == attacker)
                        {
                            if (attacker.Hunter == Hunter.Weavel)
                            {
                                killedBy = Strings.GetHudMessage(253); // HALFTURRET SLICE
                            }
                            else
                            {
                                killedBy = _altAttackNames[(int)attacker.Hunter];
                            }
                        }
                        else if (bomb != null)
                        {
                            if (bomb.BombType == BombType.MorphBall)
                            {
                                killedBy = Strings.GetHudMessage(252); // MORPH BALL BOMB
                            }
                            else if (bomb.BombType == BombType.Stinglarva)
                            {
                                killedBy = _altAttackNames[(int)Hunter.Kanden];
                            }
                            else if (bomb.BombType == BombType.Lockjaw)
                            {
                                killedBy = _altAttackNames[(int)Hunter.Sylux];
                            }
                        }
                        if (killedBy != null)
                        {
                            QueueHudMessage(128, 70, 140, 90 / (float)SimTicks.LegacyHz, 2, $"({killedBy})");
                        }
                    }
                    if (attacker == this)
                    {
                        _scene.Match.Players[SlotIndex].Suicides++;
                        if (_scene.Match.Rules.Mode == MatchMode.Battle || _scene.Match.Rules.Mode == MatchMode.TeamBattle)
                        {
                            _scene.Match.Players[SlotIndex].Points--;
                        }
                    }
                    else
                    {
                        if (attacker.TeamIndex == TeamIndex)
                        {
                            _scene.Match.Players[attacker.SlotIndex].FriendlyKills++;
                            _scene.Match.Players[attacker.SlotIndex].KillStreak = 0;
                            // todo: update license info
                            if (attacker == _scene.LocalPlayer)
                            {
                                string nickname = _scene.Roster.Nicknames[SlotIndex];
                                string message = Strings.GetHudMessage(240); // YOU KILLED A TEAMMATE, (%s)!
                                QueueHudMessage(128, 70, 140, 60 / (float)SimTicks.LegacyHz, 2, message.Replace("%s", nickname));
                            }
                        }
                        else
                        {
                            if (attacker == _scene.LocalPlayer)
                            {
                                // todo: update license info
                                string nickname = _scene.Roster.Nicknames[SlotIndex];
                                // YOUR HEADSHOT KILLED %s! / YOU KILLED %s!
                                string message = Strings.GetHudMessage(flags.TestFlag(DamageFlags.Headshot) ? 239 : 238);
                                QueueHudMessage(128, 70, 140, 60 / (float)SimTicks.LegacyHz, 2, message.Replace("%s", nickname));
                            }
                            if (flags.TestFlag(DamageFlags.Headshot))
                            {
                                _scene.Match.Players[attacker.SlotIndex].HeadshotKills++;
                            }
                            _scene.Match.Players[attacker.SlotIndex].Kills++;
                            // todo?: the game also updates another kills stat(?) here
                            if (attacker.IsPrimeHunter)
                            {
                                _scene.Match.Players[attacker.SlotIndex].KillsAsPrime++;
                            }
                            if (beamType <= BeamType.OmegaCannon)
                            {
                                _scene.Match.Players[attacker.SlotIndex].SetBeamKills((int)beamType,
                                    _scene.Match.Players[attacker.SlotIndex].GetBeamKills((int)beamType) + 1);
                                // todo: update license info
                            }
                            if (_scene.Match.Players[attacker.SlotIndex].KillStreak < 255)
                            {
                                _scene.Match.Players[attacker.SlotIndex].KillStreak++;
                                _scene.Match.Players[attacker.SlotIndex].LongestKillStreak = Math.Max(
                                    _scene.Match.Players[attacker.SlotIndex].LongestKillStreak, _scene.Match.Players[attacker.SlotIndex].KillStreak);
                            }
                            if (_scene.Match.Players[attacker.SlotIndex].KillStreak == 5)
                            {
                                _soundSource.QueueStream(VoiceId.VOICE_CONSECUTIVE_KILLS, delay: 1);
                                string message;
                                if (attacker.IsMainPlayer)
                                {
                                    message = Strings.GetHudMessage(254); // YOU KILLED 5 IN A ROW!
                                }
                                else
                                {
                                    string nickname = _scene.Roster.Nicknames[attacker.SlotIndex];
                                    message = Strings.GetHudMessage(255); // %s KILLED 5 IN A ROW!
                                    message = message.Replace("%s", nickname);
                                }
                                QueueHudMessage(128, 70, 140, 90 / (float)SimTicks.LegacyHz, 2, message);
                            }
                            if (_scene.Match.Rules.Mode == MatchMode.PrimeHunter)
                            {
                                if (attacker.IsPrimeHunter)
                                {
                                    attacker.GainHealth(70);
                                }
                                else if (attacker.Health > 0 && (_scene.Match.PrimeHunter == -1 || IsPrimeHunter))
                                {
                                    _scene.Match.PrimeHunter = attacker.SlotIndex;
                                    _scene.Match.Players[attacker.SlotIndex].PrimesKilled++;
                                    if (_scene.LocalPlayer?.IsPrimeHunter == true)
                                    {
                                        _soundSource.QueueStream(VoiceId.VOICE_PRIME, delay: 1);
                                    }
                                    string nickname = _scene.Roster.Nicknames[attacker.SlotIndex];
                                    string message = Strings.GetHudMessage(241); // %s is the new prime hunter!
                                    QueueHudMessage(128, 70, 140, 90 / (float)SimTicks.LegacyHz, 2, message.Replace("%s", nickname));
                                }
                            }
                            else if (_scene.Match.Rules.Mode == MatchMode.Battle || _scene.Match.Rules.Mode == MatchMode.TeamBattle)
                            {
                                if (_scene.Match.Players[attacker.SlotIndex].Points < 99999)
                                {
                                    _scene.Match.Players[attacker.SlotIndex].Points++;
                                }
                            }
                            else if (_scene.Match.Rules.IsOctolithMode && OctolithFlag != null)
                            {
                                _scene.Match.Players[attacker.SlotIndex].OctolithStops++;
                            }
                            // bugfix?: this flag is also set for suicides/environmental damage/etc.
                            if (flags.TestFlag(DamageFlags.FromAlt))
                            {
                                _scene.Match.Players[attacker.SlotIndex].AltDamageCount++;
                            }
                        }
                    }
                }
                else // no attacker
                {
                    _scene.Match.Players[SlotIndex].Suicides++;
                    if (_scene.Match.Rules.Mode == MatchMode.Battle || _scene.Match.Rules.Mode == MatchMode.TeamBattle)
                    {
                        _scene.Match.Players[SlotIndex].Points--;
                    }
                }
                if (IsAltForm || IsMorphing)
                {
                    _scene.SpawnEffect(216, Vector3.UnitX, Vector3.UnitY, Position); // deathAlt
                }
                if (attacker == null || attacker == this)
                {
                    Vector3 camFacing = CameraInfo.Position + CameraInfo.Facing;
                    SwitchCamera(CameraType.Free, camFacing);
                }
                else
                {
                    SwitchCamera(CameraType.Free, attacker.Position);
                }
                if (IsPrimeHunter)
                {
                    _scene.Match.PrimeHunter = -1;
                    QueueHudMessage(128, 70, 140, 90 / (float)SimTicks.LegacyHz, 2, 242); // the prime hunter is dead!
                }

                if (attacker != null && attacker != this)
                {
                    ItemType itemType = ItemType.UASmall;
                    if (attacker.EquipInfo.Weapon.AmmoType == 1)
                    {
                        itemType = ItemType.MissileSmall;
                    }
                    Vector3 position = _volume.SpherePosition.AddY(0.35f);
                    ItemSpawnEntity.SpawnItem(itemType, position, NodeRef, SimTicks.From30HzFrames(300), _scene);
                }
                WeaponSelection = CurrentWeapon;
                Flags1 &= ~PlayerFlags1.WeaponMenuOpen;
            }
            else // not dead
            {
                bool skipSfx = false;
                _health -= (int)damage; // todo?: if wifi, only do this if main player
                if (beam != null && !ignoreDamage)
                {
                    if (beam.Afflictions.TestFlag(Affliction.Freeze))
                    {
                        if (flags.TestFlag(DamageFlags.Halfturret))
                        {
                            _soundSource.PlaySfx(SfxId.SHOTGUN_FREEZE);
                            _halfturret.OnFrozen();
                        }
                        else // todo?: if wifi, only do this if main player
                        {
                            _soundSource.PlaySfx(SfxId.SHOTGUN_FREEZE);
                            if (IsMainPlayer)
                            {
                                _drawIceLayer = true;
                            }
                            if (_frozenTimer == 0)
                            {
                                if (_timeSinceFrozen > SimTicks.From30HzFrames(60))
                                {
                                    int time = SimTicks.From30HzFrames(75);
                                    _frozenTimer = (ushort)time;
                                }
                                else if (_frozenTimer < SimTicks.From30HzFrames(15))
                                {
                                    _frozenTimer = (ushort)SimTicks.From30HzFrames(15);
                                }
                                _frozenGfxTimer = (ushort)(_frozenTimer + SimTicks.From30HzFrames(5));
                            }
                            EndAltAttack();
                        }
                    }
                    if (beam.Afflictions.TestFlag(Affliction.Disrupt) && !flags.TestFlag(DamageFlags.Halfturret))
                    {
                        _disruptedTimer = (ushort)SimTicks.From30HzFrames(60);
                        if (IsMainPlayer)
                        {
                            skipSfx = true;
                            HudOnDisrupted();
                            _soundSource.PlaySfx(SfxId.LOB_DISRUPT);
                        }
                    }
                    if (beam.Afflictions.TestFlag(Affliction.Burn))
                    {
                        if (flags.TestFlag(DamageFlags.Halfturret))
                        {
                            _halfturret.OnSetOnFire();
                        }
                        else // todo?: if wifi, only do this if main player
                        {
                            ushort time = (ushort)SimTicks.From30HzFrames(150);
                            _burnedBy = beam.Owner;
                            CombatBurnSource = beam.CombatShot;
                            _burnTimer = time;
                            CreateBurnEffect();
                        }
                    }
                }
                if (!skipSfx && !flags.TestFlag(DamageFlags.NoSfx))
                {
                    PlayHunterSfx(HunterSfx.Damage);
                }
                if (IsMainPlayer && !IsAltForm)
                {
                    PlayRandomDamageSfx();
                }
            }
            _timeSinceDamage = 0;
            if (_health > 0 && _frozenTimer == 0)
            {
                Vector3? hitDirection = null;
                if (direction.HasValue)
                {
                    if (!IsAltForm)
                    {
                        if (!flags.TestFlag(DamageFlags.Halfturret))
                            Speed = DamageImpulse.Apply(Speed, direction.Value,
                                altForm: false, halfturret: false);
                        if (direction.Value != Vector3.Zero)
                        {
                            hitDirection = direction.Value;
                        }
                        else if (beam != null)
                        {
                            hitDirection = beam.Velocity;
                        }
                    }
                    else if (!flags.TestFlag(DamageFlags.Halfturret))
                    {
                        Speed = DamageImpulse.Apply(Speed, direction.Value,
                            altForm: true, halfturret: false);
                    }
                }
                else if (attacker != null)
                {
                    hitDirection = Position - attacker.Position;
                }
                if (hitDirection.HasValue && !IsAltForm)
                {
                    // todo: clean this up
                    float hitZ = hitDirection.Value.Z;
                    float hitX = -hitDirection.Value.X;
                    float v126 = -hitZ * _field74;
                    float dirHorizontal = -hitZ * _gunVec2.Z;
                    float dirLeftRight = hitX * _gunVec2.X + dirHorizontal;
                    if (dirLeftRight < 0)
                    {
                        dirHorizontal = -dirLeftRight;
                    }
                    float v125 = hitX * _field70;
                    float dirUpDown = v125 + v126;
                    if (dirLeftRight >= 0)
                    {
                        dirHorizontal = dirLeftRight;
                    }
                    float dirVertical;
                    if (dirUpDown >= 0)
                    {
                        dirVertical = v125 + v126;
                    }
                    else
                    {
                        dirVertical = -dirUpDown;
                    }
                    // todo: diagonals (behind feature switch)
                    PlayerAnimation anim = PlayerAnimation.None;
                    if (dirVertical <= dirHorizontal)
                    {
                        if (dirLeftRight <= 0)
                        {
                            anim = PlayerAnimation.DamageRight;
                            if (IsMainPlayer)
                            {
                                ShowDamageIndicator(2); // todo: FPS stuff
                            }
                        }
                        else
                        {
                            anim = PlayerAnimation.DamageLeft;
                            if (IsMainPlayer)
                            {
                                ShowDamageIndicator(6); // todo: FPS stuff
                            }
                        }
                    }
                    else if (dirUpDown <= 0)
                    {
                        anim = PlayerAnimation.DamageBack;
                        if (IsMainPlayer)
                        {
                            ShowDamageIndicator(4); // todo: FPS stuff
                        }
                    }
                    else
                    {
                        anim = PlayerAnimation.DamageFront;
                        if (IsMainPlayer)
                        {
                            ShowDamageIndicator(0); // todo: FPS stuff
                        }
                    }
                    if (anim != PlayerAnimation.None)
                    {
                        SetBipedAnimation(anim, AnimFlags.NoLoop, setBiped1: false, setBiped2: true, setIfMorphing: false);
                    }
                }
            }
            if (_health > 0 && !IsAltForm)
            {
                float shake = 0.03f;
                if (!flags.TestFlag(DamageFlags.Burn))
                {
                    shake = Math.Max(damage * 0.01f, 0.05f);
                }
                CameraInfo.SetShake(shake);
            }
            _scene.Services.Combat?.NoteDamage(this, source, attacker, beam?.Beam ?? BeamType.None,
                flags, direction, combatPreviousHealth, _frozenTimer, _burnTimer, _disruptedTimer,
                combatFrozen != _frozenTimer || combatBurn != _burnTimer || combatDisrupt != _disruptedTimer);
            if (IsMainPlayer)
            {
                // todo: rumble
            }
        }

        private static IReadOnlyList<string> _altAttackNames = Array.AsReadOnly(new string[8]);
        private static IReadOnlyList<string> _hunterNames = Array.AsReadOnly(new string[8]);
        private static IReadOnlyList<string> _weaponNames = Array.AsReadOnly(new string[9]);
        private static (long Generation, Language Language)? _nameContext;

        public static void LoadWeaponNames()
        {
            lock (ContentEnvironment.SyncRoot)
            {
                var context = (ContentEnvironment.Generation, Scene.Language);
                if (_nameContext == context) { return; }
                string[] weaponNames = new string[9];
                string[] hunterNames = new string[8];
                string[] altAttackNames = new string[8];
                for (int i = 0; i < 9; ++i)
                {
                    weaponNames[i] = Strings.GetMessage('W', i + 1, StringTables.WeaponNames);
                }
                for (int i = 0; i < 8; ++i)
                {
                    hunterNames[i] = Strings.GetMessage('H', i + 1, StringTables.WeaponNames);
                }
                // todo: Guardian alt form
                for (int i = 0; i < 7; ++i)
                {
                    altAttackNames[i] = Strings.GetMessage('A', i + 1, StringTables.WeaponNames);
                }
                _weaponNames = Array.AsReadOnly(weaponNames);
                _hunterNames = Array.AsReadOnly(hunterNames);
                _altAttackNames = Array.AsReadOnly(altAttackNames);
                _nameContext = context;
            }
        }

        public static PlayerCollisionVolumes PlayerVolumes { get; } = new();

        // Kept as a source-compatible warmup entry point; the table is immutable.
        public static void GeneratePlayerVolumes() => _ = PlayerVolumes;

        public sealed class PlayerCollisionVolumes
        {
            private readonly CollisionVolume[,] _values = new CollisionVolume[8, 3];
            public CollisionVolume this[int hunter, int volume] => _values[hunter, volume];

            internal PlayerCollisionVolumes()
            {
                for (int i = 0; i < 8; i++)
                {
                    PlayerValues values = Metadata.PlayerValues[i];
                    // placed so the bottom of the sphere coincides with the min pickup height (0.5f below y pos)
                    float radius = Fixed.ToFloat(values.BipedColRadius);
                    var center = new Vector3(0, Fixed.ToFloat(values.MinPickupHeight) + radius, 0);
                    _values[i, 0] = new CollisionVolume(center, radius);
                    // placed so the top of the sphere coincides with the max pickup height (1.1f above y pos)
                    center = new Vector3(0, Fixed.ToFloat(values.MaxPickupHeight) - radius, 0);
                    _values[i, 1] = new CollisionVolume(center, radius);
                    // placed so the bottom of the sphere coincides with the ground level in alt form
                    radius = Fixed.ToFloat(values.AltColRadius);
                    center = new Vector3(0, Fixed.ToFloat(values.AltColYPos), 0);
                    _values[i, 2] = new CollisionVolume(center, radius);
                }
            }
        }

        public static IReadOnlyList<float> KandenAltNodeDistances { get; private set; } = Array.AsReadOnly(new float[4]);
        private static long _kandenContext = -1;

        public static void GenerateKandenAltNodeDistances()
        {
            lock (ContentEnvironment.SyncRoot)
            {
                if (_kandenContext == ContentEnvironment.Generation) { return; }
                float[] distances = new float[4];
                Model model = Read.GetModelInstance("KandenAlt_lod0").Model;
                model.ComputeNodeMatrices(0);
                IReadOnlyList<Node> nodes = model.Nodes;
                for (int i = 0; i < 4; i++)
                {
                    Vector3 pos1 = nodes[i].Transform.Row3.Xyz;
                    Vector3 pos2 = nodes[i + 1].Transform.Row3.Xyz;
                    distances[i] = Vector3.Distance(pos1, pos2);
                }
                KandenAltNodeDistances = Array.AsReadOnly(distances);
                _kandenContext = ContentEnvironment.Generation;
            }
        }
    }

    [Flags]
    public enum DamageFlags : int
    {
        None = 0,
        NoDmgInvuln = 1,
        IgnoreInvuln = 2,
        Death = 4,
        Halfturret = 8,
        Headshot = 0x10,
        Deathalt = 0x20,
        Burn = 0x40,
        NoSfx = 0x80,
        FromAlt = 0x100
    }

    [Flags]
    public enum PlayerFlags1 : uint
    {
        None = 0,
        Standing = 1,
        StandingPrevious = 2,
        NoUnmorph = 4,
        NoUnmorphPrevious = 8,
        CollidingLateral = 0x10,
        OnAcid = 0x20,
        OnLava = 0x40,
        CollidingEntity = 0x80,
        UsedJump = 0x100,
        AltForm = 0x200,
        AltFormPrevious = 0x400,
        Morphing = 0x800,
        Unmorphing = 0x1000,
        Strafing = 0x2000,
        FreeLook = 0x4000,
        FreeLookPrevious = 0x8000,
        ShotUncharged = 0x10000,
        ShotMissile = 0x20000,
        ShotCharged = 0x40000,
        GunOpenAnimation = 0x80000,
        Grounded = 0x100000,
        GroundedPrevious = 0x200000,
        Walking = 0x400000,
        MovingBiped = 0x800000,
        NoAimInput = 0x1000000,
        WeaponMenuOpen = 0x2000000,
        Boosting = 0x4000000,
        CanTouchBoost = 0x8000000,
        UsedJumpPad = 0x10000000,
        AltDirOverride = 0x20000000,
        DrawGunSmoke = 0x80000000
    }

    [Flags]
    public enum PlayerFlags2 : uint
    {
        None = 0,
        ChargeEffect = 1,
        HideModel = 2,
        Shooting = 4,
        AltAttack = 8,
        BipedStuck = 0x10,
        Halfturret = 0x20,
        Cloaking = 0x40,
        GravityOverride = 0x80,
        NoFormSwitch = 0x100,
        BipedLock = 0x200,
        AltFormGravity = 0x400,
        Lod1 = 0x800,
        DrawnThirdPerson = 0x1000,
        RadarReveal = 0x2000,
        RadarRevealPrevious = 0x4000,
        SpireClimbing = 0x8000,
        NoShotsFired = 0x10000,
        UnequipOmegaCannon = 0x20000,
        /// <summary>Quake-3-style spectating: hidden and non-solid to everyone, replicated via <see cref="Mods.Network.NetProtocol.PlayerState"/>'s FlagSpectating bit.</summary>
        Spectating = 0x40000
    }

    [Flags]
    public enum AbilityFlags : short
    {
        None = 0,
        AltForm = 1,
        SpaceJump = 2,
        Bombs = 4,
        Boost = 0x40,
        NoxusAltAttack = 0x100,
        SpireAltAttack = 0x200,
        TraceAltAttack = 0x400,
        WeavelAltAttack = 0x1000
    }

    [Flags]
    public enum FhPlayerFlags : uint
    {
        None = 0,
        Standing = 1,
        StandingPrevious = 2,
        CollidingLateral = 4,
        UsedJump = 8,
        AltForm = 0x10,
        AltFormPrevious = 0x20,
        FreeStrafe = 0x40,
        LockedOn = 0x80,
        AutoLockOn = 0x100,
        FreeLook = 0x200,
        FreeLookPrevious = 0x400,
        ShotUncharged = 0x800,
        ShotMissile = 0x1000,
        ShotCharged = 0x2000,
        GunOpenAnimation = 0x4000,
        Grounded = 0x8000,
        GroundedPrevious = 0x10000,
        Walking = 0x20000,
        MovingBiped = 0x40000,
        NoAttack = 0x80000,
        Boosting = 0x100000,
        Targeted = 0x200000,
        UsedJumpPad = 0x400000,
        AltDirOverride = 0x800000,
        CenterAltFormCamera = 0x1000000,
        DrawGunSmoke = 0x2000000,
        DrawMuzzleEffect = 0x4000000,
        DrawBallDeath = 0x8000000,
        HideModel = 0x10000000
    }

    public readonly struct PlayerValues
    {
        public readonly Hunter Hunter; // the game doesn't have this
        public readonly int WalkBipedTraction;
        public readonly int StrafeBipedTraction;
        public readonly int WalkSpeedCap;
        public readonly int StrafeSpeedCap;
        public readonly int AltMinHSpeed;
        public readonly int BoostSpeedCap;
        public readonly int BipedGravity;
        public readonly int AltAirGravity;
        public readonly int AltGroundGravity;
        public readonly int JumpSpeed;
        public readonly int WalkSpeedFactor;
        public readonly int AltGroundSpeedFactor;
        public readonly int StrafeSpeedFactor;
        public readonly int AirSpeedFactor;
        public readonly int StandSpeedFactor;
        public readonly int RollAltTraction;
        public readonly int AltColRadius;
        public readonly int AltColYPos;
        public readonly ushort BoostChargeMin;
        public readonly ushort BoostChargeMax;
        public readonly int BoostSpeedMin;
        public readonly int BoostSpeedMax;
        public readonly int AltHSpeedCapIncrement;
        public readonly int Field58;
        public readonly int Field5C;
        public readonly int WalkBobMax;
        public readonly int AimDistance;
        public readonly ushort CamSwitchTime;
        public readonly ushort Padding6A;
        public readonly int NormalFov;
        public readonly int ZoomSensitivityFactor;
        public readonly int AimYOffset;
        public readonly int Field78;
        public readonly int Field7C;
        public readonly int Field80;
        public readonly int Field84;
        public readonly int Field88;
        public readonly int Field8C;
        public readonly int Field90;
        public readonly int MinPickupHeight;
        public readonly int MaxPickupHeight;
        public readonly int BipedColRadius;
        public readonly int LockOnTolerance; // unused FH leftover
        public readonly int LockOnMinDistance; // unused FH leftover
        public readonly int LockOnMaxDistance; // unused FH leftover
        public readonly short DamageInvuln;
        public readonly ushort DamageFlashTime;
        public readonly int FieldB0;
        public readonly int FieldB4;
        public readonly int FieldB8;
        public readonly int MuzzleOffset;
        public readonly int BombCooldown;
        public readonly int BombSelfRadius;
        public readonly int BombSelfRadiusSquared;
        public readonly int BombRadius;
        public readonly int BombRadiusSquared;
        public readonly int BombJumpSpeed;
        public readonly int BombRefillTime;
        public readonly short BombDamage;
        public readonly short BombEnemyDamage;
        public readonly short LockOnSnapTime; // unused FH leftover
        public readonly short SpawnInvulnerability;
        public readonly ushort AimMinTouchTime;
        public readonly ushort PaddingE6;
        public readonly int AutoAimFindTolerance; // unused FH leftover
        public readonly int AutoAimHoldTolerance; // unused FH leftover
        public readonly int SwayStartTime;
        public readonly int SwayIncrement;
        public readonly int SwayLimit;
        public readonly int GunIdleTime;
        public readonly short MpAmmoCap;
        public readonly byte AmmoRecharge;
        public readonly byte Padding103;
        public readonly ushort EnergyTank;
        public readonly short AmmoTank; // unused FH leftover
        public readonly byte AltFormStrafe;
        public readonly byte Padding109;
        public readonly ushort Padding10A;
        public readonly int FallDamageSpeed;
        public readonly int FallDamageMax;
        public readonly int ViewTiltIncrement;
        public readonly int ViewTiltFactor;
        public readonly int JumpPadSlideFactor;
        public readonly int AltTiltAngleCap;
        public readonly int AltMinWobble;
        public readonly int AltMaxWobble;
        public readonly int AltMinSpinAccel;
        public readonly int AltMaxSpinAccel;
        public readonly int AltMinSpinSpeed;
        public readonly int AltMaxSpinSpeed;
        public readonly int AltTiltAngleMax;
        public readonly int AltBounceWobble;
        public readonly int AltBounceTilt;
        public readonly int AltBounceSpin;
        public readonly int AltAttackKnockbackAccel;
        public readonly short AltAttackKnockbackTime;
        public readonly ushort AltAttackStartup;
        public readonly int Field154; // unused -- 409 (0.1f)
        public readonly int Field158; // unused -- 1024 (0.25f)
        public readonly int LungeHSpeed;
        public readonly int LungeVSpeed;
        public readonly ushort AltAttackDamage;
        public readonly short AltAttackCooldown;

        public PlayerValues(Hunter hunter, int walkBipedTraction, int strafeBipedTraction, int walkSpeedCap, int strafeSpeedCap,
            int altMinHSpeed, int boostSpeedCap, int bipedGravity, int altAirGravity, int altGroundGravity, int jumpSpeed, int walkSpeedFactor,
            int altGroundSpeedFactor, int strafeSpeedFactor, int airSpeedFactor, int standSpeedFactor, int rollAltTraction, int altColRadius,
            int altColYPos, ushort boostChargeMin, ushort boostChargeMax, int boostSpeedMin, int boostSpeedMax, int altHSpeedCapIncrement,
            int field58, int field5C, int walkBobMax, int aimDistance, ushort camSwitchTime, ushort padding6A, int normalFov, int zoomSensitivityFactor,
            int aimYOffset, int field78, int field7C, int field80, int field84, int field88, int field8C, int field90, int minPickupHeight,
            int maxPickupHeight, int bipedColRadius, int lockOnTolerance, int lockOnMinDistance, int lockOnMaxDistance, short damageInvuln, ushort damageFlashTime,
            int fieldB0, int fieldB4, int fieldB8, int muzzleOffset, int bombCooldown, int bombSelfRadius, int bombSelfRadiusSquared,
            int bombRadius, int bombRadiusSquared, int bombJumpSpeed, int bombRefillTime, short bombDamage, short bombEnemyDamage,
            short lockOnSnapTime, short spawnInvulnerability, ushort aimMinTouchTime, ushort paddingE6, int autoAimFindTolerance, int autoAimHoldTolerance, int swayStartTime,
            int swayIncrement, int swayLimit, int gunIdleTime, short mpAmmoCap, byte ammoRecharge, byte padding103, ushort energyTank,
            short ammoTank, byte altFormStrafe, byte padding109, ushort padding10A, int fallDamageSpeed, int fallDamageMax, int viewTiltIncrement,
            int viewTiltFactor, int jumpPadSlideFactor, int altTiltAngleCap, int altMinWobble, int altMaxWobble, int altMinSpinAccel, int altMaxSpinAccel,
            int altMinSpinSpeed, int altMaxSpinSpeed, int altTiltAngleMax, int altBounceWobble, int altBounceTilt, int altBounceSpin,
            int altAttackKnockbackAccel, short altAttackKnockbackTime, ushort altAttackStartup, int field154, int field158, int lungeHSpeed,
            int lungeVSpeed, ushort altAttackDamage, short altAttackCooldown)
        {
            Hunter = hunter;
            WalkBipedTraction = walkBipedTraction;
            StrafeBipedTraction = strafeBipedTraction;
            WalkSpeedCap = walkSpeedCap;
            StrafeSpeedCap = strafeSpeedCap;
            AltMinHSpeed = altMinHSpeed;
            BoostSpeedCap = boostSpeedCap;
            BipedGravity = bipedGravity;
            AltAirGravity = altAirGravity;
            AltGroundGravity = altGroundGravity;
            JumpSpeed = jumpSpeed;
            WalkSpeedFactor = walkSpeedFactor;
            AltGroundSpeedFactor = altGroundSpeedFactor;
            StrafeSpeedFactor = strafeSpeedFactor;
            AirSpeedFactor = airSpeedFactor;
            StandSpeedFactor = standSpeedFactor;
            RollAltTraction = rollAltTraction;
            AltColRadius = altColRadius;
            AltColYPos = altColYPos;
            BoostChargeMin = boostChargeMin;
            BoostChargeMax = boostChargeMax;
            BoostSpeedMin = boostSpeedMin;
            BoostSpeedMax = boostSpeedMax;
            AltHSpeedCapIncrement = altHSpeedCapIncrement;
            Field58 = field58;
            Field5C = field5C;
            WalkBobMax = walkBobMax;
            AimDistance = aimDistance;
            CamSwitchTime = camSwitchTime;
            Padding6A = padding6A;
            NormalFov = normalFov;
            ZoomSensitivityFactor = zoomSensitivityFactor;
            AimYOffset = aimYOffset;
            Field78 = field78;
            Field7C = field7C;
            Field80 = field80;
            Field84 = field84;
            Field88 = field88;
            Field8C = field8C;
            Field90 = field90;
            MinPickupHeight = minPickupHeight;
            MaxPickupHeight = maxPickupHeight;
            BipedColRadius = bipedColRadius;
            LockOnTolerance = lockOnTolerance;
            LockOnMinDistance = lockOnMinDistance;
            LockOnMaxDistance = lockOnMaxDistance;
            DamageInvuln = damageInvuln;
            DamageFlashTime = damageFlashTime;
            FieldB0 = fieldB0;
            FieldB4 = fieldB4;
            FieldB8 = fieldB8;
            MuzzleOffset = muzzleOffset;
            BombCooldown = bombCooldown;
            BombSelfRadius = bombSelfRadius;
            BombSelfRadiusSquared = bombSelfRadiusSquared;
            BombRadius = bombRadius;
            BombRadiusSquared = bombRadiusSquared;
            BombJumpSpeed = bombJumpSpeed;
            BombRefillTime = bombRefillTime;
            BombDamage = bombDamage;
            BombEnemyDamage = bombEnemyDamage;
            LockOnSnapTime = lockOnSnapTime;
            SpawnInvulnerability = spawnInvulnerability;
            AimMinTouchTime = aimMinTouchTime;
            PaddingE6 = paddingE6;
            AutoAimFindTolerance = autoAimFindTolerance;
            AutoAimHoldTolerance = autoAimHoldTolerance;
            SwayStartTime = swayStartTime;
            SwayIncrement = swayIncrement;
            SwayLimit = swayLimit;
            GunIdleTime = gunIdleTime;
            MpAmmoCap = mpAmmoCap;
            AmmoRecharge = ammoRecharge;
            Padding103 = padding103;
            EnergyTank = energyTank;
            AmmoTank = ammoTank;
            AltFormStrafe = altFormStrafe;
            Padding109 = padding109;
            Padding10A = padding10A;
            FallDamageSpeed = fallDamageSpeed;
            FallDamageMax = fallDamageMax;
            ViewTiltIncrement = viewTiltIncrement;
            ViewTiltFactor = viewTiltFactor;
            JumpPadSlideFactor = jumpPadSlideFactor;
            AltTiltAngleCap = altTiltAngleCap;
            AltMinWobble = altMinWobble;
            AltMaxWobble = altMaxWobble;
            AltMinSpinAccel = altMinSpinAccel;
            AltMaxSpinAccel = altMaxSpinAccel;
            AltMinSpinSpeed = altMinSpinSpeed;
            AltMaxSpinSpeed = altMaxSpinSpeed;
            AltTiltAngleMax = altTiltAngleMax;
            AltBounceWobble = altBounceWobble;
            AltBounceTilt = altBounceTilt;
            AltBounceSpin = altBounceSpin;
            AltAttackKnockbackAccel = altAttackKnockbackAccel;
            AltAttackKnockbackTime = altAttackKnockbackTime;
            AltAttackStartup = altAttackStartup;
            Field154 = field154;
            Field158 = field158;
            LungeHSpeed = lungeHSpeed;
            LungeVSpeed = lungeVSpeed;
            AltAttackDamage = altAttackDamage;
            AltAttackCooldown = altAttackCooldown;
        }
    }
}
