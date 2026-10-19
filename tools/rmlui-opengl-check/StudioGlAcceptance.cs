using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Pages.Studio;
using MphRead.Mods.Launcher.RmlUi.Render;
using MphRead.Mods.Render;
using OpenTK.Graphics.OpenGL;

/// <summary>Actual native Prime Studio hub; prohibited map authority throws on every access.</summary>
internal static class StudioGlAcceptance
{
    internal static void Run(RmlUiHost host,int width,int height)
    {
        const string scope="Actual native Prime Studio hub, disabled Map Studio placeholder, Replay Studio pointer route and map-intent rejection. Prohibited synthetic map authority only; no library reads, project, editor, filesystem picker, publication, process launch or gameplay.";
        string output=Path.GetFullPath(Path.Combine("artifacts","rmlui-studio-workspace",width+"x"+height));Directory.CreateDirectory(output);
        using var pages=new RmlUiPageManager(host);var owner=new ProhibitedMapOwner();using var initialPage=new NativeStudioEntryPage(host,pages,owner);NativeStudioEntryPage page=initialPage;
        int checks=0;bool headerStudio=false,hubStudioRole=false;host.Input.SetFramebufferScale(1,1);page.Open(showChooser:false);
        host.SetBool(page.Document,"class:screen:reduce-motion",true);
        host.SetBool(page.Document,"class:@document:reduce-motion",true);Settle();
        CheckHeader(false);
        CaptureHeader("main");
        ClickHeaderTheatre("game Theatre");
        headerStudio=true;Settle();CheckHeader(true);CaptureHeader("studio");
        ClickHeaderTheatre("Studio Replay Studio");
        headerStudio=false;Settle();CheckHeader(false);
        Check(page.ShowingChooser&&!page.Closed&&owner.Calls==0,"Open always shows chooser without reading map catalog or acquiring editor");
        var semantic=NativeSemanticReadback.Read(host,page.Document);
        Check(semantic.ContainsText("PRIME STUDIO")&&semantic.ContainsText("MAP STUDIO")&&semantic.ContainsText("COMING SOON")&&semantic.ContainsText("OPEN REPLAY STUDIO"),"actual hub names available and future workspaces");
        Check(host.FocusDocument(page.Document,"native_studio_map"),"Main chooser can request separate Map Studio placeholder");
        foreach(string id in new[]{"native_studio_launch","native_studio_edit_current","native_studio_publish","native_studio_path","native_studio_create","native_studio_row0"})
            Check(!host.FocusDocument(page.Document,id),"retired map control is absent: "+id);
        Click("native_studio_map");
        Check(host.TryTakeIntent(out var mapIntent)&&mapIntent.Kind==RmlUiIntentKind.StudioAction&&mapIntent.Argument==0&&!host.TryTakeIntent(out _),"Main Map Studio card emits bounded workspace launch action");
        Check(page.Handle(mapIntent)&&page.TryTakeWorkspaceLaunch(out bool replayMap)&&!replayMap&&!page.TryTakeWorkspaceLaunch(out _),"Main Map Studio launch request is consumed once without authoring");
        Check(!page.Handle(mapIntent)&&!page.TryTakeWorkspaceLaunch(out _),"duplicate map card sequence cannot launch twice");
        Click("native_studio_theatre");
        Check(host.TryTakeIntent(out var replay)&&replay.Kind==RmlUiIntentKind.StudioAction&&replay.Argument==1&&!host.TryTakeIntent(out _),"Main Replay Studio card emits separate workspace launch action");
        Check(page.Handle(replay)&&page.TryTakeWorkspaceLaunch(out bool replayWorkspace)&&replayWorkspace&&!page.TryTakeWorkspaceLaunch(out _),"Main Replay Studio launch request is consumed once");
        Click("native_studio_map");Check(host.TryTakeIntent(out var covered)&&page.Handle(covered),"workspace request is pending before a modal covers chooser");
        pages.OpenModal(new("hub-cover","pages/studio/native-entry.rml","native_studio_chooser_back"));host.Update();
        Check(!page.TryTakeWorkspaceLaunch(out _),"covered chooser cannot hand off workspace launch");pages.CloseModal();Settle();
        Check(!page.TryTakeWorkspaceLaunch(out _)&&owner.Calls==0,"returning foreground cannot resurrect discarded launch request");
        ulong sequence=1000;
        for(int action=0;action<224;action++)
        {
            if(action is 0 or 1 or 4)continue;
            Check(!page.Handle(new(RmlUiIntentKind.StudioAction,action,page.Document,sequence++)),"map/editor action is refused: "+action);
        }
        Check(!page.Handle(new(RmlUiIntentKind.CommunityDetailAction,5,page.Document,sequence++)),"publish intent is refused");
        Check(!page.TryTakeWorkspaceLaunch(out _)&&!page.TryTakeEditorRequest()&&!page.TryTakePublishRequest()&&!page.TryTakeProjectChangeRequest(out var changes)&&changes==null&&owner.Calls==0,
            "all attempted map actions leave editor publication and project-change handoffs unreachable");
        Settle();string image=Path.Combine(output,"prime-studio-coming-soon.png");Check(ScreenCapture.SaveWindow(width,height,image),"capture native hub");
        FinalCompositeCapture.WriteEvidence(image,width,height,scope);File.WriteAllText(image+".semantic.json",NativeSemanticReadback.Read(host,page.Document).RawJson);
        Check(page.Handle(new(RmlUiIntentKind.StudioAction,4,page.Document,sequence++))&&page.Closed,"existing typed Back closes hub");
        page.Dispose();
        using var studioPage=new NativeStudioEntryPage(host,pages,owner,isStudio:true);page=studioPage;hubStudioRole=headerStudio=true;page.Open();Settle();
        Check(!host.FocusDocument(page.Document,"native_studio_map")&&!host.FocusDocument(page.Document,"native_studio_theatre")
            &&host.FocusDocument(page.Document,"native_studio_replay_open"),"Studio app keeps Map Coming Soon and exposes native Replay library route");
        Click("native_studio_replay_open");Check(host.TryTakeIntent(out var studioReplay)&&studioReplay.Kind==RmlUiIntentKind.Navigate
            &&studioReplay.Argument==(int)RmlUiRouteArgument.Theatre&&!host.TryTakeIntent(out _),"Studio Replay card navigates its own library without another process request");
        Check(!page.Handle(new(RmlUiIntentKind.StudioAction,0,page.Document,sequence++))&&!page.Handle(new(RmlUiIntentKind.StudioAction,1,page.Document,sequence++))
            &&!page.TryTakeWorkspaceLaunch(out _)&&!page.TryTakeEditorRequest()&&!page.TryTakePublishRequest(),"Studio app cannot reopen retired map authoring or enqueue another workspace launch");
        Check(NativeSemanticReadback.Read(host,page.Document).ContainsText("COMING SOON")&&owner.Calls==0,"Studio app preserves future Map placeholder without owner access");
        CaptureHeader("studio-app-placeholder");
        Check(owner.Calls==0&&GL.GetError()==ErrorCode.NoError,"hub never touches map authority and leaves no GL error");
        page.Dispose();HomeRailsGlAcceptance.Run(host,width,height);StartupGlAcceptance.Run(host,width,height);NativeUpdatePromptGlAcceptance.Run(host,width,height);NativeVersionsGlAcceptance.Run(host,width,height);NativeFullSettingsGlAcceptance.Run(host,width,height);
        File.WriteAllText(Path.Combine(output,"studio-summary.json"),JsonSerializer.Serialize(new{passed=true,checks,width,height,scope,mapOwnerCalls=owner.Calls},new JsonSerializerOptions{WriteIndented=true}));

        void Check(bool value,string label){if(!value)throw new InvalidOperationException(label);checks++;Console.WriteLine("RML STUDIO HUB PASS "+label);}
        void Frame(){page.Refresh();host.SetBool(page.Document,"class:screen:reduce-motion",true);host.SetBool(page.Document,"class:@document:reduce-motion",true);host.Update();RmlUiApplicationHeader.Apply(host,page.Document,headerStudio);host.Update();GL.BindFramebuffer(FramebufferTarget.Framebuffer,0);GL.Viewport(0,0,width,height);GL.Disable(EnableCap.ScissorTest);GL.ClearColor(.025f,.05f,.08f,1);GL.Clear(ClearBufferMask.ColorBufferBit|ClearBufferMask.DepthBufferBit|ClearBufferMask.StencilBufferBit);RmlUiOpenGlFrame.Draw(host,width,height);}
        void Settle(int ms=120){var watch=Stopwatch.StartNew();do{Frame();Thread.Sleep(5);}while(watch.ElapsedMilliseconds<ms);}
        void CheckHeader(bool studio)
        {
            host.Input.PointerMoved(width-2,height-2);
            Check(host.FocusDocument(page.Document,hubStudioRole?"native_studio_replay_open":"native_studio_theatre"),"neutral hub focus clears header hover/focus transforms");
            Settle(180);
            var ids=studio?new[]{"nav_studio_home","nav_theatre","header_settings"}:new[]{"nav_play","nav_hunters","nav_community","nav_theatre","nav_studio","header_settings"};
            var boxes=new List<(string Id,float X,float Y,float W,float H)>();
            Check(host.TryGetElementBounds(page.Document,"topbar",out float hx,out float hy,out float hw,out float hh)&&hw>0&&hh>0,"authored header has visible bounds");
            var measurements=new List<object>();
            foreach(string id in new[]{"topbar","brand","brand_title","primary_nav","nav_play","nav_hunters","nav_community","nav_theatre","nav_studio","nav_studio_home","account_cluster","profile","header_settings"})
            {
                bool visible=host.TryGetElementBounds(page.Document,id,out float x,out float y,out float w,out float h);
                measurements.Add(new{id,visible,x,y,width=w,height=h});
            }
            string measured=JsonSerializer.Serialize(new{role=studio?"studio":"game",width,height,density=Environment.GetEnvironmentVariable("PROJECT_PRIME_RMLUI_CHECK_DENSITY")??"1",reduceMotion=true,measurements},new JsonSerializerOptions{WriteIndented=true});
            File.WriteAllText(Path.Combine(output,"header-"+(studio?"studio":"game")+"-bounds.json"),measured);
            Console.WriteLine("RML STUDIO HEADER BOUNDS "+measured);
            foreach(string id in ids)
            {
                Check(host.TryGetElementBounds(page.Document,id,out float x,out float y,out float w,out float h)&&w>0&&h>0
                    &&x>=0&&y>=0&&x+w<=width+1&&y+h<=height+1&&x>=hx&&y>=hy&&x+w<=hx+hw+1&&y+h<=hy+hh+1,
                    "role header control fits actual framebuffer and header: "+id+" control=("+x+","+y+","+w+","+h+") header=("+hx+","+hy+","+hw+","+hh+") framebuffer="+width+"x"+height);
                boxes.Add((id,x,y,w,h));
            }
            foreach(string id in new[]{"brand_title","profile"})
                if(host.TryGetElementBounds(page.Document,id,out float x,out float y,out float w,out float h)&&w>0&&h>0)
                {
                    Check(x>=0&&y>=0&&x+w<=width+1&&y+h<=height+1,"responsive header identity/account stays in framebuffer: "+id);
                    boxes.Add((id,x,y,w,h));
                }
            for(int a=0;a<boxes.Count;a++)for(int b=a+1;b<boxes.Count;b++)
            {
                var first=boxes[a];var second=boxes[b];
                bool overlap=first.X<second.X+second.W-.5f&&first.X+first.W>second.X+.5f&&first.Y<second.Y+second.H-.5f&&first.Y+first.H>second.Y+.5f;
                Check(!overlap,"role header controls do not overlap: "+first.Id+" / "+second.Id);
            }
            foreach(string id in studio?new[]{"nav_play","nav_hunters","nav_community","nav_studio","profile"}:new[]{"nav_studio_home"})
                Check(!host.FocusDocument(page.Document,id)&&!host.TryGetElementBounds(page.Document,id,out _,out _,out _,out _),"other application header control is hidden: "+id);
            var semantics=NativeSemanticReadback.Read(host,page.Document);
            Check(semantics.Label("nav_theatre")== (studio?"REPLAY STUDIO":"THEATRE"),"actual header destination label follows application role");
            if(studio)Check(host.TryGetElementBounds(page.Document,"brand_title",out _,out _,out float brandWidth,out float brandHeight)&&brandWidth>0&&brandHeight>0,
                "Studio identity has positive visible geometry; text requires screenshot review");
            Check(pages.Top==page.Document&&host.CurrentInputDocument==page.Document,"role projection preserves foreground document ownership");
        }
        void ClickHeaderTheatre(string label)
        {
            Check(host.FocusDocument(page.Document,"nav_theatre"),"native focus reaches "+label);Settle();
            Check(host.TryGetElementBounds(page.Document,"nav_theatre",out float x,out float y,out float w,out float h),"native header pointer target has bounds");
            host.DiscardIntents();host.Input.PointerMoved(x+w/2,y+h/2);host.Input.PointerButton(0,x+w/2,y+h/2,true);host.Input.PointerButton(0,x+w/2,y+h/2,false);host.Update();
            Check(host.TryTakeIntent(out var intent)&&intent.Kind==RmlUiIntentKind.Navigate&&intent.Argument==(int)RmlUiRouteArgument.Theatre
                &&intent.Document==page.Document&&!host.TryTakeIntent(out _),"foreground native header pointer emits one exact Theatre route: "+label);
        }
        void CaptureHeader(string role)
        {
            Frame();string path=Path.Combine(output,"header-"+role+".png");Check(ScreenCapture.SaveWindow(width,height,path),"capture actual "+role+" header");
            FinalCompositeCapture.WriteEvidence(path,width,height,scope);File.WriteAllText(path+".semantic.json",NativeSemanticReadback.Read(host,page.Document).RawJson);
        }
        void Click(string id)
        {
            Check(host.FocusDocument(page.Document,id),"native focus reaches Replay Studio");Settle();
            for(int i=0;i<120;i++)
            {
                bool found=host.TryGetElementBounds(page.Document,id,out float x,out float y,out float w,out float h);
                host.TryGetElementBounds(page.Document,"native_studio_entry",out float sx,out float sy,out float sw,out float sh);
                if(found&&w>0&&h>0&&x>=0&&y>=sy&&x+w<=width+1&&y+h<=height+1&&y+h<=sy+sh+1)
                {host.DiscardIntents();host.Input.PointerMoved(x+w/2,y+h/2);host.Input.PointerButton(0,x+w/2,y+h/2,true);host.Input.PointerButton(0,x+w/2,y+h/2,false);host.Update();return;}
                host.Input.PointerMoved(sx+sw/2,sy+sh/2);host.Input.PointerWheel(i==0?10000:-1);Settle(15);
            }
            throw new InvalidOperationException("Replay Studio route is not contained in actual scroll viewport");
        }
    }
    private sealed class ProhibitedMapOwner:INativeStudioLauncher
    {
        internal int Calls;
        private T Fail<T>(){Calls++;throw new InvalidOperationException("Coming Soon hub accessed retired map owner");}
        public bool LaunchMapStudio()=>Fail<bool>();
        public bool PickerAvailable=>Fail<bool>();
        public IReadOnlyList<NativeStudioTemplate> Templates()=>Fail<IReadOnlyList<NativeStudioTemplate>>();
        public Task<IReadOnlyList<NativeStudioLibraryRow>> ReadLibraryAsync(bool recoveries,CancellationToken token)=>Fail<Task<IReadOnlyList<NativeStudioLibraryRow>>>();
        public Task<NativeStudioPickResult> PickMapAsync(CancellationToken token)=>Fail<Task<NativeStudioPickResult>>();
        public NativeStudioResult OpenPath(string path)=>Fail<NativeStudioResult>();
        public NativeStudioResult OpenEntry(string key)=>Fail<NativeStudioResult>();
        public NativeStudioResult CreateProject(string name,string templateId)=>Fail<NativeStudioResult>();
        public NativeStudioResult DuplicateEntry(string key)=>Fail<NativeStudioResult>();
        public NativeStudioDeleteRequest? PrepareDelete(string key)=>Fail<NativeStudioDeleteRequest?>();
        public NativeStudioResult Delete(NativeStudioDeleteRequest request)=>Fail<NativeStudioResult>();
    }
}
