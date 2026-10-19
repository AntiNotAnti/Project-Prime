using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Pages.Theatre;
using MphRead.Mods.Launcher.RmlUi.Render;
using MphRead.Mods.Network;
using MphRead.Mods.Render;
using OpenTK.Graphics.OpenGL;

internal static partial class EditorGlAcceptance
{
    internal static void Run(RmlUiHost host,int width,int height)
    {
        Check(!DemoPlayback.IsActive,"editor fixture has no real replay world or transport session");
        var memory=new MemoryEditor();using var transport=new TheatrePlaybackController(new NoStudio());
        using var pages=new RmlUiPageManager(host);using var page=new TheatrePlaybackPagePresenter(host,pages,transport,editor:memory);
        page.Open();Frame();var playback=page.Document;
        Check(page.HandleIntent(new(RmlUiIntentKind.ReplayAction,(int)TheatrePlaybackAction.Studio,playback,1)),"Studio action retains native replay-editor modal when editor capability exists");
        Frame();var panel=pages.Top;Check(panel!=playback&&host.FocusedElement()=="replay_editor_refresh","native editor modal owns foreground focus");
        Click("replay_editor_player",TheatrePlaybackAction.EditorPlayer);Click("replay_editor_player",TheatrePlaybackAction.EditorPlayer);Click("replay_editor_player",TheatrePlaybackAction.EditorPlayer);
        Click("replay_editor_filter",TheatrePlaybackAction.EditorEventFilter);
        Click("replay_editor_next",TheatrePlaybackAction.EditorNext);Click("replay_editor_previous",TheatrePlaybackAction.EditorPrevious);
        Click("replay_editor_seek",TheatrePlaybackAction.EditorSeek);
        Check(memory.Seeks==1,"native event seek delegates to existing injected transport owner");
        Click("replay_editor_refresh",TheatrePlaybackAction.EditorRefresh);
        host.SetField(panel,"replay_editor_name","SYNTHETIC REVIEWED SEGMENT");
        Click("replay_editor_add_selection",TheatrePlaybackAction.EditorAddSelectionToReel);var confirmation=pages.Top;
        Check(confirmation!=panel&&memory.Commits==0,"native marked range edit stages before exact persisted confirmation");
        memory.Fail=true;Click("replay_editor_confirm_apply",TheatrePlaybackAction.EditorConfirm);
        Check(memory.Commits==0&&pages.Top==confirmation,"native backend write error retains exact pending edit");
        memory.Fail=false;Capture("reviewed-retry");Click("replay_editor_confirm_apply",TheatrePlaybackAction.EditorConfirm);
        Check(memory.Commits==1&&memory.Context.Reel.Single().Name=="SYNTHETIC REVIEWED SEGMENT"&&host.ReadField(panel,"replay_editor_name")=="SYNTHETIC REVIEWED SEGMENT","retry preserves native entered name and immutable reviewed range");
        Check(!page.HandleIntent(new(RmlUiIntentKind.ReplayAction,(int)TheatrePlaybackAction.EditorConfirm,confirmation,ulong.MaxValue)),"retired confirmation cannot duplicate saved edit");
        Click("replay_editor_tab",TheatrePlaybackAction.EditorTab);
        Click("replay_editor_select_range",TheatrePlaybackAction.EditorSelectRange);
        Click("replay_editor_refresh",TheatrePlaybackAction.EditorRefresh);
        Click("replay_editor_add_highlight",TheatrePlaybackAction.EditorAddHighlightToReel);Click("replay_editor_confirm_apply",TheatrePlaybackAction.EditorConfirm);
        Click("replay_editor_add_top",TheatrePlaybackAction.EditorAddTopHighlights);Click("replay_editor_confirm_apply",TheatrePlaybackAction.EditorConfirm);
        Check(memory.Context.Reel.Length==3,"selected and top automatic highlights append only on native confirmation");
        Click("replay_editor_tab",TheatrePlaybackAction.EditorTab);
        Click("replay_editor_remove_named",TheatrePlaybackAction.EditorRemoveNamedHighlight);Click("replay_editor_confirm_cancel",TheatrePlaybackAction.EditorCancel);
        Check(memory.Context.NamedHighlights.Length==1,"native cancellation preserves named highlight");
        Click("replay_editor_remove_named",TheatrePlaybackAction.EditorRemoveNamedHighlight);Click("replay_editor_confirm_apply",TheatrePlaybackAction.EditorConfirm);
        Check(memory.Context.NamedHighlights.IsEmpty,"native exact named highlight removal completes once");
        Click("replay_editor_tab",TheatrePlaybackAction.EditorTab);
        Click("replay_editor_move_down",TheatrePlaybackAction.EditorMoveDown);Click("replay_editor_confirm_apply",TheatrePlaybackAction.EditorConfirm);
        Click("replay_editor_next",TheatrePlaybackAction.EditorNext);
        Click("replay_editor_move_up",TheatrePlaybackAction.EditorMoveUp);Click("replay_editor_confirm_apply",TheatrePlaybackAction.EditorConfirm);
        Click("replay_editor_trim",TheatrePlaybackAction.EditorTrim);Click("replay_editor_confirm_apply",TheatrePlaybackAction.EditorConfirm);
        Click("replay_editor_camera",TheatrePlaybackAction.EditorCycleCamera);Click("replay_editor_confirm_apply",TheatrePlaybackAction.EditorConfirm);
        Click("replay_editor_remove",TheatrePlaybackAction.EditorRemove);Click("replay_editor_confirm_apply",TheatrePlaybackAction.EditorConfirm);
        Check(memory.Context.Reel.Length==2,"native reel order/trim/camera/remove all use staged owner commands");
        Click("replay_editor_clear",TheatrePlaybackAction.EditorClearReel);confirmation=pages.Top;memory.Current=false;Frame();
        Check(!host.FocusDocument(confirmation,"replay_editor_confirm_apply"),"owner change disables exact native clear confirmation");
        Capture("stale-clear");Click("replay_editor_confirm_cancel",TheatrePlaybackAction.EditorCancel);memory.Current=true;
        Click("replay_editor_refresh",TheatrePlaybackAction.EditorRefresh);
        Click("replay_editor_clear",TheatrePlaybackAction.EditorClearReel);Click("replay_editor_confirm_apply",TheatrePlaybackAction.EditorConfirm);
        Check(memory.Context.Reel.IsEmpty,"fresh explicit review clears only reviewed synthetic reel");
        foreach(var pair in new[]{("replay_editor_previous_shot",TheatrePlaybackAction.EditorPreviousShot),("replay_editor_current_shot",TheatrePlaybackAction.EditorShotAtPlayhead),("replay_editor_next_shot",TheatrePlaybackAction.EditorNextShot),("replay_editor_analytics_hud",TheatrePlaybackAction.EditorToggleAnalyticsHud),("replay_editor_network_hud",TheatrePlaybackAction.EditorToggleNetworkHud),("replay_editor_combat_hud",TheatrePlaybackAction.EditorToggleCombatHud),("replay_editor_shot_rays",TheatrePlaybackAction.EditorToggleShotRays)})Click(pair.Item1,pair.Item2);
        memory.Rendering=true;Frame();Check(!host.FocusDocument(panel,"replay_editor_add_selection")&&!host.FocusDocument(panel,"replay_editor_next_shot"),"existing video exporter blocks native editor playback mutations");memory.Rendering=false;
        Click("replay_editor_tab",TheatrePlaybackAction.EditorTab);Check(pages.Top==panel,"analytics remains in same native editor workspace");
        Click("replay_editor_close",TheatrePlaybackAction.EditorClose);Check(pages.Top==playback&&memory.Retired==1,"native close retires inspection only");
        Check(!page.HandleIntent(new(RmlUiIntentKind.ReplayAction,(int)TheatrePlaybackAction.EditorSeek,panel,ulong.MaxValue)),"retired editor cannot seek any replacement transport");
        Check(!DemoPlayback.IsActive&&GL.GetError()==ErrorCode.NoError,"native editor fixture ends without replay files/world/encoder or GL errors");
        void Frame(){page.Present();host.Update();GL.BindFramebuffer(FramebufferTarget.Framebuffer,0);GL.Viewport(0,0,width,height);GL.Disable(EnableCap.ScissorTest);GL.ClearColor(.2f,.3f,.4f,1);GL.Clear(ClearBufferMask.ColorBufferBit|ClearBufferMask.DepthBufferBit|ClearBufferMask.StencilBufferBit);RmlUiOpenGlFrame.Draw(host,width,height);}
        void Settle(){var timer=Stopwatch.StartNew();do{Frame();Thread.Sleep(10);}while(timer.ElapsedMilliseconds<200);}
        void Click(string id,TheatrePlaybackAction action){Frame();var target=pages.Top;Check(host.FocusDocument(target,id),"actual native editor focus: "+id);Settle();Check(host.TryGetElementBounds(target,id,out float x,out float y,out float w,out float h)&&w>0&&h>0&&x+w/2>=0&&x+w/2<width&&y+h/2>=0&&y+h/2<height,"reachable native editor geometry: "+id);host.DiscardIntents();host.Input.PointerButton(0,x+w/2,y+h/2,true);host.Input.PointerButton(0,x+w/2,y+h/2,false);host.Update();bool received=false;while(host.TryTakeIntent(out var intent)){bool accepted=page.HandleIntent(intent);if(intent.Kind!=RmlUiIntentKind.ReplayAction||intent.Argument!=(int)action)continue;Check(accepted&&intent.Document==target,"actual native editor action: "+id);Check(!page.HandleIntent(intent),"duplicate native editor sequence refused: "+id);received=true;}Check(received,"expected native editor intent emitted: "+id);Frame();}
        void Capture(string name){Settle();var pixels=FinalCompositeCapture.Read(width,height);string folder=Path.GetFullPath(Path.Combine("artifacts","rmlui-editor-rml59"));Directory.CreateDirectory(folder);string path=Path.Combine(folder,$"synthetic-{name}-{width}x{height}.ppm");using var file=File.Create(path);file.Write(Encoding.ASCII.GetBytes($"P6\n# SYNTHETIC EDITOR - NO REPLAY WORLD\n{width} {height}\n255\n"));for(int row=height-1;row>=0;row--)file.Write(pixels,row*width*3,width*3);FinalCompositeCapture.WriteEvidence(path,width,height,"Actual native replay editor and reviewed metadata confirmations with injected memory entries only; no real replay/world/transport acceptance.");}
    }
    private static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);Console.WriteLine("RML EDITOR GL PASS "+message);}
    private sealed class NoStudio:ITheatrePlaybackBackend{public bool OpenStudio(string path,out string? error)=>throw new InvalidOperationException("No legacy Studio launch permitted.");}
    private sealed partial class MemoryEditor:ITheatreEditorBackend
    {
        internal bool Current=true,Rendering,Fail;internal int Commits,Retired,Seeks;
        internal TheatreEditorContext Context=new(1,new object(),new object(),"SYNTHETIC.ppdemo","SYNTHETIC.ppdemo","SOURCE","ANNOTATION","REEL",10,600,0,10,80,true,
            ImmutableArray.Create(new TheatreEditorPlayer(0,"Hunter"),new TheatreEditorPlayer(1,"Target")),
            ImmutableArray.Create(new TheatreEditorEntry("event:0",10,10,"SHOT","SYNTHETIC",0,10,Target:1),new TheatreEditorEntry("event:1",200,200,"KILL","SYNTHETIC",1,2,Target:0)),
            ImmutableArray.Create(new TheatreEditorEntry("highlight:0",0,100,"KILL","SYNTHETIC",0,FocusFrame:30)),
            ImmutableArray.Create(new TheatreEditorEntry(Guid.NewGuid().ToString("N"),0,20,"NAMED","SYNTHETIC")),ImmutableArray<TheatreEditorEntry>.Empty,
            ImmutableArray.Create(new TheatreEditorEntry("analytics",0,0,"TOTAL","SYNTHETIC")),new object());
        public TheatreEditorContext Capture()=>Context;
        public TheatreEditorLive ReadLive(TheatreEditorContext context)=>new(Current,Rendering,"SYNTHETIC SHOT EVIDENCE",false,false,false,false);
        public void Seek(TheatreEditorContext context,TheatreEditorEntry exact,bool range)=>Seeks++;
        public void InspectShot(TheatreEditorContext context,TheatreEditorEntry? previous,int direction,int player){}
        public void ToggleHud(TheatreEditorContext context,TheatreEditorHud hud){}
        public TheatreEditorContext Commit(TheatreEditorEdit reviewed){if(Fail)throw new IOException("SYNTHETIC SHARING VIOLATION");Commits++;Context=reviewed.Kind==TheatreEditorEditKind.RemoveNamedHighlight?Context with{NamedHighlights=reviewed.After}:Context with{Reel=reviewed.After};return Context;}
        public void Retire()=>Retired++;
    }
}
