using MphRead.Mods;
using System.Text.Json;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Pages.Updates;
using MphRead.Mods.Launcher.RmlUi.Render;
using MphRead.Mods.Update;
using MphRead.Mods.Render;
using OpenTK.Graphics.OpenGL;

/// <summary>Synthetic catalog and effect-only install recorder; never calls HTTP or installer APIs.</summary>
internal static class NativeVersionsGlAcceptance
{
    internal static void Run(RmlUiHost host, int width, int height)
    {
        int checks = 0, installs = 0, loads = 0;
        bool owner = true, busy = false, wheelReached = false;
        string status = "synthetic download 42%";
        ulong sequence = 10000;
        var releases = Enumerable.Range(0, 30).Select(i => new UpdateInfo
        {
            Tag = "v2.0." + (29-i), Version = new Version(2,0,29-i),
            AssetName = "", AssetUrl = "", AssetDigest = "", PageUrl = "", Notes = ""
        }).ToArray();
        string root=Environment.GetEnvironmentVariable("PROJECT_PRIME_USER_DATA") ?? throw new InvalidOperationException("Explicit private user data required.");
        if (!Path.IsPathFullyQualified(root)) throw new InvalidOperationException("Private user data must be absolute.");
        string output=Path.Combine(root,"native-versions-gl",width+"x"+height); Directory.CreateDirectory(output);
        var pending = NewCompletion();
        using var pages = new RmlUiPageManager(host);
        var parent = pages.OpenPage(new("versions-fixture-parent", "pages/studio/native-entry.rml", "nav_play"));
        host.Input.SetFramebufferScale(1,1);
        var page = Create();
        try
        {
            page.Open(); Frame(); var document = page.Document;
            Check(Read().ContainsText("Loading published releases"), "loading state is native text");
            pending.SetResult((releases, "")); page.Tick(); Frame();
            Check(loads == 1 && Read().ContainsText("v2.0.29") && Read().ContainsText("v2.0.0"), "all thirty lowercase published tags project");
            Check(Read().ContainsText("CURRENT") && Read().ContainsText("DOWNGRADE") && Read().ContainsText("SWITCH"), "current and relative version labels project");
            Check(Read().ContainsText(status), "retained owner progress projects");
            Check(host.TryGetElementBounds(document,"update_card",out float cx,out float cy,out float cw,out float ch)
                && cx>=-1 && cy>=-1 && cw>0 && ch>0 && cx+cw<=width+1 && cy+ch<=height+1,
                "native catalog card stays inside framebuffer bounds");
            Capture("catalog");
            Check(host.TryGetElementBounds(document,"versions_rows",out float x,out float y,out float w,out float h) && w>0 && h>0,
                "scroll viewport has actual native bounds");
            Check(x>=cx-1 && y>=cy-1 && x+w<=cx+cw+1 && y+h<=cy+ch+1,
                "complete initial catalog viewport fits inside native card at current density");
            Check(host.TryGetElementBounds(document,"versions_row_0",out float firstX,out _,out float firstW,out _)
                && firstW>=w*.9f && firstX>=x-1 && firstX+firstW<=x+w+1,
                "release rows fill their viewport without narrow percentage-width collapse");
            host.DiscardIntents();
            Check(host.TryGetElementBounds(document,"versions_row_0",out float px,out float py,out float pw,out float ph),
                "first native release row has pointer bounds");
            host.Input.PointerMoved(px+pw/2,py+ph/2);Frame();
            Check(host.HoveredElement()=="versions_row_0" && host.HoveredAction()=="settings:action:0",
                "native high-density hover resolves actual release action");
            host.Input.PointerButton(0,px+pw/2,py+ph/2,true);
            host.Input.PointerButton(0,px+pw/2,py+ph/2,false);Frame();
            Check(host.TryTakeIntent(out var pointerSelect) && pointerSelect.Document==document
                && pointerSelect.Kind==RmlUiIntentKind.SettingsAction && pointerSelect.Argument==0
                && page.Handle(pointerSelect) && installs==0,
                "actual native pointer selects published release without installing");
            ClickAction("versions_cancel",RmlUiIntentKind.SettingsDiscard);
            Check(installs==0 && Read().Label("versions_confirmation")=="", "actual pointer cancel clears frozen target without installation");
            Check(host.FocusDocument(document,"versions_row_0"),"focus returns to catalog before wheel qualification");
            for(int settle=0;settle<30;settle++){Thread.Sleep(8);Frame();}
            host.TryGetElementBounds(document,"versions_rows",out x,out y,out w,out h);
            bool lastReached=false;
            for(int scroll=0;scroll<100;scroll++)
            {
                // Native smooth wheel scrolling advances with elapsed time; one immediate
                // Update after a large wheel event does not prove the final scroll position.
                host.Input.PointerMoved(x+w/2,y+h/2); host.Input.PointerWheel(-3);
                Thread.Sleep(8); Frame();
                if(host.TryGetElementBounds(document,"versions_row_29",out float lx,out float ly,out float lw,out float lh)
                    && lw>=w*.9f && lh>0 && lx>=x-1 && lx+lw<=x+w+1 && ly>=y-1 && ly+lh<=y+h+1)
                { lastReached=true;break; }
            }
            wheelReached=lastReached;
            Check(wheelReached,"thirtieth release is fully contained after actual native wheel scrolling");
            // Qualify keyboard/accessibility scrolling independently as well.
            Check(host.FocusDocument(document,"versions_row_29"),"native keyboard focus reaches thirtieth published release");
            lastReached=false;
            for(int settle=0;settle<100;settle++)
            {
                Thread.Sleep(8);Frame();
                host.TryGetElementBounds(document,"versions_rows",out x,out y,out w,out h);
                if(host.TryGetElementBounds(document,"versions_row_29",out float lx,out float ly,out float lw,out float lh)
                    && lw>=w*.9f && lh>0 && lx>=x-1 && lx+lw<=x+w+1 && ly>=y-1 && ly+lh<=y+h+1
                    && lx>=cx-1 && ly>=cy-1 && lx+lw<=cx+cw+1 && ly+lh<=cy+ch+1)
                {lastReached=true;break;}
            }
            Check(lastReached,"thirtieth release is fully contained after native keyboard/accessibility scrolling");
            Capture("catalog-scrolled");
            Send(RmlUiIntentKind.SettingsAction, 15);
            Check(Read().ContainsText("to v2.0.14"),"row fifteen crosses reserved wire action without becoming unreachable");
            Send(RmlUiIntentKind.SettingsDiscard);
            Send(RmlUiIntentKind.SettingsAction, 29);
            Check(Read().ContainsText("Downgrade from v2.0.15 to v2.0.0"), "frozen downgrade target has explicit confirmation");
            Send(RmlUiIntentKind.SettingsAction, 0);
            Check(Read().ContainsText("to v2.0.0"), "second selection cannot replace frozen target");
            Send(RmlUiIntentKind.SettingsDiscard);
            Send(RmlUiIntentKind.SettingsApply);
            Check(installs==0, "cancel then confirm has no install effect");
            Send(RmlUiIntentKind.SettingsAction,14); Send(RmlUiIntentKind.SettingsApply);
            Check(installs==0,"current version cannot install");
            Send(RmlUiIntentKind.SettingsAction,0);
            var confirm = new RmlUiIntent(RmlUiIntentKind.SettingsApply,0,document,sequence++);
            var cover = pages.OpenModal(new("versions-fixture-cover","pages/updates/available.rml","update_later"));
            string before = Read().RawJson; status="covered must wait"; page.Tick();
            Check(Read().RawJson==before && !page.Handle(confirm) && installs==0,"covered document neither mutates nor installs");
            page.Back(); Check(!page.Closed,"covered Back cannot close owner");
            pages.CloseModal(); Frame();
            string frozenBeforeRejection=Read().Label("versions_confirmation");
            Check(!page.Handle(confirm with { Document=parent }) && !page.Handle(confirm with { Sequence=0 })
                && installs==0 && Read().Label("versions_confirmation")==frozenBeforeRejection,
                "foreign and zero sequence confirmations are rejected without effect or frozen-target mutation");
            owner=false; Check(!page.Handle(confirm) && installs==0,"stale owner cannot install"); owner=true;
            page.Handle(confirm); page.Handle(confirm);
            Check(installs==1,"frozen foreground confirmation installs exactly once");
            busy=true; Send(RmlUiIntentKind.SettingsAction,29); Send(RmlUiIntentKind.SettingsApply);
            Check(installs==1,"busy retained owner rejects another install"); busy=false;
            page.Dispose(); Check(!page.Handle(confirm) && installs==1,"retired document rejects delayed confirmation");

            pending=NewCompletion(); page=Create(); page.Open();
            pending.SetException(new InvalidOperationException("synthetic catalog failure")); page.Tick(); Frame();
            Check(Read().ContainsText("synthetic catalog failure"),"faulted catalog shows native retryable error");
            pending=NewCompletion(); Send(RmlUiIntentKind.SettingsAction,30);
            pending.SetResult((Array.Empty<UpdateInfo>(),"synthetic empty catalog")); page.Tick(); Frame();
            Check(Read().ContainsText("synthetic empty catalog"),"empty catalog retains explanation");
            pending=NewCompletion(); Send(RmlUiIntentKind.SettingsAction,30);
            pending.SetCanceled(); page.Tick(); Frame();
            Check(Read().ContainsText("Release loading was canceled"),"canceled metadata enables retry");
            pending=NewCompletion(); Send(RmlUiIntentKind.SettingsAction,30);
            var late=pending; var retired=page.Document; page.Dispose();
            late.SetResult((releases,"late")); page.Tick();
            Check(!host.IsAlive(retired) && pages.Top==parent && installs==1,"late catalog cannot resurrect disposed page");

            pending=NewCompletion(); page=Create(); page.Open();Frame();
            ClickAction("versions_close",RmlUiIntentKind.SettingsClose);
            Check(page.Closed && installs==1,"actual pointer close retires catalog without another installation");
            page.Dispose();
            pending=NewCompletion(); page=Create(); page.Open(); var coveredDocument=page.Document;
            cover=pages.OpenModal(new("versions-dispose-cover","pages/updates/available.rml","update_later"));
            page.Dispose();
            Check(pages.Top==cover && host.IsAlive(cover),"covered disposal preserves foreign top modal");
            pages.CloseModal(); Check(pages.Top==coveredDocument && pages.CloseModal(),"fixture explicitly retires covered disposed document");
            Check(GL.GetError()==ErrorCode.NoError,"native versions fixture leaves no GL error");
            File.WriteAllText(Path.Combine(output,"summary.json"),JsonSerializer.Serialize(new { passed=true,checks,loads,installs,width,height,wheelReached,
                scope="Synthetic thirty-release catalog, actual native modal and keyboard/accessibility scroll; wheel outcome recorded separately, current/rank labels, frozen target, cancel, duplicate/foreign/retired/stale owner rejection, progress, failure/retry/empty/canceled/late completion. No HTTP, browser, download, installation or restart." }, new JsonSerializerOptions{WriteIndented=true}));
        }
        finally { page.Dispose(); pages.ClosePage(); host.DiscardIntents(); }
        NativeVersionManager Create() => new(host,pages,()=>owner,()=>busy,()=>status,_=>installs++,()=>{loads++;return pending.Task;},new Version(2,0,15));
        void ClickAction(string id,RmlUiIntentKind expected)
        {
            Check(host.FocusDocument(page.Document,id),"native focus reaches pointer action "+id);
            for(int settle=0;settle<30;settle++){Thread.Sleep(8);Frame();}
            Check(host.TryGetElementBounds(page.Document,id,out float ax,out float ay,out float aw,out float ah)
                && aw>0 && ah>0 && ax>=0 && ay>=0 && ax+aw<=width+1 && ay+ah<=height+1,
                "actual pointer action fits framebuffer "+id);
            host.DiscardIntents();host.Input.PointerMoved(ax+aw/2,ay+ah/2);Frame();
            Check(host.HoveredElement()==id,"actual native hover resolves "+id);
            host.Input.PointerButton(0,ax+aw/2,ay+ah/2,true);host.Input.PointerButton(0,ax+aw/2,ay+ah/2,false);Frame();
            Check(host.TryTakeIntent(out var action) && action.Document==page.Document && action.Kind==expected
                && page.Handle(action),"actual native pointer emits and admits "+id);
        }
        void Capture(string name)
        {
            string image=Path.Combine(output,name+".png");
            Check(ScreenCapture.SaveWindow(width,height,image),"framebuffer capture "+name);
            FinalCompositeCapture.WriteEvidence(image,width,height,"Native version manager synthetic catalog; no network or installation.");
            File.WriteAllText(image+".semantic.json",Read().RawJson);
        }
        NativeSemanticReadback Read() => NativeSemanticReadback.Read(host,page.Document);
        void Send(RmlUiIntentKind kind,int arg=0)
        {
            // Shared SettingsAction wire value 15 is intentionally reserved.
            int wire=kind==RmlUiIntentKind.SettingsAction && arg>=15 ? arg+1 : arg;
            page.Handle(new(kind,wire,page.Document,sequence++)); Frame();
        }
        void Frame() { host.Update(); GL.BindFramebuffer(FramebufferTarget.Framebuffer,0); GL.Viewport(0,0,width,height); GL.Clear(ClearBufferMask.ColorBufferBit); RmlUiOpenGlFrame.Draw(host,width,height); }
        void Check(bool condition,string label) { if(!condition)throw new InvalidOperationException("NATIVE VERSIONS GL: "+label);checks++;Console.WriteLine("NATIVE VERSIONS GL PASS "+label); }
    }
    private static TaskCompletionSource<(IReadOnlyList<UpdateInfo> Releases,string Reason)> NewCompletion() => new();
}
