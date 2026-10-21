#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using MphRead.Mods.Launcher.Core;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using MphRead.Mods.Input;
using MphRead.Mods.Render;
using MphRead.Mods.Render.Hud;
using MphRead.Mods.Settings;
using OpenTK.Windowing.GraphicsLibraryFramework;
namespace MphRead.Mods.Launcher;

// The native view owns text drafts. Only a validated transactional commit
// publishes engine state, whether triggered by automatic saving or explicit Apply.
internal sealed class NativeSettingRow
{
    internal readonly string Category, Group, Label;
    internal readonly Func<string> Read;
    internal readonly Action<string> Write;
    internal readonly Func<string, string?> Validate;
    internal readonly string[] Choices;
    internal string Saved, Value;
    internal Dictionary<string,string>? ChoiceLabels;
    internal string DisplayValue=>ChoiceLabels?.GetValueOrDefault(Value)??(Choices.Length==0?Value:Value.ToLowerInvariant() switch{"true" or "on"=>"On","false" or "off"=>"Off",_=>Regex.Replace(Value.Replace('_',' '),"(?<=[a-z])([A-Z])"," $1")});
    internal string Description="Changes save automatically.";
    internal string? HintOverride;
    internal PadAction? BindingAction;
    internal int BindingSlot;
    internal bool KeyboardBinding;
    internal bool IsBinding=>KeyboardBinding||BindingAction!=null;
    internal double? Minimum, Maximum;
    internal bool WholeNumber, Advanced, HudOnly;
    internal bool HasSlider=>Minimum.HasValue&&Maximum.HasValue&&Maximum.Value>Minimum.Value;
    internal NativeSettingRow(string category, string group, string label, Func<string> read,
        Action<string> write, Func<string,string?> validate, string[]? choices = null)
    { Category=category; Group=group; Label=label; Read=read; Write=write; Validate=validate;
      Choices=choices ?? Array.Empty<string>(); string initial=read();Saved=Value=Choices.FirstOrDefault(c=>c.Equals(initial,StringComparison.OrdinalIgnoreCase))??initial; }
    internal bool Dirty => Value != Saved;
    internal string Hint => HintOverride ?? (Choices.Length > 0 ? string.Join(" / ", Choices.Select(c=>ChoiceLabels?.GetValueOrDefault(c)??c)) : Description);
}

internal sealed partial class NativeFullSettingsController : IDisposable
{
    internal static readonly string[] Categories = {"Display","Graphics","Audio","Controls","Replays","Profile","System","Maintenance","Credits"};
    internal readonly List<NativeSettingRow> Rows = new();
    private readonly MenuSettings _settings;
    private GamepadRuntimeConfig _runtime=GamepadRuntimeConfig.Current;
    private readonly Action<WindowStartMode>? _window;
    private HudProfile? _hud;
    private WindowStartMode? _videoBefore;
    private DateTime _videoDeadline;
    private bool _videoUsable;
    internal bool CanKeepVideo=>ConfirmingVideo&&_videoUsable;
    internal void AcknowledgeVideoFrame(){if(ConfirmingVideo&&!_videoUsable){_videoUsable=true;_videoDeadline=DateTime.UtcNow.AddSeconds(15);Status="Keep this display mode? Reverting automatically in 15 seconds.";}}
    internal int Category { get; private set; }
    internal string Status { get; private set; } = "Changes save automatically.";
    internal bool Closed { get; private set; }
    internal bool Dirty => Rows.Any(r=>r.Dirty) || _hud != null || HiddenBindingsDirty;
    internal bool ConfirmingVideo => _videoBefore != null;
    internal int VideoSeconds => Math.Max(0,(int)Math.Ceiling((_videoDeadline-DateTime.UtcNow).TotalSeconds));
    internal bool HudRequested { get; private set; }
    internal bool GameFilesRequested { get; private set; }
    internal bool ArenaImportRequested { get; private set; }
    private bool _arenaPicking;
    private bool _disposed;
    private string[]? _characterImportIds;
    private int _characterPreviewIndex;
    internal string CharacterPreviewLabel => Import.RetroCharacterPreviewExporter.Sources[_characterPreviewIndex].DisplayName;
    internal string CharacterPreviewId => Import.RetroCharacterPreviewExporter.Sources[_characterPreviewIndex].Id;
    internal void CycleCharacterPreview() { if(_disposed||Closed||Busy)return; _characterPreviewIndex = (_characterPreviewIndex + 1) % Import.RetroCharacterPreviewExporter.Sources.Count; }
    internal void RequestCharacterImport(bool all)
    {
        if(_disposed||Closed)return;
        if (Busy) { Status="Finish the current operation first."; return; }
        if (!ReadyForOperation("Apply or discard changes before importing a preview.")) return;
        var selected=Import.RetroCharacterPreviewExporter.Sources[_characterPreviewIndex];
        _characterImportIds=all ? Import.RetroCharacterPreviewExporter.Sources.Where(s=>s.Game==selected.Game).Select(s=>s.Id).ToArray() : new[]{selected.Id};
        ArenaImportRequested=true; _arenaPicking=true;
    }
    internal void CheckCharacterPreview()
    {
        if(_disposed||Closed||Busy)return;
        string id=CharacterPreviewId;
        Start(()=> { var result=Import.RetroCharacterPreviewExporter.InspectBundle(Import.RetroCharacterPreviewExporter.DefaultRoot,id); return Import.RetroCharacterPreviewExporter.DescribeInspection(id,result); });
    }
    private string? _characterSourcePreview;
    internal void RequestCharacterSourcePreview()
    {
        if (_disposed || Closed || Busy || Capturing || ConfirmingVideo || Confirmation.Length != 0) return;
        _characterSourcePreview = CharacterPreviewId;
    }
    internal bool TakeCharacterSourcePreview(out string id)
    {
        id = "";
        if (_disposed || Closed || Busy || Capturing || ConfirmingVideo || Confirmation.Length != 0 || _characterSourcePreview == null) return false;
        id = _characterSourcePreview; _characterSourcePreview = null; return true;
    }
    internal void RequestRemoveCharacterPreview()
    {
        if(_disposed||Closed||Busy)return;
        if (!ReadyForOperation("Apply or discard changes before removing a preview.")) return;
        _removeCharacterId=CharacterPreviewId; _confirmationAction=243;
        Confirmation="Remove "+_removeCharacterId+" from the active local preview inventory? Source media and retained generation files are kept for running consumers.";
    }
    private string? _removeCharacterId;
    internal void RequestArenaImport(){if(_disposed||Closed||Busy)return;if(!ReadyForOperation("Apply or discard changes before importing local arenas."))return;_characterImportIds=null;ArenaImportRequested=true;_arenaPicking=true;}
    internal bool TakeArenaImport(){if(!ArenaImportRequested)return false;ArenaImportRequested=false;return true;}
    internal void BeginArenaImport(string? path)
    {
        if(_disposed||Closed)return;
        var characterIds=_characterImportIds; _characterImportIds=null;
        _arenaPicking=false;if(string.IsNullOrWhiteSpace(path)){Status="Local import cancelled.";return;}
        if(!ReadyForOperation("Apply or discard changes before importing local arenas."))return;
        if (Busy) { Status="Another operation is already running."; return; }
        var cancellation = new System.Threading.CancellationTokenSource();
        _arenaCancellation = cancellation;
        _arenaStage = (int)Import.ImportPublishStage.Checking;
        // Only detached source/map values cross to this existing worker. The
        // catalog activates the committed generation on the next restart.
        Start(()=>
        {
            try
            {
                cancellation.Token.ThrowIfCancellationRequested();
                if (characterIds != null)
                {
                    var files=Import.RetroCharacterPreviewExporter.ExportFromOwnedGame(path,null,cancellation.Token,
                        new ArenaProgress(stage=>System.Threading.Volatile.Write(ref _arenaStage,(int)stage)),characterIds);
                    return files.Count+" private character authoring previews updated. No playable Hunter or online content was installed.";
                }
                var definitions = Import.EchoesPrivateMapImport.ReadAllArenas(path);
                var maps = Import.EchoesPrivateMapImport.InstallLocalDefinitions(definitions,
                    MapGen.CustomRooms.UserMapDirectory, cancellation.Token,
                    new ArenaProgress(stage=>System.Threading.Volatile.Write(ref _arenaStage,(int)stage)));
                return maps.Count+" local Echoes collision previews installed. Restart Project Prime to load them. No game assets were shared.";
            }
            finally { cancellation.Dispose(); }
        });
    }
    internal bool FinalKillCamAvailable => Rows.Any(row=>row.Label=="Kill Cam Enabled"&&row.Value.Equals("true",StringComparison.OrdinalIgnoreCase));
    internal HudEditorOption[] CopyHudOptions() => Rows.Select((row,index)=>(row,index))
        .Where(item => IsHudEditorOption(item.row) && (item.row.Group != "HUD" || item.row.Label == "Smooth Native Hud"))
        .Select(item => CreateHudOption(item.row,item.index))
        .OrderBy(option=>option.Section).ThenBy(option=>option.Group=="Stacked dual screens"?0:1)
        .ThenBy(option=>option.Group,StringComparer.Ordinal).ToArray();
    internal void AcceptHudOptions(HudEditorOption[] options)
    {
        foreach (var option in options)
            if (int.TryParse(option.Id, NumberStyles.None, CultureInfo.InvariantCulture, out int index)
                && index >= 0 && index < Rows.Count && IsHudEditorOption(Rows[index])) Stage(Rows[index], option.Value);
    }
    internal HudProfile CopyHud()
    {
        var hud=(_hud??HudProfiles.CopyCurrent()).DeepClone();
        bool Flag(string label,bool fallback){var row=Rows.FirstOrDefault(r=>r.Category=="Display"&&r.Group=="HUD"&&r.Label==label);return row!=null&&bool.TryParse(row.Value,out bool b)?b:fallback;}
        bool pro=Flag("Pro Hud",hud.Mode!=HudMode.Classic);hud.Mode=pro?(hud.Mode==HudMode.Classic?HudMode.ProjectPrime:hud.Mode):HudMode.Classic;
        hud.FixedWeapon=Flag("Pro Hud Fixed Weapon",hud.FixedWeapon);hud.Elements["combat.killFeed"].Enabled=Flag("Kill Feed Enabled",hud.Elements["combat.killFeed"].Enabled);
        hud.Elements["core.radar"].Enabled=Flag("Enabled",hud.Elements["core.radar"].Enabled);hud.RadarBackground=Flag("Show Background",hud.RadarBackground);hud.RadarOutlines=Flag("Show Outlines",hud.RadarOutlines);
        if(_hud==null)
        {
            var style=Rows.First(r=>r.Group=="HUD"&&r.Label=="Style");var size=Rows.First(r=>r.Group=="HUD"&&r.Label=="Size");
            if(style.Dirty||size.Dirty)hud.Crosshair=CrosshairProfile.FromLegacy(Enum.Parse<CrosshairStyle>(style.Value,true),Enum.Parse<CrosshairSize>(size.Value,true));
        }
        return hud;
    }
    internal void SetHud(HudProfile? value)
    {
        HudRequested=false;if(value==null)return;_hud=value.DeepClone();MarkAutoApplyEdited();
        void Set(string label,string val){var row=Rows.FirstOrDefault(r=>r.Category=="Display"&&r.Group=="HUD"&&r.Label==label);if(row!=null)row.Value=row.Choices.FirstOrDefault(c=>c.Equals(val,StringComparison.OrdinalIgnoreCase))??val;}
        Set("Pro Hud",(value.Mode!=HudMode.Classic).ToString());Set("Pro Hud Fixed Weapon",value.FixedWeapon.ToString());
        Set("Kill Feed Enabled",value.Elements["combat.killFeed"].Enabled.ToString());
        Set("Enabled",value.Elements["core.radar"].Enabled.ToString());Set("Show Background",value.RadarBackground.ToString());Set("Show Outlines",value.RadarOutlines.ToString());
    }
    internal void AcknowledgeGameFiles() => GameFilesRequested=false;

    internal NativeFullSettingsController(MenuSettings settings, Action<WindowStartMode>? window = null)
    {
        _settings=settings; _window=window;
        Add(typeof(LauncherPrefs),null,"Display","Window","WindowMode");
        Menu("Display","View","FieldOfView",null,RenderOptions.MinFov,RenderOptions.MaxFov);
        Menu("Display","Frame pacing","FrameRateCap",new[]{"display","30","60","75","90","100","120","144","165","180","200","240","360","unlimited"});
        Menu("Display","Frame pacing","ShowFps",OnOff);
        Menu("Display","HUD","SmoothNativeHud",OnOff);
        Add(typeof(LauncherPrefs),null,"Display","Campaign map","DetailedCampaignMap");
        if (!OperatingSystem.IsAndroid())
        {
            Add(typeof(LauncherPrefs),null,"Display","Campaign ending","OriginalCampaignCredits");
            Rows[^1].HintOverride = Rows[^1].Description = "Animated cartridge credits. Uses the mission receipt when unavailable.";
        }
        AddPrimitive(typeof(DsLayout),"Display","HUD preferences",new[]{"Enabled","ScreenAspect","RadarSize","RadarX","RadarY"});
        AddPrimitive(typeof(StylusZone),"Display","HUD preferences",new[]{"SelectSize"});
        Add(typeof(Features),null,"Display","HUD","ProHud","KillFeedEnabled","ProHudFixedWeapon");
        Add(typeof(Crosshair),null,"Display","HUD","Size","Style");
        Add(typeof(Radar),null,"Display","HUD","Enabled","ShowBackground","ShowOutlines");
        Add(typeof(RenderOptions),null,"Display","Accessibility","BrightSkins","BrightSkinStyle","PlayerOutline","PlayerOutlineWidth");
        Add(typeof(LauncherPrefs),null,"Display","Accessibility","ReduceMotion");
        var outline=OutlineColorPalette.CopyCurrent();
        for(int colorIndex=0;colorIndex<outline.Teams.Length+outline.Players.Length;colorIndex++)
        {
            int index=colorIndex;bool team=index<outline.Teams.Length;int slot=team?index:index-outline.Teams.Length;
            string Read(){var colors=OutlineColorPalette.CopyCurrent();return team?colors.Teams[slot]:colors.Players[slot];}
            void Write(string value){var colors=OutlineColorPalette.CopyCurrent();if(team)colors.Teams[slot]=value;else colors.Players[slot]=value;OutlineColorPalette.Publish(colors);}
            Rows.Add(new("Display","Outline colors",(team?"Team ":"Player ")+(slot+1),Read,Write,v=>Regex.IsMatch(v,"^#[0-9a-fA-F]{6}$")?null:"Use #RRGGBB."));
        }
        Menu("Graphics","Quality preset","GraphicsPreset",new[]{"original","performance","enhanced","ultra","extreme","custom"});
        Menu("Graphics","Rendering","ResolutionScale",null,RenderOptions.MinScale,RenderOptions.MaxScale);
        Menu("Graphics","Textures","TextureSampling",new[]{"auto","legacy","custom"});
        foreach(string p in new[]{"TextureFiltering","TextureMipmaps","TextureReplacements","CharacterModelReplacements"}) Menu("Graphics","Textures",p,OnOff);
        Menu("Graphics","Textures","TextureAnisotropy",new[]{"1","2","4","8","16"});
        Menu("Graphics","Textures","TextureUpscale",new[]{"off","scale2x","scale4x"});
        Menu("Graphics","Textures","TextureQuality",new[]{"automatic","low","medium","high","ultra"});
        foreach(string p in new[]{"Lighting","AdvancedMaterials","Fog"}) Menu("Graphics","Lighting and atmosphere",p,OnOff);
        Menu("Graphics","Lighting and atmosphere","ShadowQuality",new[]{"off","low","high","ultra"});
        Menu("Graphics","Cosmetics","ShowCustomCosmetics",OnOff);
        Menu("Graphics","Cosmetics","CosmeticQuality",new[]{"off","low","medium","high"});
        Rows[^1].HintOverride = "Menu atmosphere and cosmetic effects. Off keeps a static chamber; Low limits detail. Reduce Motion freezes movement.";
        Menu("Graphics","Cel shading","CelShading",OnOff); Menu("Graphics","Cel shading","CelBands",null,2,8); Menu("Graphics","Cel shading","CelEdge",null,0,100);
        foreach(string p in new[]{"SfxVolume","MusicVolume","PlayerVolume","WeaponVolume","NotificationVolume","EffectsVolume"}) Menu("Audio","Mixer",p,null,0,1);
        Menu("Audio","General","Language",Enum.GetNames<Language>());
        Add(typeof(LauncherPrefs),null,"Audio","Combat notifications","CombatFeedbackVolume","CombatNotificationsVisible");
        foreach(var cue in Enum.GetValues<Sound.CombatFeedbackCue>())
        {
            var c=cue;var options=Sound.CombatFeedbackAudio.GetOptions(c).Select(o=>o.Id).Append(Sound.CombatFeedbackAudio.GetSelection(c)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            string cueGroup=c is Sound.CombatFeedbackCue.ImperialistHeadshot or Sound.CombatFeedbackCue.FirstBlood?"Match awards":c>=Sound.CombatFeedbackCue.KillingSpree?"Life streaks":"Rapid multi-kills";
            Rows.Add(new("Audio",cueGroup,Words(c.ToString()),()=>Sound.CombatFeedbackAudio.GetSelection(c),v=>Sound.CombatFeedbackAudio.SetSelection(c,v),v=>options.Contains(v,StringComparer.OrdinalIgnoreCase)?null:"Choose an installed sound.",options){ChoiceLabels=Sound.CombatFeedbackAudio.GetOptions(c).ToDictionary(o=>o.Id,o=>o.Label,StringComparer.OrdinalIgnoreCase)});
        }
        Add(typeof(InputSettings),null,"Controls","Mouse and keyboard",new[]{"MouseSensitivity","HorizontalSensitivity","VerticalSensitivity","ImperialistZoomSensitivity","ImperialistZoomAmount","AltSwipeSensitivity","InvertMouseY","InvertMouseX","MouseMovementBoost","MouseAltFormMovement","ClassicMorphBallTouch","StylusMovementBoost","ScrollAllWeapons","ChatKey","ClipKey"});
        Add(typeof(InputSettings),null,"Controls","Custom scope (all aim devices)",
            "CustomScopeHold","CustomScope25Sensitivity","CustomScope4Sensitivity");
        foreach(var property in InputSettings.Bindings)
        {
            var p=property;
            string Read()
            {
                var bind=InputSettings.Bind(p);
                return bind.Type switch { Entities.ButtonType.Mouse=>"Mouse:"+bind.MouseButton,
                    Entities.ButtonType.ScrollUp=>"ScrollUp",Entities.ButtonType.ScrollDown=>"ScrollDown",_=>bind.Key.ToString() };
            }
            void Write(string value)
            {
                if(value=="ScrollUp"||value=="ScrollDown") InputSettings.Rebind(p,value=="ScrollUp"?Entities.ButtonType.ScrollUp:Entities.ButtonType.ScrollDown,Keys.Unknown,MouseButton.Left);
                else if(value.StartsWith("Mouse:",StringComparison.OrdinalIgnoreCase)) InputSettings.Rebind(p,Entities.ButtonType.Mouse,Keys.Unknown,Enum.Parse<MouseButton>(value[6..],true));
                else InputSettings.Rebind(p,Entities.ButtonType.Key,Enum.Parse<Keys>(value,true),MouseButton.Left);
            }
            Rows.Add(new("Controls","Keyboard bindings",InputSettings.ActionName(p),Read,Write,
                value=>ValidBinding(value)?null:"Use a key name (W, Space, LeftShift) or Mouse:Left / Mouse:Right / Mouse:Middle."){KeyboardBinding=true});
        }
        Rows.Add(new("Controls","Controller","Control layout",()=>GamepadRuntimeConfig.Current.Layout.Name,
            value=>GamepadRuntimeConfig.Current.Layout.Apply(value),
            value=>ControllerPresets.Contains(value,StringComparer.OrdinalIgnoreCase)?null:"Choose an available control layout.",ControllerPresets));
        AddPrimitive(typeof(GamepadOptions),"Controls","Controller");
        var pad=GamepadRuntimeConfig.Current.Bindings;
        foreach(var action in pad.Actions)
        for(int slot=0;slot<2;slot++)
        {
            var a=action;int index=slot;bool replay=pad.ReplayActions.Contains(a);
            string Read()=>GamepadRuntimeConfig.Current.Bindings.Modifier(a,index)+"+"+GamepadRuntimeConfig.Current.Bindings.Slot(a,index);
            bool Valid(string value){var parts=value.Split('+');return parts.Length==2&&Enum.TryParse<GamepadButtons>(parts[0],true,out var mod)&&Enum.TryParse<GamepadButtons>(parts[1],true,out var button)&&PadBindings.Single(mod)&&PadBindings.Single(button)&&((mod==0)||(mod!=button&&button!=0));}
            Rows.Add(new(replay?"Replays":"Controls","Controller bindings",pad.Name(a)+(slot==0?" primary":" secondary"),Read,
                value=>{var parts=value.Split('+');GamepadRuntimeConfig.Current.Bindings.SetSlot(a,index,Enum.Parse<GamepadButtons>(parts[1],true),Enum.Parse<GamepadButtons>(parts[0],true));},
                value=>Valid(value)?null:"Use Modifier+Button, for example None+A."){BindingAction=a,BindingSlot=index});
        }
        foreach(var control in TouchSettings.Order)
        {
            var c=control.Control;
            Rows.Add(new("Controls","Touch buttons",control.Label,()=>TouchSettings.IsEnabled(c).ToString(),v=>TouchSettings.SetEnabled(c,bool.Parse(v)),v=>bool.TryParse(v,out _)?null:"Choose true or false.",new[]{"false","true"}));
            string ReadLayout(){var layout=TouchSettings.CopyLayout();if(!layout.TryGetValue(c,out var b))return "default";return FormattableString.Invariant($"{b.X},{b.Y},{b.Scale}");}
            Rows.Add(new("Controls","Touch layout",control.Label+" position / size",ReadLayout,
                value=>{if(value=="default")TouchSettings.ResetLayout(c);else{var n=value.Split(',').Select(v=>float.Parse(v,CultureInfo.InvariantCulture)).ToArray();TouchSettings.SetLayout(c,n[0],n[1],n[2]);}},
                value=>value=="default"||ValidTuple(value,new[]{0d,0,TouchSettings.MinIndividualScale},new[]{1d,1,TouchSettings.MaxIndividualScale})?null:"Enter x,y,size (x/y 0–1, size 0.4–2), or default."));
        }
        for (int extra = 0; extra < TouchSettings.MaximumExtraInstances; extra++)
        {
            int id = TouchSettings.FirstExtraInstanceId + extra;
            string label = "Extra " + (extra + 1);
            TouchButtonInstance? Existing() => TouchSettings.ExtraInstances.FirstOrDefault(x => x.Id == id) is var x && x.Id == id ? x : null;
            void Publish(TouchButtonInstance? value)
            {
                var instances = TouchSettings.CopyInstances().Where(x => x.Id != id).ToList();
                if (value.HasValue) instances.Add(value.Value);
                TouchSettings.ReplaceInstances(instances);
            }
            Rows.Add(new("Controls", "Touch buttons", label + " action", () => Existing()?.Action.ToString() ?? "none",
                value => { if (value == "none") Publish(null); else Publish((Existing() ?? new(id,TouchControl.Shoot,true,.5f,.5f,1)) with { Action = Enum.Parse<TouchControl>(value) }); },
                value => value == "none" || Enum.TryParse<TouchControl>(value, out var a) && Enum.IsDefined(a) ? null : "Choose an action or none to delete.",
                new[] { "none" }.Concat(Enum.GetNames<TouchControl>()).ToArray()));
            Rows.Add(new("Controls", "Touch buttons", label + " enabled", () => (Existing()?.Enabled ?? true).ToString(),
                value => { if (Existing() is { } instance) Publish(instance with { Enabled = bool.Parse(value) }); },
                value => bool.TryParse(value,out _) ? null : "Choose true or false.", new[] { "false", "true" }));
            Rows.Add(new("Controls", "Touch layout", label + " position / size", () => Existing() is { } b ? FormattableString.Invariant($"{b.X},{b.Y},{b.Scale}") : "default",
                value => { if (Existing() is not { } b) return; var n = value == "default" ? new[] { .5f,.5f,1f } : value.Split(',').Select(v => float.Parse(v,CultureInfo.InvariantCulture)).ToArray(); Publish(b with { X=n[0],Y=n[1],Scale=n[2] }); },
                value => value == "default" || ValidTuple(value,new[] {0d,0,TouchSettings.MinIndividualScale},new[] {1d,1,TouchSettings.MaxIndividualScale}) ? null : "Enter x,y,size or default."));
        }
        AddControllerCalibrationRows();
        AddPrimitive(typeof(PointerInput),"Controls","Stylus",new[]{"StylusMode","GuardJumps"});
        Rows.Add(new("Controls","Stylus guide","Guide position / width",()=>FormattableString.Invariant($"{StylusZone.SavedLeft},{StylusZone.SavedTop},{StylusZone.SavedWidth}"),
            value=>{var n=value.Split(',').Select(v=>float.Parse(v,CultureInfo.InvariantCulture)).ToArray();StylusZone.SetRect(n[0],n[1],n[2]);},
            value=>ValidTuple(value,new[]{0d,0,.1},new[]{.9,.9,1d})?null:"Enter left,top,width (normalized screen coordinates)."));
        Rows.Add(new("Controls","Stylus guide","Enabled",()=>StylusZone.Wanted.ToString(),v=>StylusZone.Enabled=bool.Parse(v),v=>bool.TryParse(v,out _)?null:"Choose On or Off.",new[]{"false","true"}));
        AddPrimitive(typeof(StylusZone),"Controls","Stylus guide",new[]{"NativeUi","NativeUiOpacity","CursorOpacity","OutlineOpacity","ButtonOpacity","GuideColor"});
        AddPrimitive(typeof(TouchSettings),"Controls","Touch overlay",new[]{"Mode","StylusResponse","DirectWeaponSelection","ButtonsVisible","ButtonScale","StickScale","OverlayOpacity"});
        Add(typeof(Network.DemoClip),null,"Replays","Clips","Seconds","PostRollSeconds");
        Add(typeof(LauncherPrefs),null,"Replays","Storage","ReplayStorageLimitGb","ReplayAutoPrune","ReplayDeleteClips");
        Add(typeof(LauncherPrefs),null,"Replays","Playback","SpectatorNameTags","KillCamEnabled","KillCamCamera","FinalKillCamEnabled");
        Add(typeof(InputSettings),null,"Replays","Playback keys",typeof(InputSettings).GetProperties().Where(p=>p.Name.StartsWith("Replay",StringComparison.Ordinal)).Select(p=>p.Name).ToArray());
        Add(typeof(LauncherPrefs),null,"Profile","You","PlayerName","LastHunter","LastColor");
        Add(typeof(LauncherPrefs),null,"Profile","Servers","ServerAddress","ServerPort","MasterHost","MasterPort");
        Add(typeof(LauncherPrefs),null,"System","Updates and diagnostics","AutoUpdate","DebugLogs");
        foreach(var row in Rows.Where(r=>r.Category=="Controls"))row.Advanced=row.Group switch
        {
            "Controller"=>row.Label is not ("Control layout" or "Look X" or "Look Y" or "Left Inner" or "Right Inner" or "Invert Y" or "Vibration"),
            "Controller calibration" or "Weapon wheel"=>true,
            "Mouse and keyboard"=>row.Label is "Mouse Movement Boost" or "Mouse Alt Form Movement" or "Classic Morph Ball Touch" or "Stylus Movement Boost" or "Scroll All Weapons",
            "Stylus guide"=>row.Label is not ("Enabled" or "Native Ui"),_=>false
        };
        foreach (var row in Rows) row.HudOnly = row.Group is "HUD" or "HUD preferences" || row.Label is "Show Fps" or "Combat Notifications Visible" or "Spectator Name Tags"
            || row.Group == "Stylus guide" && row.Label is "Native Ui" or "Native Ui Opacity" or "Cursor Opacity" or "Outline Opacity" or "Button Opacity" or "Guide Color";
        UpdateLegacyHints(_savedBindings);
    }
    private static readonly string[] OnOff={"off","on"};
    private static bool ValidBinding(string value) => value is "ScrollUp" or "ScrollDown" || (value.StartsWith("Mouse:",StringComparison.OrdinalIgnoreCase)
        ? Enum.TryParse<MouseButton>(value[6..],true,out var mouse)&&Enum.IsDefined(mouse)
        : Enum.TryParse<Keys>(value,true,out var key)&&Enum.IsDefined(key));
    private void Menu(string category,string group,string name,string[]? choices=null,double min=double.NaN,double max=double.NaN)
    {
        var p=typeof(MenuSettings).GetProperty(name)!;
        bool volume=name.EndsWith("Volume",StringComparison.Ordinal);if(volume){min=0;max=100;}
        string Read(){string text=(string)p.GetValue(_settings)!;return volume&&double.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out double n)?(n*100).ToString("0.###",CultureInfo.InvariantCulture):text;}
        Rows.Add(new(category,group,Words(name)+(volume?" (%)":""),Read,v=>p.SetValue(_settings,volume?(double.Parse(v,CultureInfo.InvariantCulture)/100).ToString(CultureInfo.InvariantCulture):v),
            v=>choices!=null ? choices.Contains(v,StringComparer.OrdinalIgnoreCase)?null:"Choose: "+string.Join(", ",choices)
                : Number(v,min,max,name is "FieldOfView" or "ResolutionScale" or "CelBands" or "CelEdge"),choices){Description=choices!=null?"Choose a value":$"{min}–{max}",Minimum=choices==null?min:null,Maximum=choices==null?max:null,WholeNumber=name is "FieldOfView" or "ResolutionScale" or "CelBands" or "CelEdge"});
    }
    private void AddPrimitive(Type type,string category,string group,string[]? names=null) => Add(type,null,category,group,
        names ?? type.GetProperties(BindingFlags.Public|BindingFlags.Static).Where(p=>p.SetMethod?.IsPublic==true && (p.PropertyType.IsPrimitive||p.PropertyType.IsEnum||p.PropertyType==typeof(string))).Select(p=>p.Name).ToArray());
    private void Add(Type type, object? owner,string category,string group,params string[] names)
    {
        foreach(string name in names)
        {
            var p=type.GetProperty(name,BindingFlags.Public|BindingFlags.Static|BindingFlags.Instance)
                ?? throw new InvalidOperationException("Missing native settings owner: "+type.Name+"."+name);
            string[]? choices=name=="LastHunter"?Enumerable.Range(0,7).Select(i=>((Hunter)i).ToString()).Append(Hunter.Random.ToString()).ToArray():p.PropertyType==typeof(bool)?new[]{"false","true"}:p.PropertyType.IsEnum&&p.PropertyType!=typeof(Keys)?Enum.GetNames(p.PropertyType):null;
            string Read()=>Convert.ToString(p.GetValue(owner),CultureInfo.InvariantCulture)??"";
            string? Validate(string value)
            {
                if(choices!=null) return choices.Contains(value,StringComparer.OrdinalIgnoreCase)?null:"Choose: "+string.Join(", ",choices);
                if(p.PropertyType==typeof(Keys))return Enum.TryParse<Keys>(value,true,out var key)&&Enum.IsDefined(key)?null:"Choose a valid key.";
                if(name=="PlayerName") return Network.PlayerNameCodec.ValidationError(value.Trim());
                if(name.EndsWith("Port",StringComparison.Ordinal)) return Number(value,1,65535,true);
                if(name.EndsWith("Host",StringComparison.Ordinal)||name.EndsWith("Address",StringComparison.Ordinal)) return value.Length is >0 and <=253 && !value.Any(char.IsWhiteSpace)?null:"Enter a host name or IP address without a port.";
                if(name=="GuideColor")return Regex.IsMatch(value,"^#[0-9a-fA-F]{6}$")?null:"Use #RRGGBB.";
                if(p.PropertyType==typeof(string)) return value.Length<=256&&!value.Any(char.IsControl)?null:"Use at most 256 printable characters.";
                var (min,max)=NumericRange(name);
                return Number(value,min,max,p.PropertyType==typeof(int));
            }
            if(name=="ScreenAspect")choices=new[]{"4:3","5:3"};
            if(name=="KillCamCamera")choices=new[]{"0","1","2"};
            var numeric=NumericRange(name);bool tune=choices==null&&(p.PropertyType==typeof(float)||p.PropertyType==typeof(int))&&!name.EndsWith("Port",StringComparison.Ordinal);
            string label=name switch
            {
                "Enabled" when type==typeof(DsLayout)=>"Dual-screen DS layout",
                "ScreenAspect" when type==typeof(DsLayout)=>"Dual-screen aspect ratio",
                "RadarSize" when type==typeof(DsLayout)=>"Dual-screen radar size",
                "RadarX" when type==typeof(DsLayout)=>"Dual-screen radar X",
                "RadarY" when type==typeof(DsLayout)=>"Dual-screen radar Y",
                "SelectSize"=>"Touchscreen weapon selector size",
                "CustomScopeHold"=>"Hold custom scope (all aim devices)",
                "CustomScope25Sensitivity"=>"2.5x custom scope sensitivity (all aim devices)",
                "CustomScope4Sensitivity"=>"4x custom scope sensitivity (all aim devices)",
                "OriginalCampaignCredits"=>"Original campaign credits",
                _=>Words(name)
            };
            Rows.Add(new(category,group,label,Read,v=>p.SetValue(owner,p.PropertyType.IsEnum?Enum.Parse(p.PropertyType,v,true):Convert.ChangeType(v,p.PropertyType,CultureInfo.InvariantCulture)),Validate,choices){KeyboardBinding=p.PropertyType==typeof(Keys),Minimum=tune?numeric.Min:null,Maximum=tune?numeric.Max:null,WholeNumber=p.PropertyType==typeof(int),Description=tune?$"{numeric.Min:0.###}–{numeric.Max:0.###}":"Changes save automatically."});
            if(name=="CustomScopeHold")Rows[^1].ChoiceLabels=new(StringComparer.OrdinalIgnoreCase){{"false","Toggle on each Zoom press"},{"true","Hold Zoom to scope"}};
            if(name is "CustomScope25Sensitivity" or "CustomScope4Sensitivity")
                Rows[^1].Description="0.1–3x for mouse, controller, touch and keyboard aim after normal scope scaling; 1x preserves that feel.";
            if(name=="KillCamCamera")Rows[^1].ChoiceLabels=new(StringComparer.OrdinalIgnoreCase){{"0","Chase"},{"1","First person"},{"2","Cinematic"}};
        }
    }
    private static (double Min,double Max) NumericRange(string name)
    {
        double min=0,max=100;
        if(name.Contains("Sensitivity",StringComparison.Ordinal)||name is "LookX" or "LookY" or "ScopedX" or "ScopedY") {min=.01;max=20;}
        else if(name.Contains("Opacity",StringComparison.Ordinal)||name.Contains("Volume",StringComparison.Ordinal)||name.Contains("Threshold",StringComparison.Ordinal)||name.Contains("Inner",StringComparison.Ordinal)||name.Contains("Outer",StringComparison.Ordinal)||name.Contains("Trigger",StringComparison.Ordinal)||name=="ImperialistZoomAmount"||name=="VibrationStrength") max=1;
        else if(name=="LastColor") max=Network.PlayerColors.Count-1;
        else if(name=="PlayerOutlineWidth") {min=1;max=8;}
        else if(name=="KillCamCamera") max=2;
        else if(name=="Seconds") max=120;
        switch(name)
        {
            case "MouseSensitivity":min=.1;max=10;break;
            case "HorizontalSensitivity":case "VerticalSensitivity":min=.01;max=3;break;
            case "RadarSize":min=.25;max=3;break;
            case "RadarX":case "RadarY":min=0;max=1;break;
            case "SelectSize":min=.5;max=(double)(48f/44f);break;
            case "ImperialistZoomSensitivity":min=.1;max=3;break;
            case "CustomScope25Sensitivity":case "CustomScope4Sensitivity":min=.1;max=3;break;
            case "ImperialistZoomAmount":min=.25;max=2;break;
            case "AltSwipeSensitivity":min=.25;max=4;break;
            case "ButtonScale":min=(double)(decimal)TouchSettings.MinButtonScale;max=(double)(decimal)TouchSettings.MaxButtonScale;break;
            case "StickScale":min=(double)(decimal)TouchSettings.MinStickScale;max=(double)(decimal)TouchSettings.MaxStickScale;break;
            case "OverlayOpacity":min=(double)(decimal)TouchSettings.MinOverlayOpacity;max=(double)(decimal)TouchSettings.MaxOverlayOpacity;break;
            case "PostRollSeconds":max=5;break;
            case "LeftInner":case "RightInner":max=.9;break;
            case "LeftOuter":case "RightOuter":max=.5;break;
            case "LookX":case "LookY":min=.1;max=5;break;
            case "ScopedX":case "ScopedY":min=.1;max=3;break;
            case "TriggerThreshold":min=.05;max=.95;break;
            case "ActivityThreshold":min=.2;max=.95;break;
            case "WheelThreshold":min=.1;max=.95;break;
            case "LeftTriggerMin":case "RightTriggerMin":max=.8;break;
        }
        if(name.EndsWith("Port",StringComparison.Ordinal)){min=1;max=65535;}
        return(min,max);
    }
    private static string? Number(string value,double min,double max,bool integer=false) =>
        double.TryParse(value,NumberStyles.Float,CultureInfo.InvariantCulture,out double n)&&double.IsFinite(n)&&n>=min&&n<=max&&(!integer||n==Math.Truncate(n))?null:$"Enter {(integer?"a whole number":"a number")} from {min} to {max}.";
    private static string Words(string text)=>Regex.Replace(text,"(?<=[a-z])([A-Z])"," $1");
    internal bool Stage(NativeSettingRow row,string value)
    { if(row.Choices.Length>0)value=row.Choices.FirstOrDefault(c=>c.Equals(value,StringComparison.OrdinalIgnoreCase))??value;string? error=row.Validate(value); if(error!=null){Status=row.Label+": "+error;return false;} bool valueChanged=row.Value!=value;bool presetChanged=row.Label=="Graphics Preset"&&valueChanged;row.Value=value;if(valueChanged)MarkAutoApplyEdited();
        if(valueChanged&&row.Label=="Control layout"){StageControllerLayout(value);return true;}
        if(valueChanged&&row.Category=="Controls"&&(row.BindingAction!=null||row.Label=="Southpaw"))Rows.First(r=>r.Label=="Control layout").Value="Custom";
        if(valueChanged&&row.Category=="Graphics"&&row.Label!="Graphics Preset")Rows.First(r=>r.Label=="Graphics Preset").Value="custom";
        if(presetChanged&&Enum.TryParse<GraphicsPreset>(value,true,out var preset))
        {
            var profile=GraphicsPresetProfile.Get(preset);
            if(profile!=null)
            {
                void Set(string label,object val){var target=Rows.FirstOrDefault(r=>r.Category=="Graphics"&&r.Label==label);if(target!=null)target.Value=Convert.ToString(val,CultureInfo.InvariantCulture)!.ToLowerInvariant();}
                Set("Resolution Scale",profile.ResolutionScale);Set("Advanced Materials",RenderOptions.OnOff(profile.AdvancedMaterials));Set("Shadow Quality",profile.Shadows);
                Set("Lighting",RenderOptions.OnOff(profile.Lighting));Set("Fog",RenderOptions.OnOff(profile.Fog));Set("Texture Filtering",RenderOptions.OnOff(profile.TextureFiltering));Set("Texture Mipmaps",RenderOptions.OnOff(profile.TextureMipmaps));Set("Texture Upscale",profile.TextureUpscale);Set("Texture Anisotropy",profile.TextureAnisotropy);
                if(preset==GraphicsPreset.Original){Set("Texture Replacements","off");Set("Character Model Replacements","off");}
            }
        }
        Status=Dirty?(AutoApply?"Saving settings…":"Changes staged // Apply Changes to save."):"Saved settings.";return true; }
    internal void Select(int category) { if(category>=0&&category<Categories.Length&&!ConfirmingVideo) Category=category; }
    internal void ResetCategory() { if(Category==0)_hud=null;if(Category is 3 or 4)RestoreCategoryBindings(Category==4);foreach(var row in Rows.Where(r=>r.Category==Categories[Category]))row.Value=row.Saved; ResetAutoApplyState();Status="Category restored to its saved values."; }
    internal void Discard() { _bindingsDraft=null;UpdateLegacyHints(_savedBindings);CancelBinding();_calibration=null;Confirmation="";RevertVideo(); foreach(var row in Rows)row.Value=row.Saved;_hud=null;if(!ReferenceEquals(_runtime,GamepadRuntimeConfig.Current))RebaseControls();ResetAutoApplyState();Status="Unsaved changes restored to saved values."; }
    internal bool Back() { if(_capture!=null){CancelBinding();Status="Binding capture cancelled.";return false;}if(_calibration!=null){SuspendInput("Calibration cancelled.");return false;}if(Busy){Status="Wait for the current operation to finish.";return false;}if(Confirmation.Length>0){CancelBinding();Confirmation="";return false;}if(ConfirmingVideo){RevertVideo();return false;} if(Dirty){if(AutoApply){if(!FlushAutoApply())return false;}else{Status="Unsaved changes // Apply Changes or Discard before leaving.";return false;}}Closed=true;return true; }
    internal void ReportPreviewFailure(string error) { Status="Preview failed: "+error; }
    internal void RequestHud(){HudRequested=true;}
    internal void RequestGameFiles(){if(!ReadyForOperation("Apply or discard changes before opening Game Files."))return;GameFilesRequested=true;}
    internal bool Apply()
    {
        if(ConfirmingVideo||Busy||Confirmation.Length>0)return false;
        if(!ReferenceEquals(_runtime,GamepadRuntimeConfig.Current))
        {
            if(HiddenBindingsDirty||Rows.Any(r=>ControllerOwned(r)&&r.Dirty))
            {Status=AutoApply?"Active controller changed. Restore unsaved changes to edit the current controller.":"Active controller changed. Discard the Controls draft and reopen Settings before applying.";return false;}
            _runtime=GamepadRuntimeConfig.Current;RebaseBindings();
        }
        foreach(var row in Rows){string? error=row.Validate(row.Value);if(error!=null){Status=row.Label+": "+error;return false;}}
        if(SettingsPersistence.RestartRequired){Status="Restart required before editing imported settings.";return false;}
        foreach(string side in new[]{"Left","Right"})
        {
            var lo=Rows.First(r=>r.Group=="Controller"&&r.Label==side+" Trigger Min");var hi=Rows.First(r=>r.Group=="Controller"&&r.Label==side+" Trigger Max");
            if(double.Parse(hi.Value,CultureInfo.InvariantCulture)<double.Parse(lo.Value,CultureInfo.InvariantCulture)+.1){Status=side+" trigger maximum must exceed its minimum by at least 0.1.";return false;}
        }
        var mode=Rows.First(r=>r.Group=="Window");
        if(mode.Dirty&&_window!=null)
        { _videoUsable=false;_videoBefore=LauncherPrefs.WindowMode;_videoDeadline=DateTime.UtcNow.AddSeconds(15);
            try{_window(Enum.Parse<WindowStartMode>(mode.Value,true));Status="Keep this display mode? Reverting automatically in 15 seconds.";return true;}
            catch(Exception ex){try{RevertVideo();}catch{}Status="Display mode could not be applied: "+ex.Message;return false;} }
        return Commit();
    }
    internal bool KeepVideo()
    {
        if(!ConfirmingVideo)return false;
        if(!_videoUsable){Status="Waiting for a usable display frame. Revert remains available.";return false;}
        bool saved=Commit();
        if(saved)_videoBefore=null;
        else
        {
            string error=Status;RevertVideo();Status=error+" Previous display mode restored.";
            if(AutoApply){AutoApplyFailed=true;_failedAutoApplyRevision=_editRevision;}
        }
        return saved;
    }
    internal void RevertVideo(){if(_videoBefore is WindowStartMode mode){_videoBefore=null;_videoUsable=false;try{_window?.Invoke(mode);if(AutoApply){var row=Rows.First(r=>r.Group=="Window");row.Value=row.Saved;MarkAutoApplyEdited();}Status=AutoApply?"Previous display mode restored.":"Previous display mode restored. Changes remain in your draft.";}catch(Exception ex){Status="Display mode restore failed: "+ex.Message;}}}
    internal void Tick(bool inputActive=true){if(inputActive)TickInput();TickOperation();if(ConfirmingVideo&&DateTime.UtcNow>=_videoDeadline)RevertVideo();TickAutoApply(inputActive);}
    private bool Commit()
    {
        using var lease=SettingsPersistence.BeginWrite();
        if(lease==null){Status="Restart required before saving settings.";return false;}
        var changed=Rows.Where(r=>r.Dirty).ToArray();var before=changed.Select(r=>r.Read()).ToArray();
        var menuBefore=typeof(MenuSettings).GetProperties().Where(p=>p.CanRead&&p.CanWrite).ToDictionary(p=>p,p=>p.GetValue(_settings));
        var hudBefore=HudProfiles.CopyCurrent();
        var bindingsBefore=GamepadRuntimeConfig.Current.Bindings.Clone();
        var bindingsToCommit=_bindingsDraft!=null?ControllerDraft().Bindings.Clone():null;
        // Back up every owner file before touching engine state. Failure in a
        // later store restores the earlier stores, including previously absent files.
        string root=LauncherPrefs.Directory;
        string hudDirectory=HudProfiles.StorageDirectory??Paths.Combine("Savedata","hud-profiles");
        string[] paths={Paths.Combine("Savedata","settings.json"),Path.Combine(root,"launcher.txt"),Path.Combine(root,"controls.txt"),Path.Combine(hudDirectory,"active.json"),Path.Combine(hudDirectory,"active.json.bak")};
        var files=new Dictionary<string,byte[]?>();
        try
        {
            using var transaction=SettingsPersistence.BeginCommit();
            foreach(string path in paths) files[path]=File.Exists(path)?File.ReadAllBytes(path):null;
            for(int i=0;i<changed.Length;i++)changed[i].Write(changed[i].Value);
            if(bindingsToCommit!=null)GamepadRuntimeConfig.Current.Bindings.CopyFrom(bindingsToCommit);
            if(changed.Any(ControllerOwned))GamepadRuntimeConfig.Current.Bindings.Preset=Rows.First(r=>r.Label=="Control layout").Value;
            SettingsMigration.ResetRetiredPostProcessing(_settings);
            GameState.CommitSettings(_settings);
            if(!LauncherPrefs.TrySave())throw new IOException("Launcher preferences could not be written.");
            if(!InputSettings.TrySave())throw new IOException("Controls could not be written.");
            GameSettings.Apply(_settings);InputSettings.ApplyToPlayers();
            Sound.CombatFeedbackAudio.Reload();
            var hud=_hud?.DeepClone()??HudProfiles.CopyCurrent();
            hud.Mode=Features.ProHud?(hud.Mode==HudMode.Classic?HudMode.ProjectPrime:hud.Mode):HudMode.Classic;
            hud.FixedWeapon=Features.ProHudFixedWeapon;hud.Elements["core.radar"].Enabled=Radar.Enabled;
            hud.RadarBackground=Radar.ShowBackground;hud.RadarOutlines=Radar.ShowOutlines;
            hud.Elements["combat.killFeed"].Enabled=Features.KillFeedEnabled;
            if(_hud==null&&changed.Any(r=>r.Label is "Size" or "Style"))hud.Crosshair=CrosshairProfile.FromLegacy(Crosshair.Style,Crosshair.Size);
            HudProfiles.Save(hud);
            RespawnChoice.Request(LauncherPrefs.LastHunter,LauncherPrefs.LastColor);
            if(changed.Any(row=>row.Category=="Replays" && row.Group=="Storage")&&LauncherPrefs.ReplayAutoPrune&&LauncherPrefs.ReplayStorageLimitGb>0)
                try{Replay.ReplayStorageManager.Apply(new Replay.ReplayStoragePolicy(LauncherPrefs.ReplayStorageLimitGb*1024L*1024L*1024L,true,LauncherPrefs.ReplayDeleteClips,false));}catch(Exception ex){DebugLog.Line("settings","Replay storage cleanup: "+ex.Message);}
            foreach(var row in Rows){string value=row.Read();row.Saved=row.Value=row.Choices.FirstOrDefault(c=>c.Equals(value,StringComparison.OrdinalIgnoreCase))??value;}
            _runtime=GamepadRuntimeConfig.Current;RebaseBindings();_hud=null;try{if(LauncherPrefs.DebugLogs)DebugLog.Attach();else DebugLog.Detach();}catch(Exception ex){DebugLog.Line("settings","Logging preference applied: "+ex.Message);}transaction.Complete();ResetAutoApplyState();Status="All changes saved.";return true;
        }
        catch(Exception ex)
        {
            var errors=new List<Exception>{ex};
            foreach(var property in menuBefore)property.Key.SetValue(_settings,property.Value);
            for(int i=changed.Length-1;i>=0;i--)try{changed[i].Write(before[i]);}catch(Exception e){errors.Add(e);}
            try{GamepadRuntimeConfig.Current.Bindings.CopyFrom(bindingsBefore);}catch(Exception e){errors.Add(e);}
            foreach(var file in files)try{if(file.Value==null)File.Delete(file.Key);else SettingsPersistence.WriteBytes(file.Key,file.Value);}catch(Exception e){errors.Add(e);}
            try{GameSettings.Apply(_settings);InputSettings.ApplyToPlayers();HudProfiles.Publish(hudBefore);}catch(Exception e){errors.Add(e);}
            if(errors.Count>1 || ex is AggregateException)try{SettingsPersistence.Replace(()=>throw new AggregateException(errors));}catch(AggregateException){}
            Status="Could not save: "+ex.Message+(errors.Count>1 || ex is AggregateException?" // Rollback incomplete; restart before editing again.":" // Previous settings restored; draft retained.");return false;
        }
    }
    public void Dispose(){_disposed=true;ArenaImportRequested=false;_arenaPicking=false;_characterImportIds=null;_removeCharacterId=null;Confirmation="";if(ImportingArenas)CancelArenaImport();CancelBinding();_calibration=null;RevertVideo();}
}
#endif
