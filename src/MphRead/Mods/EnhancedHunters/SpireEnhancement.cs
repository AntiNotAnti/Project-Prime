using System;
using MphRead.Entities;

namespace MphRead.Mods.EnhancedHunters;
internal static class SpireEnhancement
{
    internal static void Ricochet(BeamProjectileEntity beam)
    {
        if (beam.Owner is not PlayerEntity owner || !EnhancedHunters.UsingAffinity(owner, beam.Beam)
            || owner.Hunter != Hunter.Spire) return;
        beam.EnhancedBounceCount = (byte)Math.Min(3, beam.EnhancedBounceCount + 1);
        owner.EnhancedState.ValueB = beam.EnhancedBounceCount;
    }
}
