using System;
using System.Buffers.Binary;
using OpenTK.Mathematics;
namespace MphRead.Mods.Network;
public enum FormPhase : byte { Unknown, Biped, Morphing, Alt, Unmorphing }
public struct PlayerFormState
{
    public const int Size = 28;
    public uint Frame, StartedFrame;
    public FormPhase Phase;
    public Vector3 TurretPosition;
    public bool TurretActive;
    public ushort TurretHealth, DamageEventId, BodyHealth;
    public bool IsValid => Phase <= FormPhase.Unmorphing
        && (StartedFrame == Frame || !NetLifecycleTracker.Newer(StartedFrame, Frame))
        && float.IsFinite(TurretPosition.X) && float.IsFinite(TurretPosition.Y) && float.IsFinite(TurretPosition.Z)
        && Math.Abs(TurretPosition.X) <= 100000 && Math.Abs(TurretPosition.Y) <= 100000 && Math.Abs(TurretPosition.Z) <= 100000;
    public void Write(Span<byte> dest)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(dest, Frame); BinaryPrimitives.WriteUInt32LittleEndian(dest[4..], StartedFrame);
        dest[8] = (byte)Phase; BinaryPrimitives.WriteSingleLittleEndian(dest[9..], TurretPosition.X);
        BinaryPrimitives.WriteSingleLittleEndian(dest[13..], TurretPosition.Y); BinaryPrimitives.WriteSingleLittleEndian(dest[17..], TurretPosition.Z);
        dest[21] = TurretActive ? (byte)1 : (byte)0; BinaryPrimitives.WriteUInt16LittleEndian(dest[22..], TurretHealth);
        BinaryPrimitives.WriteUInt16LittleEndian(dest[24..], DamageEventId);
        BinaryPrimitives.WriteUInt16LittleEndian(dest[26..], BodyHealth);
    }
    public static bool TryRead(ReadOnlySpan<byte> src, out PlayerFormState value)
    {
        value = default; if (src.Length != Size || src[21] > 1) return false;
        var state = new PlayerFormState { Frame = BinaryPrimitives.ReadUInt32LittleEndian(src), StartedFrame = BinaryPrimitives.ReadUInt32LittleEndian(src[4..]),
            Phase = (FormPhase)src[8], TurretPosition = new(BinaryPrimitives.ReadSingleLittleEndian(src[9..]), BinaryPrimitives.ReadSingleLittleEndian(src[13..]), BinaryPrimitives.ReadSingleLittleEndian(src[17..])),
            TurretActive = src[21] != 0, TurretHealth = BinaryPrimitives.ReadUInt16LittleEndian(src[22..]), DamageEventId = BinaryPrimitives.ReadUInt16LittleEndian(src[24..]), BodyHealth = BinaryPrimitives.ReadUInt16LittleEndian(src[26..]) };
        if (!state.IsValid) return false; value = state; return true;
    }
}
