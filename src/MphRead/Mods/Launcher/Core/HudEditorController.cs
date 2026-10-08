using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using MphRead.Mods.Input;
using MphRead.Mods.Render.Hud;

namespace MphRead.Mods.Launcher.Core;

/// <summary>Detached HUD authoring. Only an explicit settings handoff can activate this profile.</summary>
public sealed class HudEditorController : IDisposable
{
    public const int PropertyPageSize = 8;
    private readonly int _owner = Environment.CurrentManagedThreadId;
    private readonly Guid _lifetime = Guid.NewGuid();
    private readonly IHudEditorBackend _backend;
    private readonly HudStudioHistory _history;
    private readonly HashSet<int> _selection = new() { 0 };
    private readonly Dictionary<int, Vector2> _groupOffsets = new();
    private readonly Dictionary<long, Vector2> _touches = new();
    private readonly GamepadEdges _padEdges = new();
    private long _revision = 1, _jsonRevision, _padRevision = -1, _padTime;
    private string _status = "", _error = "", _json = "", _name;
    private string? _before;
    private Vector2 _start, _offset;
    private float _pinchDistance, _pinchScale;
    private bool _closed, _disposed, _cancelled, _padNeutral, _controllerMove;
    private int _selected, _propertyPage, _propertyIndex, _aspect, _hunter, _scenario, _grid = 4;
    private int _crosshairTarget, _crosshairPreset = -1, _palette = -1, _radarPreset = -1;
    private float _width = 640, _height = 360;
    private float? _guideX, _guideY;
    private bool _snapGuides = true;
    private HudProfile? _accepted;
    private HudEditorSnapshot? _snapshot;
    private static readonly Vector2[] Sizes = { new(60), new(248,112), new(326,112), new(248,620), Vector2.Zero,
        new(180,120), new(200,60), new(520,160), new(340,80), new(520,200), new(300,100), new(120,40), new(225,17), new(304,17) };

    public HudEditorController(HudProfile original, IHudEditorBackend? backend = null)
    {
        _backend = backend ?? new HudEditorEngineBackend();
        _history = new(original ?? throw new ArgumentNullException(nameof(original)));
        _name = original.Name; _status = _backend.Warning ?? "Drag to move; arrows nudge. Shift selects a group or disables snapping. Ctrl gives fine movement; Ctrl+Z/Y undo and redo.";
    }
    public HudProfile CopyDraft() { VerifyOwner(); return _history.Draft.DeepClone(); }
#if MPHREAD_RMLUI_ANDROID_CHECK
    internal string GestureSummaryForCheck()
    {
        VerifyOwner();
        return $"touches={_touches.Count}, editing={_before != null}, pinchDistance={_pinchDistance.ToString(CultureInfo.InvariantCulture)}, selected={_selected}, scale={_history.Draft.Elements[HudProfileDefaults.ElementIds[_selected]].Scale.ToString(CultureInfo.InvariantCulture)}";
    }
#endif
    public HudEditorSnapshot Snapshot()
    {
        VerifyOwner();
        if (_snapshot?.Revision == _revision) return _snapshot;
        HudProfile profile = _history.Draft;
        var allProperties = Properties().ToImmutableArray();
        _propertyIndex = Math.Clamp(_propertyIndex, 0, Math.Max(0, allProperties.Length-1));
        int pages = Math.Max(1, (allProperties.Length+PropertyPageSize-1)/PropertyPageSize);
        _propertyPage = Math.Clamp(_propertyPage, 0, pages-1);
        HudPreviewState preview = HudPreviewState.For((HudPreviewScenario)_scenario);
        var elements = HudProfileDefaults.ElementIds.Select((id, i) =>
        {
            var element = profile.Elements[id];
            bool visible = element.Enabled && (element.Contexts & (preview.Spectator ? HudContext.SpectatorFree : HudContext.Playing)) != 0
                && (element.Visibility switch { HudVisibility.Spectator => preview.Spectator, HudVisibility.Damaged => preview.Health < 99,
                    HudVisibility.AmmoNotFull => preview.Ammo < 80, HudVisibility.Objective => preview.Objective, HudVisibility.Combat => preview.Combat, _ => true });
            return new HudEditorElement(i, id, Bounds(i), element.Enabled, element.Locked, _selected == i || _selection.Contains(i),
                visible ? element.Opacity*profile.GlobalOpacity : .18f, element.Color);
        }).ToImmutableArray();
        var e = profile.Elements[HudProfileDefaults.ElementIds[_selected]];
        var (previewWidth, previewHeight) = Aspect;
        return _snapshot = new()
        {
            Lifetime = _lifetime, Revision = _revision, ProfileJson = HudProfileStore.Serialize(profile), Name = _name,
            BasePreset = profile.BasePreset, CanUndo = _history.CanUndo, CanRedo = _history.CanRedo, Closed = _closed || _disposed,
            Selected = _selected, Elements = elements, Properties = allProperties.Skip(_propertyPage*PropertyPageSize).Take(PropertyPageSize).ToImmutableArray(),
            PropertyPage = _propertyPage, PropertyPageCount = pages, PropertyIndex = _propertyIndex, Property = allProperties.ElementAtOrDefault(_propertyIndex),
            X = Number(e.OffsetX), Y = Number(e.OffsetY), Scale = Number(e.Scale), Opacity = Number(e.Opacity), Color = e.Color,
            Anchor = e.Anchor, Visibility = e.Visibility, GridSize = GridSize, SnapGuides = _snapGuides,
            PreviewWidth = previewWidth, PreviewHeight = previewHeight, PreviewHunter = _hunter, Scenario = (HudPreviewScenario)_scenario,
            CanvasWidth = _width, CanvasHeight = _height,
            CrosshairTarget = _crosshairTarget, CrosshairOverride = _crosshairTarget == 0 || CurrentCrosshair(profile) != null,
            JsonText = _json, JsonRevision = _jsonRevision, Status = _status, Error = _error, Surface = Surface, GuideX = _guideX, GuideY = _guideY
        };
    }
    public void SetCanvasSize(float width, float height)
    {
        Verify(); if (!float.IsFinite(width) || !float.IsFinite(height) || width <= 0 || height <= 0) return;
        if (_width == width && _height == height) return;
        EndGesture(); _width = width; _height = height; Touch();
    }
    public bool SelectElement(int index, bool group = false)
    {
        Verify(); if ((uint)index >= HudProfileDefaults.ElementIds.Length) return false;
        EndGesture();
        if (group) { if (!_selection.Add(index)) _selection.Remove(index); }
        else { _selection.Clear(); _selection.Add(index); }
        _selected = index; _propertyIndex = _propertyPage = 0; Touch(); return true;
    }
    public bool SelectProperty(int row)
    {
        Verify(); int index = _propertyPage*PropertyPageSize+row;
        if ((uint)row >= PropertyPageSize || index >= Properties().Count()) return false;
        _propertyIndex = index; Touch(); return true;
    }
    public bool Dispatch(HudEditorAction action, string value = "", string name = "", string x = "", string y = "",
        string scale = "", string opacity = "", string color = "")
    {
        Verify(); if (_closed || !Enum.IsDefined(action)) return false;
        EndGesture(); _error = "";
        string id = HudProfileDefaults.ElementIds[_selected];
        try
        {
            switch (action)
            {
                case HudEditorAction.Open: return false;
                case HudEditorAction.Use:
                    var accepted = _history.Draft.DeepClone(); accepted.Name = name;
                    if (accepted.Mode == HudMode.Custom && HudProfileDefaults.Presets.Contains(accepted.Name)) accepted.Name += " (Custom)";
                    accepted.Validate(); _accepted = accepted; _closed = true; break;
                case HudEditorAction.Cancel: _closed = _cancelled = true; break;
                case HudEditorAction.NextPreset:
                    int preset = (Array.IndexOf(HudProfileDefaults.Presets, _history.Draft.BasePreset)+1)%HudProfileDefaults.Presets.Length;
                    _history.Replace(HudProfileDefaults.Create(HudProfileDefaults.Presets[preset])); _name = _history.Draft.Name; break;
                case HudEditorAction.Undo: _history.Undo(); break;
                case HudEditorAction.Redo: _history.Redo(); break;
                case HudEditorAction.LockAll: Edit(p => { foreach (var element in p.Elements.Values) element.Locked = true; }); break;
                case HudEditorAction.UnlockAll: Edit(p => { foreach (var element in p.Elements.Values) element.Locked = false; }); break;
                case HudEditorAction.AlignLeft: Align(true); break;
                case HudEditorAction.AlignTop: Align(false); break;
                case HudEditorAction.NativeElements: Edit(p => p.Health.Native = p.Ammo.Native = p.Inventory.Native = p.Crosshair.Native = true); break;
                case HudEditorAction.ResetHud: _history.Replace(HudProfileDefaults.Create(_history.Draft.BasePreset)); break;
                case HudEditorAction.ToggleVisible: Edit(p => p.Elements[id].Enabled = !p.Elements[id].Enabled); break;
                case HudEditorAction.ToggleLock: Edit(p => p.Elements[id].Locked = !p.Elements[id].Locked); break;
                case HudEditorAction.NextAnchor: if (_selected != 0) Edit(p => p.Elements[id].Anchor = (HudAnchor)(((int)p.Elements[id].Anchor+1)%9)); break;
                case HudEditorAction.NextVisibility: Edit(p => p.Elements[id].Visibility = (HudVisibility)(((int)p.Elements[id].Visibility+1)%7)); break;
                case HudEditorAction.ResetElement: Edit(p => p.ResetElement(id)); break;
                case HudEditorAction.ResetSection:
                    string prefix = id.Split('.')[0]+"."; Edit(p => { foreach (string key in HudProfileDefaults.ElementIds) if (key.StartsWith(prefix, StringComparison.Ordinal)) p.ResetElement(key); }); break;
                case HudEditorAction.NextAspect: _aspect = (_aspect+1)%5; break;
                case HudEditorAction.NextHunter: _hunter = (_hunter+1)%8; break;
                case HudEditorAction.NextScenario: _scenario = (_scenario+1)%7; break;
                case HudEditorAction.NextGrid: _grid = (_grid+1)%6; break;
                case HudEditorAction.ToggleGuides: _snapGuides = !_snapGuides; break;
                case HudEditorAction.SaveNamed: _backend.SaveNamed(name, _history.Draft.DeepClone()); _status = "Named profile saved. Use in settings, then Apply to activate."; break;
                case HudEditorAction.LoadNamed: _history.Replace(_backend.LoadNamed(name)); _name = _history.Draft.Name; break;
                case HudEditorAction.ExportJson: _json = HudProfileStore.Serialize(_history.Draft); _jsonRevision++; _status = "Select and copy the JSON to share your profile."; break;
                case HudEditorAction.ImportJson: _history.Replace(HudProfileStore.Parse(value)); _name = _history.Draft.Name; break;
                case HudEditorAction.PropertyPrevious: _propertyPage = Math.Max(0, _propertyPage-1); _propertyIndex = _propertyPage*PropertyPageSize; break;
                case HudEditorAction.PropertyNext:
                    _propertyPage = Math.Min((Properties().Count()-1)/PropertyPageSize, _propertyPage+1); _propertyIndex = _propertyPage*PropertyPageSize; break;
                case HudEditorAction.PropertyApply: ApplyProperty(value); break;
                case HudEditorAction.PropertyReset: ResetProperty(); break;
                case HudEditorAction.NextPalette: _palette = (_palette+1)%HudPalettes.Names.Length; Edit(p => HudPalettes.Apply(p, _palette)); break;
                case HudEditorAction.NextCrosshairTarget: _crosshairTarget = (_crosshairTarget+1)%11; _propertyPage = _propertyIndex = 0; break;
                case HudEditorAction.ToggleCrosshairOverride:
                    if (_crosshairTarget != 0) Edit(p => SetCrosshair(p, CurrentCrosshair(p) == null ? InheritedCrosshair(p) : null)); break;
                case HudEditorAction.NextCrosshairPreset: _crosshairPreset = (_crosshairPreset+1)%CrosshairPresets.Names.Length; Edit(p => SetCrosshair(p, CrosshairPresets.Create(_crosshairPreset))); break;
                case HudEditorAction.ShareCrosshair: _json = CrosshairPresets.Share(DisplayedCrosshair(_history.Draft)); _jsonRevision++; break;
                case HudEditorAction.ImportCrosshair: var imported = CrosshairPresets.Import(value.Trim()); Edit(p => SetCrosshair(p, imported)); break;
                case HudEditorAction.NextRadarPreset: _radarPreset = (_radarPreset+1)%Enum.GetValues<HudRadarStyle>().Length; Edit(p => HudRadarStyles.Apply(p, (HudRadarStyle)_radarPreset)); break;
                case HudEditorAction.NudgeLeft: Nudge(-1,0); break;
                case HudEditorAction.NudgeRight: Nudge(1,0); break;
                case HudEditorAction.NudgeUp: Nudge(0,-1); break;
                case HudEditorAction.NudgeDown: Nudge(0,1); break;
                case HudEditorAction.ScaleDown: Resize(-.1f); break;
                case HudEditorAction.ScaleUp: Resize(.1f); break;
                case HudEditorAction.ApplyLayout:
                    float nx = ParseNumber(x), ny = ParseNumber(y), ns = ParseNumber(scale), no = ParseNumber(opacity);
                    if (ns is < .1f or > 8 || no is < 0 or > 1 || nx is < -3840 or > 3840 || ny is < -2160 or > 2160
                        || HudColor.Normalize(color) != color) throw new FormatException("Layout values are outside their allowed range or color format.");
                    Edit(p => { var e = p.Elements[id]; if (_selected != 0) { e.OffsetX = nx; e.OffsetY = ny; } e.Scale = ns; e.Opacity = no; e.Color = color; }); break;
                default: return false;
            }
            Touch(); return true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        { _error = ex.Message; Touch(); return false; }
    }
    private void Edit(Action<HudProfile> change) => _history.Edit(p => { change(p); p.Mode = HudMode.Custom; });
    private void ApplyProperty(string value)
    {
        HudEditorProperty row = Properties().ElementAt(_propertyIndex);
        if (row.Path == "/independentGauges")
        {
            bool enabled = bool.Parse(value);
            Edit(p => { if (enabled) p.DetachGauges(); else p.IndependentGauges = false; });
            return;
        }
        if (_selected == 0 && row.Path.StartsWith("/crosshair/", StringComparison.Ordinal))
        {
            string[] fields = row.Path[11..].Split('/');
            PropertyInfo property = fields.Length == 1 ? typeof(CrosshairProfile).GetProperty(Pascal(fields[0]))!
                : typeof(CrosshairPartStyle).GetProperty(Pascal(fields[1]))!;
            object parsed = ParseValue(value, property.PropertyType);
            if (property.PropertyType == typeof(string) && HudColor.Normalize(value) != value)
                throw new FormatException("Use a color in #RRGGBB format.");
            if (property.PropertyType.IsEnum && !Enum.IsDefined(property.PropertyType, parsed))
                throw new FormatException("Choose a listed value.");
            if (property.PropertyType == typeof(float) || property.PropertyType == typeof(int))
            {
                var descriptor = fields.Length == 1 ? CrosshairProperties.All.Single(d => d.Id == property.Name) : null;
                float minimum = descriptor?.Min ?? (property.Name == "Outline" ? -1 : 0);
                float maximum = descriptor?.Max ?? (property.Name == "Outline" ? 20 : 1);
                float number = Convert.ToSingle(parsed, CultureInfo.InvariantCulture);
                if (number < minimum || number > maximum) throw new FormatException("The value is outside its allowed range.");
            }
            Edit(p =>
            {
                var crosshair = CurrentCrosshair(p) ?? InheritedCrosshair(p); SetCrosshair(p, crosshair);
                if (fields.Length == 1) CrosshairProperties.All.Single(d => d.Id == property.Name).Set(crosshair, parsed);
                else { string partName = Pascal(fields[0]); CrosshairProperties.MarkPart(crosshair, partName); property.SetValue(typeof(CrosshairProfile).GetProperty(partName)!.GetValue(crosshair), parsed); }
            });
            return;
        }
        JsonNode data = JsonNode.Parse(HudProfileStore.Serialize(_history.Draft))!;
        JsonNode currentValue = At(data, row.Path)!;
        JsonNode replacement = currentValue.GetValueKind() switch
        {
            JsonValueKind.Number => JsonNode.Parse(value) ?? throw new FormatException("Enter a number."),
            JsonValueKind.True or JsonValueKind.False => JsonValue.Create(bool.Parse(value))!, _ => JsonValue.Create(value)!
        };
        if (row.Path.EndsWith("/contexts", StringComparison.Ordinal))
        {
            if (!Enum.TryParse(value, true, out HudContext contexts) || (contexts & ~HudContext.All) != 0)
                throw new FormatException("Choose HUD contexts from Playing, SpectatorPov, SpectatorFree and Replay.");
            replacement = JsonValue.Create(contexts.ToString())!;
        }
        Put(data, row.Path, replacement);
        var profile = HudProfileStore.Parse(data.ToJsonString());
        if (!JsonNode.DeepEquals(At(data,row.Path), At(JsonNode.Parse(HudProfileStore.Serialize(profile))!,row.Path)))
            throw new FormatException("The value is outside its allowed range or format.");
        profile.Mode = HudMode.Custom; _history.Replace(profile);
    }
    private void ResetProperty()
    {
        var row = Properties().ElementAt(_propertyIndex);
        if (_selected == 0 && row.Path.StartsWith("/crosshair/", StringComparison.Ordinal))
        {
            string[] fields = row.Path[11..].Split('/');
            Edit(p =>
            {
                var crosshair = CurrentCrosshair(p) ?? InheritedCrosshair(p); SetCrosshair(p,crosshair);
                if (fields.Length == 1) CrosshairProperties.All.Single(d => d.Id == Pascal(fields[0])).Reset(crosshair,
                    _crosshairTarget == 0 ? HudProfileDefaults.Create(p.BasePreset).Crosshair : DisplayedInheritance(p));
                else
                {
                    string part = Pascal(fields[0]); typeof(CrosshairProfile).GetProperty(part)!.SetValue(crosshair,new CrosshairPartStyle());
                    if (crosshair.OverrideProperties != null) crosshair.OverrideProperties = crosshair.OverrideProperties.Where(v => v != part).ToArray();
                }
            });
        }
        else
        {
            JsonNode data = JsonNode.Parse(HudProfileStore.Serialize(_history.Draft))!;
            JsonNode defaults = JsonNode.Parse(HudProfileStore.Serialize(HudProfileDefaults.Create(_history.Draft.BasePreset)))!;
            Put(data,row.Path,At(defaults,row.Path)!.DeepClone());
            var profile = HudProfileStore.Parse(data.ToJsonString()); profile.Mode=HudMode.Custom; _history.Replace(profile);
        }
    }
    private IEnumerable<HudEditorProperty> Properties()
    {
        var profile = _history.Draft;
        JsonNode data = JsonNode.Parse(HudProfileStore.Serialize(profile))!;
        if (_selected == 0) data["crosshair"] = JsonNode.Parse(HudProfileStore.Serialize(new HudProfile { Crosshair = DisplayedCrosshair(profile) }))!["crosshair"]!.DeepClone();
        string id = HudProfileDefaults.ElementIds[_selected];
        string section = _selected switch { 0 => "/crosshair/", 1 or 12 => "/health/", 2 or 13 => "/ammo/", 3 => "/inventory/", 4 => "/radar/", 7 => "/killFeed/", 8 => "/notifications/", _ => "!" };
        foreach (var leaf in Leaves(data,""))
        {
            if (leaf.Path is "/schemaVersion" or "/basePreset" or "/name" || leaf.Path.Contains("overrideProperties",StringComparison.Ordinal)
                || leaf.Path.StartsWith("/weaponCrosshairs/",StringComparison.Ordinal) || leaf.Path.StartsWith("/zoomCrosshair/",StringComparison.Ordinal)) continue;
            bool global = leaf.Path.LastIndexOf('/') == 0;
            if (!global && !leaf.Path.StartsWith("/elements/"+id+"/",StringComparison.Ordinal) && !leaf.Path.StartsWith(section,StringComparison.Ordinal)
                && !(_selected == 0 && leaf.Path.StartsWith("/hitMarker/",StringComparison.Ordinal))) continue;
            if (_selected == 0 && leaf.Path is "/elements/core.crosshair/offsetX" or "/elements/core.crosshair/offsetY" or "/elements/core.crosshair/anchor") continue;
            string text = leaf.Value.GetValueKind() == JsonValueKind.String ? leaf.Value.GetValue<string>() : leaf.Value.ToJsonString();
            string[] choices = Choices(leaf.Path);
            string kind = choices.Length > 0 ? "Choice" : leaf.Value.GetValueKind() switch { JsonValueKind.True or JsonValueKind.False => "Boolean", JsonValueKind.Number => "Number", _ => "Text" };
            yield return new(leaf.Path, leaf.Path.Trim('/').Replace('/',' '), text, kind, choices.ToImmutableArray());
        }
    }
    private static IEnumerable<(string Path,JsonValue Value)> Leaves(JsonNode node, string path)
    {
        if (node is JsonValue value) { yield return (path,value); yield break; }
        if (node is JsonObject obj) foreach (var pair in obj) { if (pair.Value == null) continue; foreach (var leaf in Leaves(pair.Value,path+"/"+pair.Key)) yield return leaf; }
        else if (node is JsonArray array) for (int i=0;i<array.Count;i++) if(array[i]!=null) foreach(var leaf in Leaves(array[i]!,path+"/"+i)) yield return leaf;
    }
    private static JsonNode? At(JsonNode node,string path)
    { foreach(string key in path.Trim('/').Split('/')) node = node is JsonArray array ? array[int.Parse(key,CultureInfo.InvariantCulture)]! : node[key]!; return node; }
    private static void Put(JsonNode node,string path,JsonNode value)
    { int split=path.LastIndexOf('/'); JsonNode parent=split==0 ? node : At(node,path[..split])!; string key=path[(split+1)..]; if(parent is JsonArray array) array[int.Parse(key,CultureInfo.InvariantCulture)]=value; else parent[key]=value; }
    private static string[] Choices(string path)
    {
        string field=path[(path.LastIndexOf('/')+1)..]; return field switch
        {
            "mode" => Enum.GetNames<HudMode>(), "anchor" => Enum.GetNames<HudAnchor>(), "visibility" => Enum.GetNames<HudVisibility>(),
            "contexts" => Enum.GetNames<HudContext>(), "dotShape" => Enum.GetNames<CrosshairDotShape>(), "orientation" => Enum.GetNames<HudRadarOrientation>(),
            "outOfRange" => Enum.GetNames<HudRadarOutOfRangeMode>(), "elevation" => Enum.GetNames<HudRadarElevationMode>(), "queue" => Enum.GetNames<HudNotificationQueue>(),
            "shape" => Enum.GetNames<HudHitMarkerShape>(), "style" when path.StartsWith("/radar/",StringComparison.Ordinal) => Enum.GetNames<HudRadarStyle>(), _ => Array.Empty<string>()
        };
    }
    private static object ParseValue(string value,Type type)
    { if(type==typeof(bool)) return bool.Parse(value); if(type==typeof(int)) return int.Parse(value,CultureInfo.InvariantCulture); if(type==typeof(float)) return ParseNumber(value); if(type.IsEnum) return Enum.Parse(type,value); return value; }
    private static float ParseNumber(string value) => float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture,out float number) && float.IsFinite(number) ? number : throw new FormatException("Enter a finite number.");
    private static string Pascal(string value) => char.ToUpperInvariant(value[0])+value[1..];
    private CrosshairProfile? CurrentCrosshair(HudProfile profile) => _crosshairTarget == 10 ? profile.ZoomCrosshair : _crosshairTarget == 0 ? profile.Crosshair : profile.WeaponCrosshairs[_crosshairTarget-1];
    private void SetCrosshair(HudProfile profile,CrosshairProfile? crosshair)
    { if(_crosshairTarget==10) profile.ZoomCrosshair=crosshair; else if(_crosshairTarget==0) profile.Crosshair=crosshair!; else profile.WeaponCrosshairs[_crosshairTarget-1]=crosshair; }
    private CrosshairProfile DisplayedInheritance(HudProfile profile) => _crosshairTarget == 10 ? CrosshairProperties.Resolve(profile.Crosshair,profile.WeaponCrosshairs[4]) : profile.Crosshair;
    private CrosshairProfile DisplayedCrosshair(HudProfile profile) => CrosshairProperties.Resolve(DisplayedInheritance(profile),CurrentCrosshair(profile));
    private CrosshairProfile InheritedCrosshair(HudProfile profile)
    { var inherited = HudProfileStore.Parse(HudProfileStore.Serialize(profile)).Crosshair; inherited.OverrideProperties = Array.Empty<string>(); return inherited; }
    private (float Width,float Height) Aspect => _aspect switch { 1 => (3440,1440), 2 => (1440,1080), 3 => (1920,1200), 4 => (1080,1920), _ => (1920,1080) };
    private int GridSize => _grid==0 ? 0 : 1 << (_grid-1);
    private HudEditorRect Surface
    {
        get { var aspect=Aspect; float scale=Math.Min(_width/aspect.Width,_height/aspect.Height); return new((_width-aspect.Width*scale)/2,(_height-aspect.Height*scale)/2,aspect.Width*scale,aspect.Height*scale); }
    }
    private HudTransform Transform => new(Surface.Width,Surface.Height,_history.Draft.SafeArea);
    public HudEditorRect Bounds(int index)
    {
        VerifyOwner(); var profile=_history.Draft; var e=profile.Elements[HudProfileDefaults.ElementIds[index]]; var t=Transform; var surface=Surface;
        Vector2 point=t.Resolve(e.Anchor,new(e.OffsetX,e.OffsetY));
        Vector2 size=(index==4 ? HudRadarGeometry.GetBounds(new(profile.Radar),profile.TextScale) : Sizes[index])*t.UnitScale*e.Scale*profile.GlobalScale;
        if(index==4) { size=Vector2.Max(size,new(16));point-=size/2; }
        if(index==11) point.X-=size.X;
        if(index is 6 or 8) point.X-=size.X/2;
        if(index==0) point=new Vector2(surface.Width/2,surface.Height/2)-size/2;
        return new(surface.X+point.X,surface.Y+point.Y,Math.Max(16,size.X),Math.Max(16,size.Y));
    }
    public void Nudge(float x,float y)
    { Verify(); if(_selected==0 || _history.Draft.Elements[HudProfileDefaults.ElementIds[_selected]].Locked) return; Edit(p=> { var e=p.Elements[HudProfileDefaults.ElementIds[_selected]];e.OffsetX+=x;e.OffsetY+=y; });Touch(); }
    public void Resize(float amount)
    { Verify(); if(_history.Draft.Elements[HudProfileDefaults.ElementIds[_selected]].Locked) return;Edit(p=>p.Elements[HudProfileDefaults.ElementIds[_selected]].Scale=Math.Clamp(p.Elements[HudProfileDefaults.ElementIds[_selected]].Scale+amount,.1f,8));Touch(); }
    public void SetVisible(bool enabled)
    { Verify(); Edit(p=>p.Elements[HudProfileDefaults.ElementIds[_selected]].Enabled=enabled); Touch(); }
    private void Align(bool horizontal)
    {
        if(_selection.Count<2) return; var target=Bounds(_selected); var before=_selection.ToDictionary(i=>i,Bounds);
        Edit(p=> { foreach(int index in _selection) { var e=p.Elements[HudProfileDefaults.ElementIds[index]];if(index==0||e.Locked) continue;e.OffsetX+=horizontal ? (target.X-before[index].X)/Transform.UnitScale : 0;e.OffsetY+=horizontal ? 0 : (target.Y-before[index].Y)/Transform.UnitScale; } });
    }
    public bool PointerDown(long pointer,float x,float y,bool shift=false,bool alt=false,bool touch=false)
    {
        Verify(); if(_closed) return false; Vector2 point=new(x,y); if(!Surface.Contains(point)) return false;
        if (_axisGesture) EndGesture();
        if(touch) { _touches[pointer]=point;if(_touches.Count==2&&_before!=null) { _pinchDistance=TouchDistance();_pinchScale=_history.Draft.Elements[HudProfileDefaults.ElementIds[_selected]].Scale;return true; } }
        for(int step=0;step<HudProfileDefaults.ElementIds.Length;step++)
        {
            int index=alt ? (_selected-1-step+HudProfileDefaults.ElementIds.Length*2)%HudProfileDefaults.ElementIds.Length : HudProfileDefaults.ElementIds.Length-1-step;
            if(!Bounds(index).Contains(point,8)) continue;
            if(shift) { if(!_selection.Add(index)) _selection.Remove(index); } else if(!_selection.Contains(index)) { _selection.Clear();_selection.Add(index); }
            _selected=index;_propertyPage=_propertyIndex=0;Touch();
            var element=_history.Draft.Elements[HudProfileDefaults.ElementIds[index]];
            if(index==0||element.Locked) return true;
            _before=_history.Capture();_start=point;_offset=new(element.OffsetX,element.OffsetY);_groupOffsets.Clear();
            foreach(int member in _selection) { var e=_history.Draft.Elements[HudProfileDefaults.ElementIds[member]];if(member!=0&&!e.Locked)_groupOffsets[member]=new(e.OffsetX,e.OffsetY); }
            return true;
        }
        return false;
    }
    public bool PointerMove(long pointer,float x,float y,bool shift=false,bool control=false)
    {
        Verify(); if(_before==null) return false;Vector2 point=new(x,y);if(_touches.ContainsKey(pointer))_touches[pointer]=point;
        var element=_history.Draft.Elements[HudProfileDefaults.ElementIds[_selected]];
        if(_touches.Count>=2&&_pinchDistance>0) element.Scale=Math.Clamp(_pinchScale*TouchDistance()/_pinchDistance,.1f,8);
        else
        {
            Vector2 offset=_offset+(point-_start)/Transform.UnitScale*(control ? .1f : 1);
            if(GridSize>0&&!shift) offset=new(MathF.Round(offset.X/GridSize)*GridSize,MathF.Round(offset.Y/GridSize)*GridSize);
            element.OffsetX=offset.X;element.OffsetY=offset.Y;_guideX=_guideY=null;
            if(_snapGuides&&!shift) Snap();
            Vector2 movement=new Vector2(element.OffsetX,element.OffsetY)-_offset;
            foreach(var member in _groupOffsets) { if(member.Key==_selected)continue;var e=_history.Draft.Elements[HudProfileDefaults.ElementIds[member.Key]];e.OffsetX=member.Value.X+movement.X;e.OffsetY=member.Value.Y+movement.Y; }
        }
        _history.Draft.Mode=HudMode.Custom;Touch();return true;
    }
    public void PointerUp(long pointer) { Verify();_touches.Remove(pointer);_pinchDistance=0;EndGesture(); }
    public void ReleaseInput() { VerifyOwner();_touches.Clear();_pinchDistance=0;EndGesture();_controllerMove=false; }
    private float TouchDistance() { var values=_touches.Values.Take(2).ToArray();return values.Length==2 ? Vector2.Distance(values[0],values[1]) : 0; }
    private void Snap()
    {
        var moving=Bounds(_selected);var surface=Surface;float margin=_history.Draft.SafeArea;float dx=7,dy=7;
        void Target(HudEditorRect rect)
        {
            foreach(float target in new[]{rect.X,rect.Center.X,rect.Right}) foreach(float from in new[]{moving.X,moving.Center.X,moving.Right}) if(Math.Abs(target-from)<Math.Abs(dx)) { dx=target-from;_guideX=target; }
            foreach(float target in new[]{rect.Y,rect.Center.Y,rect.Bottom}) foreach(float from in new[]{moving.Y,moving.Center.Y,moving.Bottom}) if(Math.Abs(target-from)<Math.Abs(dy)) { dy=target-from;_guideY=target; }
        }
        Target(surface);Target(new(surface.X+surface.Width*margin,surface.Y+surface.Height*margin,surface.Width*(1-2*margin),surface.Height*(1-2*margin)));
        for(int i=1;i<HudProfileDefaults.ElementIds.Length;i++) if(i!=_selected&&!_selection.Contains(i)&&_history.Draft.Elements[HudProfileDefaults.ElementIds[i]].Enabled)Target(Bounds(i));
        var element=_history.Draft.Elements[HudProfileDefaults.ElementIds[_selected]];if(_guideX!=null)element.OffsetX+=dx/Transform.UnitScale;if(_guideY!=null)element.OffsetY+=dy/Transform.UnitScale;
    }
    private bool _axisGesture;
    private void EndGesture() { _axisGesture=false;_guideX=_guideY=null;if(_before==null)return;_history.Commit(_before);_before=null;Touch(); }
    public void HandleControllerAxes(GamepadSnapshot snapshot,long now)
    {
        Verify();
        // The owner polls gamepads every frame, including while a mouse/touch
        // gesture owns the draft. Idle/disconnected pads must not commit it.
        if (_before != null && !_axisGesture) { _padTime=now; return; }
        var state=snapshot.State;var pressed=_padEdges.Update(snapshot);
        if(snapshot.Revision!=_padRevision||!state.Connected) { EndGesture();_padRevision=snapshot.Revision;_padNeutral=false;_padTime=now;return; }
        float dt=Math.Clamp((now-_padTime)/1000f,0,.05f);_padTime=now;
        bool moving=Math.Abs(state.LeftX)>.25f||Math.Abs(state.LeftY)>.25f||Math.Abs(state.RightX)>.25f||Math.Abs(state.RightY)>.25f;
        if(!_padNeutral) { if(!moving&&state.Buttons==0)_padNeutral=true;return; }
        string id=HudProfileDefaults.ElementIds[_selected];
        if((pressed&GamepadButtons.X)!=0) { EndGesture();Edit(p=>p.ResetElement(id));Touch(); }
        if((pressed&GamepadButtons.Y)!=0) { EndGesture();Edit(p=>p.Elements[id].Enabled=!p.Elements[id].Enabled);Touch(); }
        var element=_history.Draft.Elements[id];if(!moving||element.Locked){EndGesture();return;}
        _axisGesture=true;_before??=_history.Capture();float Axis(float v)=>Math.Abs(v)>.25f?v:0;
        if(_selected!=0) { element.OffsetX+=(Axis(state.LeftX)*180+Axis(state.RightX)*20)*dt;element.OffsetY-=Axis(state.LeftY)*180*dt; }
        element.Scale=Math.Clamp(element.Scale+Axis(state.RightY)*dt,.1f,8);_history.Draft.Mode=HudMode.Custom;Touch();
    }
    public bool HandleController(UiAction action,bool canvasFocused)
    {
        Verify();EndGesture();
        if(action==UiAction.Back) { if(_controllerMove){_controllerMove=false;Touch();}else Dispatch(HudEditorAction.Cancel);return true; }
        if(action is UiAction.PreviousTab or UiAction.NextTab) return SelectElement((_selected+(action==UiAction.NextTab?1:HudProfileDefaults.ElementIds.Length-1))%HudProfileDefaults.ElementIds.Length);
        if(action==UiAction.Accept&&canvasFocused) { _controllerMove=!_controllerMove;_status=_controllerMove?"Move mode: stick/D-pad moves; triggers resize; Back returns to controls.":"Shoulders select an element; Accept on canvas enters move mode.";Touch();return true; }
        if(!_controllerMove)return false;
        switch(action) { case UiAction.PageUp:Resize(-.1f);break;case UiAction.PageDown:Resize(.1f);break;case UiAction.Left:Nudge(-1,0);break;case UiAction.Right:Nudge(1,0);break;case UiAction.Up:Nudge(0,-1);break;case UiAction.Down:Nudge(0,1);break;default:return false; }return true;
    }
    public bool TryTakeAccepted(out HudProfile profile)
    { VerifyOwner();profile=null!;if(_disposed||_accepted==null)return false;profile=_accepted;_accepted=null;return true; }
    public bool TryTakeCancelled() { VerifyOwner();if(!_cancelled)return false;_cancelled=false;return true; }
    public void ReportFailure(string error) { Verify();_closed=false;_accepted=null;_error=error;Touch(); }
    private static string Number(float value)=>value.ToString("0.###",CultureInfo.InvariantCulture);
    private void Touch()=>_revision++;
    private void VerifyOwner(){if(_owner!=Environment.CurrentManagedThreadId)throw new InvalidOperationException("HUD authoring belongs to the engine thread.");}
    private void Verify(){VerifyOwner();ObjectDisposedException.ThrowIf(_disposed,this);}
    public void Dispose(){VerifyOwner();if(_disposed)return;ReleaseInput();_accepted=null;_closed=_disposed=true;Touch();}
}
