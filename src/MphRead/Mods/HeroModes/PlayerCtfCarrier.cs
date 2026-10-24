using System;
using MphRead.Formats;
using MphRead.Hud;
using MphRead.Mods.Input;
using MphRead.Mods.HeroModes;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.Entities;

public partial class PlayerEntity
{
    private bool _ctfZoomHeld;
    private ulong _ctfLastInteractionFrame = ulong.MaxValue;
    public bool ModCarryingCtfFlag => CtfRules.Active(_scene.GameState.Mode) && OctolithFlag != null;

    internal void ModChangeCtfHunterWhileDead(Hunter hunter)
    {
        if (!CtfRules.Active(_scene.GameState.Mode) || Health > 0 || Hunter == hunter) return;
        ushort countdown = _respawnTimer;
        var deadTime = _timeSinceDead;
        var load = LoadFlags;
        var position = Position; var facing = _facingVector; var up = _upVector;
        var node = NodeRef; var previous = PrevPosition; var idle = IdlePosition;
        var speed = Speed; var gunForward = _gunVec1; var gunRight = _gunVec2; var aim = _aimPosition;
        var camera = (CameraInfo.Position, CameraInfo.PrevPosition, CameraInfo.Target,
            CameraInfo.UpVector, CameraInfo.Fov, CameraInfo.NodeRef);
        ModCancelFlagCarrierFire();
        ModPrepareHunterResources(hunter);
        ModSetHunter(hunter);
        Initialize();
        // Reinitializing graphics must not grant a life, heal, or reset the authoritative countdown.
        _health = 0; // Initialize has restored the native hunter health capacity.
        _respawnTimer = countdown; _timeSinceDead = deadTime; LoadFlags = load;
        // Initialize resets coordinates and the camera to the origin. A dead
        // roster change is not a spawn: retain the corpse's world/camera pose
        // until the normal respawn chooses a valid team-side location.
        _facingVector = facing; _upVector = up; SetTransform(facing, up, position);
        NodeRef = node; PrevPosition = previous; IdlePosition = idle; Speed = speed;
        _gunVec1 = gunForward; _gunVec2 = gunRight; _aimPosition = aim;
        _volume = CollisionVolume.Move(_volumeUnxf, position);
        CameraInfo.Position = camera.Position; CameraInfo.PrevPosition = camera.PrevPosition;
        CameraInfo.Target = camera.Target; CameraInfo.UpVector = camera.UpVector;
        CameraInfo.Fov = camera.Fov; CameraInfo.NodeRef = camera.NodeRef;
        CameraInfo.Update(); CameraInfo.ModResetDrawState();
    }

    internal void ModPrepareCtfSpawn()
    {
        if (!CtfRules.Active(_scene.GameState.Mode)) return;
        // Native Spawn owns health/equipment/ammunition. This only clears contextual input state.
        _ctfZoomHeld = Controls.Zoom.IsDown;
        _ctfLastInteractionFrame = ulong.MaxValue;
        _ctfEventText = null;
    }

    internal void ModCancelFlagCarrierFire()
    {
        if (EquipInfo.ChargeLevel > 0) StopBeamChargeSfx(CurrentWeapon);
        EquipInfo.ChargeLevel = 0;
        if (EquipInfo.Zoomed) UpdateZoom(false);
        _chargeEffect?.SetDrawEnabled(false);
        _muzzleEffect?.SetDrawEnabled(false);
        // Cancel delayed, not-yet-launched rounds only; already flying projectiles remain alive.
        foreach (var beam in EquipInfo.Beams)
            if (beam.Owner == this && beam.Age < 0) beam.Lifespan = 0;
    }

    internal bool ModProcessCtfInteraction()
    {
        if (!CtfRules.Active(_scene.GameState.Mode)) return false;
        bool pressed = Controls.Zoom.IsPressed && !_ctfZoomHeld && _ctfLastInteractionFrame != _scene.FrameCount;
        _ctfZoomHeld = Controls.Zoom.IsDown;
        if (Health <= 0 || IsAltForm || IsMorphing || IsUnmorphing || !ModIsInPlay) return false;
        if (ModCarryingCtfFlag)
        {
            ModCancelFlagCarrierFire();
            if (pressed)
            {
                _ctfLastInteractionFrame = _scene.FrameCount;
                if (!NetObjectiveSync.IsClient(_scene) && !_scene.Services.IsReplica)
                    OctolithFlag!.TryThrowCtf(this, _gunVec1);
            }
            return true;
        }
        foreach (var flag in _scene.GetOctolithFlagEntities())
        {
            if (!flag.CanPickupCtf(this)) continue;
            if (!pressed) return false;
            _ctfLastInteractionFrame = _scene.FrameCount;
            if (!NetObjectiveSync.IsClient(_scene) && !_scene.Services.IsReplica) flag.TryPickupCtf(this);
            return true;
        }
        return false;
    }

    private string ModCtfBindingLabel()
    {
        string key = Controls.Zoom.Type switch
        {
        ButtonType.Key => Controls.Zoom.Key.ToString(),
        ButtonType.Mouse => "Mouse " + Controls.Zoom.MouseButton,
        ButtonType.ScrollUp => "Wheel up", ButtonType.ScrollDown => "Wheel down", _ => "Zoom / ADS"
        };
        GamepadButtons pad = PadBindings.Get(PadAction.Zoom);
        return pad == GamepadButtons.None ? key : key + " / " + PadBindings.Describe(pad);
    }

    private readonly CtfFlagModel _ctfViewFlag = new();
    internal void ModDrawFirstPersonFlag()
    {
        if (OctolithFlag == null) return;
        // GunPosition is the rear of a barrel, almost at the camera's near
        // plane. A vertical flag rooted there straddles/behind that plane and
        // disappears. Attach the pole in front of the final, late-latched camera
        // instead, with its banner on the right and clear of the crosshair.
        Matrix4 camera = _scene.ViewMatrix.Inverted();
        Vector3 right = camera.Row0.Xyz.Normalized();
        Vector3 up = camera.Row1.Xyz.Normalized();
        Vector3 forward = -camera.Row2.Xyz.Normalized();
        Vector3 origin = camera.Row3.Xyz + forward * 1.1f + right * .40f - up * .52f;
        _ctfViewFlag.Draw(_scene, origin, right, up, forward, .5f, OctolithFlag.Data.TeamId);
    }
}
