#if MPHREAD_RMLUI_GL_SMOKE
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
                float originalAspect=controller.Snapshot().PreviewWidth;
                Activate("hud_aspect",RmlUiIntentKind.HudAction);Activate("hud_aspect_previous",RmlUiIntentKind.HudAction);
                Check(controller.Snapshot().PreviewWidth==originalAspect,"real native previous preview choice reverses forward selection");
                for(int i=0;i<HudProfileDefaults.ElementIds.Length;i++){Activate("hud_pick"+i,RmlUiIntentKind.HudElement);Check(controller.Snapshot().Selected==i,"all registered HUD elements remain editable");}
                Activate("hud_pick14",RmlUiIntentKind.HudElement);
                int movementIndex=HudProfileDefaults.MovementTelemetryIndex;
                const string movementId=HudProfileDefaults.MovementTelemetryId;
                var movementSnapshot=controller.Snapshot();
                var movementBefore=movementSnapshot.Elements[movementIndex].Bounds;
                Check(movementSnapshot.Selected==movementIndex&&movementSnapshot.Elements[movementIndex].Selected
                    &&movementSnapshot.Elements[movementIndex].Id==movementId,
                    "real native movement picker selects its registered preview rectangle");
                host.Update();
                Check(bounds(page.Document.DocumentId,"hud_element14",out _,out _,out float movementWidth,out float movementHeight)!=0
                    &&movementWidth>0&&movementHeight>0
                    &&Math.Abs(movementWidth-movementBefore.Width)<=2*viewport.Item3+1
                    &&Math.Abs(movementHeight-movementBefore.Height)<=2*viewport.Item3+1,
                    "native movement preview dimensions match the shared editor bounds");
                float movementX=controller.CopyDraft().Elements[movementId].OffsetX;
                Activate("hud_nudge_right",RmlUiIntentKind.HudAction);
                var movementNudged=controller.Snapshot().Elements[movementIndex].Bounds;
                Check(controller.CopyDraft().Elements[movementId].OffsetX==movementX+1
                    &&movementNudged.X>movementBefore.X&&movementNudged.Width==movementBefore.Width,
                    "real native movement nudge changes its draft and preview position only");
                Activate("hud_undo",RmlUiIntentKind.HudAction);
                Check(controller.CopyDraft().Elements[movementId].OffsetX==movementX
                    &&controller.Snapshot().Elements[movementIndex].Bounds==movementBefore,
                    "real native movement undo restores exact draft and preview bounds");
                Activate("hud_section_layout",RmlUiIntentKind.HudAction);
                void Inline(string path,string value)
                {
                    int row=controller.Snapshot().Properties.Select((property,index)=>(property,index)).Single(item=>item.property.Path==path).index;
                    if(controller.Snapshot().Properties[row].Range!=null)Activate("hud_property"+row+"_exact",RmlUiIntentKind.HudProperty);
                    host.SetField(page.Document,"hud_property"+row+"_input",value);
                    // Capture each precise value before moving to another row's optional field.
                    Activate("hud_property"+row+"_info",RmlUiIntentKind.HudProperty);
                }
                Activate("hud_section_global",RmlUiIntentKind.HudAction);
                host.SetField(page.Document,"hud_property_search","/typography/globalFont");
                Activate("hud_search",RmlUiIntentKind.HudAction);
                Check(controller.Snapshot().Properties.Single().Path=="/typography/globalFont"
                    &&host.ReadField(page.Document,"hud_property0_input")==HudFonts.All[0].Name,
                    "native lettering field displays the registered friendly font name");
                host.SetField(page.Document,"hud_property0_input",HudFonts.All[2].Name);
                Activate("hud_section_global",RmlUiIntentKind.HudAction);
                Check(controller.CopyDraft().Typography.GlobalFont==HudFonts.All[2].Id,
                    "native pending font text is converted to its canonical profile id");
                Activate("hud_section_global",RmlUiIntentKind.HudAction);
                host.SetField(page.Document,"hud_property_search","/typography/globalFont");
                Activate("hud_search",RmlUiIntentKind.HudAction);
                Check(host.ReadField(page.Document,"hud_property0_input")==HudFonts.All[2].Name,
                    "native presenter refreshes the committed font as a friendly name");
                host.SetField(page.Document,"hud_property0_input",HudFonts.All[1].Id);
                host.SetField(page.Document,"hud_property_search","/typography/globalFont");
                Activate("hud_search",RmlUiIntentKind.HudAction);
                Check(controller.CopyDraft().Typography.GlobalFont==HudFonts.All[1].Id
                    &&host.ReadField(page.Document,"hud_property0_input")==HudFonts.All[1].Name,
                    "native capture canonicalizes a legacy font id in the visible choice field");
                Activate("hud_property0_next",RmlUiIntentKind.HudProperty);
                Check(controller.CopyDraft().Typography.GlobalFont==HudFonts.All[2].Id
                    &&host.ReadField(page.Document,"hud_property0_input")==HudFonts.All[2].Name,
                    "native font arrow advances from the canonicalized field value");
                host.SetField(page.Document,"hud_property_search","/typography/globalFont");
                Activate("hud_search",RmlUiIntentKind.HudAction);
                Check(controller.CopyDraft().Typography.GlobalFont==HudFonts.All[2].Id,
                    "a later native capture does not reapply the stale legacy font id");
                Activate("hud_property0_reset",RmlUiIntentKind.HudProperty);
                Check(controller.CopyDraft().Typography.GlobalFont==HudFonts.All[0].Id,
                    "native font reset restores the fixture's default typography before geometry checks");
                Activate("hud_section_layout",RmlUiIntentKind.HudAction);
                Inline("/elements/"+movementId+"/offsetX","100");Inline("/elements/"+movementId+"/offsetY","-220");
                Inline("/elements/"+movementId+"/scale","1.5");
                Activate("hud_section_appearance",RmlUiIntentKind.HudAction);
                Inline("/elements/"+movementId+"/opacity","0.4");Inline("/elements/"+movementId+"/color","#44AAFF");
                Activate("hud_workspace_profiles",RmlUiIntentKind.HudAction);
                var movementApplied=controller.CopyDraft().Elements[movementId];
                var movementAfter=controller.Snapshot().Elements[movementIndex];
                string movementDiagnostics=$"before={movementBefore}; after={movementAfter.Bounds}; fields=(x={movementApplied.OffsetX}, y={movementApplied.OffsetY}, scale={movementApplied.Scale}, opacity={movementApplied.Opacity}, color={movementApplied.Color}); preview=(selected={movementAfter.Selected}, opacity={movementAfter.Opacity}, color={movementAfter.Color}); expectedWidth={movementBefore.Width*1.5f}";
                Check(movementApplied.OffsetX==100&&movementApplied.OffsetY==-220&&movementApplied.Scale==1.5f
                    &&movementApplied.Opacity==.4f&&movementApplied.Color=="#44AAFF"
                    &&movementAfter.Selected&&movementAfter.Opacity==.4f&&movementAfter.Color=="#44AAFF"
                    &&Math.Abs(movementAfter.Bounds.Width-movementBefore.Width*1.5f)<.01f,
                    "real native movement layout fields apply placement, scale, opacity and color to preview and draft; "+movementDiagnostics);
                for(int edit=0;edit<5;edit++)Activate("hud_undo",RmlUiIntentKind.HudAction);
                Check(controller.CopyDraft().Elements[movementId].Scale==1
                    &&controller.Snapshot().Elements[movementIndex].Bounds==movementBefore,
                    "native movement inline fields each retain reversible authoring history");
                Activate("hud_workspace_elements",RmlUiIntentKind.HudAction);
                Activate("hud_pick1",RmlUiIntentKind.HudElement);float before=controller.CopyDraft().Elements["core.health"].OffsetX;
                Activate("hud_nudge_right",RmlUiIntentKind.HudAction);Check(controller.CopyDraft().Elements["core.health"].OffsetX==before+1,"native nudge reaches detached shared layout");
                Activate("hud_undo",RmlUiIntentKind.HudAction);Check(controller.CopyDraft().Elements["core.health"].OffsetX==before,"native undo reverses the layout edit");
                Check(host.FocusDocument(page.Document,"hud_canvas")&&page.KeyDown(OpenTK.Windowing.GraphicsLibraryFramework.Keys.Right,RmlUiInputModifiers.Shift)
                    &&controller.CopyDraft().Elements["core.health"].OffsetX==before+10,"canvas shortcut applies shifted logical nudge");
                Check(page.KeyDown(OpenTK.Windowing.GraphicsLibraryFramework.Keys.Z,RmlUiInputModifiers.Command)
                    &&controller.CopyDraft().Elements["core.health"].OffsetX==before,"Command undo uses authoring history on canvas");
                Check(page.KeyDown(OpenTK.Windowing.GraphicsLibraryFramework.Keys.C,RmlUiInputModifiers.Control)
                    &&HudProfileStore.Parse(host.ReadClipboard()).Elements.Count==HudProfileDefaults.ElementIds.Length,"canvas copy exports full validated profile through native clipboard");
                Activate("hud_workspace_profiles",RmlUiIntentKind.HudAction);
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
            Console.WriteLine($"HUD NATIVE PASS {checks} checks (actual DOM/registered elements/shared preview texture/large Unicode textarea/lifetimes; four viewport-density combinations).");
        }
        finally{NativeLibrary.Free(module);try{Directory.Delete(directory,true);}catch(IOException){}}
    }
}
#endif
