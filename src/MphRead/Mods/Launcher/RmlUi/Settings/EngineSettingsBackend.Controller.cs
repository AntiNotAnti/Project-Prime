#if (MPHREAD_RMLUI_POC || MPHREAD_RMLUI) && !MPHREAD_SERVER
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MphRead.Mods.Input;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Settings;

namespace MphRead.Mods.Launcher.RmlUi.Settings;
internal sealed partial class EngineSettingsBackend
{
    private void RefreshController()
    {
        var lines=new List<string>(); _pad.Options.Write(lines); _pad.Bindings.Write(lines);
        lines.Add("gamepad_preset="+_pad.Bindings.Preset);
        _padValues.Clear(); foreach(string line in lines){int split=line.IndexOf('=');_padValues[line[..split]]=line[(split+1)..];}
    }
    private void AddController()
    {
        RefreshController();
        foreach(var pair in _padValues)
        {
            string key=pair.Key;
            string[] choices=key switch {
                "gamepad_curve"=>Names<GamepadCurve>(),"gamepad_glyph_style"=>Names<GamepadFamily>(),
                "gamepad_preset"=>new[]{"Default","Bumper Jumper","Southpaw","Classic","Custom"},
                _=>Array.Empty<string>()};
            var kind=choices.Length>0?SettingsValueKind.Choice:bool.TryParse(pair.Value,out _)?SettingsValueKind.Boolean:
                float.TryParse(pair.Value,NumberStyles.Float,Invariant,out _)?SettingsValueKind.Number:SettingsValueKind.Text;
            Add("pad."+key,SettingsCategory.Controls,key.Replace("gamepad_","Controller ").Replace("pad_","Controller binding ").Replace('_',' '),
                ()=>_padValues[key],v=>_padValues[key]=v,v=>ValidateControllerValue(key,v),kind,choices,
                help:key.StartsWith("pad_",StringComparison.Ordinal)?"Button names; None to unbind. Both slots and modifiers are retained.":"Controller values are validated by the authoritative controller parser.",store:"controls",key:key);
        }
    }
    private static string ValidateControllerValue(string key,string value)
    {
        value=SettingsValueValidation.Text(value).Trim();
        if(!PreferenceText.IsValid(key,value,launcher:false))throw new FormatException("Invalid controller value.");
        if(bool.TryParse(value,out var flag))return flag?"True":"False";
        if(float.TryParse(value,NumberStyles.Float,Invariant,out _))
        {
            (double min,double max)=key switch {
                "gamepad_left_inner_deadzone" or "gamepad_right_inner_deadzone"=>(0,.9),
                "gamepad_left_outer_deadzone" or "gamepad_right_outer_deadzone"=>(0,.5),
                "gamepad_look_x" or "gamepad_look_y"=>(.1,5),
                "gamepad_scoped_x" or "gamepad_scoped_y"=>(.1,3),
                "gamepad_trigger_threshold"=>(.05,.95),"gamepad_activity_threshold"=>(.2,.95),
                "gamepad_wheel_threshold"=>(.1,.95),"gamepad_vibration_strength"=>(0,1),
                "gamepad_lt_min" or "gamepad_rt_min"=>(0,.8),"gamepad_lt_max" or "gamepad_rt_max"=>(.1,1),
                _ when key.Contains("_center_",StringComparison.Ordinal)=>(-.3,.3),
                _ when key.Contains("_min_",StringComparison.Ordinal)=>(-1,-.4),
                _ when key.Contains("_max_",StringComparison.Ordinal)=>(.4,1),
                _ when key.Contains("_radius_",StringComparison.Ordinal)=>(.65,1.45),
                _=>(0,65535)};
            return SettingsValueValidation.Number(value,min,max);
        }
        return value;
    }
    private GamepadRuntimeConfig BuildController(IReadOnlyDictionary<string,string> values)
    {
        var lines=_padValues.Keys.Select(key=>key+"="+ValidateControllerValue(key,values["pad."+key])).ToArray();
        var pad=new GamepadRuntimeConfig();pad.Options.Load(lines);
        foreach(string line in lines){int split=line.IndexOf('=');pad.Bindings.TryLoad(line[..split],line[(split+1)..]);}
        pad.Bindings.LoadSlots(lines); pad.Bindings.Preset=values["pad.gamepad_preset"];
        var normalized=new List<string>();pad.Options.Write(normalized);
        foreach(string line in normalized)
        {
            int split=line.IndexOf('=');string key=line[..split],actual=line[(split+1)..],wanted=values["pad."+key];
            if(float.TryParse(actual,NumberStyles.Float,Invariant,out float a)&&float.TryParse(wanted,NumberStyles.Float,Invariant,out float b)
                &&Math.Abs(a-b)>.00001f)throw new FormatException("Controller value is outside the allowed range: "+key);
        }
        var bindingValues=new List<string>();pad.Bindings.Write(bindingValues);
        foreach(string line in bindingValues)
        {
            int split=line.IndexOf('=');string key=line[..split];
            if(key.EndsWith("_modifier",StringComparison.Ordinal)&&line[(split+1)..]!=values["pad."+key])
                throw new FormatException("A controller modifier requires a distinct bound button: "+key);
        }
        return pad;
    }
    internal GamepadRuntimeConfig DraftController(IReadOnlyDictionary<string,string> values)=>BuildController(values);
    internal IReadOnlyDictionary<string,string> StageController(IReadOnlyDictionary<string,string> values,GamepadRuntimeConfig runtime)
    {
        var result=new Dictionary<string,string>(values,StringComparer.Ordinal);var lines=new List<string>();
        runtime.Options.Write(lines);runtime.Bindings.Write(lines);lines.Add("gamepad_preset="+runtime.Bindings.Preset);
        foreach(string line in lines){int split=line.IndexOf('=');result["pad."+line[..split]]=line[(split+1)..];}
        BuildController(result);return result;
    }
    internal IReadOnlyDictionary<string,string> StageControllerProfile(IReadOnlyDictionary<string,string> values,GamepadProfile profile)
    {
        var runtime=new GamepadRuntimeConfig();runtime.Options.Load(profile.Settings);
        foreach(string line in profile.Settings){int split=line.IndexOf('=');runtime.Bindings.TryLoad(line[..split],line[(split+1)..]);}
        runtime.Bindings.LoadSlots(profile.Settings);
        string? preset=profile.Settings.LastOrDefault(l=>l.StartsWith("gamepad_preset=",StringComparison.Ordinal));
        if(preset!=null)runtime.Bindings.Preset=preset[15..];
        return StageController(values,runtime);
    }
    public void ExpandDraft(string id,string value,IDictionary<string,string> draft)
    {
        if(id.StartsWith("hud/",StringComparison.Ordinal)||id.StartsWith("hudOverride.",StringComparison.Ordinal))
        {draft["$hudEdited"]="true";return;}
        if(id.StartsWith("pad.pad_",StringComparison.Ordinal))
        {
            string actionName=id[8..].Split('_')[0];
            if(Enum.TryParse<PadAction>(actionName,out var action)&&!(action>=PadAction.ReplayPlayPause&&action<=PadAction.ReplayCameraMode))
                draft["pad.gamepad_preset"]="Custom";
            return;
        }
        if(id=="pad.gamepad_preset")
        {
            if(value=="Custom")return;
            var runtime=new GamepadRuntimeConfig();
            var lines=_padValues.Keys.Select(key=>key+"="+draft["pad."+key]).ToArray();runtime.Options.Load(lines);
            runtime.Layout.Apply(value);var expanded=new List<string>();runtime.Options.Write(expanded);runtime.Bindings.Write(expanded);
            foreach(string line in expanded){int split=line.IndexOf('=');draft["pad."+line[..split]]=line[(split+1)..];}
            return;
        }
        if(id!="menu.GraphicsPreset")return;
        var recipe=GraphicsPresetProfile.Get(Enum.Parse<GraphicsPreset>(value,true));if(recipe==null)return;
        void Number(string name,int value)=>draft["menu."+name]=value.ToString(Invariant);
        void Flag(string name,bool value)=>draft["menu."+name]=value?"on":"off";
        void Choice(string name,object value)=>draft["menu."+name]=value.ToString()!.ToLowerInvariant();
        Number("ResolutionScale",recipe.ResolutionScale);Flag("Lighting",recipe.Lighting);Flag("Fog",recipe.Fog);
        Flag("TextureFiltering",recipe.TextureFiltering);Flag("TextureMipmaps",recipe.TextureMipmaps);Number("TextureAnisotropy",recipe.TextureAnisotropy);
        Choice("TextureUpscale",recipe.TextureUpscale);Choice("AntiAliasing",recipe.AntiAliasing);Number("SharpenStrength",recipe.SharpenStrength);
        Flag("Bloom",recipe.Bloom);Number("BloomIntensity",recipe.BloomIntensity);Choice("ColorGrade",recipe.ColorGrade);
        Number("Gamma",recipe.Gamma);Number("Contrast",recipe.Contrast);Number("Saturation",recipe.Saturation);
        Flag("EnhancedLighting",recipe.EnhancedLighting);Flag("AdvancedMaterials",recipe.AdvancedMaterials);Flag("DeferredPbr",recipe.DeferredPbr);
        Choice("ShadowQuality",recipe.Shadows);Choice("AmbientOcclusion",recipe.AmbientOcclusion);Flag("ContactShadows",recipe.ContactShadows);
        Flag("EnhancedFog",recipe.EnhancedFog);Flag("VolumetricFog",recipe.VolumetricFog);Flag("InternalHdr",recipe.InternalHdr);
        Flag("Reflections",recipe.Reflections);Flag("DynamicGlow",recipe.DynamicGlow);
        if(value.Equals("Original",StringComparison.OrdinalIgnoreCase)){draft["menu.TextureReplacements"]="off";draft["menu.CharacterModelReplacements"]="off";}
    }
}
#endif
