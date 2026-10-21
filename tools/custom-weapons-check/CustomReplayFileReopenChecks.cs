using System.Text.Json;
using MphRead;
using MphRead.Formats;
using MphRead.Mods;
using MphRead.Mods.CustomWeapons.Packages;
using MphRead.Mods.Network;
using MphRead.Mods.Render;
using MphRead.Mods.Replay;
using MphRead.Sound;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;

internal static class CustomReplayFileReopenChecks
{
    internal static int Run(string input,string output)
    {
        string cwd=Directory.GetCurrentDirectory();NativeWindow? window=null;DesktopGraphicsSession? graphics=null;
        try
        {
            input=Path.GetFullPath(input);output=Path.GetFullPath(output);
            string data=File.ReadAllText(Path.Combine(input,"replay-fixture-data.txt"));Directory.SetCurrentDirectory(data);Paths.UpdatePaths();Paths.ChooseMphPath();MapAudit.ForceEveryone=true;
            var settings=DesktopGlContext.Settings(background:true);settings.ClientSize=new(960,720);settings.StartVisible=true;settings.Title="Cold custom replay file Scene inspection";
            window=new NativeWindow(settings);graphics=new DesktopGraphicsSession(window);GLFW.SwapInterval(0);
            int checks=Validate(input,output,window);Console.WriteLine($"PASS {checks} cold custom replay file Scene checks");return 0;
        }
        catch(Exception error){Console.Error.WriteLine("CUSTOM REPLAY FILE FAIL "+error);return 1;}
        finally{AudioLifetime.Shutdown();graphics?.Dispose();window?.Dispose();Directory.SetCurrentDirectory(cwd);}
    }
    internal static int Validate(string input,string output,NativeWindow window)
    {
        int checks=0;void Check(bool ok,string label){if(!ok)throw new InvalidDataException(label);checks++;}
        byte[] Read(string name,int cap){if(Path.GetFileName(name)!=name)throw new InvalidDataException("Fixture input must be a local basename.");using var stream=File.OpenRead(Path.Combine(input,name));if(stream.Length<=0||stream.Length>cap)throw new InvalidDataException("Fixture input exceeds bound.");byte[] value=new byte[(int)stream.Length];stream.ReadExactly(value);return value;}
        Directory.CreateDirectory(output);byte[] package=Read("variant.ppweapon",32*1024*1024);var parsed=WeaponPackage.Parse(package);
        string archive=Path.Combine(input,"actual-full-custom"+DemoFile.Extension);using var json=JsonDocument.Parse(Read("file-expected-worlds.json",64*1024));var rows=json.RootElement.EnumerateArray().ToArray();Check(rows.Length is >0 and <=8,"bounded genuine expected world list");
        uint Frame(JsonElement row)=>row.GetProperty("frame").GetUInt32();
        ReplayWorldCheckpoint Expected(JsonElement row)=>ReplayWorldCheckpoint.FromCustomBytes(Read(row.GetProperty("filename").GetString()!,ReplayWorldCheckpoint.MaximumBytes));
        var finalRow=rows.Single(row=>row.GetProperty("name").GetString()=="end");
        bool missingRefused=false;try{using var missing=Task.Run(()=>PreparedCustomReplayArchive.Prepare(archive,new WeaponPackageStore(Path.Combine(output,"missing-store")))).GetAwaiter().GetResult();}catch(Exception error)when(error is IOException or InvalidDataException){missingRefused=true;File.WriteAllText(Path.Combine(output,"missing-package-refusal.txt"),error.ToString());}Check(missingRefused,"missing exact historical package refuses cold file Scene preparation");
        int ordinal=0,cadenceDraws=0,syntheticDrawChecks=0;var results=new List<object>();
        foreach(var row in rows.Reverse())
        {
            var foregroundGame=GameState.Current;var foregroundRandom=Rng.Current;uint foregroundRng1=Rng.Rng1,foregroundRng2=Rng.Rng2;bool foregroundActive=NetSession.Active;ushort foregroundMatch=NetSession.CurrentMatchId;ulong foregroundEpoch=NetSession.AuthorityEpoch;
            string name=row.GetProperty("name").GetString()!;uint target=Frame(row);var store=new WeaponPackageStore(Path.Combine(output,"store-"+ordinal++));store.Install(package);
            int worker=0;using var prepared=Task.Run(()=>{worker=Environment.CurrentManagedThreadId;return PreparedCustomReplayArchive.Prepare(archive,store,target);}).GetAwaiter().GetResult();Check(worker!=Environment.CurrentManagedThreadId,"archive/resource preparation occurs on worker");
            if(name=="between"){using var selectedWorld=prepared.CopyWorld();Check(selectedWorld.Frame<target,"non-indexed target reconstructs forward from an earlier durable world");}
            using(var scene=PassiveReplayScene.OpenCustomArchiveInspection(prepared,new Vector2i(640,480)))
            {
                prepared.Dispose();Check(store.Uninstall(parsed.PackageHash)&&store.Prune()==0,"file Scene owns historical package after preparation disposal and uninstall");
                using var expected=Expected(row);using var actual=ReplayWorldCheckpoint.CaptureCustom(scene);var differences=expected.CanonicalGraphDifferences(actual);File.WriteAllText(Path.Combine(output,name+"-differences.json"),JsonSerializer.Serialize(differences));Check(differences.Length==0,"file seek restores exact genuine world "+name);
                Check(scene.Session.RecordingFrame==target&&scene.Scene.LocalWeaponPackage?.PackageHash==parsed.PackageHash,"file seek exact clock and package "+name);
                void DrawCadence(int sample)
                {
                    int previousWatched=scene.Scene.Players.MainPlayerIndex;
                    try
                    {
                    var watched=scene.Scene.Players.Items[0];Vector3 targetPoint=watched.Position+new Vector3(0,1,0);bool far=(sample&1)!=0;
                    scene.Scene.Players.MainPlayerIndex=far?1:0;scene.Scene.SetReplicaCamera(targetPoint+(far?new Vector3(70,30,90):new Vector3(2.7f,1.5f,3.8f)),targetPoint,55);scene.Scene.OnResize();
                    using var beforeDraw=ReplayWorldCheckpoint.CaptureCustom(scene);
                    scene.Scene.OnDrawFrame();Check(scene.Scene.OnRenderFrame()&&GL.GetError()==OpenTK.Graphics.OpenGL.ErrorCode.NoError,"cadenced actual file Scene renders "+name);DesktopGraphicsSession.Present(window);scene.Scene.AfterRenderFrame();
                    using var afterDraw=ReplayWorldCheckpoint.CaptureCustom(scene);var drawDifferences=beforeDraw.CanonicalGraphDifferences(afterDraw);File.WriteAllText(Path.Combine(output,name+"-draw-"+sample+"-differences.json"),JsonSerializer.Serialize(drawDifferences));Check(drawDifferences.Length==0,"strict graph unchanged by near/far watched draw "+name);cadenceDraws++;
                    scene.Scene.ClearPresentationEffectTransforms();using var afterClear=ReplayWorldCheckpoint.CaptureCustom(scene);Check(beforeDraw.CanonicalGraphDifferences(afterClear).Length==0,"draw override cleanup preserves canonical suffix owner "+name);
                    }
                    finally{scene.Scene.Players.MainPlayerIndex=previousWatched;}
                }
                for(int draw=0;draw<3;draw++)DrawCadence(draw);
                int suffixDrawSample=3;while(!scene.Session.AtEnd){if(!scene.StepCustomArchiveInspection())break;if((scene.Session.RecordingFrame-target)%17==0){DrawCadence(suffixDrawSample++);DrawCadence(suffixDrawSample++);}}
                using var expectedFinal=Expected(finalRow);using var actualFinal=ReplayWorldCheckpoint.CaptureCustom(scene);var finalDifferences=expectedFinal.CanonicalGraphDifferences(actualFinal);File.WriteAllText(Path.Combine(output,name+"-end-differences.json"),JsonSerializer.Serialize(finalDifferences));Check(finalDifferences.Length==0&&scene.Session.RecordingFrame==Frame(finalRow),"file suffix reaches genuine final world "+name);byte[] endDecoder=scene.State.CaptureCheckpoint().Bytes.ToArray();Check(!scene.StepCustomArchiveInspection()&&scene.State.CaptureCheckpoint().Bytes.SequenceEqual(endDecoder),"file EOF step is inert "+name);results.Add(new{name,target,finalFrame=scene.Session.RecordingFrame});
                // Synthetic adversarial presentation is isolated from genuine accepted facts and suffix owners.
                using(var adversarialPrepared=Task.Run(()=>PreparedCustomReplayArchive.Prepare(archive,store,target)).GetAwaiter().GetResult())
                using(var adversarialScene=PassiveReplayScene.OpenCustomArchiveInspection(adversarialPrepared,new Vector2i(640,480)))
                {
                    // Only this independent synthetic owner waits for a genuine accepted live-biped frame.
                    while((adversarialScene.Scene.Players.Items[0].Health==0||adversarialScene.Scene.Players.Items[0].IsAltForm)&&!adversarialScene.Session.AtEnd)
                        if(!adversarialScene.StepCustomArchiveInspection())break;
                    syntheticDrawChecks+=CustomReplayDrawFlagChecks.Run(adversarialScene,window,Path.Combine(output,name+"-synthetic"));
                }
                // A fresh picture owner may draw, but is never advanced or used for canonical suffix comparisons afterward.
                using var picturePrepared=Task.Run(()=>PreparedCustomReplayArchive.Prepare(archive,store,target)).GetAwaiter().GetResult();
                using var pictureScene=PassiveReplayScene.OpenCustomArchiveInspection(picturePrepared,new Vector2i(640,480));picturePrepared.Dispose();
                var pictureActor=pictureScene.Scene.Players.Items[0];Vector3 cameraTarget=pictureActor.Position+new Vector3(0,1,0);pictureScene.Scene.SetReplicaCamera(cameraTarget+new Vector3(2.7f,1.5f,3.8f),cameraTarget,55);pictureScene.Scene.OnResize();pictureScene.Scene.OnDrawFrame();Check(pictureScene.Scene.OnRenderFrame()&&GL.GetError()==OpenTK.Graphics.OpenGL.ErrorCode.NoError,"independent cold file picture Scene native GL renders "+name);Check(ScreenCapture.SaveWindow(pictureScene.Scene,Path.Combine(output,name+"-window.png"))&&ScreenCapture.Save(pictureScene.Scene,Path.Combine(output,name+"-world.png")),"independent cold file picture images retained "+name);DesktopGraphicsSession.Present(window);pictureScene.Scene.AfterRenderFrame();
            }
            Check(store.Prune()==1,"file Scene disposal releases isolated historical package");
            Check(ReferenceEquals(GameState.Current,foregroundGame)&&ReferenceEquals(Rng.Current,foregroundRandom)&&Rng.Rng1==foregroundRng1&&Rng.Rng2==foregroundRng2&&NetSession.Active==foregroundActive&&NetSession.CurrentMatchId==foregroundMatch&&NetSession.AuthorityEpoch==foregroundEpoch,"file inspection preserves foreground GameState RNG and session identity "+name);
        }
        File.WriteAllText(Path.Combine(output,"file-reopen-results.json"),JsonSerializer.Serialize(new{checks,cadenceDraws,syntheticDrawChecks,results,scope="Actual full native/custom format7 file, independent worker preparation, fresh Scene reopen/backward-forward seeks, strict repeated near/far draw and genuine suffix world parity, with isolated labelled synthetic frozen/hidden/spectating draw fixtures. No graph masks, genuine freeze/contact or live authority claim."},new JsonSerializerOptions{WriteIndented=true}));return checks;
    }
}
