#if (MPHREAD_RMLUI_POC || MPHREAD_RMLUI) && !MPHREAD_SERVER
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MphRead.Entities;
using MphRead.Mods.Input;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Settings;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace MphRead.Mods.Launcher.RmlUi.Settings;
internal sealed partial class EngineSettingsBackend
{
    private void AddInput()
    {
        Number("input.MouseSensitivity",SettingsCategory.Controls,"Mouse sensitivity",()=>InputSettings.MouseSensitivity,v=>InputSettings.MouseSensitivity=v,0.01,3,"controls","sensitivity");
        Number("input.ImperialistZoomSensitivity",SettingsCategory.Controls,"Imperialist zoom sensitivity",()=>InputSettings.ImperialistZoomSensitivity,v=>InputSettings.ImperialistZoomSensitivity=v,0.1,3,"controls","imperialist_zoom_sensitivity");
        Number("input.ImperialistZoomAmount",SettingsCategory.Controls,"Imperialist zoom amount",()=>InputSettings.ImperialistZoomAmount,v=>InputSettings.ImperialistZoomAmount=v,0.25,2,"controls","imperialist_zoom_amount");
        Number("input.AltSwipeSensitivity",SettingsCategory.Controls,"Alt swipe sensitivity",()=>InputSettings.AltSwipeSensitivity,v=>InputSettings.AltSwipeSensitivity=v,0.25,4,"controls","alt_swipe_sensitivity");
        Flag("input.InvertMouseX",SettingsCategory.Controls,"InvertMouseX",()=>InputSettings.InvertMouseX,v=>InputSettings.InvertMouseX=v,"controls","invert_x");
        Flag("input.InvertMouseY",SettingsCategory.Controls,"InvertMouseY",()=>InputSettings.InvertMouseY,v=>InputSettings.InvertMouseY=v,"controls","invert_y");
        Flag("input.MouseMovementBoost",SettingsCategory.Controls,"MouseMovementBoost",()=>InputSettings.MouseMovementBoost,v=>InputSettings.MouseMovementBoost=v,"controls","mouse_movement_boost");
        Flag("input.MouseAltFormMovement",SettingsCategory.Controls,"MouseAltFormMovement",()=>InputSettings.MouseAltFormMovement,v=>InputSettings.MouseAltFormMovement=v,"controls","mouse_alt_form_movement");
        Flag("input.StylusMovementBoost",SettingsCategory.Controls,"StylusMovementBoost",()=>InputSettings.StylusMovementBoost,v=>InputSettings.StylusMovementBoost=v,"controls","stylus_movement_boost");
        Flag("input.ScrollAllWeapons",SettingsCategory.Controls,"ScrollAllWeapons",()=>InputSettings.ScrollAllWeapons,v=>InputSettings.ScrollAllWeapons=v,"controls","scroll_all_weapons");
        Add("input.ChatKey",SettingsCategory.Controls,"ChatKey",()=>KeyName(InputSettings.ChatKey),v=>InputSettings.ChatKey=ParseKey(v),ValidateKey,store:"controls",key:"chat_key");
        Add("input.ClipKey",SettingsCategory.Replays,"ClipKey",()=>KeyName(InputSettings.ClipKey),v=>InputSettings.ClipKey=ParseKey(v),ValidateKey,store:"controls",key:"clip_key");
        Add("input.ReplayPlayPauseKey",SettingsCategory.Replays,"ReplayPlayPauseKey",()=>KeyName(InputSettings.ReplayPlayPauseKey),v=>InputSettings.ReplayPlayPauseKey=ParseKey(v),ValidateKey,store:"controls",key:"replay_play_pause");
        Add("input.ReplayStepBackKey",SettingsCategory.Replays,"ReplayStepBackKey",()=>KeyName(InputSettings.ReplayStepBackKey),v=>InputSettings.ReplayStepBackKey=ParseKey(v),ValidateKey,store:"controls",key:"replay_step_back");
        Add("input.ReplayStepForwardKey",SettingsCategory.Replays,"ReplayStepForwardKey",()=>KeyName(InputSettings.ReplayStepForwardKey),v=>InputSettings.ReplayStepForwardKey=ParseKey(v),ValidateKey,store:"controls",key:"replay_step_forward");
        Add("input.ReplaySeekBackKey",SettingsCategory.Replays,"ReplaySeekBackKey",()=>KeyName(InputSettings.ReplaySeekBackKey),v=>InputSettings.ReplaySeekBackKey=ParseKey(v),ValidateKey,store:"controls",key:"replay_seek_back");
        Add("input.ReplaySeekForwardKey",SettingsCategory.Replays,"ReplaySeekForwardKey",()=>KeyName(InputSettings.ReplaySeekForwardKey),v=>InputSettings.ReplaySeekForwardKey=ParseKey(v),ValidateKey,store:"controls",key:"replay_seek_forward");
        Add("input.ReplaySlowerKey",SettingsCategory.Replays,"ReplaySlowerKey",()=>KeyName(InputSettings.ReplaySlowerKey),v=>InputSettings.ReplaySlowerKey=ParseKey(v),ValidateKey,store:"controls",key:"replay_slower");
        Add("input.ReplayFasterKey",SettingsCategory.Replays,"ReplayFasterKey",()=>KeyName(InputSettings.ReplayFasterKey),v=>InputSettings.ReplayFasterKey=ParseKey(v),ValidateKey,store:"controls",key:"replay_faster");
        Add("input.ReplayRestartKey",SettingsCategory.Replays,"ReplayRestartKey",()=>KeyName(InputSettings.ReplayRestartKey),v=>InputSettings.ReplayRestartKey=ParseKey(v),ValidateKey,store:"controls",key:"replay_restart");
        Add("input.ReplayCameraTrackKey",SettingsCategory.Replays,"ReplayCameraTrackKey",()=>KeyName(InputSettings.ReplayCameraTrackKey),v=>InputSettings.ReplayCameraTrackKey=ParseKey(v),ValidateKey,store:"controls",key:"replay_camera_track");
        Add("input.ReplayConstantSpeedKey",SettingsCategory.Replays,"ReplayConstantSpeedKey",()=>KeyName(InputSettings.ReplayConstantSpeedKey),v=>InputSettings.ReplayConstantSpeedKey=ParseKey(v),ValidateKey,store:"controls",key:"replay_constant_speed");
        Add("input.ReplayInterpolationKey",SettingsCategory.Replays,"ReplayInterpolationKey",()=>KeyName(InputSettings.ReplayInterpolationKey),v=>InputSettings.ReplayInterpolationKey=ParseKey(v),ValidateKey,store:"controls",key:"replay_interpolation");
        Add("input.ReplayEasingKey",SettingsCategory.Replays,"ReplayEasingKey",()=>KeyName(InputSettings.ReplayEasingKey),v=>InputSettings.ReplayEasingKey=ParseKey(v),ValidateKey,store:"controls",key:"replay_easing");
        foreach (var property in InputSettings.Bindings)
        {
            var captured=property;
            Add("binding."+property.Name,SettingsCategory.Controls,InputSettings.ActionName(property),
                ()=>ExactBinding(InputSettings.Bind(captured)),v=>RestoreBinding(InputSettings.Bind(captured),v),ValidateBinding,
                help:"Type:key:mouse. Types Key, Mouse, ScrollUp, ScrollDown; retained unused fields are part of the draft.",store:"binding",key:property.Name);
        }
        Flag("pointer.StylusMode",SettingsCategory.Controls,"Stylus mode",()=>PointerInput.StylusMode,v=>PointerInput.StylusMode=v,"controls","stylus_mode");
        Flag("pointer.GuardJumps",SettingsCategory.Controls,"Reposition filtering",()=>PointerInput.GuardJumps,v=>PointerInput.GuardJumps=v,"controls","pointer_jump_guard");
        Flag("stylus.Wanted",SettingsCategory.Controls,"DS touch screen zone",()=>StylusZone.Wanted,v=>StylusZone.Enabled=v,"controls","stylus_zone");
        Flag("stylus.NativeUi",SettingsCategory.Controls,"Native stylus UI",()=>StylusZone.NativeUi,v=>StylusZone.NativeUi=v,"controls","stylus_native_ui");
        Number("stylus.NativeUiOpacity",SettingsCategory.Controls,"Stylus NativeUiOpacity",()=>StylusZone.NativeUiOpacity,v=>StylusZone.NativeUiOpacity=v,0,1,"controls","stylus_native_ui_opacity");
        Number("stylus.CursorOpacity",SettingsCategory.Controls,"Stylus CursorOpacity",()=>StylusZone.CursorOpacity,v=>StylusZone.CursorOpacity=v,0,1,"controls","stylus_cursor_opacity");
        Number("stylus.OutlineOpacity",SettingsCategory.Controls,"Stylus OutlineOpacity",()=>StylusZone.OutlineOpacity,v=>StylusZone.OutlineOpacity=v,0,1,"controls","stylus_zone_outline_opacity");
        Number("stylus.ButtonOpacity",SettingsCategory.Controls,"Stylus ButtonOpacity",()=>StylusZone.ButtonOpacity,v=>StylusZone.ButtonOpacity=v,0,1,"controls","stylus_zone_button_opacity");
        Add("stylus.GuideColor",SettingsCategory.Controls,"Stylus guide color",()=>StylusZone.GuideColor,v=>StylusZone.GuideColor=v,
            v=>{if(v.Length!=7||v[0]!='#'||!uint.TryParse(v[1..],NumberStyles.HexNumber,Invariant,out _))throw new FormatException("Use #RRGGBB.");return v.ToUpperInvariant();},store:"controls",key:"stylus_zone_color");
        Add("stylus.Rectangle",SettingsCategory.Controls,"Stylus zone: left,top,width",()=>string.Join(",",new[]{StylusZone.Left,StylusZone.Top,StylusZone.Width}.Select(v=>v.ToString(Invariant))),
            v=>{var n=Vector(v,0,1);StylusZone.SetRect(n[0],n[1],n[2]);},
            v=>{var n=Vector(v,0,1);if(n[2]<.1f||n[0]+n[2]>1||n[1]+n[2]*.75f*StylusZone.AspectCorrection>1)throw new FormatException("The zone must fit within the screen and be at least 0.1 wide.");return string.Join(",",n.Select(v=>v.ToString(Invariant)));},
            store:"controls",key:"stylus_zone_rect");
        Number("touch.ButtonScale",SettingsCategory.Controls,"Touch button size",()=>TouchSettings.ButtonScale,v=>TouchSettings.ButtonScale=v,TouchSettings.MinButtonScale,TouchSettings.MaxButtonScale,"controls",TouchSettings.ButtonScaleSettingKey);
        Number("touch.StickScale",SettingsCategory.Controls,"Touch movement stick size",()=>TouchSettings.StickScale,v=>TouchSettings.StickScale=v,TouchSettings.MinStickScale,TouchSettings.MaxStickScale,"controls",TouchSettings.StickScaleSettingKey);
        Number("touch.OverlayOpacity",SettingsCategory.Controls,"Touch overlay opacity",()=>TouchSettings.OverlayOpacity,v=>TouchSettings.OverlayOpacity=v,0,1,"controls",TouchSettings.OverlayOpacitySettingKey);
        Flag("touch.ButtonsVisible",SettingsCategory.Controls,"On screen touch buttons",()=>TouchSettings.ButtonsVisible,v=>TouchSettings.ButtonsVisible=v,"controls",TouchSettings.ButtonsSettingKey);
        foreach ((TouchControl control,string label) in TouchSettings.Order)
        {
            var captured=control;
            Flag("touch.Enabled."+control,SettingsCategory.Controls,label+" touch button",()=>TouchSettings.IsEnabled(captured),v=>TouchSettings.SetEnabled(captured,v),"controls",TouchSettings.SettingKey(control));
            Add("touch.Layout."+control,SettingsCategory.Controls,label+" position: x,y,scale",()=>TouchSettings.CopyLayout().TryGetValue(captured,out var layout)?$"{layout.X.ToString(Invariant)},{layout.Y.ToString(Invariant)},{layout.Scale.ToString(Invariant)}":"default",
                v=>TouchSettings.ReadSetting(TouchSettings.LayoutKey(captured),v),
                v=>{if(v.Equals("default",StringComparison.OrdinalIgnoreCase))return "default";var n=Vector(v,0,2);if(n[0]>1||n[1]>1||n[2]<TouchSettings.MinIndividualScale||n[2]>TouchSettings.MaxIndividualScale)throw new FormatException("X and Y from 0 to 1; scale from 0.4 to 2.");return string.Join(",",n.Select(v=>v.ToString(Invariant)));},store:"controls",key:TouchSettings.LayoutKey(control));
        }
        Choice("clip.Seconds",SettingsCategory.Replays,"Clip length seconds",()=>Network.DemoClip.Seconds.ToString(Invariant),v=>Network.DemoClip.Seconds=int.Parse(v,Invariant),Network.DemoClip.Lengths.Select(v=>v.ToString(Invariant)).ToArray(),store:"controls",key:"clip_seconds");
        Choice("clip.PostRoll",SettingsCategory.Replays,"Clip post roll seconds",()=>Network.DemoClip.PostRollSeconds.ToString(Invariant),v=>Network.DemoClip.PostRollSeconds=int.Parse(v,Invariant),Network.DemoClip.PostRollLengths.Select(v=>v.ToString(Invariant)).ToArray(),store:"controls",key:"clip_postroll");
    }
    private static string KeyName(Keys key)=>key==Keys.Unknown?"none":key.ToString();
    private static Keys ParseKey(string value)=>value.Equals("none",StringComparison.OrdinalIgnoreCase)?Keys.Unknown:Enum.Parse<Keys>(value,true);
    private static string ValidateKey(string value)
    {
        if(value.Equals("none",StringComparison.OrdinalIgnoreCase))return "none";
        if(!Enum.TryParse<Keys>(value,true,out var key)||!Enum.IsDefined(key))throw new FormatException("Enter a keyboard key name or none.");
        return KeyName(key);
    }
    private static string ExactBinding(Keybind bind)=>$"{bind.Type}:{bind.Key}:{bind.MouseButton}";
    private static string ValidateBinding(string value)
    {
        var parts=value.Split(':');
        if(parts.Length!=3||!Enum.TryParse<ButtonType>(parts[0],out var type)||!Enum.IsDefined(type)
            ||!Enum.TryParse<Keys>(parts[1],out var key)||!Enum.IsDefined(key)
            ||!Enum.TryParse<MouseButton>(parts[2],out var mouse)||!Enum.IsDefined(mouse))
            throw new FormatException("Use Type:key:mouse with valid names, for example Key:Space:Left.");
        return $"{type}:{key}:{mouse}";
    }
    private static void RestoreBinding(Keybind bind,string value)
    {
        var parts=ValidateBinding(value).Split(':');
        bind.Type=Enum.Parse<ButtonType>(parts[0]);bind.Key=Enum.Parse<Keys>(parts[1]);bind.MouseButton=Enum.Parse<MouseButton>(parts[2]);
    }
    private static float[] Vector(string value,double min,double max)
    {
        var parts=value.Split(',');if(parts.Length!=3)throw new FormatException("Enter three comma separated numbers.");
        return parts.Select(v=>float.Parse(SettingsValueValidation.Number(v,min,max),Invariant)).ToArray();
    }
}
#endif
