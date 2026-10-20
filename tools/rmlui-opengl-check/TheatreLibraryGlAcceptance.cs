using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MphRead;
using MphRead.Mods;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.RmlUi.Core;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Render;
using MphRead.Mods.Render;
using OpenTK.Graphics.OpenGL;

internal static class TheatreLibraryGlAcceptance
{
    internal static void Run(RmlUiHost host,int width,int height)
    {
        const string scope="Actual native Theatre library and real menu controller, injected immutable SYNTHETIC replay descriptors only. No replay reader, account, player recordings, network, OS picker or playback world. Native input events are injected into RmlUiHost.";
        string output=Path.GetFullPath(Path.Combine("artifacts","rmlui-theatre-library",$"{width}x{height}"));Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output,"README.txt"),scope);
        using var controller=new RmlUiMenuController(new FixtureServices());using var manager=new RmlUiPageManager(host);using var layout=new RmlUiLauncherPages(host,manager);
        host.Input.SetFramebufferScale(1,1);long presented=0;int checks=0;
        Check(controller.Dispatch(RmlUiIntentKind.Navigate,(int)RmlUiRouteArgument.Theatre),"production Theatre route opens");
        Settle(350);
        if(Environment.GetEnvironmentVariable("PROJECT_PRIME_RMLUI_CHECK_TOUCH")=="1")host.SetBool(layout.Document,"class:@document:touch-targets",true);
        if(Environment.GetEnvironmentVariable("PROJECT_PRIME_RMLUI_CHECK_LARGE_TEXT")=="1")host.SetBool(layout.Document,"class:@document:large-text",true);
        Settle(100);Check(Text("theatre_summary").StartsWith("18 MATCHING"),"all injected rows reach real library owner");Capture("initial");
        File.WriteAllText(Path.Combine(output,"initial-bounds.json"),JsonSerializer.Serialize(new[]{"theatre_results","theatre_summary","theatre_empty","theatre_entry0","theatre_selected_scroll","theatre_error","theatre_launch_problem"}.Select(id=>{bool visible=host.TryGetElementBounds(layout.Document,id,out float x,out float y,out float w,out float h);return new{id,visible,x,y,width=w,height=h};}),new JsonSerializerOptions{WriteIndented=true}));
        var headerBoxes=new List<(string Id,float X,float Y,float W,float H)>();
        foreach(string id in new[]{"nav_play","nav_hunters","nav_community","nav_theatre","nav_studio","profile","header_settings"})
        {
            Check(Inside(id)&&Within(id,"topbar"),"compact native header control remains fully visible: "+id);
            host.TryGetElementBounds(layout.Document,id,out float x,out float y,out float w,out float h);headerBoxes.Add((id,x,y,w,h));
        }
        for(int a=0;a<headerBoxes.Count;a++)for(int b=a+1;b<headerBoxes.Count;b++)
        {
            var first=headerBoxes[a];var second=headerBoxes[b];
            Check(!(first.X<second.X+second.W-.5f&&first.X+first.W>second.X+.5f&&first.Y<second.Y+second.H-.5f&&first.Y+first.H>second.Y+.5f),"compact native header controls do not overlap: "+first.Id+" / "+second.Id);
        }
        Check(Within("theatre_entry0","theatre_results"),"at least one complete archive row is initially visible inside results");
        foreach(string id in new[]{"theatre_empty","theatre_error","theatre_launch_problem"})Check(!host.TryGetElementBounds(layout.Document,id,out _,out _,out _,out _),"empty notice has no layout box: "+id);
        foreach(string id in new[]{"theatre_watch","theatre_studio"}) Check(Inside(id),"primary action initially visible: "+id);
        Click("theatre_entry2",RmlUiIntentKind.TheatreEntry,2);Check(Text("theatre_selected_title").Contains("02"),"pointer selects exact real owner entry");
        Click("theatre_next_page",RmlUiIntentKind.TheatreAction,4);Check(Text("theatre_page_number")=="2 / 3","native paging advances actual owner");Capture("page2");
        Click("theatre_previous_page",RmlUiIntentKind.TheatreAction,3);Check(Text("theatre_page_number")=="1 / 3","native previous restores page");
        host.SetField(layout.Document,"theatre_search","REPLAY 17");Click("theatre_search_submit",RmlUiIntentKind.TheatreAction,0);Check(Text("theatre_summary").StartsWith("1 MATCHING"),"native field search filters actual owner");
        host.SetField(layout.Document,"theatre_search","NO-SYNTHETIC-MATCH");Click("theatre_search_submit",RmlUiIntentKind.TheatreAction,0);Check(Text("theatre_summary").StartsWith("0 MATCHING")&&host.TryGetElementBounds(layout.Document,"theatre_empty",out _,out _,out _,out _),"empty search displays real owner notice");
        Click("theatre_clear_search",RmlUiIntentKind.TheatreAction,23);Check(Text("theatre_summary").StartsWith("18 MATCHING"),"native clear restores all rows");Check(!host.TryGetElementBounds(layout.Document,"theatre_empty",out _,out _,out _,out _),"clear search retires empty notice box");
        foreach(string id in new[]{"theatre_favorite","theatre_validate","theatre_export","theatre_rename","theatre_rename_apply","theatre_tags","theatre_collections","theatre_organize","theatre_delete","theatre_import_path","theatre_import_path_apply"}) { Reach(id);Check(Inside(id),"metadata/tool can scroll fully into framebuffer: "+id); }
        Capture("tools-scrolled");
        Reach("theatre_selected_scroll");
        if(host.TryGetElementBounds(layout.Document,"theatre_selected_scroll",out float sx,out float sy,out float sw,out float sh)){host.Input.PointerMoved(sx+sw/2,sy+sh/2);host.Input.PointerWheel(1000,RmlUiInputModifiers.None);Settle(300);}
        Reach("theatre_watch");Check(Inside("theatre_watch")&&Inside("theatre_studio"),"primary actions remain visible after details wheel scroll");
        Click("theatre_home",RmlUiIntentKind.Navigate,(int)RmlUiRouteArgument.Home);Check(!controller.TheatreOpen,"native footer returns real owner Home");
        Check(GL.GetError()==ErrorCode.NoError,"no GL error");Console.WriteLine($"THEATRE LIBRARY GL PASS {checks} assertions");
        void Check(bool ok,string label){if(!ok)throw new InvalidOperationException("THEATRE LIBRARY GL: "+label);checks++;Console.WriteLine("THEATRE LIBRARY GL PASS "+label);}
        string Text(string id)=>controller.Tick().Bindings[id].Text;
        bool Inside(string id)=>host.TryGetElementBounds(layout.Document,id,out float x,out float y,out float w,out float h)&&w>0&&h>0&&x>=-1&&y>=-1&&x+w<=width+1&&y+h<=height+1;
        bool Within(string id,string container)=>host.TryGetElementBounds(layout.Document,id,out float x,out float y,out float w,out float h)&&host.TryGetElementBounds(layout.Document,container,out float sx,out float sy,out float sw,out float sh)&&w>0&&h>0&&x>=sx-1&&y>=sy-1&&x+w<=sx+sw+1&&y+h<=sy+sh+1;
        void Frame(){var state=controller.Tick();if(state.Revision>presented){foreach(var item in state.Bindings){if(item.Value.Kind==RmlUiBindingKind.Text)layout.SetText(item.Key,item.Value.Text);else layout.SetBool(item.Key,item.Value.Boolean);}presented=state.Revision;}layout.Flush();host.Update();layout.AfterUpdate();GL.BindFramebuffer(FramebufferTarget.Framebuffer,0);GL.Viewport(0,0,width,height);GL.Disable(EnableCap.ScissorTest);GL.ClearColor(.025f,.05f,.08f,1);GL.Clear(ClearBufferMask.ColorBufferBit|ClearBufferMask.DepthBufferBit|ClearBufferMask.StencilBufferBit);RmlUiOpenGlFrame.Draw(host,width,height);}
        void Settle(int ms){var timer=Stopwatch.StartNew();do{Frame();Thread.Sleep(10);}while(timer.ElapsedMilliseconds<ms);Frame();}
        void Reach(string id){Frame();Check(host.FocusDocument(layout.Document,id),"native focus reaches "+id);Settle(250);}
        void Click(string id,RmlUiIntentKind kind,int arg){Reach(id);Check(Inside(id),"actual pointer bounds visible: "+id);host.TryGetElementBounds(layout.Document,id,out float x,out float y,out float w,out float h);host.DiscardIntents();host.Input.PointerButton(0,x+w/2,y+h/2,true);host.Input.PointerButton(0,x+w/2,y+h/2,false);host.Update();bool received=false;while(host.TryTakeIntent(out var raw)){if(!layout.HandleIntent(raw,out var intent)||intent==default)continue;Check(intent.Kind==kind&&intent.Argument==arg,"exact native action: "+id);Check(controller.Dispatch(intent.Kind,intent.Argument,theatreSearch:layout.ReadField("theatre_search"),theatreInputs:controller.TheatreInputs),"real owner accepts "+id);Check(layout.HandleIntent(raw,out var duplicate)&&duplicate==default,"duplicate sequence rejected: "+id);received=true;}Check(received,"native pointer emits "+id);Settle(100);}
        void Capture(string name){Settle(80);string file=Path.Combine(output,name+".png");Check(ScreenCapture.SaveWindow(width,height,file),"capture "+name);FinalCompositeCapture.WriteEvidence(file,width,height,scope);File.WriteAllText(file+".semantic.json",NativeSemanticReadback.Read(host,layout.Document).RawJson);}
    }
    private sealed class FixtureServices : IRmlUiMenuServices
    {
        public Task<RmlUiReplayEntry[]> ReadTheatreAsync(CancellationToken token) => Task.FromResult(System.Linq.Enumerable.Range(0, 18).Select(i => new RmlUiReplayEntry("SYNTHETIC-NOT-A-FILE-"+i, $"SYNTHETIC REPLAY {i:00}", "SYNTHETIC LONG MATCH // SAMUS VS SYLUX // 18 MINUTES // QA ONLY", "SYNTHETIC ARENA", DateTime.UtcNow.Ticks-i*TimeSpan.TicksPerMinute, 1000+i, i+1, false, false, false, true)).ToArray());
        public HubSnapshot Hub() => new("SYNTHETIC FIXTURE", Hunter.Samus, Hunter.Samus, 0, true, "SYNTHETIC GL FIXTURE");
        public string BuildVersion() => "synthetic-theatre-library";
        public RmlUiLobbyView Lobby() => new(false,false,false,false,false,false,false,"","","","","",8,Array.Empty<RmlUiLobbyPlayer>());
        public IReadOnlyList<string> BuiltInArenaKeys() => new[] { "SYNTHETIC ARENA" };
        public Task<RmlUiDirectory> Browse(CancellationToken token) => Task.FromResult(new RmlUiDirectory(false, "FIXTURE", Array.Empty<RmlUiServerCandidate>()));
        public Task<RmlUiServerCandidate?> QuickMatch(CancellationToken token) => Task.FromResult<RmlUiServerCandidate?>(null);
        public Task<RmlUiJoinResult> Join(string endpoint, string name, CancellationToken token) => Task.FromResult(new RmlUiJoinResult(false, new LaunchPlan(), "FIXTURE"));
        public Task<RmlUiJoinResult> CreateLobby(LobbyCreationRequest request, CancellationToken token) => Task.FromResult(new RmlUiJoinResult(false, new LaunchPlan(), "FIXTURE"));
        public bool AcceptJoin(LaunchPlan plan) => false;
        public bool OpenCreateLobby() => false;
        public bool NavigateTo(RmlUiRouteArgument destination) => false;
        public bool OpenStudio() => false;
        public bool SetReady() => false;
        public bool StartMatch() => false;
        public bool LeaveLobby() => false;
        public bool NextHunter() => false;
        public bool NextSuit() => false;
        public bool OpenLobbyDetails() => false;
        public RmlUiRuleDraft? ReadRules() => null;
        public bool SubmitRules(RmlUiRuleDraft draft, out string error) { error = "FIXTURE"; return false; }
        public void Quit() { }
    }
}
