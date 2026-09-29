using System;
using System.IO;

namespace MphRead.Mods.Physics;

/// <summary>Bounded, explicit world-checkpoint appendix. Scene frame parity is
/// restored by the existing scene graph; this section owns policy and edges.</summary>
internal sealed record NativeCadenceCheckpoint(NativeMovementMode Mode, bool[] Pending, bool[]? FirePending = null)
{
    internal byte[] Encode()
    {
        if (!Enum.IsDefined(Mode) || Pending.Length is < 1 or > 64 || FirePending != null && FirePending.Length != Pending.Length)
            throw new InvalidDataException("Invalid native cadence checkpoint.");
        byte[] bytes = new byte[3 + Pending.Length];
        bytes[0] = 2; // appendix schema version
        bytes[1] = (byte)Mode;
        bytes[2] = (byte)Pending.Length;
        for (int i = 0; i < Pending.Length; i++)
        {
            if ((Pending[i] || FirePending?[i] == true) && Mode != NativeMovementMode.DiagnosticCadence60)
                throw new InvalidDataException("Pending edge outside native cadence.");
            bytes[i + 3] = (byte)((Pending[i] ? 1 : 0) | (FirePending?[i] == true ? 2 : 0));
        }
        return bytes;
    }

    internal static NativeCadenceCheckpoint Decode(ReadOnlySpan<byte> bytes, int players)
    {
        if (players is < 1 or > 64 || bytes.Length != players + 3 || bytes[0] is not (1 or 2)
            || bytes[2] != players || !Enum.IsDefined((NativeMovementMode)bytes[1]))
            throw new InvalidDataException("Invalid native cadence appendix version, mode or size.");
        var mode = (NativeMovementMode)bytes[1];
        bool[] pending = new bool[players], fire = new bool[players];
        for (int i = 0; i < players; i++)
        {
            byte value = bytes[i + 3];
            if (value > (bytes[0] == 1 ? 1 : 3) || value != 0 && mode != NativeMovementMode.DiagnosticCadence60)
                throw new InvalidDataException("Invalid native cadence pending edge.");
            pending[i] = (value & 1) != 0;
            fire[i] = (value & 2) != 0;
        }
        return new(mode, pending, fire);
    }
}
