using System.Text.Json;
using System.Buffers.Binary;
using MphRead;
using MphRead.Mods.CustomWeapons;
using MphRead.Mods.CustomWeapons.Packages;
using MphRead.Mods.Network;
using MphRead.Mods.Replay;
using MphRead.NetTest;
using OpenTK.Mathematics;

// Detached content-free world envelope; no actors, Scene, rendering or native simulation.
internal static class CustomReplayFileChecks
{
    internal static int Run(string? output=null)
    {
        int checks=0;string root=Path.GetFullPath(output??Path.Combine(Path.GetTempPath(),"cw12f-file-"+Guid.NewGuid().ToString("N")));
        void Check(bool ok,string label){if(!ok)throw new InvalidOperationException(label);checks++;}
        void Reject(Action action,string label){try{action();}catch(Exception e)when(e is InvalidDataException or IOException or ArgumentException or InvalidOperationException or OperationCanceledException){Check(true,label);return;}throw new InvalidOperationException("Expected refusal: "+label);}
        try
        {
            Directory.CreateDirectory(root);var foreground=GameState.Current;var random=Rng.Current;using var f=new CustomReplayStateChecks.Fixture();var descriptor=ReplayCustomWeaponDescriptor.ForInspection(f.Context);
            var owner=new ReplayReplicaState();owner.EnableCustomInspection(descriptor);var match=new MatchStatePacket{MatchId=7,AuthorityEpoch=f.World.Start.AuthorityEpoch,RoomKey="MP3 PROVING GROUND",NextRoomKey="",Mode=(byte)GameMode.Battle,RuleBits=(uint)(MatchModifierFlags.RiftlineM7Available|MatchModifierFlags.PackagedWeaponAvailable)};byte[] packet=new byte[1+MatchStatePacket.Size];packet[0]=(byte)PacketType.MatchState;match.Write(packet.AsSpan(1));owner.Accept(packet,1);owner.Accept(ReplayCustomWeaponFact.Bootstrap(f.Context).CopyBytes(),10);owner.Accept(ReplayCustomWeaponFact.Baseline(f.Context,2,new(7,f.World.Start.AuthorityEpoch,f.Context.Bootstrap.Packages.MechanicsDigest,1001,8,0),f.Slots,Array.Empty<CustomReplicaFlight>()).CopyBytes(),11);for(int i=0;i<f.Context.Layout.Count;i++)owner.Accept(f.Pickup(owner.CustomWeapons!.Sequence+1,i).CopyBytes(),12);
            var initial=owner.CaptureCheckpoint();owner.Accept(ReplayCustomWeaponFact.Clock(f.Context,owner.CustomWeapons!.Sequence+1,1004).CopyBytes(),20);var baseline=owner.CaptureCheckpoint();
            byte[] Envelope(ReplayReplicaCheckpoint current,uint frame,string? contract=null,bool retained83=false,bool initial83Only=false,bool retained84=false,bool retained85=false)
            {
                ReplayReplicaCheckpoint Protocol85(ReplayReplicaCheckpoint source)
                {
                    byte[] bytes = source.Bytes.ToArray();
                    if (bytes[6] != 86 || bytes[4] != 17 || bytes[7] != 119) throw new InvalidOperationException("Expected current86/custom17 fixture.");
                    bytes[6] = 85; return new(bytes);
                }
                var construction=retained83||initial83Only?new ReplayReplicaCheckpoint(Protocol83CheckpointFixtures.ToProtocol83(initial.Bytes)):retained84?new ReplayReplicaCheckpoint(Protocol84CheckpointFixtures.ToProtocol84(initial.Bytes)):retained85?Protocol85(initial):initial;
                var decoder=retained83?new ReplayReplicaCheckpoint(Protocol83CheckpointFixtures.ToProtocol83(current.Bytes)):retained84?new ReplayReplicaCheckpoint(Protocol84CheckpointFixtures.ToProtocol84(current.Bytes)):retained85?Protocol85(current):current;
                using var stream=new MemoryStream();using var writer=new BinaryWriter(stream);void Component(byte[] b){writer.Write(b.Length);writer.Write(b);}
                writer.Write(0x43575050u);writer.Write((ushort)12);writer.Write((byte)15);writer.Write(contract??ReplayWorldCheckpoint.ComputeContract(stable:true));writer.Write("MP3 PROVING GROUND");writer.Write((int)GameMode.Battle);writer.Write(17ul);writer.Write(frame);writer.Write(123u);writer.Write(456u);Component(construction.Bytes.ToArray());Component(decoder.Bytes.ToArray());writer.Write(0);for(int i=0;i<5;i++)Component(Array.Empty<byte>());Component(new byte[8*57]);Component(new byte[4]);Component(new byte[184]);Component(ReplayWorldCheckpoint.CustomWorldAppendix(decoder));writer.Write(false);return stream.ToArray();
            }
            using var world=ReplayWorldCheckpoint.FromCustomBytes(Envelope(baseline,20));var metadata=world.ClipMetadata(0,Array.Empty<ReplayPlayerInfo>());string path=Path.Combine(root,"valid.ppdemo");var suffix=ReplayCustomWeaponFact.Clock(f.Context,owner.CustomWeapons!.Sequence+1,1010);owner.Accept(suffix.CopyBytes(),21);var later=owner.CaptureCheckpoint();
            Check(metadata.ProtocolVersion==86&&baseline.Bytes[4]==17&&baseline.Bytes[6]==86&&baseline.Bytes[7]==119,"current file metadata85 uses exact checkpoint17/phase64 decoder without changing world12 appendix15");

            var marker=new ReplayMarker(ReplayMarkerKind.Kill,0,1,1,new ReplayKillIdentity(7,f.World.Start.AuthorityEpoch,1004,1,0,1,1,1,1,1));byte[] markerBytes=ReplayTimelineArchive.EncodeMarker(1004,marker);var markerRecord=ReplayTimelineArchive.DecodeMarker(21,markerBytes);Check(markerRecord.Marker==marker&&markerRecord.ServerTick==1004,"actual semantic marker codec preserves full kill identity and recorded tick");markerRecord.Release();
            using(var writer=new ReplayWriterV3(path,metadata)){writer.WriteRecord(1,markerBytes);writer.WriteRecord(1,suffix.CopyBytes());writer.WriteCheckpoint(1,Envelope(later,21));writer.WriteRecord(3,new byte[]{255});}
            const string legacyBuild="0.1.0+legacy-copy-contract";byte[] legacyBytes=Envelope(baseline,20,ReplayWorldCheckpoint.ComputeContract(stable:false,producerBuild:legacyBuild));using var legacyWorld=ReplayWorldCheckpoint.FromCustomBytes(legacyBytes,legacyBuild);string legacyPath=Path.Combine(root,"legacy-producer.ppdemo");var legacyMetadata=new ReplayMetadata{FormatVersion=7,ProtocolVersion=86,BuildVersion=legacyBuild,BuildId=legacyBuild,RoomKey=metadata.RoomKey,Mode=metadata.Mode,MapHash=metadata.MapHash,OriginRecordingFrame=20,CustomWeapons=descriptor,Bootstrap=metadata.Bootstrap,WorldCheckpoint=legacyBytes};using(var writer=new ReplayWriterV3(legacyPath,legacyMetadata)){writer.WriteRecord(1,suffix.CopyBytes());}Reject(()=>{using var rejected=ReplayWorldCheckpoint.FromCustomBytes(legacyBytes);},"legacy producer contract genuinely requires supplied build identity");
            string storePath=Path.Combine(root,"historical");var store=new WeaponPackageStore(storePath);store.Install(f.Package.CopyArchive());store.Uninstall(f.Package.PackageHash);using(var legacyPrepared=PreparedCustomReplayArchive.Prepare(legacyPath,store)){using var legacyCopy=legacyPrepared.CopyWorld();Check(legacyCopy.Bytes.SequenceEqual(legacyBytes),"CopyWorld preserves accepted producer-specific legacy contract and exact bytes");}
            int callingThread=Environment.CurrentManagedThreadId;var result=Task.Run(()=>{int worker=Environment.CurrentManagedThreadId;return (worker,prepared:PreparedCustomReplayArchive.Prepare(path,store));}).GetAwaiter().GetResult();using(var prepared=result.prepared)
            {
                Check(result.worker!=callingThread&&prepared.TargetFrame==20&&prepared.Path==Path.GetFullPath(path),"actual worker prepares exact historical archive without installed marker");using var copy=prepared.CopyWorld();Check(copy.Bytes.SequenceEqual(world.Bytes),"prepared baseline owns exact detached world12 bytes");
                Reject(()=>{using var exclusive=new FileStream(path,FileMode.Open,FileAccess.ReadWrite,FileShare.None);},"prepared source remains read-only shared and denies writer");
                using var reader=prepared.TakeReader();Reject(()=>prepared.TakeReader(),"reader transfers exactly once");prepared.Dispose();Check(reader.ReadNext()!=null,"transferred reader survives parent disposal");Reject(()=>prepared.CopyWorld(),"disposed preparation cannot expose baseline");
            }
            Check(store.Prune()==1,"worker disposal releases historical package lease");store.Install(f.Package.CopyArchive());
            ReplayMetadata RecordedWorld(byte protocol,byte[] bytes)
            {
                using var recorded=ReplayWorldCheckpoint.FromCustomBytes(bytes);
                return new ReplayMetadata{FormatVersion=7,ProtocolVersion=protocol,RoomKey=metadata.RoomKey,Mode=metadata.Mode,
                    MapHash=metadata.MapHash,OriginRecordingFrame=20,CustomWeapons=descriptor,WorldCheckpoint=bytes,
                    Bootstrap=new(){Packets=new[]{ReplayTimelineArchive.Construction(recorded.DecoderState())}}};
            }
            byte[] retainedWorld=Envelope(baseline,20,retained83:true),retainedLater=Envelope(later,21,retained83:true);
            Check(metadata.IndexedFireContinuation==false&&!world.IndexedFirePresentation,"fresh custom metadata preserves actual false world presentation capability");
            var falseWorldTrueMetadata=new ReplayMetadata{FormatVersion=7,ProtocolVersion=86,RoomKey=metadata.RoomKey,Mode=metadata.Mode,MapHash=metadata.MapHash,OriginRecordingFrame=20,CustomWeapons=descriptor,Bootstrap=metadata.Bootstrap,WorldCheckpoint=world.Bytes.ToArray(),IndexedFireContinuation=true};Reject(()=>ReplayFormatV3.EncodeMetadata(falseWorldTrueMetadata),"explicit true metadata cannot relabel actual false world baseline");
            // Deliberately forged negative envelope only; positive indexed worlds are captured from a real prepared cursor.
            byte[] forgedTrueDurable=Envelope(later,21);forgedTrueDurable[^1]=1;using(var forged=ReplayWorldCheckpoint.FromCustomBytes(forgedTrueDurable))Check(forged.IndexedFirePresentation,"negative mixed-contract durable is structurally valid before file admission");string mixed=Path.Combine(root,"historical-null-mixed-contract.ppdemo");var historicalNull=RecordedWorld(86,world.Bytes.ToArray());Check(historicalNull.IndexedFireContinuation==null,"historical nullable metadata negative fixture remains unspecified");using(var writer=new ReplayWriterV3(mixed,historicalNull)){writer.WriteRecord(1,suffix.CopyBytes());writer.WriteCheckpoint(1,forgedTrueDurable);writer.WriteRecord(3,new byte[]{255});}Reject(()=>PreparedCustomReplayArchive.Prepare(mixed,store,21).Dispose(),"historical nullable metadata cannot switch false baseline to true durable contract");
            var retainedMetadata=RecordedWorld(83,retainedWorld);string retainedPath=Path.Combine(root,"retained83.ppdemo");
            using(var writer=new ReplayWriterV3(retainedPath,retainedMetadata)){writer.WriteRecord(1,markerBytes);writer.WriteRecord(1,suffix.CopyBytes());writer.WriteCheckpoint(1,retainedLater);writer.WriteRecord(3,new byte[]{255});}
            using(var prepared=PreparedCustomReplayArchive.Prepare(retainedPath,store))
            {
                using var selected=prepared.CopyWorld();var oldLayout=ReplayReplicaState.InspectCustomCheckpointLayout(selected.DecoderState());
                Check(selected.Bytes.SequenceEqual(retainedWorld)&&oldLayout.Version==15&&oldLayout.Protocol==83&&oldLayout.IntentBytes==1298,
                    "retained83 file prepares authentic custom15/1298 world12 bytes rather than relabelling current17");
                var host=new PassiveReplaySessionHost();using var session=new ReplayPlaybackSession(host);
                session.JoinCustomInspection(selected.DecoderState(),prepared.Resources,17);session.RestoreCustomInspection(selected.DecoderState());session.AttachCustomArchive(prepared);
                Check(session.SourceProtocolVersion==83&&session.AdvanceCustomArchiveInspection()&&host.State.CaptureCheckpoint().Bytes.SequenceEqual(later.Bytes),
                    "real83 file suffix reconstructs exact current custom facts without losing recorded protocol");
            }
            using(var prepared=PreparedCustomReplayArchive.Prepare(retainedPath,store,21))
            {using var selected=prepared.CopyWorld();Check(selected.Frame==21&&selected.Bytes.SequenceEqual(retainedLater),"retained83 indexed seek selects exact old15 durable checkpoint bytes");}
            byte[] retained84World=Envelope(baseline,20,retained84:true);
            var retained84Metadata=RecordedWorld(84,retained84World);
            var retained85Metadata=RecordedWorld(85,Envelope(baseline,20,retained85:true));
            foreach(byte protocol in new byte[]{83,84,85,86})
            foreach(byte wrongProtocol in new byte[]{83,84,85,86})
            {
                if(protocol==wrongProtocol)continue;
                var correct=protocol==83?retainedMetadata:protocol==84?retained84Metadata:protocol==85?retained85Metadata:metadata;
                byte[] wrong=Envelope(later,21,retained83:wrongProtocol==83,retained84:wrongProtocol==84,retained85:wrongProtocol==85);
                using(var standalone=ReplayWorldCheckpoint.FromCustomBytes(wrong))
                    Check(ReplayReplicaState.InspectCustomCheckpointLayout(standalone.DecoderState()).Protocol==wrongProtocol,"cross-protocol durable fixture is independently valid in its exact source layout");
                string mismatch=Path.Combine(root,"cross-protocol-"+protocol+"-"+wrongProtocol+".ppdemo");
                using var writer=new ReplayWriterV3(mismatch,correct);
                Reject(()=>writer.WriteCheckpoint(1,wrong),"archive"+protocol+" refuses durable source"+wrongProtocol+" before publication");
            }
            using(var hybrid=ReplayWorldCheckpoint.FromCustomBytes(Envelope(later,21,initial83Only:true)))
                Reject(()=>hybrid.ValidateCustomArchive(metadata),"current85 file refuses old83 construction even when current durable decoder is85");

            // The exact newly admitted custom file path must preserve custom
            // Match/Session rules while normalizing only historical pose bytes.
            byte[] NativeSnapshot(byte phase,uint frame,byte wall=0)
            {
                byte[] bytes=new byte[1+SnapshotHeader.Size+192+NetMatchTimeSync.Size+NetHealthSync.HeaderSize];bytes[0]=(byte)PacketType.Snapshot;
                new SnapshotHeader{MatchId=7,AuthorityEpoch=f.World.Start.AuthorityEpoch,Frame=frame,PlayerCount=1}.Write(bytes.AsSpan(1));
                new PlayerState{SlotIndex=0,SlotGeneration=2,LifeId=3,Flags=PlayerState.FlagSpawned,Health=80,Facing=Vector3.UnitZ,
                    Crouched=true,Tucked=true,MantleVisualPhase=phase,WallVisualCue=wall}.Write(bytes.AsSpan(1+SnapshotHeader.Size));
                BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(bytes.Length-NetHealthSync.HeaderSize),7);return bytes;
            }
            byte[] NativeIntent(byte phase,uint frame,bool old83,byte wall=0)
            {
                byte[] bytes=new byte[2+(old83?1298:1299)];bytes[0]=(byte)PacketType.SlotIntent;
                new IntentPacket{MatchId=7,AuthorityEpoch=f.World.Start.AuthorityEpoch,SlotGeneration=2,LifeId=3,Frame=frame,
                    Aim=Vector3.UnitZ,Target=NetTargetIdentity.None,GrappleRequestNonce=0xbeef,MantleVisualPhase=phase,WallVisualCue=wall,
                    Buttons=IntentButtons.InPlayState|IntentButtons.CrouchedState|IntentButtons.TuckedState}.Write(bytes.AsSpan(2));return bytes;
            }
            var roster=RosterPacket.Create();roster.MatchId=7;roster.AuthorityEpoch=f.World.Start.AuthorityEpoch;roster.Revision=1;
            roster.Count=1;roster.Slots[0]=0;roster.Generations[0]=2;roster.Names[0]="file-phase-owner";
            byte[] rosterBytes=new byte[1+RosterPacket.Size];rosterBytes[0]=(byte)PacketType.Roster;roster.Write(rosterBytes.AsSpan(1));
            var configuration=new SessionStatePacket{MatchId=7,AuthorityEpoch=f.World.Start.AuthorityEpoch,StartGeneration=f.World.Start.StartGeneration,
                Revision=1,MaxPlayers=8,OwnerSlot=0,Phase=SessionPhase.InMatch,WorldProfile=MphRead.Mods.Multiplayer.MatchWorldProfile.Resolve(2),
                Match=new MatchDefinition{RoomKey="MP3 PROVING GROUND",Mode=GameMode.Battle,RiftlineM7Available=true,PackagedWeaponAvailable=true}};
            byte[] configurationBytes=new byte[1+SessionStatePacket.Size];configurationBytes[0]=(byte)PacketType.SessionState;configuration.Write(configurationBytes.AsSpan(1));
            foreach(byte protocol in new byte[]{83,84,85,86})
            {
                byte expected=protocol==83?(byte)0:(byte)9;
                byte expectedWall=protocol>=85?(byte)6:(byte)0;
                byte[] observed=NativeSnapshot(expected,100,expectedWall),held=NativeIntent(expected,100,protocol==83,expectedWall);
                if(protocol==84){observed[1+SnapshotHeader.Size+173]|=0xf0;held[^1]|=0xf0;}
                if(protocol==83)observed[1+SnapshotHeader.Size+114]|=0xf0; // legal old reserved nibble cannot invent a pose
                byte[] clear=NativeSnapshot(0,101),released=NativeIntent(0,101,protocol==83);
                string phasePath=Path.Combine(root,"phase-suffix-"+protocol+".ppdemo");
                using(var writer=new ReplayWriterV3(phasePath,protocol==83?retainedMetadata:protocol==84?retained84Metadata:protocol==85?retained85Metadata:metadata))
                {writer.WriteRecord(1,packet);writer.WriteRecord(1,configurationBytes);writer.WriteRecord(1,rosterBytes);writer.WriteRecord(1,observed);writer.WriteRecord(1,held);writer.WriteRecord(2,clear);writer.WriteRecord(2,released);writer.WriteRecord(3,new byte[]{255});}
                using var prepared=PreparedCustomReplayArchive.Prepare(phasePath,store);using var source=prepared.CopyWorld();
                var host=new PassiveReplaySessionHost();using var session=new ReplayPlaybackSession(host);
                session.JoinCustomInspection(source.DecoderState(),prepared.Resources,17);session.RestoreCustomInspection(source.DecoderState());session.AttachCustomArchive(prepared);
                Check(session.AdvanceCustomArchiveInspection()&&host.State.Configuration is {} acceptedConfiguration&&acceptedConfiguration.Match.PackagedWeaponAvailable
                    &&host.State.Match is {} acceptedMatch&&((MatchModifierFlags)acceptedMatch.RuleBits).HasFlag(MatchModifierFlags.PackagedWeaponAvailable),
                    "explicit custom"+protocol+" Match/Session suffix preserves admitted package rules instead of using native refusal adapter");
                Check(host.State.TryGetPlayer(0,out var visual)&&visual.MantleVisualPhase==expected&&visual.WallVisualCue==expectedWall&&visual.Crouched&&visual.Tucked
                    &&host.State.TryGetIntent(0,out var intent)&&intent.MantleVisualPhase==expected&&intent.WallVisualCue==expectedWall&&intent.GrappleRequestNonce==0xbeef,
                    "file"+protocol+" suffix preserves retained84/current85 phase9 or clears reserved83 phase with real1298 intent expansion");
                Check(session.AdvanceCustomArchiveInspection()&&host.State.TryGetPlayer(0,out visual)&&visual.MantleVisualPhase==0&&visual.WallVisualCue==0
                    &&host.State.TryGetIntent(0,out intent)&&intent.MantleVisualPhase==0&&intent.WallVisualCue==0,"file"+protocol+" later explicitNone clears both accepted state and intent phase");
            }
            using(var prepared=PreparedCustomReplayArchive.Prepare(path,store,21)){using var selected=prepared.CopyWorld();Check(selected.Frame==21&&selected.DecoderState().Bytes.SequenceEqual(later.Bytes),"seek preparation selects valid nearest indexed checkpoint floor");}
            Reject(()=>PreparedCustomReplayArchive.Prepare(path,store,19).Dispose(),"target before origin refused");Reject(()=>PreparedCustomReplayArchive.Prepare(path,store,24).Dispose(),"target beyond archive duration refused");
            using(var cancel=new CancellationTokenSource()){cancel.Cancel();Reject(()=>PreparedCustomReplayArchive.Prepare(path,store,cancellation:cancel.Token).Dispose(),"cancelled preparation refuses before publication");}
            string absent=Path.Combine(root,"absent");Reject(()=>PreparedCustomReplayArchive.Prepare(path,new(absent)).Dispose(),"missing exact historical package refuses");Check(!Directory.Exists(absent),"missing resource refusal creates no store");
            using(var prepared=PreparedCustomReplayArchive.Prepare(path,store))
            {
                var host=new PassiveReplaySessionHost();using var session=new ReplayPlaybackSession(host);Reject(()=>session.AttachCustomArchive(prepared),"inactive session cannot take archive");session.JoinCustomInspection(baseline,prepared.Resources,17);session.RestoreCustomInspection(baseline);session.AttachCustomArchive(prepared);prepared.Dispose();Check(session.IsCustomArchiveInspection&&session.CurrentFrame==0&&session.RecordingFrame==20,"attached reader clocks remain relative visible and absolute recorded");Reject(()=>session.PumpFrame(),"ordinary native PumpFrame refuses custom file scope");byte[] beforeSeek=host.State.CaptureCheckpoint().Bytes.ToArray();Reject(()=>session.Reposition(0,0),"generic native reposition refuses custom file scope");Check(session.IsActive&&session.CurrentFrame==0&&session.RecordingFrame==20&&host.State.CaptureCheckpoint().Bytes.SequenceEqual(beforeSeek),"generic seek refusal preserves healthy custom owner and clocks");Check(session.AdvanceCustomArchiveInspection()&&session.CurrentFrame==1&&session.RecordingFrame==21&&host.State.CaptureCheckpoint().Bytes.SequenceEqual(later.Bytes),"explicit file suffix reconstructs exact canonical current owner");Check(session.AdvanceCustomArchiveInspection()&&session.AdvanceCustomArchiveInspection()&&session.AtEnd&&session.CurrentFrame==3&&session.RecordingFrame==23,"quiet file frames advance to exact EOF without custom simulation");Check(!session.AdvanceCustomArchiveInspection()&&host.State.CustomWeapons!.World==1010,"EOF remains inert and preserves last accepted authority clock");session.Stop();
            }
            string frameZero=Path.Combine(root,"frame-zero.ppdemo");using(var writer=new ReplayWriterV3(frameZero,metadata)){writer.WriteRecord(0,suffix.CopyBytes());}Reject(()=>PreparedCustomReplayArchive.Prepare(frameZero,store).Dispose(),"ambiguous packet suffix at completed baseline frame zero refuses");
            string hiddenZero=Path.Combine(root,"hidden-frame-zero.ppdemo");using(var writer=new ReplayWriterV3(hiddenZero,metadata)){writer.WriteRecord(0,new byte[]{255});writer.WriteRecord(0,suffix.CopyBytes());}Reject(()=>PreparedCustomReplayArchive.Prepare(hiddenZero,store).Dispose(),"frame-zero marker cannot hide following ambiguous custom packet");
            string earlyEnd=Path.Combine(root,"early-end.ppdemo");using(var writer=new ReplayWriterV3(earlyEnd,metadata)){writer.WriteRecord(1,new byte[]{255});writer.WriteRecord(2,suffix.CopyBytes());}
            using(var prepared=PreparedCustomReplayArchive.Prepare(earlyEnd,store)){var host=new PassiveReplaySessionHost();using var session=new ReplayPlaybackSession(host);session.JoinCustomInspection(baseline,prepared.Resources,17);session.RestoreCustomInspection(baseline);session.AttachCustomArchive(prepared);Reject(()=>session.AdvanceCustomArchiveInspection(),"end marker before declared footer duration fails closed");Check(!session.IsActive&&session.LastResult==ReplayOpenResult.Corrupt,"premature end marker retires active file owner");}
            string noWorld=Path.Combine(root,"no-world.ppdemo");var detached=new ReplayMetadata{FormatVersion=7,ProtocolVersion=86,RoomKey=metadata.RoomKey,Mode=metadata.Mode,MapHash=metadata.MapHash,OriginRecordingFrame=20,CustomWeapons=descriptor,Bootstrap=metadata.Bootstrap};using(var writer=new ReplayWriterV3(noWorld,detached)){writer.WriteRecord(1,suffix.CopyBytes());}Reject(()=>PreparedCustomReplayArchive.Prepare(noWorld,store).Dispose(),"metadata-only archive cannot masquerade as recorded world baseline");
            string native=Path.Combine(root,"native.ppdemo");using(var writer=new ReplayWriterV3(native,new ReplayMetadata{FormatVersion=4,ProtocolVersion=83,RoomKey="MP3 PROVING GROUND",Mode=GameMode.Battle})){writer.WriteRecord(1,new byte[]{255});}Reject(()=>PreparedCustomReplayArchive.Prepare(native,store).Dispose(),"native archive cannot enter explicit custom preparation");
            string lead=Path.Combine(root,"lead-in.ppdemo");var leadMetadata=new ReplayMetadata{FormatVersion=7,ProtocolVersion=86,RoomKey=metadata.RoomKey,Mode=metadata.Mode,MapHash=metadata.MapHash,OriginRecordingFrame=20,LeadInFrames=1,CustomWeapons=descriptor,Bootstrap=metadata.Bootstrap,WorldCheckpoint=metadata.WorldCheckpoint};using(var writer=new ReplayWriterV3(lead,leadMetadata)){writer.WriteRecord(1,suffix.CopyBytes());}using (var preparedLead = PreparedCustomReplayArchive.Prepare(lead, store))
            {
                Check(preparedLead.TargetFrame == 21, "lead-in preparation targets the declared visible origin");
                using var leadWorld = preparedLead.CopyWorld();
                var leadHost = new PassiveReplaySessionHost(); using var leadSession = new ReplayPlaybackSession(leadHost);
                leadSession.JoinCustomInspection(leadWorld.DecoderState(), preparedLead.Resources, 17);
                leadSession.RestoreCustomInspection(leadWorld.DecoderState()); leadSession.AttachCustomArchive(preparedLead);
                Check(leadSession.AdvanceCustomArchiveInspection() && leadSession.CurrentFrame == 0
                    && leadHost.State.CaptureCheckpoint().Bytes.SequenceEqual(later.Bytes),
                    "lead-in reconstructs the actual suffix at visible frame zero");
            }
            Reject(()=>PreparedCustomReplayArchive.Prepare(lead,store,20).Dispose(), "lead-in refuses targets before the visible origin");
            string conflict=Path.Combine(root,"conflicting-suffix.ppdemo");using(var writer=new ReplayWriterV3(conflict,metadata)){writer.WriteRecord(1,suffix.CopyBytes());writer.WriteRecord(2,ReplayCustomWeaponFact.Clock(f.Context,owner.CustomWeapons!.Sequence,1011).CopyBytes());}
            using(var prepared=PreparedCustomReplayArchive.Prepare(conflict,store)){var host=new PassiveReplaySessionHost();using var session=new ReplayPlaybackSession(host);session.JoinCustomInspection(baseline,prepared.Resources,17);session.RestoreCustomInspection(baseline);session.AttachCustomArchive(prepared);Check(session.AdvanceCustomArchiveInspection(),"structurally valid suffix prefix applies before later semantic conflict");Reject(()=>session.AdvanceCustomArchiveInspection(),"same-sequence contradictory suffix fails closed");Check(!session.IsActive&&session.LastResult==ReplayOpenResult.Corrupt&&host.State.CustomWeapons==null,"corrupt file suffix retires private inspection owner");}
            string budget=Path.Combine(root,"budget.ppdemo");using(var writer=new ReplayWriterV3(budget,metadata)){writer.WriteRecord(PreparedCustomReplayArchive.MaximumReconstructionFrames+1,new byte[]{255});}Reject(()=>PreparedCustomReplayArchive.Prepare(budget,store,20+PreparedCustomReplayArchive.MaximumReconstructionFrames+1).Dispose(),"distant target without nearer checkpoint refuses bounded reconstruction");
            Check(ReferenceEquals(foreground,GameState.Current)&&ReferenceEquals(random,Rng.Current)&&!NetSession.Active,"worker/file inspection leaves live game RNG and network owners untouched");Check(store.Uninstall(f.Package.PackageHash)&&store.Prune()==1,"all file owners release package leases after transfer and session disposal");
            File.WriteAllText(Path.Combine(root,"results.json"),JsonSerializer.Serialize(new{checks,scope="Pure worker preparation and detached content-free world12 file inspection; no loaded Scene, rendering, simulation or native gameplay claim."}));Console.WriteLine($"PASS {checks} custom replay file preparation/inspection checks");return 0;
        }
        catch(Exception error){Console.Error.WriteLine("CUSTOM REPLAY FILE FAIL "+error);return 1;}
    }
}
