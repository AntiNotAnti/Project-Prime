using System;
using System.Buffers.Binary;
using System.Security.Cryptography;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.Mods.CustomWeapons;

internal enum CustomReplicaKind : byte { Launch=1,Contact=2,State=3 }
internal readonly record struct CustomReplicaLaunch(CustomSourceCertificate Source,uint World,Vector3 Origin,Vector3 Direction)
{
    internal CustomActionIdentity Identity => CustomActionIdentity.For(Source.Action);
}
internal readonly record struct CustomReplicaPage(ulong Id,ulong Frontier,uint World,byte Flights,byte Index)
{
    internal int PageCount => 1+(Flights+3)/4;
    internal int Count => Index==0 ? 8 : Math.Min(4,Flights-(Index-1)*4);
    internal int Length => CustomReplicaCodec.PagePrefix+Count*(Index==0 ? CustomReplicaCodec.SlotSize:CustomReplicaCodec.FlightSize);
    internal bool IsValid => Id!=0 && Flights<=64 && Index<PageCount;
}
internal readonly record struct CustomReplicaAck(bool Baseline,ulong Id,ulong Frontier,WeaponDigest Digest)
{
    internal const int Size=115;
}
internal readonly record struct CustomReplicaEvent(CustomReplicaKind Kind,ulong Token,uint World,
    CustomReplicaLaunch Launch,CustomContactMessage Contact);

// Recipient identity is always the OUTER header. Nested launch/contact headers
// retain the ORIGINAL source bootstrap; no source generation is inferred from a
// viewer, and none of these facts is an ingress health or resource command.
internal static class CustomReplicaCodec
{
    internal const int Header=66,PagePrefix=88,SlotSize=41,LaunchSize=171,FlightSize=219,
        EventPrefix=79,MaximumPayload=1024,MaximumPages=17;
    internal static WeaponDigest Hash(ReadOnlySpan<byte> bytes)
    { Span<byte> hash=stackalloc byte[32]; SHA256.HashData(bytes,hash); return WeaponDigest.Read(hash); }
    private static bool Before(uint a,uint b)=>unchecked((int)(a-b))<0;
    private static bool Finite(Vector3 v)=>float.IsFinite(v.X)&&float.IsFinite(v.Y)&&float.IsFinite(v.Z)
        && MathF.Abs(v.X)<=1_000_000&&MathF.Abs(v.Y)<=1_000_000&&MathF.Abs(v.Z)<=1_000_000;
    private static void WriteHeader(Span<byte> bytes,CustomReadinessWire wire)
    { CustomWeaponProtocol.WriteHeader(bytes,wire.Bootstrap,wire.Manifest.Digest);bytes[65]=wire.Slot; }
    internal static bool HeaderMatches(ReadOnlySpan<byte> bytes,CustomReadinessWire wire)
        => bytes.Length>=Header && CustomWeaponProtocol.ReadHeader(bytes,out var bootstrap,out var manifest)
            && bootstrap==wire.Bootstrap && manifest==wire.Manifest.Digest && bytes[65]==wire.Slot;
    internal static bool ValidSlot(in CustomReplicaSlot row,int index,CustomReadinessWire wire)
    {
        if (row.Slot!=index || !row.Occupied && (row.Generation!=0||row.Life!=0||row.Spectating||row.HasWeapon)
            || row.Occupied && row.Generation==0) return false;
        if (!row.HasWeapon) return row.Selected==default && row.SettledSequence==0 && row.CommittedSourceFrame==0 && row.State==default;
        if (!row.Occupied||row.Spectating||row.Life==0 || row.Selected.Value is not (0 or 10)) return false;
        var owner=new WeaponOwnerContext(wire.Bootstrap.Start.MatchId,wire.Bootstrap.Start.AuthorityEpoch,row.Slot,row.Generation,row.Life);
        return row.State.Phase!=CustomWeaponPhase.Invalid && CustomCombatResults.SnapshotValid(row.State,owner,wire.Definition)
            && row.State.LastAcceptedShotId<=row.SettledSequence && !Before(row.State.Tick,row.CommittedSourceFrame);
    }
    private static void WriteSlot(Span<byte> bytes,in CustomReplicaSlot row)
    {
        bytes[..SlotSize].Clear();bytes[0]=row.Slot;bytes[1]=(byte)((row.Occupied?1:0)|(row.Spectating?2:0)|(row.HasWeapon?4:0));
        BinaryPrimitives.WriteUInt16LittleEndian(bytes[2..],row.Generation);BinaryPrimitives.WriteUInt16LittleEndian(bytes[4..],row.Life);
        if (!row.HasWeapon) return;
        BinaryPrimitives.WriteUInt16LittleEndian(bytes[6..],row.Selected.Value);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes[8..],row.SettledSequence);BinaryPrimitives.WriteUInt32LittleEndian(bytes[12..],row.CommittedSourceFrame);
        CustomCombatResults.WriteSnapshot(bytes[16..],row.State);
    }
    private static bool ReadSlot(ReadOnlySpan<byte> bytes,int index,CustomReadinessWire wire,out CustomReplicaSlot row)
    {
        row=default;if(bytes[0]!=index||bytes[1]>7)return false;
        byte flags=bytes[1];ushort generation=BinaryPrimitives.ReadUInt16LittleEndian(bytes[2..]),life=BinaryPrimitives.ReadUInt16LittleEndian(bytes[4..]);
        ushort selected=BinaryPrimitives.ReadUInt16LittleEndian(bytes[6..]);if(selected is not (0 or 10))return false;
        WeaponStateSnapshot state=default;
        if((flags&4)!=0)
        { if(generation==0||life==0)return false;state=CustomCombatResults.ReadSnapshot(bytes[16..],new(wire.Bootstrap.Start.MatchId,wire.Bootstrap.Start.AuthorityEpoch,(byte)index,generation,life),wire.Definition); }
        else for(int i=6;i<SlotSize;i++)if(bytes[i]!=0)return false;
        var value=new CustomReplicaSlot((byte)index,(flags&1)!=0,generation,life,(flags&2)!=0,(flags&4)!=0,
            selected==0?default:new(selected),BinaryPrimitives.ReadUInt32LittleEndian(bytes[8..]),BinaryPrimitives.ReadUInt32LittleEndian(bytes[12..]),state);
        if(!ValidSlot(value,index,wire))return false;row=value;return true;
    }
    internal static bool ValidLaunch(in CustomReplicaLaunch launch,CustomReadinessWire wire,uint world)
    {
        var source=launch.Source;var action=source.Action;
        return action.IsValid&&action.Kind==CustomWeaponActionKind.Fire&&action.Weapon.Value==10
            && action.Manifest==wire.Manifest.Digest&&action.Bootstrap.Start==wire.Bootstrap.Start
            && source.CanUseWeapon&&CustomCombatResults.SourceValid(source,action,launch.World)
            && !Before(world,launch.World)&&Finite(launch.Origin)&&Finite(launch.Direction)
            && MathF.Abs(launch.Direction.LengthSquared-1)<=.02f;
    }
    private static void WriteLaunch(Span<byte> bytes,in CustomReplicaLaunch launch)
    {
        var source=launch.Source;source.Action.Write(bytes);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes[109..],source.ObservedWorldFrame);BinaryPrimitives.WriteUInt32LittleEndian(bytes[113..],source.TargetWorldFrame);
        bytes[117]=source.TargetWorldSubFrame;CustomWeaponProtocol.WriteVector(bytes[118..],source.Body);CustomWeaponProtocol.WriteVector(bytes[130..],source.Aim);
        bytes[142]=(byte)((source.CanUseWeapon?1:0)|(source.DoubleDamage?2:0)|(source.PrimeHunter?4:0)|(source.Crouched?8:0)|(source.Tucked?16:0));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes[143..],launch.World);CustomWeaponProtocol.WriteVector(bytes[147..],launch.Origin);CustomWeaponProtocol.WriteVector(bytes[159..],launch.Direction);
    }
    private static bool ReadLaunch(ReadOnlySpan<byte> bytes,CustomReadinessWire wire,uint world,out CustomReplicaLaunch launch)
    {
        launch=default;if(!CustomWeaponAction.TryRead(bytes[..109],out var action)||bytes[142]>31)return false;
        byte flags=bytes[142];var source=new CustomSourceCertificate(action,BinaryPrimitives.ReadUInt32LittleEndian(bytes[109..]),
            BinaryPrimitives.ReadUInt32LittleEndian(bytes[113..]),bytes[117],CustomWeaponProtocol.ReadVector(bytes[118..]),CustomWeaponProtocol.ReadVector(bytes[130..]),(flags&1)!=0,(flags&2)!=0,(flags&4)!=0,(flags&8)!=0,(flags&16)!=0);
        var value=new CustomReplicaLaunch(source,BinaryPrimitives.ReadUInt32LittleEndian(bytes[143..]),CustomWeaponProtocol.ReadVector(bytes[147..]),CustomWeaponProtocol.ReadVector(bytes[159..]));
        if(!ValidLaunch(value,wire,world))return false;launch=value;return true;
    }
    private static CustomReplicaLaunch LaunchFor(in CustomReplicaFlight flight)
        => new(flight.Certificate,flight.LaunchWorld,flight.SpawnPosition,flight.Direction);
    internal static bool ValidFlight(in CustomReplicaFlight flight,CustomReadinessWire wire,uint world)
        => flight.Certificate.Action.IsValid&&flight.Action==CustomActionIdentity.For(flight.Certificate.Action)&&ValidLaunch(LaunchFor(flight),wire,world)
            && flight.PaymentDeadline==unchecked(flight.LaunchWorld+(uint)wire.Definition.Mechanics.LifetimeTicks+2)
            && !Before(flight.PaymentDeadline,world)&&Finite(flight.SpawnPosition)&&Finite(flight.Position)&&Finite(flight.Velocity)
            && float.IsFinite(flight.AgeSeconds)&&flight.AgeSeconds>=0&&float.IsFinite(flight.RemainingSeconds)
            && flight.RemainingSeconds>0&&flight.AgeSeconds<=wire.Definition.Mechanics.LifetimeTicks/60f+.00001f
            && flight.RemainingSeconds<=wire.Definition.Mechanics.LifetimeTicks/60f+.00001f
            && MathF.Abs(flight.AgeSeconds+flight.RemainingSeconds-wire.Definition.Mechanics.LifetimeTicks/60f)<=.00002f;
    private static void WriteFlight(Span<byte> bytes,in CustomReplicaFlight flight)
    {
        WriteLaunch(bytes,LaunchFor(flight));BinaryPrimitives.WriteUInt32LittleEndian(bytes[171..],flight.PaymentDeadline);
        CustomWeaponProtocol.WriteVector(bytes[175..],flight.SpawnPosition);CustomWeaponProtocol.WriteVector(bytes[187..],flight.Position);
        CustomWeaponProtocol.WriteVector(bytes[199..],flight.Velocity);BinaryPrimitives.WriteSingleLittleEndian(bytes[211..],flight.AgeSeconds);
        BinaryPrimitives.WriteSingleLittleEndian(bytes[215..],flight.RemainingSeconds);
    }
    private static bool ReadFlight(ReadOnlySpan<byte> bytes,CustomReadinessWire wire,uint world,out CustomReplicaFlight flight)
    {
        flight=default;if(!ReadLaunch(bytes,wire,world,out var launch))return false;
        var value=new CustomReplicaFlight(launch.Identity,launch.Source,launch.World,BinaryPrimitives.ReadUInt32LittleEndian(bytes[171..]),
            CustomWeaponProtocol.ReadVector(bytes[175..]),CustomWeaponProtocol.ReadVector(bytes[187..]),CustomWeaponProtocol.ReadVector(bytes[199..]),
            launch.Direction,BinaryPrimitives.ReadSingleLittleEndian(bytes[211..]),BinaryPrimitives.ReadSingleLittleEndian(bytes[215..]));
        if(launch.Origin!=value.SpawnPosition||!ValidFlight(value,wire,world))return false;flight=value;return true;
    }
    internal static bool TryWritePage(CustomReadinessWire wire,in CustomReplicaPage page,ReadOnlySpan<CustomReplicaSlot> slots,
        ReadOnlySpan<CustomReplicaFlight> flights,Span<byte> bytes,out int length)
    {
        length=0;if(!page.IsValid||Before(page.World,wire.Bootstrap.AuthorityFrame)
            ||bytes.Length<page.Length||slots.Length<8||flights.Length<page.Flights)return false;
        if(page.Index==0){for(int i=0;i<8;i++)if(!ValidSlot(slots[i],i,wire))return false;}
        else for(int i=0;i<page.Count;i++)if(!ValidFlight(flights[(page.Index-1)*4+i],wire,page.World))return false;
        WriteHeader(bytes,wire);BinaryPrimitives.WriteUInt64LittleEndian(bytes[66..],page.Id);BinaryPrimitives.WriteUInt64LittleEndian(bytes[74..],page.Frontier);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes[82..],page.World);bytes[86]=page.Flights;bytes[87]=page.Index;
        for(int i=0;i<page.Count;i++)if(page.Index==0)WriteSlot(bytes[(PagePrefix+i*SlotSize)..],slots[i]);
            else WriteFlight(bytes[(PagePrefix+i*FlightSize)..],flights[(page.Index-1)*4+i]);
        length=page.Length;return true;
    }
    internal static bool TryReadPage(CustomReadinessWire wire,ReadOnlySpan<byte> bytes,out CustomReplicaPage page)
    {
        page=default;if(bytes.Length<PagePrefix||!HeaderMatches(bytes,wire))return false;
        var value=new CustomReplicaPage(BinaryPrimitives.ReadUInt64LittleEndian(bytes[66..]),BinaryPrimitives.ReadUInt64LittleEndian(bytes[74..]),
            BinaryPrimitives.ReadUInt32LittleEndian(bytes[82..]),bytes[86],bytes[87]);
        if(!value.IsValid||Before(value.World,wire.Bootstrap.AuthorityFrame)||bytes.Length!=value.Length)return false;
        for(int i=0;i<value.Count;i++)if(value.Index==0){if(!ReadSlot(bytes[(PagePrefix+i*SlotSize)..],i,wire,out _))return false;}
            else if(!ReadFlight(bytes[(PagePrefix+i*FlightSize)..],wire,value.World,out _))return false;
        page=value;return true;
    }
    internal static void CopyPage(CustomReadinessWire wire,ReadOnlySpan<byte> bytes,in CustomReplicaPage page,
        Span<CustomReplicaSlot> slots,Span<CustomReplicaFlight> flights)
    {
        // Internal assembler calls only after complete shape/content validation.
        for(int i=0;i<page.Count;i++)if(page.Index==0)ReadSlot(bytes[(PagePrefix+i*SlotSize)..],i,wire,out slots[i]);
            else ReadFlight(bytes[(PagePrefix+i*FlightSize)..],wire,page.World,out flights[(page.Index-1)*4+i]);
    }
    internal static bool TryWriteAck(CustomReadinessWire wire,in CustomReplicaAck ack,Span<byte> bytes)
    {
        if(bytes.Length<CustomReplicaAck.Size||ack.Id==0||!ack.Digest.IsValid||!ack.Baseline&&ack.Frontier!=0)return false;
        WriteHeader(bytes,wire);bytes[66]=ack.Baseline?(byte)1:(byte)0;BinaryPrimitives.WriteUInt64LittleEndian(bytes[67..],ack.Id);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes[75..],ack.Frontier);ack.Digest.Write(bytes[83..]);return true;
    }
    internal static bool TryReadAck(CustomReadinessWire wire,ReadOnlySpan<byte> bytes,out CustomReplicaAck ack)
    {
        ack=default;if(bytes.Length!=CustomReplicaAck.Size||!HeaderMatches(bytes,wire)||bytes[66]>1)return false;
        var value=new CustomReplicaAck(bytes[66]==1,BinaryPrimitives.ReadUInt64LittleEndian(bytes[67..]),BinaryPrimitives.ReadUInt64LittleEndian(bytes[75..]),WeaponDigest.Read(bytes[83..]));
        if(value.Id==0||!value.Digest.IsValid||!value.Baseline&&value.Frontier!=0)return false;ack=value;return true;
    }
    internal static bool TryWriteEvent(CustomReadinessWire wire,in CustomReplicaEvent value,Span<byte> bytes,out int length)
    {
        length=0;int size=value.Kind==CustomReplicaKind.Launch?LaunchSize:value.Kind==CustomReplicaKind.Contact?CustomContactMessage.Size:0;
        if(size==0||bytes.Length<EventPrefix+size||value.Token==0||Before(value.World,wire.Bootstrap.AuthorityFrame))return false;
        if(value.Kind==CustomReplicaKind.Launch){if(value.World!=value.Launch.World||!ValidLaunch(value.Launch,wire,value.World))return false;}
        else if(value.Contact.Token!=value.Token||value.Contact.Outcome.AuthorityFrame!=value.World
            ||value.Contact.Bootstrap.Start!=wire.Bootstrap.Start||!CustomCombatResults.Valid(value.Contact,wire.Manifest,wire.Definition))return false;
        WriteHeader(bytes,wire);bytes[66]=(byte)value.Kind;BinaryPrimitives.WriteUInt64LittleEndian(bytes[67..],value.Token);BinaryPrimitives.WriteUInt32LittleEndian(bytes[75..],value.World);
        if(value.Kind==CustomReplicaKind.Launch)WriteLaunch(bytes[EventPrefix..],value.Launch);
        else CustomCombatResults.Write(value.Contact,bytes[EventPrefix..],wire.Manifest,wire.Definition);
        length=EventPrefix+size;return true;
    }
    internal static bool TryReadEvent(CustomReadinessWire wire,ReadOnlySpan<byte> bytes,out CustomReplicaEvent value)
    {
        value=default;if(bytes.Length<EventPrefix||!HeaderMatches(bytes,wire))return false;
        var kind=(CustomReplicaKind)bytes[66];ulong token=BinaryPrimitives.ReadUInt64LittleEndian(bytes[67..]);uint world=BinaryPrimitives.ReadUInt32LittleEndian(bytes[75..]);
        if(token==0||Before(world,wire.Bootstrap.AuthorityFrame))return false;
        CustomReplicaLaunch launch=default;CustomContactMessage contact=default;
        if(kind==CustomReplicaKind.Launch){if(bytes.Length!=EventPrefix+LaunchSize||!ReadLaunch(bytes[EventPrefix..],wire,world,out launch)||launch.World!=world)return false;}
        else if(kind==CustomReplicaKind.Contact){if(bytes.Length!=EventPrefix+CustomContactMessage.Size
            ||!CustomCombatResults.TryReadContact(bytes[EventPrefix..],wire.Manifest,wire.Definition,out contact)||contact.Token!=token
            ||contact.Outcome.AuthorityFrame!=world||contact.Bootstrap.Start!=wire.Bootstrap.Start)return false;}
        else return false;
        value=new(kind,token,world,launch,contact);return true;
    }
    internal static bool TryWriteState(CustomReadinessWire wire,uint world,ReadOnlySpan<CustomReplicaSlot> slots,Span<byte> bytes,out int length,ulong revision=1)
    {
        length=0;if(revision==0||Before(world,wire.Bootstrap.AuthorityFrame)||slots.Length<8||bytes.Length<EventPrefix+8*SlotSize)return false;
        for(int i=0;i<8;i++)if(!ValidSlot(slots[i],i,wire))return false;
        if(!slots[wire.Slot].Occupied||slots[wire.Slot].Generation!=wire.Bootstrap.SlotGeneration)return false;
        WriteHeader(bytes,wire);bytes[66]=(byte)CustomReplicaKind.State;BinaryPrimitives.WriteUInt64LittleEndian(bytes[67..],revision);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes[75..],world);
        for(int i=0;i<8;i++)WriteSlot(bytes[(EventPrefix+i*SlotSize)..],slots[i]);
        length=EventPrefix+8*SlotSize;return true;
    }
    internal static bool TryReadState(CustomReadinessWire wire,ReadOnlySpan<byte> bytes,out uint world)
        => TryReadState(wire,bytes,out world,out _);
    internal static bool TryReadState(CustomReadinessWire wire,ReadOnlySpan<byte> bytes,out uint world,out ulong revision)
    {
        world=0;revision=0;if(bytes.Length!=EventPrefix+8*SlotSize||!HeaderMatches(bytes,wire)||bytes[66]!=(byte)CustomReplicaKind.State)return false;
        ulong sequence=BinaryPrimitives.ReadUInt64LittleEndian(bytes[67..]);if(sequence==0)return false;
        uint value=BinaryPrimitives.ReadUInt32LittleEndian(bytes[75..]);if(Before(value,wire.Bootstrap.AuthorityFrame))return false;
        for(int i=0;i<8;i++)if(!ReadSlot(bytes[(EventPrefix+i*SlotSize)..],i,wire,out _))return false;
        ReadSlot(bytes[(EventPrefix+wire.Slot*SlotSize)..],wire.Slot,wire,out var recipient);
        if(!recipient.Occupied||recipient.Generation!=wire.Bootstrap.SlotGeneration)return false;
        world=value;revision=sequence;return true;
    }
    internal static void CopyState(CustomReadinessWire wire,ReadOnlySpan<byte> bytes,Span<CustomReplicaSlot> slots)
    { for(int i=0;i<8;i++)ReadSlot(bytes[(EventPrefix+i*SlotSize)..],i,wire,out slots[i]); }
}
