using System;
using System.Buffers.Binary;
using MphRead.Mods.Network;

namespace MphRead.Mods.CustomWeapons;

// Primary custom control intent. No client source timestamp, ray, ACK, resource
// claim or native Shoot edge is a part of this contract.
internal readonly record struct CustomControlRequest(WorldBootstrapIdentity Bootstrap,WeaponDigest Manifest,
    byte Slot,ushort Life,WeaponRuntimeId Weapon,uint Sequence,CustomWeaponActionKind Kind,byte Optic)
{
    internal const int Size=76;
    internal WeaponOwnerContext Owner => new(Bootstrap.Start.MatchId,Bootstrap.Start.AuthorityEpoch,Slot,Bootstrap.SlotGeneration,Life);
    internal bool IsValid => CustomWeaponProtocol.ValidBootstrap(Bootstrap) && Manifest.IsValid
        && Slot<8 && Life!=0 && Weapon.Value is >=10 and <=73 && Sequence!=0
        && (byte)Kind is >=1 and <=5 && Optic<=2
        && (Kind is CustomWeaponActionKind.Fire or CustomWeaponActionKind.Optic || Optic==0);
    internal bool Matches(in CustomWeaponAction action) => IsValid && action.IsValid && Bootstrap==action.Bootstrap
        && Manifest==action.Manifest && Slot==action.Slot && Life==action.Life && Weapon==action.Weapon
        && Sequence==action.Sequence && Kind==action.Kind && Optic==action.Optic;
    internal void Write(Span<byte> bytes)
    {
        if (!IsValid || bytes.Length<Size) throw new ArgumentException("Valid control and complete buffer required.");
        CustomWeaponProtocol.WriteHeader(bytes,Bootstrap,Manifest); bytes[65]=Slot;
        BinaryPrimitives.WriteUInt16LittleEndian(bytes[66..],Life); BinaryPrimitives.WriteUInt16LittleEndian(bytes[68..],Weapon.Value);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes[70..],Sequence); bytes[74]=(byte)Kind; bytes[75]=Optic;
    }
    internal static bool TryRead(ReadOnlySpan<byte> bytes,out CustomControlRequest request)
    {
        request=default;
        if (bytes.Length!=Size || !CustomWeaponProtocol.ReadHeader(bytes,out var bootstrap,out var manifest)) return false;
        ushort runtime=BinaryPrimitives.ReadUInt16LittleEndian(bytes[68..]); if (runtime is <10 or >73) return false;
        var value=new CustomControlRequest(bootstrap,manifest,bytes[65],BinaryPrimitives.ReadUInt16LittleEndian(bytes[66..]),
            new(runtime),BinaryPrimitives.ReadUInt32LittleEndian(bytes[70..]),(CustomWeaponActionKind)bytes[74],bytes[75]);
        if (!value.IsValid) return false; request=value; return true;
    }
}

internal enum CustomControlDenialReason : byte { SourceUnavailable=1,LifeRetired,StaleSequence }

// An honest terminal outcome when there is no canonical Action/ray. It cannot
// carry a source certificate, resource delta or contact, and never refunds ammo.
internal readonly record struct CustomControlDenied(CustomControlRequest Request,ulong Token,uint AuthorityFrame,
    CustomControlDenialReason Reason)
{
    internal const int Size=89;
    internal bool IsValid => Request.IsValid && Token!=0 && (byte)Reason is >=1 and <=3
        && unchecked((int)(AuthorityFrame-Request.Bootstrap.AuthorityFrame))>=0;
    internal CustomResultAck Ack => new(Request.Bootstrap,Request.Manifest,Request.Slot,Request.Life,Request.Weapon,
        Request.Sequence,CustomResultKind.Receipt,Token);
    internal void Write(Span<byte> bytes)
    {
        if (!IsValid || bytes.Length<Size) throw new ArgumentException("Valid denial and complete buffer required.");
        Request.Write(bytes); BinaryPrimitives.WriteUInt64LittleEndian(bytes[76..],Token);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes[84..],AuthorityFrame); bytes[88]=(byte)Reason;
    }
    internal static bool TryRead(ReadOnlySpan<byte> bytes,out CustomControlDenied denied)
    {
        denied=default;
        if (bytes.Length!=Size || !CustomControlRequest.TryRead(bytes[..76],out var request)) return false;
        var value=new CustomControlDenied(request,BinaryPrimitives.ReadUInt64LittleEndian(bytes[76..]),
            BinaryPrimitives.ReadUInt32LittleEndian(bytes[84..]),(CustomControlDenialReason)bytes[88]);
        if (!value.IsValid) return false; denied=value; return true;
    }
}
