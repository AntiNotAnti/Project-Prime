using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MphRead;
using MphRead.Mods;
using MphRead.Mods.Diagnostics;
using MphRead.Mods.Input;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.Gui;
using MphRead.Mods.Launcher.RmlUi.Core;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Pages.Theatre;
using MphRead.Mods.Launcher.RmlUi.Pages.Hunters;
using MphRead.Mods.Launcher.RmlUi.Pages.License;
using MphRead.Mods.Launcher.RmlUi.Pages.Studio;
using MphRead.Mods.Cosmetics;
using MphRead.Mods.Launcher.RmlUi.Render;
using MphRead.Mods.Network;
using MphRead.Mods.Render;
using MphRead.Mods.Replay;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.GraphicsLibraryFramework;

/// <summary>
/// Opt-in real-window qualification. Shell owns the sole window, player, scene and clock.
/// Inputs go through the production native input/page lease; reflection only reads QA facts.
/// This mode must be run alone, with an explicitly supplied owned/synthetic archive and setup.
/// </summary>
internal sealed partial class ReplayWindowAcceptance
{
    private sealed record Step(string Name, Func<bool> Poll, int Timeout = 20000);
    private readonly Queue<Step> _steps = new();
    private readonly List<object> _checks = new(), _frames = new(), _captures = new();
    private readonly List<object> _timelineGates = new();
    private readonly List<bool> _workspaceRequests = new();
    private int _replayWorkspaceAttempts;
    private readonly Stopwatch _elapsed = Stopwatch.StartNew();
    private readonly string _output, _source, _replay, _sourceHash;
    private readonly int _width, _height;
    private readonly bool _reopenOnly;
    private readonly bool _studioRole = Environment.GetEnvironmentVariable("PROJECT_PRIME_RMLUI_TEST_STUDIO_ROLE") == "1";
    private readonly bool _libraryDiagnostics = Environment.GetEnvironmentVariable("PROJECT_PRIME_RMLUI_REPLAY_DIAGNOSTICS") == "1";
    private readonly RmlUiReplayEntry _entry;
    private RenderWindow? _window;
    private long _stepStarted, _lastWorldFrame, _observedFrames, _lastFocusAttempt;
    private Vector2i _observedClient, _observedFramebuffer;
    private string? _captureFailure;
    private string _lastLibraryDiagnostic = "";
    private Exception? _failure;
    private readonly LoadedNativeModuleCheck.Expected _expectedNative;
    private LoadedNativeModuleCheck.Attestation? _loadedNative;
    private bool _completed, _closing, _observerAttached, _frameAttached;
    private object? _playerOwner, _oldTransport;
    private Scene? _oldReplica;
    private TheatreCameraToolsController? _originalTools;
    private string _beforeDrag = "";
    private uint[] _beforeFrames = Array.Empty<uint>();
    private uint[] _expectedFrames = Array.Empty<uint>();
    private (double X,double Y) _dragStart, _dragEnd;
    private Vector2i _beforeResize;
    private long _firstPicture;
    private byte[]? _validSidecar;
    private uint _firstFrame;
    private int _pausedPictures;
    private static readonly BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;
    private static readonly BindingFlags StaticPrivate = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    internal static int Run(string[] args)
    {
        if (args.Length is < 4 or > 5)
            throw new ArgumentException("--replay-window OWNED_OR_SYNTHETIC.ppdemo SETUP_paths.txt NEW_OUTPUT_DIRECTORY [WIDTHxHEIGHT]");
        if (args[0] != "--replay-reopen" && Environment.GetEnvironmentVariable("PROJECT_PRIME_RMLUI_TEST_STUDIO_ROLE") != "1")
            throw new ArgumentException("The deprecated complex replay-window diagnostic requires PROJECT_PRIME_RMLUI_TEST_STUDIO_ROLE=1. Use --replay-reopen for Main viewer qualification.");
        string source = Path.GetFullPath(args[1]), setup = Path.GetFullPath(args[2]), output = Path.GetFullPath(args[3]);
        int width=960,height=600;
        if(args.Length==5)
        {
            string[] size=args[4].Split('x');
            if(size.Length!=2||!int.TryParse(size[0],out width)||!int.TryParse(size[1],out height)
                ||width is <640 or >1920||height is <360 or >1080)throw new ArgumentException("Use a client size from 640x360 through 1920x1080.");
        }
        if(!File.Exists(source)||!File.Exists(setup)||Directory.Exists(output)||File.Exists(output))
            throw new ArgumentException("Supply an existing owned/synthetic replay, existing setup paths.txt, and a new output directory.");
        var info=new FileInfo(source);
        if(info.LinkTarget!=null||info.Length is <1 or >268435456||!source.EndsWith(".ppdemo",StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Qualification accepts one ordinary bounded canonical .ppdemo archive, not a virtual descriptor or link.");
        Directory.CreateDirectory(output);
        string user=Path.Combine(output,"isolated-user-data");Directory.CreateDirectory(user);
        // Preserve read-only extraction roots, resolve relative paths against the supplied setup,
        // and place every export/write under this new fixture directory.
        string setupRoot=Path.GetDirectoryName(setup)!;
        var lines=File.ReadAllLines(setup).Select(line=>
        {
            int split=line.IndexOf('=');if(split<0)return line;
            string key=line[..split].Trim(),value=line[(split+1)..].Trim();
            if(key=="Export")return "Export="+Path.Combine(user,"exports");
            return key+"="+(value.Length==0?"":Path.GetFullPath(value,setupRoot));
        }).Where(line=>!line.StartsWith("Export=",StringComparison.Ordinal)).ToList();
        lines.Add("Export="+Path.Combine(user,"exports"));File.WriteAllLines(Path.Combine(user,"paths.txt"),lines);
        Directory.CreateDirectory(Path.Combine(user,"exports"));
        bool reopenOnly = args[0] == "--replay-reopen";
        string replayDirectory = reopenOnly ? Path.Combine(user,"exports","_demos") : user;
        Directory.CreateDirectory(replayDirectory);
        string copied=Path.Combine(replayDirectory,"SYNTHETIC-OWNED-QUALIFICATION.ppdemo");File.Copy(source,copied,overwrite:false);
        if (reopenOnly) File.Copy(source, Path.Combine(replayDirectory,"SECOND-REOPEN-QUALIFICATION.ppdemo"), overwrite:false);
        string sourceHash=Hash(source);
        using(var reader=DemoReader.Open(copied,out var result))
        {
            if(reader==null||result!=ReplayOpenResult.Success)throw new InvalidDataException("Replay preflight: "+result);
            while(reader.ReadNext()!=null){}
            if(reader.LastResult!=ReplayOpenResult.Success||reader.Metadata==null||reader.DurationFrames<180
                ||reader.Metadata.WorldCheckpoint.Length==0)
                throw new InvalidDataException("Qualification requires a healthy durable world archive lasting at least 180 frames: "+reader.LastResult);
        }
        string? previousUser=Environment.GetEnvironmentVariable("PROJECT_PRIME_USER_DATA");
        string previousDirectory=Directory.GetCurrentDirectory();
        ReplayWindowAcceptance? run=null;
        IThumbnailHost? previousThumbnail=null;bool thumbnailBoundary=false;
        bool previousStudioRole = Shell.StudioWindow;
        // Sample the requested policy before touching the static host. Final
        // native-default qualification deliberately leaves the override unset.
        bool nativeDefault = Environment.GetEnvironmentVariable("PROJECT_PRIME_RMLUI_TEST_NATIVE_DEFAULT") == "1";
        string? previousNative = Environment.GetEnvironmentVariable("PROJECT_PRIME_RMLUI_GL_SMOKE");
        Environment.SetEnvironmentVariable("PROJECT_PRIME_RMLUI_GL_SMOKE",nativeDefault ? null : "1");
        var previousWorkspaceLaunch = RmlUiOpenGlSmoke.StudioWorkspaceLaunchOverride;
        var previousCommunityReader = RmlUiOpenGlSmoke.CommunityReaderOverride;
        try
        {
            Environment.SetEnvironmentVariable("PROJECT_PRIME_USER_DATA",user);
            Environment.SetEnvironmentVariable("PROJECT_PRIME_RMLUI_GL_SMOKE",nativeDefault ? null : "1");
            if (nativeDefault && !RmlUiOpenGlSmoke.NativeUiRequired)
                throw new InvalidOperationException("Native-enabled client did not default to RmlUi without an environment override.");
            Console.WriteLine("RMLUI REAL REPLAY POLICY nativeDefault=" + nativeDefault
                + " explicitOverride=" + (Environment.GetEnvironmentVariable("PROJECT_PRIME_RMLUI_GL_SMOKE") ?? "unset"));
            Environment.SetEnvironmentVariable("PROJECT_PRIME_GL_PROFILE","1");
            ConsoleSetup.Run();GameFiles.Root=user;LauncherPrefs.Directory=user;GraphicsBackendPolicy.Configure("opengl");
            if (Environment.GetEnvironmentVariable("PROJECT_PRIME_RMLUI_REPLAY_DIAGNOSTICS") == "1")
            {
                // Include the renderer's inner allocation/refusal reason in this
                // isolated qualification profile, without changing client logging.
                DebugLog.Force(); DebugLog.Attach();
            }
            WindowMode.ForceStartup(WindowStartMode.Windowed);
            if(!GameFiles.Ready)throw new InvalidOperationException("Isolated setup: "+GameFiles.Problem());
            Paths.SetPath("Export",Path.Combine(user,"exports"));
            // This diagnostic entrypoint does not implement the client's -thumbnail worker.
            // Keep unrelated preview jobs from starting more windows or encoders; the replay
            // player, native UI Current backends and presentation/render owners remain actual.
            previousThumbnail=ThumbnailHost.Current;ThumbnailHost.Current=new NoThumbnailJobs();thumbnailBoundary=true;
            run=new(source,copied,sourceHash,output,width,height,reopenOnly);
            run.Attach();
            Shell.StudioWindow = run._studioRole;
            RmlUiOpenGlSmoke.StudioWorkspaceLaunchOverride = run.ObserveWorkspaceLaunch;
            RmlUiOpenGlSmoke.CommunityReaderOverride = () => new EmptyHeaderCommunityReader();
            // This is the same bootstrap used by ModEntry's ordinary -launcher path,
            // including the existing instance guard and deferred custom-room registration.
            bool shellCompleted=GuiLauncher.TryRun();
            if(!shellCompleted)run.Fail(new InvalidOperationException("Production Shell did not complete a native window session."));
            if(!run._completed&&run._failure==null)run.Fail(new InvalidOperationException("Window closed before qualification completed."));
        }
        catch(Exception error)
        {
            if(run==null)
            {
                File.WriteAllText(Path.Combine(output,"acceptance.json"),JsonSerializer.Serialize(new{Passed=false,Stage="setup",Error=error.ToString()},JsonOptions));
                Console.Error.WriteLine(error);return 1;
            }
            run.Fail(error);
        }
        finally
        {
            run?.CleanupDetachedQualification();
            run?.Detach();
            if(thumbnailBoundary)ThumbnailHost.Current=previousThumbnail;
            Shell.StudioWindow = previousStudioRole;
            RmlUiOpenGlSmoke.StudioWorkspaceLaunchOverride = previousWorkspaceLaunch;
            RmlUiOpenGlSmoke.CommunityReaderOverride = previousCommunityReader;
            Environment.SetEnvironmentVariable("PROJECT_PRIME_RMLUI_GL_SMOKE",previousNative);
            Directory.SetCurrentDirectory(previousDirectory);
            Environment.SetEnvironmentVariable("PROJECT_PRIME_USER_DATA",previousUser);
        }
        return run!.Finish();
    }

    private ReplayWindowAcceptance(string source,string replay,string sourceHash,string output,int width,int height,bool reopenOnly)
    {
        _source=source;_replay=replay;_sourceHash=sourceHash;_output=output;_width=width;_height=height;_reopenOnly=reopenOnly;
        // Independent expected SHA is required BEFORE GuiLauncher creates a window.
        _expectedNative=LoadedNativeModuleCheck.CaptureExpectedPackage(
            Path.GetDirectoryName(typeof(RmlUiHost).Assembly.Location)!);
        using var reader=DemoReader.Open(replay,out _);
        _rml97HasRecordedCue=reader!.Metadata!.Events.Any(e=>e.Frame<30);
        var file=new FileInfo(replay);
        _entry=new(replay,"Owned synthetic replay qualification","Isolated archive copy",reader!.Metadata!.RoomKey,
            reader.Metadata.RecordedAtUtc.Ticks,file.Length,file.LastWriteTimeUtc.Ticks,false,false,false,true);
        Plan();
    }
    private void Attach(){LauncherWindowCheck.NativeReplayWindowObserved+=Observe;_observerAttached=true;}
    private void Observe(RenderWindow window)
    {
        if(_window!=null)return;
        _window=window;
        window.RenderFrame+=AfterSwap;_frameAttached=true;
        LauncherWindowCheck.NativeReplayWindowObserved-=Observe;_observerAttached=false;
    }
    private void Detach()
    {
        Scene.NativeReplayMultiViewFailureForTests=null;
        DetachDisplayTrialObserver();
        RestoreNativeLobbyTyping();
        if(_observerAttached)LauncherWindowCheck.NativeReplayWindowObserved-=Observe;
        if(_frameAttached&&_window!=null)_window.RenderFrame-=AfterSwap;
        _observerAttached=_frameAttached=false;
    }
    private void AfterSwap(FrameEventArgs args)
    {
        if(_closing)return;
        try
        {
            if(++_observedFrames>30000||_elapsed.Elapsed.TotalSeconds>180)throw new TimeoutException("Real-window qualification exceeded its total bound.");
            if(_window==null||!ReferenceEquals(_window,Shell.Window))throw new InvalidOperationException("Shell window owner changed.");
            if (_libraryDiagnostics && _observedFrames == 1) EnableReplayGlDiagnostics();
            _observedClient=_window.ClientSize;_observedFramebuffer=_window.FramebufferSize;
            if (_reopenOnly && _libraryDiagnostics)
            {
                string diagnostic=LibraryFacts();
                if (diagnostic!=_lastLibraryDiagnostic)
                {
                    Console.WriteLine("RMLUI LIBRARY TRANSITION swap="+_observedFrames+" "+diagnostic);
                    _lastLibraryDiagnostic=diagnostic;
                }
            }
            if(DemoPlayback.IsActive&&DemoPlayback.PresentationScene is{} scene
                &&DemoPlayback.LogicalPath is{} logical&&DemoPlayback.PlaybackPath is{} playback
                &&ReplayCamera.NativePresented.TryRead(scene,DemoPlayback.Session.Transport,DemoPlayback.Session.Transport.SeekGeneration,
                    ReplayController.CurrentFrame,logical,playback,ReplayCamera.PresentedSettings(scene),out var presented))
            {
                _lastWorldFrame=_observedFrames;long picture=presented!.Revision;
                if(_observedFrames%30==0)_frames.Add(new{Swap=_observedFrames,Frame=ReplayController.CurrentFrame,Picture=picture,
                    Seek=DemoPlayback.Session.Transport.SeekGeneration,Player=Id(DemoPlayback.PlaybackOwner),
                    Transport=Id(DemoPlayback.Session.Transport),Replica=Id(scene),Focused=_window.IsFocused,
                    Client=_window.ClientSize.ToString(),Framebuffer=_window.FramebufferSize.ToString()});
            }
            if(_steps.Count==0){_completed=true;_closing=true;Shell.RequestQuit();return;}
            var step=_steps.Peek();
            if (_libraryDiagnostics && _stepStarted == 0
                && (step.Name == "RPS03 inject third-tile draw failure in real Studio window"
                    || step.Name == "RPS03 stress restore initial replay frame"))
                EnableReplayGlDiagnostics();
            if(_stepStarted==0)_stepStarted=_elapsed.ElapsedMilliseconds;
            if(_elapsed.ElapsedMilliseconds-_stepStarted>step.Timeout)throw new TimeoutException("Timed out: "+step.Name+"; "+Facts());
            if(step.Poll())
            {
                _checks.Add(new{Name=step.Name,Passed=true,Swap=_observedFrames,ElapsedMs=_elapsed.ElapsedMilliseconds,Facts=Facts(),ObservedClient=new{_observedClient.X,_observedClient.Y},ObservedFramebuffer=new{_observedFramebuffer.X,_observedFramebuffer.Y}});
                Console.WriteLine("RMLUI REAL REPLAY PASS "+step.Name);_steps.Dequeue();_stepStarted=0;
            }
        }
        catch(Exception error)
        {
            try{Capture("first-failure");}catch(Exception captureError){_captureFailure=captureError.Message;}
            Fail(error);_closing=true;Shell.RequestQuit();
        }
    }
    private static void EnableReplayGlDiagnostics()
        => ScreenCapture.EnableDebugOutput(message =>
            Console.WriteLine("RMLUI GL DIAGNOSTIC " + message + Environment.NewLine + Environment.StackTrace));
    private static void RequireReplayGlClean(string stage)
    {
        var errors = new List<OpenTK.Graphics.OpenGL.ErrorCode>();
        for (int i = 0; i < 32; i++)
        {
            var error = GL.GetError();
            if (error == OpenTK.Graphics.OpenGL.ErrorCode.NoError) break;
            errors.Add(error);
        }
        Require(errors.Count == 0, "actual replay GL errors after " + stage + ": " + string.Join(", ", errors));
    }
    private void Fail(Exception error){_failure??=error;Console.Error.WriteLine("RMLUI REAL REPLAY FAIL "+error);}
    private int Finish()
    {
        try
        {
            if(Hash(_source)!=_sourceHash||Hash(_replay)!=_sourceHash)throw new InvalidOperationException("The supplied recording or isolated recording bytes changed.");
            if(_reopenOnly&&Hash(Path.Combine(Path.GetDirectoryName(_replay)!,"SECOND-REOPEN-QUALIFICATION.ppdemo"))!=_sourceHash)
                throw new InvalidOperationException("The second isolated recording bytes changed.");
            if(_completed)
            {
                if(_loadedNative==null)throw new InvalidOperationException("Native loaded-module attestation missing.");
                // No reload: require the same exact frozen path/hash at run completion.
                LoadedNativeModuleCheck.RequireLoaded(_expectedNative);
            }
            if(_completed&&(DemoPlayback.IsActive||Shell.Active))throw new InvalidOperationException("Production replay/Shell ownership remained active after normal teardown.");
        }
        catch(Exception error){Fail(error);}
        bool passed=_completed&&_failure==null;
        File.WriteAllText(Path.Combine(_output,"acceptance.json"),JsonSerializer.Serialize(new{Passed=passed,
            Claim="Actual production Shell/DemoPlayback/native RmlUi input and successful-world-presentation qualification; no fake backend or second foreground player/window/clock.",
            FocusedLibraryReopen=_reopenOnly,
            ApplicationRole=_studioRole ? "Prime Studio diagnostic role" : "Main Theatre viewer",
            InputClaim="Native keyboard/semantic/controller event injection and production pointer mapping; physical keyboard/controller hardware is not asserted.",
            LoadedNativeModule=_loadedNative,
            Source=_source,SourceSha256=_sourceHash,IsolatedReplay=_replay,RequestedClient=new{Width=_width,Height=_height},
            ObservedClient=new{_observedClient.X,_observedClient.Y},ObservedFramebuffer=new{_observedFramebuffer.X,_observedFramebuffer.Y},
            Bootstrap="Existing ModEntry -launcher path via GuiLauncher.TryRun; actual native startup Continue action, no legacy startup UI.",
            WorkspaceLaunchRequests=_workspaceRequests.Select(replay=>replay ? "Replay" : "Map"),
            WorkspaceLaunchClaim="Explicit native Main hub commands reach an injected process boundary; no child app or IPC acceptance is claimed.",
            ThumbnailClaim="Unrelated thumbnail jobs are suppressed through the public IThumbnailHost seam; no thumbnail acceptance is claimed.",
            HeaderNavigationClaim="Actual Main native header actions and foreground leases; Community discovery uses an empty read-only fixture and no catalog/network acceptance is claimed.",
            HeaderTransitions=_headerTransitions,
            AuxiliaryBackdropChecks=_auxiliaryBackdropChecks,
            OwnedDisplayTrialChecks=_ownedDisplayTrialChecks,
            OwnedDisplayTrialClaim="Actual private Shell window stages Borderless Fullscreen through native Settings, acknowledges a usable drawn frame, then reverts and discards. No Keep or persistence commit is requested; window geometry, scene ownership and preference bytes are checked.",
            AuxiliaryBackdropClaim="Actual Main Home Social and Read Dispatch navigation with composed-window captures; native foreground, selected backdrop and absence of gameplay/replay scenes are checked. Social service completion, account mutations and external links are not exercised.",
            NativeUpdateOwnerClaim="A synthetic offer is queued on the retained production StartScreen, deferred in Theatre, shown by the Shell owner tick at Home, and dismissed with Later; no update check, confirmation, installation, download or browser acceptance is exercised.",
            NativeUpdateOwnerChecks=_updateOwnerChecks,
            NativeLobbyTypingChecks=_nativeLobbyTypingChecks,
            MenuAudioChecks=_menuAudioChecks,
            MenuAudioScope="Actual output-device acquisition, embedded source identity, shared output and loop state across native tabs/scene transitions; human audibility is not asserted.",
            Rml97StudioChecks=_rml97StudioChecks,
            Rml97StudioScope="Studio role only: actual native controls/owners, isolated source and authored PCM fixture. Full-world comparison uses one private bounded-seek replica; foreground transport is retained. No physical hardware input claim.",
            CompletedSteps=_checks,PendingSteps=_steps.Select(s=>s.Name),Frames=_frames,Captures=_captures,TimelineGateDiagnostics=_timelineGates,Error=_failure?.ToString(),CaptureError=_captureFailure,
            ObserverRemoved=!_observerAttached,FrameHandlerRemoved=!_frameAttached,ElapsedMs=_elapsed.ElapsedMilliseconds},JsonOptions));
        return passed?0:1;
    }
    private void Plan()
    {
        Do("configure existing Shell client after its first successful swap",()=>{_window!.ClientSize=new(_width,_height);_window.Focus();});
        Wait("actual native startup gate owns the foreground document",()=>RmlUiOpenGlSmoke.NativeStartupOpen && Host is { Active: true });
        Do("capture actual native startup gate",()=>Capture("native-startup-gate"));
        Do("continue actual native startup through its production action",()=>Activate("native_startup_continue"));
        Wait("native startup continuation permits actual native launcher",()=>!RmlUiOpenGlSmoke.NativeStartupOpen
            && Shell.RmlUiCanPresent&&Host is{Active:true});
        Do("attest exact actually loaded native module after existing host startup",()=>
        {
            _loadedNative=LoadedNativeModuleCheck.RequireLoaded(_expectedNative);
            LoadedNativeModuleCheck.WriteNew(Path.Combine(_output,"loaded-native-module.json"),_loadedNative);
        });
        Wait("actual production window acquires foreground focus",()=>
        {
            if(_window!.IsFocused)return true;
            if(_elapsed.ElapsedMilliseconds-_lastFocusAttempt>500){_window.Focus();_lastFocusAttempt=_elapsed.ElapsedMilliseconds;}
            return false;
        });
        if (!_studioRole) { PlanNativeLobbyTyping(); PlanMenuAudio(); }
        if (_reopenOnly)
        {
            if (!_studioRole)
            {
                PlanMainHeaderNavigation(); PlanOwnedDisplayTrialQualification(); PlanOwnedHudQualification();
                PlanNativeHomeAuxiliaryBackgrounds(); PlanNativeUpdateOwnerIntegration();
            }
            PlanLibraryReopen(); return;
        }
        if(_studioRole)
        {
            Wait("actual Studio role hub owns native foreground before replay launch",()=>
            {
                var studio=ReadStatic<NativeStudioEntryPage>("_studio");
                if(studio==null||Host!.CurrentInputDocument!=studio.Document)return false;
                Require(!DemoPlayback.IsActive&&!_window!.HasScene
                    &&ReadStatic<NativeStudioEditorPage>("_studioEditor")==null,
                    "Studio hub holds the sole Shell window before acquiring a replay or editor");
                Capture("studio-role-startup-hub");return true;
            });
        }
        else
        {
            NativeHunterPagePresenter? headerHunter = null;
            CosmeticLoadout? headerLoadout = null;
            Wait("actual Shell Home owns native header before replay launch",()=>
                ReadStatic<RmlUiLauncherPages>("_launcherPages") is { Suspended:false,Page:RmlUiMenuPage.Home }
                && ReadStatic<NativeHunterPagePresenter>("_hunterChoice")==null
                && ReadStatic<NativeLicenseOverviewPage>("_license")==null
                && ReadStatic<NativeStudioEntryPage>("_studio")==null && !_window!.HasScene);
            Do("Home header HUNTERS uses its actual native action",()=>Activate("nav_hunters"));
            Wait("actual native Hunter picker owns header navigation",()=>
            {
                headerHunter=ReadStatic<NativeHunterPagePresenter>("_hunterChoice");
                if(headerHunter==null||Host!.CurrentInputDocument!=headerHunter.PreviewDocument)return false;
                Require(!DemoPlayback.IsActive&&!_window!.HasScene,"header Hunters opens without a player or scene");
                headerLoadout=headerHunter.PreviewSelection.Cosmetics;Capture("header-hunters-picker");return true;
            });
            Do("stage one cosmetic choice without equipping",()=>Activate("hunter_skin"));
            Wait("actual picker owns an unsaved cosmetic draft",()=>headerHunter!=null&&!headerHunter.CanNavigateAway);
            Do("dirty picker header STUDIO goes through the actual route guard",()=>Activate("nav_studio"));
            WaitFrames(3,"dirty picker refuses Studio without dropping or saving its draft",()=>
            {
                Require(ReferenceEquals(ReadStatic<NativeHunterPagePresenter>("_hunterChoice"),headerHunter)
                    && headerHunter is { CanNavigateAway:false }
                    && ReadStatic<NativeStudioEntryPage>("_studio")==null
                    && Host!.CurrentInputDocument==headerHunter.PreviewDocument,
                    "unsaved cosmetic owner and native foreground remain intact after Studio request");
                Capture("header-studio-dirty-refused");
            });
            Do("discard cosmetic draft through the actual local picker action",()=>Activate("hunter_cosmetics_reset"));
            Wait("discard restores the exact initial cosmetic loadout without Equip",()=>
                headerHunter is { CanNavigateAway:true } && headerHunter.PreviewSelection.Cosmetics==headerLoadout);
            Do("open full Hunter License through the actual picker action",()=>Activate("hunter_full"));
            Wait("actual Hunter License replaces the clean picker",()=>
                ReadStatic<NativeLicenseOverviewPage>("_license")!=null
                && ReadStatic<NativeHunterPagePresenter>("_hunterChoice")==null
                && Host!.TryGetElementBounds(Host.CurrentInputDocument,"native_license_tab_overview",out _,out _,out float w,out float h)
                && w>0&&h>0);
            Do("Hunter License header STUDIO uses its foreground native action",()=>Activate("nav_studio"));
            Wait("actual Studio entry replaces native License from its header",()=>
            {
                var studio=ReadStatic<NativeStudioEntryPage>("_studio");
                if(studio==null||Host!.CurrentInputDocument!=studio.Document
                    ||ReadStatic<NativeLicenseOverviewPage>("_license")!=null)return false;
                Require(!DemoPlayback.IsActive&&!_window!.HasScene,"header Studio retains the sole Shell window before playback");
                Capture("header-studio-entry");return true;
            });
            Do("Studio header PLAY returns using the actual native route",()=>Activate("nav_play"));
            Wait("actual Home resumes after native header journey",()=>
                ReadStatic<RmlUiLauncherPages>("_launcherPages") is { Suspended:false,Page:RmlUiMenuPage.Home }
                && ReadStatic<NativeStudioEntryPage>("_studio")==null
                && ReadStatic<NativeLicenseOverviewPage>("_license")==null
                && ReadStatic<NativeHunterPagePresenter>("_hunterChoice")==null && !_window!.HasScene);
        }
        Wait(_studioRole?"production Studio hub and exact isolated file launch":"production launcher and exact isolated file launch",()=>Host is{Active:true}&&_window!.IsFocused&&Shell.RmlUiLaunchReplay(_entry));
        Wait("production replay opens native playback on the same Shell window",()=>DemoPlayback.IsActive&&Page is{IsOpen:true}&&_lastWorldFrame==_observedFrames);
        Do("pause existing replay through native keyboard action",()=>{if(!ReplayController.IsPaused)Activate("replay_pause");});
        Wait("paused successful world picture",()=>ReplayController.IsPaused&&!ReplayController.IsSeeking&&_lastWorldFrame==_observedFrames);
        if (_studioRole)
        {
            Do("Studio Space owns playback even with Library focused and key repeat",()=>
            {
                Focus("replay_back");
                var space=new KeyboardKeyEventArgs(Keys.Space,scanCode:0,0,isRepeat:false);
                Require(RmlUiOpenGlSmoke.KeyDown(space)&&RmlUiOpenGlSmoke.KeyDown(space)
                    &&RmlUiOpenGlSmoke.KeyUp(space),"production Studio accepts Space down/repeat/up");
            });
            Wait("Studio Space resumes without closing the workspace",()=>
                DemoPlayback.IsActive&&Page is{IsOpen:true}&&_window!.HasScene
                &&!ReplayController.IsPaused&&!ReplayController.IsSeeking);
            Do("Studio Right seeks through the transport rather than the Library button",()=>
            {
                Focus("replay_back");
                var right=new KeyboardKeyEventArgs(Keys.Right,scanCode:0,0,isRepeat:false);
                Require(RmlUiOpenGlSmoke.KeyDown(right)&&RmlUiOpenGlSmoke.KeyUp(right),
                    "production Studio accepts Right down/up");
            });
            Wait("Studio arrow seek retains playing state and the replay owner",()=>
                DemoPlayback.IsActive&&Page is{IsOpen:true}&&_window!.HasScene
                &&!ReplayController.IsPaused&&!ReplayController.IsSeeking);
            Do("Studio Space pauses without invoking focused Library button",()=>
            {
                Focus("replay_back");
                var space=new KeyboardKeyEventArgs(Keys.Space,scanCode:0,0,isRepeat:false);
                Require(RmlUiOpenGlSmoke.KeyDown(space)&&RmlUiOpenGlSmoke.KeyUp(space),
                    "production Studio accepts Space pause down/up");
            });
            Wait("Studio keyboard transport returns to paused live workspace",()=>
                DemoPlayback.IsActive&&Page is{IsOpen:true}&&_window!.HasScene
                &&ReplayController.IsPaused&&!ReplayController.IsSeeking&&_lastWorldFrame==_observedFrames);
        }
        HeldViewportResize();
        Do("capture real playback ownership",()=>{_playerOwner=DemoPlayback.PlaybackOwner;_oldTransport=DemoPlayback.Session.Transport;_oldReplica=DemoPlayback.PresentationScene;});
        Do("open actual native bookmark panel",()=>Activate(_studioRole?"studio_tab_markers":"replay_bookmarks"));
        Do("add a real isolated bookmark through native UI",()=>
        {Host!.SetField(Host.CurrentInputDocument,"replay_bookmark_name","Timeline qualification marker");Activate("replay_bookmark_add");});
        Wait("source-owned bookmark persisted",()=>ReplayAnnotations.Bookmarks(_replay).Count==1);
        Do("return from actual bookmark panel",()=>Activate(_studioRole?"studio_tab_camera":"replay_bookmark_close"));
        OpenCamera();
        Do("save first real presented camera pose using native action",()=>Activate("replay_camera_add"));
        Wait("first camera key persisted",()=>ReplayCamera.Track.Keys.Count==1);
        OpenTimeline();
        uint second=0,third=0;
        Do("choose exact bounded later replay frames",()=>{second=ReplayController.DurationFrames/3;third=(uint)((ulong)ReplayController.DurationFrames*2/3);});
        SeekTimeline(()=>second,"second");CloseTimelineToPlayback();OpenCamera();
        Do("save second actual world camera pose",()=>Activate("replay_camera_add"));
        Wait("second camera key persisted",()=>ReplayCamera.Track.Keys.Count==2);OpenTimeline();
        SeekTimeline(()=>third,"third");CloseTimelineToPlayback();OpenCamera();
        Do("save third actual world camera pose",()=>Activate("replay_camera_add"));
        Wait("three actual source-bound camera keys persisted",()=>ReplayCamera.Track.Keys.Count==3);OpenTimeline();
        Do("record same actual tools owner and first presented timeline",()=>
        {
            _originalTools=Tools;_firstFrame=ReplayController.CurrentFrame;
            Require(DemoPlayback.PresentationScene!.TryCaptureNativeReplayCamera(_firstFrame,out _,out _firstPicture),"successful actual picture exists");
            _pausedPictures=0;
            var lanes=Read<TheatreTimelineLanes>(Page,"_timelineLanes")!;
            Require(lanes.Context.Events.Count(row=>row.Id.StartsWith("bookmark:",StringComparison.Ordinal))==1,"actual Editor owner supplied existing saved bookmark lane");
            Require(lanes.Project(Ticket!.Window,TimelineLane.Bookmarks).Sum(row=>row.Count)==1,"native timeline preserves actual saved bookmark count");
            Capture("initial-three-key-timeline");
        });
        Wait("120 identical paused swaps keep reviewed timeline usable while picture IDs advance",()=>
        {
            if(!ReadyTimeline())return false;
            Require(ReplayController.CurrentFrame==_firstFrame,"paused canonical frame remains unchanged");
            if(++_pausedPictures<120)return false;
            Require(DemoPlayback.PresentationScene!.TryCaptureNativeReplayCamera(_firstFrame,out _,out long revision)&&revision>_firstPicture,
                "real picture ID advances without silently invalidating exact stable pose review");return true;
        });
        Do("native marker keyboard activation resolves exact authored key",()=>Activate("replay_timeline_key_1"));
        Wait("keyboard marker focuses saved second key",()=>Tools.Snapshot().Focus?.Frame==ReplayCamera.Track.Keys[1].Frame);
        Do("native controller action resolves exact authored key",()=>
        {
            Focus("replay_timeline_key_2");
            typeof(RmlUiOpenGlSmoke).GetMethod("NativeGamepadAction",StaticPrivate)!.Invoke(null,new object[]{UiAction.Accept});
        });
        Wait("controller marker focuses saved third key",()=>Tools.Snapshot().Focus?.Frame==ReplayCamera.Track.Keys[2].Frame);
        Do("native accessibility press resolves exact authored key",()=>AccessiblePress("replay_timeline_key_0"));
        Wait("accessible marker focuses saved first key",()=>Tools.Snapshot().Focus?.Frame==ReplayCamera.Track.Keys[0].Frame);
        Do("native queued marker cannot be retargeted by subsequent Tab",()=>
        {
            AccessiblePress("replay_timeline_key_2");Host!.Input.Key(1,true);Host.Input.Key(1,false);
            Require(Host.FocusedElement()!="replay_timeline_key_2","native Tab moved focus after queued source");
        });
        // The queue guard must DROP the old source, rather than treating the newly focused
        // marker as if it had been activated. The previously focused key remains first.
        WaitFrames(3,"retargeted queued marker is discarded",()=>Require(Tools.Snapshot().Focus?.Frame==ReplayCamera.Track.Keys[0].Frame,"queued activation did not select another source"));
        Do("select all actual camera keys using current native controls",()=>Activate(_studioRole?"replay_camera_tools_all":"replay_timeline_all"));
        Wait("all three keys selected",()=>Tools.Snapshot().Selection.Length==3);
        Drag(commit:false,"cancelled");
        Drag(commit:true,"confirmed");
        Do("capture prior actual transport and replica before backward seek",()=>{_oldTransport=DemoPlayback.Session.Transport;_oldReplica=DemoPlayback.PresentationScene;});
        SeekTimeline(()=>0,"backward rebuild");
        Do("explicit refresh reauthorizes same durable player after replica rebuild",()=>Activate("replay_timeline_refresh"));
        Wait("same durable tools owner survives its own backward transport rebuild",()=>
        {
            if(!ReadyTimeline())return false;
            Require(ReferenceEquals(_playerOwner,DemoPlayback.PlaybackOwner),"seek retained exact durable player owner");
            Require(ReferenceEquals(_originalTools,Tools),"explicit refresh retained same tools controller");
            Require(!ReferenceEquals(_oldTransport,DemoPlayback.Session.Transport)&&!ReferenceEquals(_oldReplica,DemoPlayback.PresentationScene),"backward replay rebuilt actual transport and replica");
            return true;
        });
        Do("resize actual client retires old presented bounds",()=>{_beforeResize=_window!.ClientSize;_window.ClientSize=new(_beforeResize.X-32,_beforeResize.Y-18);});
        Wait("new actual resized timeline ticket is current",()=>_window!.ClientSize!=_beforeResize&&ReadyTimeline());
        Do("capture real resized timeline",()=>Capture("after-backward-seek-and-resize"));
        Do("native focus release retires active timeline ticket",()=>RmlUiOpenGlSmoke.NativeFocusChanged(false));
        Do("focus release leaves saved sidecar unchanged",()=>Require(Hash(_replay+".camera")==_beforeDrag,"focus release did not persist another edit"));
        Do("explicit review restores input after focus release",()=>Activate("replay_timeline_refresh"));
        Wait("fresh actual timeline picture after release",ReadyTimeline);
        Do("stage source-sidecar ABA edit in isolated file only",()=>
        {
            _beforeDrag=Hash(_replay+".camera");string path=_replay+".camera";var time=File.GetLastWriteTimeUtc(path);byte[] bytes=File.ReadAllBytes(path);
            _validSidecar=(byte[])bytes.Clone();bytes[^1]^=1;File.WriteAllBytes(path,bytes);File.SetLastWriteTimeUtc(path,time);
        });
        Do("native reviewed marker action rejects same-length same-stamp changed camera sidecar",()=>Activate("replay_timeline_key_1"));
        Wait("stale sidecar cannot authorize native camera edits",()=>!Tools.Snapshot().Current);
        Do("capture actual stale-sidecar state",()=>Capture("stale-sidecar"));
        CloseTimelineToPlayback();
        Do("normal native Library action releases existing replay",()=>Activate("replay_back"));
        Wait("production back releases replay player and native playback lease",()=>!DemoPlayback.IsActive&&Page==null);
        Do("restore isolated valid sidecar for a new same-path production session",()=>File.WriteAllBytes(_replay+".camera",_validSidecar!));
        Wait("relaunch same isolated path through normal production Editor request",()=>Shell.RmlUiLaunchReplay(_entry,openEditor:true));
        Wait("new actual same-path player never revives prior tools authority",()=>
        {
            if(!DemoPlayback.IsActive||Page==null||_lastWorldFrame!=_observedFrames)return false;
            Require(!ReferenceEquals(_playerOwner,DemoPlayback.PlaybackOwner),"same path restart has a distinct actual durable player owner");
            Require(_originalTools?.Snapshot().Current==false,"retired tools cannot adopt same-path replacement player");return true;
        });
        Wait("Library Editor captures actual ready replacement player and current transport",()=>
        {
            if(_window?.CanonicalScene is not{} canonical||!DemoPlayback.RequestReady(DemoPlayback.RequestOwner,canonical)
                ||Editor is not{} editor||Page==null||Host!.CurrentInputDocument!=Page.Document)return false;
            var state=editor.Snapshot();
            Require(state.Live.Current&&ReferenceEquals(state.Context.Owner,DemoPlayback.PlaybackOwner)
                &&ReferenceEquals(state.Context.Transport,DemoPlayback.Session.Transport),"actual library Editor did not bind the provisional prepared session");
            Require(typeof(RmlUiOpenGlSmoke).GetField("_pendingNativeReplayEditor",StaticPrivate)!.GetValue(null)==null,
                "actual ready Editor consumed the exact pending request once");
            return true;
        });
        Do("capture actual ready-player Library Editor",()=>Capture("same-path-ready-player-editor"));
        Do("select Camera in actual persistent Library Editor",()=>Activate("studio_tab_camera"));
        Wait("actual replacement persistent Editor keeps playback foreground",()=>Page!=null&&Host!.CurrentInputDocument==Page.Document);
        Do("normal native Back closes replacement session",()=>Activate("replay_back"));
        Wait("replacement replay tears down through existing Shell",()=>!DemoPlayback.IsActive&&Page==null);
    }

    private void PlanLibraryReopen()
    {
        RmlUiMenuController? menu = null;
        RmlUiTheatreIndex? library = null;
        string firstPath = "", secondPath = "";
        object? firstPlayer = null, secondPlayer = null;
        if (_studioRole)
        {
            Wait("actual Studio role starts in native hub",()=>ReadStatic<NativeStudioEntryPage>("_studio")!=null);
            Do("open native library through Studio hub replay action",()=>Activate("native_studio_replay_open"));
        }
        else
        {
            NativeStudioEntryPage? mainHub = null;
            Do("Main Studio header opens native destination hub without launching another app",()=>Activate("nav_studio"));
            Wait("Main native destination hub owns foreground without a scene or process request",()=>
            {
                mainHub = ReadStatic<NativeStudioEntryPage>("_studio");
                if (mainHub == null || Host!.CurrentInputDocument != mainHub.Document) return false;
                Require(_workspaceRequests.Count == 0 && !_window!.HasScene && !DemoPlayback.IsActive,
                    "Main header navigation does not launch a process or acquire replay/map gameplay");
                return true;
            });
            Do("explicit Main Map workspace choice reaches only process boundary",()=>Activate("native_studio_map"));
            Wait("accepted Map workspace preserves Main native hub with coming-soon status",()=>
            {
                if (_workspaceRequests.Count != 1) return false;
                Require(!_workspaceRequests[0] && ReferenceEquals(mainHub, ReadStatic<NativeStudioEntryPage>("_studio"))
                    && Read<string>(mainHub, "_status")!.Contains("COMING SOON", StringComparison.Ordinal)
                    && !_window!.HasScene && !DemoPlayback.IsActive && ReadStatic<NativeStudioEditorPage>("_studioEditor") == null,
                    "Map choice passes replay=false and keeps Main free of any map editor or scene");
                return true;
            });
            Do("explicit Main Replay workspace choice exercises process refusal",()=>Activate("native_studio_theatre"));
            Wait("refused Replay workspace remains in native hub with actionable failure",()=>
            {
                if (_workspaceRequests.Count != 2) return false;
                Require(_workspaceRequests[1] && ReferenceEquals(mainHub, ReadStatic<NativeStudioEntryPage>("_studio"))
                    && Read<string>(mainHub, "_status")!.Contains("QUALIFICATION RETRY", StringComparison.Ordinal)
                    && !_window!.HasScene && !DemoPlayback.IsActive,
                    "Replay choice passes replay=true and failed launch retains Main hub");
                return true;
            });
            Do("retry explicit Main Replay workspace choice",()=>Activate("native_studio_theatre"));
            Wait("Replay retry is captured once with correct workspace choice",()=>
            {
                if (_workspaceRequests.Count != 3) return false;
                Require(_workspaceRequests.SequenceEqual(new[] { false, true, true })
                    && ReferenceEquals(mainHub, ReadStatic<NativeStudioEntryPage>("_studio"))
                    && Read<string>(mainHub, "_status")!.Contains("SEPARATE APP", StringComparison.Ordinal)
                    && !_window!.HasScene && !DemoPlayback.IsActive,
                    "Main workspace launch sequence is exactly Map, refused Replay, accepted Replay");
                return true;
            });
            Do("Main destination Back returns through native Home action",()=>Activate("native_studio_chooser_back"));
            Wait("native Main Home resumes after destination hub closes",()=>ReadStatic<NativeStudioEntryPage>("_studio")==null
                && ReadStatic<RmlUiLauncherPages>("_launcherPages") is {Suspended:false,Page:RmlUiMenuPage.Home});
            Do("open Main viewer library directly through Theatre header",()=>Activate("nav_theatre"));
        }
        Wait("actual native library scans both isolated archives",()=>
        {
            menu=ReadStatic<RmlUiMenuController>("_controller");
            library=Read<RmlUiTheatreIndex>(menu,"_theatre");
            return menu is {Busy:false} && library?.TotalCount==2
                && ReadStatic<RmlUiLauncherPages>("_launcherPages") is {Suspended:false,Page:RmlUiMenuPage.Theatre};
        });
        Do("select first recording through native library row",()=>Activate("theatre_entry0"));
        Do("Watch first recording through native library action",()=>
        {
            firstPath=library!.Selected!.Value.Path;
            Activate("theatre_watch");
        });
        Wait("first library Watch owns an actual replay picture",()=>DemoPlayback.IsActive&&Page is {IsOpen:true}
            &&DemoPlayback.LogicalPath==firstPath&&_lastWorldFrame==_observedFrames);
        if (!_studioRole) { PlanMenuAudioScenePause(); PlanViewerCapabilities("first", () => firstPath); }
        Do("capture first playback and return using Library",()=>
        {firstPlayer=DemoPlayback.PlaybackOwner;Capture("first-library-watch");Activate("replay_back");});
        Wait("normal teardown restores same library with Watch and Editor enabled",Returned);
        Do("capture returned library and select different recording",()=>
        {Capture("returned-library");Activate("theatre_entry1");});
        Do("Watch second recording through restored native library",()=>
        {
            secondPath=library!.Selected!.Value.Path;
            Require(secondPath!=firstPath,"a different archive is selected after returning");
            Activate("theatre_watch");
        });
        Wait("second library Watch presents a fresh actual player",()=>
        {
            if(!DemoPlayback.IsActive||Page is not {IsOpen:true}||DemoPlayback.LogicalPath!=secondPath||_lastWorldFrame!=_observedFrames)return false;
            secondPlayer=DemoPlayback.PlaybackOwner;
            Require(!ReferenceEquals(firstPlayer,secondPlayer),"second recording owns a fresh player");
            return true;
        });
        if (!_studioRole) PlanViewerCapabilities("second", () => secondPath);
        Do("capture second playback and close through Library",()=>{Capture("second-library-watch");Activate("replay_back");});
        Wait("second normal teardown restores same library again",Returned);
        if (_studioRole)
        {
            Do("reopen selected path in Editor through restored Studio library",()=>Activate("theatre_studio"));
            Wait("same-path Editor captures fresh ready player and actual world picture",()=>
            {
                if(!DemoPlayback.IsActive||Page==null||DemoPlayback.LogicalPath!=secondPath||_lastWorldFrame!=_observedFrames
                    ||Editor?.Snapshot().Live.Current!=true||Host!.CurrentInputDocument!=Page.Document)return false;
                Require(!ReferenceEquals(secondPlayer,DemoPlayback.PlaybackOwner),"same-path Editor owns a fresh player");
                return true;
            });
            Do("capture reopened persistent Editor and select Camera",()=>{Capture("same-path-library-editor");Activate("studio_tab_camera");});
            Wait("persistent Editor keeps actual playback controls",()=>Page!=null&&Host!.CurrentInputDocument==Page.Document);
            PlanRml97StudioFeatures();
        }
        else
        {
            Vector2i resized = default;
            Do("resize returned Main library before third Watch",()=>
            { resized = new(_window!.ClientSize.X - 32, _window.ClientSize.Y - 18); _window.ClientSize = resized; });
            Wait("resized native Main library owns current framebuffer",()=>_observedClient==resized && Returned());
            Do("Watch selected path again without inline editor",()=>Activate("theatre_watch"));
            Wait("third Main Watch owns fresh actual player and resized world picture",()=>
            {
                if(!DemoPlayback.IsActive||Page is not {IsOpen:true}||DemoPlayback.LogicalPath!=secondPath
                    ||_lastWorldFrame!=_observedFrames||_observedClient!=resized)return false;
                Require(!ReferenceEquals(secondPlayer,DemoPlayback.PlaybackOwner),"third Main Watch owns a fresh player");
                return true;
            });
            PlanViewerCapabilities("third-resized", () => secondPath);
            Do("capture third Main viewer playback",()=>Capture("same-path-main-watch-resized"));
        }
        Do("close third playback through normal Library action",()=>{RequireDetachedExportActiveAtClose();Activate("replay_back");});
        Wait("third normal teardown leaves library ready for another replay",Returned);
        if (!_studioRole) PlanMenuAudioResume();
        if (_studioRole) { PlanDetachedExportCompletion(); PlanRml97LibraryPromotion(); }
        Do("capture final ready library",()=>Capture("final-returned-library"));

        bool Returned()
        {
            if(DemoPlayback.IsActive||_window!.HasScene||Page!=null
                ||ReadStatic<RmlUiLauncherPages>("_launcherPages") is not {Suspended:false,Page:RmlUiMenuPage.Theatre})return false;
            Require(ReferenceEquals(menu,ReadStatic<RmlUiMenuController>("_controller"))
                &&ReferenceEquals(library,Read<RmlUiTheatreIndex>(menu,"_theatre")),"the original menu and library owners survive return");
            Require(!library!.Launched&&menu!.Snapshot.Bindings["theatre_watch_enabled"].Boolean
                &&menu.Snapshot.Bindings["theatre_edit_enabled"].Boolean,"return releases launch fence and restores Watch/Editor");
            Require(menu!.Snapshot.Bindings["theatre_advanced_tools"].Boolean == _studioRole
                && menu.Snapshot.Bindings["theatre_studio"].Text == (_studioRole ? "EDIT REPLAY" : "OPEN IN PRIME STUDIO"),
                "returned library retains correct Main versus Studio capability and launch wording");
            return true;
        }
    }
    private bool ObserveWorkspaceLaunch(bool replay, out string? error)
    {
        _workspaceRequests.Add(replay);
        bool refused = replay && ++_replayWorkspaceAttempts == 1;
        error = refused ? "QUALIFICATION RETRY // PRIME STUDIO PROCESS REFUSED" : null;
        return !refused;
    }

    private void PlanViewerCapabilities(string name, Func<string> replayPath)
    {
        ReplayCameraKeyframe[] keys = Array.Empty<ReplayCameraKeyframe>();
        bool constantSpeed = false, bookmarkRequested = false;
        ReplayCameraInterpolation interpolation = default;
        ReplayCameraEase ease = default;
        string? cameraHash = null;
        Do(name + " Main playback has viewer-only backend ownership", () =>
        {
            var page = Page!;
            Require(!Shell.StudioWindow && !ReplayInput.EditingAllowed, "Main role disables replay edit hotkeys");
            foreach (string field in new[] { "_bookmarkBackend", "_rangeBackend", "_cameraBackend", "_exportBackend", "_editorBackend" })
                Require(Read<object>(page, field) == null, "Main playback has no editing backend: " + field);
            foreach (string id in new[] { "replay_bookmarks", "replay_camera_editor", "replay_export_open", "replay_range_controls", "replay_range_summary" })
                Require(!Host!.TryGetElementBounds(page.Document, id, out _, out _, out _, out _),
                    "Main viewer hides advanced control and summary: " + id);
            foreach (TheatrePlaybackAction action in Enum.GetValues<TheatrePlaybackAction>())
                if (action >= TheatrePlaybackAction.Bookmarks)
                    Require(!page.HandleIntent(new(RmlUiIntentKind.ReplayAction, (int)action, page.Document,
                        (ulong)(_observedFrames * 1000 + (int)action + 1))), "Main typed action cannot acquire editing owner: " + action);
            Require(!page.TryOpenEditor() && Editor == null && Host!.CurrentInputDocument == page.Document,
                "direct native editor request cannot bypass Main capability");
            Require(Host!.FocusDocument(page.Document, "replay_studio"), "active Main Studio handoff remains enabled");
            Focus("replay_pause");
            keys = ReplayCamera.Track.Keys.ToArray();
            constantSpeed = ReplayCamera.TrackConstantSpeed;
            interpolation = ReplayCamera.TrackInterpolation;
            ease = ReplayCamera.TrackEase;
            bookmarkRequested = ReplayCamera.BookmarkRequested;
            string sidecar = replayPath() + ".camera";
            cameraHash = File.Exists(sidecar) ? Hash(sidecar) : null;
            page.Viewport.Focus();
            foreach (Keys key in new[] { Keys.B, Keys.Delete, Keys.Minus, Keys.Equal, Keys.Semicolon, Keys.Apostrophe,
                InputSettings.ReplayConstantSpeedKey, InputSettings.ReplayInterpolationKey, InputSettings.ReplayEasingKey }.Distinct())
            {
                if (key == Keys.Unknown) continue;
                page.Viewport.KeyDown(key); page.Viewport.KeyUp(key);
            }
            page.Viewport.Release();
            Require(ReplayCamera.BookmarkRequested == bookmarkRequested,
                "Main viewport hotkeys cannot queue a new camera bookmark");
        });
        WaitFrames(3, name + " Main replay hotkeys leave actual track and sidecar unchanged", () =>
        {
            Require(DemoPlayback.IsActive && Page is { IsOpen: true } && Editor == null
                && _lastWorldFrame == _observedFrames, "actual viewer world picture remains current after hotkeys");
            Require(ReplayCamera.Track.Keys.SequenceEqual(keys) && ReplayCamera.TrackConstantSpeed == constantSpeed
                && ReplayCamera.TrackInterpolation == interpolation && ReplayCamera.TrackEase == ease,
                "Main viewport and configured replay shortcuts cannot mutate camera track authoring state");
            string sidecar = replayPath() + ".camera";
            Require(cameraHash == (File.Exists(sidecar) ? Hash(sidecar) : null), "Main edit shortcuts leave exact sidecar bytes unchanged");
        });
    }

    private void HeldViewportResize()
    {
        Vector2i originalSize=default;
        (double X,double Y) pointer=default;
        ReplayCameraKeyframe pose=default;
        long picture=0;
        string? sidecar=null;
        bool CurrentPicture(out ReplayCameraKeyframe key,out long revision)
        {
            key=default;revision=0;
            return _lastWorldFrame==_observedFrames&&DemoPlayback.PresentationScene is{} scene
                &&scene.TryCaptureNativeReplayCamera(ReplayController.CurrentFrame,out key,out revision);
        }
        (double X,double Y) ViewportCenter()
        {
            Require(Host!.TryGetElementBounds(Page!.Document,"replay_viewport",out float x,out float y,out float width,out float height)
                &&width>40&&height>40,"actual native replay viewport has usable bounds");
            var window=_window!;
            return ((x+width/2)*window.ClientSize.X/window.FramebufferSize.X,
                (y+height/2)*window.ClientSize.Y/window.FramebufferSize.Y);
        }
        Do("hold actual native replay viewport camera before resize",()=>
        {
            originalSize=_window!.ClientSize;
            sidecar=File.Exists(_replay+".camera")?Hash(_replay+".camera"):null;
            Require(CurrentPicture(out _,out picture),"successful paused camera picture before native pointer input");
            pointer=ViewportCenter();
            RmlUiOpenGlSmoke.PointerButton(MouseButton.Left,pointer.X,pointer.Y,true);
        });
        Wait("held native viewport owns a successful free-camera picture",()=>
        {
            if(Page?.Viewport.Focused!=true||!CurrentPicture(out var key,out long revision)||revision<=picture)return false;
            pose=key;picture=revision;return true;
        });
        Do("move held native viewport through production pointer routing",()=>RmlUiOpenGlSmoke.PointerMoved(pointer.X+20,pointer.Y));
        Wait("actual presented camera rotates from held native pointer movement",()=>
        {
            if(!CurrentPicture(out var key,out long revision)||revision<=picture||key.Rotation==pose.Rotation)return false;
            pose=key;picture=revision;return true;
        });
        Do("resize actual client while camera drag and unapplied pointer delta remain held",()=>
        {
            Require(CurrentPicture(out pose,out picture),"last actual camera pose before resize");
            RmlUiOpenGlSmoke.PointerMoved(pointer.X+40,pointer.Y);
            _window!.ClientSize=new(originalSize.X-32,originalSize.Y-18);
        });
        Wait("actual resize retires held camera before its next published picture",()=>
        {
            if(_window!.ClientSize==originalSize||_observedClient!=_window.ClientSize
                ||!CurrentPicture(out var key,out long revision)||revision<=picture)return false;
            Require(Page?.Viewport.Focused==false,"resize released native viewport ownership");
            Require(key==pose,"pre-resize queued rotation never reached the actual presented camera");
            picture=revision;return true;
        });
        Do("move pointer after resize without a fresh button press",()=>RmlUiOpenGlSmoke.PointerMoved(pointer.X+80,pointer.Y+10));
        int stablePictures=0;
        Wait("four real resized pictures reject the old held pointer gesture",()=>
        {
            if(!CurrentPicture(out var key,out long revision)||revision<=picture)return false;
            Require(Page?.Viewport.Focused==false&&key==pose,"orphaned pointer movement leaves actual camera unchanged");
            picture=revision;return ++stablePictures>=4;
        });
        Do("release old button and acquire the resized native viewport again",()=>
        {
            RmlUiOpenGlSmoke.PointerButton(MouseButton.Left,pointer.X+80,pointer.Y+10,false);
            pointer=ViewportCenter();
            RmlUiOpenGlSmoke.PointerButton(MouseButton.Left,pointer.X,pointer.Y,true);
        });
        Wait("fresh press reacquires actual resized camera input",()=>
        {
            if(Page?.Viewport.Focused!=true||!CurrentPicture(out var key,out long revision)||revision<=picture)return false;
            pose=key;picture=revision;return true;
        });
        Do("move fresh held pointer in resized viewport",()=>RmlUiOpenGlSmoke.PointerMoved(pointer.X+20,pointer.Y));
        Wait("fresh resized pointer gesture rotates an actual new camera picture",()=>
        {
            if(!CurrentPicture(out var key,out long revision)||revision<=picture||key.Rotation==pose.Rotation)return false;
            Require(sidecar==(File.Exists(_replay+".camera")?Hash(_replay+".camera"):null),"viewport resize and reentry leave camera sidecar unchanged");
            Capture("held-camera-resize-and-reentry");return true;
        });
        Do("release resized camera and restore requested client size",()=>
        {
            RmlUiOpenGlSmoke.PointerButton(MouseButton.Left,pointer.X+20,pointer.Y,false);
            _window!.ClientSize=originalSize;
        });
        Wait("restored requested client has a released camera and actual world picture",()=>
            _window!.ClientSize==originalSize&&_observedClient==originalSize&&Page?.Viewport.Focused==false&&CurrentPicture(out _,out _));
    }
    private void OpenCamera()
    {
        Do("open actual native camera controls",()=>Activate(_studioRole?"studio_tab_camera":"replay_camera_editor"));
        if (_studioRole) Do("refresh actual persistent camera review",()=>Activate("studio_refresh"));
        Wait("camera modal has current actual presented pose",()=>Camera?.Snapshot().Current==true);
    }
    private void OpenTimeline()
    {
        if (_studioRole)
        {
            Do("refresh persistent Studio timeline owners",()=>Activate("studio_refresh"));
            Do("focus persistent Studio timeline",()=>Focus("replay_timeline_surface"));
            Wait("persistent timeline and actual world picture were successfully swapped",ReadyTimeline);
            return;
        }
        Do("open tools on existing production camera backend",()=>Activate("replay_camera_tools_open"));
        Wait("same production camera tools context is current",()=>TryTools()?.Snapshot().Current==true);
        Do("open native graphical timeline",()=>Activate("replay_camera_tools_timeline"));
        Wait("timeline bounds and exact actual world picture were successfully swapped",ReadyTimeline);
    }
    private void CloseTimelineToPlayback()
    {
        if (_studioRole)
        {
            Do("return focus to actual persistent Studio Camera tab",()=>Activate("studio_tab_camera"));
            Wait("persistent workspace keeps playback foreground",StudioWorkspaceForeground);
            return;
        }
        Do("close native timeline through its authored action",()=>Activate("replay_timeline_close"));
        Do("close native tools through its authored action",()=>Activate("replay_camera_tools_close"));
        Do("close native camera through its authored action",()=>Activate("replay_camera_close"));
        Wait("original playback page regains actual foreground",()=>Page!=null&&Host!.CurrentInputDocument==Page.Document);
    }
    private void SeekTimeline(Func<uint> frame,string name)
    {
        Do("focus exact native timeline seek: "+name,()=>
        {CaptureTimelineGate("before-focus:"+name);Host!.SetField(Host.CurrentInputDocument,_studioRole?"studio_frame":"replay_timeline_frame",frame().ToString(CultureInfo.InvariantCulture));Focus(_studioRole?"studio_seek":"replay_timeline_seek");CaptureTimelineGate("after-focus-before-next-draw:"+name);});
        WaitFrames(3,"actual focused seek control is drawn before Enter: "+name,()=>{});
        Do("request exact native timeline seek: "+name,()=>{CaptureTimelineGate("after-three-swaps-before-enter:"+name);Capture("before-seek-"+name.Replace(' ','-'));Host!.Input.Key(2,true);Host.Input.Key(2,false);});
        Wait("settled actual world seek: "+name,()=>ReplayController.CurrentFrame==frame()&&!ReplayController.IsSeeking&&_lastWorldFrame==_observedFrames,45000);
    }
    private void Drag(bool commit,string name)
    {
        Do("focus actual camera marker before "+name+" drag",()=>Focus("replay_timeline_key_0"));
        Wait("actual presented marker ready for "+name+" drag",ReadyTimeline);
        Do("begin ephemeral native "+name+" drag",()=>
        {
            var ticket=Ticket!;var key=ticket.Keys[0];
            _beforeDrag=Hash(_replay+".camera");_beforeFrames=ReplayCamera.Track.Keys.Select(k=>k.Frame).ToArray();
            // Move the group left from its earliest key. If the first key is already near
            // zero, first move right; choose a whole-frame distance fitting all actual keys.
            long distance=Math.Max(10,(long)Math.Ceiling(ticket.Window.Span*14.0/ticket.CameraLane.Width));
            long delta=_beforeFrames[0]>distance?-distance:distance;
            Require(_beforeFrames[^1]+delta<=ReplayController.DurationFrames&&_beforeFrames[0]+delta>=0,"bounded group drag fits the replay");
            _expectedFrames=_beforeFrames.Select(f=>(uint)(f+delta)).ToArray();
            double startX=key.Box.X+key.Box.Width/2, y=key.Box.Y+key.Box.Height/2;
            double endX=startX+delta/(double)Math.Max(1u,ticket.Window.Span)*ticket.CameraLane.Width;
            var metrics=ticket.Metrics;
            _dragStart=(startX*metrics.ClientWidth/metrics.FramebufferWidth,y*metrics.ClientHeight/metrics.FramebufferHeight);
            _dragEnd=(endX*metrics.ClientWidth/metrics.FramebufferWidth,_dragStart.Y);
            RmlUiOpenGlSmoke.PointerButton(MouseButton.Left,_dragStart.X,_dragStart.Y,true);
            RmlUiOpenGlSmoke.PointerMoved(_dragEnd.X,_dragEnd.Y);
        });
        Wait("native drag preview exists without file publication: "+name,()=>
        {
            if(Timeline?.Dragging!=true)return false;
            Require(Hash(_replay+".camera")==_beforeDrag,"drag preview leaves saved bytes unchanged");
            Require(ReplayCamera.Track.Keys.Select(k=>k.Frame).SequenceEqual(_beforeFrames),"drag preview leaves retained track unchanged");return true;
        });
        if(!commit)
        {
            Do("cancel actual drag on native input release",()=>{Capture("ephemeral-drag-preview");RmlUiOpenGlSmoke.NativeFocusChanged(false);});
            Wait("cancelled real drag releases ephemeral preview",()=>Timeline?.Dragging==false);
            Do("cancelled drag preserves exact source-bound file bytes",()=>
            {Require(Hash(_replay+".camera")==_beforeDrag,"cancel kept exact file");RmlUiOpenGlSmoke.PointerButton(MouseButton.Left,_dragEnd.X,_dragEnd.Y,false);Activate("replay_timeline_refresh");});
            Wait("explicit timeline review after drag cancel",ReadyTimeline);
        }
        else
        {
            Do("release actual native group drag into existing confirmation",()=>RmlUiOpenGlSmoke.PointerButton(MouseButton.Left,_dragEnd.X,_dragEnd.Y,false));
            Wait("existing full before/after camera confirmation owns modal",()=>Tools.Snapshot().Pending is{Kind:TheatreCameraToolsChange.Move});
            Do("review actual complete group proposal without publishing",()=>
            {
                Require(Hash(_replay+".camera")==_beforeDrag,"review before confirm preserves original file");
                var edit=Tools.Snapshot().Pending!;_expectedFrames=edit.After.Select(k=>k.Frame).ToArray();
                Require(!edit.Before.SequenceEqual(edit.After)&&edit.Before.Length==3&&edit.After.Length==3,"actual pointer produced one bounded three-key group proposal");
                Capture("actual-group-confirmation");Activate("replay_camera_tools_confirm_apply");
            });
            Wait("confirmed actual group edit reloads durable source-bound camera sidecar",()=>
            {
                if(!ReplayCamera.Track.Keys.Select(k=>k.Frame).SequenceEqual(_expectedFrames))return false;
                var loaded=new ReplayCameraTrack();Require(loaded.Load(_replay)&&loaded.Keys.Select(k=>k.Frame).SequenceEqual(_expectedFrames),"binary camera sidecar reload matches actual confirmed group");
                Require(Hash(_replay+".camera")!=_beforeDrag,"confirmed edit published new exact bytes");_beforeDrag=Hash(_replay+".camera");return true;
            });
            Do("explicitly refresh same timeline after accepted track edit",()=>Activate("replay_timeline_refresh"));Wait("post-confirm actual timeline picture",ReadyTimeline);
        }
    }
    private void Wait(string name,Func<bool> predicate,int timeout=20000)=>_steps.Enqueue(new(name,predicate,timeout));
    private void Do(string name,Action action)=>Wait(name,()=>{action();return true;});
    private void WaitFrames(int count,string name,Action check){int left=count;Wait(name,()=>{if(--left>0)return false;check();return true;});}
    private static T? ReadStatic<T>(string field) where T:class
        =>(T?)typeof(RmlUiOpenGlSmoke).GetField(field,StaticPrivate)!.GetValue(null);
    private RmlUiHost? Host=>ReadStatic<RmlUiHost>("_host");
    private TheatrePlaybackPagePresenter? Page=>(TheatrePlaybackPagePresenter?)typeof(RmlUiOpenGlSmoke).GetField("_playback",StaticPrivate)!.GetValue(null);
    private TheatreCameraController? Camera=>Read<TheatreCameraController>(Page,"_cameraEditor");
    private TheatreEditorController? Editor=>Read<TheatreEditorController>(Page,"_editor");
    private TheatreCameraToolsController? TryTools()=>Read<TheatreCameraToolsController>(Page,"_cameraTools");
    private TheatreCameraToolsController Tools=>TryTools()??throw new InvalidOperationException("Actual camera tools controller is absent.");
    private TheatreTimelineViewState? Timeline=>Read<TheatreTimelineViewState>(Page,"_timeline");
    private TimelineRenderTicket? Ticket=>Read<TimelineRenderTicket>(Timeline,"_presented");
    private static T? Read<T>(object? owner,string field)where T:class=>owner==null?null:(T?)owner.GetType().GetField(field,Private)!.GetValue(owner);
    private void CaptureTimelineGate(string stage)
    {
        var page=Page;var host=Host;var state=Timeline;var ticket=Ticket;var snapshot=TryTools()?.Snapshot();
        object? Value(object? owner,string field)=>owner?.GetType().GetField(field,Private)?.GetValue(owner);
        var modal=(RmlUiDocumentToken?)Value(page,"_timelineModal")??default;
        var pages=(RmlUiPageManager?)Value(page,"_pages");
        var reviewed=(TheatreCameraToolsContext?)Value(state,"_review");
        var metrics=(TimelineMetrics?)Value(page,"_timelineMetrics");
        var revision=(long?)Value(state,"_viewRevision");
        object? Box(string id)=>host!=null&&host.TryGetElementBounds(modal,id,out float x,out float y,out float w,out float h)?new{X=x,Y=y,Width=w,Height=h}:null;
        object Bounds(TimelineBox box)=>new{box.X,box.Y,box.Width,box.Height};
        object? TicketProperty(string name)=>ticket?.GetType().GetProperty(name,Private)?.GetValue(ticket);
        var row=new{Stage=stage,Swap=_observedFrames,Frame=ReplayController.CurrentFrame,Seeking=ReplayController.IsSeeking,
            Focused=_window?.IsFocused,Client=_window?.ClientSize.ToString(),Framebuffer=_window?.FramebufferSize.ToString(),HostActive=host?.Active,
            PageOpen=page?.IsOpen,Modal=modal,Top=pages?.Top,Input=host?.CurrentInputDocument,ModalAlive=host?.IsAlive(modal),FocusedElement=host?.FocusedElement(),
            PresenterMetrics=metrics,StateViewRevision=revision,TicketPaint=ticket?.Paint,TicketViewRevision=ticket?.ViewRevision,TicketDocument=ticket?.Document,
            TicketMetrics=ticket?.Metrics,TicketCamera=ticket==null?null:Bounds(ticket.CameraLane),TicketKeys=ticket?.Keys.Length,
            TicketClip=TicketProperty("Clip") is TimelineBox clip?Bounds(clip):null,
            TicketGo=TicketProperty("SeekControl") is TimelineBox go?Bounds(go):null,
            Current=snapshot?.Current,Pending=snapshot?.Pending!=null,SameReviewedContext=snapshot!=null&&ReferenceEquals(reviewed,snapshot.Context),
            SameReviewedKeys=snapshot!=null&&reviewed!=null&&snapshot.Context.Camera.Keys.SequenceEqual(reviewed.Camera.Keys),
            Owner=Id(snapshot?.Context.Owner),CurrentPlayer=Id(DemoPlayback.PlaybackOwner),Transport=Id(snapshot?.Context.Camera.Transport),CurrentTransport=Id(DemoPlayback.Session.Transport),
            SceneGeneration=snapshot?.Context.Camera.SceneGeneration,SeekGeneration=snapshot?.Context.Camera.SeekGeneration,ReviewFrame=snapshot?.Context.Camera.Frame,
            PanelBounds=Box("replay_timeline_panel"),CameraBounds=Box("replay_timeline_camera"),GoBounds=Box("replay_timeline_seek"),
            Error=Value(page,"_timelineProblem"),Notice=state?.Notice,
            CameraInsideFramebuffer=ticket!=null&&_window!=null&&ticket.CameraLane.Inside(new(0,0,_window.FramebufferSize.X,_window.FramebufferSize.Y))};
        _timelineGates.Add(row);
        File.WriteAllText(Path.Combine(_output,"timeline-gate-"+_timelineGates.Count.ToString("000",CultureInfo.InvariantCulture)+".json"),JsonSerializer.Serialize(row,JsonOptions));
    }
    private bool ReadyTimeline()
    {
        if(_window is not{IsFocused:true}||Host==null||Page==null||Timeline==null||Ticket is not{} ticket||TryTools()?.Snapshot() is not{Current:true} state)return false;
        return Timeline.PresentedCurrent(state,Host.CurrentInputDocument,ticket.Metrics,true)
            &&ticket.Metrics.FramebufferWidth==_window.FramebufferSize.X&&ticket.Metrics.FramebufferHeight==_window.FramebufferSize.Y
            &&ticket.Metrics.ClientWidth==_window.ClientSize.X&&ticket.Metrics.ClientHeight==_window.ClientSize.Y
            &&_lastWorldFrame==_observedFrames;
    }
    private void Focus(string id)
    {
        var host=Host??throw new InvalidOperationException("Native host absent.");
        Require(host.FocusDocument(host.CurrentInputDocument,id),"native enabled focus: "+id);
    }
    private void Activate(string id)
    {
        Focus(id);var host=Host!;host.Input.Key(2,true);host.Input.Key(2,false);
    }
    private void AccessiblePress(string id)
    {
        var host=Host!;var doc=host.CurrentInputDocument;
        byte[] buffer=new byte[1024*1024+1];int length=AccessibilitySnapshot(doc.DocumentId,buffer,buffer.Length);
        Require(length>0&&length<buffer.Length,"bounded actual semantic projection");
        using var json=JsonDocument.Parse(buffer.AsMemory(0,length));var root=json.RootElement;
        var node=root.GetProperty("nodes").EnumerateArray().First(row=>row.GetProperty("id").GetString()==id);
        Require(AccessibilityAction(doc.Generation,doc.DocumentId,root.GetProperty("revision").GetUInt64(),node.GetProperty("key").GetString()!,1)==1,"native semantic press: "+id);
    }
    private void Capture(string name)
    {
        var window=_window!;int width=window.FramebufferSize.X,height=window.FramebufferSize.Y;
        int readBuffer=GL.GetInteger(GetPName.ReadBuffer),readFbo=GL.GetInteger(GetPName.ReadFramebufferBinding),alignment=GL.GetInteger(GetPName.PackAlignment);
        byte[] pixels=new byte[checked(width*height*3)];
        try
        {
            GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer,0);GL.ReadBuffer(ReadBufferMode.Front);GL.PixelStore(PixelStoreParameter.PackAlignment,1);
            GL.ReadPixels(0,0,width,height,PixelFormat.Rgb,PixelType.UnsignedByte,pixels);
        }
        finally{GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer,readFbo);GL.ReadBuffer((ReadBufferMode)readBuffer);GL.PixelStore(PixelStoreParameter.PackAlignment,alignment);}
        string path=Path.Combine(_output,name+".ppm");using(var stream=File.Create(path))
        {stream.Write(Encoding.ASCII.GetBytes($"P6\n# ACTUAL PRODUCTION SHELL WINDOW / ISOLATED OWNED ARCHIVE\n{width} {height}\n255\n"));for(int y=height-1;y>=0;y--)stream.Write(pixels,y*width*3,width*3);}
        _captures.Add(new{Name=name,Path=path,Width=width,Height=height,Swap=_observedFrames,Frame=ReplayController.CurrentFrame,Sha256=Hash(path),
            TimelineTicket=Ticket is{} ticket?new{ticket.Paint,ticket.ViewRevision,ticket.Document,ticket.Metrics,ticket.Window,
                Keys=ticket.Keys.Select(row=>new{row.Frame,row.Box}),Lanes=ticket.Lanes.Select(row=>new{row.Id,row.Box,row.Glyph.Lane,row.Glyph.Count,row.Glyph.First,row.Glyph.Last})}:null});
    }
    private string Facts()=>"swap="+_observedFrames+" active="+DemoPlayback.IsActive+" frame="+ReplayController.CurrentFrame+" seek="+ReplayController.IsSeeking
        +" native="+(Page!=null)+" host="+Host?.Active+" nativeRequested="+typeof(RmlUiOpenGlSmoke).GetField("Requested",StaticPrivate)?.GetValue(null)
        +" shellCanPresent="+Shell.RmlUiCanPresent+" visible="+_window?.IsVisible+" client="+_observedClient+" framebuffer="+_observedFramebuffer
        +" focus="+_window?.IsFocused+" element="+Host?.FocusedElement()+" problem="+DemoPlayback.LastError
        +" camera="+Camera?.Snapshot().Error+" tools="+TryTools()?.Snapshot().Error+" timeline="+Timeline?.Notice+" "+LibraryFacts();
    private string LibraryFacts()
    {
        var menu=ReadStatic<RmlUiMenuController>("_controller");
        var layout=ReadStatic<RmlUiLauncherPages>("_launcherPages");
        var library=Read<RmlUiTheatreIndex>(menu,"_theatre");
        string status=menu?.Snapshot.Bindings.TryGetValue("theatre_status",out var value)==true?value.Text:"";
        object? pending=typeof(Shell).GetField("_pending",StaticPrivate)?.GetValue(null);
        object? played=typeof(Shell).GetField("_played",StaticPrivate)?.GetValue(null);
        string basic="menu="+Id(menu)+" layout="+layout?.Page+" suspended="+layout?.Suspended
            +" theatre="+menu?.TheatreOpen+" library="+Id(library)+" launched="+library?.Launched
            +" busy="+menu?.Busy+" joining="+menu?.Joining+" status="+status
            +" pending="+pending+" played="+played+" matchError="+MatchStart.LastError;
        if (!_libraryDiagnostics) return basic;
        var selected=library?.Selected;
        var file=selected.HasValue?new FileInfo(selected.Value.Path):null;
        return basic+" folder="+DemoLibrary.Directory+" selected="+selected?.Path+" playable="+selected?.Playable
            +" expectedBytes="+selected?.Bytes+" actualBytes="+(file?.Exists==true?file.Length:0)
            +" expectedModified="+selected?.LastWriteTicks+" actualModified="+(file?.Exists==true?file.LastWriteTimeUtc.Ticks:0)
            +" link="+file?.LinkTarget;
    }
    private static int Id(object? value)=>value==null?0:System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value);
    private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    private static string Hash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private sealed class NoThumbnailJobs:IThumbnailHost
    {
        public System.Threading.Tasks.Task<int> RenderAsync(IReadOnlyList<string> rooms,Action<string> report)
        {report("Replay-window diagnostic excludes unrelated thumbnail jobs.");return System.Threading.Tasks.Task.FromResult(0);}
    }
    [DllImport("ProjectPrime.RmlUi.Native",CallingConvention=CallingConvention.Cdecl,EntryPoint="pp_rmlui_document_accessibility_snapshot")]
    private static extern int AccessibilitySnapshot(ulong document,[Out]byte[] buffer,int capacity);
    [DllImport("ProjectPrime.RmlUi.Native",CallingConvention=CallingConvention.Cdecl,EntryPoint="pp_rmlui_accessibility_action")]
    private static extern int AccessibilityAction(ulong generation,ulong document,ulong revision,[MarshalAs(UnmanagedType.LPUTF8Str)]string key,int action);
}
