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
    private readonly StackPanel _inspector = new() { Spacing = 6, Margin = new Thickness(8) };
    private readonly TextBlock _status = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly TextBox _name = new() { PlaceholderText = "Profile name", MinWidth = 130 };
    private readonly TextBox _json = new() { AcceptsReturn = true, Height = 90, PlaceholderText = "Paste profile JSON here to import" };
    private readonly ComboBox _elements;
    private int _weapon = -1;
    private bool _zoom;
    private readonly Action<HudProfile?> _close;
    private bool _controllerMove;
    public HudStudioView(HudProfile original, Action<HudProfile?> close)
    {
        _close = close;
        SetValue(ControllerNav.ModalProperty, true);
        _history = new(original);
        _canvas = new(_history);
        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), Margin = new Thickness(12) };
        var toolbar = new WrapPanel { Orientation = Orientation.Horizontal };
        toolbar.Children.Add(new TextBlock { Text = "HUD STUDIO", FontSize = 22, Margin = new Thickness(6) });
        var presets = new ComboBox { ItemsSource = HudProfileDefaults.Presets, SelectedIndex = Array.IndexOf(HudProfileDefaults.Presets, original.Name), MinWidth = 150 };
        presets.SelectionChanged += (_, _) => { if (presets.SelectedItem is string preset) { _history.Replace(HudProfileDefaults.Create(preset)); _name.Text = preset; Refresh(); } };
        toolbar.Children.Add(presets);
        Button(toolbar, "Undo", () => { _history.Undo(); Refresh(); });
        Button(toolbar, "Redo", () => { _history.Redo(); Refresh(); });
        Button(toolbar, "Lock all", () => { _history.Edit(p => { foreach (var element in p.Elements.Values) element.Locked = true; }); Refresh(); });
        Button(toolbar, "Unlock all", () => { _history.Edit(p => { foreach (var element in p.Elements.Values) element.Locked = false; }); Refresh(); });
        Button(toolbar, "Align left", () => _canvas.AlignSelection(true));
        Button(toolbar, "Align top", () => _canvas.AlignSelection(false));
        Button(toolbar, "Reset HUD", () => { _history.Replace(HudProfileDefaults.Create(_history.Draft.BasePreset)); Refresh(); });
        Button(toolbar, "Use in settings", () =>
        {
            try { var p = _history.Draft.DeepClone(); p.Name = _name.Text ?? "Custom"; if (p.Mode == HudMode.Custom && Array.IndexOf(HudProfileDefaults.Presets,p.Name) >= 0) p.Name += " (Custom)"; p.Validate(); close(p); }
            catch (Exception ex) { _status.Text = ex.Message; }
        });
        Button(toolbar, "Cancel", () => close(null));
        root.Children.Add(toolbar);
        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("*,300") };
        Grid.SetRow(body, 1); root.Children.Add(body); body.Children.Add(_canvas);
        var scroll = new ScrollViewer { Content = _inspector }; Grid.SetColumn(scroll, 1); body.Children.Add(scroll);
        var footer = new StackPanel { Spacing = 6 };
        var controls = new WrapPanel();
        _elements = new ComboBox { ItemsSource = HudProfileDefaults.ElementIds, SelectedIndex = 0, MinWidth = 180 };
        _elements.SelectionChanged += (_, _) => { _canvas.Selected = Math.Max(0, _elements.SelectedIndex); Refresh(); };
        controls.Children.Add(_elements);
        var aspects = new ComboBox { ItemsSource = new[] { "16:9", "21:9", "4:3", "16:10", "Android portrait" }, SelectedIndex = 0, MinWidth = 130 };
        aspects.SelectionChanged += (_, _) =>
        {
            (_canvas.PreviewWidth, _canvas.PreviewHeight) = aspects.SelectedIndex switch { 1 => (3440,1440), 2 => (1440,1080), 3 => (1920,1200), 4 => (1080,1920), _ => (1920,1080) }; _canvas.InvalidateVisual();
        };
        controls.Children.Add(aspects);
        var scenario = new ComboBox { ItemsSource = Enum.GetNames<HudPreviewScenario>(), SelectedIndex = 0, MinWidth = 120 };
        scenario.SelectionChanged += (_,_) => { _canvas.Scenario=(HudPreviewScenario)scenario.SelectedIndex; _canvas.Zoom=_canvas.Scenario==HudPreviewScenario.Zoomed; _canvas.Refresh(); };
        controls.Children.Add(scenario);
        var grid = new ComboBox { ItemsSource = new[] { "Grid off", "1", "2", "4", "8", "16" }, SelectedIndex = 4 };
        grid.SelectionChanged += (_, _) => { _canvas.GridSize = grid.SelectedIndex == 0 ? 0 : 1 << (grid.SelectedIndex-1); _canvas.InvalidateVisual(); };
        var guides = new CheckBox { Content = "Snap guides", IsChecked = true, Margin = new Thickness(6) };
        guides.IsCheckedChanged += (_, _) => _canvas.SnapGuides = guides.IsChecked == true;
        controls.Children.Add(guides);
        controls.Children.Add(grid); footer.Children.Add(controls); footer.Children.Add(_status);
        Grid.SetRow(footer, 2); root.Children.Add(footer); Content = root;
        _canvas.Changed += () => { _elements.SelectedIndex = _canvas.Selected; Refresh(); };
        _name.Text = original.Name;
        SizeChanged += (_, e) =>
        {
            bool narrow = e.NewSize.Width < 700;
            body.ColumnDefinitions = new ColumnDefinitions(narrow ? "*" : "*,300");
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
        _history.Edit(p => { edit(p); p.Mode = HudMode.Custom; }); _canvas.Refresh();
    }
    private void Number(string name, float value, float min, float max, Action<HudProfile, float> set, bool integer=false)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        row.Children.Add(new TextBlock { Text = name, Width = 125, VerticalAlignment = VerticalAlignment.Center });
        var field = new NumericUpDown { Value = (decimal)value, Minimum = (decimal)min, Maximum = (decimal)max, Increment = integer ? 1m : max <= 8 ? .1m : 1m, Width = 115 };
        field.ValueChanged += (_, _) => { if (field.Value is decimal v) Edit(p => set(p, (float)v)); };
        row.Children.Add(field); _inspector.Children.Add(row);
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
    private void Refresh()
    {
        _canvas.Refresh(); _inspector.Children.Clear();
        var p = _history.Draft; string id = HudProfileDefaults.ElementIds[_canvas.Selected]; var e = p.Elements[id];
        _inspector.Children.Add(new TextBlock { Text = id, FontSize = 18 });
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
        Number("Scale", e.Scale,.1f,8,(p,v) => p.Elements[id].Scale=v);
        Number("Opacity", e.Opacity,0,1,(p,v) => p.Elements[id].Opacity=v);
        var tint = new TextBox { Text = e.Color, PlaceholderText = "Tint #RRGGBB" };
        tint.LostFocus += (_, _) => Edit(p => p.Elements[id].Color = tint.Text ?? "#FFFFFF");
        _inspector.Children.Add(tint);
        Button(_inspector,"Reset element",() => { Edit(p => p.ResetElement(id)); Refresh(); });
        Button(_inspector,"Reset section",() =>
        {
            string prefix=id.Split('.')[0]+".";
            Edit(p=> { foreach (string elementId in HudProfileDefaults.ElementIds) if(elementId.StartsWith(prefix,StringComparison.Ordinal)) p.ResetElement(elementId); }); Refresh();
        });
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
            Number("Radius scale",p.Radar.RadiusScale,.1f,4,(p,v)=>p.Radar.RadiusScale=v);
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
        _inspector.Children.Add(new Separator());
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
        _inspector.Children.Add(_name);
        Button(_inspector,"Save named profile",() => Try(() => { var copy=p.DeepClone(); copy.Name=_name.Text ?? "Custom"; HudProfiles.SaveNamed(copy.Name,copy); _status.Text="Named profile saved. Use in settings, then Apply to activate."; }));
        Button(_inspector,"Load named profile",() => Try(() => { _history.Replace(HudProfiles.LoadNamed(_name.Text ?? "")); Refresh(); }));
        _inspector.Children.Add(_json);
        Button(_inspector,"Export JSON",() => { _json.Text = HudProfileStore.Serialize(_history.Draft); _status.Text="Select and copy the JSON to share your profile."; });
        Button(_inspector,"Import JSON",() => Try(() => { _history.Replace(HudProfileStore.Parse(_json.Text ?? "")); _name.Text=_history.Draft.Name; Refresh(); }));
        _status.Text = "Drag to move • Arrows to nudge • Shift: 10 units / no snap • Ctrl+Z/Y: undo/redo. Crosshair, gauge and radar-frame geometry is shared; other previews are schematic. Shift-click selects a group.";
    }
    private void Try(Action action) { try { action(); } catch (Exception ex) { _status.Text=ex.Message; } }
    private void MeterInspector(bool health)
    {
        HudMeterProfile Meter(HudProfile p) => health ? p.Health : p.Ammo;
        var m = Meter(_history.Draft);
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
    private void ColorField(string label, string value, Action<HudProfile,string> set)
    {
        _inspector.Children.Add(new TextBlock { Text = label + " color" });
        var field = new TextBox { Text=value, PlaceholderText="#RRGGBB" };
        field.LostFocus += (_,_)=>Edit(p=>set(p,field.Text ?? "#FFFFFF"));
        _inspector.Children.Add(field);
    }
    private void CrosshairInspector()
    {
        var target = new ComboBox { ItemsSource = new[] { "Default", "Power Beam", "Volt Driver", "Missile", "Battlehammer", "Imperialist", "Judicator", "Magmaul", "Shock Coil", "Omega Cannon", "Zoom" }, SelectedIndex = _zoom ? 10 : _weapon+1 };
        target.SelectionChanged += (_, _) => { _zoom=target.SelectedIndex==10; _weapon=_zoom ? -1 : target.SelectedIndex-1; _canvas.Weapon=_weapon; _canvas.Zoom=_zoom; Refresh(); };
        _inspector.Children.Add(target);
        var p=_history.Draft;
        bool custom = _zoom ? p.ZoomCrosshair != null : _weapon < 0 || p.WeaponCrosshairs[_weapon] != null;
        if (_zoom || _weapon >= 0)
        {
            Toggle("Override default",custom,(p,v) => { if (v) CrosshairOf(p); else if (_zoom) p.ZoomCrosshair=null; else p.WeaponCrosshairs[_weapon]=null; });
            Button(_inspector,"Refresh override controls",Refresh);
            if (!custom) return;
        }
        var c=CrosshairOf(p);
        var presets = new ComboBox { ItemsSource=CrosshairPresets.Names, PlaceholderText="Crosshair preset" };
        presets.SelectionChanged += (_,_) => { if (presets.SelectedIndex < 0) return; Edit(p => { var c=CrosshairPresets.Create(presets.SelectedIndex); if (_zoom) p.ZoomCrosshair=c; else if (_weapon>=0) p.WeaponCrosshairs[_weapon]=c; else p.Crosshair=c; }); Refresh(); };
        _inspector.Children.Add(presets);
        Button(_inspector,"Share crosshair",() => { _json.Text = CrosshairPresets.Share(CrosshairOf(_history.Draft)); _json.BringIntoView(); });
        Button(_inspector,"Import crosshair code",() => Try(() =>
        {
            var imported = CrosshairPresets.Import(_json.Text?.Trim() ?? "");
            Edit(p => { if (_zoom) p.ZoomCrosshair=imported; else if (_weapon>=0) p.WeaponCrosshairs[_weapon]=imported; else p.Crosshair=imported; }); Refresh();
        }));
        var inherited = _zoom ? CrosshairProperties.Resolve(p.Crosshair,p.WeaponCrosshairs[4]) : p.Crosshair;
        var displayed = _zoom || _weapon>=0 ? CrosshairProperties.Resolve(inherited,c) : c;
        string? category=null;
        foreach (var descriptor in CrosshairProperties.All.OrderBy(d=>d.Category))
        {
            if(category!=descriptor.Category)
            { category=descriptor.Category; _inspector.Children.Add(new TextBlock { Text=category,FontSize=16 }); }
            object value=descriptor.Get(displayed)!;
            if(descriptor.Type==typeof(bool)) Toggle(descriptor.Name,(bool)value,(p,v)=>descriptor.Set(CrosshairOf(p),v));
            else if(descriptor.Type==typeof(string)) ColorField(descriptor.Name,(string)value,(p,v)=>descriptor.Set(CrosshairOf(p),v));
            else if(descriptor.Type.IsEnum)
            {
                var choice=new ComboBox { ItemsSource=Enum.GetNames(descriptor.Type),SelectedIndex=Convert.ToInt32(value) };
                choice.SelectionChanged += (_,_)=>Edit(p=>descriptor.Set(CrosshairOf(p),Enum.ToObject(descriptor.Type,Math.Max(0,choice.SelectedIndex))));
                _inspector.Children.Add(choice);
            }
            else Number(descriptor.Name,Convert.ToSingle(value),descriptor.Min,descriptor.Max,(p,v)=>descriptor.Set(CrosshairOf(p),descriptor.Type==typeof(int) ? (object)(int)v : v));
            Button(_inspector,"Reset "+descriptor.Name,()=>
            { Edit(p=>descriptor.Reset(CrosshairOf(p),_weapon>=0 || _zoom ? inherited : HudProfileDefaults.Create(p.BasePreset).Crosshair)); Refresh(); });
        }
        foreach(string partName in CrosshairProperties.PartNames)
        {
            var property=typeof(CrosshairProfile).GetProperty(partName)!;
            var part=(CrosshairPartStyle)property.GetValue(displayed)!;
            CrosshairPartStyle Part(HudProfile profile)
            { var crosshair=CrosshairOf(profile); CrosshairProperties.MarkPart(crosshair,partName); return (CrosshairPartStyle)property.GetValue(crosshair)!; }
            _inspector.Children.Add(new TextBlock { Text=partName,FontSize=16 });
            Toggle("Custom color",part.CustomColor,(p,v)=>Part(p).CustomColor=v);
            ColorField("Part",part.Color,(p,v)=>Part(p).Color=v);
            Number("Part opacity",part.Opacity,0,1,(p,v)=>Part(p).Opacity=v);
            Number("Outline (-1 inherits)",part.Outline,-1,20,(p,v)=>Part(p).Outline=v);
            Button(_inspector,"Reset "+partName,()=> { Edit(p=>
            {
                var crosshair=CrosshairOf(p); property.SetValue(crosshair,new CrosshairPartStyle());
                if(crosshair.OverrideProperties!=null) crosshair.OverrideProperties=crosshair.OverrideProperties.Where(id=>id!=partName).ToArray();
            }); Refresh(); });
        }
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
