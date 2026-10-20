using System;
using System.Buffers.Binary;
using MphRead.Mods.Network;

namespace MphRead.Mods.CustomWeapons;

// Optional live envelope; the enclosed native CompactIntentCodec bytes are not
// changed, trimmed, reinterpreted as a custom shot, or written into replay data.
internal static class CustomInputCarrier
{
    internal const int PrefixSize=2+CustomControlRequest.Size;
    internal const int MaxPayload=1200-NetHeader.Size;
    internal static bool Matches(in CustomControlRequest request,in IntentPacket intent)
        => request.IsValid && request.Owner.MatchId==intent.MatchId && request.Owner.AuthorityEpoch==intent.AuthorityEpoch
            && request.Owner.Generation==intent.SlotGeneration && request.Life==intent.LifeId;
    internal static bool TryWrite(in CustomControlRequest request,ReadOnlySpan<byte> native,Span<byte> bytes,out int written)
    {
        written=0;
        if (native.Length>MaxPayload-PrefixSize || !CompactIntentCodec.TryRead(native,out var intent)
            || !Matches(request,intent) || bytes.Length<PrefixSize+native.Length) return false;
        // Copy first so an overlapping source/destination remains correct.
        native.CopyTo(bytes[PrefixSize..]);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes,(ushort)native.Length); request.Write(bytes[2..PrefixSize]);
        written=PrefixSize+native.Length; return true;
    }
    internal static bool TryRead(ReadOnlySpan<byte> bytes,out CustomControlRequest request,out IntentPacket intent)
    {
        request=default; intent=default;
        if (bytes.Length<PrefixSize+CompactIntentCodec.HeaderSize || bytes.Length>MaxPayload
            || BinaryPrimitives.ReadUInt16LittleEndian(bytes)!=bytes.Length-PrefixSize
            || !CustomControlRequest.TryRead(bytes[2..PrefixSize],out var control)
            || !CompactIntentCodec.TryRead(bytes[PrefixSize..],out var native) || !Matches(control,native)) return false;
        request=control; intent=native; return true;
    }
}
