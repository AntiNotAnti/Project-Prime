#if !MPHREAD_SERVER
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
    private readonly List<HudEditorOption> _options;
    private readonly HudEditorOption[] _originalOptions;
    private int _inspectorSection;
    private bool _profilesWorkspace;
    private string _propertySearch = "";
    private HudEditorSnapshot? _snapshot;
    private static readonly Vector2[] Sizes = { new(60), new(248,112), new(326,112), new(248,620), Vector2.Zero,
        new(180,120), new(200,60), new(520,160), new(340,80), new(520,200), new(300,100), new(120,40), new(225,17), new(304,17), new(750,HudProfileDefaults.MovementTelemetryHeight*5.625f), new(400,60), new(400,60), new(400,60), new(520,60), new(520,80), new(520,120), new(1000,600), new(520,120), new(520,60), new(520,80), new(1000,600) };
    private static readonly string[] CrosshairFirstPagePaths = { "/crosshair/enabled", "/crosshair/native", "/crosshair/color", "/crosshair/scale",
        "/crosshair/gap", "/crosshair/thickness", "/crosshair/lengthX", "/crosshair/lengthY" };
    private static readonly string[] HudWideFirstPagePaths = { "/globalScale", "/globalOpacity", "/textScale", "/iconScale", "/accentColor", "/safeArea" };

    public HudEditorController(HudProfile original, IHudEditorBackend? backend = null, HudEditorOption[]? options = null)
    {
        _backend = backend ?? new HudEditorEngineBackend();
        _originalOptions = options?.ToArray() ?? Array.Empty<HudEditorOption>();
        _options = new(_originalOptions);
        _history = new(original ?? throw new ArgumentNullException(nameof(original)));
        _name = original.Name; _status = _backend.Warning ?? "Drag to move; arrows nudge. Shift selects a group or disables snapping. Ctrl gives fine movement; Ctrl+Z/Y undo and redo.";
    }
    public HudEditorOption[] CopyOptions() { VerifyOwner(); return _options.ToArray(); }
    public HudProfile CopyDraft() { VerifyOwner(); return _history.Draft.DeepClone(); }
    public bool ControllerMoveMode { get { VerifyOwner(); return _controllerMove; } }
    internal bool GestureActive { get { VerifyOwner(); return _before != null || _touches.Count != 0; } }
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
                HudPreviewState.ElementVisible(id,(HudPreviewScenario)_scenario) || i==_selected ? visible ? element.Opacity*profile.GlobalOpacity : .18f : 0, element.Color);
        }).ToImmutableArray();
        var e = profile.Elements[HudProfileDefaults.ElementIds[_selected]];
        var (previewWidth, previewHeight) = Aspect;
        return _snapshot = new()
        {
            Lifetime = _lifetime, Revision = _revision, ProfileJson = HudProfileStore.Serialize(profile), Name = _name,
            BasePreset = profile.BasePreset, CanUndo = _history.CanUndo, CanRedo = _history.CanRedo, Closed = _closed || _disposed,
            Selected = _selected, Elements = elements, Properties = allProperties.Skip(_propertyPage*PropertyPageSize).Take(PropertyPageSize).ToImmutableArray(),
            InspectorSection = _inspectorSection, ProfilesWorkspace = _profilesWorkspace, PropertySearch = _propertySearch,
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
        _selected = index; _inspectorSection = 0; _profilesWorkspace = false; _propertySearch = ""; _propertyIndex = _propertyPage = 0; Touch(); return true;
    }
    public bool SelectProperty(int row)
    {
        Verify(); int index = _propertyPage*PropertyPageSize+row;
        if ((uint)row >= PropertyPageSize || index >= Properties().Count()) return false;
        _propertyIndex = index; Touch(); return true;
    }
    public bool ApplyPropertyRow(int row, string value) => SelectProperty(row) && Dispatch(HudEditorAction.PropertyApply, value);
    public bool CyclePropertyRow(int row, bool previous) => SelectProperty(row)
        && Dispatch(previous ? HudEditorAction.PropertyChoicePrevious : HudEditorAction.PropertyChoiceNext);
    public bool ResetPropertyRow(int row) => SelectProperty(row) && Dispatch(HudEditorAction.PropertyReset);
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
                case HudEditorAction.PreviousPreset:
                case HudEditorAction.NextPreset:
                    int preset = Cycle(Array.IndexOf(HudProfileDefaults.Presets, _history.Draft.BasePreset), HudProfileDefaults.Presets.Length, action == HudEditorAction.PreviousPreset);
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
                case HudEditorAction.PreviousAnchor:
                case HudEditorAction.NextAnchor: if (_selected != 0) Edit(p => p.Elements[id].Anchor = (HudAnchor)Cycle((int)p.Elements[id].Anchor, 9, action == HudEditorAction.PreviousAnchor)); break;
                case HudEditorAction.PreviousVisibility:
                case HudEditorAction.NextVisibility: Edit(p => p.Elements[id].Visibility = (HudVisibility)Cycle((int)p.Elements[id].Visibility, 7, action == HudEditorAction.PreviousVisibility)); break;
                case HudEditorAction.ResetElement: Edit(p => p.ResetElement(id)); break;
                case HudEditorAction.ResetSection:
                    string prefix = id.Split('.')[0]+"."; Edit(p => { foreach (string key in HudProfileDefaults.ElementIds) if (key.StartsWith(prefix, StringComparison.Ordinal)) p.ResetElement(key); }); break;
                case HudEditorAction.PreviousAspect:
                case HudEditorAction.NextAspect: _aspect = Cycle(_aspect, 6, action == HudEditorAction.PreviousAspect); break;
                case HudEditorAction.PreviousHunter:
                case HudEditorAction.NextHunter: _hunter = Cycle(_hunter, 8, action == HudEditorAction.PreviousHunter); break;
                case HudEditorAction.PreviousScenario:
                case HudEditorAction.NextScenario: _scenario = Cycle(_scenario, Enum.GetValues<HudPreviewScenario>().Length, action == HudEditorAction.PreviousScenario); break;
                case HudEditorAction.PreviousGrid:
                case HudEditorAction.NextGrid: _grid = Cycle(_grid, 6, action == HudEditorAction.PreviousGrid); break;
                case HudEditorAction.ToggleGuides: _snapGuides = !_snapGuides; break;
                case HudEditorAction.SaveNamed: _backend.SaveNamed(name, _history.Draft.DeepClone()); _status = "Named profile saved. Use in settings, then Apply to activate."; break;
                case HudEditorAction.LoadNamed: _history.Replace(_backend.LoadNamed(name)); _name = _history.Draft.Name; break;
                case HudEditorAction.ExportJson: _json = HudProfileStore.Serialize(_history.Draft); _jsonRevision++; _status = "Select and copy the JSON to share your profile."; break;
                case HudEditorAction.ImportJson: _history.Replace(HudProfileStore.Parse(value)); _name = _history.Draft.Name; break;
                case HudEditorAction.PropertyPrevious: _propertyPage = Math.Max(0, _propertyPage-1); _propertyIndex = _propertyPage*PropertyPageSize; break;
                case HudEditorAction.PropertyNext:
                    _propertyPage = Math.Min((Properties().Count()-1)/PropertyPageSize, _propertyPage+1); _propertyIndex = _propertyPage*PropertyPageSize; break;
                case HudEditorAction.PropertyApply: ApplyProperty(value); break;
                case HudEditorAction.PropertyChoicePrevious:
                case HudEditorAction.PropertyChoiceNext:
                    var property = Properties().ElementAtOrDefault(_propertyIndex);
                    if (property == null || property.Choices.IsEmpty) return false;
                    ApplyProperty(property.Choices[Cycle(property.Choices.IndexOf(property.Value), property.Choices.Length, action == HudEditorAction.PropertyChoicePrevious)]); break;
                case HudEditorAction.SectionAppearance: case HudEditorAction.SectionLayout: case HudEditorAction.SectionGlobal:
                case HudEditorAction.SectionTypography: case HudEditorAction.SectionDecorations:
                    _inspectorSection = (int)action - (int)HudEditorAction.SectionAppearance;
                    _propertyPage = _propertyIndex = 0; _propertySearch = ""; _profilesWorkspace = false; break;
                case HudEditorAction.WorkspaceElements: _profilesWorkspace = false; break;
                case HudEditorAction.WorkspaceProfiles: _profilesWorkspace = true; break;
                case HudEditorAction.SearchProperties: _propertySearch = value.Trim(); _propertyPage = _propertyIndex = 0; break;
                case HudEditorAction.ClearSearch: _propertySearch = ""; _propertyPage = _propertyIndex = 0; break;
                case HudEditorAction.PropertyReset: ResetProperty(); break;
                case HudEditorAction.PreviousPalette:
                case HudEditorAction.NextPalette: _palette = Cycle(_palette, HudPalettes.Names.Length, action == HudEditorAction.PreviousPalette); Edit(p => HudPalettes.Apply(p, _palette)); break;
                case HudEditorAction.PreviousCrosshairTarget:
                case HudEditorAction.NextCrosshairTarget: _crosshairTarget = Cycle(_crosshairTarget, 11, action == HudEditorAction.PreviousCrosshairTarget); _propertyPage = _propertyIndex = 0; break;
                case HudEditorAction.ToggleCrosshairOverride:
                    if (_crosshairTarget != 0) Edit(p => SetCrosshair(p, CurrentCrosshair(p) == null ? InheritedCrosshair(p) : null)); break;
                case HudEditorAction.PreviousCrosshairPreset:
                case HudEditorAction.NextCrosshairPreset: _crosshairPreset = Cycle(_crosshairPreset, CrosshairPresets.Names.Length, action == HudEditorAction.PreviousCrosshairPreset); Edit(p => SetCrosshair(p, CrosshairPresets.Create(_crosshairPreset))); break;
                case HudEditorAction.ShareCrosshair: _json = CrosshairPresets.Share(DisplayedCrosshair(_history.Draft)); _jsonRevision++; break;
                case HudEditorAction.ImportCrosshair: var imported = CrosshairPresets.Import(value.Trim()); Edit(p => SetCrosshair(p, imported)); break;
                case HudEditorAction.PreviousRadarPreset:
                case HudEditorAction.NextRadarPreset: _radarPreset = Cycle(_radarPreset, Enum.GetValues<HudRadarStyle>().Length, action == HudEditorAction.PreviousRadarPreset); Edit(p => HudRadarStyles.Apply(p, (HudRadarStyle)_radarPreset)); break;
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
        if (IsTypographyFontPath(row.Path)) value = FontId(value);
        if (row.Path.StartsWith("/options/", StringComparison.Ordinal))
        {
            int index = _options.FindIndex(o => o.Id == row.Path[9..]);
            var option = _options[index];
            if (!option.Choices.IsEmpty && !option.Choices.Contains(value)) throw new FormatException("Choose a listed value.");
            if (option.Validate?.Invoke(value) is string error) throw new FormatException(error);
            _options[index] = option with { Value = value }; return;
        }

        if (row.Path == "/smoothArtwork")
        {
            if (value is not ("inherit" or "false" or "true")) throw new FormatException("Choose inherit, false or true.");
            Edit(p => p.SmoothArtwork = value == "inherit" ? null : bool.Parse(value)); return;
        }
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
                if(fields.Length==1&&property.Name is "ShotScale" or "ChargeScale" or "ZoomScale" or "NativeThickness")maximum=Math.Min(maximum,4);
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
        // Choosing a face is an affirmative typography edit. Keep the change in
        // this one JSON transaction so it remains one undo step and does not
        // rewrite any of the style's other saved presentation values.
        if (row.Path is "/typography/globalFont" or "/typography/gameFont" or "/typography/interfaceFont")
        {
            string enablePath = row.Path switch
            {
                "/typography/globalFont" => "/typography/useGlobalFont",
                "/typography/gameFont" => "/typography/useGameFont",
                _ => "/typography/useInterfaceFont"
            };
            Put(data, enablePath, JsonValue.Create(true)!);
            if(enablePath!="/typography/useGlobalFont")Put(data,"/typography/useGlobalFont",JsonValue.Create(false)!);
        }
        else if (IsTypographyStyleFontPath(row.Path))
        {
            string stylePath = row.Path[..^"/font".Length];
            Put(data, stylePath + "/enabled", JsonValue.Create(true)!);
        }
        var profile = HudProfileStore.Parse(data.ToJsonString());
        if (!JsonNode.DeepEquals(At(data,row.Path), At(JsonNode.Parse(HudProfileStore.Serialize(profile))!,row.Path)))
            throw new FormatException("The value is outside its allowed range or format.");
        profile.Mode = HudMode.Custom; _history.Replace(profile);
    }
    private void ResetProperty()
    {
        var row = Properties().ElementAt(_propertyIndex);
        if (row.Path.StartsWith("/options/", StringComparison.Ordinal))
        {
            int index = _options.FindIndex(o => o.Id == row.Path[9..]); _options[index] = _originalOptions[index]; return;
        }
        if (row.Path == "/smoothArtwork") { Edit(p => p.SmoothArtwork = HudProfileDefaults.Create(p.BasePreset).SmoothArtwork); return; }
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
                    string part=Pascal(fields[0]);var partProperty=typeof(CrosshairProfile).GetProperty(part)!;
                    var defaults=_crosshairTarget==0?HudProfileDefaults.Create(p.BasePreset).Crosshair:DisplayedInheritance(p);
                    object currentPart=partProperty.GetValue(crosshair)!,defaultPart=partProperty.GetValue(defaults)!;
                    var field=typeof(CrosshairPartStyle).GetProperty(Pascal(fields[1]))!;
                    field.SetValue(currentPart,field.GetValue(defaultPart));
                    if(crosshair.OverrideProperties!=null)
                    {
                        bool inherited=typeof(CrosshairPartStyle).GetProperties().All(property=>Equals(property.GetValue(currentPart),property.GetValue(defaultPart)));
                        if(inherited)crosshair.OverrideProperties=crosshair.OverrideProperties.Where(value=>value!=part).ToArray();
                        else CrosshairProperties.MarkPart(crosshair,part);
                    }
                }
            });
        }
        else
        {
            JsonNode data = JsonNode.Parse(HudProfileStore.Serialize(_history.Draft))!;
            var defaultProfile=HudProfileDefaults.Create(_history.Draft.BasePreset);
            defaultProfile.Validate();
            JsonNode defaults = JsonNode.Parse(HudProfileStore.Serialize(defaultProfile))!;
            Put(data,row.Path,At(defaults,row.Path)!.DeepClone());
            var profile = HudProfileStore.Parse(data.ToJsonString()); profile.Mode=HudMode.Custom; _history.Replace(profile);
        }
    }
    private IEnumerable<HudEditorProperty> Properties() => EnumerateProperties().OrderBy(PropertyPresentationPriority);
    private IEnumerable<HudEditorProperty> EnumerateProperties()
    {
        var profile = _history.Draft;
        JsonNode data = JsonNode.Parse(HudProfileStore.Serialize(profile))!;
        if (_selected == 0) data["crosshair"] = JsonNode.Parse(HudProfileStore.Serialize(new HudProfile { Crosshair = DisplayedCrosshair(profile) }))!["crosshair"]!.DeepClone();
        data["smoothArtwork"] = profile.SmoothArtwork?.ToString().ToLowerInvariant() ?? "inherit";
        string id = HudProfileDefaults.ElementIds[_selected];
        string section = _selected switch { 0 => "/crosshair/", 1 or 12 => "/health/", 2 or 13 => "/ammo/", 3 => "/inventory/", 4 => "/radar/", 7 => "/killFeed/", 8 => "/notifications/", _ => "!" };
        // The dependent master, proportions and stylus switch must stay together
        // before the per-element geometry can push them onto separate pages.
        foreach(var option in _options.Where(option=>option.Section==_inspectorSection&&option.Group=="Stacked dual screens"&&(option.Element<0||option.Element==_selected)))
        {
            if(_propertySearch.Length!=0&&!option.Label.Contains(_propertySearch,StringComparison.OrdinalIgnoreCase)
                &&!option.Group.Contains(_propertySearch,StringComparison.OrdinalIgnoreCase))continue;
            yield return new HudEditorProperty("/options/"+option.Id,option.Label,option.Value,OptionKind(option),option.Choices)
                { Range=option.Range, Help=option.Hint };
        }
        var typographyIds=TypographyIdsForElement(_selected);
        var decorationIds=DecorationIdsForElement(_selected);
        foreach (var leaf in Leaves(data,""))
        {
            if (leaf.Path is "/schemaVersion" or "/basePreset" or "/name" or "/mode" or "/typography/paletteDefaultsVersion" || leaf.Path.Contains("overrideProperties",StringComparison.Ordinal)
                || leaf.Path.StartsWith("/weaponCrosshairs/",StringComparison.Ordinal) || leaf.Path.StartsWith("/zoomCrosshair/",StringComparison.Ordinal)) continue;
            bool global = leaf.Path.LastIndexOf('/') == 0;
            bool element = leaf.Path.StartsWith("/elements/"+id+"/",StringComparison.Ordinal);
            bool elementStyle = element && leaf.Path[(leaf.Path.LastIndexOf('/')+1)..] is "opacity" or "color";
            bool appearance = (_selected == 0 && leaf.Path is "/weaponCrosshairTheme" or "/nativeReticleOpacity")
                || (_selected == 4 && leaf.Path is "/radarBackground" or "/radarOutlines")
                || (_selected is 1 or 12 && leaf.Path == "/haloLowHealthSound")
                || leaf.Path.StartsWith(section,StringComparison.Ordinal) || (_selected == 0 && leaf.Path.StartsWith("/hitMarker/",StringComparison.Ordinal));
            if(appearance&&!AppearancePropertyVisible(profile,leaf.Path))continue;
            bool typography = IsSelectedTypographyPath(profile,leaf.Path,typographyIds);
            bool globalTypography = IsHudWideTypographyPath(profile,leaf.Path);
            bool decoration = IsSelectedDecorationPath(profile,leaf.Path,decorationIds);
            bool globalDecoration = IsHudWideDecorationPath(profile,leaf.Path);
            bool visible = _inspectorSection switch
            {
                0 => appearance || elementStyle,
                1 => (_selected is not (12 or 13) || profile.IndependentGauges || leaf.Path == "/independentGauges")
                    && (element && !elementStyle || leaf.Path=="/independentGauges"&&(_selected is 1 or 2 or 12 or 13)),
                2 => global && leaf.Path is not ("/textScale" or "/independentGauges" or "/weaponCrosshairTheme"
                    or "/nativeReticleOpacity" or "/radarBackground" or "/radarOutlines" or "/haloLowHealthSound")
                    || _inspectorSection == 2 && (leaf.Path == "/textScale" || globalTypography || globalDecoration),
                3 => typography, 4 => decoration, _ => false
            };
            if (!visible || _propertySearch.Length != 0 && !leaf.Path.Contains(_propertySearch,StringComparison.OrdinalIgnoreCase)
                && !PropertyLabel(leaf.Path).Contains(_propertySearch,StringComparison.OrdinalIgnoreCase)) continue;
            if (_selected == 0 && leaf.Path is "/elements/core.crosshair/offsetX" or "/elements/core.crosshair/offsetY" or "/elements/core.crosshair/anchor") continue;
            string text = leaf.Value.GetValueKind() == JsonValueKind.String ? leaf.Value.GetValue<string>() : leaf.Value.ToJsonString();
            string[] choices = leaf.Value.GetValueKind() is JsonValueKind.True or JsonValueKind.False ? new[]{"false", "true"} : Choices(leaf.Path);
            if (IsTypographyFontPath(leaf.Path)) text = FontName(text);
            string kind = choices.Length > 0 ? "Choice" : leaf.Value.GetValueKind() switch
            { JsonValueKind.True or JsonValueKind.False => "Boolean", JsonValueKind.Number => "Number", _ => IsColorValue(text) ? "Color" : "Text" };
            yield return new HudEditorProperty(leaf.Path, PropertyLabel(leaf.Path), text, kind, choices.ToImmutableArray())
                { Range=leaf.Value.GetValueKind()==JsonValueKind.Number ? PropertyRange(profile,leaf.Path) : null, Help=PropertyHelp(leaf.Path) };
        }
        foreach (var option in _options)
        {
            if(option.Group=="Stacked dual screens"||option.Element>=0&&option.Element!=_selected)continue;
            if (_inspectorSection != option.Section || _propertySearch.Length != 0 && !option.Label.Contains(_propertySearch, StringComparison.OrdinalIgnoreCase)
                && !option.Group.Contains(_propertySearch,StringComparison.OrdinalIgnoreCase)) continue;
            yield return new HudEditorProperty("/options/"+option.Id, option.Group.Length==0?option.Label:option.Group+" · "+option.Label,
                option.Value,OptionKind(option),option.Choices) { Range=option.Range, Help=option.Hint };
        }
    }
    private int PropertyPresentationPriority(HudEditorProperty property)
    {
        string path = property.Path;
        switch (_inspectorSection)
        {
            case 0:
                if (_selected == 0)
                {
                    int first = Array.IndexOf(CrosshairFirstPagePaths, path);
                    if (first >= 0) return first;
                    if (path == "/crosshair/nativeThickness") return 8;
                    if (path == "/crosshair/nativeOriginalColors") return 9;
                    if (path == "/crosshair/nativeAnimations") return 10;
                    if (path == "/nativeReticleOpacity") return 11;
                    if (path.StartsWith("/crosshair/", StringComparison.Ordinal)) return path.IndexOf('/', 11) >= 0 ? 200 : 100;
                    if (path.StartsWith("/hitMarker/", StringComparison.Ordinal)) return 300;
                    if (path == "/weaponCrosshairTheme") return 290;
                }
                if (_selected == 4 && path.StartsWith("/radar/", StringComparison.Ordinal)) return 100 + RadarPresentationPriority(path);
                if (_selected == 4 && path is ("/radarBackground" or "/radarOutlines")) return 180;
                if (path == "/haloLowHealthSound") return 700;
                if (path.StartsWith("/elements/", StringComparison.Ordinal)) return 800;
                return 100;
            case 1:
                if (path == "/independentGauges") return 0;
                if (path.StartsWith("/elements/", StringComparison.Ordinal))
                {
                    string field = path[(path.LastIndexOf('/') + 1)..];
                    return 100 + (field switch { "enabled" => 0, "anchor" => 1, "offsetX" => 2, "offsetY" => 3,
                        "scale" => 4, "visibility" => 5, "layer" => 6, "contexts" => 7, "locked" => 8, _ => 9 });
                }
                if (path.StartsWith("/options/", StringComparison.Ordinal)) return 500;
                return 1000;
            case 2:
                if (IsStackedScreenOption(path)) return 0;
                int shared = Array.IndexOf(HudWideFirstPagePaths, path);
                if (shared >= 0) return 100 + shared;
                if (path.StartsWith("/typography/", StringComparison.Ordinal)) return 1000 + TypographyPresentationPriority(path);
                if (path.StartsWith("/decorations/", StringComparison.Ordinal)) return 3000 + DecorationPresentationPriority(path);
                if (path.StartsWith("/options/", StringComparison.Ordinal)) return IsTouchOption(path) ? 4000 : 300;
                return 200;
            case 3:
                return TypographyPresentationPriority(path);
            case 4:
                return DecorationPresentationPriority(path);
            default:
                return 10000;
        }
    }
    private bool IsStackedScreenOption(string path)
        => _options.Any(option => option.Section == _inspectorSection && option.Group == "Stacked dual screens"
            && (option.Element < 0 || option.Element == _selected) && path == "/options/" + option.Id);
    private bool IsTouchOption(string path)
        => _options.Any(option => option.Section == _inspectorSection && path == "/options/" + option.Id
            && option.Group.StartsWith("Touchscreen", StringComparison.Ordinal));
    private static int RadarPresentationPriority(string path)
    {
        string field = path[(path.LastIndexOf('/') + 1)..];
        return field switch
        {
            "style" => 0, "orientation" => 1, "rangeScale" => 2, "radiusScale" => 3, "rangeRings" => 4,
            "hunters" => 5, "weapons" => 6, "powerups" => 7, "cardinals" => 8, "objectives" => 9, "hunterFacing" => 10,
            "outOfRange" => 11, "backgroundOpacity" => 12, "outlineThickness" => 13, "blipScale" => 14, "blipOpacity" => 15,
            "elevation" => 100, "elevationThreshold" => 101, "sweepSpeed" => 102, "sweepOpacity" => 103, "trailSamples" => 104,
            _ => 50
        };
    }
    private static int TypographyPresentationPriority(string path)
    {
        if (path == "/typography/linkKillAssist") return Array.IndexOf(HudTypography.Ids, "combat.kill") * 20 + 19;
        const string prefix = "/typography/styles/";
        if (!path.StartsWith(prefix, StringComparison.Ordinal)) return path switch
        {
            "/typography/useGlobalFont" => 0, "/typography/globalFont" => 1, "/typography/useGameFont" => 2,
            "/typography/gameFont" => 3, "/typography/useInterfaceFont" => 4, "/typography/interfaceFont" => 5, _ => 10000
        };
        string rest = path[prefix.Length..]; int slash = rest.IndexOf('/');
        if (slash < 0) return 10000;
        int style = Array.IndexOf(HudTypography.Ids, rest[..slash]);
        if (style < 0) return 10000;
        string field = rest[(slash + 1)..];
        return style * 20 + (field switch { "enabled" => 0, "font" => 1, "size" => 2, "fitToScreen" => 3,
            "opacity" => 4, "color" => 5, "matchPalette" => 6, "preserveSemanticColors" => 7,
            "outline" => 8, "outlineColor" => 9, _ => 15 });
    }
    private static int DecorationPresentationPriority(string path)
    {
        const string prefix = "/decorations/styles/";
        if (!path.StartsWith(prefix, StringComparison.Ordinal)) return 10000;
        string rest = path[prefix.Length..]; int slash = rest.IndexOf('/');
        if (slash < 0) return 10000;
        int style = Array.IndexOf(HudDecorations.Ids, rest[..slash]);
        if (style < 0) return 10000;
        string field = rest[(slash + 1)..];
        return style * 10 + (field switch { "matchPalette" => 0, "color" => 1, "opacity" => 2, "lineScale" => 3, _ => 8 });
    }
    private static string OptionKind(HudEditorOption option)
        => option.Range!=null ? "Number" : option.Choices.IsEmpty ? IsColorValue(option.Value) ? "Color" : "Text" : "Choice";
    private static bool IsColorValue(string value)
        => value.Length==7&&value[0]=='#'&&uint.TryParse(value.AsSpan(1),NumberStyles.HexNumber,CultureInfo.InvariantCulture,out _);
    private static string PropertyHelp(string path) => path switch
    {
        "/crosshair/motion" => "Static pins the reticle to screen center. Dynamic projects the aim point when fixed-weapon presentation is enabled.",
        "/typography/globalFont" => "Choosing a face also turns on the shared font override.",
        "/typography/gameFont" => "Choosing a face turns on the gameplay font override and disables the shared override.",
        "/typography/interfaceFont" => "Choosing a face turns on the interface font override and disables the shared override.",
        _ when path.StartsWith("/typography/styles/touch.labels/",StringComparison.Ordinal) => "Shared touchscreen control and boost labels.",
        _ when path.StartsWith("/typography/styles/ui.",StringComparison.Ordinal) => "Shared interface text used by menus and settings.",
        _ when path.StartsWith("/typography/styles/game.replay/",StringComparison.Ordinal) => "Shared replay and killcam lettering.",
        _ when path.StartsWith("/typography/styles/match.gameOver/",StringComparison.Ordinal) => "Shared match-end lettering and results.",
        _ when IsTypographyStyleFontPath(path) => "Choosing a face also enables this element's text style.",
        _ => ""
    };
    private static string[] TypographyIdsForElement(int index)
    {
        if(index==8)return new[]{"combat.notifications","combat.kill","combat.assist"};
        if(index>=HudProfileDefaults.MovementTelemetryIndex)return new[]{"all.other"};
        if((uint)index>=HudProfileDefaults.ElementIds.Length)return Array.Empty<string>();
        string id=HudProfileDefaults.ElementIds[index];
        return Array.IndexOf(HudTypography.Ids,id)>=0?new[]{id}:Array.Empty<string>();
    }
    private static string[] DecorationIdsForElement(int index) => index switch
    {
        1 or 12 => new[]{"health"}, 2 or 3 or 13 => new[]{"weapons"}, 4 => new[]{"radar"}, 5 => new[]{"points"}, _ => Array.Empty<string>()
    };
    private static bool IsSelectedTypographyPath(HudProfile profile,string path,string[] styleIds)
    {
        if(path=="/typography/linkKillAssist")return styleIds.Contains("combat.kill",StringComparer.Ordinal);
        const string prefix="/typography/styles/";
        if(!path.StartsWith(prefix,StringComparison.Ordinal))return false;
        string rest=path[prefix.Length..];int slash=rest.IndexOf('/');
        if(slash<0)return false;
        string id=rest[..slash];
        if(!styleIds.Contains(id,StringComparer.Ordinal))return false;
        string field=rest[(slash+1)..];var style=profile.Typography.Get(id);
        if((field=="color"&&style.MatchPalette)||(field=="outlineColor"&&style.Outline<=0))return false;
        return !(field=="font"&&profile.Typography.SharedFontFor(id)!=null);
    }
    private static bool IsHudWideTypographyPath(HudProfile profile,string path)
    {
        if(path is "/typography/useGlobalFont" or "/typography/globalFont")return true;
        if(path is "/typography/useGameFont" or "/typography/gameFont")return !profile.Typography.UseGlobalFont;
        if(path is "/typography/useInterfaceFont" or "/typography/interfaceFont")return !profile.Typography.UseGlobalFont;
        const string prefix="/typography/styles/";
        if(!path.StartsWith(prefix,StringComparison.Ordinal))return false;
        string rest=path[prefix.Length..];int slash=rest.IndexOf('/');
        if(slash<0)return false;
        string id=rest[..slash];
        if(id is not ("touch.labels" or "ui.body" or "ui.headings" or "ui.data" or "game.replay" or "match.gameOver"))return false;
        string field=rest[(slash+1)..];var style=profile.Typography.Get(id);
        if((field=="color"&&style.MatchPalette)||(field=="outlineColor"&&style.Outline<=0))return false;
        return !(field=="font"&&profile.Typography.SharedFontFor(id)!=null);
    }
    private static bool IsSelectedDecorationPath(HudProfile profile,string path,string[] styleIds)
    {
        const string prefix="/decorations/styles/";
        if(!path.StartsWith(prefix,StringComparison.Ordinal))return false;
        string rest=path[prefix.Length..];int slash=rest.IndexOf('/');
        if(slash<=0||!styleIds.Contains(rest[..slash],StringComparer.Ordinal))return false;
        var style=profile.Decorations.Get(rest[..slash]);string field=rest[(slash+1)..];
        if(field=="color"&&style.MatchPalette)return false;
        return field!="lineScale"||rest[..slash] is "borders" or "radar" or "controls";
    }
    private static bool IsHudWideDecorationPath(HudProfile profile,string path)
    {
        const string prefix="/decorations/styles/";
        if(!path.StartsWith(prefix,StringComparison.Ordinal))return false;
        string rest=path[prefix.Length..];int slash=rest.IndexOf('/');
        if(slash<0)return false;
        if(rest[..slash] is not ("borders" or "background" or "controls" or "boost"))return false;
        var style=profile.Decorations.Get(rest[..slash]);string field=rest[(slash+1)..];
        if(field=="color"&&style.MatchPalette)return false;
        return field!="lineScale"||rest[..slash] is "borders" or "controls";
    }
    private bool AppearancePropertyVisible(HudProfile profile,string path)
    {
        CrosshairProfile displayedCrosshair=DisplayedCrosshair(profile);
        if(path=="/nativeReticleOpacity")return displayedCrosshair.Native||profile.Mode==HudMode.Classic;
        if(path.StartsWith("/health/",StringComparison.Ordinal)||path.StartsWith("/ammo/",StringComparison.Ordinal))
        {
            bool health=path.StartsWith("/health/",StringComparison.Ordinal);
            bool gaugeOnly=_selected is 12 or 13;
            // A gauge element edits only the gauge it draws; the parent readout
            // owns the number, icon and native artwork choices.
            var meter=health?profile.Health:profile.Ammo;
            string field=path[(path.LastIndexOf('/')+1)..];
            if(gaugeOnly && field is ("native" or "number" or "numberScale" or "background" or "icon"))return false;
            if(!gaugeOnly&&meter.Native&&field!="native")return false;
            if(field=="numberScale"&&!meter.Number)return false;
            if(field is ("gaugeScale" or "gaugeThickness" or "vertical") && !meter.Gauge)return false;
            if(field is ("warning" or "danger" or "fullColor" or "warningColor" or "dangerColor") && !meter.Gauge)return false;
            return true;
        }
        if(_selected==0&&path.StartsWith("/crosshair/",StringComparison.Ordinal))
        {
            string field=path[(path.LastIndexOf('/')+1)..];
            if(field is "nativeThickness" or "nativeOriginalColors" or "nativeAnimations")return displayedCrosshair.Native;
            if(path.Contains("Style/",StringComparison.OrdinalIgnoreCase))
            {
                string[] fields=path[11..].Split('/');
                var part=(CrosshairPartStyle)typeof(CrosshairProfile).GetProperty(Pascal(fields[0]))!.GetValue(displayedCrosshair)!;
                if(fields[^1]=="color"&&!part.CustomColor)return false;
                if(fields[^1]=="outlineColor"&&!part.CustomOutlineColor)return false;
                if(displayedCrosshair.Native)return false;
            }
            if(displayedCrosshair.Native&&field is ("widthScale" or "heightScale" or "rotation" or "dotStyle" or "innerStyle" or "outerStyle" or "ringStyle" or "bracketStyle"
                or "dot" or "dotShape" or "dotSize" or "inner" or "lengthX" or "lengthY" or "thickness" or "gap" or "tStyle" or "outer" or "outerLength" or "outerGap"
                or "ring" or "radius" or "ringThickness" or "segments" or "brackets" or "bracketCorner" or "bracketLength" or "bracketThickness"))return false;
        }
        return true;
    }
    private static HudEditorNumericRange? PropertyRange(HudProfile profile,string path)
    {
        static HudEditorNumericRange R(float min,float max,float step)=>new(min,max,step);
        if(path.StartsWith("/crosshair/",StringComparison.Ordinal))
        {
            string[] fields=path[11..].Split('/');
            if(fields.Length==1&&CrosshairProperties.All.FirstOrDefault(d=>d.Id==Pascal(fields[0])) is {} descriptor)
            {
                float max=descriptor.Max;
                if(descriptor.Id is "ShotScale" or "ChargeScale" or "ZoomScale" or "NativeThickness")max=Math.Min(max,4);
                return R(descriptor.Min,max,descriptor.Type==typeof(int)?1:descriptor.Step);
            }
            if(fields.Length==2)
            {
                var property=typeof(CrosshairPartStyle).GetProperty(Pascal(fields[1]));
                if(property?.PropertyType==typeof(float))return fields[1]=="outline"?R(-1,20,.1f):fields[1]=="opacity"?R(0,1,.01f):null;
            }
            return null;
        }
        string field=path[(path.LastIndexOf('/')+1)..];
        if(path.StartsWith("/elements/",StringComparison.Ordinal))return field switch
        {"offsetX"=>R(-3840,3840,1),"offsetY"=>R(-2160,2160,1),"scale"=>R(.1f,8,.1f),"opacity"=>R(0,1,.01f),"layer"=>R(0,3,1),_=>null};
        if(path is "/globalScale")return R(.1f,8,.1f);if(path is "/globalOpacity" or "/nativeReticleOpacity")return R(0,1,.01f);
        if(path is "/textScale" or "/iconScale")return R(.5f,3,.05f);if(path=="/safeArea")return R(0,.2f,.01f);
        if(path.StartsWith("/health/",StringComparison.Ordinal)||path.StartsWith("/ammo/",StringComparison.Ordinal))
        {
            if(field=="danger")
            {
                float maximum=path.StartsWith("/health/",StringComparison.Ordinal)?profile.Health.Warning:profile.Ammo.Warning;
                return maximum>0?R(0,maximum,.01f):null;
            }
            return field switch {"numberScale" or "gaugeScale"=>R(.1f,8,.1f),"gaugeThickness"=>R(.1f,20,.1f),"warning"=>R(0,1,.01f),_=>null};
        }
        if(path.StartsWith("/radar/",StringComparison.Ordinal))return field switch
        {"radiusScale"=>R(.1f,4,.1f),"backgroundOpacity" or "blipOpacity" or "sweepOpacity"=>R(0,1,.01f),"outlineThickness"=>R(.1f,8,.1f),"blipScale"=>R(.1f,8,.1f),"elevationThreshold"=>R(.1f,20,.1f),"trailSamples"=>R(0,4,1),"rangeScale"=>R(.5f,1,.01f),"sweepSpeed"=>R(.1f,2,.05f),_=>null};
        if(path.StartsWith("/killFeed/",StringComparison.Ordinal))return field switch {"rows"=>R(1,10,1),"lifetime"=>R(1,10,.1f),"spacing"=>R(8,32,.1f),_=>null};
        if(path.StartsWith("/inventory/",StringComparison.Ordinal))return field switch {"iconScale"=>R(.1f,4,.1f),"spacing"=>R(0,16,.1f),"unownedOpacity"=>R(0,1,.01f),_=>null};
        if(path.StartsWith("/hitMarker/",StringComparison.Ordinal))return field switch {"scale"=>R(.1f,8,.1f),"opacity"=>R(0,1,.01f),"gap"=>R(0,100,.1f),"length"=>R(.1f,100,.1f),"thickness"=>R(.1f,20,.1f),_=>null};
        if(path.StartsWith("/notifications/",StringComparison.Ordinal))return field switch {"spacing"=>R(0,32,.1f),"maxVisible"=>R(1,5,1),_=>null};
        if(path.StartsWith("/typography/styles/",StringComparison.Ordinal))return field switch {"size"=>R(.05f,6,.05f),"opacity"=>R(0,1,.01f),"outline"=>R(0,5,.1f),_=>null};
        if(path.StartsWith("/decorations/styles/",StringComparison.Ordinal))return field switch {"opacity"=>R(0,1,.01f),"lineScale"=>R(.1f,5,.1f),_=>null};
        return null;
    }
    private static string PropertyLabel(string path)
    {
        if(path=="/smoothArtwork")return "Original artwork smoothing";
        if(path=="/textScale")return "Text scale";
        if(path=="/nativeReticleOpacity")return "Original reticle opacity";
        if(path=="/radarBackground")return "Background";
        if(path=="/radarOutlines")return "Frame outlines";
        if(path=="/haloLowHealthSound")return "Low-health alert sound";
        string label=path.Trim('/');
        if(label.StartsWith("elements/",StringComparison.Ordinal))label=label[(label.IndexOf('/',9)+1)..];
        else if(label.StartsWith("decorations/styles/",StringComparison.Ordinal))
        {
            string rest=label[19..];int split=rest.IndexOf('/');
            string id=split<0?rest:rest[..split];int index=Array.IndexOf(HudDecorations.Ids,id);
            label=(index<0?id:HudDecorations.Names[index])+(split<0?"":" · "+rest[(split+1)..]);
        }
        else if(label.StartsWith("typography/styles/",StringComparison.Ordinal))
        {
            string rest=label[18..];int split=rest.IndexOf('/');
            string id=split<0?rest:rest[..split];int index=Array.IndexOf(HudTypography.Ids,id);
            label=(index<0?id:HudTypography.Names[index])+(split<0?"":" · "+rest[(split+1)..]);
        }
        else if(label.StartsWith("typography/",StringComparison.Ordinal))label=label[11..];
        else if(label.StartsWith("crosshair/",StringComparison.Ordinal))label=label[10..];
        else if(label.StartsWith("hitMarker/",StringComparison.Ordinal))label="Hit marker · "+label[10..];
        else if(label.Contains('/'))label=label[(label.IndexOf('/')+1)..];
        int separator=label.IndexOf(" · ",StringComparison.Ordinal);
        label=separator<0?label.Replace('/', ' '):label[..(separator+3)]+label[(separator+3)..].Replace('/', ' ');
        label=System.Text.RegularExpressions.Regex.Replace(label, "(?<=[a-z])([A-Z])", match=>" "+match.Groups[1].Value.ToLowerInvariant());
        int detail=label.IndexOf(" · ",StringComparison.Ordinal)+3;
        if(detail>=3&&detail<label.Length)label=label[..detail]+char.ToUpperInvariant(label[detail])+label[(detail+1)..];
        return label.Length==0?label:char.ToUpperInvariant(label[0])+label[1..];
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
        string field=path[(path.LastIndexOf('/')+1)..];
        if (IsTypographyFontPath(path)) return HudFonts.All.Select(font=>font.Name).ToArray();
        return field switch
        {
            "smoothArtwork" => new[]{"inherit","false","true"}, "mode" => Enum.GetNames<HudMode>(), "motion" => Enum.GetNames<CrosshairMotion>(),
            "weaponCrosshairTheme" => new[]{"", HaloWeaponCrosshairs.Theme}, "anchor" => Enum.GetNames<HudAnchor>(), "visibility" => Enum.GetNames<HudVisibility>(),
            "contexts" => Enum.GetNames<HudContext>(), "dotShape" => Enum.GetNames<CrosshairDotShape>(), "orientation" => Enum.GetNames<HudRadarOrientation>(),
            "outOfRange" => Enum.GetNames<HudRadarOutOfRangeMode>(), "elevation" => Enum.GetNames<HudRadarElevationMode>(), "queue" => Enum.GetNames<HudNotificationQueue>(),
            "shape" => Enum.GetNames<HudHitMarkerShape>(), "style" when path.StartsWith("/radar/",StringComparison.Ordinal) => Enum.GetNames<HudRadarStyle>(), _ => Array.Empty<string>()
        };
    }
    private static bool IsTypographyFontPath(string path)
    {
        if (path is "/typography/globalFont" or "/typography/gameFont" or "/typography/interfaceFont") return true;
        const string stylesPrefix="/typography/styles/";
        return path.StartsWith(stylesPrefix,StringComparison.Ordinal) && path.EndsWith("/font",StringComparison.Ordinal)
            && Array.IndexOf(HudTypography.Ids,path[stylesPrefix.Length..^5])>=0;
    }
    private static bool IsTypographyStyleFontPath(string path)
    {
        const string stylesPrefix="/typography/styles/";
        return path.StartsWith(stylesPrefix,StringComparison.Ordinal) && path.EndsWith("/font",StringComparison.Ordinal)
            && Array.IndexOf(HudTypography.Ids,path[stylesPrefix.Length..^5])>=0;
    }
    private static string FontName(string id)
        => HudFonts.All.FirstOrDefault(font=>font.Id==id)?.Name ?? id;
    private static string FontId(string nameOrId)
    {
        var font=HudFonts.All.FirstOrDefault(candidate=>candidate.Id==nameOrId||candidate.Name==nameOrId);
        return font?.Id ?? throw new FormatException("Choose a listed font.");
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
    private (float Width,float Height) Aspect => _aspect switch { 1 => (3440,1440), 2 => (1440,1080), 3 => (1920,1200), 4 => (1080,1920), 5 => (1800,1080), _ => (1920,1080) };
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
        if(index==HudProfileDefaults.MovementTelemetryIndex) size.X*=Aspect.Width/Aspect.Height*.5625f;
        if(index==4) { size=Vector2.Max(size,new(16));point-=size/2; }
        if(index==11) point.X-=size.X;
        if(index is 6 or 8 || index==HudProfileDefaults.MovementTelemetryIndex || index is 15 or 16 or 17 or 18 or 19 or 23 or 24) point.X-=size.X/2;
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
            if(index!=_selected&&!HudPreviewState.ElementVisible(HudProfileDefaults.ElementIds[index],(HudPreviewScenario)_scenario)) continue;
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
        for(int i=1;i<HudProfileDefaults.ElementIds.Length;i++) if(i!=_selected&&HudPreviewState.ElementVisible(HudProfileDefaults.ElementIds[i],(HudPreviewScenario)_scenario)&&!_selection.Contains(i)&&_history.Draft.Elements[HudProfileDefaults.ElementIds[i]].Enabled)Target(Bounds(i));
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
    private static int Cycle(int index, int count, bool previous) => index < 0 ? (previous ? count-1 : 0) : (index + (previous ? count-1 : 1)) % count;
    private static string Number(float value)=>value.ToString("0.###",CultureInfo.InvariantCulture);
    private void Touch()=>_revision++;
    private void VerifyOwner(){if(_owner!=Environment.CurrentManagedThreadId)throw new InvalidOperationException("HUD authoring belongs to the engine thread.");}
    private void Verify(){VerifyOwner();ObjectDisposedException.ThrowIf(_disposed,this);}
    public void Dispose(){VerifyOwner();if(_disposed)return;ReleaseInput();_accepted=null;_closed=_disposed=true;Touch();}
}

#endif
