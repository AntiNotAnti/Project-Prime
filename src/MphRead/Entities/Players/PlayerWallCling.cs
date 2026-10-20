using System;
using MphRead.Formats;
using MphRead.Mods.Multiplayer;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.Entities;

public partial class PlayerEntity
{
    // Proposed PRIVATE/offline mechanics slice. Native remains the default;
    // no launcher, wire, replay or career path binds TrooperV1 in this slice.
    private CharacterGameplayRules _modCharacterGameplay = CharacterGameplayRules.Native;
    private bool _modCharacterGameplayBound, _modCharacterGameplayRetired;
    private bool _modCharacterGameplayInitializationStarted;
    private Hunter _modCharacterGameplayHunter;
    private long _modCharacterGameplayScene;
    internal CharacterGameplayRules ModCharacterGameplay
    {
        get
        {
            if (_modCharacterGameplayBound && _modCharacterGameplay.Variant != CharacterGameplayVariant.Native
                && !ModWallClingEnabled) ModRetireCharacterGameplay();
            return _modCharacterGameplay;
        }
    }

    // Explicit synthetic rule selection for the private CPU fixture. It must
    // happen before Initialize, once, on the actual local player/Scene owner.
    internal bool ModTryBindCharacterGameplay(CharacterGameplayRules rules)
    {
        if (_scene.ForegroundLease.ThreadId != Environment.CurrentManagedThreadId
            || _modCharacterGameplayBound || _modCharacterGameplayInitializationStarted || _scene.Exiting
            || _scene.Services.IsReplica || _scene.SideScene || _scene.IsReplayLab || NetSession.Active
            || !ReferenceEquals(global::MphRead.GameState.Current, _scene.GameState)
            || !ReferenceEquals(LegacyRegistry, _scene.Players)
            || !rules.IsValidFor(Hunter)
            || (uint)SlotIndex >= _scene.Players.Items.Count
            || !ReferenceEquals(_scene.Players.Items[SlotIndex], this)) return false;
        _modCharacterGameplay = rules;
        _modCharacterGameplayBound = true;
        _modCharacterGameplayHunter = Hunter;
        _modCharacterGameplayScene = _scene.LifetimeGeneration;
        WallClingInit();
        return true;
    }

    private bool ModWallClingEnabled => !_modCharacterGameplayRetired
        && _modCharacterGameplayBound
        && _modCharacterGameplay == CharacterGameplayRules.TrooperV1
        && _modCharacterGameplay.IsValidFor(Hunter)
        && Hunter == _modCharacterGameplayHunter
        && _modCharacterGameplayScene == _scene.LifetimeGeneration
        && ReferenceEquals(global::MphRead.GameState.Current, _scene.GameState)
        && ReferenceEquals(LegacyRegistry, _scene.Players)
        && !_scene.Exiting && !_scene.Services.IsReplica && !_scene.SideScene && !_scene.IsReplayLab && !NetSession.Active
        && !_scene.GameState.BalancedMode && !_scene.GameState.EnhancedHunters
        && (uint)SlotIndex < _scene.Players.Items.Count
        && ReferenceEquals(_scene.Players.Items[SlotIndex], this);

    internal void ModRetireCharacterGameplay()
    {
        _modCharacterGameplayRetired = true;
        _modCharacterGameplay = CharacterGameplayRules.Native;
        WallClingInit();
    }

    private bool WallClingMovementBlocked => Flags2.TestAny(PlayerFlags2.BipedLock | PlayerFlags2.BipedStuck)
        || Flags1.TestFlag(PlayerFlags1.UsedJumpPad) || _jumpPadControlLock > 0;

    private bool ModPrepareWallCling()
    {
        if (!ModWallClingEnabled)
        {
            if (_modCharacterGameplayBound && _modCharacterGameplay.Variant != CharacterGameplayVariant.Native)
                ModRetireCharacterGameplay();
            return false;
        }
        if (_health <= 0 || IsBot || Flags2.TestFlag(PlayerFlags2.Spectating)
            || !LoadFlags.TestFlag(LoadFlags.Active)) { WallClingInit(); return false; }
        // Existing native movement owners win. In particular, never zero a
        // pad launch or manufacture a wall jump through a native biped lock.
        if (WallClingMovementBlocked || _frozenTimer > 0 || IsAltForm || IsMorphing || IsUnmorphing)
        {
            if (_clinging) { WallClingDrops++; ClingRelease(); }
            _clingKickTimer = 0;
            _clingTouched = false;
        }
        return true;
    }

    private void WallClingNoteJump()
    {
        if (ModPrepareWallCling()) _clingRose = true;
    }

    private void ModResetWallClingForLife()
    {
        if (_modCharacterGameplayBound && !_modCharacterGameplay.IsValidFor(Hunter))
            ModRetireCharacterGameplay();
        WallClingInit();
    }

    // Current local SecondHunt authored defaults, adapted to this existing
    // native 60Hz player/collision owner. No donor network or draw code.
    internal const float WallClingMinDrop = 2.0f; // units below the highest point since he stood (taller than he is)

    internal const int WallClingTime = 150; // frames (60 Hz): 2.5 s
    internal const int WallClingChain = 3; // clings between touching the ground
    internal const float WallClingMinNormalY = -0.0175f; // sin(1 degree): the most a wall may overhang
    internal const float WallClingMaxNormalY = 0.0872f; // sin(5 degrees): the most a wall may lean back
    private const int _clingWait = 20; // after letting go, frames before another cling
    private const float _clingMinInto = 0.02f; // horizontal speed into the wall that counts as jumping at it
    private const float _clingSameWall = 0.7f; // cos between wall normals above which it's the wall he last left
    private const float _clingJumpPush = 0.3f; // a wall jump's horizontal speed off the wall
    private const int _clingKickTime = 14;

    private bool _clinging = false;
    private int _clingTimer = 0;
    private int _clingWaitTimer = 0;
    private int _clingKickTimer = 0;
    private Vector3 _clingNormal = Vector3.Zero; // horizontal unit normal of the wall he's on
    private Vector3 _clingLastNormal = Vector3.Zero; // the last wall he clung to since standing (zero: none)
    private int _clingChainCount = 0; // clings since he last stood
    private const float _clingProbeReach = 0.35f; // past his collision radius, how far the height rays look
    private const float _clingProbeInset = 0.12f; // the foot and head rays, this far in from his feet / head
    private float _clingTouchNormalY = 0;
    // this frame's wall contact (from HandleCollision): the face moved into the most
    private bool _clingTouched = false;
    private Vector3 _clingTouchNormal = Vector3.Zero;
    private float _clingTouchInto = 0;
    // since he last stood: frames in the air, whether he jumped (a jump or wall jump: not a walk off a ledge), and
    // the highest he's been
    private int _clingAirFrames = 0;
    private bool _clingRose = false;
    private float _clingTopY = 0;

    internal bool WallClinging => ModPrepareWallCling() && _clinging;
    internal int WallClingTimer => ModPrepareWallCling() ? _clingTimer : 0;
    internal Vector3 WallClingNormal => ModPrepareWallCling() ? _clingNormal : Vector3.Zero;
    internal float WallClingNormalY { get; private set; } // the wall face's own normal.Y (tests)
    internal int WallClingChainCount => ModPrepareWallCling() ? _clingChainCount : 0;
    internal int WallClingTooShort { get; private set; } // clings refused: the wall shorter than he is (tests)
    internal int WallClingAirFrames => _clingAirFrames; // (tests, -wallclingprobe)
    internal bool WallClingRose => _clingRose;
    internal int WallClingStartAir { get; private set; } // the last cling's air frames, jump, drop when it started (tests)
    internal bool WallClingStartRose { get; private set; }
    internal float WallClingStartDrop { get; private set; }
    // counts for tests (-wallclingtest)
    internal int WallClings { get; private set; }
    internal int WallJumps { get; private set; }
    internal int WallClingTimeouts { get; private set; }
    internal int WallClingDrops { get; private set; }
    internal int WallClingLongest { get; private set; }

    private bool WallClingHeld => ModPrepareWallCling() && _clinging;

    // Initialize (spawn and respawn)
    private void WallClingInit()
    {
        _clinging = false;
        _clingTimer = 0;
        _clingWaitTimer = 0;
        _clingKickTimer = 0;
        _clingNormal = Vector3.Zero;
        _clingLastNormal = Vector3.Zero;
        _clingChainCount = 0;
        _clingTouched = false;
        _clingTouchNormal = Vector3.Zero;
        _clingTouchNormalY = 0;
        _clingTouchInto = 0;
        WallClingNormalY = 0;
        WallClingStartAir = 0;
        WallClingStartRose = false;
        WallClingStartDrop = 0;
        _clingAirFrames = 0;
        _clingRose = false;
        _clingTopY = Position.Y;
    }

    // HandleCollision, on a wall contact (|normal.Y| < 0.1), before the push-out changes Speed. face: a collision face's
    // own plane (false: an edge or corner, whose normal MphRead makes from the contact point -- never clung to)
    private void WallClingTouch(Vector3 normal, bool face)
    {
        if (!ModPrepareWallCling() || WallClingMovementBlocked) return;
        if (!float.IsFinite(normal.X) || !float.IsFinite(normal.Y) || !float.IsFinite(normal.Z)
            || !face || normal.Y < WallClingMinNormalY || normal.Y > WallClingMaxNormalY)
        {
            return;
        }
        float len = MathF.Sqrt(normal.X * normal.X + normal.Z * normal.Z);
        if (len < 1e-4f)
        {
            return;
        }
        var n = new Vector3(normal.X / len, 0, normal.Z / len);
        float into = -(Speed.X * n.X + Speed.Z * n.Z);
        if (!_clingTouched || into > _clingTouchInto)
        {
            _clingTouched = true;
            _clingTouchNormal = n;
            _clingTouchNormalY = normal.Y;
            _clingTouchInto = into;
        }
    }

    // ProcessBiped, before walking/jumping: Jump while clinging = wall jump (true: he jumped this frame)
    private bool WallClingInput()
    {
        if (!ModPrepareWallCling()) return false;
        if (!_clinging || !Controls.Jump.IsPressed)
        {
            return false;
        }
        ClingRelease();
        Flags1 |= PlayerFlags1.UsedJump;
        float up = IsPrimeHunter ? 0.35f : Fixed.ToFloat(Values.JumpSpeed); // as a jump (PlayerInput)
        Speed = new Vector3(_clingNormal.X * _clingJumpPush, up, _clingNormal.Z * _clingJumpPush);
        _clingRose = true;
        _timeSinceGrounded = 8 * 2;
        _clingKickTimer = _clingKickTime;
        WallJumps++;
        PlayHunterSfx(HunterSfx.Jump);
        return true;
    }

    // ProcessBiped, before the walk/strafe speed is added: just after a wall jump, steering back into the wall he
    // kicked off is ignored (holding forward into the wall would otherwise pull him straight back onto it)
    private Vector3 WallClingSteer(Vector3 speedDelta)
    {
        if (!ModPrepareWallCling()) return speedDelta;
        if (_clingKickTimer > 0)
        {
            float into = speedDelta.X * _clingNormal.X + speedDelta.Z * _clingNormal.Z;
            if (into < 0)
            {
                speedDelta.X -= _clingNormal.X * into;
                speedDelta.Z -= _clingNormal.Z * into;
            }
        }
        return speedDelta;
    }

    // ProcessMovement, after a knockback's speed: on the wall nothing moves him (and gravity is skipped there)
    private void WallClingHold()
    {
        if (!ModPrepareWallCling()) return;
        _clingTouched = false;
        if (_clinging)
        {
            Speed = Vector3.Zero;
        }
    }

    // ProcessMovement, after the room collision and the grounded update: start, keep or end a cling
    private void WallClingAfterCollision()
    {
        if (!ModPrepareWallCling()) return;
        bool standing = Flags1.TestFlag(PlayerFlags1.Standing);
        if (standing)
        {
            _clingLastNormal = Vector3.Zero; // landed: any wall again
            _clingChainCount = 0;
            _clingAirFrames = 0;
            _clingRose = false;
            _clingTopY = Position.Y;
        }
        else
        {
            if (_clingAirFrames < 10000)
            {
                _clingAirFrames++;
            }
            _clingTopY = MathF.Max(_clingTopY, Position.Y);
        }
        if (_clingWaitTimer > 0)
        {
            _clingWaitTimer--;
        }
        if (_clingKickTimer > 0 && (--_clingKickTimer == 0 || standing))
        {
            _clingKickTimer = 0;
        }
        if (_clinging)
        {
            _clingTimer--;
            int held = WallClingTime - _clingTimer;
            if (held > WallClingLongest)
            {
                WallClingLongest = held;
            }
            if (_health == 0 || IsAltForm || IsMorphing || standing || _frozenTimer > 0)
            {
                WallClingDrops++;
                ClingRelease();
            }
            else if (_clingTimer <= 0)
            {
                WallClingTimeouts++;
                ClingRelease(); // falls from here (gravity is back next frame)
            }
        }
        else if (!WallClingMovementBlocked && _clingTouched && _clingTouchInto > _clingMinInto && _clingWaitTimer == 0 && !IsBot
            && _health > 0 && !standing && !Flags1.TestFlag(PlayerFlags1.Grounded) && !IsAltForm && !IsMorphing
            && !IsUnmorphing && _frozenTimer == 0 && !Flags1.TestFlag(PlayerFlags1.UsedJumpPad)
            && _clingChainCount < WallClingChain
            && (_clingRose || _clingTopY - Position.Y >= WallClingMinDrop)
            && Vector3.Dot(_clingTouchNormal, _clingLastNormal) < _clingSameWall
            && WallClingTallEnough(_clingTouchNormal))
        {
            _clinging = true;
            _clingTimer = WallClingTime;
            _clingNormal = _clingTouchNormal;
            _clingLastNormal = _clingTouchNormal;
            WallClingNormalY = _clingTouchNormalY;
            WallClingStartAir = _clingAirFrames;
            WallClingStartRose = _clingRose;
            WallClingStartDrop = _clingTopY - Position.Y;
            _clingChainCount++;
            Speed = Vector3.Zero;
            WallClings++;
            _soundSource.PlaySfx(SfxId.LAND_METAL);
        }
        _clingTouched = false;
    }

    // the wall covers him from feet to head: a ray into it (against its normal) at each height meets a face standing
    // within the allowed angles and facing the same way as the one he touched
    private bool WallClingTallEnough(Vector3 n)
    {
        float bottom = ModBipedBottom + _clingProbeInset;
        float top = ModBipedTop - _clingProbeInset;
        float reach = Fixed.ToFloat(Values.BipedColRadius) + _clingProbeReach;
        for (int i = 0; i < 3; i++)
        {
            float y = i == 0 ? bottom : i == 1 ? (bottom + top) / 2 : top;
            Vector3 from = Position.AddY(y);
            var result = new CollisionResult();
            if (!CollisionDetection.CheckBetweenPoints(from, from - n * reach, TestFlags.Players, _scene, ref result)
                || result.Plane.Y < WallClingMinNormalY || result.Plane.Y > WallClingMaxNormalY
                || result.Plane.X * n.X + result.Plane.Z * n.Z < 0.95f)
            {
                WallClingTooShort++;
                return false;
            }
        }
        return true;
    }

    private void ClingRelease()
    {
        _clinging = false;
        _clingTimer = 0;
        _clingWaitTimer = _clingWait;
    }

}
