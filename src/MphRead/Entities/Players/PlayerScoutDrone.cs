using System;
using System.Collections.Generic;
using MphRead.Formats;
using MphRead.Hud;
using MphRead.Mods.Multiplayer;
using OpenTK.Mathematics;

namespace MphRead.Entities;

// Adapted from SecondHunt 41452f2 PlayerScoutDrone (MIT), through the existing
// native local player/form/projectile owners. No donor host flags or packets.
public partial class PlayerEntity
{
    private const int DroneUnfoldSteps = 32, DroneFireSteps = 150, DroneFoldSteps = 24, DroneWaitSteps = 120;
    private ModelInstance? _droneModel;
    private EquipInfo? _droneEquip;
    private BeamProjectileEntity[]? _droneBeams;
    private int _droneFireState, _droneFireFrame, _droneCooldown, _droneVolley;
    private bool _dronePadFall;
    private float _droneFloorGap = -1;
    private readonly int[] _droneShotsBySide = new int[2];

    internal bool ScoutDroneEnabled
    {
        get
        {
            // The rule getter retires a lost Scene/player/rules owner. Every
            // emission checks this gate again; native Spawn is not admission.
            return ModCharacterGameplay == CharacterGameplayRules.TrooperDroneV1;
        }
    }
    internal int DroneFireState => _droneFireState;
    internal int DroneFireFrame => _droneFireFrame;
    internal int DroneCooldown => _droneCooldown;
    internal int DroneShots { get; private set; }
    internal int DroneDamageTotal { get; private set; }
    internal int DroneBursts { get; private set; }
    internal int DroneMuzzleClamps { get; private set; }
    internal IReadOnlyList<int> DroneShotsBySide => _droneShotsBySide;
    internal IReadOnlyList<BeamProjectileEntity> DroneBeams => _droneBeams ?? Array.Empty<BeamProjectileEntity>();
    internal HalfturretEntity DroneBody => _halfturret;
    internal float DroneFloorGap => _droneFloorGap;
    internal float DroneCap => Fixed.ToFloat(Values.MaxPickupHeight - Values.MinPickupHeight);
    internal bool ModScoutDroneOwnsBeam(BeamProjectileEntity beam) => ReferenceEquals(beam.Owner, this)
        && _droneBeams != null && Array.IndexOf(_droneBeams, beam) >= 0;

    // Before base.Initialize registers the player's models. Resources are private
    // to the actual initialized owner, not a global catalog or imported skin.
    private void ScoutDroneInitialize()
    {
        if (!ScoutDroneEnabled) return;
        _droneModel = _scene.GetModelInstance(Metadata.EnemyModelNames[23]); // native Psycho Bit fallback
        _droneModel.SetAnimation(3);
        _models.Add(_droneModel);
        _droneBeams ??= SceneSetup.CreateBeamList(40, _scene);
        _droneEquip = new EquipInfo { Weapon = _scene.WeaponRules[0], Beams = _droneBeams, InfiniteAmmo = true };
    }

    private void ScoutDroneResetBurst(int wait)
    {
        _droneFireState = _droneFireFrame = _droneVolley = 0;
        _droneCooldown = wait;
    }

    private void ScoutDroneRetireFlights()
    {
        if (_droneBeams == null) return;
        foreach (var beam in _droneBeams)
        {
            // Destroy releases effects/audio/references; Remove releases Scene
            // membership too. Both are idempotent for an unused pool member.
            if (beam.Owner != null || beam.Lifespan > 0) beam.Destroy();
            if (_scene.TryGetEntity(beam.Id, out var registered) && ReferenceEquals(registered, beam))
                _scene.RemoveEntity(beam);
        }
    }

    private void ScoutDroneResetForLife()
    {
        ScoutDroneResetBurst(0);
        _dronePadFall = false;
        _droneFloorGap = -1;
        ScoutDroneRetireFlights();
    }

    private void ScoutDroneRetire()
    {
        ScoutDroneResetForLife();
        if (_droneEquip != null && _halfturret != null && Flags2.TestFlag(PlayerFlags2.Halfturret))
        {
            // A retired practice body cannot silently become an autonomous
            // native turret when the rule getter falls back to Native.
            _halfturret.Health = 0;
            OnHalfturretDied();
            _halfturret.Destroy();
            if (_scene.TryGetEntity(_halfturret.Id, out var body) && ReferenceEquals(body, _halfturret))
                _scene.RemoveEntity(_halfturret);
        }
        if (_droneModel != null) _models.Remove(_droneModel);
        _droneModel = null;
        _droneEquip = null;
        _droneBeams = null;
    }

    private void ScoutDroneMerge()
    {
        if (ScoutDroneEnabled && _droneFireState != 0) ScoutDroneResetBurst(DroneWaitSteps);
        _dronePadFall = false;
    }

    // Called once per native simulation input step, including frozen steps.
    // Biped cooldown continues instead of being reset by a quick split/merge.
    private void ScoutDroneStep(bool mergedThisStep = false)
    {
        if (!ScoutDroneEnabled || mergedThisStep) return;
        if (_health <= 0 || IsBot || Flags2.TestFlag(PlayerFlags2.Spectating)
            || !LoadFlags.TestFlag(LoadFlags.Active) || !LoadFlags.TestFlag(LoadFlags.Spawned))
        { ScoutDroneResetForLife(); return; }
        if (!IsAltForm || IsMorphing || IsUnmorphing || _frozenTimer > 0 || AttachedEnemy != null
            || _field35C != null || _field6D0 || Flags2.TestAny(PlayerFlags2.BipedLock | PlayerFlags2.BipedStuck)
            || _scene.GameState.MatchState != MatchState.InProgress)
        {
            if (_droneFireState != 0) ScoutDroneResetBurst(DroneWaitSteps);
            else if (_droneCooldown > 0) _droneCooldown--;
            return;
        }
        if (_droneCooldown > 0 && _droneFireState == 0) _droneCooldown--;
        bool held = Controls.AltAttack.IsDown;
        switch (_droneFireState)
        {
            case 0:
                if (held && _droneCooldown == 0)
                {
                    _droneFireState = 1; _droneFireFrame = _droneVolley = 0; DroneBursts++;
                }
                break;
            case 1:
                // Release cannot abort the unfold; only a still-held trigger
                // can enter firing after all32 subsequent simulation steps.
                if (++_droneFireFrame >= DroneUnfoldSteps)
                { _droneFireState = held ? 2 : 3; _droneFireFrame = 0; }
                break;
            case 2:
                if (!held) { _droneFireState = 3; _droneFireFrame = 0; break; }
                if (_droneFireFrame % 8 == 3) ScoutDroneVolley();
                if (++_droneFireFrame >= DroneFireSteps)
                { _droneFireState = 3; _droneFireFrame = 0; }
                break;
            case 3:
                if (++_droneFireFrame >= DroneFoldSteps) ScoutDroneResetBurst(DroneWaitSteps);
                break;
        }
    }

    private void ScoutDroneVolley()
    {
        if (!ScoutDroneEnabled || !IsAltForm || _health <= 0 || _frozenTimer > 0
            || _droneEquip == null || _droneBeams == null) return;
        // Native Spawn otherwise evicts the final live pool slot. Reserve the
        // complete pair first; exhaustion cannot replace an earlier flight.
        int free = 0;
        foreach (var beam in _droneBeams) if (beam.Lifespan <= 0) free++;
        ushort damage = (ushort)(_droneVolley++ % 2 == 0 ? 2 : 3);
        if (free < 2) return;
        Vector3 center = Position.AddY(Fixed.ToFloat(Values.AltColYPos));
        Vector3 aim = NormalizeSpatialOr(_gunVec1, new Vector3(_field70, 0, _field74));
        float horizontal = MathF.Sqrt(aim.X * aim.X + aim.Z * aim.Z);
        Vector3 right = horizontal < .0001f ? NormalizeSpatialOr(new Vector3(_field74, 0, -_field70), Vector3.UnitX)
            : new Vector3(aim.Z / horizontal, 0, -aim.X / horizontal);
        _droneEquip.UnchargedDamage = _droneEquip.HeadshotDamage = damage;
        _droneEquip.ChargeLevel = 0;
        BeamSpawnFlags flags = _doubleDmgTimer > 0 ? BeamSpawnFlags.DoubleDamage
            : IsPrimeHunter ? BeamSpawnFlags.PrimeHunter : BeamSpawnFlags.None;
        bool fired = false;
        for (int side = 0; side < 2; side++)
        {
            // Recheck even within a pair; a future callback cannot carry the
            // already-approved first muzzle into another owner or live session.
            if (!ScoutDroneEnabled) return;
            Vector3 muzzle = center + right * (side == 0 ? -.10f : .10f) + aim * .73f;
            var hit = new CollisionResult();
            if (CollisionDetection.CheckBetweenPoints(center, muzzle, TestFlags.Beams, _scene, ref hit))
            {
                Vector3 toWall = hit.Position - center;
                float length = toWall.Length;
                muzzle = length > .05f ? center + toWall * ((length - .05f) / length) : center;
                DroneMuzzleClamps++;
            }
            if (BeamProjectileEntity.Spawn(this, _droneEquip, muzzle, aim, flags, NodeRef, _scene) != BeamResultFlags.NoSpawn)
            { fired = true; DroneShots++; _droneShotsBySide[side]++; DroneDamageTotal += damage; }
        }
        if (fired)
        {
            _timeSinceShot = 0;
            _soundSource.PlaySfx(Metadata.BeamSfx[(int)BeamType.PowerBeam, (int)BeamSfx.Shot]);
        }
    }

    // Native pad launch and lock win until the apex. On descent use biped
    // gravity and cut horizontal velocity once to retain the authored landing.
    private bool ScoutDroneHover()
    {
        if (!ScoutDroneEnabled || !IsAltForm || _health <= 0 || _frozenTimer > 0
            || AttachedEnemy != null || Flags2.TestAny(PlayerFlags2.BipedLock | PlayerFlags2.BipedStuck | PlayerFlags2.GravityOverride)) return false;
        bool pad = Flags1.TestFlag(PlayerFlags1.UsedJumpPad);
        if (!pad) _dronePadFall = false;
        if (pad && (_jumpPadControlLock > 0 || Speed.Y > 0)) return false;
        bool rise = Controls.Jump.IsDown;
        if (pad)
        {
            if (!_dronePadFall)
            {
                _dronePadFall = true;
                float cut = MathF.Sqrt(Fixed.ToFloat(Values.BipedGravity) / Fixed.ToFloat(Values.AltAirGravity));
                Speed = new Vector3(Speed.X * cut, Speed.Y, Speed.Z * cut);
                _jumpPadAccel = new Vector3(_jumpPadAccel.X * cut, _jumpPadAccel.Y, _jumpPadAccel.Z * cut);
            }
            if (!rise) return false;
        }
        if (!rise && Flags1.TestFlag(PlayerFlags1.Standing)) { _droneFloorGap = 0; return false; }
        float cap = DroneCap, radius = Fixed.ToFloat(Values.AltColRadius);
        Vector3 center = Position.AddY(Fixed.ToFloat(Values.AltColYPos));
        var result = new CollisionResult();
        _droneFloorGap = -1;
        if (CollisionDetection.CheckBetweenPoints(center, center.AddY(-(radius + cap + 4)), TestFlags.Players, _scene, ref result))
            _droneFloorGap = MathF.Max(0, center.Y - radius - result.Position.Y);
        if (pad && (_droneFloorGap < 0 || _droneFloorGap > cap)) return false;
        float target, accel = .012f;
        if (_droneFloorGap < 0 || _droneFloorGap > cap)
        {
            float over = _droneFloorGap < 0 ? 1 : _droneFloorGap - cap;
            target = MathF.Max(-.3f, -.06f - over * .6f); accel = .03f;
            if (Speed.Y > 0) Speed = Speed.WithY(0);
        }
        else target = rise ? MathF.Min(.1f, (cap - _droneFloorGap) * .5f) : -.06f;
        Speed = Speed.AddY(Math.Clamp(target - Speed.Y, -accel, accel));
        // Deceleration alone can carry a rising sphere over the cap. Bound the
        // completed native half-tick displacement as well as its target speed.
        if (_droneFloorGap >= 0 && _droneFloorGap <= cap && Speed.Y > 0)
            Speed = Speed.WithY(MathF.Min(Speed.Y, (cap - _droneFloorGap) * 2));
        return true;
    }

    private void DrawScoutDrone(Vector4? brightSkin, Vector4? outline, Vector3 drawPosition, Vector3 drawFacing)
    {
        if (_droneModel == null) return;
        Vector3 facing = NormalizeSpatialOr(new Vector3(drawFacing.X, 0, drawFacing.Z), Vector3.UnitZ);
        Matrix4 transform = Matrix4.CreateScale(.45f)
            * GetTransformMatrix(facing, Vector3.UnitY, drawPosition.AddY(Fixed.ToFloat(Values.AltColYPos)));
        UpdateTransforms(_droneModel, transform, 0);
        GetDrawItems(_droneModel, _droneModel.Model.Nodes[0], _curAlpha, 0, overrideColor: brightSkin, outlineColor: outline);
    }

    private void DrawScoutDroneHud()
    {
        string state = _droneFireState switch { 1 => "UNFOLDING", 2 => "FIRING", 3 => "FOLDING",
            _ => _droneCooldown > 0 ? "COOLING " + ((_droneCooldown + 59) / 60) : "READY" };
        DrawText2D(128, 132, Align.Center, 0, "DRONE " + state);
        DrawText2D(128, 142, Align.Center, 0, "JUMP: HOVER / ALT ATTACK: BURST");
        // Native reticle projected along the same simulation aim as both muzzles.
        Vector3 center = Position.AddY(Fixed.ToFloat(Values.AltColYPos));
        Vector3 aim = NormalizeSpatialOr(_gunVec1, Vector3.UnitZ);
        Vector3 at = center + aim * 40;
        var hit = new CollisionResult();
        if (CollisionDetection.CheckBetweenPoints(center, at, TestFlags.Beams, _scene, ref hit)) at = hit.Position;
        Matrix.ProjectPosition(at, _scene.ViewMatrix, _scene.PerspectiveMatrix, out Vector2 position);
        if (float.IsFinite(position.X) && float.IsFinite(position.Y))
        {
            _targetCircleInst.PositionX = position.X; _targetCircleInst.PositionY = position.Y;
            _targetCircleInst.Alpha = Features.ReticleOpacity;
            _scene.DrawHudObject(_targetCircleInst, scale: Mods.Render.Crosshair.Scale);
        }
    }
}
