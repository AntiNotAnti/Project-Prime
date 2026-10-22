using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using MphRead.Mods;
using MphRead.Mods.Replay;
using OpenTK.Mathematics;

/// <summary>Headless file/JSON contract qualification; no worker, replay provider, window or GL context.</summary>
internal static class DetachedExportTicketAcceptance
{
    internal static int Run()
    {
        int checks=0;
        void Check(bool condition,string message)
        {if(!condition)throw new InvalidOperationException(message);checks++;Console.WriteLine("DETACHED TICKET CPU PASS "+message);}
        static bool Reject<T>(Action action) where T:Exception
        {try{action();return false;}catch(T){return true;}}

        string root=Path.GetFullPath(Path.Combine(Path.GetTempPath(),"prime-detached-ticket-"+Guid.NewGuid().ToString("N")));
        string job=Path.Combine(root,"private-job");Directory.CreateDirectory(job);
        try
        {
            string replay=Path.Combine(job,"replay.ppdemo"),path=Path.Combine(job,"ticket.json"),paths=Path.Combine(job,"paths.txt");
            byte[] bytes=Enumerable.Range(0,131073).Select(i=>(byte)(i%251)).ToArray();File.WriteAllBytes(replay,bytes);
            string inputHash=Convert.ToHexString(SHA256.HashData(bytes));
            byte[] pathsBytes="SYNTHETIC PRIVATE PATHS // CPU ONLY\n"u8.ToArray();File.WriteAllBytes(paths,pathsBytes);
            string pathsHash=Convert.ToHexString(SHA256.HashData(pathsBytes));
            Check(ReplayDetachedExportFiles.Hash(replay)==inputHash,"regular private synthetic replay input has the actual SHA256 of its bytes");
            string firstHash=new('A',64),lastHash=new('b',64);
            Check(ReplayDetachedExportFiles.PlaybackHash("SOURCE:PLAYBACK-SHA:"+firstHash+":RANGE:0:20")==firstHash,
                "review witness extracts the complete playback hash before range suffix");
            Check(ReplayDetachedExportFiles.PlaybackHash("SOURCE:PLAYBACK-SHA:"+firstHash+":OTHER:PLAYBACK-SHA:"+lastHash+":RANGE:12:83")==lastHash,
                "last playback marker selects the actual playback identity and preserves hexadecimal case");
            foreach(string invalid in new[]{"no playback marker",":PLAYBACK-SHA:"+new string('0',63),
                ":PLAYBACK-SHA:Z"+new string('0',63),":PLAYBACK-SHA:"+firstHash+":PLAYBACK-SHA:short"})
                Check(Reject<InvalidDataException>(()=>ReplayDetachedExportFiles.PlaybackHash(invalid)),
                    "missing, short, nonhex or invalid final playback witness is rejected");

            var keys=ImmutableArray.Create(
                new ReplayCameraKeyframe(12,new Vector3(1.5f,-2.25f,3.125f),Quaternion.FromEulerAngles(.3f,-.4f,.7f).Normalized(),
                    MathHelper.DegreesToRadians(77.5f),2,.125f,ReplayCameraInterpolation.Bezier,ReplayCameraEase.Out,
                    new Vector3(-.5f,.75f,1.25f),new Vector3(1.75f,-2.5f,.375f),.11f,-.22f,.33f,-.44f),
                new ReplayCameraKeyframe(82,new Vector3(-4.5f,6.25f,-8.125f),Quaternion.FromEulerAngles(-.2f,.6f,-.5f).Normalized(),
                    MathHelper.DegreesToRadians(91.25f),-1,-.375f,ReplayCameraInterpolation.Spline,ReplayCameraEase.InOut,
                    new Vector3(.25f,1.5f,-.75f),new Vector3(-1.25f,.5f,2.75f),-.12f,.24f,-.36f,.48f));
            var presentation=new ReplayExportPresentation(ReplayCameraMode.Free,ReplayPresentationProfile.Presentation,
                77.5f,-12.25f,8.5f,3.75f,2,false,true,true,true,ReplayCameraInterpolation.Bezier,ReplayCameraEase.Out,keys);
            var manifest=new ReplayVideoExportManifest(2,replay,12,83,640,360,60,true,false,true,
                Path.Combine(job,"frame-%06d.png"),Path.Combine(job,"SYNTHETIC.mp4"),"SYNTHETIC CPU ONLY // NEVER EXECUTE",
                new[]{new ReplayVideoSegment(12,40,"FIRST",ReplaySegmentCamera.Chase),new ReplayVideoSegment(40,83,"SECOND",ReplaySegmentCamera.Orbit)},
                "SYNTHETIC REVIEWED PRESET",GameHud:false,ReplayOverlay:true,EncoderExecutable:"SYNTHETIC-NOT-AN-ENCODER",EncoderCodec:"SYNTHETIC");
            string runtimeHash=ReplayDetachedExportFiles.Hash(typeof(ModEntry).Assembly.Location);
            var ticket=new ReplayDetachedExportTicket(1,Guid.NewGuid(),job,manifest,presentation,runtimeHash,
                "SYNTHETIC-MPH-KEY","SYNTHETIC-FH-KEY",Path.Combine(root,"maps"),Path.Combine(root,"user-maps"),
                0x0123456789ABCDEFUL,2,new Dictionary<string,string>(StringComparer.Ordinal){{replay,inputHash},{paths,pathsHash}});
            ReplayDetachedExportFiles.Write(path,ticket);
            var restored=ReplayDetachedExportTicket.Read(path);
            Check(Path.IsPathFullyQualified(restored.WorkDirectory)&&restored.WorkDirectory==job&&restored.Id==ticket.Id
                &&restored.RuntimeHash==runtimeHash&&restored.GeometryHash==ticket.GeometryHash&&restored.Subject==ticket.Subject
                &&restored.MphKey==ticket.MphKey&&restored.FhKey==ticket.FhKey&&restored.MapDirectory==ticket.MapDirectory&&restored.UserMapDirectory==ticket.UserMapDirectory
                &&restored.InputHashes.Count==2&&restored.InputHashes[replay]==inputHash&&restored.InputHashes[paths]==pathsHash,
                "ticket Read verifies real loaded runtime hash, full private work directory and exact replay/paths input hashes");
            Check(restored.Presentation.Keys.SequenceEqual(keys)&&(restored.Presentation with{Keys=keys})==presentation,
                "real presentation JSON retains every nonzero vector, quaternion, tangent, key FOV, roll and interpolation value");
            Check(restored.Manifest.Segments!.SequenceEqual(manifest.Segments!)
                &&(restored.Manifest with{Segments=manifest.Segments})==manifest,
                "real export manifest roundtrip preserves source range, segments, output and encoder/HUD options");
            using(var json=JsonDocument.Parse(File.ReadAllBytes(path)))
            {
                var key=json.RootElement.GetProperty("Presentation").GetProperty("Keys")[0];
                Check(key.GetProperty("Position").GetArrayLength()==3&&key.GetProperty("Rotation").GetArrayLength()==4
                    &&key.GetProperty("IncomingTangent").GetArrayLength()==3&&key.GetProperty("OutgoingTangent").GetArrayLength()==3,
                    "OpenTK converters write bounded component arrays without recursive swizzle properties");
            }
            Check(Reject<JsonException>(()=>JsonSerializer.Deserialize<Vector3>("[1,2]",ReplayDetachedExportFiles.Json))
                &&Reject<JsonException>(()=>JsonSerializer.Deserialize<Quaternion>("[1,2,3,4,5]",ReplayDetachedExportFiles.Json)),
                "vector and quaternion converters reject malformed component counts");

            bytes[bytes.Length/2]^=1;File.WriteAllBytes(replay,bytes);
            Check(Reject<IOException>(()=>ReplayDetachedExportTicket.Read(path)),"mutated private replay bytes cannot reuse the reviewed ticket");
            bytes[bytes.Length/2]^=1;File.WriteAllBytes(replay,bytes);
            ReplayDetachedExportFiles.Write(path,ticket with{InputHashes=new Dictionary<string,string>{{replay,inputHash}}});
            Check(Reject<InvalidDataException>(()=>ReplayDetachedExportTicket.Read(path)),"missing private paths hash rejects the ticket");
            ReplayDetachedExportFiles.Write(path,ticket);File.Delete(paths);
            Check(Reject<IOException>(()=>ReplayDetachedExportTicket.Read(path)),"missing reviewed private paths file rejects the ticket");
            File.WriteAllBytes(paths,pathsBytes);pathsBytes[0]^=1;File.WriteAllBytes(paths,pathsBytes);
            Check(Reject<IOException>(()=>ReplayDetachedExportTicket.Read(path)),"mutated private paths bytes cannot reuse the reviewed ticket");
            pathsBytes[0]^=1;File.WriteAllBytes(paths,pathsBytes);
            ReplayDetachedExportFiles.Write(path,ticket with{RuntimeHash=new string('0',64)});
            Check(Reject<InvalidDataException>(()=>ReplayDetachedExportTicket.Read(path)),"wrong loaded runtime hash rejects the ticket");
            ReplayDetachedExportFiles.Write(path,ticket with{Id=Guid.Empty});
            Check(Reject<InvalidDataException>(()=>ReplayDetachedExportTicket.Read(path)),"empty job identity rejects the ticket");
            string outside=Path.Combine(root,"outside-job.ppdemo");File.WriteAllBytes(outside,bytes);
            ReplayDetachedExportFiles.Write(path,ticket with{Manifest=manifest with{Replay=outside},
                InputHashes=new Dictionary<string,string>{{outside,inputHash},{paths,pathsHash}}});
            Check(Reject<IOException>(()=>ReplayDetachedExportTicket.Read(path))&&ReplayDetachedExportFiles.Hash(outside)==inputHash,
                "matching-hash input outside the exact private job root is rejected without changing it");
            ReplayDetachedExportFiles.Write(path,ticket);
            Check(ReplayDetachedExportTicket.Read(path).Id==ticket.Id,"rejected variants do not poison a fresh valid ticket");

            var rendering=new ReplayDetachedExportStatus(ticket.Id,Environment.ProcessId,ReplayExportState.Rendering,"SYNTHETIC CPU STATUS",.375f,9,null,null,
                new DateTime(2026,10,19,12,34,56,DateTimeKind.Utc));
            ReplayDetachedExportFiles.WriteStatus(ticket.StatusPath,rendering);
            Check(JsonSerializer.Deserialize<ReplayDetachedExportStatus>(File.ReadAllBytes(ticket.StatusPath),ReplayDetachedExportFiles.Json)==rendering,
                "atomic initial status roundtrip preserves exact job ID, progress, state, frames and UTC receipt");
            var completed=rendering with{State=ReplayExportState.Completed,Message="SYNTHETIC COMPLETE",Progress=1,Frames=71,Output=manifest.SuggestedOutput};
            ReplayDetachedExportFiles.WriteStatus(ticket.StatusPath,completed);
            Check(JsonSerializer.Deserialize<ReplayDetachedExportStatus>(File.ReadAllBytes(ticket.StatusPath),ReplayDetachedExportFiles.Json)==completed
                &&!Directory.EnumerateFiles(job,"*.tmp").Any(),
                "atomic status replacement publishes exact completion and leaves no staging files");
            Check(ticket.CancelPath==Path.Combine(job,"cancel")&&ticket.LogPath==Path.Combine(job,"worker.log"),
                "ticket-owned status, cancel and log paths remain in the exact private job directory");
            Console.WriteLine($"PASS {checks} detached ticket CPU checks; explicit synthetic data and actual file/JSON/runtime helpers only; no worker, provider UI, GL or encoding claim");
            return 0;
        }
        finally{Directory.Delete(root,true);}
    }
}
