using System;

namespace MphRead.Mods.Movement;

// Presentation amount only; movement, terrain selection and sound lifetime stay native.
internal static class TraversalSlideFeedback
{
    internal const float MaximumAmount = 0xFFFF * .18f;

    internal static float Amount(float horizontalSpeed, float referenceSpeed, bool supported, bool sliding)
    {
        if (!supported || !sliding || !float.IsFinite(horizontalSpeed) || horizontalSpeed <= 0
            || !float.IsFinite(referenceSpeed) || referenceSpeed <= 0) return 0;
        return MaximumAmount * Math.Clamp(horizontalSpeed / referenceSpeed, 0, 1);
    }
}
