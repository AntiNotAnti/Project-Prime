using System.Text.Json;
using MphRead;
using MphRead.Formats;
using MphRead.Mods;
using MphRead.Mods.CustomWeapons.Packages;
using MphRead.Mods.Network;
using MphRead.Mods.Render;
using MphRead.Mods.Replay;
using MphRead.Sound;
using OpenTK.Mathematics;
using OpenTK.Windowing.Desktop;

internal static class CustomReplayIndexedFireChecks
{
    private sealed record Fact(ReplayFactKind Kind,uint Frame,uint Tick,byte[] Bytes);
    internal static int Run(string input,string output)
    {
        string cwd=Directory.GetCurrentDirectory();NativeWindow? window=null;DesktopGraphicsSession? graphics=null;
        try
        {
            string loaded=typeof(Scene).Assembly.Location;Console.WriteLine("Loaded ProjectPrime: "+loaded+" SHA256="+Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(loaded))));
            input=Path.GetFullPath(input);output=Path.GetFullPath(output);Directory.CreateDirectory(output);string seed=Path.Combine(output,"unindexed-seed");
            if(CustomReplayContactBundleChecks.Run(input,seed)!=0)throw new InvalidDataException("Actual unindexed source pass failed.");
            string data=File.ReadAllText(Path.Combine(seed,"replay-fixture-data.txt"));Directory.SetCurrentDirectory(data);Paths.UpdatePaths();Paths.ChooseMphPath();MapAudit.ForceEveryone=true;
            byte[] packageBytes=File.ReadAllBytes(Path.Combine(seed,"variant.ppweapon"));var package=WeaponPackage.Parse(packageBytes);using var rowsJson=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(seed,"file-expected-worlds.json")));var rows=rowsJson.RootElement.EnumerateArray().Select(row=>(Name:row.GetProperty("name").GetString()!,Frame:row.GetProperty("frame").GetUInt32(),Filename:row.GetProperty("filename").GetString()!)).ToArray();if(rows.Length is <1 or >8)throw new InvalidDataException("Bounded genuine targets required.");var first=rows.Single(row=>row.Name=="baseline");var end=rows.Single(row=>row.Name=="end");
            var facts=new List<Fact>();long payload=0;using(var stream=File.OpenRead(Path.Combine(input,"accepted-full-stream.bin")))using(var reader=new BinaryReader(stream)){if(stream.Length>65*1024*1024)throw new InvalidDataException("Raw stream bound.");while(stream.Position<stream.Length){if(facts.Count>=32768||stream.Length-stream.Position<13)throw new InvalidDataException("Raw record bound.");var kind=(ReplayFactKind)reader.ReadByte();uint frame=reader.ReadUInt32(),tick=reader.ReadUInt32();int length=reader.ReadInt32();if(length<=0||length>16384||(payload+=length)>64*1024*1024||length>stream.Length-stream.Position)throw new InvalidDataException("Raw payload bound.");facts.Add(new(kind,frame,tick,reader.ReadBytes(length)));}}
            int checks=0;void Check(bool ok,string label){if(!ok)throw new InvalidDataException(label);checks++;}
            int nativeFireCarriers=0,nativeFireEvents=0,nativeShotFacts=0;foreach(var fact in facts){if(fact.Bytes[0]==(byte)PacketType.SlotIntent&&fact.Bytes.Length>=2+IntentPacket.Size){var intent=IntentPacket.Read(fact.Bytes.AsSpan(2));if(intent.HasFireEvents&&NetFireEvents.Validate(intent)){nativeFireCarriers++;nativeFireEvents+=intent.FireEventCount;}}if(fact.Bytes[0]==(byte)PacketType.ReplayShotFact&&ReplayShotFactPacket.TryRead(fact.Bytes.AsSpan(1),out _))nativeShotFacts++;}Check(nativeFireEvents>0&&nativeShotFacts>0,"actual accepted native FireEvent carriers and reliable ShotFacts required");
            foreach(var row in rows){using var falseWorld=ReplayWorldCheckpoint.FromCustomBytes(File.ReadAllBytes(Path.Combine(seed,row.Filename)));Check(!falseWorld.IndexedFirePresentation,"original actual source world remains unindexed "+row.Name);}string seedArchive=Path.Combine(seed,"actual-full-custom"+DemoFile.Extension);using(var reader=DemoReader.Open(seedArchive,out var opened)){Check(reader!=null&&opened==ReplayOpenResult.Success&&reader.Metadata!.IndexedFireContinuation==false,"seed archive preserves explicit false presentation capability");}
            var settings=DesktopGlContext.Settings(background:true);settings.ClientSize=new(960,720);settings.StartVisible=true;settings.Title="Actual indexed native/custom replay source";window=new NativeWindow(settings);graphics=new DesktopGraphicsSession(window);
            var store=new WeaponPackageStore(Path.Combine(output,"indexed-store"));store.Install(packageBytes);using var prepared=Task.Run(()=>PreparedCustomReplayArchive.Prepare(seedArchive,store,first.Frame)).GetAwaiter().GetResult();using var baseline=prepared.CopyWorld();using var source=PassiveReplayScene.RestoreCustomInspection(baseline,prepared.Resources,new Vector2i(640,480));source.Scene.ReplayPoses?.Dispose();source.Scene.ReplayPoses=new ReplayPoseStream(source,prepared,true);Check(source.Scene.ReplayPoses.Prepare()&&source.Scene.ReplayPoses.IndexedFirePresentation,"explicit admitted custom cursor prepares true before actual source capture");
            var expectedRows=new List<object>();void Save(string name){using var world=ReplayWorldCheckpoint.CaptureCustom(source);Check(world.IndexedFirePresentation,"newly captured source world owns true indexed presentation "+name);Check(world.ConstructionState().Bytes.SequenceEqual(baseline.ConstructionState().Bytes),"actual source retains original native/custom construction "+name);string filename="file-expected-"+name+"-world12.bin";File.WriteAllBytes(Path.Combine(output,filename),world.Bytes.ToArray());expectedRows.Add(new{name,frame=source.Session.RecordingFrame,filename});}
            Save("baseline");using(var world=ReplayWorldCheckpoint.CaptureCustom(source))using(var writer=new ReplayWriterV3(Path.Combine(output,"actual-full-custom"+DemoFile.Extension),world.ClipMetadata(0,Array.Empty<ReplayPlayerInfo>())))
            {
                for(ulong next=(ulong)first.Frame+1;next<=end.Frame;next++)
                {
                    uint frame=(uint)next;var records=new List<ReplayTimelineRecord>();try{foreach(var fact in facts.Where(fact=>fact.Frame==frame)){ReplayTimelineRecord record;if(fact.Kind==ReplayFactKind.Event){record=ReplayTimelineArchive.DecodeMarker(frame,fact.Bytes);Check(record.ServerTick==fact.Tick,"actual marker tick preserved");}else record=new(frame,fact.Tick,fact.Kind,fact.Bytes);records.Add(record);ReplayTimelineArchive.Write(writer,record,first.Frame);}source.StepCustomInspection(frame,records);Check(source.Scene.ReplayPoses!.Prepare(),"actual indexed cursor remains valid at accepted frame "+frame);}finally{foreach(var record in records)record.Release();}
                    foreach(var row in rows.Where(row=>row.Frame==frame&&row.Name!="baseline"))Save(row.Name);if(rows.Any(row=>row.Frame==frame)){using var checkpoint=ReplayWorldCheckpoint.CaptureCustom(source);writer.WriteCheckpoint(frame-first.Frame,checkpoint.Bytes);}
                }
                writer.WriteRecord(end.Frame-first.Frame,new byte[]{255});
            }
            File.WriteAllBytes(Path.Combine(output,"variant.ppweapon"),packageBytes);File.WriteAllText(Path.Combine(output,"replay-fixture-data.txt"),data);File.WriteAllText(Path.Combine(output,"file-expected-worlds.json"),JsonSerializer.Serialize(expectedRows));File.Copy(Path.Combine(seed,"contact-target-timing.json"),Path.Combine(output,"contact-target-timing.json"));using(var reader=DemoReader.Open(Path.Combine(output,"actual-full-custom"+DemoFile.Extension),out var opened))Check(reader!=null&&opened==ReplayOpenResult.Success&&reader.Metadata!.IndexedFireContinuation==true,"new actual archive retains explicit true capability");string malformed=Path.Combine(output,"malformed-native-lookahead"+DemoFile.Extension);using(var valid=DemoReader.Open(Path.Combine(output,"actual-full-custom"+DemoFile.Extension),out var validResult)){Check(valid!=null&&validResult==ReplayOpenResult.Success,"actual indexed baseline available for isolated malformed-lookahead negative");using var writer=new ReplayWriterV3(malformed,valid!.Metadata!);writer.WriteRecord(1,new byte[]{(byte)PacketType.ReplayShotFact,0});writer.WriteRecord(3,new byte[]{255});}
            using(var invalidPrepared=Task.Run(()=>PreparedCustomReplayArchive.Prepare(malformed,store,first.Frame)).GetAwaiter().GetResult())using(var invalidWorld=invalidPrepared.CopyWorld())using(var isolated=PassiveReplayScene.RestoreCustomInspection(invalidWorld,invalidPrepared.Resources,new Vector2i(640,480))){byte[] before=isolated.State.CaptureCheckpoint().Bytes.ToArray();isolated.Scene.ReplayPoses?.Dispose();isolated.Scene.ReplayPoses=new ReplayPoseStream(isolated,invalidPrepared,true);Check(!isolated.Scene.ReplayPoses.Prepare()&&isolated.Scene.ReplayPoses.LastError!=null,"malformed native ShotFact lookahead fails closed before a Scene step");Check(isolated.State.CaptureCheckpoint().Bytes.SequenceEqual(before),"malformed lookahead never mutates accepted canonical owner");}
                        // Synthetic budget-only adversaries derived from one genuine accepted fact; never stepped or claimed as combat.
            Check(NetFireEvents.RetentionFrames+6<900&&ReplayPoseStream.ShotFactLookaheadFrames>=900,"synthetic budget probes lie beyond pose lookahead and within independent shot lookahead");
            var actualShotBytes=facts.First(fact=>fact.Bytes[0]==(byte)PacketType.ReplayShotFact&&ReplayShotFactPacket.TryRead(fact.Bytes.AsSpan(1),out _)).Bytes;ReplayShotFactPacket.TryRead(actualShotBytes.AsSpan(1),out var actualShot);
            foreach(bool overflow in new[]{false,true})
            {
                string budgetPath=Path.Combine(output,overflow?"synthetic-shot-budget-overflow.ppdemo":"synthetic-shot-budget-duplicate.ppdemo");using(var valid=DemoReader.Open(Path.Combine(output,"actual-full-custom"+DemoFile.Extension),out _))using(var writer=new ReplayWriterV3(budgetPath,valid!.Metadata!))
                {
                    byte[] bytes=new byte[1+ReplayShotFactPacket.Size];bytes[0]=(byte)PacketType.ReplayShotFact;
                    for(int identity=1;identity<=ReplayPoseStream.MaximumCustomShotFacts;identity++){var synthetic=actualShot with{DamageEventId=checked((ushort)identity)};ReplayShotFactPacket.Write(synthetic,bytes.AsSpan(1));writer.WriteRecord(900,bytes);}
                    var final=actualShot with{DamageEventId=overflow?checked((ushort)(ReplayPoseStream.MaximumCustomShotFacts+1)):(ushort)1};ReplayShotFactPacket.Write(final,bytes.AsSpan(1));writer.WriteRecord(900,bytes);writer.WriteRecord(901,new byte[]{255});
                }
                using var budgetPrepared=Task.Run(()=>PreparedCustomReplayArchive.Prepare(budgetPath,store,first.Frame)).GetAwaiter().GetResult();using var budgetWorld=budgetPrepared.CopyWorld();using var budgetScene=PassiveReplayScene.RestoreCustomInspection(budgetWorld,budgetPrepared.Resources,new Vector2i(640,480));byte[] before=budgetScene.State.CaptureCheckpoint().Bytes.ToArray();budgetScene.Scene.ReplayPoses?.Dispose();budgetScene.Scene.ReplayPoses=new ReplayPoseStream(budgetScene,budgetPrepared,true);bool ready=budgetScene.Scene.ReplayPoses.Prepare();Check(ready!=overflow&&(overflow?budgetScene.Scene.ReplayPoses.LastError!=null:budgetScene.Scene.ReplayPoses.LastError==null),overflow?"synthetic8193rd distinct shot identity refuses bounded lookahead":"syntheticduplicate at8192 identity capacity remains valid");Check(budgetScene.State.CaptureCheckpoint().Bytes.SequenceEqual(before),"synthetic lookahead budget probe leaves canonical owner untouched");
            }
            Check(CustomReplayFileReopenChecks.Validate(output,Path.Combine(output,"indexed-file-reopen"),window)>0,"actual indexed file reverse fresh Scene restore and genuine suffix/draw parity");
            File.WriteAllText(Path.Combine(output,"indexed-fire-results.json"),JsonSerializer.Serialize(new{checks,nativeFireCarriers,nativeFireEvents,nativeShotFacts,targets=rows.Select(row=>new{row.Name,row.Frame}),scope="Two actual source passes: preserved unindexed producer then newly captured true-indexed private source using genuine native FireEvents/ShotFacts and custom accepted facts. Strict canonical cold restore/suffix/draw checks; no fabricated native packet or byte-flipped contract."},new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine($"PASS {checks} actual indexed native/custom replay source checks");return 0;
        }
        catch(Exception error){Console.Error.WriteLine("INDEXED CUSTOM REPLAY FAIL "+error);return 1;}
        finally{AudioLifetime.Shutdown();graphics?.Dispose();window?.Dispose();Directory.SetCurrentDirectory(cwd);}
    }
}
