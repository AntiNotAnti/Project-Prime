using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using MphRead;
using MphRead.Mods.CustomWeapons;
using MphRead.Mods.Network;
using MphRead.Mods.MapGen;
using MphRead.Mods.Multiplayer;
using MphRead.NetTest;

// Explicit metadata-only replica inspection. No archive/resources, Scene or IO
// is required to decode this detached capability; native restore stays closed.
internal static class CustomReplayReplicaCheckpointChecks
{
    internal static int Run(string? output=null)
    {
        int checks=0;string root=Path.GetFullPath(output??Path.Combine(Path.GetTempPath(),"cw12f-replica-"+Guid.NewGuid().ToString("N")));
        void Check(bool ok,string label){if(!ok)throw new InvalidOperationException(label);checks++;}
        void Reject(Action action,string label){try{action();}catch(Exception error)when(error is InvalidDataException or ArgumentException or InvalidOperationException){Check(true,label);return;}throw new InvalidOperationException("Expected rejection: "+label);}
        try
        {
            Directory.CreateDirectory(root);using var f=new CustomReplayStateChecks.Fixture();var descriptor=ReplayCustomWeaponDescriptor.ForInspection(f.Context);byte[] metadata=descriptor.CopyBytes();
            bool metadataRead=ReplayCustomWeaponDescriptor.TryRead(metadata,out var decoded);Check(BinaryPrimitives.ReadUInt16LittleEndian(metadata.AsSpan(4))==2&&metadataRead,"CWRD2 canonical metadata descriptor parses");
            var coldContext=decoded!.InspectContext();Check(coldContext.Digest==f.Context.Digest&&coldContext.Definition.Mechanics.ToCanonicalBytes().SequenceEqual(f.Package.Definition.Mechanics.ToCanonicalBytes())&&coldContext.Definition.Name==f.Package.Definition.Name,"cold metadata inspection reconstructs exact definition and layout without store/resource lookup");
            var legacy=new ReplayCustomWeaponDescriptor(f.Context);Check(BinaryPrimitives.ReadUInt16LittleEndian(legacy.CopyBytes().AsSpan(4))==1,"legacy descriptor writer remains CWRD1");Reject(()=>legacy.InspectContext(),"legacy descriptor cannot invent inspection metadata");
            byte[] nativeBefore=new ReplayReplicaState().CaptureCheckpoint().Bytes.ToArray();Check(BinaryPrimitives.ReadUInt16LittleEndian(nativeBefore.AsSpan(4))==16&&nativeBefore[6]==86&&nativeBefore[7]==87,"native85/16 declares phase64 without custom capability");
            var match=new MatchStatePacket{MatchId=7,AuthorityEpoch=f.World.Start.AuthorityEpoch,RoomKey="MP3 PROVING GROUND",NextRoomKey="",Mode=(byte)GameMode.Battle,RuleBits=(uint)(MatchModifierFlags.RiftlineM7Available|MatchModifierFlags.PackagedWeaponAvailable)};
            byte[] MatchBytes(MatchStatePacket value){var bytes=new byte[1+MatchStatePacket.Size];bytes[0]=(byte)PacketType.MatchState;value.Write(bytes.AsSpan(1));return bytes;}
            var configuration=new SessionStatePacket{MatchId=7,AuthorityEpoch=f.World.Start.AuthorityEpoch,StartGeneration=f.World.Start.StartGeneration,Revision=1,MaxPlayers=8,OwnerSlot=0,Phase=SessionPhase.InMatch,Policy=ServerSessionPolicy.Lobby,WorldProfile=MatchWorldProfile.Resolve(2),Match=new MatchDefinition{RoomKey="MP3 PROVING GROUND",Mode=GameMode.Battle,RiftlineM7Available=true,PackagedWeaponAvailable=true,PointGoal=999}};
            byte[] ConfigurationBytes(SessionStatePacket value){var bytes=new byte[1+SessionStatePacket.Size];bytes[0]=(byte)PacketType.SessionState;value.Write(bytes.AsSpan(1));return bytes;}
            Check(SessionStatePacket.TryRead(ConfigurationBytes(configuration).AsSpan(1),out _,allowRetiredEnhanced:true)&&!SessionStatePacket.TryReadArchived(ConfigurationBytes(configuration).AsSpan(1),out _),"valid live custom configuration parses while ordinary native archived parser stays closed");
            var inspection=new ReplayReplicaState();inspection.EnableCustomInspection(descriptor);inspection.Accept(MatchBytes(match),1);inspection.Accept(ConfigurationBytes(configuration),2);Check(inspection.Configuration is {} acceptedConfiguration&&acceptedConfiguration.StartGeneration==configuration.StartGeneration&&acceptedConfiguration.Match.PackagedWeaponAvailable,"explicit inspection accepts actual exact custom session configuration");inspection.Accept(ReplayCustomWeaponFact.Bootstrap(f.Context).CopyBytes(),10);
            var baseline=ReplayCustomWeaponFact.Baseline(f.Context,2,new(7,f.World.Start.AuthorityEpoch,f.Context.Bootstrap.Packages.MechanicsDigest,1001,8,1),f.Slots,new[]{f.Flight()});inspection.Accept(baseline.CopyBytes(),11);
            for(int i=0;i<f.Context.Layout.Count;i++)inspection.Accept(f.Pickup(inspection.CustomWeapons!.Sequence+1,i).CopyBytes(),12);
            Check(inspection.CustomWeapons is {CompleteForRestore:true},"actual replica Accept composes custom match/bootstrap/baseline/pickup scope");uint rng1=inspection.Rng1,rng2=inspection.Rng2;
            var checkpoint=inspection.CaptureCheckpoint();var layout=ReplayReplicaState.InspectCustomCheckpointLayout(checkpoint);Check(layout.Version==17&&layout.Protocol==86&&layout.IntentBytes==1299&&layout.Has(ReplayReplicaCheckpointFeatures.CustomWeapons)&&checkpoint.Bytes[7]==119,"custom85/17 declares exact phase64/custom32 feature119");
            byte[] retainedNative83=Protocol83CheckpointFixtures.ToProtocol83(nativeBefore);
            var oldNativeLayout=ReplayReplicaState.InspectCheckpointLayout(new(retainedNative83));
            Check(oldNativeLayout.Version==14&&oldNativeLayout.Protocol==83&&oldNativeLayout.PlayerBytes==192&&oldNativeLayout.IntentBytes==1298&&retainedNative83[7]==23,"published native83/14 retains192/1298 rows and feature23");
            var oldNative=new ReplayReplicaState();oldNative.RestoreCheckpoint(new(retainedNative83));
            Check(oldNative.CaptureCheckpoint().Bytes.SequenceEqual(nativeBefore),"retained native83/14 restores unchanged facts into current85/16");
            byte[] retainedCustom83=Protocol83CheckpointFixtures.ToProtocol83(checkpoint.Bytes);
            var oldCustomLayout=ReplayReplicaState.InspectCustomCheckpointLayout(new(retainedCustom83));
            Check(oldCustomLayout.Version==15&&oldCustomLayout.Protocol==83&&oldCustomLayout.PlayerBytes==192&&oldCustomLayout.IntentBytes==1298&&retainedCustom83[7]==55,"published custom83/15 preserves grapple16 and custom32 without phase64");
            var oldCustom=new ReplayReplicaState();oldCustom.RestoreCustomCheckpoint(new(retainedCustom83));
            Check(oldCustom.CaptureCheckpoint().Bytes.SequenceEqual(checkpoint.Bytes),"retained custom83/15 cold restore preserves all native/custom facts and tails");
            Reject(()=>new ReplayReplicaState().RestoreCheckpoint(new(retainedCustom83)),"native restore still refuses retained custom83/15");
            byte[] activePhase=checkpoint.Bytes.ToArray();int firstActor=Protocol83CheckpointFixtures.FirstActorOffset(activePhase);
            activePhase[firstActor+7+Protocol83CheckpointFixtures.PublishedAuxiliaryOffset]|=0x10;
            Reject(()=>Protocol83CheckpointFixtures.ToProtocol83(activePhase),"retained83 projector refuses accepted phase at independently checked byte114");
            byte[] requestedPhase=checkpoint.Bytes.ToArray();requestedPhase[firstActor+7+192+1+1298]=1;
            Reject(()=>Protocol83CheckpointFixtures.ToProtocol83(requestedPhase),"retained83 projector refuses requested phase instead of silently dropping it");
            var timedOwner=new ReplayReplicaState();timedOwner.RestoreCustomCheckpoint(new(checkpoint.Bytes));timedOwner.TimedShots.Begin(f.World.Start.AuthorityEpoch,7,42,TimedGrantProofMode.Shadow);var combined=timedOwner.CaptureCheckpoint();Check(combined.Bytes[7]==127,"combined custom32 and timed8 checkpoint selects all127 feature bits");var timedCold=new ReplayReplicaState();timedCold.RestoreCustomCheckpoint(new(combined.Bytes));Check(timedCold.TimedShots.Enabled&&timedCold.CustomWeapons!=null&&timedCold.CaptureCheckpoint().Bytes.SequenceEqual(combined.Bytes),"combined timed/custom appendix cold restore preserves exact canonical order and enabled capability");
            byte[] retainedTimed83=Protocol83CheckpointFixtures.ToProtocol83(combined.Bytes);
            var oldTimed=new ReplayReplicaState();oldTimed.RestoreCustomCheckpoint(new(retainedTimed83));
            Check(retainedTimed83[4]==15&&retainedTimed83[6]==83&&retainedTimed83[7]==63&&oldTimed.TimedShots.Enabled&&oldTimed.CaptureCheckpoint().Bytes.SequenceEqual(combined.Bytes),"published timed custom83/15 feature63 retains original timed/custom tails");
            var restored=new ReplayReplicaState();restored.RestoreCustomCheckpoint(new(checkpoint.Bytes));Check(restored.CaptureCheckpoint().Bytes.SequenceEqual(checkpoint.Bytes)&&restored.CustomWeapons!.Context.Digest==f.Context.Digest,"cold custom17 restore round trips all native/custom bytes");
            var stateFact=ReplayCustomWeaponFact.State(f.Context,inspection.CustomWeapons!.Sequence+1,1004,f.Slots);inspection.Accept(stateFact.CopyBytes(),20);restored.Accept(stateFact.CopyBytes(),20);
            var flights=new CustomReplicaFlight[256];var restoredFlights=new CustomReplicaFlight[256];Check(inspection.CustomWeapons.CopyFlights(flights)==1&&restored.CustomWeapons!.CopyFlights(restoredFlights)==1&&flights[0]==restoredFlights[0],"restored baseline original anchor matches suffix flight exactly");
            var launch=ReplayCustomWeaponFact.Launch(f.Context,inspection.CustomWeapons.Sequence+1,f.Launch() with{Origin=f.Flight().SpawnPosition});inspection.Accept(launch.CopyBytes(),20);restored.Accept(launch.CopyBytes(),20);Check(inspection.CaptureCheckpoint().Bytes.SequenceEqual(restored.CaptureCheckpoint().Bytes),"consistent accepted launch suffix preserves original baseline anchor");
            var contact=ReplayCustomWeaponFact.Contact(f.Context,inspection.CustomWeapons.Sequence+1,f.World,f.Contact());inspection.Accept(contact.CopyBytes(),21);restored.Accept(contact.CopyBytes(),21);Check(inspection.CaptureCheckpoint().Bytes.SequenceEqual(restored.CaptureCheckpoint().Bytes)&&restored.CustomWeapons!.TryContact(f.Launch().Identity,out var outcome,out uint frame)&&outcome==f.Contact()&&frame==21,"contact suffix equality includes typed terminal and first recording clock");
            inspection.Accept(contact.CopyBytes(),21);restored.Accept(contact.CopyBytes(),21);Check(inspection.CaptureCheckpoint().Bytes.SequenceEqual(restored.CaptureCheckpoint().Bytes)&&inspection.IgnoredPackets==restored.IgnoredPackets,"durable duplicate retry keeps ignored counters and sequence stable");
            Check(inspection.Rng1==rng1&&inspection.Rng2==rng2&&restored.Rng1==rng1&&restored.Rng2==rng2,"custom inspection never advances native RNG");
            for(int i=0;i<8;i++){inspection.CustomWeapons.TrySlot(i,out var x);restored.CustomWeapons!.TrySlot(i,out var y);Check(x==y,"suffix slot parity "+i);}
            var nativeOwner=new ReplayReplicaState();byte[] nativeSentinel=nativeOwner.CaptureCheckpoint().Bytes.ToArray();Reject(()=>nativeOwner.RestoreCheckpoint(checkpoint),"ordinary native RestoreCheckpoint refuses version17");Reject(()=>ReplayReplicaState.InspectCheckpointLayout(checkpoint),"ordinary native layout inspection refuses version17");Check(nativeOwner.CaptureCheckpoint().Bytes.SequenceEqual(nativeSentinel),"native refusal leaves existing owner unchanged");Reject(()=>restored.RestoreCustomCheckpoint(new(nativeBefore)),"custom restore refuses ordinary native16 checkpoint");
            var health=restored.CaptureCheckpoint();void Bad(byte[] bytes,string label){Reject(()=>restored.RestoreCustomCheckpoint(new(bytes)),label);Check(restored.CaptureCheckpoint().Bytes.SequenceEqual(health.Bytes),label+" atomic owner preservation");}
            byte[] wire=checkpoint.Bytes.ToArray();for(int n=0;n<wire.Length;n++)Bad(wire[..n],"custom17 truncation "+n);Bad(wire.Concat(new byte[]{0}).ToArray(),"custom17 trailing bytes");
            foreach(int offset in new[]{0,4,6,7}){var bad=(byte[])wire.Clone();bad[offset]=255;Bad(bad,"custom17 unknown signature/version/protocol/features "+offset);}
            var noCustom=(byte[])wire.Clone();noCustom[7]&=unchecked((byte)~32);Bad(noCustom,"version17 without exact custom feature refused");
            int descriptorAt=-1;for(int i=8;i+4<=wire.Length;i++)if(wire.AsSpan(i,4).SequenceEqual("CWRD"u8)){descriptorAt=i;break;}Check(descriptorAt>=4,"custom appendix stores length-delimited durable descriptor");var excessive=(byte[])wire.Clone();BinaryPrimitives.WriteInt32LittleEndian(excessive.AsSpan(descriptorAt-4),20481);Bad(excessive,"descriptor declared over-budget bound");
            byte[] exactConfiguration=ConfigurationBytes(configuration)[1..];int configurationAt=-1;for(int at=8;at+exactConfiguration.Length<=wire.Length;at++)if(wire.AsSpan(at,exactConfiguration.Length).SequenceEqual(exactConfiguration)){configurationAt=at;break;}Check(configurationAt>=0,"custom17 includes actual accepted session configuration");
            var wrongCheckpointConfiguration=(byte[])wire.Clone();var wrongGeneration=configuration;wrongGeneration.StartGeneration++;wrongGeneration.Write(wrongCheckpointConfiguration.AsSpan(configurationAt,SessionStatePacket.Size));Bad(wrongCheckpointConfiguration,"custom17 session StartGeneration contradiction");
            var wrongRoomCheckpoint=(byte[])wire.Clone();var wrongRoom=configuration;wrongRoom.Match=wrongRoom.Match with{RoomKey="MP2 HARVESTER"};wrongRoom.Write(wrongRoomCheckpoint.AsSpan(configurationAt,SessionStatePacket.Size));Bad(wrongRoomCheckpoint,"custom17 session room contradiction");
            var changedMetadata=(byte[])metadata.Clone();changedMetadata[^1]^=1;Check(!ReplayCustomWeaponDescriptor.TryRead(changedMetadata,out _),"canonical mechanics metadata mutation contradicts exact digest");
            foreach(string mode in new[]{"map","match","epoch","flags"}){var owner=new ReplayReplicaState();owner.EnableCustomInspection(descriptor);var wrong=match;if(mode=="map")wrong.RoomKey="MP2 HARVESTER";if(mode=="match")wrong.MatchId++;if(mode=="epoch")wrong.AuthorityEpoch++;if(mode=="flags")wrong.RuleBits=0;Reject(()=>owner.Accept(MatchBytes(wrong),1),"inspection match scope mismatch "+mode);Check(!owner.Match.HasValue,"mismatched match not published "+mode);}
            foreach(string mode in new[]{"room","maphash","start","oneflag"})
            {
                var owner=new ReplayReplicaState();owner.EnableCustomInspection(descriptor);owner.Accept(MatchBytes(match),1);var wrong=configuration;
                if(mode=="room")wrong.Match=wrong.Match with{RoomKey="MP2 HARVESTER"};
                if(mode=="maphash")wrong.Match=wrong.Match with{MapIdentity=new(Guid.Parse("12f512f5-1122-3344-5566-778899aabbcc"),MapHash256.Parse(new string('a',64)),MapHash256.Parse(new string('b',64)),NetworkMapFlags.Custom)};
                if(mode=="start")wrong.StartGeneration++;
                if(mode=="oneflag")wrong.Match=wrong.Match with{PackagedWeaponAvailable=false};
                Reject(()=>owner.Accept(ConfigurationBytes(wrong),2),"inspection rejects configuration scope mismatch "+mode);Check(!owner.Configuration.HasValue,"invalid configuration never publishes "+mode);
            }
            var ordinary=new ReplayReplicaState();Reject(()=>ordinary.Accept(MatchBytes(match),1),"native replica cannot adopt custom match without explicit metadata capability");var late=new ReplayReplicaState();var nativeMatch=match;nativeMatch.RuleBits=0;late.Accept(MatchBytes(nativeMatch),1);Reject(()=>late.EnableCustomInspection(descriptor),"inspection capability cannot attach after native facts");
            restored.Rewind();Check(restored.CustomWeaponDescriptor==null&&restored.CustomWeapons==null,"Rewind clears inspection scope");var reset=new ReplayReplicaState();reset.EnableCustomInspection(descriptor);reset.Reset();Check(reset.CustomWeaponDescriptor==null&&reset.CustomWeapons==null,"Reset clears inspection scope");Check(new ReplayReplicaState().CaptureCheckpoint().Bytes.SequenceEqual(nativeBefore),"native default16 exact bytes unchanged after custom inspection");
            File.WriteAllBytes(Path.Combine(root,"retained-native83-14.bin"),retainedNative83);File.WriteAllBytes(Path.Combine(root,"retained-custom83-15.bin"),retainedCustom83);File.WriteAllBytes(Path.Combine(root,"retained-timed-custom83-15.bin"),retainedTimed83);
            File.WriteAllBytes(Path.Combine(root,"metadata.cwrd"),metadata);File.WriteAllBytes(Path.Combine(root,"custom17.bin"),wire);File.WriteAllText(Path.Combine(root,"results.json"),JsonSerializer.Serialize(new{checks,nativeSha256=Convert.ToHexStringLower(SHA256.HashData(nativeBefore)),scope="Explicit metadata-only replica checkpoint17 and retained native83/14/custom83/15 cold restore. Native archive/world restore APIs remain closed; no resource provenance, Scene, render or playback enable claim."}));Console.WriteLine($"PASS {checks} custom replica checkpoint17 inspection checks");return 0;
        }
        catch(Exception error){Console.Error.WriteLine("CUSTOM REPLICA CHECKPOINT FAIL "+error);return 1;}
    }
}
