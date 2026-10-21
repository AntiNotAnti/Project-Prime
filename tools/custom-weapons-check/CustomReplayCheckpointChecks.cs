using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using MphRead.Mods.CustomWeapons;
using MphRead.Mods.MapGen;
using MphRead.Mods.Network;

internal static class CustomReplayCheckpointChecks
{
    internal static int Run(string? output=null)
    {
        int checks=0;string root=Path.GetFullPath(output??Path.Combine(Path.GetTempPath(),"cw12f-checkpoint-"+Guid.NewGuid().ToString("N")));
        void Check(bool ok,string label){if(!ok)throw new InvalidOperationException(label);checks++;}
        void Reject(Action action,string label){try{action();}catch(Exception error)when(error is ArgumentException or InvalidDataException){Check(true,label);return;}throw new InvalidOperationException("Expected rejection: "+label);}
        void BadDescriptor(byte[] bytes,string label)=>Check(!ReplayCustomWeaponDescriptor.TryRead(bytes,out var descriptor)&&descriptor==null,label);
        byte[] Redigest(byte[] bytes){SHA256.HashData(bytes.AsSpan(0,bytes.Length-32),bytes.AsSpan(bytes.Length-32));return bytes;}
        CustomReplicaFlight[] Flights(ReplayCustomWeaponState state){var values=new CustomReplicaFlight[256];return values[..state.CopyFlights(values)];}
        void Equal(ReplayCustomWeaponState a,ReplayCustomWeaponState b,string label)
        {
            Check(a.World==b.World&&a.RecordingFrame==b.RecordingFrame&&a.Sequence==b.Sequence&&a.CompleteForRestore==b.CompleteForRestore&&Flights(a).SequenceEqual(Flights(b)),label+" exact clocks/anchors");
            for(int i=0;i<8;i++){a.TrySlot(i,out var x);b.TrySlot(i,out var y);Check(x==y,label+" slot "+i);}for(int i=0;i<a.Context.Layout.Count;i++){a.TryPickup(i,out var x);b.TryPickup(i,out var y);Check(x==y,label+" pickup "+i);}
            Check(a.CaptureCheckpointBytes().SequenceEqual(b.CaptureCheckpointBytes()),label+" canonical durable bytes");
        }
        try
        {
            Directory.CreateDirectory(root);using var f=new CustomReplayStateChecks.Fixture();var descriptor=new ReplayCustomWeaponDescriptor(f.Context);byte[] descriptorBytes=descriptor.CopyBytes();
            Check(ReplayCustomWeaponDescriptor.Version==1&&ReplayCustomWeaponDescriptor.HeaderSize==12&&ReplayCustomWeaponDescriptor.PlacementSize==28&&ReplayCustomWeaponDescriptor.MaximumBytes==20480,"frozen descriptor bounds");
            Check(ReplayCustomWeaponState.CheckpointVersion==1&&ReplayCustomWeaponState.MaximumCheckpointBytes==768*1024,"frozen durable checkpoint bounds");
            Check(ReplayCustomWeaponDescriptor.TryRead(descriptorBytes,out var decoded)&&decoded!.Count==f.Context.Layout.Count&&decoded.Map==f.Context.Layout.MapIdentity,"descriptor preserves exact historical map/layout");
            using var fresh=new CustomReplayStateChecks.Fixture();var context=decoded!.Bind(fresh.Package.Definition);Check(context.Digest==f.Context.Digest&&!ReferenceEquals(context,f.Context)&&context.Layout.Placements.SequenceEqual(f.Context.Layout.Placements),"fresh exact definition binds detached authored descriptor");
            var state=f.State(baselineFlight:true);state.Advance(1004,20);byte[] checkpoint=state.CaptureCheckpointBytes();Check(checkpoint.AsSpan(0,4).SequenceEqual("CWRS"u8)&&BinaryPrimitives.ReadUInt16LittleEndian(checkpoint.AsSpan(4))==1&&SHA256.HashData(checkpoint.AsSpan(0,checkpoint.Length-32)).AsSpan().SequenceEqual(checkpoint.AsSpan(checkpoint.Length-32)),"durable CWRS1 independent signature/version/SHA trailer");
            var restored=ReplayCustomWeaponState.RestoreCheckpointBytes(checkpoint,context);Equal(state,restored,"midflight cold durable restore");
            state.Advance(1005,21);restored.Advance(1005,21);Equal(state,restored,"cold suffix motion retains original per-tick anchor");
            var terminal=ReplayCustomWeaponFact.Contact(f.Context,state.Sequence+1,f.World,f.Contact());state.Accept(terminal,22);restored.Accept(terminal,22);Equal(state,restored,"cold suffix terminal equality");Check(restored.TryContact(f.Launch().Identity,out var contact,out uint contactFrame)&&contact==f.Contact()&&contactFrame==22,"contact history survives cold suffix");
            byte[] ended=restored.CaptureCheckpointBytes();var endedRestored=ReplayCustomWeaponState.RestoreCheckpointBytes(ended,context);Check(!endedRestored.Accept(terminal,22),"exact retry survives durable seen-ring restore");endedRestored.Accept(ReplayCustomWeaponFact.Launch(context,endedRestored.Sequence+1,f.Launch() with{Origin=f.Flight().SpawnPosition}),23);Check(Flights(endedRestored).Length==0,"durable terminal tombstone prevents late launch resurrection");
            var expired=f.State(baselineFlight:true);expired.Advance(1010,30);var expiredCold=ReplayCustomWeaponState.RestoreCheckpointBytes(expired.CaptureCheckpointBytes(),context);expiredCold.Accept(ReplayCustomWeaponFact.Launch(context,expiredCold.Sequence+1,f.Launch() with{Origin=f.Flight().SpawnPosition}),31);Check(Flights(expiredCold).Length==0,"expired baseline history does not reset lifetime on delayed launch");
            byte[] owned=(byte[])checkpoint.Clone();var independent=ReplayCustomWeaponState.RestoreCheckpointBytes(owned,context);owned.AsSpan().Clear();Check(independent.CaptureCheckpointBytes().SequenceEqual(checkpoint),"restore never borrows mutable checkpoint input");byte[] detached=descriptor.CopyBytes();detached.AsSpan().Clear();Check(descriptor.CopyBytes().SequenceEqual(descriptorBytes),"descriptor returns independent byte owners");
            // Recorded placements deliberately differ from today's stock and use
            // a historical room name: Bind must use serialized authored rows.
            var placements=f.Context.Layout.Placements.ToArray();placements[0]=placements[0] with{X4096=placements[0].X4096+4096};var mechanics=new CustomWeaponManifest(new[]{f.Package.Definition});using var registry=new WeaponRegistry(true,new[]{f.Package.Definition});var authoredLayout=new CustomPickupLayout(MapContentIdentity.BuiltIn("HISTORICAL_AUTHORED_ROOM"),mechanics,registry,placements);var authoredContext=new ReplayCustomWeaponContext(new(f.World,42,authoredLayout.Digest,f.Context.Bootstrap.Packages),f.Package.Definition,authoredLayout);var authoredDescriptor=new ReplayCustomWeaponDescriptor(authoredContext);Check(ReplayCustomWeaponDescriptor.TryRead(authoredDescriptor.CopyBytes(),out var historical),"historical nonstock room descriptor parses");var rebound=historical!.Bind(fresh.Package.Definition);Check(rebound.Layout.Placements.SequenceEqual(placements)&&rebound.Layout.MapIdentity.RoomKey=="HISTORICAL_AUTHORED_ROOM"&&rebound.Layout.Digest==authoredLayout.Digest,"historical authored placement restored without stock lookup");
            var wrongBootstrap=new ReplayCustomWeaponBootstrap(f.World,43,f.Context.Layout.Digest,f.Context.Bootstrap.Packages);var wrongContext=new ReplayCustomWeaponContext(wrongBootstrap,f.Package.Definition,f.Context.Layout);Reject(()=>ReplayCustomWeaponState.RestoreCheckpointBytes(checkpoint,wrongContext),"different bootstrap context cannot restore");Reject(()=>descriptor.Bind(WeaponDefinition.Create(Guid.Parse("12f412f4-1122-3344-5566-778899aabbcc"),"Wrong historical definition",f.Package.Definition.Mechanics)),"wrong exact definition bind refuses");
            for(int n=0;n<descriptorBytes.Length;n++)BadDescriptor(descriptorBytes[..n],"descriptor truncation "+n);BadDescriptor(descriptorBytes.Concat(new byte[]{0}).ToArray(),"descriptor trailing bytes");BadDescriptor(new byte[20481],"descriptor oversize");
            int bootLength=BinaryPrimitives.ReadUInt16LittleEndian(descriptorBytes.AsSpan(8)),nameLength=BinaryPrimitives.ReadUInt16LittleEndian(descriptorBytes.AsSpan(10));int map=12+bootLength,name=map+81,count=name+nameLength,placement=count+2;
            foreach(int at in new[]{0,4,6,8,10,map,map+1,name,count,placement+2}){var bad=(byte[])descriptorBytes.Clone();bad[at]=255;BadDescriptor(bad,"descriptor malformed field "+at);}var invalidName=(byte[])descriptorBytes.Clone();invalidName[name]=10;BadDescriptor(invalidName,"control character historical name");
            foreach(int offset in new[]{placement,placement+20}){var bad=(byte[])descriptorBytes.Clone();bad.AsSpan(offset,offset==placement?2:4).Clear();BadDescriptor(bad,"zero required authored placement field "+offset);}
            var changedLayout=(byte[])descriptorBytes.Clone();changedLayout[placement+4]^=1;Check(ReplayCustomWeaponDescriptor.TryRead(changedLayout,out var contradictory),"bounded authored row mutation remains structurally readable");Reject(()=>contradictory!.Bind(f.Package.Definition),"authored row digest contradiction rejected at Bind");
            for(int n=0;n<checkpoint.Length;n++)Reject(()=>ReplayCustomWeaponState.RestoreCheckpointBytes(checkpoint.AsSpan(0,n),context),"checkpoint truncation "+n);Reject(()=>ReplayCustomWeaponState.RestoreCheckpointBytes(checkpoint.Concat(new byte[]{0}).ToArray(),context),"checkpoint trailing bytes");Reject(()=>ReplayCustomWeaponState.RestoreCheckpointBytes(new byte[768*1024+1],context),"checkpoint over budget");
            int pickupCountAt=60+8*41,firstPickup=pickupCountAt+1,flightCount=firstPickup+f.Context.Layout.Count*25;
            foreach(int at in new[]{0,4,6,8,40,48,52,pickupCountAt,firstPickup+16,flightCount}){var bad=(byte[])checkpoint.Clone();bad[at]=255;Reject(()=>ReplayCustomWeaponState.RestoreCheckpointBytes(Redigest(bad),context),"redigested semantic checkpoint mutation "+at);}
            foreach((int at,int length) field in new[]{(52,8),(firstPickup+4,8)}){var bad=(byte[])checkpoint.Clone();bad.AsSpan(field.at,field.length).Clear();Reject(()=>ReplayCustomWeaponState.RestoreCheckpointBytes(Redigest(bad),context),"redigested zero semantic checkpoint field "+field.at);}
            var impossibleSequence=(byte[])checkpoint.Clone();BinaryPrimitives.WriteUInt64LittleEndian(impossibleSequence.AsSpan(52),2);Reject(()=>ReplayCustomWeaponState.RestoreCheckpointBytes(Redigest(impossibleSequence),context),"complete pickup checkpoint cannot claim sequence2 baseline-only history");
            var missingHistory=(byte[])checkpoint.Clone();int historyCount=flightCount+2+2+4+219;BinaryPrimitives.WriteUInt16LittleEndian(missingHistory.AsSpan(historyCount),0);Reject(()=>ReplayCustomWeaponState.RestoreCheckpointBytes(Redigest(missingHistory),context),"redigested active flight missing canonical source history refused");
            // Recompute the outer checksum while contradicting retained semantic
            // facts and recent retry digests; SHA integrity alone is insufficient.
            int seenCount=(int)Math.Min(restored.Sequence,1024UL),seenAt=ended.Length-32-2-seenCount*40;
            var badBootstrapDigest=(byte[])ended.Clone();badBootstrapDigest[seenAt+2+8]^=1;Reject(()=>ReplayCustomWeaponState.RestoreCheckpointBytes(Redigest(badBootstrapDigest),context),"redigested bootstrap seen digest contradicts exact bootstrap fact");
            int contactSequence=(int)terminal.Sequence;var badContactDigest=(byte[])ended.Clone();badContactDigest[seenAt+2+(contactSequence-1)*40+8]^=1;Reject(()=>ReplayCustomWeaponState.RestoreCheckpointBytes(Redigest(badContactDigest),context),"redigested recent retry digest contradicts retained contact fact");
            int retainedContact=-1;for(int at=60;at+157<=ended.Length-32;at++)if(ended[at]==250&&ended[at+2]==5&&ReplayCustomWeaponFact.TryRead(ended.AsSpan(at,157),context,out var candidate)&&candidate!.Kind==ReplayCustomWeaponFactKind.Contact){retainedContact=at;break;}
            Check(retainedContact>=0,"checkpoint retains exact canonical contact fact bytes");var badRetained=(byte[])ended.Clone();badRetained[retainedContact+49+85]^=1;Reject(()=>ReplayCustomWeaponState.RestoreCheckpointBytes(Redigest(badRetained),context),"redigested valid semantic contact mutation disagrees with retained retry digest");
            File.WriteAllBytes(Path.Combine(root,"descriptor.cwrd"),descriptorBytes);File.WriteAllBytes(Path.Combine(root,"midflight.cwrs"),checkpoint);File.WriteAllText(Path.Combine(root,"results.json"),JsonSerializer.Serialize(new{checks,scope="Detached durable descriptor/checkpoint cold restores and supplied-fact suffix parity. No native archive/reopen/seek scene or custom playback enable claim."}));Console.WriteLine($"PASS {checks} custom replay durable checkpoint checks");return 0;
        }
        catch(Exception error){Console.Error.WriteLine("CUSTOM REPLAY CHECKPOINT FAIL "+error);return 1;}
    }
}
