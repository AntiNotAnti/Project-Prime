#if (MPHREAD_RMLUI_POC || MPHREAD_RMLUI) && !MPHREAD_SERVER
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MphRead.Entities;
using MphRead.Mods.Input;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Settings;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace MphRead.Mods.Launcher.RmlUi.Settings;
internal sealed partial class SettingsPagePresenter
{
    private GamepadCalibration? _calibration;
    private GamepadMappingWizard? _mapping;
    private string? _setupDevice, _captureId;
    private long _setupRevision, _setupStarted, _captureStarted;
    private bool _mappingMode, _setupComplete, _captureReleased;
    private GamepadButtons _captureButtons, _setupButtons;
    private string _setupStatus="", _captureStatus="";
    private string? _completedProfileName;
    private GamepadRuntimeConfig? _conflictRuntime;
    private PadAction _conflictAction;
    private int _conflictSlot;
    private GamepadButtons _conflictButton,_conflictModifier;
    private void OpenTools()
    {
        GamepadProfiles.Initialize();
        _host.SetField(_page,"settings_controller_name","My controller");
        _host.SetField(_page,"settings_controller_file",Path.Combine(LauncherPrefs.Directory,"controller-profile.json"));
        _host.SetField(_page,"settings_controller_selected",GamepadProfiles.Profiles.FirstOrDefault()?.Name??"");
        _host.SetField(_page,"settings_capture_id",InputSettings.Bindings[0].Name);
    }
    private void ToolAction(int action)
    {
        string ProfileName()=>_host.ReadField(_page,"settings_controller_selected").Trim();
        string ProfileFile()=>_host.ReadField(_page,"settings_controller_file").Trim();
        switch(action)
        {
            case 38:
                if(!ArchiveReady())return;
                StopSetup("");CancelBindingCapture();
                var devices=GamepadManager.Devices;
                int current=devices.ToList().FindIndex(d=>d.DeviceId==GamepadManager.SelectedDeviceId);
                GamepadManager.SelectDevice(current+1<devices.Count?devices[current+1].DeviceId:null);
                _backend=new(_menu,_state,_inGame,_players);_controller=new(_backend);_controller.SelectCategory(SettingsCategory.Controls);
                _operationStatus="Selected controller changed.";break;
            case 39:
                if(!ArchiveReady())return;
                GamepadProfiles.Save(_host.ReadField(_page,"settings_controller_name"));
                _host.SetField(_page,"settings_controller_selected",GamepadProfiles.ActiveName);_operationStatus="Controller profile saved.";break;
            case 40:
                var profile=GamepadProfiles.Profiles.FirstOrDefault(p=>p.Name==ProfileName())??throw new InvalidDataException("Choose a saved controller profile.");
                _controller.StageSnapshot(_backend.StageControllerProfile(_controller.Draft,profile));_operationStatus="Controller profile staged. Apply to save.";break;
            case 41:
                if(!ArchiveReady())return;
                GamepadProfiles.Assign(ProfileName(),GamepadManager.ActiveDevice??throw new InvalidDataException("Connect and select a controller first."));
                RecaptureController();
                _operationStatus="Controller profile assigned.";break;
            case 42:
                if(!ArchiveReady())return;
                GamepadProfiles.Unassign(GamepadManager.ActiveDevice??throw new InvalidDataException("Connect and select a controller first."));
                RecaptureController();
                _operationStatus="Automatic controller profile assignment removed.";break;
            case 43:
                if(OperatingSystem.IsAndroid())Start(ExportControllerProfile(ProfileName()));
                else{GamepadProfiles.Export(ProfileName(),ProfileFile());_operationStatus="Controller profile exported.";}break;
            case 44:
                if(OperatingSystem.IsAndroid()){Start(ImportControllerProfile());break;}
                string imported=GamepadProfiles.Import(ProfileFile());_host.SetField(_page,"settings_controller_selected",imported);
                _operationStatus="Controller profile imported. Load it to stage its settings.";break;
            case 45:StartSetup(false);break;
            case 46:if(!OperatingSystem.IsAndroid()&&ArchiveReady())StartSetup(true);break;
            case 47:ApplySetup();break;
            case 48:StopSetup("Setup cancelled. Settings unchanged.");CloseModal();break;
            case 49:if(!OperatingSystem.IsAndroid()&&ArchiveReady()){GamepadMappings.ResetOverrides();_operationStatus="Custom mappings reset. Restart to restore platform mappings.";}break;
            case 50:Start(Task.Run(()=>Maintenance.ClearMapBuildCache().Summary,_pages.Lifetime(_page)));break;
            case 51:Start(Task.Run(()=>Maintenance.ClearThumbnails().Summary,_pages.Lifetime(_page)));break;
            case 52:if(!_inGame&&ThumbnailHost.CanRender)Start(RebuildThumbnails());break;
            case 53:
                if(LogShare.Current is not ILogShare sharer||!LogArchive.Any())return;
                string path=sharer.StagingPath(LogArchive.FileName());
                if(!LogArchive.Create(path,out string error)||!sharer.Share(path,Branding.Name+" debugging logs",out error))throw new IOException(error);
                _operationStatus="Log sharing chooser opened.";break;
            case 59:StartBindingCapture();break;
            case 60:CancelBindingCapture();CloseModal();break;
            case 61:ResolvePadConflict("Swap");break;
            case 62:ResolvePadConflict("Replace");break;
            case 63:ResolvePadConflict("Keep");break;
        }
    }
    private void RecaptureController()
    {
        // Assign/Unassign replaces the selected device's Runtime object. A clean
        // Settings transaction must now capture that authoritative replacement.
        _backend=new(_menu,_state,_inGame,_players);_controller=new(_backend);
        _controller.SelectCategory(SettingsCategory.Controls);_resetNativeFields=true;
    }
    private static async Task<string> RebuildThumbnails()
    {
        var cleared=await Task.Run(Maintenance.ClearThumbnails);
        int rendered=await ThumbnailHost.RenderMissingAsync(line=>DebugLog.Line("thumbnails",line));
        int missing=ThumbnailGenerator.MissingThumbnails().Count;
        return cleared.Summary+$" Rendered {rendered} thumbnails; {missing} still unavailable.";
    }
    private async Task<string> ExportControllerProfile(string name)
    {
        var lifetime=_pages.Lifetime(_page);
        string path=Path.Combine(Path.GetTempPath(),"prime-controller-"+Guid.NewGuid().ToString("N")+".json");
        try
        {
            GamepadProfiles.Export(name,path);
            using Stream? output=await (SettingsArchivePlatform.PickDocument??throw new InvalidOperationException("Document picker unavailable."))(true);
            lifetime.ThrowIfCancellationRequested();if(output==null)return "Controller profile export cancelled.";
            using var input=File.OpenRead(path);await input.CopyToAsync(output,lifetime);await output.FlushAsync(lifetime);return "Controller profile exported.";
        }
        finally{if(File.Exists(path))File.Delete(path);}
    }
    private async Task<string> ImportControllerProfile()
    {
        var lifetime=_pages.Lifetime(_page);
        using Stream? input=await (SettingsArchivePlatform.PickDocument??throw new InvalidOperationException("Document picker unavailable."))(false);
        lifetime.ThrowIfCancellationRequested();if(input==null)return "Controller profile import cancelled.";
        string path=Path.Combine(Path.GetTempPath(),"prime-controller-"+Guid.NewGuid().ToString("N")+".json");
        try
        {
            using(var output=File.Create(path))
            {
                var buffer=new byte[8192];int read,total=0;
                while((read=await input.ReadAsync(buffer,lifetime))>0)
                {total+=read;if(total>65536)throw new InvalidDataException("Controller profile exceeds 64 KiB.");await output.WriteAsync(buffer.AsMemory(0,read),lifetime);}
            }
            lifetime.ThrowIfCancellationRequested();_completedProfileName=GamepadProfiles.Import(path);
            return "Controller profile imported. Load it to stage its settings.";
        }
        finally{if(File.Exists(path))File.Delete(path);}
    }
    private void PresentTools(Dictionary<string,RmlUiBindingValue> bindings)
    {
        bindings["settings_controller_device"]=RmlUiBindingValue.FromText(GamepadManager.ActiveDevice?.Name??"No controller connected");
        bindings["settings_controller_library"]=RmlUiBindingValue.FromText("Saved profiles: "+string.Join(", ",GamepadProfiles.Profiles.Select(p=>p.Name)));
        bindings["visible:settings_manual_mapping"]=RmlUiBindingValue.FromBoolean(!OperatingSystem.IsAndroid());
        bindings["visible:settings_controller_file_tools"]=RmlUiBindingValue.FromBoolean(!OperatingSystem.IsAndroid());
        bindings["visible:settings_share_logs"]=RmlUiBindingValue.FromBoolean(LogShare.Available);
        bindings["disabled:settings_render_thumbnails"]=RmlUiBindingValue.FromBoolean(_inGame||!ThumbnailHost.CanRender||_operation!=null);
        bindings["settings_controls_help"]=RmlUiBindingValue.FromText("Search for mouse, controller, touch, stylus, or a binding. Controller profiles and setup apply to the selected device.");
        if(_modal!=default&&_setupDevice!=null)
            _pages.Present(_modal,++_bindingRevision,new Dictionary<string,RmlUiBindingValue>{
                ["settings_setup_status"]=RmlUiBindingValue.FromText(_setupStatus),
                ["disabled:settings_setup_apply"]=RmlUiBindingValue.FromBoolean(!_setupComplete||(!_mappingMode&&!(_calibration?.Valid??false)))});
        if(_modal!=default&&_captureId!=null)
            _pages.Present(_modal,++_bindingRevision,new Dictionary<string,RmlUiBindingValue>{
                ["settings_capture_status"]=RmlUiBindingValue.FromText(_captureStatus)});
    }
    private void StartSetup(bool mapping)
    {
        StopSetup("");CancelBindingCapture();
        var snapshot=GamepadManager.Snapshot;
        if(snapshot.DeviceId==null)throw new InvalidOperationException("Connect and select a controller first.");
        _setupDevice=snapshot.DeviceId;_setupRevision=snapshot.Revision;_setupButtons=snapshot.State.Buttons;
        _mappingMode=mapping;_setupStarted=Environment.TickCount64;_setupComplete=false;
        _calibration=mapping?null:new();_mapping=null;GamepadContexts.Capturing=true;
        if(mapping){GamepadMappingWizard.Latest=null;GamepadMappingWizard.RequestedDevice=_setupDevice;}
        _setupStatus="Release controls. Keep both sticks centered. Cancel stops setup.";
        _modal=_pages.OpenModal(new("settings-setup","pages/settings/setup.rml","settings_setup_cancel"));
    }
    private void TickTools()
    {
        if((_captureId!=null||_setupDevice!=null)&&(_modal==default||!_host.IsAlive(_modal)))
        {CancelBindingCapture();StopSetup("Setup cancelled.");return;}
        var snapshot=GamepadManager.Snapshot;
        if(_captureId!=null)
        {
            if(Environment.TickCount64-_captureStarted>10000){CancelBindingCapture();CloseModal();_operationError="Binding capture timed out.";return;}
            if(!_captureReleased){_captureReleased=snapshot.State.Buttons==GamepadButtons.None;_captureButtons=snapshot.State.Buttons;}
            else if(_conflictRuntime==null&&_captureId.StartsWith("pad.",StringComparison.Ordinal))
            {
                var pressed=snapshot.State.Buttons&~_captureButtons;_captureButtons=snapshot.State.Buttons;
                if(pressed!=GamepadButtons.None)
                {
                    int bits=(int)pressed;var first=(GamepadButtons)(bits&-bits);
                    ChoosePadButton(first);
                }
            }
        }
        if(_setupDevice==null||_setupComplete)return;
        if(!GamepadContexts.Focused||snapshot.DeviceId!=_setupDevice||snapshot.Revision!=_setupRevision)
        {StopSetup("Controller or focus changed. Restart setup.");CloseModal();return;}
        long elapsed=Environment.TickCount64-_setupStarted;
        var newButtons=snapshot.State.Buttons&~_setupButtons;_setupButtons=snapshot.State.Buttons;
        if(!_mappingMode&&(newButtons&GamepadButtons.B)!=0){StopSetup("Calibration cancelled.");CloseModal();return;}
        if(_mappingMode)
        {
            var sample=GamepadMappingWizard.Latest;if(sample==null||sample.DeviceId!=_setupDevice)return;
            if(_mapping==null)
            {
                if(sample.Buttons.Any(b=>b)||sample.Hats.Any(h=>h!=0)){_setupStarted=Environment.TickCount64;return;}
                if(elapsed<1500)return;_mapping=new(sample);
            }
            try{_mapping.Sample(sample);_setupStatus=_mapping.Prompt;_setupComplete=_mapping.Complete;}
            catch(Exception ex){StopSetup(ex.Message);CloseModal();return;}
        }
        else
        {
            var device=GamepadManager.ActiveDevice;if(device==null||elapsed<1000)return;
            _calibration!.Sample(device.Value.RawState,elapsed<3500);
            _setupStatus=elapsed<3500?"Keep sticks and triggers released. Measuring rest...":$"Rotate both sticks fully and squeeze both triggers. {Math.Max(0,(10500-elapsed)/1000)} seconds remaining.";
            if(elapsed>=10500){_setupComplete=true;_setupStatus=_calibration.Summary;}
        }
        if(_setupComplete){GamepadContexts.Capturing=false;GamepadMappingWizard.RequestedDevice=null;}
    }
    private void ApplySetup()
    {
        if(!_setupComplete||GamepadManager.Snapshot.DeviceId!=_setupDevice)throw new InvalidOperationException("Finish setup with the same controller first.");
        if(_mappingMode)GamepadMappings.SaveOverride(_mapping!.Mapping);
        else
        {
            var runtime=_backend.DraftController(_controller.Draft);var frame=GamepadRuntimeConfig.Frame;
            try{GamepadRuntimeConfig.Frame=runtime;_calibration!.Apply();}
            finally{GamepadRuntimeConfig.Frame=frame;}
            _controller.StageSnapshot(_backend.StageController(_controller.Draft,runtime));
        }
        StopSetup(_mappingMode?"Mapping saved for this controller.":"Calibration staged. Apply to save.");CloseModal();
    }
    private void StopSetup(string status)
    {
        if(_setupDevice!=null)GamepadContexts.Capturing=false;
        _setupDevice=null;_setupComplete=false;_calibration=null;_mapping=null;
        GamepadMappingWizard.RequestedDevice=null;GamepadMappingWizard.Latest=null;
        if(status.Length>0)_operationStatus=status;
    }
    private void StartBindingCapture()
    {
        string name=_host.ReadField(_page,"settings_capture_id").Trim();
        string id=name.StartsWith("pad.",StringComparison.Ordinal)||name.StartsWith("input.",StringComparison.Ordinal)||name.StartsWith("binding.",StringComparison.Ordinal)?name:"binding."+name;
        if(!_backend.Definitions.Any(d=>d.Id==id)||(!id.StartsWith("binding.",StringComparison.Ordinal)&&!id.StartsWith("pad.pad_",StringComparison.Ordinal)&&!(id.StartsWith("input.",StringComparison.Ordinal)&&id.EndsWith("Key",StringComparison.Ordinal))))
            throw new FormatException("Enter the name of a keyboard/mouse action, or a controller binding shown above.");
        StopSetup("");_captureId=id;_captureStarted=Environment.TickCount64;_captureButtons=GamepadManager.Snapshot.State.Buttons;_captureReleased=false;
        GamepadContexts.Capturing=true;_captureStatus="Release the opening button, then press a key, mouse button, wheel, or controller button. Escape cancels.";
        _modal=_pages.OpenModal(new("settings-binding","pages/settings/capture.rml","settings_capture_cancel"));
    }
    internal bool TryCaptureKey(Keys key)
    {
        if(_captureId==null)return false;
        if(key==Keys.Escape){CancelBindingCapture();CloseModal();Refresh();return true;}
        if(_conflictRuntime!=null)return false;
        if(_captureId.StartsWith("input.",StringComparison.Ordinal)&&Environment.TickCount64-_captureStarted>=200)
        {CompleteBinding(key==Keys.Delete?"none":key.ToString());return true;}
        if(!_captureId.StartsWith("binding.",StringComparison.Ordinal)||Environment.TickCount64-_captureStarted<200)return true;
        var parts=_controller.Draft[_captureId].Split(':');parts[0]=ButtonType.Key.ToString();parts[1]=(key==Keys.Delete?Keys.Unknown:key).ToString();
        CompleteBinding(string.Join(':',parts));return true;
    }
    internal bool TryCaptureMouse(MouseButton button)
    {
        if(_captureId==null)return false;
        if(_conflictRuntime!=null)return false;
        if(_captureId.StartsWith("binding.",StringComparison.Ordinal)&&Environment.TickCount64-_captureStarted>=200)
        {var parts=_controller.Draft[_captureId].Split(':');parts[0]=ButtonType.Mouse.ToString();parts[2]=button.ToString();CompleteBinding(string.Join(':',parts));}
        return true;
    }
    internal bool TryCaptureWheel(float delta)
    {
        if(_captureId==null)return false;
        if(_conflictRuntime!=null)return false;
        if(_captureId.StartsWith("binding.",StringComparison.Ordinal)&&delta!=0&&Environment.TickCount64-_captureStarted>=200)
        {var parts=_controller.Draft[_captureId].Split(':');parts[0]=(delta>0?ButtonType.ScrollUp:ButtonType.ScrollDown).ToString();CompleteBinding(string.Join(':',parts));}
        return true;
    }
    private void CompleteBinding(string value){_controller.Set(_captureId!,value);CancelBindingCapture();CloseModal();Refresh();}
    private void ChoosePadButton(GamepadButtons button)
    {
        try
        {
            string name=_captureId![8..];string actionName=name.Split('_')[0];
            if(!Enum.TryParse<PadAction>(actionName,out var action))throw new FormatException("Choose a controller binding action.");
            if(name.EndsWith("_modifier",StringComparison.Ordinal)){CompleteBinding(button.ToString());return;}
            var modifier=Enum.Parse<GamepadButtons>(_controller.Draft["pad.gamepad_binding_modifier"]);
            if(button==modifier)return;
            int slot=name.EndsWith("_secondary",StringComparison.Ordinal)?1:0;
            var runtime=_backend.DraftController(_controller.Draft);var conflicts=runtime.Bindings.Conflicts(action,button,modifier);
            if(conflicts.Count==0)
            {
                runtime.Bindings.SetSlot(action,slot,button,modifier);
                _controller.StageSnapshot(_backend.StageController(_controller.Draft,runtime));CancelBindingCapture();CloseModal();return;
            }
            _conflictRuntime=runtime;_conflictAction=action;_conflictSlot=slot;_conflictButton=button;_conflictModifier=modifier;
            _captureStatus=button+" is already assigned to "+string.Join(" / ",conflicts.Select(PadBindings.Name))+". Choose how to resolve the conflict.";
            GamepadContexts.Capturing=false;CloseModal();_captureStarted=Environment.TickCount64;
            _modal=_pages.OpenModal(new("settings-conflict","pages/settings/conflict.rml","settings_capture_cancel"));
        }
        catch(Exception ex){_operationError=ex.Message;CancelBindingCapture();CloseModal();}
    }
    private void ResolvePadConflict(string resolution)
    {
        if(_conflictRuntime==null)return;
        _conflictRuntime.Bindings.Assign(_conflictAction,_conflictSlot,_conflictButton,resolution,_conflictModifier);
        _controller.StageSnapshot(_backend.StageController(_controller.Draft,_conflictRuntime));CancelBindingCapture();CloseModal();
    }
    private void CancelBindingCapture(){if(_captureId!=null)GamepadContexts.Capturing=false;_captureId=null;_conflictRuntime=null;}
}
#endif
