using System;
using System.Buffers.Binary;
using MphRead.Mods.Network;

namespace MphRead.Mods.CustomWeapons;

internal readonly record struct CustomInputEnvelope(WorldBootstrapIdentity Bootstrap,WeaponDigest Manifest,
    CustomResourceKnowledge Knowledge,CustomControlRequest? Control,IntentPacket Intent);

// Native CompactIntent bytes stay verbatim. Input knowledge is independent of
// the oldest repeated control; an owned client also stamps input without a tap.
internal static class CustomInputCarrier
{
    internal const int EmptyPrefixSize=CustomWeaponProtocol.HeaderSize+1+CustomResourceKnowledge.Size+2;
    internal const int PrefixSize=EmptyPrefixSize+CustomControlRequest.Size;
    internal const int MaxPayload=1200-NetHeader.Size;
    internal static bool Matches(in CustomControlRequest request,in IntentPacket intent)
        =>request.IsValid&&request.Owner.MatchId==intent.MatchId&&request.Owner.AuthorityEpoch==intent.AuthorityEpoch
            &&request.Owner.Generation==intent.SlotGeneration&&request.Life==intent.LifeId;
    private static bool Matches(WorldBootstrapIdentity bootstrap,in IntentPacket intent)
        =>CustomWeaponProtocol.ValidBootstrap(bootstrap)&&bootstrap.Start.MatchId==intent.MatchId
            &&bootstrap.Start.AuthorityEpoch==intent.AuthorityEpoch&&bootstrap.SlotGeneration==intent.SlotGeneration
            &&intent.LifeId!=0;
    internal static bool TryWrite(in CustomControlRequest request,ReadOnlySpan<byte> native,Span<byte> bytes,out int written)
        =>TryWrite(request.Bootstrap,request.Manifest,default,request,native,bytes,out written);
    internal static bool TryWrite(WorldBootstrapIdentity bootstrap,WeaponDigest manifest,CustomResourceKnowledge knowledge,
        CustomControlRequest? request,ReadOnlySpan<byte> native,Span<byte> bytes,out int written)
    {
        written=0;int prefix=request.HasValue?PrefixSize:EmptyPrefixSize;
        if(!manifest.IsValid||!knowledge.IsValid||native.Length>MaxPayload-prefix
            ||!CompactIntentCodec.TryRead(native,out var intent)||!Matches(bootstrap,intent)
            ||request is {} control&&(!Matches(control,intent)||control.Bootstrap!=bootstrap||control.Manifest!=manifest)
            ||bytes.Length<prefix+native.Length)return false;
        native.CopyTo(bytes[prefix..]); // Synchronous overlapping copies remain valid.
        CustomWeaponProtocol.WriteHeader(bytes,bootstrap,manifest);bytes[65]=request.HasValue?(byte)1:(byte)0;
        knowledge.Write(bytes[66..]);BinaryPrimitives.WriteUInt16LittleEndian(bytes[90..],(ushort)native.Length);
        if(request is {} value)value.Write(bytes[EmptyPrefixSize..prefix]);
        written=prefix+native.Length;return true;
    }
    internal static bool TryRead(ReadOnlySpan<byte> bytes,out CustomInputEnvelope envelope)
    {
        envelope=default;
        if(bytes.Length<EmptyPrefixSize+CompactIntentCodec.HeaderSize||bytes.Length>MaxPayload
            ||!CustomWeaponProtocol.ReadHeader(bytes,out var bootstrap,out var manifest)||bytes[65]>1
            ||!CustomResourceKnowledge.TryRead(bytes.Slice(66,CustomResourceKnowledge.Size),out var knowledge))return false;
        int prefix=bytes[65]!=0?PrefixSize:EmptyPrefixSize;
        if(bytes.Length<prefix+CompactIntentCodec.HeaderSize||BinaryPrimitives.ReadUInt16LittleEndian(bytes[90..])!=bytes.Length-prefix
            ||!CompactIntentCodec.TryRead(bytes[prefix..],out var intent)||!Matches(bootstrap,intent))return false;
        CustomControlRequest? request=null;
        if(bytes[65]!=0)
        {
            if(!CustomControlRequest.TryRead(bytes[EmptyPrefixSize..prefix],out var control)||!Matches(control,intent)
                ||control.Bootstrap!=bootstrap||control.Manifest!=manifest)return false;
            request=control;
        }
        envelope=new(bootstrap,manifest,knowledge,request,intent);return true;
    }
    internal static bool TryRead(ReadOnlySpan<byte> bytes,out CustomControlRequest request,out IntentPacket intent)
    {
        request=default;intent=default;
        if(!TryRead(bytes,out var envelope)||envelope.Control is not {} control)return false;
        request=control;intent=envelope.Intent;return true;
    }
}
