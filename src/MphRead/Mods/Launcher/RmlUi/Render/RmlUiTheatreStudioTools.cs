#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using System.Collections.Immutable;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Entities;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.RmlUi.Core;
using MphRead.Mods.Network;
using MphRead.Mods.MapGen;
using MphRead.Mods.Replay;

namespace MphRead.Mods.Launcher.RmlUi.Render;
internal static partial class RmlUiOpenGlSmoke
{
    private sealed partial class CurrentReplayEditor
    {
        private ReplayStudioAudioPreview? _studioPreview;
        public void PreviewStudioAudio(TheatreEditorContext context,string wavePath)
        {
            Validate(context,true);StopStudioAudioPreview();RequireRegular(wavePath);
            _studioPreview=new ReplayStudioAudioPreview(StudioPcmAudio.ReadWave(wavePath));
        }
        public void StopStudioAudioPreview(){_studioPreview?.Dispose();_studioPreview=null;}
        private sealed record BuildEvidence(int Version,ushort Schema,string Build,uint Frame,string SourceHash,string Gameplay,string Presentation,string FullGraph,string Camera,string Projectiles,string Animations);
        public void ValidateStudioTool(TheatreEditorContext context)=>Validate(context,false);
        public Task<TheatreStudioToolResult> RunStudioTool(TheatreEditorContext context,TheatreStudioToolOptions options,CancellationToken cancellation)
        {
            Validate(context,false);
            options=options with{AudioCues=Array.AsReadOnly(ReplayStudioAudioMix.FreezeCues(options.AudioCues))};
            if(context.Persisted is not Files files)throw new InvalidOperationException("Refresh the editor before running a Studio tool.");
            if(!Enum.IsDefined(options.Tool)||options.Start>=options.End||options.End>context.Duration)throw new ArgumentException("Review a nonempty replay range.");
            foreach(float gain in new[]{options.GameGain,options.CombatGain,options.ReplayGain,options.MusicGain})
                if(!float.IsFinite(gain)||gain<0||gain>4)throw new ArgumentException("Audio bus gains must be between 0 and 4.");
            if(options.Tool==TheatreStudioTool.CompareWorld)return BeginWorldComparison(context,options.InputPath,cancellation);
            string source=context.PlaybackPath;
            var mapIdentity=DemoPlayback.Metadata?.CustomMapIdentity;
            ulong mapHash=DemoPlayback.Metadata?.MapHash??0;
            var players=(DemoPlayback.Metadata?.Players??Array.Empty<ReplayPlayerInfo>()).ToImmutableArray();
            // Snapshot only bounded, already-reviewed sidecars. Never copy an arbitrary live file tree.
            var camera=ReplayReviewedMetadata.Read(context.LogicalPath+".camera",8192);
            var annotations=files.Annotations;var reel=files.Reel;
            byte[]? portableCamera=null;string? portableMap=null;
            if(options.Tool==TheatreStudioTool.PortableBundle)
            {
                var track=new ReplayCameraTrack();if(!track.Load(context.LogicalPath))throw new InvalidDataException(track.LastError);portableCamera=track.ExportState();
                if(ReplayReviewedMetadata.Read(camera.Path,8192).Revision!=camera.Revision)throw new IOException("Camera changed while preparing bundle.");
                if(mapIdentity is{} required&&CustomRooms.Installed.TryGet(required.MapId,out var installed)&&installed.Identity.Matches(required))portableMap=installed.PackagePath;
            }
            BuildEvidence? evidence=null;
            if(options.Tool is TheatreStudioTool.SaveBuildEvidence or TheatreStudioTool.CompareBuildEvidence)
            {
                Validate(context,true);
                var scene=DemoPlayback.PresentationScene??throw new InvalidOperationException("Wait for a settled replay picture.");
                string Hash(object value)=>Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));
                var matrix=scene.ViewMatrix;
                var entities=new List<EntityBase>();foreach(var entity in scene.Entities)entities.Add(entity);
                var projectiles=entities.OfType<BeamProjectileEntity>().Where(p=>p.Active).Select(p=>new{p.ModShotId,p.ModLaunchFrame,Owner=p.Owner is PlayerEntity owner?owner.SlotIndex:-1,Weapon=(int)p.BeamKind,
                    Position=new[]{p.Position.X,p.Position.Y,p.Position.Z},Velocity=new[]{p.Velocity.X,p.Velocity.Y,p.Velocity.Z},p.Age,p.Lifespan,Flags=(uint)p.Flags}).ToArray();
                var animations=entities.SelectMany((entity,index)=>entity.ReplayModels.Select((model,modelIndex)=>new{Entity=index,Type=entity.Type.ToString(),Model=modelIndex,
                    A0=model.AnimInfo.Index[0],A1=model.AnimInfo.Index[1],F0=model.AnimInfo.Frame[0],F1=model.AnimInfo.Frame[1],Flags0=(int)model.AnimInfo.Flags[0],Flags1=(int)model.AnimInfo.Flags[1]})).ToArray();
                using var world=ReplayWorldCheckpoint.Capture(DemoPlayback.ReviewedStudioWorld());
                evidence=new(2,ReplayStateHash.Schema,ReplayStateHash.BuildId,context.Frame,"",ReplayStateHash.Compute(scene,context.Frame),scene.ReplayPresentationHash(context.Frame),world.GraphFingerprint(),
                    Hash(new[]{matrix.M11,matrix.M12,matrix.M13,matrix.M14,matrix.M21,matrix.M22,matrix.M23,matrix.M24,matrix.M31,matrix.M32,matrix.M33,matrix.M34,matrix.M41,matrix.M42,matrix.M43,matrix.M44}),Hash(projectiles),Hash(animations));
            }
            var frozenEvidence=evidence;
            string exportRoot=Path.Combine(DemoLibrary.Directory,"exports");
            if(Directory.Exists(exportRoot)&&new DirectoryInfo(exportRoot).LinkTarget!=null)throw new IOException("Use a regular export directory.");
            string directory=Path.Combine(exportRoot,"studio_"+Guid.NewGuid().ToString("N"));
            return Task.Run(async()=>
            {
                cancellation.ThrowIfCancellationRequested();Directory.CreateDirectory(directory);
                try
                {
                    string path;string summary;
                    if(frozenEvidence!=null)
                    {await using var original=File.OpenRead(source);frozenEvidence=frozenEvidence with{SourceHash=Convert.ToHexString(await SHA256.HashDataAsync(original,cancellation))};}
                    switch(options.Tool)
                    {
                        case TheatreStudioTool.AudioMix:
                            path=Path.Combine(directory,"audio.wav");ReplayStudioAudioMix.Write(path,options,files.Events,players,cancellation);
                            summary=$"Offline stereo PCM16 mix: {options.Start}–{options.End} at 48 kHz. Event cues use canonical game assets; continuous in-world sound is not reconstructed.";break;
                        case TheatreStudioTool.MuxVideo:
                            string video=Path.GetFullPath(options.InputPath);RequireRegular(video);string videoFacts=FileFacts(video);
                            string audio=Path.Combine(directory,"audio.wav");ReplayStudioAudioMix.Write(audio,options with{InputPath=""},files.Events,players,cancellation);
                            path=Path.Combine(directory,"replay-with-audio.mp4");
                            var startInfo=new ProcessStartInfo("ffmpeg"){UseShellExecute=false,RedirectStandardError=true,RedirectStandardOutput=true,CreateNoWindow=true};
                            foreach(string argument in new[]{"-v","error","-nostdin","-i",video,"-i",audio,"-map","0:v:0","-map","1:a:0","-c:v","copy","-c:a","aac","-shortest",path})startInfo.ArgumentList.Add(argument);
                            using(var process=Process.Start(startInfo)??throw new IOException("FFmpeg did not start."))
                            {
                                var stderr=process.StandardError.ReadToEndAsync(cancellation);var stdout=process.StandardOutput.ReadToEndAsync(cancellation);
                                try{await process.WaitForExitAsync(cancellation);}
                                catch{try{process.Kill(entireProcessTree:true);}catch(InvalidOperationException){}throw;}
                                string details=await stderr;await stdout;
                                if(process.ExitCode!=0)throw new IOException("FFmpeg audio mux failed: "+details[^Math.Min(1000,details.Length)..]);
                            }
                            if(videoFacts!=FileFacts(video))throw new IOException("The input video changed during audio mux.");
                            summary="Saved a new video with the reviewed offline audio mix. Video frames were copied without re-encoding; output ends at the shorter stream.";break;
                        case TheatreStudioTool.CompareRecording:
                            string other=Path.GetFullPath(options.InputPath);RequireRegular(other);
                            var before=FileFacts(other);
                            var a=ReplayStudioComparison.Read(source,context.Frame,cancellation);var b=ReplayStudioComparison.Read(other,context.Frame,cancellation);
                            if(before!=FileFacts(other))throw new IOException("Comparison recording changed while reading.");
                            var comparison=ReplayStudioComparison.Compare(a,b);path=Path.Combine(directory,"recording-comparison.json");
                            await Write(path,new{Scope="Recorded decoder state and player poses; not a simulated full-world comparison",A=a,B=b,Comparison=comparison},cancellation);
                            summary=$"FRAME {context.Frame} // DECODER EQUAL {comparison.DecoderEqual}\n"
                                +string.Join("\n",comparison.Players.Select(p=>p.SameLife
                                    ?$"P{p.Slot+1}: distance {p.Distance:0.######}, health {p.HealthA}/{p.HealthB}, weapon {p.WeaponA}/{p.WeaponB}"
                                    :$"P{p.Slot+1}: recorded life differs or is unavailable; position comparison withheld"));break;
                        case TheatreStudioTool.SaveBuildEvidence:
                            path=Path.Combine(directory,"build-evidence.json");await Write(path,frozenEvidence!,cancellation);
                            summary="Saved the reviewed gameplay, full graph, simulation presentation, camera, projectile and animation hashes with build/schema/frame identity.";break;
                        case TheatreStudioTool.CompareBuildEvidence:
                            string input=Path.GetFullPath(options.InputPath);RequireRegular(input);
                            if(new FileInfo(input).Length>64*1024)throw new InvalidDataException("Build evidence exceeds 64 KiB.");
                            var prior=JsonSerializer.Deserialize<BuildEvidence>(await File.ReadAllTextAsync(input,cancellation))??throw new InvalidDataException("Invalid build evidence.");
                            if(prior.Version!=2||prior.Schema!=ReplayStateHash.Schema||prior.Frame!=context.Frame||prior.SourceHash!=frozenEvidence!.SourceHash)throw new InvalidDataException("Build evidence must use the same recording bytes, schema and reviewed frame.");
                            path=Path.Combine(directory,"build-comparison.json");
                            var result=new{Frame=context.Frame,BuildA=prior.Build,BuildB=frozenEvidence!.Build,GameplayEqual=prior.Gameplay==frozenEvidence.Gameplay,PresentationEqual=prior.Presentation==frozenEvidence.Presentation,FullGraphEqual=prior.FullGraph==frozenEvidence.FullGraph,CameraEqual=prior.Camera==frozenEvidence.Camera,ProjectilesEqual=prior.Projectiles==frozenEvidence.Projectiles,AnimationsEqual=prior.Animations==frozenEvidence.Animations};
                            await Write(path,result,cancellation);summary=JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true});break;
                        case TheatreStudioTool.ImportPortable:
                            string bundle=Path.GetFullPath(options.InputPath);RequireRegular(bundle);
                            path=ReplayPortableBundle.Import(bundle,directory,cancellation);summary="Verified portable replay and sidecars extracted. The exact bundled map remains beside the recording; opening a custom replay still requires that exact map to be installed.";break;
                        case TheatreStudioTool.PortableBundle:
                            path=Path.Combine(directory,RmlUiReplayFileNames.PortableBundleName);
                            ReplayPortableBundle.Export(path,source,annotations.Bytes,ReplayReels.ReadReviewed(reel,context.LogicalPath,context.Duration),portableCamera!,mapIdentity,portableMap,cancellation);
                            summary="Saved portable replay and camera state. Custom weapon recordings include the exact weapon package; custom maps are included when available.";break;
                        default:
                            path=Path.Combine(directory,options.Tool==TheatreStudioTool.PortableBundle?RmlUiReplayFileNames.PortableBundleName:"diagnostics.zip");
                            using(var archive=ZipFile.Open(path,ZipArchiveMode.Create))
                            {
                                async Task Entry(string name,byte[] bytes){var item=archive.CreateEntry(name);await using var stream=item.Open();await stream.WriteAsync(bytes,cancellation);}
                                if(annotations.Bytes!=null)await Entry("recording.ppdemo"+ReplayAnnotations.Extension,annotations.Bytes.ToArray());
                                if(reel.Bytes!=null)await Entry("recording.ppdemo.reel.json",reel.Bytes.ToArray());
                                if(camera.Bytes!=null)await Entry("recording.ppdemo.camera",camera.Bytes.ToArray());
                                await Entry("manifest.json",JsonSerializer.SerializeToUtf8Bytes(new{Version=1,Scope=options.Tool.ToString(),Duration=context.Duration,Frame=context.Frame,Events=files.Events,Players=players.Select(p=>new{p.Slot,p.Hunter,p.Team}),MapHash=mapHash,RequiredCustomMap=mapIdentity,CustomMapPolicy="Install the exact required custom map separately; map assets are not included."}));
                            }
                            summary=options.Tool==TheatreStudioTool.PortableBundle?"Portable replay and reviewed editor sidecars saved. Required custom map assets are not included.":"Diagnostic events and reviewed editor sidecars saved. Recording bytes and player names are excluded.";break;
                    }
                    cancellation.ThrowIfCancellationRequested();return new TheatreStudioToolResult(summary,path);
                }
                catch(Exception error){if(error is not ReplayPortableWeaponInstallationRequiredException){try{Directory.Delete(directory,true);}catch(IOException){}}throw;}
            },cancellation);
        }
        private static string FileFacts(string path){var info=new FileInfo(path);return info.Length+":"+info.LastWriteTimeUtc.Ticks;}
        private static void RequireRegular(string path){var info=new FileInfo(path);if(!info.Exists||info.LinkTarget!=null)throw new IOException("Choose an existing regular file.");}
        private static async Task Write(string path,object value,CancellationToken cancellation)=>await File.WriteAllTextAsync(path,JsonSerializer.Serialize(value,new JsonSerializerOptions{WriteIndented=true}),cancellation);
    }
}
#endif
