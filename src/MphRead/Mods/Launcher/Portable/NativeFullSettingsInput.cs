#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using System.Globalization;
using System.Linq;
using MphRead.Mods.Input;
using OpenTK.Windowing.GraphicsLibraryFramework;
namespace MphRead.Mods.Launcher;
internal sealed partial class NativeFullSettingsController
{
    internal static readonly string[] ControllerPresets={"Default","Bumper Jumper","Southpaw","Classic","Custom"};
    private PadBindingState _savedBindings=GamepadRuntimeConfig.Current.Bindings.Clone();
    private PadBindingState? _bindingsDraft;
    private bool HiddenBindingsDirty=>_bindingsDraft!=null&&!BindingLines(_bindingsDraft).SequenceEqual(BindingLines(_savedBindings));
    private static string[] BindingLines(PadBindingState state)
    {var lines=new System.Collections.Generic.List<string>();state.Write(lines);lines.Add("preset="+state.Preset);return lines.ToArray();}
    private void RebaseBindings(){_savedBindings=GamepadRuntimeConfig.Current.Bindings.Clone();_bindingsDraft=null;UpdateLegacyHints(_savedBindings);}
    private void UpdateLegacyHints(PadBindingState state)
    {
        foreach(var row in Rows.Where(r=>r.BindingAction!=null))
        {
            var action=row.BindingAction!.Value;var extras=state.Get(action)&~(state.Slot(action,0)|state.Slot(action,1));
            row.Description=extras==0?"Choose to capture a button. Backspace/Delete clears this slot.":"Preserved additional legacy alternatives: "+extras+". Conflicts include these alternatives.";
        }
    }
    private NativeSettingRow? _capture,_conflictTarget;
    private NativeSettingRow[] _conflictRows=Array.Empty<NativeSettingRow>();
    private string _conflictValue="";
    private GamepadButtons _captureHeld,_captureModifier;
    private long _captureRevision;
    private string? _captureDevice;
    private GamepadRuntimeConfig? _captureRuntime;
    private GamepadCalibration? _calibration;
    private bool _calibrationRest;
    internal bool Capturing=>_capture!=null;
    internal bool ControllerCapturing=>_capture?.BindingAction!=null;
    internal bool BindingConflict=>_conflictTarget!=null&&Confirmation.Length>0;
    internal string CaptureModifierLabel=>"MODIFIER: "+_captureModifier;
    internal string CurrentDeviceLabel=>GamepadManager.SelectedDeviceId is {} id
        ? GamepadManager.Devices.FirstOrDefault(d=>d.DeviceId==id).Name??"Selected controller disconnected"
        : "Automatic (last used)";
    internal string CapturePrompt=>_capture==null?"":_capture.BindingAction!=null
        ? "Press a controller button for "+_capture.Label+". "+(_captureModifier==0?"No modifier.":"Chord modifier: "+_captureModifier+".")+" Backspace/Delete clears this slot; Escape cancels."
        : "Press a key, mouse button or wheel for "+_capture.Label+". Backspace/Delete clears; Escape cancels.";
    internal void BeginCapture(NativeSettingRow row)
    {
        if(Busy||ConfirmingVideo||Confirmation.Length>0)return;
        CancelBinding();_capture=row;var snapshot=GamepadManager.Snapshot;
        _captureHeld=snapshot.State.Buttons;_captureRevision=snapshot.Revision;_captureDevice=snapshot.DeviceId;_captureRuntime=GamepadRuntimeConfig.Current;
        var modifier=Rows.FirstOrDefault(r=>r.Group=="Controller"&&r.Label=="Binding Modifier");
        _captureModifier=modifier!=null&&Enum.TryParse<GamepadButtons>(modifier.Value,true,out var value)?value:0;
        Status=CapturePrompt;
    }
    internal void CycleCaptureModifier()
    {
        if(!ControllerCapturing)return;
        var choices=Enum.GetValues<GamepadButtons>().Where(PadBindings.Single).ToArray();
        _captureModifier=choices[(Array.IndexOf(choices,_captureModifier)+1)%choices.Length];Status=CapturePrompt;
    }
    internal bool KeyDown(Keys key)
    {
        if(_capture==null)return false;
        if(key==Keys.Escape){CancelBinding();Status="Binding capture cancelled.";return true;}
        if(key is Keys.Backspace or Keys.Delete){CaptureBinding(_capture.BindingAction!=null?"None+None":"Unknown");return true;}
        if(_capture.KeyboardBinding)CaptureBinding(key.ToString());return true;
    }
    internal bool MouseDown(MouseButton button)
    {if(_capture==null)return false;if(_capture.KeyboardBinding&&_capture.Group=="Keyboard bindings")CaptureBinding("Mouse:"+button);return true;}
    internal bool ScrollBinding(bool up)
    {if(_capture==null)return false;if(_capture.KeyboardBinding&&_capture.Group=="Keyboard bindings")CaptureBinding(up?"ScrollUp":"ScrollDown");return true;}
    internal void CancelBinding(string? reason=null)
    {_capture=null;_captureRuntime=null;_conflictTarget=null;_conflictRows=Array.Empty<NativeSettingRow>();_conflictValue="";if(_confirmationAction==25)Confirmation="";if(reason!=null)Status=reason;}
    internal bool SuspendInput(string reason)
    {
        bool pending=Capturing||BindingConflict||_calibration!=null;
        CancelBinding();_calibration=null;
        if(pending)Status=reason;return pending;
    }
    private void CaptureBinding(string value)
    {
        if(_capture==null)return;var row=_capture;_capture=null;_captureRuntime=null;
        if(row.Validate(value) is {} error){Status=error;return;}
        NativeSettingRow[] conflicts;
        if(row.BindingAction is {} action)
        {
            var parts=value.Split('+');var modifier=Enum.Parse<GamepadButtons>(parts[0],true);var button=Enum.Parse<GamepadButtons>(parts[1],true);
            var draft=ControllerDraft();var actions=draft.Bindings.Conflicts(action,button,modifier);
            conflicts=Rows.Where(r=>r.BindingAction is {} other&&actions.Contains(other)).GroupBy(r=>r.BindingAction).Select(g=>g.FirstOrDefault(r=>r.Value.Equals(value,StringComparison.OrdinalIgnoreCase))??g.First()).ToArray();
        }
        else conflicts=value=="Unknown"?Array.Empty<NativeSettingRow>():Rows.Where(r=>r!=row&&r.KeyboardBinding&&r.Group==row.Group&&r.Value.Equals(value,StringComparison.OrdinalIgnoreCase)).ToArray();
        if(conflicts.Length>0)
        {
            _conflictTarget=row;_conflictRows=conflicts;_conflictValue=value;_confirmationAction=25;
            Confirmation=value+" is assigned to "+string.Join(" / ",conflicts.Select(r=>r.Label))+". Choose Swap, Replace, Keep Both, or Cancel.";return;
        }
        Stage(row,value);Status="Binding staged. Apply Changes to save, or Discard to restore.";
    }
    private void TickInput()
    {
        PollBindingCapture(GamepadManager.Snapshot);
        if(_calibration!=null)_calibration.Sample(GamepadManager.ActiveState,_calibrationRest);
    }
    internal void PollBindingCapture(GamepadSnapshot snapshot)
    {
        if(!ControllerCapturing)return;
        if(!snapshot.State.Connected||snapshot.Revision!=_captureRevision||snapshot.DeviceId!=_captureDevice||!ReferenceEquals(_captureRuntime,GamepadRuntimeConfig.Current))
        {CancelBinding();Status="Controller changed or disconnected. Choose the binding again.";return;}
        var newly=snapshot.State.Buttons&~_captureHeld;_captureHeld=snapshot.State.Buttons;
        foreach(var button in Enum.GetValues<GamepadButtons>())
            if(button!=0&&button!=_captureModifier&&(newly&button)!=0&&PadBindings.Single(button))
            {CaptureBinding(_captureModifier+"+"+button);return;}
    }
    internal bool ConfirmBindingSwap()=>ResolveBinding("Swap");
    internal bool ResolveBinding(string resolution)
    {
        if(_conflictTarget is not {} target||!BindingConflict)return false;
        if(resolution=="Cancel"){CancelBinding();return true;}
        if(resolution is not ("Swap" or "Replace" or "Keep Both"))return false;
        if(target.BindingAction!=null&&!ReferenceEquals(_runtime,GamepadRuntimeConfig.Current)){CancelBinding();Status="Controller changed. Discard Controls changes and choose the binding again.";return false;}
        if(target.BindingAction is {} action)
        {
            var parts=_conflictValue.Split('+');var draft=ControllerDraft();
            draft.Bindings.Assign(action,target.BindingSlot,Enum.Parse<GamepadButtons>(parts[1],true),resolution,Enum.Parse<GamepadButtons>(parts[0],true));
            ProjectControllerDraft(draft);
        }
        else
        {
            string old=target.Value;
            if(resolution is "Swap" or "Replace")foreach(var row in _conflictRows)Stage(row,resolution=="Swap"?old:"Unknown");
            Stage(target,_conflictValue);
        }
        CancelBinding();Status="Binding resolution staged. Apply Changes to save, or Discard to restore.";return true;
    }
    private static bool ControllerOwned(NativeSettingRow row)=>row.BindingAction!=null||row.Category=="Controls"&&(row.Group is "Controller" or "Controller bindings" or "Controller calibration" or "Weapon wheel");
    private void RestoreCategoryBindings(bool replay)
    {
        var draft=ControllerDraft();foreach(var action in draft.Bindings.Actions.Where(a=>draft.Bindings.ReplayActions.Contains(a)==replay))draft.Bindings.CopyActionFrom(_savedBindings,action);
        if(!replay)draft.Bindings.Preset=_savedBindings.Preset;ProjectControllerDraft(draft);
    }
    internal void ResetReplayControls()
    {
        var keys=new[]{"Space","Comma","Period","Left","Right","LeftBracket","RightBracket","Home","K","J","I","G"};
        var names=new[]{"Replay Play Pause Key","Replay Step Back Key","Replay Step Forward Key","Replay Seek Back Key","Replay Seek Forward Key","Replay Slower Key","Replay Faster Key","Replay Restart Key","Replay Camera Track Key","Replay Constant Speed Key","Replay Interpolation Key","Replay Easing Key"};
        for(int i=0;i<names.Length;i++){var row=Rows.First(r=>r.Category=="Replays"&&r.Label==names[i]);Stage(row,keys[i]);}
        var draft=ControllerDraft();foreach(var action in draft.Bindings.ReplayActions)draft.Bindings.Set(action,draft.Bindings.Default(action));ProjectControllerDraft(draft);
        Status="Default replay controls staged. Apply Changes to save, or Discard to restore.";
    }
    private GamepadRuntimeConfig ControllerDraft()
    {
        var current=GamepadRuntimeConfig.Current;var draft=current.Clone();
        if(_bindingsDraft!=null)draft.Bindings.CopyFrom(_bindingsDraft);
        try
        {
            GamepadRuntimeConfig.Current=draft;
            foreach(var row in Rows.Where(ControllerOwned))if(row.Label!="Control layout")row.Write(row.Value);
            draft.Bindings.Preset=Rows.First(r=>r.Label=="Control layout").Value;
        }
        finally{GamepadRuntimeConfig.Current=current;}
        return draft;
    }
    private void ProjectControllerDraft(GamepadRuntimeConfig draft)
    {
        _bindingsDraft=draft.Bindings.Clone();UpdateLegacyHints(_bindingsDraft);
        var current=GamepadRuntimeConfig.Current;
        try{GamepadRuntimeConfig.Current=draft;foreach(var row in Rows.Where(ControllerOwned)){string value=row.Read();row.Value=row.Choices.FirstOrDefault(c=>c.Equals(value,StringComparison.OrdinalIgnoreCase))??value;}}
        finally{GamepadRuntimeConfig.Current=current;}
    }
    private void StageControllerLayout(string name)
    {
        var draft=ControllerDraft();draft.Layout.Apply(name);ProjectControllerDraft(draft);
        Status="Controller layout staged. Apply Changes to save, or Discard to restore.";
    }
    internal bool CycleControllerDevice()
    {
        if(Busy||ConfirmingVideo||Capturing||Confirmation.Length>0)return false;
        if(HiddenBindingsDirty||Rows.Any(r=>r.Category=="Controls"&&r.Dirty)){Status="Apply or discard Controls changes before selecting another controller.";return false;}
        var devices=GamepadManager.Devices;int current=Array.FindIndex(devices.ToArray(),d=>d.DeviceId==GamepadManager.SelectedDeviceId)+1;
        int next=(current+1)%(devices.Count+1);GamepadManager.SelectDevice(next==0?null:devices[next-1].DeviceId);
        _calibration=null;RebaseControls();Status="Editing controller: "+CurrentDeviceLabel;return true;
    }
    private static bool ValidTuple(string value,double[] min,double[] max)
    {
        var values=value.Split(',');if(values.Length!=min.Length)return false;
        for(int i=0;i<values.Length;i++)if(!double.TryParse(values[i],NumberStyles.Float,CultureInfo.InvariantCulture,out double n)||!double.IsFinite(n)||n<min[i]||n>max[i])return false;
        return true;
    }
    private void AddControllerCalibrationRows()
    {
        void Calibration(string label,Func<StickCalibration> read,Action<StickCalibration> write)
        {
            string Read(){var c=read();return FormattableString.Invariant($"{c.CenterX},{c.CenterY},{c.MinX},{c.MaxX},{c.MinY},{c.MaxY}");}
            Rows.Add(new("Controls","Controller calibration",label,Read,
                value=>{var n=value.Split(',').Select(v=>float.Parse(v,CultureInfo.InvariantCulture)).ToArray();write(new(n[0],n[1],n[2],n[3],n[4],n[5]));},
                value=>ValidTuple(value,new[]{-.3,-.3,-1d,.4,-1,.4},new[]{.3,.3,-.4,1d,-.4,1})?null:"Enter centerX,centerY,minX,maxX,minY,maxY."));
        }
        Calibration("Left stick axes",()=>GamepadOptions.LeftCalibration,c=>GamepadOptions.LeftCalibration=c);
        Calibration("Right stick axes",()=>GamepadOptions.RightCalibration,c=>GamepadOptions.RightCalibration=c);
        void Radial(string label,Func<StickRadialCalibration> read,Action<StickRadialCalibration> write)
        {
            Rows.Add(new("Controls","Controller calibration",label,()=>string.Join(",",Enumerable.Range(0,8).Select(i=>read().Radius(i).ToString(CultureInfo.InvariantCulture))),
                value=>{var n=value.Split(',').Select(v=>float.Parse(v,CultureInfo.InvariantCulture)).ToArray();write(new(n[0],n[1],n[2],n[3],n[4],n[5],n[6],n[7]));},
                value=>ValidTuple(value,Enumerable.Repeat(.65,8).ToArray(),Enumerable.Repeat(1.45,8).ToArray())?null:"Enter eight gate radii, each from 0.65 to 1.45."));
        }
        Radial("Left stick gate",()=>GamepadOptions.LeftRadial,c=>GamepadOptions.LeftRadial=c);Radial("Right stick gate",()=>GamepadOptions.RightRadial,c=>GamepadOptions.RightRadial=c);
        for(int i=0;i<6;i++)
        {int slot=i;Rows.Add(new("Controls","Weapon wheel","Position "+(i+1),()=>GamepadOptions.WheelOrder[slot].ToString(CultureInfo.InvariantCulture),v=>GamepadOptions.SetWheelSlot(slot,int.Parse(v,CultureInfo.InvariantCulture)),v=>Number(v,0,5,true),new[]{"0","1","2","3","4","5"}){ChoiceLabels=new(StringComparer.OrdinalIgnoreCase){{"0","Volt Driver"},{"1","Battlehammer"},{"2","Imperialist"},{"3","Judicator"},{"4","Magmaul"},{"5","Shock Coil"}}});}
    }
    internal void CalibrationAction(int action)
    {
        if(action==26){_calibration=new();_calibrationRest=true;Status="Release both sticks and triggers. Sample rest for at least one second, then choose Sample Range.";}
        if(action==27&&_calibration!=null){_calibrationRest=false;Status="Rotate both sticks around their gates and fully press both triggers, then Stage Calibration.";}
        if(action==28&&_calibration!=null)
        {
            if(!_calibration.Valid){Status=_calibration.Summary;return;}
            var runtime=GamepadRuntimeConfig.Current;var preview=runtime.Clone();
            try{GamepadRuntimeConfig.Current=preview;_calibration.Apply();foreach(var row in Rows.Where(r=>r.Group is "Controller" or "Controller calibration"))row.Value=row.Read();}
            finally{GamepadRuntimeConfig.Current=runtime;}
            _calibration=null;Status="Calibration staged. Apply Changes to save, or Discard to restore.";
        }
    }
}
#endif
