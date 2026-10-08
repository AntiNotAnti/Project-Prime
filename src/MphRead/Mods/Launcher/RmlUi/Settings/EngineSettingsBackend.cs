#if (MPHREAD_RMLUI_POC || MPHREAD_RMLUI) && !MPHREAD_SERVER
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using MphRead.Entities;
using MphRead.Mods.Input;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Render;
using MphRead.Mods.Render.Hud;
using MphRead.Mods.Settings;
using MphRead.Mods.Sound;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace MphRead.Mods.Launcher.RmlUi.Settings;

// Explicit application adapters; no native controls, arbitrary property writes,
// new stores, or duplicated gameplay/render services are introduced here.
internal sealed partial class EngineSettingsBackend : ISettingsBackend
{
    private sealed record Field(SettingsFieldDefinition Definition, Func<string> Read, Action<string> Write,
        string Store = "", string Key = "");
    private readonly MenuSettings _menu;
    private readonly SceneGameState _state;
    private readonly ScenePlayerRegistry? _players;
    private readonly bool _inGame;
    private readonly GamepadRuntimeConfig _pad;
    private readonly List<Field> _fields = new();
    private readonly Dictionary<string, string> _padValues = new(StringComparer.Ordinal);
    private readonly JsonObject _hudTemplate;
    private readonly Dictionary<string, string> _hudValues = new(StringComparer.Ordinal);
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    public IReadOnlyList<SettingsFieldDefinition> Definitions { get; }
    public bool RestartRequired => SettingsPersistence.RestartRequired;
    public string ApplyWarning { get; private set; } = "";

    internal EngineSettingsBackend(MenuSettings menu, SceneGameState state, bool inGame = false,
        ScenePlayerRegistry? players = null)
    {
        _menu = menu; _state = state; _inGame = inGame; _players = players;
        _pad = GamepadRuntimeConfig.Current;
        _hudTemplate = (JsonObject)JsonNode.Parse(HudProfileStore.Serialize(HudProfiles.CopyCurrent()))!;
        AddGeneral(); AddInput(); AddController(); AddHud();
        Definitions = Array.AsReadOnly(_fields.Select(field => field.Definition).ToArray());
    }
    public IReadOnlyDictionary<string, string> Capture()
    {
        RefreshController(); RefreshHud();
        var result = _fields.ToDictionary(field => field.Definition.Id, field => field.Read(), StringComparer.Ordinal);
        // Preserve the full profile, including null overrides and future profile data,
        // as a detached draft sidecar rather than a presentation text field.
        result["$hud"] = HudProfileStore.Serialize(HudProfiles.CopyCurrent());
        result["$hudEdited"] = "false";
        result["$legacyStyle"] = Crosshair.Style.ToString();
        result["$legacySize"] = Crosshair.Size.ToString();
        return result;
    }
    public void Apply(IReadOnlyDictionary<string, string> values, bool persist)
    {
        ApplyWarning="";
        using var lease = SettingsPersistence.BeginWrite()
            ?? throw new InvalidOperationException("Imported/reset preferences require a restart.");
        var before = Capture();
        Dictionary<string, byte[]?>? files = persist ? BackupStores() : null;
        try
        {
            ApplyRuntime(values);
            if (!persist) return;
            var frame = GamepadRuntimeConfig.Frame;
            try
            {
                GamepadRuntimeConfig.Frame = _pad;
                _state.CommitSettings(_menu);
                InputSettings.Save(); LauncherPrefs.Save();
                HudProfiles.Save(BuildHud(values));
                VerifySaved(values);
                PreserveUnknownPreferences(files!);
            }
            finally { GamepadRuntimeConfig.Frame = frame; }
        }
        catch (Exception failure)
        {
            var errors = new List<Exception> { failure };
            if (files != null) foreach (var file in files)
                try
                {
                    if (file.Value == null) { if (File.Exists(file.Key)) File.Delete(file.Key); }
                    else { Directory.CreateDirectory(Path.GetDirectoryName(file.Key)!); File.WriteAllBytes(file.Key, file.Value); }
                }
                catch (Exception rollback) { errors.Add(rollback); }
            try { ApplyRuntime(before); } catch (Exception rollback) { errors.Add(rollback); }
            if (errors.Count > 1)
            {
                var incomplete=new AggregateException("Settings save and rollback failed. Restart before saving again.",errors);
                SettingsPersistence.Replace(()=>throw incomplete);
            }
            throw;
        }
        void AfterSave(string name,Action action)
        {
            try{action();}
            catch(Exception ex){ApplyWarning+=(ApplyWarning.Length>0?" ":"")+name+": "+ex.Message;}
        }
        if (values.TryGetValue("menu.Renderer", out string? renderer) && before["menu.Renderer"] != renderer)
            AfterSave("Renderer restart guard",GraphicsBackendPolicy.ClearStartupGuardForRendererChange);
        if (before["prefs.DebugLogs"] != values["prefs.DebugLogs"])
        {
            AfterSave("Debug logging",()=>{if (LauncherPrefs.DebugLogs) DebugLog.Attach(); else DebugLog.Detach();});
        }
        AfterSave("Respawn selection",()=>RespawnChoice.Request(LauncherPrefs.LastHunter, LauncherPrefs.LastColor));
        AfterSave("Combat audio",()=>{CombatFeedbackAudio.Reload(); if (_inGame) CombatFeedbackAudio.Warm();});
        if (LauncherPrefs.ReplayAutoPrune && LauncherPrefs.ReplayStorageLimitGb > 0)
            AfterSave("Replay cleanup",()=>Replay.ReplayStorageManager.Apply(new Replay.ReplayStoragePolicy(
                MaxBytes:LauncherPrefs.ReplayStorageLimitGb*1024L*1024L*1024L, DeleteFullMatches:true,
                DeleteMaterializedClips:LauncherPrefs.ReplayDeleteClips,DeleteVirtualClips:false)));
    }
    private void ApplyRuntime(IReadOnlyDictionary<string, string> values)
    {
        // Validate the entire detached controller/profile before changing live objects.
        GamepadRuntimeConfig pad = BuildController(values);
        HudProfile hud = BuildHud(values);
        foreach (Field field in _fields)
            if (values.TryGetValue(field.Definition.Id, out string? value) && field.Read() != value)
                field.Write(field.Definition.Validate(value));
        var optionLines = new List<string>(); pad.Options.Write(optionLines);
        _pad.Options.Load(optionLines);
        var bindingLines = new List<string>(); pad.Bindings.Write(bindingLines);
        _pad.Bindings.Reset();
        foreach (string line in bindingLines)
        {
            int split = line.IndexOf('='); _pad.Bindings.TryLoad(line[..split], line[(split+1)..]);
        }
        _pad.Bindings.LoadSlots(bindingLines); _pad.Bindings.Preset = pad.Bindings.Preset;
        HudProfiles.Publish(hud); GameSettings.Apply(_menu);
        if (_inGame && _players != null) InputSettings.ApplyToPlayers(_players); else InputSettings.ApplyToPlayers();
    }
    private void Add(string id, SettingsCategory category, string label, Func<string> read, Action<string> write,
        Func<string,string> validate, SettingsValueKind kind = SettingsValueKind.Text,
        string[]? choices = null, string help = "", bool video = false, bool restart = false,
        string store = "", string key = "")
        => _fields.Add(new(new(id,category,label,kind,help,choices ?? Array.Empty<string>(),validate,video,restart),read,write,store,key));
    private void Flag(string id, SettingsCategory category, string label, Func<bool> read, Action<bool> write,
        string store = "", string key = "") => Add(id,category,label,()=>read()?"true":"false",v=>write(bool.Parse(v)),
            SettingsValueValidation.Boolean,SettingsValueKind.Boolean,new[]{"false","true"},store:store,key:key);
    private void Number(string id, SettingsCategory category, string label, Func<float> read, Action<float> write,
        double min, double max, string store = "", string key = "") => Add(id,category,label,()=>read().ToString(Invariant),
            v=>write(float.Parse(v,Invariant)),v=>SettingsValueValidation.Number(v,min,max),SettingsValueKind.Number,
            help:$"{min} to {max}",store:store,key:key);
    private void Choice(string id, SettingsCategory category, string label, Func<string> read, Action<string> write,
        string[] choices, bool video = false, bool restart = false, string store = "", string key = "")
        => Add(id,category,label,read,write,v=>SettingsValueValidation.Choice(v,choices),SettingsValueKind.Choice,
            choices,video:video,restart:restart,store:store,key:key);
    private void MenuNumber(string name, SettingsCategory category, string label, Func<string> read,
        Action<string> write, int min, int max, bool video=false) => Add("menu."+name,category,label,read,write,
            v=>SettingsValueValidation.Integer(v,min,max),SettingsValueKind.Number,help:$"{min} to {max}",video:video,store:"menu",key:name);
    private void MenuChoice(string name, SettingsCategory category, string label, Func<string> read,
        Action<string> write,string[] choices,bool restart=false) => Choice("menu."+name,category,label,read,write,
            choices,restart:restart,store:"menu",key:name);
    private static string[] Names<T>() where T:struct,Enum => Enum.GetNames<T>();
    private void AddGeneral()
    {
        MenuNumber(nameof(MenuSettings.ResolutionScale),SettingsCategory.Graphics,"Render scale",()=>_menu.ResolutionScale,v=>_menu.ResolutionScale=v,25,800,video:true);
        MenuNumber(nameof(MenuSettings.FieldOfView),SettingsCategory.Display,"Field of view",()=>_menu.FieldOfView,v=>_menu.FieldOfView=v,60,120,video:true);
        MenuNumber(nameof(MenuSettings.CelBands),SettingsCategory.Graphics,"Shading bands",()=>_menu.CelBands,v=>_menu.CelBands=v,2,8,video:false);
        MenuNumber(nameof(MenuSettings.CelEdge),SettingsCategory.Graphics,"Cel outline strength",()=>_menu.CelEdge,v=>_menu.CelEdge=v,0,100,video:false);
        MenuNumber(nameof(MenuSettings.SharpenStrength),SettingsCategory.Graphics,"Image sharpening",()=>_menu.SharpenStrength,v=>_menu.SharpenStrength=v,0,100,video:false);
        MenuNumber(nameof(MenuSettings.BloomIntensity),SettingsCategory.Graphics,"Bloom intensity",()=>_menu.BloomIntensity,v=>_menu.BloomIntensity=v,0,150,video:false);
        MenuNumber(nameof(MenuSettings.Gamma),SettingsCategory.Graphics,"Gamma",()=>_menu.Gamma,v=>_menu.Gamma=v,50,150,video:false);
        MenuNumber(nameof(MenuSettings.Contrast),SettingsCategory.Graphics,"Contrast",()=>_menu.Contrast,v=>_menu.Contrast=v,50,150,video:false);
        MenuNumber(nameof(MenuSettings.Saturation),SettingsCategory.Graphics,"Saturation",()=>_menu.Saturation,v=>_menu.Saturation=v,0,200,video:false);
        MenuChoice(nameof(MenuSettings.Lighting),SettingsCategory.Graphics,"Lighting",()=>_menu.Lighting,v=>_menu.Lighting=v,new[]{"off","on"});
        MenuChoice(nameof(MenuSettings.Fog),SettingsCategory.Graphics,"Fog",()=>_menu.Fog,v=>_menu.Fog=v,new[]{"off","on"});
        MenuChoice(nameof(MenuSettings.TextureFiltering),SettingsCategory.Graphics,"Texture filtering",()=>_menu.TextureFiltering,v=>_menu.TextureFiltering=v,new[]{"off","on"});
        MenuChoice(nameof(MenuSettings.TextureMipmaps),SettingsCategory.Graphics,"Texture mipmaps",()=>_menu.TextureMipmaps,v=>_menu.TextureMipmaps=v,new[]{"off","on"});
        MenuChoice(nameof(MenuSettings.ShowFps),SettingsCategory.Display,"Show fps",()=>_menu.ShowFps,v=>_menu.ShowFps=v,new[]{"off","on"});
        MenuChoice(nameof(MenuSettings.SmoothNativeHud),SettingsCategory.Display,"Smooth native hud",()=>_menu.SmoothNativeHud,v=>_menu.SmoothNativeHud=v,new[]{"off","on"});
        MenuChoice(nameof(MenuSettings.CelShading),SettingsCategory.Graphics,"Cel shading",()=>_menu.CelShading,v=>_menu.CelShading=v,new[]{"off","on"});
        MenuChoice(nameof(MenuSettings.Bloom),SettingsCategory.Graphics,"Bloom",()=>_menu.Bloom,v=>_menu.Bloom=v,new[]{"off","on"});
        MenuChoice(nameof(MenuSettings.EnhancedLighting),SettingsCategory.Graphics,"Enhanced lighting",()=>_menu.EnhancedLighting,v=>_menu.EnhancedLighting=v,new[]{"off","on"});
        MenuChoice(nameof(MenuSettings.AdvancedMaterials),SettingsCategory.Graphics,"Advanced materials",()=>_menu.AdvancedMaterials,v=>_menu.AdvancedMaterials=v,new[]{"off","on"});
        MenuChoice(nameof(MenuSettings.DeferredPbr),SettingsCategory.Graphics,"Deferred pbr",()=>_menu.DeferredPbr,v=>_menu.DeferredPbr=v,new[]{"off","on"});
        MenuChoice(nameof(MenuSettings.ContactShadows),SettingsCategory.Graphics,"Contact shadows",()=>_menu.ContactShadows,v=>_menu.ContactShadows=v,new[]{"off","on"});
        MenuChoice(nameof(MenuSettings.EnhancedFog),SettingsCategory.Graphics,"Enhanced fog",()=>_menu.EnhancedFog,v=>_menu.EnhancedFog=v,new[]{"off","on"});
        MenuChoice(nameof(MenuSettings.VolumetricFog),SettingsCategory.Graphics,"Volumetric fog",()=>_menu.VolumetricFog,v=>_menu.VolumetricFog=v,new[]{"off","on"});
        MenuChoice(nameof(MenuSettings.InternalHdr),SettingsCategory.Graphics,"Internal hdr",()=>_menu.InternalHdr,v=>_menu.InternalHdr=v,new[]{"off","on"});
        MenuChoice(nameof(MenuSettings.Reflections),SettingsCategory.Graphics,"Reflections",()=>_menu.Reflections,v=>_menu.Reflections=v,new[]{"off","on"});
        MenuChoice(nameof(MenuSettings.DynamicGlow),SettingsCategory.Graphics,"Dynamic glow",()=>_menu.DynamicGlow,v=>_menu.DynamicGlow=v,new[]{"off","on"});
        MenuChoice(nameof(MenuSettings.ShowCustomCosmetics),SettingsCategory.Graphics,"Show custom cosmetics",()=>_menu.ShowCustomCosmetics,v=>_menu.ShowCustomCosmetics=v,new[]{"off","on"});
        MenuChoice(nameof(MenuSettings.TextureReplacements),SettingsCategory.Graphics,"Texture replacements",()=>_menu.TextureReplacements,v=>_menu.TextureReplacements=v,new[]{"off","on"});
        MenuChoice(nameof(MenuSettings.CharacterModelReplacements),SettingsCategory.Graphics,"Character model replacements",()=>_menu.CharacterModelReplacements,v=>_menu.CharacterModelReplacements=v,new[]{"off","on"});
        MenuChoice(nameof(MenuSettings.GraphicsPreset),SettingsCategory.Graphics,"GraphicsPreset",()=>_menu.GraphicsPreset,v=>_menu.GraphicsPreset=v,Names<GraphicsPreset>().Select(v=>v.ToLowerInvariant()).ToArray());
        MenuChoice(nameof(MenuSettings.AntiAliasing),SettingsCategory.Graphics,"AntiAliasing",()=>_menu.AntiAliasing,v=>_menu.AntiAliasing=v,Names<AntiAliasingMode>().Select(v=>v.ToLowerInvariant()).ToArray());
        MenuChoice(nameof(MenuSettings.TextureSampling),SettingsCategory.Graphics,"TextureSampling",()=>_menu.TextureSampling,v=>_menu.TextureSampling=v,Names<TextureSamplingMode>().Select(v=>v.ToLowerInvariant()).ToArray());
        MenuChoice(nameof(MenuSettings.TextureUpscale),SettingsCategory.Graphics,"TextureUpscale",()=>_menu.TextureUpscale,v=>_menu.TextureUpscale=v,Names<TextureUpscaleMode>().Select(v=>v.ToLowerInvariant()).ToArray());
        MenuChoice(nameof(MenuSettings.TextureQuality),SettingsCategory.Graphics,"TextureQuality",()=>_menu.TextureQuality,v=>_menu.TextureQuality=v,Names<TextureAssetQuality>().Select(v=>v.ToLowerInvariant()).ToArray());
        MenuChoice(nameof(MenuSettings.ShadowQuality),SettingsCategory.Graphics,"ShadowQuality",()=>_menu.ShadowQuality,v=>_menu.ShadowQuality=v,Names<ShadowQuality>().Select(v=>v.ToLowerInvariant()).ToArray());
        MenuChoice(nameof(MenuSettings.AmbientOcclusion),SettingsCategory.Graphics,"AmbientOcclusion",()=>_menu.AmbientOcclusion,v=>_menu.AmbientOcclusion=v,Names<AmbientOcclusionQuality>().Select(v=>v.ToLowerInvariant()).ToArray());
        MenuChoice(nameof(MenuSettings.ColorGrade),SettingsCategory.Graphics,"ColorGrade",()=>_menu.ColorGrade,v=>_menu.ColorGrade=v,Names<ColorGradeProfile>().Select(v=>v.ToLowerInvariant()).ToArray());
        MenuChoice(nameof(MenuSettings.CosmeticQuality),SettingsCategory.Graphics,"CosmeticQuality",()=>_menu.CosmeticQuality,v=>_menu.CosmeticQuality=v,Names<Cosmetics.CosmeticEffectQuality>().Select(v=>v.ToLowerInvariant()).ToArray());
        MenuChoice(nameof(MenuSettings.Renderer),SettingsCategory.Graphics,"Renderer",()=>_menu.Renderer,v=>_menu.Renderer=v,GraphicsBackendPolicy.RendererChoices().Select(v=>v.ToString()).ToArray(),restart:true);
        MenuChoice(nameof(MenuSettings.TextureAnisotropy),SettingsCategory.Graphics,"Anisotropic filtering",()=>_menu.TextureAnisotropy,v=>_menu.TextureAnisotropy=v,new[]{"1","2","4","8","16"});
        MenuChoice(nameof(MenuSettings.FrameRateCap),SettingsCategory.Display,"FPS limit",()=>_menu.FrameRateCap,v=>_menu.FrameRateCap=v,new[]{"display","unlimited","30","60","75","90","100","120","144","165","180","200","240"});
        MenuChoice(nameof(MenuSettings.Language),SettingsCategory.Audio,"Text language",()=>_menu.Language,v=>_menu.Language=v,Names<Language>());
        Add("menu.SfxVolume",SettingsCategory.Audio,"SfxVolume",()=>_menu.SfxVolume,v=>_menu.SfxVolume=v,v=>SettingsValueValidation.Number(v,0,1),SettingsValueKind.Number,help:"0 to 1",store:"menu",key:"SfxVolume");
        Add("menu.PlayerVolume",SettingsCategory.Audio,"PlayerVolume",()=>_menu.PlayerVolume,v=>_menu.PlayerVolume=v,v=>SettingsValueValidation.Number(v,0,1),SettingsValueKind.Number,help:"0 to 1",store:"menu",key:"PlayerVolume");
        Add("menu.WeaponVolume",SettingsCategory.Audio,"WeaponVolume",()=>_menu.WeaponVolume,v=>_menu.WeaponVolume=v,v=>SettingsValueValidation.Number(v,0,1),SettingsValueKind.Number,help:"0 to 1",store:"menu",key:"WeaponVolume");
        Add("menu.NotificationVolume",SettingsCategory.Audio,"NotificationVolume",()=>_menu.NotificationVolume,v=>_menu.NotificationVolume=v,v=>SettingsValueValidation.Number(v,0,1),SettingsValueKind.Number,help:"0 to 1",store:"menu",key:"NotificationVolume");
        Add("menu.EffectsVolume",SettingsCategory.Audio,"EffectsVolume",()=>_menu.EffectsVolume,v=>_menu.EffectsVolume=v,v=>SettingsValueValidation.Number(v,0,1),SettingsValueKind.Number,help:"0 to 1",store:"menu",key:"EffectsVolume");
        Add("menu.MusicVolume",SettingsCategory.Audio,"MusicVolume",()=>_menu.MusicVolume,v=>_menu.MusicVolume=v,v=>SettingsValueValidation.Number(v,0,1),SettingsValueKind.Number,help:"0 to 1",store:"menu",key:"MusicVolume");
        if(!OperatingSystem.IsAndroid())Choice("prefs.WindowMode",SettingsCategory.Display,"Window mode",()=>LauncherPrefs.WindowMode.ToString(),v=>{LauncherPrefs.WindowMode=Enum.Parse<WindowStartMode>(v);WindowMode.Startup=LauncherPrefs.WindowMode;PauseMenu.RequestWindowMode(LauncherPrefs.WindowMode);},Names<WindowStartMode>(),video:true,store:"launcher",key:"window_mode");
        Flag("prefs.ReduceMotion",SettingsCategory.Display,"ReduceMotion",()=>LauncherPrefs.ReduceMotion,v=>LauncherPrefs.ReduceMotion=v,"launcher","reduce_motion");
        Flag("prefs.HighContrast",SettingsCategory.Display,"High contrast",()=>LauncherPrefs.HighContrast,v=>LauncherPrefs.HighContrast=v,"launcher","high_contrast");
        Flag("prefs.LargeText",SettingsCategory.Display,"Large text",()=>LauncherPrefs.LargeText,v=>LauncherPrefs.LargeText=v,"launcher","large_text");
        Flag("prefs.TouchTargets",SettingsCategory.Display,"Large touch targets",()=>LauncherPrefs.TouchTargets,v=>LauncherPrefs.TouchTargets=v,"launcher","touch_targets");
        Flag("prefs.AutoUpdate",SettingsCategory.Profile,"AutoUpdate",()=>LauncherPrefs.AutoUpdate,v=>LauncherPrefs.AutoUpdate=v,"launcher","auto_update");
        Flag("prefs.DebugLogs",SettingsCategory.Profile,"DebugLogs",()=>LauncherPrefs.DebugLogs,v=>LauncherPrefs.DebugLogs=v,"launcher","debug_logs");
        Flag("prefs.CombatNotificationsVisible",SettingsCategory.Audio,"CombatNotificationsVisible",()=>LauncherPrefs.CombatNotificationsVisible,v=>LauncherPrefs.CombatNotificationsVisible=v,"launcher","combat_notifications_visible");
        Flag("prefs.ReplayAutoPrune",SettingsCategory.Replays,"ReplayAutoPrune",()=>LauncherPrefs.ReplayAutoPrune,v=>LauncherPrefs.ReplayAutoPrune=v,"launcher","replay_auto_prune");
        Flag("prefs.ReplayDeleteClips",SettingsCategory.Replays,"ReplayDeleteClips",()=>LauncherPrefs.ReplayDeleteClips,v=>LauncherPrefs.ReplayDeleteClips=v,"launcher","replay_delete_clips");
        Flag("prefs.SpectatorNameTags",SettingsCategory.Replays,"SpectatorNameTags",()=>LauncherPrefs.SpectatorNameTags,v=>LauncherPrefs.SpectatorNameTags=v,"launcher","spectator_name_tags");
        Flag("prefs.KillCamEnabled",SettingsCategory.Replays,"KillCamEnabled",()=>LauncherPrefs.KillCamEnabled,v=>LauncherPrefs.KillCamEnabled=v,"launcher","kill_cam");
        Flag("prefs.FinalKillCamEnabled",SettingsCategory.Replays,"FinalKillCamEnabled",()=>LauncherPrefs.FinalKillCamEnabled,v=>LauncherPrefs.FinalKillCamEnabled=v,"launcher","final_kill_cam");
        Add("prefs.PlayerName",SettingsCategory.Profile,"Player name",()=>LauncherPrefs.PlayerName,v=>LauncherPrefs.PlayerName=v,v=>Network.PlayerNameCodec.ValidationError(v) is string error ? throw new FormatException(error):v.Trim(),store:"launcher",key:"player_name");
        Choice("prefs.LastHunter",SettingsCategory.Profile,"Default Hunter",()=>LauncherPrefs.LastHunter.ToString(),v=>LauncherPrefs.LastHunter=Enum.Parse<Hunter>(v),Names<Hunter>(),store:"launcher",key:"hunter");
        Add("prefs.LastColor",SettingsCategory.Profile,"Suit color",()=>LauncherPrefs.LastColor.ToString(Invariant),v=>LauncherPrefs.LastColor=int.Parse(v,Invariant),v=>SettingsValueValidation.Integer(v,0,3),SettingsValueKind.Number,store:"launcher",key:"color");
        Choice("prefs.ReplayStorageLimitGb",SettingsCategory.Replays,"Replay storage limit GB",()=>LauncherPrefs.ReplayStorageLimitGb.ToString(Invariant),v=>LauncherPrefs.ReplayStorageLimitGb=int.Parse(v,Invariant),new[]{"0","5","10","25","50"},store:"launcher",key:"replay_storage_gb");
        Add("prefs.KillCamCamera",SettingsCategory.Replays,"Kill cam camera: 0 chase, 1 first person, 2 cinematic",()=>LauncherPrefs.KillCamCamera.ToString(Invariant),v=>LauncherPrefs.KillCamCamera=int.Parse(v,Invariant),v=>SettingsValueValidation.Integer(v,0,2),SettingsValueKind.Number,store:"launcher",key:"kill_cam_camera");
        Add("prefs.ServerAddress",SettingsCategory.Profile,"ServerAddress",()=>LauncherPrefs.ServerAddress,v=>LauncherPrefs.ServerAddress=v,v=>!string.IsNullOrWhiteSpace(v)?SettingsValueValidation.Text(v.Trim()):throw new FormatException("Enter a host address."),store:"launcher",key:"server_address");
        Add("prefs.MasterHost",SettingsCategory.Profile,"MasterHost",()=>LauncherPrefs.MasterHost,v=>LauncherPrefs.MasterHost=v,v=>!string.IsNullOrWhiteSpace(v)?SettingsValueValidation.Text(v.Trim()):throw new FormatException("Enter a host address."),store:"launcher",key:"master_host");
        Add("prefs.ServerPort",SettingsCategory.Profile,"ServerPort",()=>LauncherPrefs.ServerPort.ToString(Invariant),v=>LauncherPrefs.ServerPort=int.Parse(v,Invariant),v=>SettingsValueValidation.Integer(v,1,65535),SettingsValueKind.Number,store:"launcher",key:"server_port");
        Add("prefs.MasterPort",SettingsCategory.Profile,"MasterPort",()=>LauncherPrefs.MasterPort.ToString(Invariant),v=>LauncherPrefs.MasterPort=int.Parse(v,Invariant),v=>SettingsValueValidation.Integer(v,1,65535),SettingsValueKind.Number,store:"launcher",key:"master_port");
        Number("prefs.CombatFeedbackVolume",SettingsCategory.Audio,"Combat notification volume",()=>LauncherPrefs.CombatFeedbackVolume,v=>LauncherPrefs.CombatFeedbackVolume=v,0,1,"launcher","combat_feedback_volume");
        foreach (CombatFeedbackCue cue in Enum.GetValues<CombatFeedbackCue>())
        {
            var captured = cue; var options=CombatFeedbackAudio.GetOptions(cue).Select(option=>option.Id).ToArray();
            string key=System.Text.RegularExpressions.Regex.Replace(cue.ToString(),"([a-z])([A-Z])","$1_$2").ToLowerInvariant()+"_sound";
            Choice("sound."+cue,SettingsCategory.Audio,cue+" sound",()=>CombatFeedbackAudio.GetSelection(captured),v=>CombatFeedbackAudio.SetSelection(captured,v),options,store:"launcher",key:key);
        }
        Flag("render.BrightSkins",SettingsCategory.Display,"Player highlight",()=>RenderOptions.BrightSkins,v=>RenderOptions.BrightSkins=v,"launcher","bright_skins");
        Choice("render.BrightSkinStyle",SettingsCategory.Display,"Player highlight style",()=>RenderOptions.BrightSkinStyle.ToString(),v=>RenderOptions.BrightSkinStyle=Enum.Parse<PlayerSkinStyle>(v),Names<PlayerSkinStyle>(),store:"launcher",key:"bright_skin_style");
        Choice("render.PlayerOutline",SettingsCategory.Display,"Player outline",()=>RenderOptions.PlayerOutline.ToString(),v=>RenderOptions.PlayerOutline=Enum.Parse<PlayerOutlineStyle>(v),Names<PlayerOutlineStyle>(),store:"launcher",key:"player_outline");
        Add("render.PlayerOutlineWidth",SettingsCategory.Display,"Outline thickness",()=>RenderOptions.PlayerOutlineWidth.ToString(Invariant),v=>RenderOptions.PlayerOutlineWidth=int.Parse(v,Invariant),v=>SettingsValueValidation.Integer(v,1,8),SettingsValueKind.Number,store:"launcher",key:"player_outline_width");
    }
}
#endif
