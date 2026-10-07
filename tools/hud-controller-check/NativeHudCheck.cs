#if MPHREAD_RMLUI_POC
using System.Runtime.InteropServices;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Pages.Hud;
using MphRead.Mods.Render.Hud;

internal static class NativeHudCheck
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]private delegate int Bounds(ulong document,[MarshalAs(UnmanagedType.LPUTF8Str)]string id,out float x,out float y,out float width,out float height);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]private delegate int Count();
    [StructLayout(LayoutKind.Sequential)]private struct TextureData{public uint Size,Width,Height,Reserved;public ulong Handle;public nint Pixels;}
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]private delegate int Texture(int index,ref TextureData texture);
    [StructLayout(LayoutKind.Sequential)]private struct DrawData{public uint Size,Kind;public ulong Geometry,Texture;public float TranslationX,TranslationY;public int X,Y,Width,Height;public uint Enabled,Operation;[MarshalAs(UnmanagedType.ByValArray,SizeConst=16)]public float[]Transform;}
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]private delegate int Draw(int index,ref DrawData command);
    public static void Run(string library,string assets)
    {
        nint module=NativeLibrary.Load(Path.GetFullPath(library));
        string directory=Path.Combine(Path.GetTempPath(),"prime-hud-native-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try
        {
            NativeLibrary.SetDllImportResolver(typeof(RmlUiHost).Assembly,(name,_,_)=>name=="ProjectPrime.RmlUi.Native"?module:0);
            var bounds=Marshal.GetDelegateForFunctionPointer<Bounds>(NativeLibrary.GetExport(module,"pp_rmlui_document_element_bounds"));
            var count=Marshal.GetDelegateForFunctionPointer<Count>(NativeLibrary.GetExport(module,"pp_rmlui_draw_command_count"));
            var textureCount=Marshal.GetDelegateForFunctionPointer<Count>(NativeLibrary.GetExport(module,"pp_rmlui_draw_texture_count"));
            var texture=Marshal.GetDelegateForFunctionPointer<Texture>(NativeLibrary.GetExport(module,"pp_rmlui_draw_texture"));
            var draw=Marshal.GetDelegateForFunctionPointer<Draw>(NativeLibrary.GetExport(module,"pp_rmlui_draw_command"));
            int checks=0;void Check(bool value,string message){if(!value)throw new InvalidOperationException("HUD native: "+message);checks++;}
            foreach(var viewport in new[]{(1280,720,1f),(1920,1080,1f),(960,540,1f),(1920,1080,2f)})
            {
                using var host=new RmlUiHost();Check(host.Initialize(viewport.Item1,viewport.Item2,viewport.Item3,Path.GetFullPath(assets),RmlUiRenderBackend.DrawList),"initialize real native core");
                host.FocusDocument(host.HomeDocument,"activity_selector");
                using var pages=new RmlUiPageManager(host);using var controller=new HudEditorController(HudProfileDefaults.Create("Project Prime"),new HudBoundary());
                var bitmap=new HudPreviewBitmap(Path.Combine(directory,"cache"),nativeRoot:"");using var page=new HudEditorPagePresenter(host,pages,controller,bitmap);
                page.Open();host.Update();Check(page.IsOpen&&host.FocusedElement()=="hud_use","native HUD page opens with settings handoff focus");
                Check(bounds(page.Document.DocumentId,"hud_canvas",out float x,out float y,out float width,out float height)!=0&&width>100&&height>100,"real native preview canvas has positive bounds");
                page.SetCanvasSize(width,height);host.Update();
                Check(bounds(page.Document.DocumentId,"hud_title",out _,out float titleY,out float titleWidth,out float titleHeight)!=0&&titleWidth>0&&titleHeight>0
                    &&bounds(page.Document.DocumentId,"hud_intro",out _,out float introY,out float introWidth,out float introHeight)!=0&&introWidth>0&&introHeight>0&&introY>=titleY+titleHeight-1,
                    "native block heading and introduction have positive widths and vertical separation");
                var deadline=System.Diagnostics.Stopwatch.StartNew();ulong previewTexture=0;
                do
                {
                    page.Present();host.Update();host.Render(viewport.Item1,viewport.Item2);
                    int expectedW=Math.Max(1,(int)Math.Round(width*Math.Min(1,1024f/Math.Max(width,height)))),expectedH=Math.Max(1,(int)Math.Round(height*Math.Min(1,1024f/Math.Max(width,height))));
                    for(int i=0;i<textureCount();i++){var candidate=new TextureData{Size=(uint)Marshal.SizeOf<TextureData>()};if(texture(i,ref candidate)!=0&&candidate.Width==expectedW&&candidate.Height==expectedH&&candidate.Pixels!=0)previewTexture=candidate.Handle;}
                    if(previewTexture==0)Thread.Sleep(1);
                }while(previewTexture==0&&deadline.Elapsed<TimeSpan.FromSeconds(5));
                Check(previewTexture!=0,"shared real HUD geometry uploads as native preview texture");
                bool drawn=false;for(int i=0;i<count();i++){var command=new DrawData{Size=(uint)Marshal.SizeOf<DrawData>(),Transform=new float[16]};drawn|=draw(i,ref command)!=0&&command.Texture==previewTexture&&command.Geometry!=0;}
                Check(drawn,"HUD preview texture participates in native draw geometry");
                void Activate(string id,RmlUiIntentKind kind)
                {
                    Check(host.FocusDocument(page.Document,id),"focus "+id);host.Input.Key(2,true);host.Input.Key(2,false);host.Update();
                    Check(host.TryTakeIntent(out var intent)&&intent.Kind==kind&&page.HandleIntent(intent),"real DOM action "+id);Check(!host.TryTakeIntent(out _),"one native activation emitted");
                }
                for(int i=0;i<14;i++){Activate("hud_pick"+i,RmlUiIntentKind.HudElement);Check(controller.Snapshot().Selected==i,"all registered HUD elements remain editable");}
                Activate("hud_pick1",RmlUiIntentKind.HudElement);float before=controller.CopyDraft().Elements["core.health"].OffsetX;
                Activate("hud_nudge_right",RmlUiIntentKind.HudAction);Check(controller.CopyDraft().Elements["core.health"].OffsetX==before+1,"native nudge reaches detached shared layout");
                Activate("hud_undo",RmlUiIntentKind.HudAction);Check(controller.CopyDraft().Elements["core.health"].OffsetX==before,"native undo reverses the layout edit");
                Check(host.FocusDocument(page.Document,"hud_canvas")&&page.KeyDown(OpenTK.Windowing.GraphicsLibraryFramework.Keys.Right,RmlUiInputModifiers.Shift)
                    &&controller.CopyDraft().Elements["core.health"].OffsetX==before+10,"canvas shortcut applies shifted logical nudge");
                Check(page.KeyDown(OpenTK.Windowing.GraphicsLibraryFramework.Keys.Z,RmlUiInputModifiers.Command)
                    &&controller.CopyDraft().Elements["core.health"].OffsetX==before,"Command undo uses authoring history on canvas");
                Check(page.KeyDown(OpenTK.Windowing.GraphicsLibraryFramework.Keys.C,RmlUiInputModifiers.Control)
                    &&HudProfileStore.Parse(host.ReadClipboard()).Elements.Count==14,"canvas copy exports full validated profile through native clipboard");
                Check(host.FocusDocument(page.Document,"hud_name")&&!page.KeyDown(OpenTK.Windowing.GraphicsLibraryFramework.Keys.C,RmlUiInputModifiers.Control),"editable text fields retain native clipboard ownership");
                string json="{\"name\":\"λ Prime\",\"padding\":\""+String.Concat(Enumerable.Repeat("λ😀",1000))+"\"}";
                host.SetField(page.Document,"hud_json",json);
                Check(host.ReadField(page.Document,"hud_json",HudProfileStore.MaximumBytes)==json,"native textarea retains full large Unicode profile paste");
                bool overflow=false;try{host.ReadField(page.Document,"hud_json",4096);}catch(InvalidOperationException){overflow=true;}
                Check(overflow,"bounded native field reads reject truncation");
                Activate("hud_import_json",RmlUiIntentKind.HudAction);Check(controller.Snapshot().Name=="λ Prime"&&controller.Snapshot().Error.Length==0,"native import uses actual profile parser");
                Activate("hud_export_json",RmlUiIntentKind.HudAction);
                Check(HudProfileStore.Parse(host.ReadField(page.Document,"hud_json",HudProfileStore.MaximumBytes)).Name=="λ Prime","native JSON export retains detached profile name");
                controller.ReportFailure("<button id=\"injected_action\">λ😀</button>");page.Present();host.Update();
                Check(bounds(page.Document.DocumentId,"injected_action",out _,out _,out _,out _) == 0,"untrusted profile errors stay escaped text");
                host.SetField(page.Document,"hud_name","λ Prime Custom");Activate("hud_use",RmlUiIntentKind.HudAction);
                Check(page.TryTakeAccepted(out var accepted)&&accepted.Name=="λ Prime Custom"&&!page.TryTakeAccepted(out _),"native Use hands one complete validated draft to settings");
                var document=page.Document;var cancellation=host.DocumentCancellation(document);
                Check(page.Close()&&cancellation.IsCancellationRequested&&host.FocusedElement()=="activity_selector","closing retires native lifetime and restores home focus");
                Check(!page.HandleIntent(new(RmlUiIntentKind.HudAction,(int)HudEditorAction.Use,document,ulong.MaxValue)),"retired document cannot submit another draft");
            }
            Console.WriteLine($"HUD NATIVE PASS {checks} checks (actual DOM/14 elements/shared preview texture/large Unicode textarea/lifetimes; four viewport-density combinations).");
        }
        finally{NativeLibrary.Free(module);try{Directory.Delete(directory,true);}catch(IOException){}}
    }
}
#endif
