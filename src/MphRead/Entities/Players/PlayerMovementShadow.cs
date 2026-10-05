using MphRead.Mods.Diagnostics;
using OpenTK.Mathematics;

namespace MphRead.Entities
{
    public partial class PlayerEntity
    {
        /// <summary>
        /// Capture live production movement after ProcessInput has completed its
        /// movement and collision work. This is observation only and is completely
        /// dormant unless -movementshadow enabled the diagnostic.
        /// </summary>
        private void ModMovementShadowObserve()
        {
            if (!MovementShadowRuntime.Enabled)
            {
                return;
            }
            // The first reference kernel is intentionally narrow. Samus is the
            // general baseline and Spire exercises the difficult climbing/contact
            // state. Other hunters are added after this pair is measurable.
            if (Hunter != Hunter.Samus && Hunter != Hunter.Spire)
            {
                return;
            }
            ulong frame = _scene.FrameCount;
            if ((frame & 1UL) != 0)
            {
                return;
            }

            int standingEntityId = _standingEntCol?.Entity.Id ?? -1;
            var snapshot = new MovementBoundarySnapshot(
                frame,
                SlotIndex,
                (int)Hunter,
                IsAltForm,
                Flags1.TestFlag(PlayerFlags1.Grounded),
                Flags1.TestFlag(PlayerFlags1.Standing),
                Flags2.TestFlag(PlayerFlags2.SpireClimbing),
                standingEntityId,
                Position,
                Speed,
                _facingVector,
                _gravity,
                _slipperiness,
                Vector3.Zero,
                0);
            MovementShadowRuntime.ObserveProduction(snapshot);
        }
    }
}
