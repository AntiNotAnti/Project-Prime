#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Render.Hud;
using OpenTK.Windowing.GraphicsLibraryFramework;
namespace MphRead.Mods.Launcher.RmlUi.Pages.Settings;

internal sealed class NativeFullSettingsPage : IDisposable
{
    internal const int Slots=96;
    internal static void RequireRowCapacity(int count)
    {
        if(count>Slots)throw new InvalidOperationException($"Native Settings form has {count} rows; authored capacity is {Slots}. Expand the authored slots before adding this form.");
    }
    private readonly RmlUiHost _host;
    private readonly RmlUiPageManager _pages;
    private readonly NativeFullSettingsController _controller;
    private readonly bool _allowGameFiles, _asModal;
    internal bool AsModal=>_asModal;
    private RmlUiDocumentToken _document;
    private long _revision;
    private readonly int[] _subpages=new int[9];
    private string? _pendingFocus;
    private bool _advanced;
    private bool _disposed,_fieldsDirty=true,_wasCapturing;
    private NativeSettingsVisualEditor? _visual;
    internal bool VisualEditorOpen=>_visual?.OwnsForeground==true;
    internal bool VisualPointerDown(float x,float y)=>_visual?.PointerDown(x,y)==true;
    internal bool VisualPointerMove(float x,float y)=>_visual?.PointerMove(x,y)==true;
    internal void VisualPointerUp()=>_visual?.ReleasePointer();
    private void OpenVisual(int kind){_controller.SuspendInput("Input capture cancelled for visual settings.");_visual=new(_host,_pages,_controller,kind);}
    private NativeSettingRow[] _visible=Array.Empty<NativeSettingRow>();
    public RmlUiDocumentToken Document=>_document;
    public bool Closed=>_disposed||_controller.Closed;
    internal bool ControllerBindingCaptureActive=>OwnsForeground&&_controller.ControllerCapturing;
    internal bool ConfirmingVideo => _controller.ConfirmingVideo;
    public bool CanNavigateAway=>!_controller.Dirty&&!_controller.ConfirmingVideo&&!_controller.Busy&&!_controller.Capturing&&_controller.Confirmation.Length==0;
    internal bool HasLivePage=>!_disposed&&_document!=default&&_host.IsAlive(_document)&&(_asModal?!_pages.Lifetime(_document).IsCancellationRequested:_pages.Page==_document);
    internal bool OwnsForeground=>HasLivePage&&_pages.Top==_document&&_host.CurrentInputDocument==_document&&_host.IsVisible(_document);
    public NativeFullSettingsPage(RmlUiHost host,RmlUiPageManager pages,MenuSettings settings,Action<WindowStartMode>? windowMode=null,bool allowGameFiles=true,bool asModal=false)
    { _host=host;_pages=pages;_controller=new(settings,windowMode);_allowGameFiles=allowGameFiles;_asModal=asModal; }
    public void Open()
    {
        _host.VerifyOwnerThread();ObjectDisposedException.ThrowIf(_disposed,this);
        if(HasLivePage)return;_document=default;_fieldsDirty=true;
        var spec=new RmlUiPageSpec("native-full-settings","pages/settings/native-full.rml","native_settings_category_0");
        _document=_asModal?_pages.OpenModal(spec):_pages.OpenPage(spec);Refresh();
    }
    public void SuspendView()
    {
        _host.VerifyOwnerThread();
        if(_asModal&&HasLivePage&&!OwnsForeground)throw new InvalidOperationException("Covered Settings modal cannot suspend.");
        if(OwnsForeground){Capture();if(!(_asModal?_pages.CloseModal():_pages.ClosePage()))throw new InvalidOperationException("Settings view could not suspend.");}
        _document=default;_fieldsDirty=true;
    }
    public void ResumeView()=>Open();
    public bool PrepareNavigation()
    {
        _host.VerifyOwnerThread();if(_visual?.Closed==false)return false;if(!HasLivePage)return true;
        if(OwnsForeground&&!Capture())return false;
        if(CanNavigateAway)return true;
        _controller.Back();Refresh();return false;
    }
    public bool TryTakeOpenHud(out HudProfile profile)
    {
        _host.VerifyOwnerThread();profile=null!;
        if(!_controller.HudRequested||!OwnsForeground)return false;
        profile=_controller.CopyHud();_controller.SetHud(null);return true;
    }
    public void AcceptHud(HudProfile profile){_host.VerifyOwnerThread();_controller.SetHud(profile);_fieldsDirty=true;Refresh();}
    internal bool TryTakeCharacterSourcePreview(out string id)
    { _host.VerifyOwnerThread(); id = ""; return OwnsForeground && _allowGameFiles && _controller.TakeCharacterSourcePreview(out id); }
    internal bool TryTakeArenaImport()=>OwnsForeground&&_controller.TakeArenaImport();
    internal void BeginArenaImport(string? path){_host.VerifyOwnerThread();_controller.BeginArenaImport(path);Refresh();}
    public bool TryTakeGameFiles(){_host.VerifyOwnerThread();if(!_controller.GameFilesRequested||!OwnsForeground)return false;_controller.AcknowledgeGameFiles();return true;}
    public bool Handle(in RmlUiIntent intent)
    {
        _host.VerifyOwnerThread();
        if(_visual?.Closed==false){bool handled=_visual.Handle(intent);Refresh();return handled;}
        if(!OwnsForeground||intent.Document!=_document)return false;
        bool known=intent.Kind is RmlUiIntentKind.SettingsApply or RmlUiIntentKind.SettingsDiscard or RmlUiIntentKind.SettingsClose
            or RmlUiIntentKind.SettingsResetCategory or RmlUiIntentKind.SettingsKeepVideo or RmlUiIntentKind.SettingsRevertVideo
            ||intent.Kind==RmlUiIntentKind.SettingsCategory&&intent.Argument<NativeFullSettingsController.Categories.Length
            ||intent.Kind==RmlUiIntentKind.SettingsAction&&(intent.Argument is >=1 and <=14 or >=16 and <=24 or >=26 and <=31 or >=32 and <212 or >=212 and <=219 or >=220 and <=222 or >=232 and <236 or >=240 and <=245);
        if(!known||!_pages.Accept(intent))return false;
        if(_controller.ConfirmingVideo&&intent.Kind is not (RmlUiIntentKind.SettingsKeepVideo or RmlUiIntentKind.SettingsRevertVideo or RmlUiIntentKind.SettingsClose))return true;
        if(_controller.Busy)
        {
            if(intent.Kind==RmlUiIntentKind.SettingsAction&&intent.Argument==219&&_controller.ImportingArenas)
            { _controller.CancelArenaImport(); Refresh(); }
            return true;
        }
        if((_controller.Capturing||_controller.Confirmation.Length>0)&&!(intent.Kind==RmlUiIntentKind.SettingsAction&&intent.Argument is 23 or 24 or 213 or 214 or 215)&&intent.Kind!=RmlUiIntentKind.SettingsClose)return true;
        if(intent.Kind is not (RmlUiIntentKind.SettingsDiscard or RmlUiIntentKind.SettingsResetCategory or RmlUiIntentKind.SettingsKeepVideo or RmlUiIntentKind.SettingsRevertVideo)&&!Capture()){Refresh();return true;}
        switch(intent.Kind)
        {
            case RmlUiIntentKind.SettingsCategory:_controller.Select((int)intent.Argument);_fieldsDirty=true;_pendingFocus="native_settings_form_start";break;
            case RmlUiIntentKind.SettingsApply:_controller.Apply();break;
            case RmlUiIntentKind.SettingsDiscard:_controller.Discard();_fieldsDirty=true;break;
            case RmlUiIntentKind.SettingsResetCategory:_controller.ResetCategory();_fieldsDirty=true;break;
            case RmlUiIntentKind.SettingsClose:_controller.Back();break;
            case RmlUiIntentKind.SettingsKeepVideo:_controller.KeepVideo();break;
            case RmlUiIntentKind.SettingsRevertVideo:_controller.RevertVideo();break;
            case RmlUiIntentKind.SettingsAction:
                if(intent.Argument is >=220 and <=222)OpenVisual((int)intent.Argument-220);
                else if(intent.Argument==3)_controller.RequestHud();
                else if(intent.Argument==4&&_allowGameFiles)_controller.RequestGameFiles();
                else if(intent.Argument is >=232 and <236){_subpages[_controller.Category]=(int)intent.Argument-232;_fieldsDirty=true;_pendingFocus="native_settings_subpage_"+_subpages[_controller.Category];}
                else if(intent.Argument==216){_advanced=!_advanced;_fieldsDirty=true;}
                else if(intent.Argument==217){_controller.ResetReplayControls();_fieldsDirty=true;}
                else if(intent.Argument==240&&_allowGameFiles)_controller.CycleCharacterPreview();
                else if(intent.Argument==241&&_allowGameFiles)_controller.RequestCharacterImport(false);
                else if(intent.Argument==242&&_allowGameFiles)_controller.RequestCharacterImport(true);
                else if(intent.Argument==243&&_allowGameFiles)_controller.RequestRemoveCharacterPreview();
                else if(intent.Argument==244&&_allowGameFiles)_controller.CheckCharacterPreview();
                else if(intent.Argument==245&&_allowGameFiles)_controller.RequestCharacterSourcePreview();
                else if(intent.Argument==218&&_allowGameFiles)_controller.RequestArenaImport();
                else if(intent.Argument==29)
                {
                    string query=_host.ReadField(_document,"native_settings_search",256).Trim();
                    if(query.Contains("touch layout",StringComparison.OrdinalIgnoreCase)||query.Contains("place touch",StringComparison.OrdinalIgnoreCase)){OpenVisual(0);break;}
                    if(query.Contains("stylus zone",StringComparison.OrdinalIgnoreCase)||query.Contains("place stylus",StringComparison.OrdinalIgnoreCase)){OpenVisual(1);break;}
                    if(query.Contains("outline color",StringComparison.OrdinalIgnoreCase)){OpenVisual(2);break;}
                    if(query.Equals("edit hud",StringComparison.OrdinalIgnoreCase)||query.Contains("hud editor",StringComparison.OrdinalIgnoreCase))
                    {_controller.Select(0);_fieldsDirty=true;_pendingFocus="native_settings_hud";break;}
                    var match=_controller.Rows.FirstOrDefault(r=>r.Label.Contains(query,StringComparison.OrdinalIgnoreCase)||r.Group.Contains(query,StringComparison.OrdinalIgnoreCase)||r.Category.Contains(query,StringComparison.OrdinalIgnoreCase)||r.Hint.Contains(query,StringComparison.OrdinalIgnoreCase));
                    if(query.Length>0&&match!=null){_controller.Select(Array.IndexOf(NativeFullSettingsController.Categories,match.Category));if(match.Advanced)_advanced=true;_subpages[_controller.Category]=RowSubpage(match);_fieldsDirty=true;
                        int index=Array.IndexOf(CategoryRows(),match);_pendingFocus=ControlId(match,index);}
                }
                else if(intent.Argument==30){_host.SetField(_document,"native_settings_search","");}
                else if(intent.Argument is >=5 and <=31 or >=212 and <=215)
                {
                    _controller.ArchivePath=_host.ReadField(_document,"native_settings_archive",4096);
                    _controller.ProfileName=_host.ReadField(_document,"native_settings_profile_name",256);
                    _controller.Operation(intent.Argument>=212?(int)intent.Argument-78:(int)intent.Argument);_fieldsDirty=true;
                }
                else
                {
                    int i=(int)intent.Argument-32;
                    if(i>=0&&i<_visible.Length&&_visible[i].IsBinding)_controller.BeginCapture(_visible[i]);
                    else if(i>=0&&i<_visible.Length&&_visible[i].HasSlider)
                    {
                        var r=_visible[i];if(!double.TryParse(_host.ReadField(_document,"native_settings_slider_"+i),NumberStyles.Float,CultureInfo.InvariantCulture,out double position)||!double.IsFinite(position))break;position=Math.Clamp(position,0,1000);
                        double value=r.Minimum!.Value+(r.Maximum!.Value-r.Minimum.Value)*position/1000;
                        if(r.WholeNumber)value=Math.Round(value);
                        _controller.Stage(r,value.ToString("0.###",CultureInfo.InvariantCulture));_fieldsDirty=true;
                    }
                    else if(i>=0&&i<_visible.Length&&_visible[i].Choices.Length>0)
                    {
                        var r=_visible[i];int next=(Array.FindIndex(r.Choices,c=>c.Equals(r.Value,StringComparison.OrdinalIgnoreCase))+1)%r.Choices.Length;
                        _controller.Stage(r,r.Choices[next]);_fieldsDirty=true;
                    }
                }
                break;
        }
        Refresh();return true;
    }
    internal static string[] Subpages(int category)=>category switch{2=>new[]{"General","Notifications"},3=>new[]{"Keyboard","Gamepad","Touch","Stylus"},_=>Array.Empty<string>()};
    internal static int RowSubpage(NativeSettingRow row)=>row.Category switch
    {
        "Audio"=>row.Group is "Mixer" or "General"?0:1,
        "Controls"=>row.Group switch{"Controller" or "Controller bindings" or "Controller calibration" or "Weapon wheel"=>1,"Touch buttons" or "Touch layout" or "Touch overlay"=>2,"Stylus" or "Stylus guide"=>3,_=>0},_=>0
    };
    private static string ControlId(NativeSettingRow row,int index)=>(row.Choices.Length>0||row.IsBinding?"native_settings_cycle_":"native_settings_value_")+index;
    internal void AcknowledgeUsableFrame(){if(OwnsForeground&&_host.TryGetElementBounds(_document,"native_settings_keep_video",out _,out _,out float w,out float h)&&w>0&&h>0)_controller.AcknowledgeVideoFrame();}
    internal void CancelInputCapture(bool revertDisplayTrial=true){_host.VerifyOwnerThread();if(revertDisplayTrial)_controller.RevertVideo();if(_controller.SuspendInput("Input focus changed. Capture cancelled; existing draft kept."))_fieldsDirty=true;Refresh();}
    public bool KeyDown(Keys key,RmlUiInputModifiers modifiers)
    { if(!OwnsForeground)return false;bool result=_controller.KeyDown(key);if(result){_fieldsDirty=true;Refresh();}return result; }
    public bool MouseDown(MouseButton button)
    {if(!OwnsForeground)return false;bool result=_controller.MouseDown(button);if(result){_fieldsDirty=true;Refresh();}return result;}
    public bool ScrollBinding(bool up)
    {if(!OwnsForeground)return false;bool result=_controller.ScrollBinding(up);if(result){_fieldsDirty=true;Refresh();}return result;}
    private NativeSettingRow[] CategoryRows()
    {
        var subpages=Subpages(_controller.Category);
        if(subpages.Length>0)_subpages[_controller.Category]=Math.Clamp(_subpages[_controller.Category],0,subpages.Length-1);
        return _controller.Rows.Where(r=>r.Category==NativeFullSettingsController.Categories[_controller.Category]
            &&(subpages.Length==0||RowSubpage(r)==_subpages[_controller.Category])&&(_advanced||!r.Advanced)).ToArray();
    }
    private bool Capture()
    {
        if(_fieldsDirty)return true;
        _controller.ArchivePath=_host.ReadField(_document,"native_settings_archive",4096);
        _controller.ProfileName=_host.ReadField(_document,"native_settings_profile_name",256);
        bool valid=true;
        for(int i=0;i<_visible.Length;i++)
        {if(_visible[i].Choices.Length>0||_visible[i].IsBinding)continue;string value=_host.ReadField(_document,"native_settings_value_"+i,2048);if(value!=_visible[i].Value&&!_controller.Stage(_visible[i],value))return false;}
        return valid;
    }
    public bool Back(){_host.VerifyOwnerThread();if(_visual?.Closed==false){bool handled=_visual.Back();Refresh();return handled;}if(!OwnsForeground)return false;if(!Capture()){Refresh();return true;}_controller.Back();Refresh();return true;}
    public void Refresh()
    {
        _host.VerifyOwnerThread();if(!HasLivePage)return;
        if(_visual!=null){_visual.Present();if(_visual.Closed){_visual.Dispose();_visual=null;_fieldsDirty=true;}}
        bool foreground=OwnsForeground;
        if(!foreground&&_controller.SuspendInput("Settings was covered. Capture cancelled; existing draft kept."))_fieldsDirty=true;
        _controller.Tick(inputActive:foreground);if(_wasCapturing!=_controller.Capturing){_wasCapturing=_controller.Capturing;_fieldsDirty=true;}
        if(foreground&&!_fieldsDirty&&!_controller.Capturing&&_controller.Confirmation.Length==0&&!_controller.ConfirmingVideo)Capture();
        if(_pendingFocus!=null&&!_fieldsDirty&&foreground&&_host.FocusDocument(_document,_pendingFocus))_pendingFocus=null;
        var rows=CategoryRows();
        RequireRowCapacity(rows.Length);
        var fields=new Dictionary<string,RmlUiBindingValue>();
        void Text(string k,string v)=>fields[k]=RmlUiBindingValue.FromText(v);
        void Bool(string k,bool v)=>fields[k]=RmlUiBindingValue.FromBoolean(v);
        Text("native_settings_title",NativeFullSettingsController.Categories[_controller.Category]);
        for(int i=0;i<NativeFullSettingsController.Categories.Length;i++)Bool("class:native_settings_category_"+i+":selected",i==_controller.Category);
        var subpages=Subpages(_controller.Category);Bool("visible:native_settings_subpages",subpages.Length>0);
        for(int i=0;i<4;i++){Bool("visible:native_settings_subpage_"+i,i<subpages.Length);if(i<subpages.Length){Text("native_settings_subpage_"+i,subpages[i]);Bool("class:native_settings_subpage_"+i+":selected",i==_subpages[_controller.Category]);}}
        Text("native_settings_status",_controller.Status);
        Text("native_settings_dirty",_controller.Dirty?"UNSAVED CHANGES":"SAVED SETTINGS");
        string focus=_host.FocusedElement();var focused=_visible.Select((r,i)=>(Row:r,Index:i)).FirstOrDefault(v=>focus==ControlId(v.Row,v.Index)||focus=="native_settings_slider_"+v.Index);
        Text("native_settings_help",focused.Row==null?"Changes remain in this draft until Apply Changes.":focused.Row.Label+" // "+focused.Row.Hint);
        Bool("visible:native_settings_advanced",_controller.Category==3);Text("native_settings_advanced",_advanced?"HIDE ADVANCED":"SHOW ADVANCED");
        Bool("visible:native_settings_touch_visual",_controller.Category==3&&_subpages[3]==2);
        Bool("visible:native_settings_stylus_visual",_controller.Category==3&&_subpages[3]==3);
        Bool("visible:native_settings_outline_visual",_controller.Category==0);
        Bool("visible:native_settings_hud",_controller.Category==0);
        Bool("visible:native_settings_reset_replay",_controller.Category==4);
        Bool("visible:native_settings_system",_controller.Category==6);
        Bool("visible:native_settings_maintenance",_controller.Category==7);
        Bool("visible:native_settings_profiles",_controller.Category==3&&_subpages[3]==1);
        Bool("visible:native_settings_controller_identity",_controller.Category==4||_controller.Category==3&&_subpages[3]==1);
        Bool("visible:native_settings_confirmation",_controller.Confirmation.Length>0);
        Bool("visible:native_settings_conflict_replace",_controller.BindingConflict);
        Bool("visible:native_settings_conflict_keep",_controller.BindingConflict);
        Text("native_settings_confirm",_controller.BindingConflict?"SWAP":"CONFIRM");
        Bool("visible:native_settings_capture_modifier",_controller.ControllerCapturing);
        Text("native_settings_capture_modifier",_controller.CaptureModifierLabel);
        Text("native_settings_device","CONTROLLER: "+_controller.CurrentDeviceLabel);
        Text("native_settings_confirmation_text",_controller.Confirmation);
        Bool("visible:native_settings_capture",_controller.Capturing);Text("native_settings_capture_text",_controller.CapturePrompt);
        Text("native_settings_summary",_controller.InstallationSummary);
        Bool("visible:native_settings_files",_controller.Category==5);
        Bool("visible:native_settings_import_arenas",_controller.Category==5&&_allowGameFiles);
        Bool("visible:native_settings_character_previews",_controller.Category==5&&_allowGameFiles);
        Text("native_settings_character_selected","PREVIEW: "+_controller.CharacterPreviewLabel);
        Bool("visible:native_settings_cancel_import",_controller.ImportingArenas);
        Bool("disabled:native_settings_files",!_allowGameFiles);
        Bool("visible:native_settings_video",_controller.ConfirmingVideo);
        Bool("disabled:native_settings_keep_video",!_controller.CanKeepVideo);
        Text("native_settings_video_countdown","Keep this display mode? Reverting in "+_controller.VideoSeconds+" seconds.");
        Bool("visible:native_settings_empty",rows.Length==0);
        Text("native_settings_empty",_controller.Category switch
        {
            7=>"Cleanup removes reproducible data only; accounts, saves, game files, actual replays and user media are kept.",
            8=>Credits.Summary+"\n\n"+Credits.Foundation.Who+" — "+Credits.Foundation.What+"\n"+string.Join("\n\n",Credits.Entries.Select(e=>e.Who+" — "+e.What+"\n"+e.Where)),
            _=>""
        });
        if(_fieldsDirty)
        {
            _visible=rows;
            _host.SetField(_document,"native_settings_archive",_controller.ArchivePath);
            _host.SetField(_document,"native_settings_profile_name",_controller.ProfileName);
            for(int i=0;i<Slots;i++)
            {
                bool visible=i<_visible.Length;Bool("visible:native_settings_row_"+i,visible);
                if(!visible)continue;
                var row=_visible[i];Text("native_settings_label_"+i,row.Label);Text("native_settings_group_"+i,row.Group);
                Text("native_settings_hint_"+i,row.Hint);Bool("visible:native_settings_cycle_"+i,row.Choices.Length>0||row.IsBinding);
                Bool("visible:native_settings_value_"+i,row.Choices.Length==0&&!row.IsBinding);
                Text("native_settings_cycle_"+i,row.DisplayValue.ToUpperInvariant()+(row.IsBinding?"  // REBIND":"  ›"));
                Bool("visible:native_settings_group_"+i,i==0||_visible[i-1].Group!=row.Group);
                Bool("visible:native_settings_slider_"+i,row.HasSlider);
                Bool("visible:native_settings_readout_"+i,row.HasSlider);
                Bool("class:native_settings_value_"+i+":native_settings_precision",row.HasSlider);
                if(row.HasSlider&&double.TryParse(row.Value,NumberStyles.Float,CultureInfo.InvariantCulture,out double number))
                {
                    double position=Math.Clamp((number-row.Minimum!.Value)/(row.Maximum!.Value-row.Minimum.Value)*1000,0,1000);
                    _host.SetField(_document,"native_settings_slider_"+i,Math.Round(position).ToString(CultureInfo.InvariantCulture));
                    Text("native_settings_readout_"+i,row.Value+"  // "+row.Minimum.Value.ToString("0.###",CultureInfo.InvariantCulture)+"–"+row.Maximum.Value.ToString("0.###",CultureInfo.InvariantCulture));
                }
                _host.SetField(_document,"native_settings_value_"+i,row.Value);
            }
            _fieldsDirty=false;
        }
        for(int i=0;i<_visible.Length;i++)if(_visible[i].Choices.Length>0||_visible[i].IsBinding)Text("native_settings_cycle_"+i,_visible[i].DisplayValue.ToUpperInvariant()+(_visible[i].IsBinding?"  // REBIND":"  ›"));
        for(int i=0;i<_visible.Length;i++)if(_visible[i].HasSlider&&double.TryParse(_visible[i].Value,NumberStyles.Float,CultureInfo.InvariantCulture,out double number))
        {
            var row=_visible[i];Text("native_settings_readout_"+i,row.Value+"  // "+row.Minimum!.Value.ToString("0.###",CultureInfo.InvariantCulture)+"–"+row.Maximum!.Value.ToString("0.###",CultureInfo.InvariantCulture));
            if(_host.FocusedElement()!="native_settings_slider_"+i)_host.SetField(_document,"native_settings_slider_"+i,Math.Round(Math.Clamp((number-row.Minimum.Value)/(row.Maximum.Value-row.Minimum.Value)*1000,0,1000)).ToString(CultureInfo.InvariantCulture));
        }
        _pages.Present(_document,++_revision,fields);
    }
    public void Dispose()
    {
        _host.VerifyOwnerThread();if(_disposed)return;
        _visual?.Dispose();_visual=null;
        if(_asModal&&HasLivePage)
        {
            if(_pages.Top!=_document||!_pages.CloseModal())throw new InvalidOperationException("Settings modal is covered or could not close.");
        }
        else if(_pages.Page==_document&&_pages.Top==_document)_pages.ClosePage();
        _disposed=true;_controller.Dispose();_document=default;
    }
}
#endif
