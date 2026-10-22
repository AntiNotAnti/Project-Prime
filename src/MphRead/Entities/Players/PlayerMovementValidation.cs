using MphRead.Mods.Network;

namespace MphRead.Entities;

public partial class PlayerEntity
{
    private ulong _movementValidationTeleportUntil;
    internal void ModResetMovementValidation() { _movementValidationTeleportUntil = 0; ModResetQ3Traversal(); }
    internal void ModNoteMovementValidationTeleport()
    {
        if (NetMovementValidationShadow.Enabled) _movementValidationTeleportUntil = _scene.FrameCount + 120;
    }
    internal MovementValidationExemption ModMovementValidationExemptions
    {
        get
        {
            MovementValidationExemption result = 0;
            // Prime uses different native cap/jump constants and is not modeled.
            if (IsPrimeHunter) result |= MovementValidationExemption.Prime;
            if (_movementValidationTeleportUntil != 0 && _scene.FrameCount <= _movementValidationTeleportUntil)
                result |= MovementValidationExemption.Teleport;
            if (_standingEntCol != null) result |= MovementValidationExemption.Platform;
            if (_q3LaunchGravity != 0) result |= MovementValidationExemption.JumpPad;
            if (_jumpPadControlLock > 0 || _timeSinceJumpPad <= 120 || Flags1.TestFlag(PlayerFlags1.UsedJumpPad))
                result |= MovementValidationExemption.JumpPad;
            if (_accelerationTimer > 0 || _damageInvulnTimer > 0) result |= MovementValidationExemption.Impulse;
            if (IsMorphing || IsUnmorphing) result |= MovementValidationExemption.FormTransition;
            if (Flags2.TestFlag(PlayerFlags2.SpireClimbing)) result |= MovementValidationExemption.Climbing;
            if (Flags1.TestFlag(PlayerFlags1.CollidingLateral)) result |= MovementValidationExemption.Collision;
            return result;
        }
    }
}
