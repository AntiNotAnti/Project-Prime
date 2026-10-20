using System;
using System.Buffers.Binary;
using MphRead.Mods.Network;

namespace MphRead.Mods.CustomWeapons;

// Authority facts, never client health/resource claims. Packet transport/version,
// endpoint, bootstrap and current manifest admission remain separate gates.
internal enum CustomResultKind : byte { Receipt=1, Contact=2 }
internal readonly record struct CustomResultAck(WorldBootstrapIdentity Bootstrap,WeaponDigest Manifest,
    byte Slot,ushort Life,WeaponRuntimeId Weapon,uint Sequence,CustomResultKind Kind,ulong Token)
{
    internal const int Size = 83;
    internal bool IsValid => CustomWeaponProtocol.ValidBootstrap(Bootstrap) && Manifest.IsValid && Slot < 8
        && Life != 0 && Weapon.Value is >= 10 and <= 73 && Sequence != 0 && Token != 0
        && Kind is CustomResultKind.Receipt or CustomResultKind.Contact;
    internal WeaponOwnerContext Owner => new(Bootstrap.Start.MatchId,Bootstrap.Start.AuthorityEpoch,Slot,Bootstrap.SlotGeneration,Life);
    internal void Write(Span<byte> bytes)
    {
        if (bytes.Length < Size || !IsValid) throw new ArgumentException("Valid full custom result acknowledgement required.");
        CustomWeaponProtocol.WriteHeader(bytes,Bootstrap,Manifest); bytes[65]=Slot;
        BinaryPrimitives.WriteUInt16LittleEndian(bytes[66..],Life); BinaryPrimitives.WriteUInt16LittleEndian(bytes[68..],Weapon.Value);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes[70..],Sequence); bytes[74]=(byte)Kind;
        BinaryPrimitives.WriteUInt64LittleEndian(bytes[75..],Token);
    }
    internal static bool TryRead(ReadOnlySpan<byte> bytes,out CustomResultAck ack)
    {
        ack=default;
        if (bytes.Length != Size || !CustomWeaponProtocol.ReadHeader(bytes,out var bootstrap,out var manifest)) return false;
        ushort runtime=BinaryPrimitives.ReadUInt16LittleEndian(bytes[68..]); if (runtime is < 10 or > 73) return false;
        var value=new CustomResultAck(bootstrap,manifest,bytes[65],BinaryPrimitives.ReadUInt16LittleEndian(bytes[66..]),new(runtime),
            BinaryPrimitives.ReadUInt32LittleEndian(bytes[70..]),(CustomResultKind)bytes[74],BinaryPrimitives.ReadUInt64LittleEndian(bytes[75..]));
        if (!value.IsValid) return false; ack=value; return true;
    }
}
internal readonly record struct CustomReceiptMessage(ulong Token,CustomActionReceipt Receipt)
{
    internal const int Size=209;
    internal CustomResultAck Ack => new(Receipt.Action.Bootstrap,Receipt.Action.Manifest,Receipt.Action.Slot,
        Receipt.Action.Life,Receipt.Action.Weapon,Receipt.Action.Sequence,CustomResultKind.Receipt,Token);
}
internal readonly record struct CustomContactMessage(WorldBootstrapIdentity Bootstrap,ulong Token,CustomContactOutcome Outcome)
{
    internal const int Size=116;
    internal CustomResultAck Ack => new(Bootstrap,Outcome.Contact.Key.Action.Manifest,Outcome.Contact.Key.Action.Owner.Slot,
        Outcome.Contact.Key.Action.Owner.Life,Outcome.Contact.Key.Action.Weapon,Outcome.Contact.Key.Action.Sequence,CustomResultKind.Contact,Token);
}
internal readonly record struct CustomStateMessage(WorldBootstrapIdentity Bootstrap,WeaponDigest Manifest,WeaponRuntimeId Weapon,
    uint AuthorityFrame,uint Revision,uint SettledSequence,WeaponRuntimeId Selected,uint CommittedSource,WeaponStateSnapshot State,
    bool HasWeapon=true)
{
    internal const int Size=114;
    internal static CustomStateMessage Empty(WorldBootstrapIdentity bootstrap,WeaponDigest manifest,WeaponRuntimeId weapon,
        uint world,uint revision,uint settled,uint committed,WeaponOwnerContext owner,WeaponDefinition definition)
        =>new(bootstrap,manifest,weapon,world,revision,settled,default,committed,
            EmptySnapshot(owner,definition,committed),false);
    internal static WeaponStateSnapshot EmptySnapshot(WeaponOwnerContext owner,WeaponDefinition definition,uint committed)
        =>new(owner,definition.Identity,committed,CustomWeaponPhase.Invalid,0,0,0,0,0,0,0);
}

internal static class CustomCombatResults
{
    internal const int SnapshotSize=25;
    private static bool Before(uint a,uint b) => unchecked((int)(a-b)) < 0;
    private static bool Resolves(WeaponDigest digest,WeaponRuntimeId runtime,CustomWeaponManifest manifest,WeaponDefinition definition)
    {
        int index=runtime.Value-10;
        return digest==manifest.Digest && (uint)index < manifest.Rows.Count
            && manifest.Rows[index].RuntimeId==runtime && MatchesDefinition(manifest.Rows[index],definition);
    }
    private static bool MatchesDefinition(in CustomManifestRow row,WeaponDefinition definition)
    {
        if (row.ContentId!=definition.Identity.ContentId || definition.Identity.DefinitionHash is not { Length:64 } hash) return false;
        Span<byte> digest=stackalloc byte[32]; row.Mechanics.Write(digest);
        const string digits="0123456789abcdef";
        for (int i=0;i<32;i++) if (hash[i*2]!=digits[digest[i]>>4] || hash[i*2+1]!=digits[digest[i]&15]) return false;
        return true;
    }
    internal static bool SnapshotValid(in WeaponStateSnapshot state,WeaponOwnerContext owner,WeaponDefinition definition)
        => state.Owner==owner && state.Weapon==definition.Identity && (byte)state.Phase <= (byte)CustomWeaponPhase.Reloading
            && state.Magazine >= 0 && state.Magazine <= definition.Mechanics.MagazineCapacity
            && state.Reserve >= 0 && state.Reserve <= definition.Mechanics.MaximumReserve && state.Reserve <= ushort.MaxValue
            && state.Optic >= 0 && state.Optic <= definition.Mechanics.OpticMagnificationsMilli.Count
            && (state.LastAcceptedShotId != 0 || state.LastAcceptedShotTick==0 && state.FireReadyTick==0);
    internal static void WriteSnapshot(Span<byte> bytes,in WeaponStateSnapshot state)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(bytes,state.Tick); bytes[4]=(byte)state.Phase; bytes[5]=(byte)state.Magazine;
        BinaryPrimitives.WriteUInt16LittleEndian(bytes[6..],(ushort)state.Reserve); bytes[8]=(byte)state.Optic;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes[9..],state.FireReadyTick); BinaryPrimitives.WriteUInt32LittleEndian(bytes[13..],state.ReloadCompleteTick);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes[17..],state.LastAcceptedShotId); BinaryPrimitives.WriteUInt32LittleEndian(bytes[21..],state.LastAcceptedShotTick);
    }
    internal static WeaponStateSnapshot ReadSnapshot(ReadOnlySpan<byte> bytes,WeaponOwnerContext owner,WeaponDefinition definition)
        => new(owner,definition.Identity,BinaryPrimitives.ReadUInt32LittleEndian(bytes),(CustomWeaponPhase)bytes[4],bytes[5],
            BinaryPrimitives.ReadUInt16LittleEndian(bytes[6..]),bytes[8],BinaryPrimitives.ReadUInt32LittleEndian(bytes[9..]),
            BinaryPrimitives.ReadUInt32LittleEndian(bytes[13..]),BinaryPrimitives.ReadUInt32LittleEndian(bytes[17..]),BinaryPrimitives.ReadUInt32LittleEndian(bytes[21..]));
    internal static bool SourceValid(in CustomSourceCertificate source,in CustomWeaponAction action,uint authority)
        => source.IsValidFor(action) && !Before(authority,source.ObservedWorldFrame) && !Before(authority,source.TargetWorldFrame)
            && !Before(source.ObservedWorldFrame,source.TargetWorldFrame)
            && (source.TargetWorldFrame!=source.ObservedWorldFrame || source.TargetWorldSubFrame==0)
            && !Before(source.ObservedWorldFrame,action.Bootstrap.AuthorityFrame)
            && !Before(source.TargetWorldFrame,action.Bootstrap.AuthorityFrame)
            && float.IsFinite(source.Aim.LengthSquared) && MathF.Abs(source.Aim.LengthSquared-1) <= .02f
            && MathF.Abs(source.Body.X) <= 1_000_000 && MathF.Abs(source.Body.Y) <= 1_000_000 && MathF.Abs(source.Body.Z) <= 1_000_000
            && !(source.DoubleDamage && source.PrimeHunter)
            && (action.Kind==CustomWeaponActionKind.Fire
                ? source.TargetWorldFrame==action.AckFrame && source.TargetWorldSubFrame==action.AckSubFrame
                : source.TargetWorldFrame==source.ObservedWorldFrame && source.TargetWorldSubFrame==0);
    internal static bool Valid(in CustomReceiptMessage message,CustomWeaponManifest manifest,WeaponDefinition definition)
    {
        var receipt=message.Receipt; var action=receipt.Action;
        if (message.Token==0 || !action.IsValid || !Resolves(action.Manifest,action.Weapon,manifest,definition)
            || Before(receipt.AuthorityFrame,action.Bootstrap.AuthorityFrame)
            || (byte)receipt.Disposition > (byte)CustomJournalDisposition.EngineFault
            || (byte)receipt.Result > (byte)WeaponActionResult.InvalidOptic || (byte)receipt.Emission > (byte)CustomEmissionDisposition.Uncertain
            || (receipt.Disposition==CustomJournalDisposition.Accepted)!=(receipt.Result==WeaponActionResult.Accepted)) return false;
        bool all=receipt.Source.HasValue && receipt.Before.HasValue && receipt.After.HasValue;
        bool none=!receipt.Source.HasValue && !receipt.Before.HasValue && !receipt.After.HasValue;
        if (!all && !none || none && (receipt.Emission!=CustomEmissionDisposition.NotAttempted
            || receipt.Disposition is CustomJournalDisposition.Accepted or CustomJournalDisposition.ResourceDenied
                or CustomJournalDisposition.OpticMismatch or CustomJournalDisposition.NoSpawn)) return false;
        if (receipt.Emission==CustomEmissionDisposition.Spawned && (action.Kind!=CustomWeaponActionKind.Fire || receipt.Result!=WeaponActionResult.Accepted)
            || receipt.Result==WeaponActionResult.Accepted && action.Kind==CustomWeaponActionKind.Fire && receipt.Emission!=CustomEmissionDisposition.Spawned
            || receipt.Emission==CustomEmissionDisposition.Uncertain && (action.Kind!=CustomWeaponActionKind.Fire || receipt.Disposition!=CustomJournalDisposition.EngineFault)
            || (receipt.Disposition==CustomJournalDisposition.NoSpawn)!=(receipt.Emission==CustomEmissionDisposition.NoSpawn)
            || receipt.Emission==CustomEmissionDisposition.NoSpawn && (action.Kind!=CustomWeaponActionKind.Fire || receipt.Result!=WeaponActionResult.CapacityUnavailable)) return false;
        if (none) return true;
        var prior=receipt.Before!.Value; var after=receipt.After!.Value;
        bool spent=receipt.Emission is CustomEmissionDisposition.Spawned or CustomEmissionDisposition.Uncertain;
        return SourceValid(receipt.Source!.Value,action,receipt.AuthorityFrame)
            && SnapshotValid(receipt.Before!.Value,action.Owner,definition) && SnapshotValid(receipt.After!.Value,action.Owner,definition)
            && prior.LastAcceptedShotId <= action.Sequence && after.LastAcceptedShotId <= action.Sequence
            && prior.Tick==action.SourceFrame && after.Tick==action.SourceFrame && after.Reserve==prior.Reserve
            && (spent ? after.Magazine==prior.Magazine-1 && after.LastAcceptedShotId==action.Sequence
                    && after.LastAcceptedShotTick==action.SourceFrame
                    && (receipt.Emission==CustomEmissionDisposition.Uncertain
                        ? after.Phase==CustomWeaponPhase.Invalid && after.Optic==0 : after.Optic==action.Optic)
                : after.Magazine==prior.Magazine && after.LastAcceptedShotId==prior.LastAcceptedShotId
                    && after.LastAcceptedShotTick==prior.LastAcceptedShotTick);
    }
    internal static void Write(in CustomReceiptMessage message,Span<byte> bytes,CustomWeaponManifest manifest,WeaponDefinition definition)
    {
        if (bytes.Length < CustomReceiptMessage.Size || !Valid(message,manifest,definition)) throw new ArgumentException("Consistent custom action result required.");
        var receipt=message.Receipt; receipt.Action.Write(bytes); BinaryPrimitives.WriteUInt64LittleEndian(bytes[109..],message.Token);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes[117..],receipt.AuthorityFrame); bytes[121]=(byte)receipt.Disposition;
        bytes[122]=(byte)receipt.Result; bytes[123]=(byte)receipt.Emission; bytes[124]=receipt.Source.HasValue ? (byte)7 : (byte)0;
        bytes.Slice(125,84).Clear();
        if (receipt.Source is { } source)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(bytes[125..],source.ObservedWorldFrame); BinaryPrimitives.WriteUInt32LittleEndian(bytes[129..],source.TargetWorldFrame);
            bytes[133]=source.TargetWorldSubFrame; CustomWeaponProtocol.WriteVector(bytes[134..],source.Body); CustomWeaponProtocol.WriteVector(bytes[146..],source.Aim);
            bytes[158]=(byte)((source.CanUseWeapon ? 1:0)|(source.DoubleDamage ? 2:0)|(source.PrimeHunter ? 4:0)|(source.Crouched ? 8:0));
            WriteSnapshot(bytes[159..],receipt.Before!.Value); WriteSnapshot(bytes[184..],receipt.After!.Value);
        }
    }
    internal static bool TryReadReceipt(ReadOnlySpan<byte> bytes,CustomWeaponManifest manifest,WeaponDefinition definition,out CustomReceiptMessage message)
    {
        message=default;
        if (bytes.Length!=CustomReceiptMessage.Size || !CustomWeaponAction.TryRead(bytes[..109],out var action)
            || !Resolves(action.Manifest,action.Weapon,manifest,definition) || bytes[124] is not (0 or 7)) return false;
        CustomSourceCertificate? source=null; WeaponStateSnapshot? before=null,after=null;
        if (bytes[124]==0)
        { for (int i=125;i<209;i++) if (bytes[i]!=0) return false; }
        else
        {
            byte flags=bytes[158]; if (flags>15) return false;
            source=new(action,BinaryPrimitives.ReadUInt32LittleEndian(bytes[125..]),BinaryPrimitives.ReadUInt32LittleEndian(bytes[129..]),bytes[133],
                CustomWeaponProtocol.ReadVector(bytes[134..]),CustomWeaponProtocol.ReadVector(bytes[146..]),(flags&1)!=0,(flags&2)!=0,(flags&4)!=0,(flags&8)!=0);
            before=ReadSnapshot(bytes[159..],action.Owner,definition); after=ReadSnapshot(bytes[184..],action.Owner,definition);
        }
        var value=new CustomReceiptMessage(BinaryPrimitives.ReadUInt64LittleEndian(bytes[109..]),new(action,
            BinaryPrimitives.ReadUInt32LittleEndian(bytes[117..]),(CustomJournalDisposition)bytes[121],(WeaponActionResult)bytes[122],
            (CustomEmissionDisposition)bytes[123],source,before,after));
        if (!Valid(value,manifest,definition)) return false; message=value; return true;
    }
    internal static bool Valid(in CustomContactMessage message,CustomWeaponManifest manifest,WeaponDefinition definition)
    {
        var outcome=message.Outcome; var contact=outcome.Contact; var action=contact.Key.Action;
        return message.Token!=0 && CustomWeaponProtocol.ValidBootstrap(message.Bootstrap) && contact.IsValid && outcome.Health.IsValid
            && Resolves(action.Manifest,action.Weapon,manifest,definition) && action.Owner.MatchId==message.Bootstrap.Start.MatchId
            && action.Owner.AuthorityEpoch==message.Bootstrap.Start.AuthorityEpoch && action.Owner.Generation==message.Bootstrap.SlotGeneration
            && !Before(outcome.AuthorityFrame,message.Bootstrap.AuthorityFrame)
            && outcome.Disposition is CustomContactDisposition.Canonical or CustomContactDisposition.TradeDenied
            && (outcome.Disposition!=CustomContactDisposition.TradeDenied || outcome.Health.BodyRemoved==0 && outcome.Health.TurretRemoved==0);
    }
    internal static void Write(in CustomContactMessage message,Span<byte> bytes,CustomWeaponManifest manifest,WeaponDefinition definition)
    {
        if (bytes.Length < CustomContactMessage.Size || !Valid(message,manifest,definition)) throw new ArgumentException("Consistent custom contact result required.");
        var outcome=message.Outcome; var contact=outcome.Contact; var action=contact.Key.Action;
        CustomWeaponProtocol.WriteHeader(bytes,message.Bootstrap,action.Manifest); bytes[65]=action.Owner.Slot;
        BinaryPrimitives.WriteUInt16LittleEndian(bytes[66..],action.Owner.Life); BinaryPrimitives.WriteUInt16LittleEndian(bytes[68..],action.Weapon.Value);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes[70..],action.Sequence); BinaryPrimitives.WriteUInt32LittleEndian(bytes[74..],outcome.AuthorityFrame);
        bytes[78]=contact.Key.Victim.Slot; BinaryPrimitives.WriteUInt16LittleEndian(bytes[79..],contact.Key.Victim.Generation);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes[81..],contact.Key.Victim.Life); bytes[83]=1; bytes[84]=(byte)contact.Key.Channel;
        CustomWeaponProtocol.WriteVector(bytes[85..],contact.Point); bytes[97]=(byte)((contact.Headshot ? 1:0)|(outcome.Disposition==CustomContactDisposition.TradeDenied ? 2:0));
        BinaryPrimitives.WriteUInt16LittleEndian(bytes[98..],(ushort)outcome.Health.BodyBefore); BinaryPrimitives.WriteUInt16LittleEndian(bytes[100..],(ushort)outcome.Health.BodyAfter);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes[102..],(ushort)outcome.Health.TurretBefore); BinaryPrimitives.WriteUInt16LittleEndian(bytes[104..],(ushort)outcome.Health.TurretAfter);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes[106..],outcome.Health.DamageEventId); BinaryPrimitives.WriteUInt64LittleEndian(bytes[108..],message.Token);
    }
    internal static bool TryReadContact(ReadOnlySpan<byte> bytes,CustomWeaponManifest manifest,WeaponDefinition definition,out CustomContactMessage message)
    {
        message=default;
        if (bytes.Length!=CustomContactMessage.Size || !CustomWeaponProtocol.ReadHeader(bytes,out var bootstrap,out var digest)
            || bytes[65]>=8 || BinaryPrimitives.ReadUInt16LittleEndian(bytes[66..])==0 || bytes[83]!=1 || bytes[97]>3) return false;
        ushort id=BinaryPrimitives.ReadUInt16LittleEndian(bytes[68..]); if (id is < 10 or > 73 || !Resolves(digest,new(id),manifest,definition)) return false;
        var owner=new WeaponOwnerContext(bootstrap.Start.MatchId,bootstrap.Start.AuthorityEpoch,bytes[65],bootstrap.SlotGeneration,BinaryPrimitives.ReadUInt16LittleEndian(bytes[66..]));
        var identity=new CustomActionIdentity(owner,digest,new(id),BinaryPrimitives.ReadUInt32LittleEndian(bytes[70..]));
        var key=new CustomPaymentKey(identity,new(bytes[78],BinaryPrimitives.ReadUInt16LittleEndian(bytes[79..]),BinaryPrimitives.ReadUInt16LittleEndian(bytes[81..])),1,(CustomImpactChannel)bytes[84]);
        var outcome=new CustomContactOutcome(new(key,CustomWeaponProtocol.ReadVector(bytes[85..]),(bytes[97]&1)!=0),BinaryPrimitives.ReadUInt32LittleEndian(bytes[74..]),
            new(BinaryPrimitives.ReadUInt16LittleEndian(bytes[98..]),BinaryPrimitives.ReadUInt16LittleEndian(bytes[100..]),BinaryPrimitives.ReadUInt16LittleEndian(bytes[102..]),
                BinaryPrimitives.ReadUInt16LittleEndian(bytes[104..]),BinaryPrimitives.ReadUInt16LittleEndian(bytes[106..])),(bytes[97]&2)!=0 ? CustomContactDisposition.TradeDenied:CustomContactDisposition.Canonical);
        var value=new CustomContactMessage(bootstrap,BinaryPrimitives.ReadUInt64LittleEndian(bytes[108..]),outcome);
        if (!Valid(value,manifest,definition)) return false; message=value; return true;
    }
    internal static bool Valid(in CustomStateMessage message,CustomWeaponManifest manifest,WeaponDefinition definition)
        => CustomWeaponProtocol.ValidBootstrap(message.Bootstrap) && message.Revision!=0
            && Resolves(message.Manifest,message.Weapon,manifest,definition) && message.State.Owner.IsValid
            && message.State.Owner.MatchId==message.Bootstrap.Start.MatchId && message.State.Owner.AuthorityEpoch==message.Bootstrap.Start.AuthorityEpoch
            && message.State.Owner.Generation==message.Bootstrap.SlotGeneration && !Before(message.AuthorityFrame,message.Bootstrap.AuthorityFrame)
            && (!message.Selected.IsValid || message.Selected==message.Weapon) && SnapshotValid(message.State,message.State.Owner,definition)
            && (message.HasWeapon || !message.Selected.IsValid
                && message.State==CustomStateMessage.EmptySnapshot(message.State.Owner,definition,message.CommittedSource))
            && !Before(message.State.Tick,message.CommittedSource) && message.State.LastAcceptedShotId <= message.SettledSequence;
    internal static void Write(in CustomStateMessage message,Span<byte> bytes,CustomWeaponManifest manifest,WeaponDefinition definition)
    {
        if (bytes.Length<CustomStateMessage.Size || !Valid(message,manifest,definition)) throw new ArgumentException("Consistent custom presentation state required.");
        CustomWeaponProtocol.WriteHeader(bytes,message.Bootstrap,message.Manifest); bytes[65]=message.State.Owner.Slot;
        BinaryPrimitives.WriteUInt16LittleEndian(bytes[66..],message.State.Owner.Life); BinaryPrimitives.WriteUInt16LittleEndian(bytes[68..],message.Weapon.Value);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes[70..],message.AuthorityFrame); BinaryPrimitives.WriteUInt32LittleEndian(bytes[74..],message.Revision);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes[78..],message.SettledSequence); BinaryPrimitives.WriteUInt16LittleEndian(bytes[82..],message.Selected.Value);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes[84..],message.CommittedSource); WriteSnapshot(bytes[88..],message.State);
        bytes[113]=message.HasWeapon?(byte)1:(byte)0;
    }
    internal static bool TryReadState(ReadOnlySpan<byte> bytes,CustomWeaponManifest manifest,WeaponDefinition definition,out CustomStateMessage message)
    {
        message=default;
        if (bytes.Length!=CustomStateMessage.Size || !CustomWeaponProtocol.ReadHeader(bytes,out var bootstrap,out var digest)
            || bytes[65]>=8 || BinaryPrimitives.ReadUInt16LittleEndian(bytes[66..])==0 || bytes[113]>1) return false;
        ushort id=BinaryPrimitives.ReadUInt16LittleEndian(bytes[68..]),selected=BinaryPrimitives.ReadUInt16LittleEndian(bytes[82..]);
        if (id is < 10 or > 73 || selected!=0 && selected!=id || !Resolves(digest,new(id),manifest,definition)) return false;
        var owner=new WeaponOwnerContext(bootstrap.Start.MatchId,bootstrap.Start.AuthorityEpoch,bytes[65],bootstrap.SlotGeneration,BinaryPrimitives.ReadUInt16LittleEndian(bytes[66..]));
        var value=new CustomStateMessage(bootstrap,digest,new(id),BinaryPrimitives.ReadUInt32LittleEndian(bytes[70..]),BinaryPrimitives.ReadUInt32LittleEndian(bytes[74..]),
            BinaryPrimitives.ReadUInt32LittleEndian(bytes[78..]),selected==0 ? default:new(selected),BinaryPrimitives.ReadUInt32LittleEndian(bytes[84..]),ReadSnapshot(bytes[88..],owner,definition),bytes[113]!=0);
        if (!Valid(value,manifest,definition)) return false; message=value; return true;
    }
}
