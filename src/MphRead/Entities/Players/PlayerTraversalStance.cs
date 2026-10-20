using System;
using MphRead.Formats;
using MphRead.Formats.Collision;
using MphRead.Mods.Movement;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.Entities;

public partial class PlayerEntity
{
    private bool _modCrouched, _modCrouchWasDown;
    private int _modSlideTicks, _modSlideEntryTicks;
    private ulong _modStanceFrame = ulong.MaxValue;
    private bool? _modHistoricalCrouched, _modHistoricalTucked;
    private bool _modTucked;
    private byte _modTuckRebases;
    internal bool ModTucked => ModCrouched && _modTucked && _scene.GameState.MovementModel == MovementModel.Parkour;
    internal byte ModTuckRebases => _modTuckRebases;
    internal float ModTuckRise => ParkourStance.TuckRise;
    private bool ModCollisionTucked => _modHistoricalTucked ?? ModTucked;
    internal bool ModCrouched => _modCrouched && (_scene.GameState.MovementModel is MovementModel.Traversal or MovementModel.Parkour)
        && !IsAltForm && !IsMorphing && !IsUnmorphing;
    internal bool ModSliding => ModCrouched && _modSlideTicks > 0;
    internal bool ModCollisionCrouched => _modHistoricalCrouched ?? ModCrouched;
    private float ModNativeBipedBottom => Fixed.ToFloat(Values.MinPickupHeight);
    internal float ModBipedBottom => ModBipedBottomFor(ModCollisionCrouched, ModCollisionTucked);
    internal float ModBipedBottomFor(bool crouched, bool tucked) => ParkourStance.Bottom(ModNativeBipedBottom, crouched, tucked);
    internal float ModBipedTop => ModBipedTopFor(ModCollisionCrouched, ModCollisionTucked);
    internal float ModBipedTopFor(bool crouched, bool tucked = false) => ParkourStance.Top(ModNativeBipedBottom,
        Fixed.ToFloat(Values.MaxPickupHeight), Fixed.ToFloat(Values.BipedColRadius), crouched, tucked);
    internal float ModBipedEyeOffset => TraversalStance.EyeOffset(ModNativeBipedBottom,
        Fixed.ToFloat(Values.MaxPickupHeight), Fixed.ToFloat(Values.BipedColRadius),
        Fixed.ToFloat(Values.AimYOffset), ModCrouched) + (ModTucked ? ModTuckRise : 0);

    private bool ModCrouchBodyAllowed => (_scene.GameState.MovementModel is MovementModel.Traversal or MovementModel.Parkour)
        && ModCharacterGameplay.Variant == Mods.Multiplayer.CharacterGameplayVariant.Native
        && Hunter is >= Hunter.Samus and <= Hunter.Weavel
        && _health > 0 && LoadFlags.TestFlag(LoadFlags.Spawned)
        && !IsAltForm && !IsMorphing && !IsUnmorphing
        && !Flags2.TestFlag(PlayerFlags2.Spectating);

    internal void ModResetTraversalStance()
    {
        ModStopTraversalSlideSfx();
        _modCrouched = _modCrouchWasDown = _modTucked = _modTuckCameraSuppressed = false;
        _modCrouchPresentation.Reset(); _modCrouchGait.Reset();
        _modSlidePresentation.Reset(); _modCrouchWalk.Reset(); _modCrouchWalkReach = 0; _modTraversalPictureFrame = ulong.MaxValue;
        _modSlideTicks = _modSlideEntryTicks = 0; _modStanceFrame = ulong.MaxValue; _modHistoricalCrouched = _modHistoricalTucked = null;
        if (!_scene.Quake3TechniquesActive || !ModMomentumBodyEligible)
            ModResetGroundTravel();
    }

    // Native standing clearance, with fixed scratch capacity. Grow only when
    // the full body fits; saturated queries fail closed.
    private bool ModCanStand() => ModCanOccupyBiped(crouched: false);
    private bool ModCanOccupyBiped(bool crouched, bool tucked = false, Vector3? origin = null)
    {
        Vector3 position = origin ?? Position;
        float nativeBottom = ModBipedBottomFor(crouched, tucked);
        float nativeTop = ModBipedTopFor(crouched, tucked);
        float radius = Fixed.ToFloat(Values.BipedColRadius);
        float bottom = nativeBottom + radius;
        float top = nativeTop - radius;
        Vector3 start = position.AddY(bottom), end = position.AddY(top);
        using var candidates = CollisionDetection.GetCandidatesForLimits(start, end, radius + .05f,
            null, Vector3.Zero, includeEntities: true, _scene);
        for (int probe = 0; probe < 3; probe++)
        {
            float y = probe == 0 ? bottom : probe == 1 ? (bottom + top) * .5f : top;
            Array.Clear(_penetrationScratch);
            int count = CollisionDetection.CheckSpherePenetration(candidates, position.AddY(y), radius,
                _penetrationScratch.Length, TestFlags.Players, _penetrationScratch);
            if (count >= _penetrationScratch.Length) return false;
            for (int index = 0; index < count; index++)
                if (_penetrationScratch[index].Field14 > CollisionRecoverySkin) return false;
        }
        // Cover the continuous capsule between the probes as well, including
        // thin edge obstructions and moving entity collision. Three spheres
        // alone leave gaps near the side of a full-height capsule.
        float clearanceRadius = Math.Max(.01f, radius - CollisionRecoverySkin);
        Array.Clear(_movementCollisionScratch);
        int blockers = CollisionDetection.CheckSphereBetweenPointsRobust(candidates, start, end,
            clearanceRadius, _movementCollisionScratch.Length, includeOffset: true,
            TestFlags.Players, _scene, _movementCollisionScratch);
        if (blockers != 0) return false;
        // Native finite-edge response uses the full biped cylinder, including
        // its flat top/bottom. Check polygon edges against that same extent;
        // a sphere-cap-only clearance could otherwise allow a side rail into
        // the newly standing body.
        Vector3 cylinderBottom = position.AddY(nativeBottom + CollisionRecoverySkin);
        float cylinderHeight = nativeTop - nativeBottom - 2 * CollisionRecoverySkin;
        candidates.SeenFaces.Clear();
        for (int index = 0; index < candidates.Count; index++)
        {
            var candidate = candidates[index];
            if (candidate.Collision.Info is not MphCollisionInfoBase info) continue;
            for (int face = 0; face < candidate.Entry.DataCount; face++)
            {
                var data = info.RuntimeData[info.RuntimeDataIndices[candidate.Entry.DataStartIndex + face]];
                if (data.IgnorePlayers || candidate.EntityCollision == null && !candidates.SeenFaces.Add(data)) continue;
                for (int edge = 0; edge < data.PointIndexCount; edge++)
                {
                    Vector3 a = ModClearanceWorldPoint(candidate, info.Points[info.RuntimePointIndices[data.PointStartIndex + edge]]);
                    Vector3 b = ModClearanceWorldPoint(candidate, info.Points[info.RuntimePointIndices[data.PointStartIndex
                        + (edge + 1) % data.PointIndexCount]]);
                    CollisionResult contact = default;
                    if (CollisionDetection.CheckCylindersOverlap(a, b, cylinderBottom, Vector3.UnitY,
                        cylinderHeight, clearanceRadius, ref contact)) return false;
                }
            }
        }
        return true;
    }

    private static Vector3 ModClearanceWorldPoint(CollisionCandidate candidate, Vector3 point)
        => candidate.EntityCollision is { } entity ? Matrix.Vec3MultMtx4(point, entity.Transform)
            : point + candidate.Collision.Translation;

    internal void ModAdmitReportedCrouch(bool held, bool reported, bool tucked = false)
    {
        if (_scene.GameState.MovementModel == MovementModel.Parkour)
        {
            // Placement follows the existing life-fenced owner-report contract.
            // The authority independently admits body/form eligibility and fit;
            // it does not simulate the owner trajectory a second time.
            bool low = reported && ModCrouchBodyAllowed
                && (held && ModMomentumBodyEligible || !ModCanStand());
            if (low && ModCanOccupyBiped(true, tucked))
            { _modCrouched = true; _modTucked = tucked; ModCancelTraversalSlide(); }
            else if (ModCanStand()) { _modCrouched = _modTucked = false; }
            return;
        }
        // A lost entry packet can leave only a released-but-clearance-blocked
        // stance report. Independently prove both the obstruction and low fit.
        if (ModCrouchBodyAllowed && reported && (!held || !ModMomentumBodyEligible) && !_modCrouched
            && !ModCanStand() && ModCanOccupyBiped(crouched: true))
        {
            _modCrouched = true;
            if (!held) { ModCancelTraversalSlide(); _modCrouchWasDown = false; return; }
        }
        ModUpdateTraversalStance(held);
    }

    // Called once in the input phase before source-time weapon facts are frozen.
    // Puppets use admitted held input; position itself follows the existing
    // owner-report contract. Stance and clearance are derived here independently.
    internal void ModUpdateTraversalStance(bool requested, bool jumpAccepted = false)
    {
        if (!ModCrouchBodyAllowed) { ModResetTraversalStance(); return; }
        if (!ModTraversalActive || _scene.Services.IsReplica || DemoPlayback.IsActive)
        { _modSlideTicks = _modSlideEntryTicks = 0; return; }
        // Exclusions stop new stance/slide input while native abilities own
        // movement. An existing low body still needs clearance before growing
        // (for example, a Prime pickup or freeze underneath a low ceiling).
        bool down = requested && ModMomentumBodyEligible;
        bool fresh = down && !_modCrouchWasDown;
        if (down) _modCrouched = true;
        else if (_modCrouched && ModCanStand()) _modCrouched = _modTucked = false;
        // A tuck changes only the hull and source offsets. It never adds jump
        // force, translates an airborne origin, or re-arms a jump.
        if (!_modTucked && ParkourStance.CanTuck(_scene.GameState.MovementModel == MovementModel.Parkour,
            ModMomentumEligible, !Flags1.TestFlag(PlayerFlags1.Standing), down)
            && ModCanOccupyBiped(true, true))
        { _modTucked = true; _modTuckCameraSuppressed = true; }
        if (_modStanceFrame != _scene.FrameCount)
        {
            if (_modSlideTicks > 0) _modSlideTicks--;
            if (_modSlideEntryTicks > 0) _modSlideEntryTicks--;
            _modStanceFrame = _scene.FrameCount;
        }
        if (fresh) _modSlideEntryTicks = TraversalMovementProfileV5.SlideEntryTicks;
        if (!_modCrouched || !down || !ModMomentumBodyEligible || jumpAccepted
            || Flags1.TestFlag(PlayerFlags1.CollidingLateral))
            _modSlideTicks = _modSlideEntryTicks = 0;
        else if (!Flags1.TestFlag(PlayerFlags1.Standing)) _modSlideTicks = 0;
        else if (_modSlideEntryTicks > 0)
        {
            // Consume a single fresh request at the first physical floor contact.
            // A held key cannot restart an expired slide or wait for a later boost.
            _modSlideEntryTicks = 0;
            float minimum = TraversalMovementProfileV5.SlideMinimumSpeed / NativeVelocityUnitsPerSecond;
            float speedSquared = ModHasFreshGroundTangent ? Speed.LengthSquared
                : Speed.X * Speed.X + Speed.Z * Speed.Z;
            if (speedSquared >= minimum * minimum)
                _modSlideTicks = TraversalMovementProfileV5.SlideTicks;
        }
        _modCrouchWasDown = down;
    }

    private void ModCompleteTuckContact()
    {
        if (!ModTucked || !ModCrouchBodyAllowed || !_scene.MomentumOwnsPlayer(this)
            || !Flags1.TestFlag(PlayerFlags1.Standing) || Speed.Y > 0) return;
        Vector3 rise = Vector3.UnitY * ModTuckRise;
        Position += rise;
        PrevPosition += rise;
        _modTucked = false;
        _volume = CollisionVolume.Move(_volumeUnxf, Position);
        _modTuckCameraSuppressed = false;
        _modCrouchPresentation.Reset();
        _modTuckRebases = (byte)((_modTuckRebases + 1) & 15);
        // Ordinary crouch at the raised origin is precisely the same world
        // capsule and eye. The origin adjustment is not physical velocity.
    }

    internal void ModCancelTraversalSlide()
        => _modSlideTicks = _modSlideEntryTicks = 0;

    internal void ModApplyReplicatedCrouch(bool crouched, bool isLocal = false, bool tucked = false)
    {
        if (!ModCrouchBodyAllowed) { ModResetTraversalStance(); return; }
        // The owner predicts this level from newer local input. A delayed
        // standing snapshot must not cancel an earned slide, nor may an old
        // crouch snapshot re-duck a released owner. Life/form/health rules above
        // still retire prediction; fresh lives are seeded as remote state.
        if (isLocal) return;
        _modTucked = crouched && tucked && _scene.GameState.MovementModel == MovementModel.Parkour;
        _modTuckCameraSuppressed |= _modTucked;
        if (crouched) _modCrouched = true;
        else _modCrouched = false;
        if (!_modCrouched) ModCancelTraversalSlide();
    }
}
