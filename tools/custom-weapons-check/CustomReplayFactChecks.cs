using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using MphRead;
using MphRead.Mods.CustomWeapons;
using MphRead.Mods.CustomWeapons.Packages;
using MphRead.Mods.MapGen;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

// Constructed file facts, not accepted gameplay/source evidence or health authority.
internal static class CustomReplayFactChecks
{
    internal static int Run(string? output=null)
    {
        int checks=0;string root=Path.GetFullPath(output??Path.Combine(Path.GetTempPath(),"cw12f-facts-"+Guid.NewGuid().ToString("N")));
        void Check(bool ok,string label){if(!ok)throw new InvalidOperationException(label);checks++;}
        void Reject(Action action,string label){try{action();}catch(Exception e)when(e is ArgumentException or InvalidDataException or InvalidOperationException){Check(true,label);return;}throw new InvalidOperationException("Expected rejection: "+label);}
        string native=JsonSerializer.Serialize(new{Weapons.WeaponsMP,Weapons.Weapons1P});
        try
        {
            Directory.CreateDirectory(root);var package=WeaponPackage.Parse(WeaponPackageReference.BuildRiftlineM7());var definition=package.Definition;var manifest=new CustomWeaponManifest(new[]{definition});using var registry=new WeaponRegistry(true,new[]{definition});
            var layout=new CustomPickupLayout(MapContentIdentity.BuiltIn("MP3 PROVING GROUND"),manifest,registry,StockRiftlinePickups.ForRoom("MP3 PROVING GROUND",definition));
            var start=new MatchStartIdentity(7,0x123456789abcdef0,3);var world=new WorldBootstrapIdentity(start,2,5,990,1,1);var bootstrap=new ReplayCustomWeaponBootstrap(world,42,layout.Digest,new(new[]{package}));var context=new ReplayCustomWeaponContext(bootstrap,definition,layout);
            byte[] Header(byte kind,ulong sequence,uint frame,byte[] body)
            {var bytes=new byte[49+body.Length];bytes[0]=250;bytes[1]=1;bytes[2]=kind;BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(3),(ushort)body.Length);BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(5),sequence);BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(13),frame);SHA256.HashData(bootstrap.CopyBytes()).CopyTo(bytes,17);body.CopyTo(bytes,49);return bytes;}
            void Bad(byte[] bytes,string label)=>Check(!ReplayCustomWeaponFact.TryRead(bytes,context,out var fact)&&fact==null,label);
            Check(ReplayCustomWeaponFact.Tag==250&&ReplayCustomWeaponFact.Version==1&&ReplayCustomWeaponFact.HeaderSize==49&&ReplayCustomWeaponFact.MaximumBytes==16384&&ReplayCustomWeaponFact.PickupSize==25,"literal file fact schema250/v1 bounds");
            var boot=ReplayCustomWeaponFact.Bootstrap(context);Check(boot.CopyBytes().SequenceEqual(Header(1,1,990,bootstrap.CopyBytes())),"independent bootstrap fact header and canonical body");
            var slots=new CustomReplicaSlot[8];for(byte i=0;i<8;i++)slots[i]=new(i,false,0,0,false,false,default,0,0,default);
            byte[] emptyRows=new byte[328];for(int i=0;i<8;i++)emptyRows[i*41]=(byte)i;
            var state=ReplayCustomWeaponFact.State(context,2,1001,slots);Check(state.CopyBytes().SequenceEqual(Header(3,2,1001,emptyRows)),"independent eight-row empty state vector");
            var emptyCapture=new CustomReplicaCapture(7,start.AuthorityEpoch,manifest.Digest,1001,8,0);var baseline=ReplayCustomWeaponFact.Baseline(context,3,emptyCapture,slots,Array.Empty<CustomReplicaFlight>());
            Check(baseline.CopyBytes().SequenceEqual(Header(2,3,1001,emptyRows.Concat(new byte[]{0}).ToArray())),"independent empty baseline count offset328");
            var owner=new WeaponOwnerContext(7,start.AuthorityEpoch,1,5,10);var runtime=new WeaponRuntimeState(definition,owner,1000);runtime.SetEquipped(1000,true);runtime.TrySetOptic(1000,1,1);runtime.TryFire(1000,1,true);var snapshot=runtime.Snapshot();slots[1]=new(1,true,5,10,false,true,new(10),1,1000,snapshot);
            var action=new CustomWeaponAction(world,manifest.Digest,1,10,new(10),1,1000,999,0,CustomWeaponActionKind.Fire,1,new(0,.5f,0),Vector3.UnitZ);
            var certificate=new CustomSourceCertificate(action,1000,999,0,Vector3.Zero,Vector3.UnitZ,true,false,false);var launch=new CustomReplicaLaunch(certificate,1001,new(0,.4f,0),Vector3.UnitZ);
            var flight=new CustomReplicaFlight(CustomActionIdentity.For(action),certificate,1001,1001+(uint)definition.Mechanics.LifetimeTicks+2,launch.Origin,launch.Origin,Vector3.UnitZ,Vector3.UnitZ,0,definition.Mechanics.LifetimeTicks/60f);
            var capture=new CustomReplicaCapture(7,start.AuthorityEpoch,manifest.Digest,1001,8,1);var full=ReplayCustomWeaponFact.Baseline(context,4,capture,slots,new[]{flight});
            Check(full.CopyBytes().Length==49+328+1+219,"baseline one-flight literal219-byte width");var copiedSlots=new CustomReplicaSlot[8];var copiedFlights=new CustomReplicaFlight[64];Check(full.CopyTable(context,copiedSlots,copiedFlights,out var restored)&&restored==capture&&copiedSlots.SequenceEqual(slots)&&copiedFlights[0]==flight,"baseline detached typed state and source certificate roundtrip");
            var launched=ReplayCustomWeaponFact.Launch(context,5,launch);Check(launched.CopyBytes().Length==220&&launched.TryLaunch(context,out var restoredLaunch)&&restoredLaunch==launch,"launch171-byte body roundtrip");
            var outcome=new CustomContactOutcome(new(new(CustomActionIdentity.For(action),new(2,6,11),1,CustomImpactChannel.Body),Vector3.UnitZ,false),1001,new(200,135,0,0,9));var contact=ReplayCustomWeaponFact.Contact(context,6,world,outcome);
            byte[] semantic=new byte[108];semantic[0]=1;
            void U16(int at,ushort value)=>BinaryPrimitives.WriteUInt16LittleEndian(semantic.AsSpan(at),value);
            void U32(int at,uint value)=>BinaryPrimitives.WriteUInt32LittleEndian(semantic.AsSpan(at),value);
            U16(1,7);BinaryPrimitives.WriteUInt64LittleEndian(semantic.AsSpan(3),0x123456789abcdef0);U32(11,3);U32(15,2);U16(19,5);U32(21,990);U32(25,1);U32(29,1);
            Convert.FromHexString("a0bc00be2884eb5b4f188f6453cc452060ccf920e26af610f419d0352709b6e9").CopyTo(semantic,33);
            semantic[65]=1;U16(66,10);U16(68,10);U32(70,1);U32(74,1001);semantic[78]=2;U16(79,6);U16(81,11);semantic[83]=1;semantic[84]=(byte)CustomImpactChannel.Body;BinaryPrimitives.WriteSingleLittleEndian(semantic.AsSpan(93),1);U16(98,200);U16(100,135);U16(106,9);
            byte[] nativeContact=new byte[116];CustomCombatResults.Write(new CustomContactMessage(world,123,outcome),nativeContact,manifest,definition);
            Check(nativeContact.AsSpan(0,108).SequenceEqual(semantic),"independent literal token-free semantic contact vector");Check(contact.CopyBytes().SequenceEqual(Header(5,6,1001,semantic)),"token-free contact108 bytes equal native116-byte semantic prefix");
            var tokenOther=new byte[116];CustomCombatResults.Write(new CustomContactMessage(world,456,outcome),tokenOther,manifest,definition);Check(!nativeContact.SequenceEqual(tokenOther)&&nativeContact.AsSpan(0,108).SequenceEqual(tokenOther.AsSpan(0,108))&&ReplayCustomWeaponFact.Contact(context,6,world,outcome).CopyBytes().SequenceEqual(contact.CopyBytes()),"delivery token changes never alter same-sequence replay contact fact");
            Reject(()=>CustomCombatResults.Write(new CustomContactMessage(world,0,outcome),new byte[116],manifest,definition),"native contact still requires nonzero delivery token");Check(contact.TryContact(context,out var source,out var restoredContact)&&source==world&&restoredContact==outcome,"contact typed semantic roundtrip");
            var item=new CustomPickupItemState(new(context.PickupWorld,layout.Placements[0].SpawnId,1),990,true,0);var pickup=ReplayCustomWeaponFact.Pickup(context,7,1001,0,item);byte[] pickupBody=new byte[25];pickupBody[1]=(byte)layout.Count;BinaryPrimitives.WriteUInt16LittleEndian(pickupBody.AsSpan(2),item.Key.SpawnId);BinaryPrimitives.WriteUInt64LittleEndian(pickupBody.AsSpan(4),1);BinaryPrimitives.WriteUInt32LittleEndian(pickupBody.AsSpan(12),990);pickupBody[16]=1;
            Check(pickup.CopyBytes().SequenceEqual(Header(6,7,1001,pickupBody))&&pickup.TryPickup(context,out int index,out var restoredItem)&&index==0&&restoredItem==item,"independent pickup25-byte vector with exact world/incarnation projection");
            var facts=new[]{boot,baseline,state,full,launched,contact,pickup};foreach(var fact in facts)
            {
                byte[] bytes=fact.CopyBytes();Check(ReplayCustomWeaponFact.TryRead(bytes,context,out var decoded)&&decoded!.CopyBytes().SequenceEqual(bytes),"strict roundtrip "+fact.Kind);for(int n=0;n<bytes.Length;n++)Bad(bytes[..n],"truncated "+fact.Kind+" "+n);Bad(bytes.Concat(new byte[]{0}).ToArray(),"trailing "+fact.Kind);
                foreach(int offset in new[]{0,1,2,3,4,17,48}){var changed=(byte[])bytes.Clone();changed[offset]^=0x80;Bad(changed,"schema/kind/length/context mutation "+fact.Kind+" "+offset);}var zero=(byte[])bytes.Clone();zero.AsSpan(5,8).Clear();Bad(zero,"zero sequence "+fact.Kind);var early=(byte[])bytes.Clone();BinaryPrimitives.WriteUInt32LittleEndian(early.AsSpan(13),989);Bad(early,"below bootstrap frame floor "+fact.Kind);
                byte[] detached=fact.CopyBytes();detached.AsSpan().Clear();Check(fact.CopyBytes().SequenceEqual(bytes),"defensive bytes "+fact.Kind);
            }
            Bad(new byte[16385],"oversized payload");Reject(()=>ReplayCustomWeaponFact.State(context,1,1001,slots),"reserved sequence1 cannot hold state");Reject(()=>ReplayCustomWeaponFact.Baseline(context,8,capture with{FlightCount=2},slots,new[]{flight,flight}),"duplicate flight action rejected");Reject(()=>ReplayCustomWeaponFact.Baseline(context,8,capture with{FlightCount=65},slots,new CustomReplicaFlight[65]),"flight count65 rejected");
            var impossible=(CustomReplicaSlot[])slots.Clone();impossible[1]=impossible[1] with{State=snapshot with{Magazine=definition.Mechanics.MagazineCapacity+1}};Reject(()=>ReplayCustomWeaponFact.State(context,8,1001,impossible),"impossible magazine rejected");impossible[1]=slots[1] with{HasWeapon=false,Selected=default};Reject(()=>ReplayCustomWeaponFact.State(context,8,1001,impossible),"typed absent weapon cannot normalize away nonzero state");
            Reject(()=>ReplayCustomWeaponFact.Baseline(context,8,capture,slots,new[]{flight with{Action=default}}),"flight identity differing from certificate rejected before write");Reject(()=>ReplayCustomWeaponFact.Launch(context,8,launch with{Origin=new(float.NaN,0,0)}),"NaN launch rejected");Reject(()=>ReplayCustomWeaponFact.Baseline(context,8,capture,slots,new[]{flight with{AgeSeconds=float.NaN}}),"NaN flight rejected");
            Reject(()=>ReplayCustomWeaponFact.Pickup(context,8,1001,0,item with{Key=item.Key with{Incarnation=0}}),"zero pickup incarnation rejected");Reject(()=>ReplayCustomWeaponFact.Pickup(context,8,1001,0,item with{Key=item.Key with{Incarnation=2}}),"noninitial incarnation with zero revision rejected");Reject(()=>ReplayCustomWeaponFact.Pickup(context,8,1001,layout.Count,item),"unknown pickup index rejected");
            var otherBootstrap=new ReplayCustomWeaponBootstrap(world,43,layout.Digest,new(new[]{package}));var otherContext=new ReplayCustomWeaponContext(otherBootstrap,definition,layout);Check(!ReplayCustomWeaponFact.TryRead(contact.CopyBytes(),otherContext,out _),"different package configuration/context cannot read fact");
            var otherLayout=new CustomPickupLayout(MapContentIdentity.BuiltIn("MP2 HARVESTER"),manifest,registry,layout.Placements.ToArray());Reject(()=>new ReplayCustomWeaponContext(bootstrap,definition,otherLayout),"actual layout digest mismatch rejected");Reject(()=>ReplayCustomWeaponFact.Pickup(context,8,1001,0,item with{Key=item.Key with{World=new(start,manifest.Digest,otherLayout.Digest)}}),"pickup from another world rejected");
            // Recorder callbacks copy payloads immediately: transient record owners
            // are released after Accepted returns and are never retained here.
            var stream=new List<(uint Frame,uint World,byte[] Bytes)>();var recorder=new ReplayRecorder();
            recorder.Accepted+=record=>{if(record.Kind==ReplayFactKind.CustomWeapon)stream.Add((record.RecordingFrame,record.ServerTick,record.Payload.ToArray()));};
            var match=new MatchStatePacket{MatchId=7,AuthorityEpoch=start.AuthorityEpoch,RoomKey="MP3 PROVING GROUND",NextRoomKey="",Mode=(byte)GameMode.Battle,RuleBits=(uint)(MatchModifierFlags.RiftlineM7Available|MatchModifierFlags.PackagedWeaponAvailable)};
            Check((int)ReplayFactKind.CustomWeapon==8&&(int)ReplayFactKind.AuthorityWorld==7&&(int)ReplayFactKind.Match==0,"custom timeline enum appended without native renumbering");
            Check(!recorder.AcceptCustomWeaponBaseline(context,capture,slots,new[]{flight},50)&&stream.Count==0,"recorder refuses custom baseline before native match");
            var wrong=match;wrong.MatchId=8;recorder.AcceptMatch(wrong,1);Check(!recorder.AcceptCustomWeaponBaseline(context,capture,slots,new[]{flight},50)&&stream.Count==0,"recorder refuses wrong match scope");
            wrong=match;wrong.AuthorityEpoch++;recorder.AcceptMatch(wrong,2);Check(!recorder.AcceptCustomWeaponBaseline(context,capture,slots,new[]{flight},50),"recorder refuses wrong authority epoch");
            foreach(uint bits in new[]{0u,(uint)MatchModifierFlags.RiftlineM7Available,(uint)MatchModifierFlags.PackagedWeaponAvailable}){wrong=match;wrong.RuleBits=bits;recorder.AcceptMatch(wrong,3);Check(!recorder.AcceptCustomWeaponBaseline(context,capture,slots,new[]{flight},50),"both custom match bits required: "+bits);}
            recorder.AcceptMatch(match,4);Check(recorder.AcceptCustomWeaponBaseline(context,capture,slots,new[]{flight},50)&&recorder.AcceptedCustomWeaponFacts==2&&stream.Count==2&&recorder.CustomWeaponContext==context,"recorder publishes validated bootstrap and baseline");
            Check(stream[0].Frame==50&&stream[0].World==990&&stream[0].Bytes.SequenceEqual(boot.CopyBytes())&&stream[1].Frame==50&&stream[1].World==1001&&stream[1].Bytes.SequenceEqual(ReplayCustomWeaponFact.Baseline(context,2,capture,slots,new[]{flight}).CopyBytes()),"accepted event copies preserve distinct recording and authority clocks and exact initial sequences");
            Check(!recorder.AcceptCustomWeaponBaseline(context,capture,slots,new[]{flight},51)&&stream.Count==2,"same-context baseline duplicate is inert");
            Check(recorder.AcceptCustomWeaponState(9,1001,slots,52)&&!recorder.AcceptCustomWeaponState(9,1001,slots,53)&&!recorder.AcceptCustomWeaponState(8,1001,slots,54)&&!recorder.AcceptCustomWeaponState(0,1001,slots,54)&&stream.Count==3,"state revisions strictly deduplicate without sequence holes");
            var launchEvent=new CustomReplicaEvent(CustomReplicaKind.Launch,123,1001,launch,default);
            Check(!recorder.AcceptCustomWeaponEvent(launchEvent with{Token=0},55)&&!recorder.AcceptCustomWeaponEvent(launchEvent with{World=1002},55)&&stream.Count==3,"event requires nonzero delivery token and exact semantic world");
            Check(recorder.AcceptCustomWeaponEvent(launchEvent,55)&&stream[^1].Bytes.SequenceEqual(ReplayCustomWeaponFact.Launch(context,4,launch).CopyBytes()),"accepted launch strips outer token into canonical next file sequence");
            var contactEvent=new CustomReplicaEvent(CustomReplicaKind.Contact,456,1001,default,new(world,456,outcome));
            Check(recorder.AcceptCustomWeaponEvent(contactEvent,56)&&stream[^1].Bytes.SequenceEqual(ReplayCustomWeaponFact.Contact(context,5,world,outcome).CopyBytes()),"accepted contact strips delivery wrapper and token");
            Check(recorder.AcceptCustomWeaponPickup(0,1001,item,57)&&!recorder.AcceptCustomWeaponPickup(0,1002,item,58)&&stream.Count==6,"identical pickup value deduplicates despite different recording/world clocks");
            Check(recorder.AcceptedCustomWeaponFacts==6&&!recorder.CustomWeaponCaptureFailed,"accepted fact count tracks only published semantic facts");
            byte[] retainedCopy=(byte[])stream[0].Bytes.Clone();recorder.RetireCustomWeaponFacts();Check(recorder.CustomWeaponContext==null&&recorder.AcceptedCustomWeaponFacts==6&&!recorder.AcceptCustomWeaponState(10,1002,slots,59),"retire clears capture scope but preserves accepted count");
            recorder.Reset();Check(recorder.CustomWeaponContext==null&&!recorder.CustomWeaponCaptureFailed&&recorder.AcceptedCustomWeaponFacts==0&&stream[0].Bytes.SequenceEqual(retainedCopy),"reset clears recorder state without invalidating copied event payloads");
            stream.Clear();recorder.AcceptMatch(match,60);Check(!recorder.AcceptCustomWeaponBaseline(context,capture,slots,new[]{flight with{Action=default}},61)&&recorder.CustomWeaponCaptureFailed&&recorder.CustomWeaponContext==null&&stream.Count==0&&recorder.AcceptedCustomWeaponFacts==0,"invalid baseline publishes neither bootstrap nor baseline and fails closed");
            Check(!recorder.AcceptCustomWeaponBaseline(context,capture,slots,new[]{flight},62),"failed baseline permanently closes capture until reset");
            recorder.Reset();stream.Clear();recorder.AcceptMatch(match,63);Check(recorder.AcceptCustomWeaponBaseline(context,capture,slots,new[]{flight},64),"reset allows a new validated capture scope");
            Check(!recorder.AcceptCustomWeaponState(1,1001,impossible,65)&&recorder.CustomWeaponCaptureFailed&&stream.Count==2&&!recorder.AcceptCustomWeaponEvent(launchEvent,66)&&!recorder.AcceptCustomWeaponPickup(0,1001,item,67),"invalid typed state closes all subsequent custom capture without partial publication");
            recorder.Reset();var failedSink=new ReplayRecorder();int sinkCalls=0;failedSink.Accepted+=record=>{if(record.Kind==ReplayFactKind.CustomWeapon){sinkCalls++;throw new IOException("constructed failing archive sink");}};failedSink.AcceptMatch(match,70);
            Check(!failedSink.AcceptCustomWeaponBaseline(context,capture,slots,new[]{flight},71)&&failedSink.CustomWeaponCaptureFailed&&sinkCalls==1,"observer IO failure is contained and stops baseline publication after notified prefix");
            Check(!failedSink.AcceptCustomWeaponState(1,1001,slots,72)&&!failedSink.AcceptCustomWeaponEvent(launchEvent,73)&&!failedSink.AcceptCustomWeaponPickup(0,1001,item,74)&&sinkCalls==1,"observer failure closes later capture without notifying additional facts");failedSink.Reset();
            var passive=new ReplayReplicaState();Reject(()=>passive.Accept(boot.CopyBytes(),1),"passive native replica explicitly refuses custom file schema until archive contract exists");
            CustomReplayBoundaryChecks.Run(Check);Check(JsonSerializer.Serialize(new{Weapons.WeaponsMP,Weapons.Weapons1P})==native,"native weapon tables unchanged");foreach(var fact in facts)File.WriteAllBytes(Path.Combine(root,fact.Kind+"-"+fact.Sequence+".bin"),fact.CopyBytes());File.WriteAllText(Path.Combine(root,"results.json"),JsonSerializer.Serialize(new{checks,scope="Constructed detached file facts and strict validation only. No source authentication, accepted health authority, recording integration or custom replay playback claim."}));Console.WriteLine($"PASS {checks} custom replay file-fact checks");return 0;
        }
        catch(Exception error){Console.Error.WriteLine("CUSTOM REPLAY FACTS FAIL "+error);return 1;}
    }
}
