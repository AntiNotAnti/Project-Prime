namespace MphRead.Mods.Physics;

// Persisted in the version-4 world-checkpoint cadence appendix. Never renumber.
internal enum NativeMovementMode : byte
{
    Legacy60 = 0,
    Diagnostic30 = 1,
    DiagnosticCadence60 = 2
}

/// <summary>Native-cadence scheduling. Policy and pending edges belong to the
/// scene/player, so independently restored worlds never share mutable state.</summary>
internal static class NativeKernelDiagnostic
{
    internal static bool BeginBiped(NativeMovementMode mode, ref bool pendingJump,
        ulong frame, bool pressed, out bool jump)
    {
        jump = pressed;
        if (mode != NativeMovementMode.DiagnosticCadence60) { pendingJump = false; return true; }
        pendingJump |= pressed;
        if ((frame & 1) == 0) { jump = false; return false; }
        jump = pendingJump;
        pendingJump = false;
        return true;
    }
}
