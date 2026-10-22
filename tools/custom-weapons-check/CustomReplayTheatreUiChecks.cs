using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using MphRead;
using MphRead.Formats;
using MphRead.Mods;
using MphRead.Mods.CustomWeapons.Packages;
using MphRead.Mods.Diagnostics;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.Gui;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Pages.Theatre;
using MphRead.Mods.Launcher.RmlUi.Render;
using MphRead.Mods.Network;
using OpenTK.Graphics.OpenGL;

internal static class CustomReplayTheatreUiChecks
{
    internal static int Run(string input,string output)
    {
        string cwd=Directory.GetCurrentDirectory();string? gate=Environment.GetEnvironmentVariable("PROJECT_PRIME_CUSTOM_WEAPON_PLAYBACK"),storeEnv=Environment.GetEnvironmentVariable("PROJECT_PRIME_WEAPON_STORE"),nativeEnv=Environment.GetEnvironmentVariable("PROJECT_PRIME_RMLUI_GL_SMOKE");Action<RenderWindow>? observe=null;
        try
        {
            input=Path.GetFullPath(input);output=Path.GetFullPath(output);Directory.CreateDirectory(output);string loaded=typeof(Scene).Assembly.Location;Console.WriteLine("Loaded ProjectPrime: "+loaded+" SHA256="+Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(loaded))));string archive=Path.Combine(input,"actual-full-custom"+DemoFile.Extension);var store=new WeaponPackageStore(Path.Combine(output,"store"));var package=store.Install(File.ReadAllBytes(Path.Combine(input,"variant.ppweapon")));Environment.SetEnvironmentVariable("PROJECT_PRIME_CUSTOM_WEAPON_PLAYBACK","1");Environment.SetEnvironmentVariable("PROJECT_PRIME_WEAPON_STORE",Path.Combine(output,"store"));Environment.SetEnvironmentVariable("PROJECT_PRIME_RMLUI_GL_SMOKE","1");Directory.SetCurrentDirectory(File.ReadAllText(Path.Combine(input,"replay-fixture-data.txt")));Paths.UpdatePaths();Paths.ChooseMphPath();
            int stage=0,checks=0,frames=0;ulong sequence=100000;uint goal=0;Exception? failure=null;var watch=Stopwatch.StartNew();var evidence=new List<object>();void Check(bool ok,string label){if(!ok)throw new InvalidDataException(label);checks++;}var playbackField=typeof(RmlUiOpenGlSmoke).GetField("_playback",BindingFlags.Static|BindingFlags.NonPublic)!;var hostField=typeof(RmlUiOpenGlSmoke).GetField("_host",BindingFlags.Static|BindingFlags.NonPublic)!;
            observe=window=>
            {
                try
                {
                    if(watch.Elapsed.TotalSeconds>120)throw new TimeoutException("Real Shell replay UI did not finish bounded smoke.");frames++;var page=playbackField.GetValue(null) as TheatrePlaybackPagePresenter;
                    bool Action(TheatrePlaybackAction action)=>page!.HandleIntent(new(RmlUiIntentKind.ReplayAction,(int)action,page.Document,sequence++));
                    void Picture(string label){Check(GL.GetError()==OpenTK.Graphics.OpenGL.ErrorCode.NoError,"real Shell/native page GL state");Check(ScreenCapture.SaveWindow(window.FramebufferSize.X,window.FramebufferSize.Y,Path.Combine(output,label+".png")),"actual Shell backbuffer screenshot");evidence.Add(new{label,frame=ReplayController.CurrentFrame,stage,document=page?.Document.ToString(),nativeHost=hostField.GetValue(null)?.GetType().FullName});}
                    if(stage==0&&frames>=20)
                    {
                        Check(Shell.Active&&!window.HasScene,"real Shell launcher owns native window before replay launch");typeof(Shell).GetMethod("Decided",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,new object[]{new LaunchPlan{Kind=LaunchKind.Demo,DemoPath=archive,Hunter=Hunter.Samus}});stage=1;
                    }
                    else if(stage==1&&page?.IsOpen==true&&DemoPlayback.PresentationScene!=null&&!ReplayController.IsSeeking)
                    {
                        Check(DemoPlayback.IsCustomPlayback&&DemoPlayback.RequestReady(DemoPlayback.RequestOwner,window.CanonicalScene!),"real native playback page belongs to ready ordinary custom request");Check(hostField.GetValue(null)!=null,"real native RmlUi host, no fake backend");if(!ReplayController.IsPaused)Check(Action(TheatrePlaybackAction.TogglePause),"native page pause intent accepted");Check(ReplayController.IsPaused,"native page actually pauses transport");Picture("native-page-paused");Check(store.Uninstall(package.PackageHash)&&store.Prune()==0,"real Shell playback retains exact archive");Check(Action(TheatrePlaybackAction.TogglePause)&&!ReplayController.IsPaused,"native page resumes actual transport");stage=2;
                    }
                    else if(stage==2&&page?.IsOpen==true&&ReplayController.CurrentFrame>0)
                    {
                        Check(Action(TheatrePlaybackAction.TogglePause)&&ReplayController.IsPaused,"native page re-pauses actual playback");goal=Math.Min(ReplayController.DurationFrames,ReplayController.CurrentFrame+600);Check(Action(TheatrePlaybackAction.JumpForward),"native page forward seek intent accepted");stage=3;
                    }
                    else if(stage==3&&page?.IsOpen==true&&!ReplayController.IsSeeking&&ReplayController.CurrentFrame==goal)
                    {
                        Picture("native-page-seek");Check(Action(TheatrePlaybackAction.Back),"native page back intent accepted");stage=4;
                    }
                    else if(stage==4&&!window.HasScene&&!DemoPlayback.IsActive)
                    {
                        Check(store.Prune()==1,"actual back-close releases package lease");Picture("native-page-closed");stage=5;window.Close();
                    }
                }
                catch(Exception error){failure=error;window.Close();}
            };
            LauncherWindowCheck.NativeReplayWindowObserved+=observe;bool ran=GuiLauncher.TryRun();if(failure!=null)throw failure;Check(ran&&stage==5,"actual Shell route completed native page/control/back-close lifecycle");File.WriteAllText(Path.Combine(output,"theatre-ui-results.json"),JsonSerializer.Serialize(new{checks,frames,seconds=watch.Elapsed.TotalSeconds,evidence,scope="Actual Shell replay launch, native RmlUi playback document, real presenter intents and backbuffer screenshots. No synthetic UI backend or automated pixel-layout correctness claim."},new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine($"PASS {checks} actual native replay theatre UI checks");return 0;
        }
        catch(Exception error){Console.Error.WriteLine("CUSTOM REPLAY THEATRE UI FAIL "+error);return 1;}
        finally{if(observe!=null)LauncherWindowCheck.NativeReplayWindowObserved-=observe;Environment.SetEnvironmentVariable("PROJECT_PRIME_CUSTOM_WEAPON_PLAYBACK",gate);Environment.SetEnvironmentVariable("PROJECT_PRIME_WEAPON_STORE",storeEnv);Environment.SetEnvironmentVariable("PROJECT_PRIME_RMLUI_GL_SMOKE",nativeEnv);Directory.SetCurrentDirectory(cwd);}
    }
}
