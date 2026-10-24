using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using MphRead.Mods.Input;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Pages.Hud;
using MphRead.Mods.Render.Hud;
using OpenTK.Windowing.GraphicsLibraryFramework;

internal static class HudInlineGlAcceptance
{
    internal static void Run(RmlUiHost host,RmlUiPageManager pages,Action<string>? capture=null)
    {
        void Check(bool value,string label)
        {
            if(!value)throw new InvalidOperationException("HUD INLINE GL: "+label);
            Console.WriteLine("HUD INLINE GL PASS "+label);
        }
        const BindingFlags fields=BindingFlags.NonPublic|BindingFlags.Instance;
        int writes=0;HudProfile? saved=null;
        using var page=new NativeHudEditorPage(host,pages,HudProfileDefaults.Create("Project Prime"),asModal:true,
            options:new[]{new HudEditorOption("ds","Screen proportions","4:3",ImmutableArray.Create("4:3","5:3"),Section:2,Group:"Stacked dual screens")},
            autoSave:(profile,_)=>{saved=profile.DeepClone();writes++;return true;});
        page.Open();host.Update();
        if(host.TryGetElementBounds(page.Document,"hud_canvas",out _,out _,out float canvasWidth,out float canvasHeight))page.SetCanvasSize(canvasWidth,canvasHeight);
        var owner=(HudEditorController)typeof(NativeHudEditorPage).GetField("_controller",fields)!.GetValue(page)!;
        var presenter=(HudEditorPagePresenter)typeof(NativeHudEditorPage).GetField("_presenter",fields)!.GetValue(page)!;
        void Refresh(){page.Refresh();host.Update();}
        void Settle()
        {
            Refresh();typeof(NativeHudEditorPage).GetField("_lastEdit",fields)!.SetValue(page,Stopwatch.GetTimestamp()-Stopwatch.Frequency);Refresh();
        }
        void Activate(string id)
        {
            var document=presenter.ForegroundDocument;
            Check(host.FocusDocument(document,id),"focus native inline control "+id);
            host.Input.Key(2,true);host.Input.Key(2,false);host.Update();
            Check(host.TryTakeIntent(out var intent)&&page.Handle(intent),"native inline control emits its stable row intent "+id);
            Check(!host.TryTakeIntent(out _),"one inline activation emits one intent");Refresh();
        }
        void Capture(string name)
        {
            var watch=Stopwatch.StartNew();
            do{Refresh();System.Threading.Thread.Sleep(5);}while(watch.ElapsedMilliseconds<250);
            capture?.Invoke(name);
        }
        void Search(string query)
        {
            host.SetField(page.Document,"hud_property_search",query);Activate("hud_search");
        }
        Check(owner.Snapshot().Properties.All(row=>row.Path.StartsWith("/crosshair/",StringComparison.Ordinal))
            &&owner.Snapshot().Properties.Any(row=>row.Path=="/crosshair/scale")
            &&owner.Snapshot().Properties.Any(row=>row.Path=="/crosshair/gap"),
            "the first crosshair page exposes reticle basics before hit markers and advanced part styling");
        Capture("hud-inline-appearance");
        int choice=owner.Snapshot().Properties.Select((row,index)=>(row,index)).First(item=>!item.row.Choices.IsEmpty).index;
        string initial=owner.Snapshot().Properties[choice].Value;
        Activate("hud_property"+choice+"_next");
        Check(owner.Snapshot().Properties[choice].Value!=initial,"choice arrows edit their own row directly");
        Activate("hud_property"+choice+"_previous");
        Check(owner.Snapshot().Properties[choice].Value==initial,"previous arrow reverses the same row choice");
        Search("/crosshair/lengthX");
        Check(owner.Snapshot().Properties.Length==1,"Appearance search remains within its component section");
        const string input="hud_property0_input";
        const string slider="hud_property0_slider";
        Check(host.TryGetElementBounds(page.Document,slider,out _,out _,out float sw,out float sh)&&sw>30&&sh>20,
            "numerical HUD values use a visible inline slider by default");
        Check(!host.FocusDocument(page.Document,input),"precise numerical text stays hidden until requested");
        host.SetField(page.Document,slider,"500");
        Check(host.FocusDocument(page.Document,slider),"focus the numerical slider");
        host.Input.Key(8,true);host.Input.Key(8,false);host.Update();
        Check(host.TryTakeIntent(out var sliderIntent)&&sliderIntent.Kind==RmlUiIntentKind.HudProperty&&page.Handle(sliderIntent),"native slider change edits its own row");
        Check(!host.TryTakeIntent(out _),"one numerical slider change emits one intent");
        var range=owner.Snapshot().Properties[0].Range!;
        float midpoint=Math.Clamp(MathF.Round((range.Minimum+(range.Maximum-range.Minimum)*.501f)/range.Step)*range.Step,range.Minimum,range.Maximum);
        Check(Math.Abs(owner.CopyDraft().Crosshair.LengthX-midpoint)<.001,"slider uses the property's validated range and step");
        Capture("hud-numeric-slider");
        Activate("hud_property0_exact");
        Check(host.TryGetElementBounds(page.Document,"hud_property0_label",out float lx,out float ly,out float lw,out float lh)
            &&host.TryGetElementBounds(page.Document,input,out float ix,out float iy,out float iw,out float ih)&&lw>0&&lh>0&&iw>0&&ih>0
            &&(ix>=lx+lw-1||iy>=ly+lh-1),"the editable value sits beside its own label without overlap");
        host.FocusDocument(page.Document,input);host.SetField(page.Document,input,"5.");
        for(int i=0;i<20;i++)presenter.Present();
        Check(host.ReadField(page.Document,input)=="5."&&host.FocusedElement()==input,"native field text and focus survive repeated HUD refreshes");
        host.SetField(page.Document,input,"5.5");Settle();
        Check(owner.CopyDraft().Crosshair.LengthX==5.5f&&writes>0,"settled inline text automatically updates and saves the intended property");
        Activate("hud_property0_reset");
        Check(owner.CopyDraft().Crosshair.LengthX==HudProfileDefaults.Create("Project Prime").Crosshair.LengthX,"native per-row Reset restores that exact value without a bottom editor");
        host.SetField(page.Document,input,"5.5");Settle();
        int beforeInvalid=writes;host.SetField(page.Document,input,"unfinished");Settle();
        Check(writes==beforeInvalid&&host.ReadField(page.Document,input)=="unfinished","invalid inline text stays editable without changing saved values");
        Activate("hud_section_layout");
        Check(owner.Snapshot().InspectorSection==1&&owner.CopyDraft().Crosshair.LengthX==5.5f,
            "section navigation drops unfinished text while preserving accepted edits");
        Capture("hud-inline-position");Activate("hud_section_global");Search("Screen proportions");
        Check(owner.Snapshot().Properties.Length==1&&owner.Snapshot().Properties[0].Path=="/options/ds",
            "shared dual-screen proportions are accessible in HUD-wide");
        Activate("hud_property0_next");Check(owner.CopyOptions()[0].Value=="5:3","dual-screen row exposes its 5:3 option inline");
        Activate("hud_property0_previous");Check(owner.CopyOptions()[0].Value=="4:3","dual-screen row returns directly to 4:3");
        Activate("hud_pick1");Activate("hud_section_decorations");
        Check(owner.Snapshot().Properties.All(row=>row.Path.StartsWith("/decorations/styles/health/"))
            &&owner.Snapshot().Properties.All(row=>!row.Label.StartsWith("Decorations",StringComparison.OrdinalIgnoreCase)),
            "Decorations contains only the selected health artwork with short labels");
        Capture("hud-inline-decorations");
        Activate("hud_section_typography");
        Check(owner.Snapshot().Properties.All(row=>row.Path.StartsWith("/typography/styles/core.health/")),
            "Lettering contains only the selected health text style");
        Search("/font");
        Check(owner.Snapshot().Properties.Length==1,"health font is available without enabling unrelated controls first");
        string oldFont=owner.CopyDraft().Typography.Get("core.health").Font;
        Activate("hud_property0_choice");
        Check(owner.CopyDraft().Typography.Get("core.health").Font!=oldFont&&owner.CopyDraft().Typography.Get("core.health").Enabled,
            "choosing a font enables and updates that element's live text style");
        Capture("hud-font-preview");
        Activate("hud_pick0");Activate("hud_section_appearance");Search("/crosshair/color");
        Check(owner.Snapshot().Properties.Length==1,"color row belongs to the selected crosshair");
        Activate("hud_property0_color");
        Check(presenter.PopupOpen&&pages.Top==presenter.ForegroundDocument&&pages.ModalCount==2,
            "HUD color palette owns a separate foreground modal");
        Check(host.TryGetElementBounds(presenter.ForegroundDocument,"hud_color_palette6",out _,out _,out float tileWidth,out float tileHeight)&&tileWidth>=30&&tileHeight>=30,
            "palette swatches retain usable width and height at this viewport");
        Check(!host.FocusDocument(page.Document,"hud_cancel"),"palette blocks the underlying editor controls");
        for(int i=0;i<12;i++){host.Input.Key(1,true);host.Input.Key(1,false);host.Update();}
        Check(host.FocusedElement().StartsWith("hud_color_",StringComparison.Ordinal),"Tab traversal stays in the color palette above a modal editor");
        Activate("hud_color_palette6");
        Check(owner.CopyDraft().Crosshair.Color==HudColorSelection.Palette[6]&&HudColorSelection.Recent[0]==HudColorSelection.Palette[6],
            "palette selection updates the draft and recent colors");
        Capture("hud-color-palette");
        Check(page.KeyDown(Keys.Escape,default)&&!presenter.PopupOpen&&!page.Closed&&pages.Top==page.Document,
            "Escape dismisses only the color palette and restores its editor");
        Activate("hud_property0_color");
        Check(host.FocusDocument(presenter.ForegroundDocument,"hud_color_recent0"),"recent color remains available on reopening");
        Activate("hud_color_done");Check(!presenter.PopupOpen&&!page.Closed,"Done remains reachable and dismisses only the color dialog");
        Activate("hud_property0_color");
        page.Back();Check(!presenter.PopupOpen&&!page.Closed,"Back dismisses the palette before the HUD editor");
        page.Back();Check(page.Closed&&saved?.Crosshair.Color==HudColorSelection.Palette[6],"Escape saves the accepted palette color and returns from the HUD editor");
        NativeRangePointerPumpRegression(host,pages);
    }

    private static void NativeRangePointerPumpRegression(RmlUiHost host,RmlUiPageManager pages)
    {
        const BindingFlags fields=BindingFlags.NonPublic|BindingFlags.Instance;
        int oldWidth=(int)typeof(RmlUiHost).GetField("_width",fields)!.GetValue(host)!;
        int oldHeight=(int)typeof(RmlUiHost).GetField("_height",fields)!.GetValue(host)!;
        float density=(float)typeof(RmlUiHost).GetField("_density",fields)!.GetValue(host)!;
        void Check(bool value,string label)
        {
            if(!value)throw new InvalidOperationException("HUD POINTER GL: "+label);
            Console.WriteLine("HUD POINTER GL PASS "+label);
        }
        try
        {
            for(int viewport=0;viewport<2;viewport++)
            {
                int width=viewport==0?1280:640,height=viewport==0?800:360;
                host.Resize(width,height,density);host.Update();
                int writes=0;HudProfile? saved=null;
                using var page=new NativeHudEditorPage(host,pages,HudProfileDefaults.Create("Project Prime"),asModal:true,
                    autoSave:(profile,_)=>{saved=profile.DeepClone();writes++;return true;});
                page.Open();host.Update();
                var owner=(HudEditorController)typeof(NativeHudEditorPage).GetField("_controller",fields)!.GetValue(page)!;
                var presenter=(HudEditorPagePresenter)typeof(NativeHudEditorPage).GetField("_presenter",fields)!.GetValue(page)!;
                void Refresh(){page.Refresh();host.Update();}
                void Settle()
                {
                    Refresh();typeof(NativeHudEditorPage).GetField("_lastEdit",fields)!.SetValue(page,Stopwatch.GetTimestamp()-Stopwatch.Frequency);Refresh();
                }
                void Activate(string id)
                {
                    Check(host.FocusDocument(page.Document,id),$"{width}x{height}: focus native {id}");
                    host.Input.Key(2,true);host.Input.Key(2,false);host.Update();
                    Check(host.TryTakeIntent(out var intent)&&page.Handle(intent),$"{width}x{height}: native action {id}");
                    Check(!host.TryTakeIntent(out _),$"{width}x{height}: one action for {id}");Refresh();
                }
                void PumpBeforeDrain(float canvasWidth,float canvasHeight)
                {
                    for(int i=0;i<3;i++)
                    {
                        page.HandleControllerAxes(new GamepadSnapshot(null,default,0),Environment.TickCount64);
                        page.SetCanvasSize(canvasWidth,canvasHeight);presenter.Present();page.Refresh();host.Update();
                    }
                }
                float SliderPosition()
                {
                    var row=owner.Snapshot().Properties[0];var range=row.Range!;
                    float number=float.Parse(row.Value,CultureInfo.InvariantCulture);
                    return Math.Clamp((number-range.Minimum)/(range.Maximum-range.Minimum)*1000,0,1000);
                }
                void CheckSliderMatchesModel(string label)
                {
                    Check(float.TryParse(host.ReadField(page.Document,"hud_property0_slider"),NumberStyles.Float,CultureInfo.InvariantCulture,out float actual)
                        &&Math.Abs(actual-SliderPosition())<.01f,$"{width}x{height}: {label} resynchronizes slider to model");
                }
                void DrainRangeChange(string expectedPosition,string beforeValue,string label)
                {
                    Check(Math.Abs(float.Parse(host.ReadField(page.Document,"hud_property0_slider"),CultureInfo.InvariantCulture)
                        -float.Parse(expectedPosition,CultureInfo.InvariantCulture))<.01f,
                        $"{width}x{height}: {label} survives repeated present/refresh/canvas/controller pumps before intent drain");
                    bool received=false;
                    while(host.TryTakeIntent(out var intent))
                    {
                        Check(intent.Kind==RmlUiIntentKind.HudProperty&&intent.Argument==0&&intent.Document==page.Document,
                            $"{width}x{height}: {label} queues its own native slider intent");
                        Check(page.Handle(intent),$"{width}x{height}: {label} applies native slider intent");received=true;
                    }
                    Check(received,$"{width}x{height}: {label} emitted a native range change");
                    var row=owner.Snapshot().Properties[0];
                    float position=float.Parse(expectedPosition,CultureInfo.InvariantCulture);
                    var range=row.Range!;float value=range.Minimum+(range.Maximum-range.Minimum)*Math.Clamp(position,0,1000)/1000;
                    value=Math.Clamp(MathF.Round(value/range.Step)*range.Step,range.Minimum,range.Maximum);
                    Check(Math.Abs(float.Parse(row.Value,CultureInfo.InvariantCulture)-value)<.001f
                        &&row.Value!=beforeValue,$"{width}x{height}: {label} updates the exact live HUD value");
                    Settle();
                    Check(writes>0&&saved!=null&&Math.Abs(saved.Crosshair.LengthX-value)<.001f,
                        $"{width}x{height}: {label} autosaves the final live value");
                }
                void ClickSlider(float fraction,string label)
                {
                    const string id="hud_property0_slider";
                    Check(host.FocusDocument(page.Document,id),$"{width}x{height}: slider receives native focus");host.Update();
                    Check(host.TryGetElementBounds(page.Document,id,out float x,out float y,out float w,out float h)
                        &&x>=0&&y>=0&&x+w<=width+1&&y+h<=height+1&&w>40&&h>20,
                        $"{width}x{height}: native slider has reachable pointer geometry");
                    float canvasWidth=host.TryGetElementBounds(page.Document,"hud_canvas",out _,out _,out float cw,out _)?cw:640;
                    float canvasHeight=host.TryGetElementBounds(page.Document,"hud_canvas",out _,out _,out _,out float ch)?ch:360;
                    float px=x+w*fraction,py=y+h/2;
                    string before=owner.Snapshot().Properties[0].Value;
                    host.DiscardIntents();host.Input.PointerButton(0,px,py,true);host.Input.PointerButton(0,px,py,false);host.Update();
                    string changed=host.ReadField(page.Document,id);
                    Check(float.TryParse(changed,NumberStyles.Float,CultureInfo.InvariantCulture,out float target)
                        &&Math.Abs(target-SliderPosition())>2,$"{width}x{height}: {label} changes the native range by pointer");
                    PumpBeforeDrain(canvasWidth,canvasHeight);DrainRangeChange(changed,before,label);
                }
                void DragSlider(float startFraction,float endFraction,string label)
                {
                    const string id="hud_property0_slider";
                    Check(host.FocusDocument(page.Document,id),$"{width}x{height}: slider receives drag focus");host.Update();
                    Check(host.TryGetElementBounds(page.Document,id,out float x,out float y,out float w,out float h)
                        &&x>=0&&y>=0&&x+w<=width+1&&y+h<=height+1&&w>40&&h>20,
                        $"{width}x{height}: held drag has reachable pointer geometry");
                    float canvasWidth=host.TryGetElementBounds(page.Document,"hud_canvas",out _,out _,out float cw,out _)?cw:640;
                    float canvasHeight=host.TryGetElementBounds(page.Document,"hud_canvas",out _,out _,out _,out float ch)?ch:360;
                    float py=y+h/2;string before=owner.Snapshot().Properties[0].Value;
                    float thumbFraction=Math.Clamp(SliderPosition()/1000,0,1);
                    float trackStart=x+5,trackWidth=Math.Max(1,w-10);
                    host.DiscardIntents();host.Input.PointerButton(0,trackStart+trackWidth*thumbFraction,py,true);host.Update();PumpBeforeDrain(canvasWidth,canvasHeight);
                    host.Input.PointerMoved(trackStart+trackWidth*endFraction,py);host.Update();PumpBeforeDrain(canvasWidth,canvasHeight);
                    string during=host.ReadField(page.Document,id);
                    Check(float.TryParse(during,NumberStyles.Float,CultureInfo.InvariantCulture,out float heldPosition)
                        &&Math.Abs(heldPosition-SliderPosition())>2,$"{width}x{height}: {label} moves the grabbed thumb while held (start {startFraction:P0})");
                    host.Input.PointerButton(0,trackStart+trackWidth*endFraction,py,false);host.Update();
                    string changed=host.ReadField(page.Document,id);PumpBeforeDrain(canvasWidth,canvasHeight);
                    Check(float.TryParse(changed,NumberStyles.Float,CultureInfo.InvariantCulture,out float target)
                        &&Math.Abs(target-SliderPosition())>2,$"{width}x{height}: {label} changes the native range while held");
                    DrainRangeChange(changed,before,label);
                }

                Activate("hud_section_appearance");
                host.SetField(page.Document,"hud_property_search","/crosshair/lengthX");Activate("hud_search");
                Check(owner.Snapshot().Properties.Length==1&&owner.Snapshot().Properties[0].Path=="/crosshair/lengthX"
                    &&owner.Snapshot().Properties[0].Range!=null,$"{width}x{height}: regression targets one numeric crosshair row");
                float initial=owner.CopyDraft().Crosshair.LengthX;
                ClickSlider(.82f,"native pointer click");
                Activate("hud_undo");Settle();
                Check(Math.Abs(owner.CopyDraft().Crosshair.LengthX-initial)<.001f,$"{width}x{height}: undo restores the prior HUD value");
                CheckSliderMatchesModel("undo");
                DragSlider(.18f,.84f,"held native pointer drag");
                Activate("hud_property0_reset");Settle();
                Check(Math.Abs(owner.CopyDraft().Crosshair.LengthX-initial)<.001f,$"{width}x{height}: row reset restores the selected HUD value");
                CheckSliderMatchesModel("row reset");
                page.Back();
                Check(page.Closed,$"{width}x{height}: pointer regression page closes cleanly");
            }
        }
        finally
        {
            host.Resize(oldWidth,oldHeight,density);host.Update();
        }
    }
}
