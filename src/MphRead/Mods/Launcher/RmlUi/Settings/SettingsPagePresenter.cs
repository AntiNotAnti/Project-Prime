#if (MPHREAD_RMLUI_POC || MPHREAD_RMLUI) && !MPHREAD_SERVER
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Settings;
using MphRead.Mods.Render.Hud;

namespace MphRead.Mods.Launcher.RmlUi.Settings;

internal sealed partial class SettingsPagePresenter : IDisposable
{
    private readonly RmlUiHost _host;
    private readonly RmlUiPageManager _pages;
    private SettingsController _controller;
    private EngineSettingsBackend _backend;
    private readonly SceneGameState _state;
    private readonly ScenePlayerRegistry? _players;
    private readonly Action? _editHud;
    private readonly Action _closed;
    private readonly Action _gameFiles;
    private readonly MenuSettings _menu;
    private readonly bool _inGame;
    private RmlUiDocumentToken _page, _modal;
    private SettingsSnapshot? _presented;
    private long _bindingRevision;
    private bool _disposed, _closing, _resetConfirmation;
    private bool _resetNativeFields;
    private string _operationStatus="", _operationError="";
    private Task<string>? _operation;
    private Action? _afterClose;
    private string _maintenanceSummary="",_storageSummary="",_filesDescription="",_creditsText="";
    private readonly Dictionary<string,string> _retainedUiFields=new(StringComparer.Ordinal);
    internal RmlUiDocumentToken Document=>_page;
    internal SettingsController Controller=>_controller;
    internal bool IsCapturingInput=>!_disposed&&_page!=default&&(_captureId!=null||_setupDevice!=null);
    internal SettingsPagePresenter(RmlUiHost host,RmlUiPageManager pages,MenuSettings menu,SceneGameState state,
        Action closed,Action gameFiles,bool inGame=false,ScenePlayerRegistry? players=null,Action? editHud=null)
    {
        _host=host;_pages=pages;_menu=menu;_state=state;_players=players;_closed=closed;_gameFiles=gameFiles;_inGame=inGame;_editHud=editHud;
        _backend=new EngineSettingsBackend(menu,state,inGame,players);_controller=new(_backend);
    }
    internal void Open()
    {
        ObjectDisposedException.ThrowIf(_disposed,this);
        _page=_pages.OpenPage(new("settings","pages/settings/settings.rml","settings_value_0"));
        _host.SetField(_page,"settings_archive_path",Path.Combine(LauncherPrefs.Directory,"ProjectPrime-settings.zip"));
        OpenTools();
        foreach(var field in _retainedUiFields)_host.SetField(_page,field.Key,field.Value);
        RefreshSummaries();
        Refresh();
        _host.Update();
        _host.FocusDocument(_page,"settings_value_0");
    }
    internal bool HandleAction(in RmlUiIntent intent)
    {
        if(_disposed||_page==default||_pages.Page!=_page||!Owns(intent.Kind)||!_pages.Accept(intent))return false;
        if(_operation!=null){Refresh();return true;}
        if(_glyphOpen){HandleGlyphAction(intent);Refresh();return true;}
        if(intent.Kind is RmlUiIntentKind.SettingsApply or RmlUiIntentKind.SettingsCategory
            ||intent.Kind==RmlUiIntentKind.SettingsAction&&intent.Argument is not (10 or 56))
            if((intent.Document==_page||intent.Kind==RmlUiIntentKind.SettingsApply)&&!StageRows()){Refresh();return true;}
        switch(intent.Kind)
        {
            case RmlUiIntentKind.SettingsApply:
                if(_controller.Apply()&&!_controller.PendingVideoConfirmation&&_closing)Close();break;
            case RmlUiIntentKind.SettingsDiscard:
                _controller.Discard();_resetNativeFields=true;if(_closing)Close();break;
            case RmlUiIntentKind.SettingsCategory:_controller.SelectCategory((SettingsCategory)intent.Argument);_host.SetField(_page,"settings_search","");_operationStatus="";break;
            case RmlUiIntentKind.SettingsResetCategory:_controller.RevertCategory();_resetNativeFields=true;break;
            case RmlUiIntentKind.SettingsClose:RequestClose();break;
            case RmlUiIntentKind.SettingsKeepVideo:
                if(_controller.KeepVideo()){CloseModal();if(_closing)Close();}break;
            case RmlUiIntentKind.SettingsRevertVideo:_controller.RevertVideo();CloseModal();break;
            case RmlUiIntentKind.SettingsAction:Action(intent.Argument);break;
        }
        Refresh();
        if (intent.Kind == RmlUiIntentKind.SettingsCategory || intent.Kind == RmlUiIntentKind.SettingsAction
            && (intent.Argument >= 64 || intent.Argument is 0 or 1 or 54 or 55))
        {
            _host.Update();
            _host.FocusDocument(_page,"settings_value_0");
        }
        return true;
    }
    private void Action(int action)
    {
        if(action>=64)
        {
            _controller.SelectGroup(action-64); _host.SetField(_page,"settings_search","");
            _operationStatus=""; return;
        }
        if(action>=16&&action<28)
        {
            int index=action-16;if(_presented==null||index>=_presented.Fields.Count)return;
            var row=_presented.Fields[index];var choices=row.Definition.Choices;
            if(row.Definition.Kind==SettingsValueKind.Boolean)_controller.Set(row.Definition.Id,bool.TryParse(row.Value,out bool b)&&b?"false":"true");
            else if(choices.Count>0)
            {
                int previous=Enumerable.Range(0,choices.Count).FirstOrDefault(i=>choices[i].Equals(row.Value,StringComparison.OrdinalIgnoreCase),-1);
                _controller.Set(row.Definition.Id,choices[(previous+1)%choices.Count]);
            }
            return;
        }
        try
        {
            _operationError="";
            switch(action)
            {
                case 0:_controller.MovePage(-1);break;
                case 1:_controller.MovePage(1);break;
                case 2:_operationStatus="Visible settings staged in the draft.";break;
                case 3:if(ArchiveReady())Start(Export(_pages.Lifetime(_page)));break;
                case 4:if(!_inGame&&ArchiveReady())Start(Import(_pages.Lifetime(_page)));break;
                case 5:
                    if(!_inGame&&ArchiveReady())
                    {_resetConfirmation=true;_modal=_pages.OpenModal(new("settings-reset","pages/settings/reset.rml","settings_reset_confirm"));}break;
                case 6:
                    if(!_inGame&&_resetConfirmation&&ArchiveReady())
                    {SettingsPersistence.Replace(()=>SettingsArchive.Reset(LauncherPrefs.Directory));_operationStatus="Settings reset. Restart to activate defaults.";_resetConfirmation=false;CloseModal();}break;
                case 7:
                    if(!OperatingSystem.IsAndroid()){Directory.CreateDirectory(LauncherPrefs.Directory);Process.Start(new ProcessStartInfo(LauncherPrefs.Directory){UseShellExecute=true});}break;
                case 8:if(SettingsPersistence.RestartRequired)SettingsArchivePlatform.Restart();break;
                case 9:CloseModal();_closing=false;_resetConfirmation=false;_afterClose=null;break;
                case 10:RequestLeave(_gameFiles);break;
                case 11:Start(Task.Run(Update.DesktopUpdate.VerifyInstallation,_pages.Lifetime(_page)));break;
                case 12:Start(Task.Run(()=>Maintenance.CleanReproducibleData().Summary,_pages.Lifetime(_page)));break;
                case 13:ResetPerformance();break;
                case 14:if(!_inGame&&!OperatingSystem.IsAndroid())Start(PerformanceDiagnostic(_pages.Lifetime(_page)));break;
                case 28:_editHud?.Invoke();break;
                case 56:OpenGlyphPicker();break;
                case 54:_controller.Search(_host.ReadField(_page,"settings_search"));break;
                case 55:_controller.Search("");_host.SetField(_page,"settings_search","");break;
                default:ToolAction(action);break;
            }
        }
        catch(Exception ex){_operationError=ex.Message;}
    }
    private bool StageRows()
    {
        if(_presented==null||_controller.PendingVideoConfirmation||SettingsPersistence.RestartRequired)return true;
        bool accepted=true;
        for(int i=0;i<_presented.Fields.Count;i++)
        {
            string value=_host.ReadField(_page,"settings_value_"+i);
            if(value!=_presented.Fields[i].Value)accepted=_controller.Set(_presented.Fields[i].Definition.Id,value)&&accepted;
        }
        return accepted;
    }
    internal void ObservePresentedFrame(bool usable)=>_controller.ObservePresentedFrame(usable);
    internal HudProfile CaptureHudDraft()
    {
        if(!StageRows())throw new InvalidOperationException("Correct the visible settings first.");
        return _backend.DraftHud(_controller.Draft);
    }
    internal bool AcceptHudDraft(HudProfile profile)
    {
        bool accepted=_controller.StageSnapshot(_backend.StageHud(_controller.Draft,profile));Refresh();return accepted;
    }
    internal bool SuspendForHud()
    {
        if(_disposed||_page==default||_operation!=null||_controller.PendingVideoConfirmation||!StageRows())return false;
        foreach(string id in new[]{"settings_archive_path","settings_controller_name","settings_controller_file","settings_controller_selected","settings_capture_id","settings_search"})
            _retainedUiFields[id]=_host.ReadField(_page,id);
        StopSetup("");CancelBindingCapture();CloseModal();
        if(!_pages.ClosePage())return false;
        _page=_modal=default;_presented=null;return true;
    }
    internal void ResumeFromHud()
    {
        ObjectDisposedException.ThrowIf(_disposed,this);
        if(_page==default)Open();else Refresh();
    }
    internal void Refresh()
    {
        if(_disposed||_page==default||!_host.IsAlive(_page)||_pages.Page!=_page)return;
        TickTools();
        if(_glyphOpen&&(_modal==default||!_host.IsAlive(_modal)))_glyphOpen=false;
        if(_operation?.IsCompleted==true)
        {
            try{_operationStatus=_operation.GetAwaiter().GetResult();_operationError="";}
            catch(Exception ex){_operationError=ex.Message;}
            _operation=null;
            if(_completedProfileName!=null){_host.SetField(_page,"settings_controller_selected",_completedProfileName);_completedProfileName=null;}
            RefreshSummaries();
        }
        var s=_controller.Snapshot();
        bool nativeDraft=NativeRowsChanged();
        if(_modal!=default&&!_host.IsAlive(_modal))
        {
            _modal=default;_closing=false;_resetConfirmation=false;
            if(s.VideoConfirmation)_controller.RevertVideo();s=_controller.Snapshot();
        }
        if(s.VideoConfirmation&&_modal==default)_modal=_pages.OpenModal(new("settings-video","pages/settings/video.rml","settings_video_revert"));
        if(!s.VideoConfirmation&&_modal!=default&&!_closing&&!_resetConfirmation&&!_glyphOpen&&_setupDevice==null&&_captureId==null)CloseModal();
        var b=new Dictionary<string,RmlUiBindingValue>();
        void Text(string id,string value)=>b[id]=RmlUiBindingValue.FromText(value);
        void Flag(string id,bool value)=>b[id]=RmlUiBindingValue.FromBoolean(value);
        Text("settings_title",_controller.Query.Length>0?"Search results":s.Category==SettingsCategory.Hud?"HUD Studio":s.Category+" / "+_controller.Group);
        var groups=_controller.Groups;
        for(int i=0;i<192;i++)
        {
            Flag("visible:settings_group_"+i,i<groups.Count);
            if(i<groups.Count){Text("settings_group_"+i,groups[i]);Flag("class:settings_group_"+i+":selected",groups[i]==_controller.Group&&_controller.Query.Length==0);}
        }
        foreach(SettingsCategory category in Enum.GetValues<SettingsCategory>())
            Flag("class:settings_"+category.ToString().ToLowerInvariant()+"_tab:selected",category==s.Category);
        Text("settings_paging",$"Page {s.Page+1} of {s.PageCount}");
        Flag("visible:settings_pager",s.Category!=SettingsCategory.Hud||_controller.Query.Length>0);
        Text("settings_status",_operation!=null?"Working...":_operationStatus.Length>0?_operationStatus:s.Status);
        Text("settings_error",_operationError.Length>0?_operationError:s.Error);
        Text("settings_dirty",s.Dirty||nativeDraft?"Unsaved changes":"Saved settings");
        Flag("disabled:settings_apply",(!s.Dirty&&!nativeDraft)||s.RestartRequired||s.VideoConfirmation||_operation!=null);
        Flag("disabled:settings_discard",s.RestartRequired||s.VideoConfirmation||_operation!=null);
        Flag("disabled:settings_previous",s.Page==0);Flag("disabled:settings_next",s.Page+1==s.PageCount);
        Flag("visible:settings_system",s.Category==SettingsCategory.System);
        Flag("visible:settings_maintenance",s.Category==SettingsCategory.Maintenance);
        Flag("visible:settings_profile_tools",s.Category==SettingsCategory.Profile);
        Flag("visible:settings_controller_tools",s.Category==SettingsCategory.Controller&&_controller.Group=="Controller / preferences"&&_controller.Query.Length==0);
        Flag("visible:settings_hud_tools",s.Category==SettingsCategory.Hud&&_controller.Query.Length==0);
        Flag("disabled:settings_hud_edit",_editHud==null||s.RestartRequired||s.VideoConfirmation);
        Flag("visible:settings_credits",s.Category==SettingsCategory.Credits);
        Flag("visible:settings_restart",s.RestartRequired);
        Flag("disabled:settings_import",_inGame||s.RestartRequired||_operation!=null);
        Flag("disabled:settings_reset",_inGame||s.RestartRequired||_operation!=null);
        Flag("disabled:settings_diagnostic",_inGame||OperatingSystem.IsAndroid()||_operation!=null);
        Flag("visible:settings_open_folder",!OperatingSystem.IsAndroid());
        Text("settings_installation",_maintenanceSummary);Text("settings_storage",_storageSummary);
        Text("settings_files",_filesDescription);
        PresentTools(b);
        Text("settings_credits_text",_creditsText);
        PresentGlyphPicker();
        for(int i=0;i<SettingsController.RowsPerPage;i++)
        {
            bool present=i<s.Fields.Count;Flag("visible:settings_row_"+i,present);if(!present)continue;
            var row=s.Fields[i];Text("settings_label_"+i,row.Definition.Label);Text("settings_help_"+i,row.Definition.Help);
            Text("settings_cycle_"+i,row.Definition.Kind==SettingsValueKind.Boolean?"TOGGLE":row.Definition.Choices.Count>0?"NEXT":"SET");
            Flag("class:settings_row_"+i+":dirty",row.Changed);
            Flag("disabled:settings_value_"+i,s.RestartRequired||s.VideoConfirmation);
            Flag("disabled:settings_cycle_"+i,s.RestartRequired||s.VideoConfirmation);
            if(_resetNativeFields||_presented==null||i>=_presented.Fields.Count
                ||row.Definition.Id!=_presented.Fields[i].Definition.Id||row.Value!=_presented.Fields[i].Value)
                _host.SetField(_page,"settings_value_"+i,row.Value);
        }
        _pages.Present(_page,++_bindingRevision,b);_presented=s;_resetNativeFields=false;
        if(_modal!=default&&s.VideoConfirmation)
            _pages.Present(_modal,++_bindingRevision,new Dictionary<string,RmlUiBindingValue>{
                ["settings_video_status"]=RmlUiBindingValue.FromText($"Reverts in {s.VideoSecondsRemaining} seconds."),
                ["disabled:settings_video_keep"]=RmlUiBindingValue.FromBoolean(!s.UsableVideoFrame),
                ["settings_video_error"]=RmlUiBindingValue.FromText(s.Error)});
        else if(_modal!=default&&_closing)
            _pages.Present(_modal,++_bindingRevision,new Dictionary<string,RmlUiBindingValue>{
                ["settings_video_error"]=RmlUiBindingValue.FromText(s.Error),
                ["disabled:settings_close_apply"]=RmlUiBindingValue.FromBoolean(s.RestartRequired||_operation!=null)});
    }
    private void RequestClose()
    {
        StageRows();
        if(!_controller.Dirty&&!NativeRowsChanged()&&!_controller.PendingVideoConfirmation){Close();return;}
        _closing=true;
        if(_controller.PendingVideoConfirmation)return;
        _modal=_pages.OpenModal(new("settings-unsaved","pages/settings/unsaved.rml","settings_close_cancel"));
    }
    internal bool Back()
    {
        if(_disposed||_pages.Page!=_page)return false;
        if(_modal!=default)
        {
            _glyphOpen=false;
            if(_controller.PendingVideoConfirmation)_controller.RevertVideo();
            StopSetup("Setup cancelled.");CancelBindingCapture();CloseModal();_closing=false;_resetConfirmation=false;_afterClose=null;Refresh();return true;
        }
        StageRows();RequestClose();Refresh();return true;
    }
    internal void RequestLeave(Action nextRoute)
    {
        if(_disposed||_page==default)return;
        StageRows();
        _afterClose=nextRoute??throw new ArgumentNullException(nameof(nextRoute));RequestClose();Refresh();
    }
    internal bool HasPendingLeave => !_disposed && _page != default && _afterClose != null;
    private void RefreshSummaries()
    {
        _maintenanceSummary=Maintenance.PerformanceSummary(_menu);_storageSummary=Maintenance.StorageSummary();_filesDescription=GameFiles.Describe();
        _creditsText=Credits.Summary+"\n\n"+Credits.Foundation.Who+"\n"+Credits.Foundation.What+"\n"+Credits.Foundation.Where+"\n\n"+Credits.Author+"\n"+Credits.ForkWork+"\n\n"+string.Join("\n\n",Credits.Entries.Where(e=>e!=Credits.Foundation).Select(e=>e.Who+"\n"+e.What+"\n"+e.Where));
    }
    private bool NativeRowsChanged()
    {
        if(_presented==null||_resetNativeFields||_page==default||!_host.IsAlive(_page))return false;
        for(int i=0;i<_presented.Fields.Count;i++)
            if(_host.ReadField(_page,"settings_value_"+i)!=_presented.Fields[i].Value)return true;
        return false;
    }
    private void CloseModal(){if(_modal!=default&&_pages.CloseModal())_modal=default;}
    private void Close(){_controller.Discard();CloseModal();if(_pages.ClosePage()){_page=default;var finished=_afterClose??_closed;_afterClose=null;finished();}}
    private bool ArchiveReady()
    {
        if(_controller.Dirty){_operationError="Apply or discard your draft first.";return false;}
        return !SettingsPersistence.RestartRequired;
    }
    private void Start(Task<string> operation){_operation=operation;_operationStatus="";_operationError="";}
    private async Task<string> Export(CancellationToken lifetime)
    {
        if(!OperatingSystem.IsAndroid())
        {SettingsArchive.ExportFile(LauncherPrefs.Directory,_host.ReadField(_page,"settings_archive_path"),Program.Version.ToString());return "Settings exported.";}
        using var buffer=new MemoryStream();SettingsArchive.Export(LauncherPrefs.Directory,buffer,Program.Version.ToString());
        using Stream? output=await (SettingsArchivePlatform.PickDocument??throw new InvalidOperationException("Document picker unavailable."))(true);
        lifetime.ThrowIfCancellationRequested();if(output==null)return "Export cancelled.";
        buffer.Position=0;await buffer.CopyToAsync(output,lifetime);await output.FlushAsync(lifetime);return "Settings exported.";
    }
    private async Task<string> Import(CancellationToken lifetime)
    {
        using Stream? stream=OperatingSystem.IsAndroid()
            ?await (SettingsArchivePlatform.PickDocument??throw new InvalidOperationException("Document picker unavailable."))(false)
            :File.OpenRead(_host.ReadField(_page,"settings_archive_path"));
        lifetime.ThrowIfCancellationRequested();if(stream==null)return "Import cancelled.";
        SettingsPersistence.Replace(()=>SettingsArchive.Import(LauncherPrefs.Directory,stream));return "Settings imported. Restart to activate them.";
    }
    private void ResetPerformance()
    {
        _controller.Set("menu.GraphicsPreset","original");
        foreach(var pair in new Dictionary<string,string>{["menu.ResolutionScale"]="100",["menu.FieldOfView"]="78",["menu.TextureSampling"]="auto",["menu.TextureQuality"]="automatic",["menu.ShowFps"]="off",["menu.SmoothNativeHud"]="on",["menu.FrameRateCap"]="display",["menu.CelShading"]="off",["menu.CelBands"]="8",["menu.CelEdge"]="50"})_controller.Set(pair.Key,pair.Value);
        _operationStatus="Performance defaults staged. Apply to save.";
    }
    private static async Task<string> PerformanceDiagnostic(CancellationToken lifetime)
    {
        string executable=Environment.ProcessPath??throw new InvalidOperationException("Could not locate executable.");
        string directory=Path.Combine(LauncherPrefs.Directory,"perf");Directory.CreateDirectory(directory);
        string output=Path.Combine(directory,$"perf-manual-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        var start=new ProcessStartInfo(executable){WorkingDirectory=AppContext.BaseDirectory,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        if(Path.GetFileNameWithoutExtension(executable).Equals("dotnet",StringComparison.OrdinalIgnoreCase))start.ArgumentList.Add(Environment.GetCommandLineArgs()[0]);
        foreach(string arg in new[]{"-perfcheck","MP3 PROVING GROUND","-seconds","20","-hz","144","-output",output})start.ArgumentList.Add(arg);
        using var process=Process.Start(start)??throw new InvalidOperationException("Could not start diagnostic.");
        var stdout=process.StandardOutput.ReadToEndAsync(lifetime);var stderr=process.StandardError.ReadToEndAsync(lifetime);
        await process.WaitForExitAsync(lifetime);await stdout;string errors=await stderr;
        if(process.ExitCode!=0)throw new IOException("Performance diagnostic failed: "+errors.Trim());
        return File.Exists(output)?"Performance report saved to "+output:"No performance report was produced.";
    }
    private static bool Owns(RmlUiIntentKind kind)=>kind is RmlUiIntentKind.SettingsApply or RmlUiIntentKind.SettingsDiscard
        or RmlUiIntentKind.SettingsCategory or RmlUiIntentKind.SettingsResetCategory or RmlUiIntentKind.SettingsClose
        or RmlUiIntentKind.SettingsKeepVideo or RmlUiIntentKind.SettingsRevertVideo or RmlUiIntentKind.SettingsAction;
    public void Dispose(){if(_disposed)return;StopSetup("");CancelBindingCapture();_controller.Discard();if(_pages.Page==_page&&_page!=default)_pages.ClosePage();_page=_modal=default;_disposed=true;}
}
#endif
