using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MphRead;
using MphRead.Mods;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.RmlUi.Core;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Pages.Community;
using MphRead.Mods.Launcher.RmlUi.Render;
using MphRead.Mods.MapGen;
using MphRead.Mods.Render;
using OpenTK.Graphics.OpenGL;

/// <summary>Real native Community buttons and composed create form, with held synthetic installation.</summary>
internal static class CommunityHostingGlAcceptance
{
    private const string Scope = "Actual native Community Install & Host, owner registration, exact one-shot identity, cancellation/root retirement, composed create form and final framebuffer. Synthetic catalog/installer and empty private filesystem only; no network, package IO, server allocation, game assets or physical Cocoa input qualification.";
    internal static void Run(RmlUiHost host, int width, int height)
    {
        string output = Path.GetFullPath(Environment.GetEnvironmentVariable("PROJECT_PRIME_RMLUI_HOST_OUTPUT")
            ?? Path.Combine("artifacts", "rmlui-community-host", width + "x" + height));
        Directory.CreateDirectory(output);
        using var isolation = new Isolation();
        var fixture = new Fixture(); var captures = new List<string>(); int checks = 0;
        var pages = new RmlUiPageManager(host);
        var page = new NativeCommunityDiscoverPage(host, pages, fixture, fixture, fixture, clipboard: _ => false);
        RmlUiLauncherPages? layout = null; RmlUiMenuController? controller = null; long shown = -1;
        var services = new Services(); NativeCommunityHostSelection? selected = null;
        host.Input.SetFramebufferScale(1, 1);
        try
        {
            Check(GameFiles.Ready, "empty private fixture configures availability without game assets");
            page.Open(); Wait(() => Has("SYNTHETIC HOST ARENA"), "synthetic catalog loads");
            Activate("native_community_row0", RmlUiIntentKind.CommunitySelect);
            Activate("native_community_install_host", RmlUiIntentKind.CommunityDetailAction);
            Wait(() => fixture.Calls == 1, "actual Install & Host reaches held installer");
            Check(fixture.Expected == fixture.Entry && fixture.Root == GameFiles.Root && fixture.Registered == 0,
                "installation captures exact selected immutable revision and configured root");
            Check(!host.FocusDocument(pages.Top, "native_community_install_host") && !page.TryTakeHostRequest(out _),
                "pending verification disables duplicate host and produces no optimistic handoff");
            Capture("verification-pending");
            fixture.Pending.SetResult(fixture.Package());
            Wait(() => fixture.Registered == 1, "verified package registers on native owner");
            Check(fixture.RegisterThread == Environment.CurrentManagedThreadId && page.TryTakeHostRequest(out selected)
                && selected != null && selected.Identity == fixture.Identity && selected.ConfiguredRoot == GameFiles.Root,
                "native page emits exact installed identity after owner registration");
            Check(!page.TryTakeHostRequest(out _), "host target is consumed exactly once");
            Capture("verified-host");

            fixture.Reset(); Activate("native_community_install_host", RmlUiIntentKind.CommunityDetailAction);
            Wait(() => fixture.Calls == 2, "second host verification begins");
            Activate("native_community_cancel_install", RmlUiIntentKind.CommunityCancelWork);
            Check(fixture.Token.IsCancellationRequested, "native cancel retires verification wait");
            fixture.Pending.SetResult(fixture.Package()); Settle();
            Check(!page.TryTakeHostRequest(out _) && fixture.Registered == 1, "late canceled install cannot register or host");
            fixture.Reset(); Activate("native_community_install_host", RmlUiIntentKind.CommunityDetailAction);
            Wait(() => fixture.Calls == 3, "fresh verification starts after cancel");
            GameFiles.Root = Path.Combine(isolation.Root, "retired-root"); Settle();
            Check(fixture.Token.IsCancellationRequested, "configured root change cancels pending native host");
            fixture.Pending.SetResult(fixture.Package()); Settle();
            Check(!page.TryTakeHostRequest(out _) && fixture.Registered == 1, "retired root cannot emit or register late target");
            GameFiles.Root = isolation.Root; Capture("root-retired");
            page.Dispose(); pages.Dispose();

            controller = new RmlUiMenuController(services); layout = new RmlUiLauncherPages(host);
            Check(controller.OpenCreateLobbyForInstalled(selected!), "real menu controller accepts exact installed host target");
            Settle();
            Check(layout.Page == RmlUiMenuPage.Play && Has("COMMUNITY // SELECTED INSTALLED REVISION")
                && Has(fixture.Identity.RoomKey) && Has("4 PLAYER SLOTS"), "composed native create form retains selected map and capacity");
            foreach (string id in new[] { "play_create_name", "play_next_map", "play_next_mode", "play_toggle_host", "play_create_submit", "play_create_cancel" }) Reach(id);
            Capture("create-form");
            host.SetField(layout.Document,"play_create_name","Exact native host review");
            Activate("play_create_submit",RmlUiIntentKind.PlayCreate);
            Wait(()=>services.Creates==1&&!controller.Busy,"native submit reaches only synthetic creation service");
            Check(services.LastRequest is {} request&&request.CommunityMap==selected&&request.RoomKey==fixture.Identity.RoomKey&&request.MaxPlayers==4&&request.Name=="Exact native host review",
                "native submit forwards exact installed revision and capacity");
            Activate("play_next_mode", RmlUiIntentKind.PlayNextMode);
            Activate("play_toggle_host", RmlUiIntentKind.PlayToggleHost);
            Check(Has("LOCAL // DEDICATED") && services.Creates == 1, "native form choices change without creating a server");
            Activate("play_next_map", RmlUiIntentKind.PlayNextMap);
            Check(Has("MPH // BUILT-IN ARENA") && !Has("COMMUNITY // SELECTED INSTALLED REVISION"),
                "native map cycling clears immutable Community target");
            Capture("cycled-map");
            Activate("play_create_cancel", RmlUiIntentKind.PlayCancel);
            Check(layout.Page == RmlUiMenuPage.Play && Has("CREATE LOBBY") && services.Creates == 1,
                "native form cancel returns to browser without allocation");
            Capture("cancelled-form");
            Check(services.Joins==0&&services.Accepted==0&&services.Creates==1,"synthetic create service never joins or accepts a real session");
            Check(GL.GetError() == ErrorCode.NoError, "native hosting journey leaves no GL error"); Save(true, null);
        }
        catch (Exception error) { Capture("failure"); Save(false, error.ToString()); throw; }
        finally { fixture.Pending.TrySetCanceled(); layout?.Dispose(); controller?.Dispose(); page.Dispose(); pages.Dispose(); }

        RmlUiDocumentToken Document() => layout?.Document ?? pages.Top;
        bool Has(string value) => NativeSemanticReadback.Read(host, Document()).ContainsText(value);
        void Check(bool value, string label) { if (!value) throw new InvalidOperationException(label); checks++; Console.WriteLine("COMMUNITY HOST GL PASS " + label); }
        void Frame()
        {
            if (layout == null) page.Refresh();
            else
            {
                var snapshot = controller!.Tick();
                if (snapshot.Revision > shown)
                {
                    foreach (var pair in snapshot.Bindings)
                        if (pair.Value.Kind == RmlUiBindingKind.Text) layout.SetText(pair.Key, pair.Value.Text);
                        else layout.SetBool(pair.Key, pair.Value.Boolean);
                    shown = snapshot.Revision;
                }
                layout.Flush();
            }
            host.Update(); layout?.AfterUpdate();
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0); GL.Viewport(0, 0, width, height);
            GL.Disable(EnableCap.ScissorTest); GL.ClearColor(.025f, .05f, .08f, 1);
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit | ClearBufferMask.StencilBufferBit);
            RmlUiOpenGlFrame.Draw(host, width, height);
        }
        void Settle(int ms = 180) { var timer = Stopwatch.StartNew(); do { Frame(); Thread.Sleep(5); } while (timer.ElapsedMilliseconds < ms); }
        void Wait(Func<bool> ready, string label)
        { var timer = Stopwatch.StartNew(); do { Frame(); if (ready()) { Check(true, label); return; } Thread.Sleep(5); } while (timer.ElapsedMilliseconds < 5000); throw new TimeoutException(label); }
        (float X,float Y,float W,float H) Reach(string id)
        {
            Frame(); Check(host.FocusDocument(Document(), id), "native focus reaches " + id); Settle();
            for (int i=0; i<120; i++)
            {
                bool found=host.TryGetElementBounds(Document(), id, out float x,out float y,out float w,out float h);
                string scroll=layout == null ? "native_community" : "multiplayer_stage";
                host.TryGetElementBounds(Document(),scroll,out float sx,out float sy,out float sw,out float sh);
                if(found&&w>0&&h>0&&x>=0&&y>=0&&x+w<=width+1&&y+h<=height+1
                    && (sw<=0||y>=sy&&y+h<=sy+sh+1)) return(x,y,w,h);
                host.Input.PointerMoved(sx+sw/2,sy+sh/2); host.Input.PointerWheel(i==0?10000:-1); Settle(20);
            }
            throw new InvalidOperationException("Native target cannot be scrolled into framebuffer: "+id);
        }
        void Activate(string id,RmlUiIntentKind kind)
        {
            var b=Reach(id); host.DiscardIntents();
            host.Input.PointerMoved(b.X+b.W/2,b.Y+b.H/2); host.Input.PointerButton(0,b.X+b.W/2,b.Y+b.H/2,true);
            host.Input.PointerButton(0,b.X+b.W/2,b.Y+b.H/2,false); host.Update(); int count=0;
            while(host.TryTakeIntent(out var intent))
            {
                if(intent.Kind==RmlUiIntentKind.StagePreview)continue;
                Check(intent.Kind==kind,"native pointer emits expected "+kind);
                if(layout==null)Check(page.Handle(intent),"Community page accepts native action");
                else { Check(layout.HandleIntent(intent,out var forwarded),"composed page routes native form action");
                    Check(controller!.Dispatch(forwarded.Kind,forwarded.Argument,lobbyName:layout.ReadField("play_create_name",48)),"real controller accepts native form action"); }
                count++;
            }
            Check(count==1,"native pointer emits exactly one action "+id);Settle();
        }
        void Capture(string name)
        {
            Frame();string path=Path.Combine(output,"community-host-"+name+".png");
            Check(ScreenCapture.SaveWindow(width,height,path),"capture "+name); FinalCompositeCapture.WriteEvidence(path,width,height,Scope);
            File.WriteAllText(path+".semantic.json",NativeSemanticReadback.Read(host,Document()).RawJson);captures.Add(path);
        }
        void Save(bool passed,string? error)=>File.WriteAllText(Path.Combine(output,"community-host-summary.json"),JsonSerializer.Serialize(
            new{passed,checks,width,height,scope=Scope,fixture.Calls,fixture.Registered,captures,error},new JsonSerializerOptions{WriteIndented=true}));
    }
    private sealed class Fixture : INativeCommunityReader, INativeCommunityInstaller, INativeCommunityCreatorAuthority
    {
        internal readonly NativeCommunityEntry Entry=new(Guid.NewGuid(),"SYNTHETIC HOST ARENA","SYNTHETIC CREATOR",new string('b',64),3,"BATTLE",2,4){Installed=true,RoomKey="SYNTHETIC_HOST_ARENA"};
        internal MapContentIdentity Identity=>new(Entry.MapId,Entry.RoomKey,MapHash256.Parse(new string('a',64)),MapHash256.Parse(Entry.LatestHash),true);
        internal TaskCompletionSource<NativeCommunityInstalled> Pending=new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Calls,Registered,RegisterThread;internal NativeCommunityEntry Expected;internal string Root="";internal CancellationToken Token;
        internal void Reset()=>Pending=new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal NativeCommunityInstalled Package()=>new(Entry.MapId,Entry.LatestHash,new MapDefinition{MapId=Entry.MapId,Name=Entry.RoomKey},Identity);
        public Task<NativeCommunityPage> BrowseAsync(string query,string sort,int page,CancellationToken cancellation)=>Task.FromResult(new NativeCommunityPage(new[]{Entry},1,1,8));
        public Task<NativeCommunityInstalled> InstallAsync(NativeCommunityEntry selected,CancellationToken cancellation)=>throw new InvalidOperationException("Expected root witness");
        public Task<NativeCommunityInstalled> InstallAsync(NativeCommunityEntry selected,CancellationToken cancellation,string expectedRoot)
        {Expected=selected;Root=expectedRoot;Token=cancellation;Interlocked.Increment(ref Calls);return Pending.Task;}
        public void Register(NativeCommunityInstalled installed){RegisterThread=Environment.CurrentManagedThreadId;Registered++;}
        public Task<NativeCreatorPage> BrowseMineAsync(int page,CancellationToken token)=>throw new InvalidOperationException("Unexpected account access");
        public Task<NativeCreatorEntry?> ReadOwnedAsync(Guid mapId,CancellationToken token)=>throw new InvalidOperationException("Unexpected account access");
        public Task<bool> ExecuteAsync(NativeCreatorAction action,Guid mapId,CancellationToken token)=>throw new InvalidOperationException("Unexpected account write");
    }
    /// <summary>Never reads the user's paths file. Restores the exact process globals it temporarily changes.</summary>
    private sealed class Isolation : IDisposable
    {
        internal readonly string Root=Path.Combine(Path.GetTempPath(),"prime-host-gl-"+Guid.NewGuid().ToString("N"));
        private readonly string _cwd=Directory.GetCurrentDirectory(),_root=GameFiles.Root,_maps=CustomRooms.MapDirectory,
            _userMaps=CustomRooms.UserMapDirectory,_namespace=CustomRooms.RuntimeNamespace,_mph=Paths.MphKey,_fh=Paths.FhKey;
        private readonly Dictionary<string,string> _paths=new((Dictionary<string,string>)typeof(Paths).GetField("_allPaths",BindingFlags.NonPublic|BindingFlags.Static)!.GetValue(null)!);
        private readonly object? _published=typeof(GameFiles).GetField("_publishedPathsRoot",BindingFlags.NonPublic|BindingFlags.Static)!.GetValue(null);
        internal Isolation()
        {
            Directory.CreateDirectory(Root);string files=Path.Combine(Root,"empty-files");Directory.CreateDirectory(files);
            File.WriteAllText(Path.Combine(Root,"paths.txt"),"0.19.0.0\nAMHE1="+files+"\n");
            GameFiles.Root=Root;Directory.SetCurrentDirectory(Root);
            CustomRooms.MapDirectory=Path.Combine(Root,"maps");CustomRooms.UserMapDirectory=Path.Combine(Root,"user-maps");CustomRooms.RuntimeNamespace="synthetic-host-gl";
        }
        public void Dispose()
        {
            GameFiles.Root=_root;CustomRooms.MapDirectory=_maps;CustomRooms.UserMapDirectory=_userMaps;CustomRooms.RuntimeNamespace=_namespace;
            Directory.SetCurrentDirectory(_cwd);var paths=(Dictionary<string,string>)typeof(Paths).GetField("_allPaths",BindingFlags.NonPublic|BindingFlags.Static)!.GetValue(null)!;
            paths.Clear();foreach(var pair in _paths)paths.Add(pair.Key,pair.Value);Paths.MphKey=_mph;Paths.FhKey=_fh;
            typeof(GameFiles).GetField("_publishedPathsRoot",BindingFlags.NonPublic|BindingFlags.Static)!.SetValue(null,_published);
            Directory.Delete(Root,true);
        }
    }
    private sealed class Services : IRmlUiMenuServices
    {
        internal TaskCompletionSource<RmlUiServerCandidate?> Scan = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal CancellationToken ScanToken;
        internal int Scans, Joins, Creates, Accepted;
        internal LobbyCreationRequest? LastRequest;
        public HubSnapshot Hub() => new("SYNTHETIC HUNTER", Hunter.Samus, Hunter.Samus, 0, true, "NO NETWORK");
        public string BuildVersion() => "synthetic-community-host";
        public RmlUiLobbyView Lobby() => new(false, false, false, false, false, false, false,
            "", "", "", "", "", 8, Array.Empty<RmlUiLobbyPlayer>());
        public Task<RmlUiDirectory> Browse(CancellationToken token) => Task.FromResult(
            new RmlUiDirectory(true, "SYNTHETIC EMPTY DIRECTORY", Array.Empty<RmlUiServerCandidate>()));
        public Task<RmlUiServerCandidate?> QuickMatch(CancellationToken token)
        { Scans++; ScanToken = token; return Scan.Task; }
        public Task<RmlUiJoinResult> Join(string endpoint, string playerName, CancellationToken token)
        { Joins++; return Task.FromResult(new RmlUiJoinResult(false, default, "NO NETWORK")); }
        public Task<RmlUiJoinResult> CreateLobby(LobbyCreationRequest request, CancellationToken token)
        { Creates++; LastRequest=request; return Task.FromResult(new RmlUiJoinResult(false, default, "NO NETWORK")); }
        public bool AcceptJoin(LaunchPlan plan) { Accepted++; return false; }
        public bool OpenCreateLobby() => false;
        public bool NavigateTo(RmlUiRouteArgument route) => false;
        public bool OpenStudio() => false;
        public bool SetReady() => false;
        public bool StartMatch() => false;
        public bool LeaveLobby() => false;
        public bool NextHunter() => false;
        public bool NextSuit() => false;
        public bool OpenLobbyDetails() => false;
        public RmlUiRuleDraft? ReadRules() => null;
        public IReadOnlyList<string> BuiltInArenaKeys() => new[] { "MP1 SANCTUS" };
        public int LobbyPlayerCapacity(string roomKey) => 4;
        public bool SubmitRules(RmlUiRuleDraft draft, out string error) { error = "NO NETWORK"; return false; }
        public void Quit() { }
    }
}
