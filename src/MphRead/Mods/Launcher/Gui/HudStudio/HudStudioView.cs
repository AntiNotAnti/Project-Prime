using Avalonia.LogicalTree;
using System;
using System.Linq;
using Avalonia.VisualTree;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using MphRead.Mods.Render;
using MphRead.Mods.Render.Hud;
namespace MphRead.Mods.Launcher.Gui;

internal sealed class HudStudioView : UserControl
{
    private readonly HudStudioHistory _history;
    private readonly HudStudioCanvas _canvas;
    private StackPanel _inspector = new() { Spacing = 6, Margin = new Thickness(8) };
    private readonly TextBlock _status = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly TextBox _name = new() { PlaceholderText = "Profile name", MinWidth = 130 };
    private readonly TextBox _json = new() { AcceptsReturn = true, Height = 90, PlaceholderText = "Paste profile JSON here to import" };
    private readonly ComboBox _elements;
    private int _weapon = -1;
    private bool _zoom;
    private int _textTarget;
    private int _navigationLevel;
    private string _category="Gameplay";
    private int _editorSection;
    private bool _letteringOnly;
    private string _fontFilter="All styles", _fontSearch="";
    private string? _textColorTarget, _decorationColorTarget;
    private int _decorationTarget;
    private Action? _refreshTextSample;
    private ScrollViewer? _inspectorScroll;
    private readonly bool _crosshairOnly;
    private readonly Action<HudProfile?> _close;
    private bool _controllerMove;
    public HudStudioView(HudProfile original, Action<HudProfile?> close, bool crosshairOnly=false, Action<HudProfile>? preview=null)
    {
        _close = close;_crosshairOnly=crosshairOnly;
        SetValue(ControllerNav.ModalProperty, true);
        _history = new(original);
        _canvas = new(_history) { CrosshairOnly=crosshairOnly };
        if(crosshairOnly){_canvas.PreviewWidth=768;_canvas.PreviewHeight=576;}
        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), Margin = new Thickness(12) };
        var toolbar = new WrapPanel { Orientation = Orientation.Horizontal };
        toolbar.Children.Add(new TextBlock { Text = crosshairOnly ? "CUSTOM CROSSHAIR" : "CUSTOM HUD", FontSize = 22, Margin = new Thickness(6) });
        var presets = new ComboBox { ItemsSource = HudProfileDefaults.Presets, SelectedIndex = Array.IndexOf(HudProfileDefaults.Presets, original.Name), MinWidth = 150 };
        presets.SelectionChanged += (_, _) => { if (presets.SelectedItem is string preset) { _history.Replace(HudProfileDefaults.Create(preset)); _name.Text = preset; Refresh(); } };
        // Presets live in their own navigable HUD section.
        Button(toolbar, "Undo", () => { _history.Undo(); Refresh(); });
        Button(toolbar, "Redo", () => { _history.Redo(); Refresh(); });
        if(!crosshairOnly)
        {
        Button(toolbar, "Lock all", () => { _history.Edit(p => { foreach (var element in p.Elements.Values) element.Locked = true; }); Refresh(); });
        Button(toolbar, "Unlock all", () => { _history.Edit(p => { foreach (var element in p.Elements.Values) element.Locked = false; }); Refresh(); });
        Button(toolbar, "Align left", () => _canvas.AlignSelection(true));
        Button(toolbar, "Align top", () => _canvas.AlignSelection(false));

        }
        Button(toolbar,crosshairOnly?"Reset crosshair":"Reset HUD",()=> { if(crosshairOnly)Edit(p=>p.ResetElement("core.crosshair"));else _history.Replace(HudProfileDefaults.Create(_history.Draft.BasePreset));Refresh(); });
        Button(toolbar, "Use in settings", () =>
        {
            try { var p = _history.Draft.DeepClone(); p.Name = _name.Text ?? "Custom"; if (p.Mode == HudMode.Custom && Array.IndexOf(HudProfileDefaults.Presets,p.Name) >= 0) p.Name += " (Custom)"; p.Validate(); close(p); }
            catch (Exception ex) { _status.Text = ex.Message; }
        });
        if(preview!=null) Button(toolbar,"Preview 60s",()=>Try(()=>preview(_history.Draft.DeepClone())));
        Button(toolbar, "Cancel", () => close(null));
        root.Children.Add(toolbar);
        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*") };
        Grid.SetRow(body, 1); root.Children.Add(body); body.Children.Add(_canvas);
        var scroll = new ScrollViewer { Content = _inspector }; _inspectorScroll=scroll; Grid.SetColumn(scroll, 1); body.Children.Add(scroll);
        var footer = new StackPanel { Spacing = 6 };
        var controls = new WrapPanel();
        _elements = new ComboBox { ItemsSource = crosshairOnly ? new[]{"core.crosshair"} : HudProfileDefaults.ElementIds, SelectedIndex = 0, MinWidth = 180 };
        _elements.SelectionChanged += (_, _) => { _canvas.Selected = Math.Max(0, _elements.SelectedIndex); SyncSelectedElement(); Refresh(); };
        controls.Children.Add(_elements);
        var aspects = new ComboBox { ItemsSource = new[] { "16:9", "21:9", "4:3", "16:10", "Android portrait", "5:3" }, SelectedIndex = Mods.Input.DsLayout.Enabled ? (Mods.Input.DsLayout.ScreenAspect == "5:3" ? 5 : 2) : crosshairOnly?2:0, MinWidth = 130 };
        (_canvas.PreviewWidth, _canvas.PreviewHeight) = aspects.SelectedIndex switch { 1 => (3440,1440), 2 => (1440,1080), 3 => (1920,1200), 4 => (1080,1920), 5 => (1800,1080), _ => (1920,1080) };
        aspects.SelectionChanged += (_, _) =>
        {
            (_canvas.PreviewWidth, _canvas.PreviewHeight) = aspects.SelectedIndex switch { 1 => (3440,1440), 2 => (1440,1080), 3 => (1920,1200), 4 => (1080,1920), 5 => (1800,1080), _ => (1920,1080) }; _canvas.InvalidateVisual();
        };
        controls.Children.Add(aspects);
        var nativeHunter=new ComboBox { ItemsSource=new[] { "Samus", "Kanden", "Trace", "Sylux", "Noxus", "Spire", "Weavel", "Guardian" },SelectedIndex=0,MinWidth=100 };
        ToolTip.SetTip(nativeHunter,"Native preview hunter; matches use your current hunter's assets.");
        nativeHunter.SelectionChanged+=(_,_)=> { _canvas.PreviewHunter=Math.Max(0,nativeHunter.SelectedIndex);_canvas.Refresh(); };
        controls.Children.Add(nativeHunter);
        var scenario = new ComboBox { ItemsSource = Enum.GetNames<HudPreviewScenario>(), SelectedIndex = 0, MinWidth = 120 };
        scenario.SelectionChanged += (_,_) => { _canvas.Scenario=(HudPreviewScenario)scenario.SelectedIndex; _canvas.Zoom=_canvas.Scenario==HudPreviewScenario.Zoomed; _canvas.Refresh(); };
        controls.Children.Add(scenario);
        var grid = new ComboBox { ItemsSource = new[] { "Grid off", "1", "2", "4", "8", "16" }, SelectedIndex = 4 };
        grid.SelectionChanged += (_, _) => { _canvas.GridSize = grid.SelectedIndex == 0 ? 0 : 1 << (grid.SelectedIndex-1); _canvas.InvalidateVisual(); };
        var guides = new CheckBox { Content = "Snap guides", IsChecked = true, Margin = new Thickness(6) };
        guides.IsCheckedChanged += (_, _) => _canvas.SnapGuides = guides.IsChecked == true;
        if(!crosshairOnly)controls.Children.Add(guides);
        if(!crosshairOnly)controls.Children.Add(grid); footer.Children.Add(controls); footer.Children.Add(_status);
        Grid.SetRow(footer, 2); root.Children.Add(footer); Content = root;
        _canvas.Changed += () => { _elements.SelectedIndex = _canvas.Selected; SyncSelectedElement(); Refresh(); };
        _name.Text = original.Name;
        SizeChanged += (_, e) =>
        {
            bool narrow = e.NewSize.Width < 700;
            body.ColumnDefinitions = new ColumnDefinitions(narrow ? "*" : "*,*");
            body.RowDefinitions = new RowDefinitions(narrow ? "*,220" : "*");
            Grid.SetColumn(scroll, narrow ? 0 : 1); Grid.SetRow(scroll, narrow ? 1 : 0);
        };
        Refresh();
    }
    private readonly MphRead.Mods.Input.GamepadEdges _editEdges = new();
    private long _padRevision=-1, _padTime;
    private bool _padNeutral;
    private string? _padBefore;
    private void EndControllerGesture()
    { if(_padBefore!=null) { _history.Commit(_padBefore); _padBefore=null; Refresh(); } }
    internal void HandleControllerAxes(MphRead.Mods.Input.GamepadSnapshot snapshot,long now)
    {
        var state=snapshot.State;
        var pressed=_editEdges.Update(snapshot);
        if(snapshot.Revision!=_padRevision || !state.Connected)
        { EndControllerGesture(); _padRevision=snapshot.Revision; _padNeutral=false; _padTime=now; return; }
        float dt=Math.Clamp((now-_padTime)/1000f,0,.05f); _padTime=now;
        bool moving=Math.Abs(state.LeftX)>.25f || Math.Abs(state.LeftY)>.25f || Math.Abs(state.RightX)>.25f || Math.Abs(state.RightY)>.25f;
        if(!_padNeutral) { if(!moving && state.Buttons==0) _padNeutral=true; return; }
        string id=HudProfileDefaults.ElementIds[_canvas.Selected];
        if((pressed&MphRead.Mods.Input.GamepadButtons.X)!=0)
        { EndControllerGesture(); Edit(p=>p.ResetElement(id)); Refresh(); }
        if((pressed&MphRead.Mods.Input.GamepadButtons.Y)!=0)
        { EndControllerGesture(); Edit(p=>p.Elements[id].Enabled=!p.Elements[id].Enabled); Refresh(); }
        var element=_history.Draft.Elements[id];
        if(!moving || element.Locked) { EndControllerGesture(); return; }
        _padBefore ??= _history.Capture();
        float Axis(float value)=>Math.Abs(value)>.25f ? value : 0;
        if(_canvas.Selected!=0)
        { element.OffsetX+=(Axis(state.LeftX)*180+Axis(state.RightX)*20)*dt; element.OffsetY-=Axis(state.LeftY)*180*dt; }
        element.Scale=Math.Clamp(element.Scale+Axis(state.RightY)*dt,.1f,8);
        _history.Draft.Mode=HudMode.Custom; _canvas.Refresh();
    }
    internal bool HandleController(MphRead.Mods.Input.UiAction action)
    {
        EndControllerGesture();
        var openChoice = this.GetVisualDescendants().OfType<ComboBox>().FirstOrDefault(c => c.IsDropDownOpen);
        if (openChoice != null)
        {
            if (action == MphRead.Mods.Input.UiAction.Back) { openChoice.IsDropDownOpen=false; return true; }
            if (action is MphRead.Mods.Input.UiAction.Up or MphRead.Mods.Input.UiAction.Down or MphRead.Mods.Input.UiAction.Accept)
            {
                FocusNavigator.Key(openChoice, action == MphRead.Mods.Input.UiAction.Up ? Avalonia.Input.Key.Up : action == MphRead.Mods.Input.UiAction.Down ? Avalonia.Input.Key.Down : Avalonia.Input.Key.Enter);
                return true;
            }
        }
        switch (action)
        {
            case MphRead.Mods.Input.UiAction.Back:
                if (_controllerMove) { _controllerMove = false; _elements.Focus(); }
                else _close(null);
                return true;
            case MphRead.Mods.Input.UiAction.PreviousTab:
            case MphRead.Mods.Input.UiAction.NextTab:
                _elements.SelectedIndex = (_canvas.Selected + (action == MphRead.Mods.Input.UiAction.NextTab ? 1 : HudProfileDefaults.ElementIds.Length - 1)) % HudProfileDefaults.ElementIds.Length;
                return true;
            case MphRead.Mods.Input.UiAction.Accept when _canvas.IsFocused:
                _controllerMove = !_controllerMove;
                _status.Text = _controllerMove ? "Move mode: stick / D-pad moves; triggers resize; B returns to controls." : "LB / RB selects an element. A on canvas enters move mode.";
                return true;
        }
        if (!_controllerMove) return false;
        if (action is MphRead.Mods.Input.UiAction.PageUp or MphRead.Mods.Input.UiAction.PageDown)
        {
            string id = HudProfileDefaults.ElementIds[_canvas.Selected];
            if (!_history.Draft.Elements[id].Locked) Edit(p => p.Elements[id].Scale += action == MphRead.Mods.Input.UiAction.PageUp ? -.1f : .1f);
        }
        else
        {
            var key = action switch { MphRead.Mods.Input.UiAction.Left => Avalonia.Input.Key.Left, MphRead.Mods.Input.UiAction.Right => Avalonia.Input.Key.Right, MphRead.Mods.Input.UiAction.Up => Avalonia.Input.Key.Up, MphRead.Mods.Input.UiAction.Down => Avalonia.Input.Key.Down, _ => Avalonia.Input.Key.None };
            if (key != Avalonia.Input.Key.None) { FocusNavigator.Key(_canvas, key); _history.Draft.Mode = HudMode.Custom; }
        }
        return true;
    }
    private static void Button(Panel panel, string text, Action action)
    {
        var button = new PrimeButton(text) { Margin = new Thickness(3), MinHeight = 32 };
        button.Click += (_, _) => action(); panel.Children.Add(button);
    }
    private void Edit(Action<HudProfile> edit)
    {
        _history.Edit(p => { edit(p); p.Mode = HudMode.Custom; }); _canvas.Refresh(); _refreshTextSample?.Invoke();
    }
    private void Number(string name, float value, float min, float max, Action<HudProfile, float> set, bool integer=false)
    {
        var row=new DecimalSliderRow(name,value,min,max,integer?1:.01f);
        row.ValueChanged+=(_,_)=>Edit(p=>set(p,row.Value));
        _inspector.Children.Add(row);
    }
    private void Toggle(string name, bool value, Action<HudProfile, bool> set)
    {
        var field = new CheckBox { Content = name, IsChecked = value };
        field.IsCheckedChanged += (_, _) => Edit(p => set(p, field.IsChecked == true)); _inspector.Children.Add(field);
    }
    private CrosshairProfile CrosshairOf(HudProfile p)
    {
        CrosshairProfile Inherited()
        {
            var inherited=HudProfileStore.Parse(HudProfileStore.Serialize(p)).Crosshair;
            inherited.OverrideProperties=Array.Empty<string>(); return inherited;
        }
        if (_zoom) return p.ZoomCrosshair ??= Inherited();
        if (_weapon >= 0) return p.WeaponCrosshairs[_weapon] ??= Inherited();
        return p.Crosshair;
    }
    private void SyncSelectedElement()
    {
        string id = HudProfileDefaults.ElementIds[_canvas.Selected];
        string lettering=id switch { "core.healthGauge"=>"core.health", "core.ammoGauge"=>"core.ammo", _=>id };
        int index = Array.IndexOf(HudTypography.Ids, lettering);
        if (index >= 0) _textTarget = index;
        _textColorTarget = null;
        _navigationLevel = 2; _editorSection = id=="core.crosshair"?3:0; _letteringOnly=false;
        string decoration=id switch { "match.score"=>"points", "core.health" or "core.healthGauge"=>"health", "core.radar"=>"radar", "core.weapons" or "core.ammo" or "core.ammoGauge"=>"weapons", _=>"borders" };
        _decorationTarget=Array.IndexOf(HudDecorations.Ids,decoration);
    }
    private void Refresh()
    {
        var expanded=_inspector.GetVisualDescendants().OfType<Expander>().Where(e=>e.Header is string).GroupBy(e=>(string)e.Header!).ToDictionary(g=>g.Key,g=>g.First().IsExpanded);
        var offset=_inspectorScroll?.Offset??default;
        _refreshTextSample=null;
        _canvas.Refresh(); _inspector.Children.Clear();
        if (!_crosshairOnly)
        {
            Button(_inspector, "‹ Back", () => { _navigationLevel=Math.Max(0,_navigationLevel-1);Refresh(); });
            _inspector.Children.Add(new TextBlock { Text=_navigationLevel==0?"Edit HUD":_category+" / "+(_navigationLevel>1?HudTypography.Names[_textTarget]:"Elements"),FontSize=18,TextWrapping=Avalonia.Media.TextWrapping.Wrap });
            if (_navigationLevel == 0)
            {
                foreach(string category in new[]{"Preset themes","Gameplay","Combat","Touchscreen","Interface","Palette and profiles"})
                    Button(_inspector,category,()=>{_category=category;_navigationLevel=1;Refresh();});
                return;
            }
            if (_navigationLevel == 1)
            {
                if (_category == "Preset themes")
                {
                    _inspector.Children.Add(new TextBlock { Text="Prebuilt themes change HUD styling in this draft. Apply/Cancel and gameplay Preview still control saving.",TextWrapping=Avalonia.Media.TextWrapping.Wrap });
                    foreach(string theme in HudProfileDefaults.Presets)
                        Button(_inspector,"Use "+theme+" theme",()=> { bool audio=_history.Draft.HaloLowHealthSound;var next=HudProfileDefaults.Create(theme);next.HaloLowHealthSound=audio;_history.Replace(next);_name.Text=theme;Refresh(); });
                    _inspector.Children.Add(new Separator());
                    _inspector.Children.Add(new TextBlock { Text="Halo theme audio",FontSize=18 });
                    Toggle("Halo low-health warning sound",_history.Draft.HaloLowHealthSound,(p,v)=>p.HaloLowHealthSound=v);
                    _inspector.Children.Add(new TextBlock { Text="Loops while alive at 99 HP or below. Stops above 99 HP, on death, in menus and while spectating. Uses game/notification volume. Works with every theme.",TextWrapping=Avalonia.Media.TextWrapping.Wrap });
                    return;
                }

                if (_category == "Palette and profiles")
                    Toggle("Filter original HUD artwork", _history.Draft.SmoothArtwork ?? Mods.RenderOptions.SmoothNativeHud, (p,v)=>p.SmoothArtwork=v);
                if (_category == "Palette and profiles")
                    Button(_inspector,"Global palette, sizing and profiles",()=>{_navigationLevel=2;_editorSection=4;Refresh();});
                else for(int i=0;i<HudTypography.Ids.Length;i++)
                {
                    int target=i;string key=HudTypography.Ids[i];
                    bool show=_category switch { "Combat"=>key.StartsWith("combat.")||key=="match.gameOver", "Touchscreen"=>key.StartsWith("touch.")||key=="core.radar", "Interface"=>key.StartsWith("ui.")||key=="all.other", _=>!key.StartsWith("combat.")&&!key.StartsWith("touch.")&&!key.StartsWith("ui.")&&key!="all.other"&&key!="match.gameOver" };
                    if(show)Button(_inspector,HudTypography.Names[i],()=>{_textTarget=target;int element=Array.IndexOf(HudProfileDefaults.ElementIds,key);if(element<0 && key is "combat.kill" or "combat.assist")element=8;
                    if(element>=0){_canvas.Selected=element;_elements.SelectedIndex=element;SyncSelectedElement();}
                    _letteringOnly=element<0;_textTarget=target;_navigationLevel=2;_editorSection=0;Refresh();});
                }
                Button(_inspector,"Crosshair",()=>{_canvas.Selected=0;_elements.SelectedIndex=0;_navigationLevel=2;_editorSection=3;Refresh();});
                return;
            }
            var sections=new ComboBox { ItemsSource=new[]{"Lettering and fonts","Decorative colors","Position and size","Element behavior","Palette and profiles"},SelectedIndex=_editorSection };
            sections.SelectionChanged+=(_,_)=>{if(sections.SelectedIndex>=0){_editorSection=sections.SelectedIndex;Refresh();}};
            _inspector.Children.Add(sections);
            if(_canvas.Selected==0 && !_letteringOnly && _editorSection is 0 or 1 or 2)
            { _inspector.Children.Add(new TextBlock { Text="Crosshair size, thickness, shape and colors are under Element behavior / Custom Crosshair.",TextWrapping=Avalonia.Media.TextWrapping.Wrap });return; }
            if(_letteringOnly && _editorSection is 1 or 2 or 3)
            { _inspector.Children.Add(new TextBlock { Text="This lettering uses its existing gameplay anchor. Edit font, size, color, opacity and outline under Lettering and fonts.",TextWrapping=Avalonia.Media.TextWrapping.Wrap });return; }
        }
        int contentStart=_inspector.Children.Count;
        if(!_crosshairOnly) { TypographyInspector(); }
        int typographyEnd=_inspector.Children.Count;
        if(!_crosshairOnly) { DecorationInspector(); }
        int decorationEnd=_inspector.Children.Count;
        var p = _history.Draft; string id = HudProfileDefaults.ElementIds[_canvas.Selected]; var e = p.Elements[id];
        _inspector.Children.Add(new TextBlock { Text = id, FontSize = 18 });
        if(_canvas.Selected!=0)
        {
        Toggle("Visible", e.Enabled, (p,v) => p.Elements[id].Enabled=v);
        Toggle("Locked", e.Locked, (p,v) => p.Elements[id].Locked=v);
        foreach (var context in new[] { HudContext.Playing,HudContext.SpectatorPov,HudContext.SpectatorFree,HudContext.Replay })
            Toggle(context.ToString(),(e.Contexts & context)!=0,(p,v)=>p.Elements[id].Contexts=v ? p.Elements[id].Contexts|context : p.Elements[id].Contexts&~context);
        var visibility = new ComboBox { ItemsSource = Enum.GetNames<HudVisibility>(), SelectedIndex = (int)e.Visibility };
        visibility.SelectionChanged += (_, _) => Edit(p => p.Elements[id].Visibility = (HudVisibility)visibility.SelectedIndex);
        _inspector.Children.Add(visibility);
        if (_canvas.Selected != 0)
        {
            var anchor = new ComboBox { ItemsSource = Enum.GetNames<HudAnchor>(), SelectedIndex = (int)e.Anchor };
            anchor.SelectionChanged += (_, _) => Edit(p => p.Elements[id].Anchor=(HudAnchor)anchor.SelectedIndex);
            _inspector.Children.Add(anchor);
            Number("X", e.OffsetX, -3840,3840, (p,v) => p.Elements[id].OffsetX=v);
            Number("Y", e.OffsetY, -2160,2160, (p,v) => p.Elements[id].OffsetY=v);
        }
        else _inspector.Children.Add(new TextBlock { Text = "Crosshair follows the existing aim center.", TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        if(_canvas.Selected!=0)
        {
            Number("Scale",e.Scale,.1f,8,(p,v)=>p.Elements[id].Scale=v);
            Number("Opacity",e.Opacity,0,1,(p,v)=>p.Elements[id].Opacity=v);
            ColorField("Tint",e.Color,(p,v)=> {
                p.Elements[id].Color=v;
                if(id=="combat.notifications")foreach(string text in new[]{"combat.notifications","combat.kill","combat.assist"})
                {var style=p.Typography.Get(text);style.Enabled=true;style.MatchPalette=false;style.Color=v;}
            });
        }
        Button(_inspector,"Reset element",() => { Edit(p => p.ResetElement(id)); Refresh(); });
        Button(_inspector,"Reset section",() =>
        {
            string prefix=id.Split('.')[0]+".";
            Edit(p=> { foreach (string elementId in HudProfileDefaults.ElementIds) if(elementId.StartsWith(prefix,StringComparison.Ordinal)) p.ResetElement(elementId); }); Refresh();
        });
        }
        int layoutEnd=_inspector.Children.Count;
        if (_canvas.Selected == 8)
        {
            Number("Spacing",p.Notifications.Spacing,0,32,(p,v)=>p.Notifications.Spacing=v);
            Number("Max notifications",p.Notifications.MaxVisible,1,5,(p,v)=>p.Notifications.MaxVisible=(int)v);
            Toggle("Only latest",p.Notifications.Queue==HudNotificationQueue.Latest,(p,v)=>p.Notifications.Queue=v ? HudNotificationQueue.Latest : HudNotificationQueue.Stack);
            Toggle("Fade notifications",p.Notifications.Fade,(p,v)=>p.Notifications.Fade=v);
        }
        if (_canvas.Selected == 0) CrosshairInspector();
        if (_canvas.Selected is 1 or 2 or 12 or 13)
        {
            Toggle("Independent gauges",p.IndependentGauges,(p,v)=> { if(v) p.DetachGauges(); else p.IndependentGauges=false; });
            MeterInspector(_canvas.Selected is 1 or 12);
        }
        if (_canvas.Selected == 7)
        {
            Number("Maximum rows",p.KillFeed.Rows,1,10,(p,v)=>p.KillFeed.Rows=(int)v);
            Number("Lifetime (sec)",p.KillFeed.Lifetime,1,10,(p,v)=>p.KillFeed.Lifetime=v);
            Number("Row spacing",p.KillFeed.Spacing,8,32,(p,v)=>p.KillFeed.Spacing=v);
            Toggle("Weapon",p.KillFeed.Weapon,(p,v)=>p.KillFeed.Weapon=v);
            Toggle("Headshot",p.KillFeed.Headshot,(p,v)=>p.KillFeed.Headshot=v);
            Toggle("Team kill",p.KillFeed.TeamKill,(p,v)=>p.KillFeed.TeamKill=v);
        }
        if (_canvas.Selected == 3)
        {
            Toggle("Horizontal",p.Inventory.Horizontal,(p,v)=>p.Inventory.Horizontal=v);
            Toggle("Show unowned",p.Inventory.ShowUnowned,(p,v)=>p.Inventory.ShowUnowned=v);
            Toggle("Show ammo",p.Inventory.ShowAmmo,(p,v)=>p.Inventory.ShowAmmo=v);
            Toggle("Native equipped-weapon icon",p.Inventory.Native,(p,v)=>p.Inventory.Native=v);
            Toggle("Selected outline",p.Inventory.SelectedOutline,(p,v)=>p.Inventory.SelectedOutline=v);
            Number("Icon scale",p.Inventory.IconScale,.1f,4,(p,v)=>p.Inventory.IconScale=v);
            Number("Spacing",p.Inventory.Spacing,0,16,(p,v)=>p.Inventory.Spacing=v);
            Number("Unowned opacity",p.Inventory.UnownedOpacity,0,1,(p,v)=>p.Inventory.UnownedOpacity=v);
            ColorField("Selected",p.Inventory.SelectedColor,(p,v)=>p.Inventory.SelectedColor=v);
        }
        if (_canvas.Selected == 4)
        {
            void Group(string title) => _inspector.Children.Add(new TextBlock { Text=title,FontSize=16,Margin=new Thickness(0,12,0,4) });
            void Choice<T>(string title,T selected,Action<HudProfile,T> set) where T : struct,Enum
            {
                _inspector.Children.Add(new TextBlock { Text=title });
                var choices=Enum.GetValues<T>();
                var combo=new ComboBox { ItemsSource=Enum.GetNames<T>(),SelectedIndex=Array.IndexOf(choices,selected) };
                combo.SelectionChanged+=(_,_)=> { if(combo.SelectedIndex>=0) { Edit(p=>set(p,choices[combo.SelectedIndex])); Refresh(); } };
                _inspector.Children.Add(combo);
            }
            Group("Radar presets");
            var preset=new ComboBox { ItemsSource=Enum.GetNames<HudRadarStyle>(),PlaceholderText="Apply radar preset" };
            preset.SelectionChanged+=(_,_)=> { if(preset.SelectedIndex>=0) { Edit(p=>HudRadarStyles.Apply(p,(HudRadarStyle)preset.SelectedIndex)); Refresh(); } };
            _inspector.Children.Add(preset);
            Group("Appearance");
            Choice("Style",p.Radar.Style,(p,v)=>p.Radar.Style=v);
            Toggle("Radar background",p.RadarBackground,(p,v)=>p.RadarBackground=v);
            Toggle("Radar outlines",p.RadarOutlines,(p,v)=>p.RadarOutlines=v);
            Number("Radar size (%)",p.Radar.RadiusScale*100,10,400,(p,v)=>p.Radar.RadiusScale=v/100);
            Number("Background alpha",p.Radar.BackgroundOpacity,0,1,(p,v)=>p.Radar.BackgroundOpacity=v);
            Number("Outline width",p.Radar.OutlineThickness,.1f,8,(p,v)=>p.Radar.OutlineThickness=v);
            Toggle("Range rings",p.Radar.RangeRings,(p,v)=>p.Radar.RangeRings=v);
            Number("Blip size",p.Radar.BlipScale,.1f,8,(p,v)=>p.Radar.BlipScale=v);
            Number("Blip opacity",p.Radar.BlipOpacity,0,1,(p,v)=>p.Radar.BlipOpacity=v);
            Group("Orientation");
            Choice("Orientation",p.Radar.Orientation,(p,v)=>p.Radar.Orientation=v);
            Toggle("Cardinal labels",p.Radar.Cardinals,(p,v)=>p.Radar.Cardinals=v);
            Toggle("Hunter facing",p.Radar.HunterFacing,(p,v)=>p.Radar.HunterFacing=v);
            Choice("Elevation",p.Radar.Elevation,(p,v)=>p.Radar.Elevation=v);
            if(p.Radar.Elevation!=HudRadarElevationMode.Off) Number("Elevation threshold",p.Radar.ElevationThreshold,.1f,20,(p,v)=>p.Radar.ElevationThreshold=v);
            Group("Contacts");
            Toggle("Hunter contacts",p.Radar.Hunters,(p,v)=>p.Radar.Hunters=v);
            Toggle("Weapon contacts",p.Radar.Weapons,(p,v)=>p.Radar.Weapons=v);
            Toggle("Powerup contacts",p.Radar.Powerups,(p,v)=>p.Radar.Powerups=v);
            Toggle("Objective contacts",p.Radar.Objectives,(p,v)=>p.Radar.Objectives=v);
            Choice("Out of range",p.Radar.OutOfRange,(p,v)=>p.Radar.OutOfRange=v);
            Number("Radar range",p.Radar.RangeScale,.5f,1,(p,v)=>p.Radar.RangeScale=v);
            Group("Motion");
            Number("Trail samples",p.Radar.TrailSamples,0,4,(p,v)=>p.Radar.TrailSamples=(int)v,integer:true);
            if(p.Radar.Style==HudRadarStyle.Scanner)
            {
                Number("Sweep speed",p.Radar.SweepSpeed,.1f,2,(p,v)=>p.Radar.SweepSpeed=v);
                Number("Sweep opacity",p.Radar.SweepOpacity,0,1,(p,v)=>p.Radar.SweepOpacity=v);
            }
        }
        int behaviorEnd=_inspector.Children.Count;
        if(_canvas.Selected!=0)
        {
        _inspector.Children.Add(new Separator());
        ColorField("HUD palette",p.AccentColor,(p,v)=>p.AccentColor=v);
        var palette=new ComboBox { ItemsSource=HudPalettes.Names,PlaceholderText="Palette" };
        palette.SelectionChanged += (_,_)=> { if(palette.SelectedIndex>=0) { Edit(p=>HudPalettes.Apply(p,palette.SelectedIndex)); Refresh(); } };
        _inspector.Children.Add(palette);
        Number("Global scale", p.GlobalScale,.1f,8,(p,v)=>p.GlobalScale=v);
        Number("Text scale",p.TextScale,.5f,3,(p,v)=>p.TextScale=v);
        Number("Icon scale",p.IconScale,.5f,3,(p,v)=>p.IconScale=v);
        Toggle("Reduce motion",p.ReduceMotion,(p,v)=>p.ReduceMotion=v);
        Toggle("Reduce transparency",p.ReduceTransparency,(p,v)=>p.ReduceTransparency=v);
        Number("Global opacity",p.GlobalOpacity,0,1,(p,v)=>p.GlobalOpacity=v);
        Number("Safe area",p.SafeArea,0,.2f,(p,v)=>p.SafeArea=v);
        }
        _inspector.Children.Add(_name);
        Button(_inspector,"Save named profile",() => Try(() => { var copy=p.DeepClone(); copy.Name=_name.Text ?? "Custom"; HudProfiles.SaveNamed(copy.Name,copy); _status.Text="Named profile saved. Use in settings, then Apply to activate."; }));
        Button(_inspector,"Duplicate profile",()=> { Edit(p=>p.Name="Copy of "+p.Name);_name.Text=_history.Draft.Name;Refresh(); });
        Button(_inspector,"Load named profile",() => Try(() => { var loaded=HudProfiles.LoadNamed(_name.Text ?? "");if(_crosshairOnly)Edit(p=>{p.Crosshair=loaded.Crosshair;p.WeaponCrosshairs=loaded.WeaponCrosshairs;p.ZoomCrosshair=loaded.ZoomCrosshair;p.WeaponCrosshairTheme=loaded.WeaponCrosshairTheme;});else _history.Replace(loaded);Refresh(); }));
        _inspector.Children.Add(_json);
        Button(_inspector,"Export JSON",() => { _json.Text = HudProfileStore.Serialize(_history.Draft); _status.Text="Select and copy the JSON to share your profile."; });
        Button(_inspector,"Import JSON",() => Try(() => { _history.Replace(HudProfileStore.Parse(_json.Text ?? "")); _name.Text=_history.Draft.Name; Refresh(); }));
        if(!_crosshairOnly)
        {
            int end=_inspector.Children.Count;
            (int first,int last)=_editorSection switch { 0=>(contentStart,typographyEnd),1=>(typographyEnd,decorationEnd),2=>(decorationEnd,layoutEnd),3=>(layoutEnd,behaviorEnd),_=>(behaviorEnd,end) };
            for(int i=end-1;i>=contentStart;i--)if(i<first||i>=last)_inspector.Children.RemoveAt(i);
        }
        foreach(var expander in _inspector.GetLogicalDescendants().OfType<Expander>())
            if(expander.Header is string header && expanded.TryGetValue(header,out bool open))expander.IsExpanded=open;
        if(_inspectorScroll!=null)Avalonia.Threading.Dispatcher.UIThread.Post(()=>_inspectorScroll.Offset=offset);
        _status.Text = _crosshairOnly ? "Preview uses the same geometry and original sprite assets as gameplay. Select weapon and state below; sizes use physical pixels for custom geometry." : "Drag to move • Arrows to nudge • Shift: 10 units / no snap • Ctrl+Z/Y: undo/redo. Crosshair, gauge and radar-frame geometry is shared; other previews are schematic. Shift-click selects a group.";
    }
    private void Try(Action action) { try { action(); } catch (Exception ex) { _status.Text=ex.Message; } }
    private void MeterInspector(bool health)
    {
        HudMeterProfile Meter(HudProfile p) => health ? p.Health : p.Ammo;
        var m = Meter(_history.Draft);
        Toggle("Use native meter",m.Native,(p,v)=>Meter(p).Native=v);
        _inspector.Children.Add(new TextBlock { Text="Native meters use original artwork and colors. Resize with the element Scale above; the style controls below apply to modern meters.",TextWrapping=Avalonia.Media.TextWrapping.Wrap });
        Toggle("Number",m.Number,(p,v)=>Meter(p).Number=v);
        Toggle("Gauge",m.Gauge,(p,v)=>Meter(p).Gauge=v);
        Toggle("Background",m.Background,(p,v)=>Meter(p).Background=v);
        if (!health) Toggle("Weapon icon",m.Icon,(p,v)=>Meter(p).Icon=v);
        Toggle("Vertical gauge",m.Vertical,(p,v)=>Meter(p).Vertical=v);
        Number("Number scale",m.NumberScale,.1f,8,(p,v)=>Meter(p).NumberScale=v);
        Number("Gauge length",m.GaugeScale,.1f,8,(p,v)=>Meter(p).GaugeScale=v);
        Number("Gauge thickness",m.GaugeThickness,.1f,20,(p,v)=>Meter(p).GaugeThickness=v);
        Number("Warning level",m.Warning,0,1,(p,v)=>Meter(p).Warning=v);
        Number("Danger level",m.Danger,0,1,(p,v)=>Meter(p).Danger=v);
        ColorField("Full",m.FullColor,(p,v)=>Meter(p).FullColor=v);
        ColorField("Warning",m.WarningColor,(p,v)=>Meter(p).WarningColor=v);
        ColorField("Danger",m.DangerColor,(p,v)=>Meter(p).DangerColor=v);
    }
    private void DecorationInspector()
    {
        var panel=new StackPanel { Spacing=6 };
        _inspector.Children.Add(new Expander { Header="Screen and control colors",Content=panel });
        var target=new ComboBox { ItemsSource=HudDecorations.Names,SelectedIndex=_decorationTarget,HorizontalAlignment=HorizontalAlignment.Stretch };
        panel.Children.Add(target);
        target.SelectionChanged+=(_,_)=> { if(target.SelectedIndex>=0){_decorationTarget=target.SelectedIndex;Refresh();} };
        var parent=_inspector;_inspector=panel;
        string id=HudDecorations.Ids[_decorationTarget];_decorationColorTarget=id;var style=_history.Draft.Decorations.Get(id);
        Toggle("Follow HUD palette",style.MatchPalette,(p,v)=>p.Decorations.Get(id).MatchPalette=v);
        ColorField("Independent",style.Color,(p,v)=>{p.Decorations.Get(id).Color=v;p.Decorations.Get(id).MatchPalette=false;});
        Number("Opacity",style.Opacity,0,1,(p,v)=>p.Decorations.Get(id).Opacity=v);
        if(id is "borders" or "radar" or "controls")Number("Line thickness multiplier",style.LineScale,.1f,5,(p,v)=>p.Decorations.Get(id).LineScale=v);
        Button(panel,"Reset decoration",()=>{Edit(p=>p.Decorations.Styles[id]=new());Refresh();});
        _inspector=parent;_decorationColorTarget=null;
    }
    private void TypographyInspector()
    {
        var section=new StackPanel { Spacing=6 };
        var host=new Expander { Header="Fonts, lettering and numbers",Content=section,IsExpanded=true };
        _inspector.Children.Add(host);
        var shared=new StackPanel { Spacing=5 };
        section.Children.Add(new Expander { Header="Shared fonts",Content=shared,IsExpanded=false });
        var global=new CheckBox { Name="GlobalFontToggle",Content="One font everywhere",IsChecked=_history.Draft.Typography.UseGlobalFont };
        var game=new CheckBox { Name="GameFontToggle",Content="One font for HUD / gameplay",IsChecked=_history.Draft.Typography.UseGameFont };
        var menus=new CheckBox { Name="InterfaceFontToggle",Content="One font for menus / lobby",IsChecked=_history.Draft.Typography.UseInterfaceFont };
        global.IsCheckedChanged+=(_,_)=>Edit(p=>p.Typography.UseGlobalFont=global.IsChecked==true);
        game.IsCheckedChanged+=(_,_)=> { Edit(p=>p.Typography.UseGameFont=game.IsChecked==true);if(game.IsChecked==true)global.IsChecked=false; };
        menus.IsCheckedChanged+=(_,_)=> { Edit(p=>p.Typography.UseInterfaceFont=menus.IsChecked==true);if(menus.IsChecked==true)global.IsChecked=false; };
        shared.Children.Add(game);shared.Children.Add(menus);shared.Children.Add(global);
        var globalLabel=new TextBlock { TextWrapping=Avalonia.Media.TextWrapping.Wrap,FontSize=11 };shared.Children.Add(globalLabel);
        void SharedLabels()
        {
            string Name(string font)=>HudFonts.All[Math.Max(0,HudFonts.Index(font))].Name;
            var t=_history.Draft.Typography;
            globalLabel.Text="Gameplay: "+Name(t.GameFont)+"\nMenus/lobby: "+Name(t.InterfaceFont)+"\nEverything: "+Name(t.GlobalFont);
        }
        SharedLabels();
        shared.Children.Add(new TextBlock { Text="Everything overrides both groups. Turning a shared-font switch off restores individual choices. Font samples keep their own faces.",TextWrapping=Avalonia.Media.TextWrapping.Wrap,FontSize=11 });
        section.Children.Add(new TextBlock { Text=HudTypography.Names[_textTarget],FontSize=16 });
        string id=HudTypography.Ids[_textTarget];_textColorTarget=id;
        var original=_inspector;_inspector=section;
        var style=_history.Draft.Typography.Get(id);
        bool interfaceFont=id.StartsWith("ui.",StringComparison.Ordinal);
        Toggle(interfaceFont?"Customize interface font":"Customize lettering",style.Enabled,(p,v)=>p.Typography.Get(id).Enabled=v);
        if(!interfaceFont)Toggle("Fit large text inside screen",style.FitToScreen,(p,v)=>p.Typography.Get(id).FitToScreen=v);
        if(!interfaceFont)Toggle("Link kill and assist styling",_history.Draft.Typography.LinkKillAssist,(p,v)=>p.Typography.LinkKillAssist=v);
        var sample=new TextBlock { Name="HudFontSample",Text="Hunter 0123456789 • KILL / ASSIST",TextWrapping=Avalonia.Media.TextWrapping.Wrap };
        void Sample()
        {
            var st=_history.Draft.Typography.Get(id);var f=HudFonts.All[Math.Max(0,HudFonts.Index(st.Font))];
            sample.FontFamily=FontFamilyFor(f);sample.FontStyle=f.Italic?Avalonia.Media.FontStyle.Italic:Avalonia.Media.FontStyle.Normal;sample.FontWeight=(Avalonia.Media.FontWeight)f.Weight;sample.FontSize=24*st.Size;
            sample.Foreground=Avalonia.Media.Brush.Parse(st.MatchPalette?_history.Draft.AccentColor:st.Color);
            sample.Opacity=st.Opacity;
        }
        GameTypography.SetKeepTypeface(sample,true);
        _refreshTextSample=Sample;
        var categories=new[]{"All styles"}.Concat(HudFonts.All.Select(f=>f.Category).Distinct()).ToArray();
        var category=new ComboBox { Name="HudFontCategory",ItemsSource=categories,SelectedItem=_fontFilter,HorizontalAlignment=HorizontalAlignment.Stretch };
        section.Children.Add(category);
        var search=new TextBox { Name="HudFontSearch",Watermark="Search fonts or styles…",Text=_fontSearch };section.Children.Add(search);
        var list=new ListBox { Name="HudFontList",Height=180 };
        GameTypography.SetKeepTypeface(list,true);
        void Populate()
        {
            list.ItemsSource=HudFonts.All.Where(f=>(_fontFilter=="All styles"||f.Category==_fontFilter) && (f.Name.Contains(_fontSearch,StringComparison.OrdinalIgnoreCase)||f.Category.Contains(_fontSearch,StringComparison.OrdinalIgnoreCase))).Select(f=>new ListBoxItem {
                Tag=f.Id,Content=new TextBlock { Text=f.Name+"  Aa 0123",FontFamily=FontFamilyFor(f),FontStyle=f.Italic?Avalonia.Media.FontStyle.Italic:Avalonia.Media.FontStyle.Normal,FontWeight=(Avalonia.Media.FontWeight)f.Weight,FontSize=18,TextWrapping=Avalonia.Media.TextWrapping.Wrap }
            }).ToArray();
        }
        search.TextChanged+=(_,_)=>{_fontSearch=search.Text??"";Populate();};
        category.SelectionChanged+=(_,_)=>{_fontFilter=category.SelectedItem as string??"All styles";Populate();};
        list.SelectionChanged+=(_,_)=> { if(list.SelectedItem is ListBoxItem item && item.Tag is string font) {Edit(p=>{var t=p.Typography.Get(id);t.Font=font;t.Enabled=true;});Sample();} };
        Populate();section.Children.Add(list);Sample();section.Children.Add(new Border { Child=sample,Padding=new Thickness(6),ClipToBounds=true,MaxHeight=120 });
        var applyScope=new ComboBox { Name="FontApplyScope",ItemsSource=new[]{"HUD / gameplay only","Menus and lobbies only","Everything"},SelectedIndex=interfaceFont?1:0,HorizontalAlignment=HorizontalAlignment.Stretch };
        section.Children.Add(applyScope);
        Button(section,"Apply selected font to group",()=> {
            int scope=applyScope.SelectedIndex;
            Edit(p=> {
                var t=p.Typography;string font=t.Get(id).Font;
                if(scope==2){t.GlobalFont=font;t.UseGlobalFont=true;}
                else {t.UseGlobalFont=false;if(scope==0){t.GameFont=font;t.UseGameFont=true;}else{t.InterfaceFont=font;t.UseInterfaceFont=true;}}
            });
            if(scope==0)game.IsChecked=true;else if(scope==1)menus.IsChecked=true;
            global.IsChecked=scope==2;SharedLabels();
        });
        if(interfaceFont)
        {
            section.Children.Add(new TextBlock { Text="Applies to menus, settings, lobbies and editors. Existing control sizes and colors are retained.",TextWrapping=Avalonia.Media.TextWrapping.Wrap,FontSize=11 });
            Button(section,"Reset interface font",()=>{Edit(p=>p.Typography.Styles[id]=new());Refresh();});
            _inspector=original;_textColorTarget=null;return;
        }
        Number("Text size multiplier",style.Size,.05f,6,(p,v)=>{p.Typography.Get(id).Size=v;p.Typography.Get(id).Enabled=true;});
        Number("Text opacity",style.Opacity,0,1,(p,v)=>{p.Typography.Get(id).Opacity=v;p.Typography.Get(id).Enabled=true;});
        Toggle("Follow HUD palette",style.MatchPalette,(p,v)=>{p.Typography.Get(id).MatchPalette=v;p.Typography.Get(id).Enabled=true;});
        Toggle("Keep team / warning colors",style.PreserveSemanticColors,(p,v)=>p.Typography.Get(id).PreserveSemanticColors=v);
        ColorField("Text",style.Color,(p,v)=>{var t=p.Typography.Get(id);t.Color=v;t.MatchPalette=false;t.Enabled=true;});
        Number("Text outline (reference px)",style.Outline,0,5,(p,v)=>{p.Typography.Get(id).Outline=v;p.Typography.Get(id).Enabled=true;});
        ColorField("Text outline",style.OutlineColor,(p,v)=>{p.Typography.Get(id).OutlineColor=v;p.Typography.Get(id).Enabled=true;});
        Button(section,"Reset lettering",()=>{Edit(p=>p.Typography.Styles[id]=new());Refresh();});
        _inspector=original;_textColorTarget=null;
    }
    internal static Avalonia.Media.FontFamily FontFamilyFor(HudFonts.Entry font)
        => new("avares://ProjectPrime/Assets/HudFonts/"+font.Id+".ttf#"+font.Family);
    private void ColorField(string label, string value, Action<HudProfile,string> set)
    {
        string? textId=_textColorTarget,decorationId=_decorationColorTarget;
        string elementId=HudProfileDefaults.ElementIds[_canvas.Selected];
        float opacity=label=="Hit marker"?_history.Draft.HitMarker.Opacity:textId!=null?_history.Draft.Typography.Get(textId).Opacity:decorationId!=null?_history.Draft.Decorations.Get(decorationId).Opacity:_canvas.Selected==0?CrosshairOf(_history.Draft).Opacity:_history.Draft.Elements[elementId].Opacity;
        _inspector.Children.Add(new HudColorEditor(label,value,color=>Edit(p=>set(p,color)),opacity,v=>Edit(p=> {
            if(label=="Hit marker")p.HitMarker.Opacity=v;
            else if(textId!=null){p.Typography.Get(textId).Opacity=v;p.Typography.Get(textId).Enabled=true;}
            else if(decorationId!=null)p.Decorations.Get(decorationId).Opacity=v;
            else if(elementId=="core.crosshair")CrosshairOf(p).Opacity=v;
            else p.Elements[elementId].Opacity=v;
        })));
    }
    private void CrosshairInspector()
    {
        var target = new ComboBox { ItemsSource = new[] { "Default", "Power Beam", "Volt Driver", "Missile", "Battlehammer", "Imperialist", "Judicator", "Magmaul", "Shock Coil", "Omega Cannon", "Zoom" }, SelectedIndex = _zoom ? 10 : _weapon+1 };
        target.SelectionChanged += (_, _) => { _zoom=target.SelectedIndex==10; _weapon=_zoom ? -1 : target.SelectedIndex-1; _canvas.Weapon=_weapon; _canvas.Zoom=_zoom; Refresh(); };
        _inspector.Children.Add(target);
        var p=_history.Draft;
        var existing = _zoom ? CrosshairProperties.Resolve(CrosshairProperties.Resolve(p.Crosshair,p.WeaponCrosshairs[4]),p.ZoomCrosshair)
            : _weapon >= 0 ? CrosshairProperties.Resolve(p.Crosshair,p.WeaponCrosshairs[_weapon]) : p.Crosshair;
        _inspector.Children.Add(new TextBlock { Text="Crosshair mode" });
        var mode=new ComboBox { ItemsSource=new[]{"Custom crosshair settings","Native Automatic — Metroid","Halo Automatic"},SelectedIndex=p.WeaponCrosshairTheme==HaloWeaponCrosshairs.Theme?2:existing.Native?1:0 };
        mode.SelectionChanged+=(_,_)=> {
            _history.Edit(p=> {
                p.WeaponCrosshairTheme=mode.SelectedIndex==2?HaloWeaponCrosshairs.Theme:"";
                if(mode.SelectedIndex!=2 && existing.Native!=(mode.SelectedIndex==1)) {
                    var c=CrosshairOf(p);c.Native=mode.SelectedIndex==1;
                    if(c.OverrideProperties!=null&&!c.OverrideProperties.Contains("Native"))c.OverrideProperties=c.OverrideProperties.Append("Native").ToArray();
                }
            });Refresh();
        };
        _inspector.Children.Add(mode);
        if(p.WeaponCrosshairTheme==HaloWeaponCrosshairs.Theme)
        {
            _inspector.Children.Add(new TextBlock { Text="Automatically follows the equipped Halo weapon, including sniper zoom. Choose a weapon above to preview it. Choose Custom crosshair settings or Native Automatic to return to those crosshairs. Other HUD settings are unchanged.",TextWrapping=Avalonia.Media.TextWrapping.Wrap });
            return;
        }
        bool custom = _zoom ? p.ZoomCrosshair != null : _weapon < 0 || p.WeaponCrosshairs[_weapon] != null;
        if (_zoom || _weapon >= 0)
        {
            Toggle("Override default",custom,(p,v) => { if (v) CrosshairOf(p); else if (_zoom) p.ZoomCrosshair=null; else p.WeaponCrosshairs[_weapon]=null; });
            Button(_inspector,"Refresh override controls",Refresh);
            if (!custom) return;
        }
        var configured=CrosshairOf(p);
        var inherited = _zoom ? CrosshairProperties.Resolve(p.Crosshair,p.WeaponCrosshairs[4]) : p.Crosshair;
        var c = _zoom || _weapon>=0 ? CrosshairProperties.Resolve(inherited,configured) : configured;
        var displayed=c;
        if(c.Native)_inspector.Children.Add(new TextBlock { Text="Uses extracted MPH hunter reticles and the original Imperialist/zoom mapping. Sprite size, stretch, rotation, opacity, palette and motion are adjustable; geometric arms/rings do not modify cartridge sprites.",TextWrapping=Avalonia.Media.TextWrapping.Wrap });
        _inspector.Children.Add(new TextBlock { Text="Color source" });
        var colorChoices=c.Native?new[]{"Original MPH colors","HUD palette","Custom color","Health / targeting"}:new[]{"HUD palette","Custom color","Health / targeting"};
        int colorIndex=c.Native&&c.NativeOriginalColors?0:(c.MatchHudPalette?0:c.HealthColor?2:1)+(c.Native?1:0);
        var colorMode=new ComboBox { ItemsSource=colorChoices,SelectedIndex=colorIndex };
        colorMode.SelectionChanged+=(_,_)=> { Edit(p=> { var c=CrosshairOf(p);int index=colorMode.SelectedIndex-(displayed.Native?1:0);c.NativeOriginalColors=displayed.Native&&index==-1;c.MatchHudPalette=index==0;c.HealthColor=index==2;
            if(c.OverrideProperties!=null)c.OverrideProperties=c.OverrideProperties.Concat(new[]{"NativeOriginalColors","MatchHudPalette","HealthColor"}).Distinct().ToArray(); });Refresh(); };
        _inspector.Children.Add(colorMode);
        var presets = new ComboBox { ItemsSource=CrosshairPresets.Names, PlaceholderText="Crosshair preset" };
        presets.SelectionChanged += (_,_) => { if (presets.SelectedIndex < 0) return; Edit(p => { p.WeaponCrosshairTheme=""; var c=CrosshairPresets.Create(presets.SelectedIndex); if (_zoom) p.ZoomCrosshair=c; else if (_weapon>=0) p.WeaponCrosshairs[_weapon]=c; else p.Crosshair=c; }); Refresh(); };
        _inspector.Children.Add(presets);
        Button(_inspector,"Share crosshair",() => { _json.Text = CrosshairPresets.Share(CrosshairOf(_history.Draft)); _json.BringIntoView(); });
        Button(_inspector,"Import crosshair code",() => Try(() =>
        {
            var imported = CrosshairPresets.Import(_json.Text?.Trim() ?? "");
            Edit(p => { p.WeaponCrosshairTheme=""; if (_zoom) p.ZoomCrosshair=imported; else if (_weapon>=0) p.WeaponCrosshairs[_weapon]=imported; else p.Crosshair=imported; }); Refresh();
        }));
        if(displayed.Native) Number("Original crosshair thickness (added px)",displayed.NativeThickness,0,4,(p,v)=>{var c=CrosshairOf(p);c.NativeThickness=v;c.OverrideProperties=(c.OverrideProperties??Array.Empty<string>()).Append("NativeThickness").Distinct().ToArray();});
        var main=_inspector;
        string? category=null;
        foreach (var descriptor in CrosshairProperties.All.OrderBy(d=>d.Category=="General"?"0":d.Category).ThenBy(d=>d.Id switch { "Scale"=>0,"WidthScale"=>1,"HeightScale"=>2,"Rotation"=>3,"Opacity"=>4,_=>5 }))
        {
            if(descriptor.Id=="NativeThickness")continue;
            if(descriptor.Id is "Native" or "NativeOriginalColors" or "MatchHudPalette" or "HealthColor")continue;
            if(displayed.Native && descriptor.Category!="General")continue;
            if(!displayed.Native && descriptor.Id is "NativeOriginalColors" or "NativeAnimations")continue;
            if(category!=descriptor.Category)
            {
                category=descriptor.Category;_inspector=new StackPanel { Spacing=6 };
                main.Children.Add(new Expander { Header=category,Content=_inspector,IsExpanded=category=="General",HorizontalAlignment=HorizontalAlignment.Stretch });
                string resetCategory=category;
                Button(_inspector,"Reset "+category,()=> { Edit(p=> { foreach(var d in CrosshairProperties.All.Where(d=>d.Category==resetCategory))d.Reset(CrosshairOf(p),_weapon>=0||_zoom?inherited:HudProfileDefaults.Create(p.BasePreset).Crosshair); });Refresh(); });
            }
            bool enabled=descriptor.Category switch { "Dot"=>displayed.Dot,"Inner"=>displayed.Inner,"Outer"=>displayed.Outer,"Ring"=>displayed.Ring,"Brackets"=>displayed.Brackets,_=>true };
            if(!enabled && descriptor.Id!=descriptor.Category)continue;
            if(descriptor.Id=="Color" && (displayed.MatchHudPalette||displayed.HealthColor||displayed.Native&&displayed.NativeOriginalColors))continue;
            object value=descriptor.Get(displayed)!;
            if(descriptor.Type==typeof(bool)) Toggle(descriptor.Name,(bool)value,(p,v)=> { descriptor.Set(CrosshairOf(p),v);Avalonia.Threading.Dispatcher.UIThread.Post(Refresh); });
            else if(descriptor.Type==typeof(string)) ColorField(descriptor.Name,(string)value,(p,v)=>descriptor.Set(CrosshairOf(p),v));
            else if(descriptor.Type.IsEnum)
            {
                _inspector.Children.Add(new TextBlock { Text=descriptor.Name });
                var choice=new ComboBox { ItemsSource=Enum.GetNames(descriptor.Type),SelectedIndex=Convert.ToInt32(value) };
                choice.SelectionChanged += (_,_)=>Edit(p=>descriptor.Set(CrosshairOf(p),Enum.ToObject(descriptor.Type,Math.Max(0,choice.SelectedIndex))));
                _inspector.Children.Add(choice);
            }
            else Number(descriptor.Name,Convert.ToSingle(value),descriptor.Min,descriptor.Max,(p,v)=>descriptor.Set(CrosshairOf(p),descriptor.Type==typeof(int) ? (object)(int)v : v));
            if(descriptor.Id=="MinimumVisibility")_inspector.Children.Add(new TextBlock { Text="Optional: native sprites keep an 8-pixel minimum extent; custom bars keep 1-pixel strokes. No substitute dot is added. With this off, very small details can fall below pixel resolution.",TextWrapping=Avalonia.Media.TextWrapping.Wrap });

        }
        _inspector=main;
        if(!displayed.Native) foreach(string partName in CrosshairProperties.PartNames)
        {
            var property=typeof(CrosshairProfile).GetProperty(partName)!;
            var part=(CrosshairPartStyle)property.GetValue(displayed)!;
            CrosshairPartStyle Part(HudProfile profile)
            { var crosshair=CrosshairOf(profile); CrosshairProperties.MarkPart(crosshair,partName); return (CrosshairPartStyle)property.GetValue(crosshair)!; }
            _inspector=new StackPanel { Spacing=6 };
            main.Children.Add(new Expander { Header=partName.Replace("Style"," styling"),Content=_inspector,IsExpanded=false,HorizontalAlignment=HorizontalAlignment.Stretch });
            Toggle("Custom color",part.CustomColor,(p,v)=>Part(p).CustomColor=v);
            ColorField("Part",part.Color,(p,v)=>Part(p).Color=v);
            Toggle("Custom outline color",part.CustomOutlineColor,(p,v)=>Part(p).CustomOutlineColor=v);
            ColorField("Outline",part.OutlineColor,(p,v)=>Part(p).OutlineColor=v);
            Number("Part opacity",part.Opacity,0,1,(p,v)=>Part(p).Opacity=v);
            Number("Outline (-1 inherits)",part.Outline,-1,20,(p,v)=>Part(p).Outline=v);
            Button(_inspector,"Reset "+partName,()=> { Edit(p=>
            {
                var crosshair=CrosshairOf(p); property.SetValue(crosshair,new CrosshairPartStyle());
                if(crosshair.OverrideProperties!=null) crosshair.OverrideProperties=crosshair.OverrideProperties.Where(id=>id!=partName).ToArray();
            }); Refresh(); });
        }
        _inspector=main;
        _inspector.Children.Add(new TextBlock { Text = "Hit marker" });
        var hit = p.HitMarker;
        Toggle("Show hit marker",hit.Enabled,(p,v)=>p.HitMarker.Enabled=v);
        var shape = new ComboBox { ItemsSource=Enum.GetNames<HudHitMarkerShape>(),SelectedIndex=(int)hit.Shape };
        shape.SelectionChanged += (_,_)=>Edit(p=>p.HitMarker.Shape=(HudHitMarkerShape)shape.SelectedIndex); _inspector.Children.Add(shape);
        ColorField("Hit marker",hit.Color,(p,v)=>p.HitMarker.Color=v);
        Number("Marker scale",hit.Scale,.1f,8,(p,v)=>p.HitMarker.Scale=v);
        Number("Marker opacity",hit.Opacity,0,1,(p,v)=>p.HitMarker.Opacity=v);
        Number("Marker gap",hit.Gap,0,100,(p,v)=>p.HitMarker.Gap=v);
        Number("Marker length",hit.Length,.1f,100,(p,v)=>p.HitMarker.Length=v);
        Number("Marker thickness",hit.Thickness,.1f,20,(p,v)=>p.HitMarker.Thickness=v);
    }
}
