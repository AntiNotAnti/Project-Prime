#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Render.Hud;
using MphRead.Mods.Input;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace MphRead.Mods.Launcher.RmlUi.Pages.Hud;

/// <summary>Native HUD editor documents and textures; the controller retains the detached profile.</summary>
public sealed class HudEditorPagePresenter : IDisposable
{
    private static readonly RmlUiPageSpec Page = new("hud-editor","pages/hud/editor.rml","hud_use");
    private readonly RmlUiHost _host;
    private readonly RmlUiPageManager _pages;
    private readonly HudEditorController _controller;
    private readonly HudPreviewBitmap _preview;
    private readonly ConcurrentQueue<Action> _completions=new();
    private CancellationTokenSource? _previewCancellation;
    private RmlUiDocumentToken _document;
    private string? _name,_search;
    private readonly string?[] _rowPaths=new string?[HudEditorController.PropertyPageSize],_rowValues=new string?[HudEditorController.PropertyPageSize];
    private readonly SliderBinding[] _sliderBindings=new SliderBinding[HudEditorController.PropertyPageSize];
    private readonly record struct SliderBinding(string? Path,string? ModelValue,HudEditorNumericRange? Range,string? Position);
    private bool _fieldValidationFailed;
    private int _exactRow=-1, _helpRow=-1, _colorRow=-1;
    private string _colorPath="", _colorValue="#FFFFFF";
    private RmlUiDocumentToken _colorDocument;
    public bool PopupOpen => _colorDocument != default && _host.IsAlive(_colorDocument);
    public RmlUiDocumentToken ForegroundDocument => PopupOpen ? _colorDocument : _document;
    private string _previewKey="";
    private long _jsonRevision=-1,_previewVersion;
    private long _uiRevision, _boundUi=-1, _boundModel=-1, _documentRevision;
    private bool _disposed;
    private readonly bool _asModal;
    internal bool AutoApply { get; set; }
    public HudEditorPagePresenter(RmlUiHost host,RmlUiPageManager pages,HudEditorController controller,HudPreviewBitmap? preview=null,bool asModal=false)
        => (_host,_pages,_controller,_preview,_asModal)=(host,pages,controller,preview??new HudPreviewBitmap(),asModal);
    public RmlUiDocumentToken Document=>_document;
    public bool IsOpen=>!_disposed&&_document!=default&&_host.IsAlive(_document)&&(_asModal?!_pages.Lifetime(_document).IsCancellationRequested:_pages.Page==_document);
    public void Open()
    {
        ObjectDisposedException.ThrowIf(_disposed,this);
        if(IsOpen)return;
        var spec=AutoApply?Page with { InitialFocus="hud_cancel" }:Page;
        _document=_asModal?_pages.OpenModal(spec):_pages.OpenPage(spec);
        _name=null;_search=null;Array.Clear(_rowPaths);Array.Clear(_rowValues);Array.Clear(_sliderBindings);_jsonRevision=-1;_previewKey="";
        _exactRow=_helpRow=_colorRow=-1;_colorDocument=default;_boundUi=_boundModel=-1;
        _host.SetBool(_document,"visible:hud_preview",false);Present();
    }
    public bool Present()
    {
        if(!IsOpen)return false;
        for(int i=0;i<16&&_completions.TryDequeue(out Action? completion);i++)completion();
        HudEditorSnapshot state=_controller.Snapshot();
        if(AutoApply)
        {
            _host.SetText(_document,"hud_intro","All HUD settings in one place. Changes apply and save automatically.");
            _host.SetBool(_document,"visible:hud_use",false);
            _host.SetText(_document,"hud_cancel","BACK");
            _host.SetText(_document,"hud_profiles_help","Start with a preset or save a named copy. Changes apply automatically.");
            if(state.Status.Contains("Use in settings",StringComparison.Ordinal))
                _host.SetText(_document,"hud_status","Named profile saved. Changes apply automatically.");
        }
        if(_search!=state.PropertySearch){_search=state.PropertySearch;_host.SetField(_document,"hud_property_search",state.PropertySearch);}
        if(_name!=state.Name){_name=state.Name;_host.SetField(_document,"hud_name",state.Name);}
        for(int i=0;i<HudEditorController.PropertyPageSize;i++)
        {
            HudEditorProperty? row=i<state.Properties.Length?state.Properties[i]:null;
            string id="hud_property"+i+"_input";
            bool changedRow=_rowPaths[i]!=row?.Path;
            if(changedRow) { if(_exactRow==i){_exactRow=-1;_uiRevision++;} if(_helpRow==i){_helpRow=-1;_uiRevision++;} }
            if(changedRow||_rowValues[i]!=row?.Value)
            {
                string raw=_host.ReadField(_document,id);
                if(changedRow||raw==_rowValues[i])_host.SetField(_document,id,row?.Value??"");
                _rowPaths[i]=row?.Path;_rowValues[i]=row?.Value;
            }
            if(row?.Range is { } range && float.TryParse(row.Value,NumberStyles.Float,CultureInfo.InvariantCulture,out float number))
            {
                string position=Math.Clamp((number-range.Minimum)/(range.Maximum-range.Minimum)*1000,0,1000).ToString("0.###",CultureInfo.InvariantCulture);
                var binding=new SliderBinding(row.Path,row.Value,range,position);
                // Native range changes are queued and consumed after this page is
                // presented. Re-publishing an unchanged model value here would
                // erase the user's pointer edit before its intent is handled.
                if(_sliderBindings[i]!=binding)
                { _host.SetField(_document,"hud_property"+i+"_slider",position);_sliderBindings[i]=binding; }
            }
            else _sliderBindings[i]=default;
        }
        if(_boundModel!=state.Revision||_boundUi!=_uiRevision)
        { _boundModel=state.Revision;_boundUi=_uiRevision;_pages.Present(_document,++_documentRevision,Bindings(state)); }
        if(_jsonRevision!=state.JsonRevision){_jsonRevision=state.JsonRevision;_host.SetField(_document,"hud_json",state.JsonText);}
        PresentPreview(state);return true;
    }
    public bool HandleIntent(in RmlUiIntent intent)
    {
        if(PopupOpen)return HandleColorIntent(intent);
        if(!IsOpen||_pages.Top!=_document||_host.CurrentInputDocument!=_document||!_host.IsVisible(_document)||intent.Document!=_document||intent.Kind is not (RmlUiIntentKind.HudAction or RmlUiIntentKind.HudElement or RmlUiIntentKind.HudProperty))return false;
        if(!_pages.Accept(intent))return false;
        bool rowAction=intent.Kind==RmlUiIntentKind.HudProperty;
        int rowIndex=rowAction?intent.Argument%HudEditorController.PropertyPageSize:-1;
        if(rowAction&&intent.Argument is >=24 and <32&&rowIndex<_controller.Snapshot().Properties.Length)
            _host.SetField(_document,"hud_property"+rowIndex+"_input",_controller.Snapshot().Properties[rowIndex].Value);
        bool leaving=intent.Kind==RmlUiIntentKind.HudElement||intent.Kind==RmlUiIntentKind.HudAction
            &&(intent.Argument is >=(int)HudEditorAction.SectionAppearance and <=(int)HudEditorAction.WorkspaceProfiles
                ||intent.Argument==(int)HudEditorAction.PropertyPrevious||intent.Argument==(int)HudEditorAction.PropertyNext);
        if(!CapturePendingFields(discardInvalid:leaving||rowAction&&intent.Argument>=8&&intent.Argument<24)){Present();return true;}
        try
        {
        if(intent.Kind==RmlUiIntentKind.HudElement)_controller.SelectElement(intent.Argument);
        else if(rowAction)
        {
            if(intent.Argument<8) { if(_controller.Snapshot().Properties.Length>rowIndex && _controller.Snapshot().Properties[rowIndex].Range!=null)ApplySlider(rowIndex); else _controller.SelectProperty(rowIndex); }
            else if(intent.Argument<24)_controller.CyclePropertyRow(rowIndex,intent.Argument<16);
            else if(intent.Argument<32){_sliderBindings[rowIndex]=default;_controller.ResetPropertyRow(rowIndex);}
            else if(intent.Argument<40)OpenColor(rowIndex);
            else if(intent.Argument is >=72 and <80){_helpRow=_helpRow==rowIndex?-1:rowIndex;_uiRevision++;}
            else if(intent.Argument is >=80 and <88){_exactRow=_exactRow==rowIndex?-1:rowIndex;_uiRevision++;}
            else return false;
        }
        else if(Enum.IsDefined((HudEditorAction)intent.Argument))
        {
            var action=(HudEditorAction)intent.Argument;
            if(action is HudEditorAction.Undo or HudEditorAction.Redo or HudEditorAction.ResetHud
                or HudEditorAction.ResetElement or HudEditorAction.ResetSection or HudEditorAction.PropertyReset)
                InvalidateSliderBindings();
            string value=action == HudEditorAction.SearchProperties ? _host.ReadField(_document,"hud_property_search",128) : action is HudEditorAction.ImportJson or HudEditorAction.ImportCrosshair
                ?_host.ReadField(_document,"hud_json",HudProfileStore.MaximumBytes):"";
            _controller.Dispatch(action,value,_host.ReadField(_document,"hud_name"));
        }
        }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException or ArgumentException)
        { _controller.ReportFailure(ex.Message); }
        Present();return true;
    }
    // Keep text detached while it is being typed; commit only a complete, valid edit.
    internal bool CapturePendingFields(bool discardInvalid=false)
    {
        if(!IsOpen)return false;
        var state=_controller.Snapshot();
        try
        {
        if(!state.ProfilesWorkspace)for(int i=0;i<state.Properties.Length;i++)
        {
            var property=state.Properties[i];string id="hud_property"+i+"_input";
            if(property.Range!=null&&_exactRow!=i)continue;
            string value=_host.ReadField(_document,id);
            if(_rowPaths[i]!=property.Path||value==property.Value)continue;
            if(!_controller.ApplyPropertyRow(i,value))
            {
                _fieldValidationFailed=true;
                if(!discardInvalid)return false;
                _host.SetField(_document,id,property.Value);
            }
            else if(!property.Choices.IsEmpty)
            {
                string acceptedValue=_controller.Snapshot().Properties[i].Value;
                if(value!=acceptedValue)_host.SetField(_document,id,acceptedValue);
                _rowValues[i]=acceptedValue;
            }
        }
        if(_fieldValidationFailed){_controller.ReportFailure("");_fieldValidationFailed=false;}
        return true;
        }
        finally
        {
            if(_controller.Snapshot().PropertyIndex!=state.PropertyIndex)_controller.SelectProperty(state.PropertyIndex%HudEditorController.PropertyPageSize);
        }
    }
    internal string PendingFieldsKey()
    {
        var key=new StringBuilder(_host.ReadField(_document,"hud_name"));
        for(int i=0;i<_rowPaths.Length;i++)if(_rowPaths[i]!=null)
            key.Append('\n').Append(_rowPaths[i]).Append('=').Append(_host.ReadField(_document,"hud_property"+i+"_input"));
        return key.ToString();
    }
    private void ApplySlider(int index)
    {
        var row=_controller.Snapshot().Properties[index];
        if(row.Range is not {} range||!float.TryParse(_host.ReadField(_document,"hud_property"+index+"_slider"),NumberStyles.Float,CultureInfo.InvariantCulture,out float position)||!float.IsFinite(position))return;
        float value=range.Minimum+(range.Maximum-range.Minimum)*Math.Clamp(position,0,1000)/1000;
        value=Math.Clamp(MathF.Round(value/range.Step)*range.Step,range.Minimum,range.Maximum);
        if(_controller.ApplyPropertyRow(index,value.ToString("0.######",CultureInfo.InvariantCulture)))
        { string accepted=_controller.Snapshot().Properties[index].Value;_host.SetField(_document,"hud_property"+index+"_input",accepted);_rowValues[index]=accepted; }
    }
    private void InvalidateSliderBindings()=>Array.Clear(_sliderBindings);
    private void OpenColor(int index)
    {
        var state=_controller.Snapshot();
        if(index>=state.Properties.Length||!IsColor(state.Properties[index]))return;
        _controller.ReleaseInput();
        _colorRow=index;_colorPath=state.Properties[index].Path;_colorValue=state.Properties[index].Value;
        _colorDocument=_pages.OpenModal(new("hud-color","pages/hud/color-picker.rml","hud_color_done"));
        PresentColor();
    }
    private static bool IsColor(HudEditorProperty row)=>row.Value.Length==7&&row.Value[0]=='#'&&HudColor.Normalize(row.Value)==row.Value;
    private void PresentColor()
    {
        if(!PopupOpen)return;
        _host.SetText(_colorDocument,"hud_color_title",_controller.Snapshot().Properties[_colorRow].Label);
        _host.SetText(_colorDocument,"color:hud_color_preview",_colorValue);
        _host.SetField(_colorDocument,"hud_color_hex",_colorValue);
        for(int i=0;i<16;i++)_host.SetText(_colorDocument,"color:hud_color_palette"+i,HudColorSelection.Palette[i]);
        for(int i=0;i<8;i++)
        { _host.SetBool(_colorDocument,"visible:hud_color_recent"+i,i<HudColorSelection.Recent.Count);if(i<HudColorSelection.Recent.Count)_host.SetText(_colorDocument,"color:hud_color_recent"+i,HudColorSelection.Recent[i]); }
        _host.SetBool(_colorDocument,"visible:hud_color_recent_empty",HudColorSelection.Recent.Count==0);
        uint rgb=uint.Parse(_colorValue.AsSpan(1),NumberStyles.HexNumber,CultureInfo.InvariantCulture);
        for(int i=0;i<3;i++)_host.SetField(_colorDocument,"hud_color_channel"+i,((rgb>>(16-8*i))&255).ToString(CultureInfo.InvariantCulture));
    }
    private bool SetColor(string value)
    {
        value=value.Trim().ToUpperInvariant();
        var rows=_controller.Snapshot().Properties;
        if(_colorRow<0||_colorRow>=rows.Length||rows[_colorRow].Path!=_colorPath)return false;
        if(value.Length!=7||value[0]!='#'||HudColor.Normalize(value)!=value)
        { _host.SetText(_colorDocument,"hud_color_error","Enter a color such as #72C7FF.");return false; }
        if(!_controller.ApplyPropertyRow(_colorRow,value))return false;
        _colorValue=value;_host.SetField(_document,"hud_property"+_colorRow+"_input",value);_rowValues[_colorRow]=value;
        _host.SetText(_colorDocument,"hud_color_error","");Present();PresentColor();return true;
    }
    private bool HandleColorIntent(in RmlUiIntent intent)
    {
        if(intent.Document!=_colorDocument||_pages.Top!=_colorDocument||_host.CurrentInputDocument!=_colorDocument||!_host.IsVisible(_colorDocument)||intent.Kind!=RmlUiIntentKind.HudProperty||!_pages.Accept(intent))return false;
        int action=intent.Argument;
        if(action==64)
        { if(SetColor(_host.ReadField(_colorDocument,"hud_color_hex",7)))DismissPopup(); }
        else if(action is >=40 and <56)
        { if(SetColor(HudColorSelection.Palette[action-40]))HudColorSelection.Remember(_colorValue);PresentColor(); }
        else if(action is >=56 and <64)
        { int i=action-56;if(i<HudColorSelection.Recent.Count){string color=HudColorSelection.Recent[i];if(SetColor(color))HudColorSelection.Remember(color);PresentColor();} }
        else if(action is >=88 and <=90)
        {
            int rgb=0;
            for(int i=0;i<3;i++)
            { if(!float.TryParse(_host.ReadField(_colorDocument,"hud_color_channel"+i),NumberStyles.Float,CultureInfo.InvariantCulture,out float value)||!float.IsFinite(value))return false;rgb=(rgb<<8)|(int)Math.Clamp(MathF.Round(value),0,255); }
            SetColor("#"+rgb.ToString("X6",CultureInfo.InvariantCulture));
        }
        else if(action==91){if(SetColor(_host.ReadField(_colorDocument,"hud_color_hex",7)))HudColorSelection.Remember(_colorValue);}
        else return false;
        return true;
    }
    public bool DismissPopup()
    {
        if(!PopupOpen||_pages.Top!=_colorDocument)return false;
        HudColorSelection.Remember(_colorValue);
        if(!_pages.CloseModal())return false;
        _colorDocument=default;_host.FocusDocument(_document,"hud_property"+_colorRow+"_color");_colorRow=-1;return true;
    }
    public void SetCanvasSize(float physicalWidth,float physicalHeight){_controller.SetCanvasSize(physicalWidth,physicalHeight);Present();}
    public bool PointerDown(long pointer,float localX,float localY,bool shift=false,bool alt=false,bool touch=false)
    { if(PopupOpen)return false;if(AutoApply&&!CapturePendingFields()){Present();return false;}bool result=_controller.PointerDown(pointer,localX,localY,shift,alt,touch);Present();return result; }
    public bool PointerMove(long pointer,float localX,float localY,bool shift=false,bool control=false)
    { if(PopupOpen)return false;bool result=_controller.PointerMove(pointer,localX,localY,shift,control);Present();return result; }
    public void PointerUp(long pointer){_controller.PointerUp(pointer);Present();}
    public void ReleaseInput(){_controller.ReleaseInput();Present();}
    public bool KeyDown(Keys key,RmlUiInputModifiers modifiers)
    {
        if(PopupOpen)return key==Keys.Escape&&DismissPopup();
        if(!IsOpen)return false;
        string focus=_host.FocusedElement();
        if(focus!="hud_canvas"&&!focus.StartsWith("hud_element",StringComparison.Ordinal))return false;
        bool control=(modifiers&(RmlUiInputModifiers.Control|RmlUiInputModifiers.Command))!=0;
        bool shift=(modifiers&RmlUiInputModifiers.Shift)!=0;
        if(control)
        {
            if(key==Keys.Z){InvalidateSliderBindings();_controller.Dispatch(shift?HudEditorAction.Redo:HudEditorAction.Undo);}
            else if(key==Keys.Y){InvalidateSliderBindings();_controller.Dispatch(HudEditorAction.Redo);}
            else if(key==Keys.C){_controller.Dispatch(HudEditorAction.ExportJson);_host.SetClipboard(_controller.Snapshot().JsonText);}
            else if(key==Keys.V)_controller.Dispatch(HudEditorAction.ImportJson,_host.ReadClipboard());
            else return false;
        }
        else
        {
            float step=shift?10:1;
            switch(key)
            {
                case Keys.Left:_controller.Nudge(-step,0);break;
                case Keys.Right:_controller.Nudge(step,0);break;
                case Keys.Up:_controller.Nudge(0,-step);break;
                case Keys.Down:_controller.Nudge(0,step);break;
                case Keys.Delete:_controller.SetVisible(false);break;
                case Keys.R:InvalidateSliderBindings();_controller.Dispatch(HudEditorAction.ResetElement);break;
                default:return false;
            }
        }
        Present();return true;
    }
    public bool HandleController(UiAction action,bool canvasFocused)
    {if(!IsOpen)return false;if(PopupOpen)return action==UiAction.Back&&DismissPopup();bool handled=_controller.HandleController(action,canvasFocused);Present();return handled;}
    public void HandleControllerAxes(GamepadSnapshot snapshot,long now)
    {if(IsOpen&&!PopupOpen){_controller.HandleControllerAxes(snapshot,now);Present();}}
    public bool Back()
    {
        if(DismissPopup())return true;
        _controller.HandleController(UiAction.Back,_host.FocusedElement()=="hud_canvas");Present();
        if(!_controller.Snapshot().Closed)_host.FocusDocument(_document,"hud_pick"+_controller.Snapshot().Selected);
        return true;
    }
    public bool TryTakeAccepted(out HudProfile profile)=>_controller.TryTakeAccepted(out profile);
    public bool TryTakeCancelled()=>_controller.TryTakeCancelled();
    public void ReportFailure(string error){_controller.ReportFailure(error);Present();}
    public bool Close()
    {
        if(PopupOpen&&!DismissPopup())return false;
        _controller.ReleaseInput();_previewCancellation?.Cancel();_previewVersion++;
        if(IsOpen&&(_asModal?(_pages.Top!=_document||!_pages.CloseModal()):!_pages.ClosePage()))return false;_document=default;return true;
    }
    public void Dispose(){if(_disposed)return;if(!Close())throw new InvalidOperationException("HUD editor could not close.");_previewCancellation?.Dispose();_disposed=true;}
    private void PresentPreview(HudEditorSnapshot state)
    {
        string key=state.ProfileJson+$"|{state.CanvasWidth}|{state.CanvasHeight}|{state.PreviewWidth}|{state.PreviewHeight}|{state.PreviewHunter}|{state.Scenario}|{state.CrosshairTarget}|{state.GridSize}|{state.Selected}|{state.InspectorSection}|{state.Property?.Path}";
        if(key==_previewKey)return;_previewKey=key;
        _previewCancellation?.Cancel();_previewCancellation?.Dispose();
        _previewCancellation=CancellationTokenSource.CreateLinkedTokenSource(_pages.Lifetime(_document));
        var cancellation=_previewCancellation.Token;long version=++_previewVersion;_ = BuildPreview(state,version,cancellation);
    }
    private async Task BuildPreview(HudEditorSnapshot state,long version,CancellationToken cancellation)
    {
        try
        {
            HudPreviewResult result=await Task.Run(()=>_preview.Build(state,cancellation),cancellation).ConfigureAwait(false);
            if(!cancellation.IsCancellationRequested)_completions.Enqueue(()=>
            {
                if(!IsOpen||version!=_previewVersion)return;
                _host.SetText(_document,"image:hud_preview",result.Path);_host.SetBool(_document,"visible:hud_preview",true);
                _host.SetText(_document,"hud_preview_warning",result.Warning);_host.SetBool(_document,"visible:hud_preview_warning",result.Warning.Length>0);
            });
        }
        catch(OperationCanceledException){}
        catch(Exception ex)
        {string error=ex.Message;if(!cancellation.IsCancellationRequested)_completions.Enqueue(()=>{if(IsOpen&&version==_previewVersion){_host.SetText(_document,"hud_preview_warning",error);_host.SetBool(_document,"visible:hud_preview_warning",true);}});}
    }
    private IEnumerable<KeyValuePair<string,RmlUiBindingValue>> Bindings(HudEditorSnapshot state)
    {
        HudProfile profile=HudProfileStore.Parse(state.ProfileJson);
        HudPreviewState preview=HudPreviewState.For(state.Scenario);
        yield return Text("hud_selected",ElementLabel(state.Selected));
        yield return Bool("visible:hud_elements_workspace",!state.ProfilesWorkspace);
        yield return Bool("visible:hud_profiles_workspace",state.ProfilesWorkspace);
        yield return Bool("class:hud_workspace_elements:selected",!state.ProfilesWorkspace);
        yield return Bool("class:hud_workspace_profiles:selected",state.ProfilesWorkspace);
        string[] sections={"appearance","layout","global","typography","decorations"};
        for(int section=0;section<sections.Length;section++) yield return Bool("class:hud_section_"+sections[section]+":selected",state.InspectorSection==section);
        yield return Text("hud_section_title",state.PropertySearch.Length>0?"SEARCH: "+state.PropertySearch:new[]{"ELEMENT APPEARANCE","POSITION & VISIBILITY","HUD-WIDE SETTINGS","LETTERING & FONTS","DECORATIONS"}[state.InspectorSection]);
        yield return Bool("visible:hud_appearance_controls",state.InspectorSection==0&&state.PropertySearch.Length==0);
        yield return Bool("visible:hud_global_controls",state.InspectorSection==2&&state.PropertySearch.Length==0);
        yield return Bool("visible:hud_property_empty",state.Properties.IsEmpty);
        yield return Text("hud_preset",state.BasePreset.ToUpperInvariant());
        yield return Text("hud_aspect",$"{state.PreviewWidth:0} × {state.PreviewHeight:0}");
        yield return Text("hud_hunter",new[]{"SAMUS","KANDEN","TRACE","SYLUX","NOXUS","SPIRE","WEAVEL","GUARDIAN"}[state.PreviewHunter]);
        yield return Text("hud_scenario",state.Scenario.ToString().ToUpperInvariant());
        yield return Text("hud_grid",state.GridSize==0?"GRID OFF":"GRID "+state.GridSize);
        yield return Text("hud_guides",state.SnapGuides?"SNAP GUIDES ON":"SNAP GUIDES OFF");
        yield return Text("hud_status",state.Status);yield return Text("hud_error",state.Error);yield return Bool("visible:hud_error",state.Error.Length>0);
        yield return Text("hud_property_page",$"{state.PropertyPage+1} / {state.PropertyPageCount}");
        yield return Text("hud_crosshair_target",new[]{"DEFAULT","POWER BEAM","VOLT DRIVER","MISSILE","BATTLEHAMMER","IMPERIALIST","JUDICATOR","MAGMAUL","SHOCK COIL","OMEGA CANNON","ZOOM"}[state.CrosshairTarget]);
        yield return Text("hud_crosshair_override",state.CrosshairOverride?"OVERRIDE ON":"INHERIT DEFAULT");
        yield return Bool("visible:hud_crosshair_controls",state.Selected==0);
        yield return Bool("visible:hud_radar_controls",state.Selected==4);
        for(int i=0;i<HudProfileDefaults.ElementIds.Length;i++)
        {
            var element=state.Elements[i];yield return Text("hud_pick"+i,ElementLabel(i));
            yield return Bool("visible:hud_element"+i, i==state.Selected || HudPreviewState.ElementVisible(element.Id,state.Scenario));
            yield return Bool("class:hud_pick"+i+":selected",element.Selected);yield return Bool("class:hud_element"+i+":selected",element.Selected);
            var rect=element.Bounds;yield return Text("rect:hud_element"+i,Rect(rect));yield return Text("opacity:hud_element"+i,element.Opacity.ToString(CultureInfo.InvariantCulture));
            string label=i switch { 1=>profile.Health.Number||profile.Health.Native||profile.Mode==HudMode.Classic ? preview.Health.ToString(CultureInfo.InvariantCulture):"",
                2=>profile.Ammo.Number||profile.Ammo.Native||profile.Mode==HudMode.Classic ? preview.Ammo+" / 80":"",
                3=>profile.Inventory.Native||profile.Mode==HudMode.Classic ? "":"POWER BEAM / VOLT DRIVER / MISSILE / IMPERIALIST",
                8=>state.Scenario==HudPreviewScenario.Objective?"OCTOLITH CAPTURED":"DOUBLE KILL", _=>"" };
            if(i is 1 or 2 or 3 or 8)yield return Text("hud_element"+i+"_label",label);
            if(i is 1 or 2 or 3 or >=5 and <=11 or >=14)yield return Bool("visible:hud_element"+i+"_label",false);
            float font=30*new HudTransform(state.Surface.Width,state.Surface.Height).UnitScale*profile.Elements[element.Id].Scale*profile.GlobalScale*profile.TextScale;
            if(i is 1 or 2)
            {
                var meter=i==1?profile.Health:profile.Ammo;float unit=5.625f*new HudTransform(state.Surface.Width,state.Surface.Height).UnitScale*profile.GlobalScale*profile.Elements[element.Id].Scale;
                font=(meter.Native||profile.Mode==HudMode.Classic?8:12*meter.NumberScale)*unit*profile.TextScale;
                var color=new HudMeterRuntime(meter).ColorFor(i==1?preview.Health/99f:preview.Ammo/80f);
                yield return Text("ink:hud_element"+i+"_label",$"#{(byte)(color.X*255):X2}{(byte)(color.Y*255):X2}{(byte)(color.Z*255):X2}");
            }
            yield return Text("font-size:hud_element"+i+"_label",Math.Clamp(font,1,1000).ToString(CultureInfo.InvariantCulture));
        }
        for(int i=0;i<HudEditorController.PropertyPageSize;i++)
        {
            HudEditorProperty? row=i<state.Properties.Length?state.Properties[i]:null;
            yield return Bool("visible:hud_property"+i,row!=null);yield return Text("hud_property"+i+"_label",row?.Label??"");
            yield return Bool("visible:hud_property"+i+"_previous",row!=null&&!row.Choices.IsEmpty);
            yield return Bool("visible:hud_property"+i+"_next",row!=null&&!row.Choices.IsEmpty);
            yield return Bool("disabled:hud_property"+i+"_input",row==null||state.Closed);
            bool color=row!=null&&IsColor(row), numeric=row?.Range!=null, choice=row!=null&&!row.Choices.IsEmpty;
            yield return Bool("visible:hud_property"+i+"_choice",choice);
            yield return Text("hud_property"+i+"_choice",row?.Value??"");
            yield return Bool("visible:hud_property"+i+"_slider",numeric&&_exactRow!=i);
            yield return Bool("visible:hud_property"+i+"_number",numeric&&_exactRow!=i);
            yield return Text("hud_property"+i+"_number",row?.Value??"");
            yield return Bool("visible:hud_property"+i+"_exact",numeric);
            yield return Bool("visible:hud_property"+i+"_input",row!=null&&!choice&&!color&&(!numeric||_exactRow==i));
            yield return Bool("visible:hud_property"+i+"_color",color);
            if(color){yield return Text("color:hud_property"+i+"_color",row!.Value);uint rgb=uint.Parse(row.Value.AsSpan(1),NumberStyles.HexNumber,CultureInfo.InvariantCulture);float l=((rgb>>16)*.2126f+((rgb>>8)&255)*.7152f+(rgb&255)*.0722f)/255;yield return Text("ink:hud_property"+i+"_color",l>.5f?"#07111B":"#FFFFFF");yield return Text("hud_property"+i+"_color",row.Value+"  PALETTE");}
            string hint=row?.Help??"";
            if(hint.Length==0&&numeric)hint=$"Adjust with the slider ({row!.Range!.Minimum:g} to {row.Range.Maximum:g}). Use # to enter an exact value.";
            if(hint.Length==0&&color)hint="Choose a palette or recent color, mix RGB channels, or enter an exact hex color.";
            yield return Bool("visible:hud_property"+i+"_info",hint.Length>0);
            yield return Text("hud_property"+i+"_help",hint);yield return Bool("visible:hud_property"+i+"_help",hint.Length!=0&&_helpRow==i);
        }
        yield return Bool("disabled:hud_undo",!state.CanUndo||state.Closed);yield return Bool("disabled:hud_redo",!state.CanRedo||state.Closed);
        yield return Bool("disabled:hud_crosshair_override",state.CrosshairTarget==0||state.Closed);
        yield return Bool("disabled:hud_property_previous",state.PropertyPage==0||state.Closed);
        yield return Bool("disabled:hud_property_next",state.PropertyPage+1>=state.PropertyPageCount||state.Closed);
        var surface=state.Surface;yield return Text("rect:hud_surface",Rect(surface));
        float insetX=surface.Width*profile.SafeArea, insetY=surface.Height*profile.SafeArea;
        yield return Text("rect:hud_safe_area",Rect(new(surface.X+insetX,surface.Y+insetY,surface.Width-2*insetX,surface.Height-2*insetY)));
        yield return Text("hud_safe_area_label",$"SAFE AREA {profile.SafeArea*100:0.#}% // HUD-wide settings");
        yield return Bool("visible:hud_guide_x",state.GuideX!=null);yield return Bool("visible:hud_guide_y",state.GuideY!=null);
        if(state.GuideX is{} gx)yield return Text("rect:hud_guide_x",Rect(new(gx,surface.Y,1,surface.Height)));
        if(state.GuideY is{} gy)yield return Text("rect:hud_guide_y",Rect(new(surface.X,gy,surface.Width,1)));
    }
    private static string ElementLabel(int index) => new[]{"Crosshair & hit marker","Health","Ammunition","Weapon inventory","Radar","Score","Match timer","Kill feed","Combat notifications","Opponent information","Objectives","FPS & diagnostics","Health gauge","Ammo gauge","Movement telemetry", "Rounds", "Respawn countdown", "Ability cooldown", "Flag action", "CTF events", "CTF flag status", "CTF scoreboard", "Oddball status", "Oddball action", "Oddball events", "Oddball scoreboard"}[index];
    private static string Rect(HudEditorRect rect)=>String.Join(",",rect.X.ToString(CultureInfo.InvariantCulture),rect.Y.ToString(CultureInfo.InvariantCulture),rect.Width.ToString(CultureInfo.InvariantCulture),rect.Height.ToString(CultureInfo.InvariantCulture));
    private static KeyValuePair<string,RmlUiBindingValue> Text(string id,string value)=>new(id,RmlUiBindingValue.FromText(value));
    private static KeyValuePair<string,RmlUiBindingValue> Bool(string id,bool value)=>new(id,RmlUiBindingValue.FromBoolean(value));
}
#endif
