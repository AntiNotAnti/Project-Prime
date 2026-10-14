using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Collections.Generic;
using MphRead;
using OpenTK.Mathematics;
using MphRead.Mods.Network;
internal static class Entry {
 static int Main(string[] args) {
  var asm=typeof(ReplayPlaybackSession).Assembly;
  Console.WriteLine($"IDENTITY ClientPath={asm.Location} SHA256={Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(asm.Location))).ToLowerInvariant()} MVID={asm.ManifestModule.ModuleVersionId}");
  Console.WriteLine($"IDENTITY EntrySHA256={Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(Entry).Assembly.Location))).ToLowerInvariant()} MVID={typeof(Entry).Assembly.ManifestModule.ModuleVersionId}");
  Console.WriteLine("ENTRY_SCOPE Final fce adapter compatibility; thin friend entry separate from full nettest fixture.");
  Console.WriteLine($"SESSION_BEFORE active={NetSession.Active} authority={NetSession.IsAuthority}");
  Paths.UpdatePaths(); Paths.ChooseMphPath();
  if(args.Length>1 && args[1]=="--scene") {
   MphRead.Mods.Headless.Enter();
   long frames=0, sceneEvents=0, certs=0, scopes=0; PassiveReplayScene? scene=null;
   try { scene=new PassiveReplayScene(args[0],new Vector2i(64,64));
    scene.Session.FactRead+=(f,p)=>{sceneEvents++;if(p[0]==252)certs++;if(p[0]==251)scopes++;};
    while(frames<1000000 && scene.Step()){frames++;if(frames%1000==0)Console.WriteLine($"SCENE_PROGRESS frames={frames} current={scene.Session.CurrentFrame} certs={certs} pendingBombs={scene.State.PendingBombPacketCount}");}
    Console.WriteLine($"SCENE_FINAL frames={frames} sceneEvents={sceneEvents} certs={certs} scopes={scopes} current={scene.Session.CurrentFrame} result={scene.Session.LastResult} error={scene.Session.LastError} warning={scene.Session.LastWarning} pendingBombs={scene.State.PendingBombPacketCount} activeNetSession={NetSession.Active} authority={NetSession.IsAuthority}");
    return scene.Session.LastResult==ReplayOpenResult.Success?0:1;
   }catch(Exception ex){Console.WriteLine($"SCENE_EXCEPTION frames={frames} current={scene?.Session.CurrentFrame} result={scene?.Session.LastResult} error={scene?.Session.LastError}: {ex}");return 1;}
   finally {scene?.Dispose();}
  }
  var records=new List<DemoRecord>(); using(var reader=DemoReader.Open(args[0],out var open)) { Console.WriteLine($"OPEN={open} format={reader?.FormatVersion} protocol={reader?.ProtocolVersion}"); if(reader==null)return 2; while(reader.ReadNext() is {} r)records.Add(r); Console.WriteLine($"INDEX records={records.Count} result={reader.LastResult} origin={reader.Metadata?.OriginRecordingFrame}"); }
  var host=new PassiveReplaySessionHost(); using var session=new ReplayPlaybackSession(host);
  bool joined=session.Join(args[0]); Console.WriteLine($"JOIN={joined} result={session.LastResult} error={session.LastError} warning={session.LastWarning}"); if(!joined)return 3;
  var pendingField=typeof(ReplayPlaybackSession).GetField("_pending",BindingFlags.NonPublic|BindingFlags.Instance)!;
  DemoRecord? initial=(DemoRecord?)pendingField.GetValue(session); int next=initial is {} first ? records.FindIndex(r=>r.Frame==first.Frame&&r.Data.AsSpan().SequenceEqual(first.Data)) : 0;
  long events=0, snapshots=0; session.FactRead+=(f,p)=>{events++; if(p[0]==(byte)PacketType.Snapshot)snapshots++; next++;};
  long pumps=0; try {while(!session.AtEnd && pumps<1000000) {session.PumpFrame();pumps++;}} catch(Exception ex) {Console.WriteLine("EXCEPTION="+ex);}
  Console.WriteLine($"FINAL pumps={pumps} events={events} snapshots={snapshots} current={session.CurrentFrame} result={session.LastResult} error={session.LastError} warning={session.LastWarning} nextIndex={next}");
  if(session.LastResult!=ReplayOpenResult.Success && next<records.Count) {var bad=records[next]; Console.WriteLine($"FAILED ordinal={next} frame={bad.Frame} tag={bad.Data[0]} length={bad.Data.Length} hex={Convert.ToHexString(bad.Data)}"); try {host.State.Accept(ReplayIdentityCompatibility.Convert(bad.Data,session.Metadata!.ProtocolVersion),checked(bad.Frame+session.Metadata.OriginRecordingFrame));}catch(Exception ex){Console.WriteLine("DIRECT_EXCEPTION="+ex);} }
  return session.LastResult==ReplayOpenResult.Success?0:1;
 }
}
