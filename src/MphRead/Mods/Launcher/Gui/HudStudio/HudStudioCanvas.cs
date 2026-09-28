using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using MphRead.Mods.Render;
using MphRead.Mods.Render.Hud;
using Vector2 = System.Numerics.Vector2;
namespace MphRead.Mods.Launcher.Gui;

internal sealed class HudStudioCanvas : Control
{
    public HudStudioHistory History { get; }
    public int Selected { get; set; }
    public HashSet<int> Selection { get; } = new();
    private readonly Dictionary<int,Vector2> _groupOffsets = new();
    internal void AlignSelection(bool horizontal)
    {
        if(Selection.Count<2) return;
        Rect target=ElementBounds(Selected);
        History.Edit(p=>
        {
            foreach(int index in Selection)
            {
                if(index==0 || p.Elements[HudProfileDefaults.ElementIds[index]].Locked) continue;
                Rect rect=ElementBounds(index); var element=p.Elements[HudProfileDefaults.ElementIds[index]];
                if(horizontal) element.OffsetX+=(float)(target.Left-rect.Left)/Transform.UnitScale;
                else element.OffsetY+=(float)(target.Top-rect.Top)/Transform.UnitScale;
            }
            p.Mode=HudMode.Custom;
        });
        Refresh(); Changed?.Invoke();
    }
    public int GridSize { get; set; } = 8;
    public bool SnapGuides { get; set; } = true;
    private double? _guideX, _guideY;
    public float PreviewWidth { get; set; } = 1920;
    public float PreviewHeight { get; set; } = 1080;
    internal int PreviewHunter { get; set; }
    private readonly HudNativePreview _native = new();
    public int Weapon { get; set; } = -1;
    public bool Zoom { get; set; }
    internal bool RadarOnly { get; set; }
    public HudPreviewScenario Scenario { get; set; }
    private HudPreviewState Preview => HudPreviewState.For(Scenario);
    public event Action? Changed;
    private Point _start;
    private Vector2 _offset;
    private string? _before;
    private CrosshairRuntime _crosshair;
    private readonly Dictionary<IPointer, Point> _touches = new();
    private double _pinchDistance;
    private float _pinchScale;
    private double TouchDistance()
    {
        Point first = default; bool found = false;
        foreach (var point in _touches.Values) { if (!found) { first = point; found = true; } else return Math.Sqrt(Math.Pow(point.X - first.X, 2) + Math.Pow(point.Y - first.Y, 2)); }
        return 0;
    }
    private static readonly string[] Labels = { "+", "74", "39 / 80", "POWER BEAM\nVOLT DRIVER\nMISSILE\nIMPERIALIST", "RADAR", "SCORE 5 / 7", "03:42", "Hunter  >  Rival\nRival  >  Player", "DOUBLE KILL", "RIVAL / HEALTH", "OBJECTIVE", "144 FPS", "HEALTH GAUGE", "AMMO GAUGE" };
    private static readonly Vector2[] Sizes = { new(60,60),new(248,112),new(326,112),new(248,620),Vector2.Zero,new(180,120),new(200,60),new(520,160),new(340,80),new(520,200),new(300,100),new(120,40),new(225,17),new(304,17) };
    public HudStudioCanvas(HudStudioHistory history)
    {
        History = history; Focusable = true; MinHeight = 260;
        _crosshair = new(history.Draft.Crosshair);
    }
    public void Refresh()
    {
        var p = History.Draft;
        var baseCrosshair = CrosshairProperties.Resolve(p.Crosshair, p.WeaponCrosshairs[Zoom ? 4 : Weapon >= 0 ? Weapon : 0]);
        if (Weapon < 0 && !Zoom) baseCrosshair=p.Crosshair;
        _crosshair = new(Zoom ? CrosshairProperties.Resolve(baseCrosshair,p.ZoomCrosshair) : baseCrosshair);
        _native.Prepare();
        InvalidateVisual();
    }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    { _native.Dispose(); base.OnDetachedFromVisualTree(e); }
    private Rect Surface
    {
        get
        {
            double scale = Math.Min(Bounds.Width / PreviewWidth, Bounds.Height / PreviewHeight);
            return new Rect((Bounds.Width - PreviewWidth * scale) / 2, (Bounds.Height - PreviewHeight * scale) / 2, PreviewWidth * scale, PreviewHeight * scale);
        }
    }
    private HudTransform Transform => new((float)Surface.Width, (float)Surface.Height, History.Draft.SafeArea);
    internal Rect ElementBounds(int index)
    {
        var p = History.Draft; var e = p.Elements[HudProfileDefaults.ElementIds[index]];
        var t = Transform;
        var point = t.Resolve(e.Anchor, new Vector2(e.OffsetX, e.OffsetY));
        var size = (index==4 ? HudRadarGeometry.GetBounds(new(p.Radar),p.TextScale) : Sizes[index]) * t.UnitScale * e.Scale * p.GlobalScale;
        if (index == 4) { size=Vector2.Max(size,new Vector2(16)); point -= size / 2; }
        if (index == 11) point.X -= size.X;
        if (index is 6 or 8) point.X -= size.X / 2;
        if (index == 0) point = new Vector2((float)Surface.Width / 2, (float)Surface.Height / 2) - size / 2;
        return new Rect(Surface.X + point.X, Surface.Y + point.Y, Math.Max(16, size.X), Math.Max(16, size.Y));
    }
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        Rect surface = Surface;
        if (surface.Width <= 0 || surface.Height <= 0) return;
        context.FillRectangle(new SolidColorBrush(Color.Parse("#101820")), surface);
        using (context.PushClip(surface))
        {
            double margin = History.Draft.SafeArea;
            context.DrawRectangle(null, new Pen(Brushes.Gray, 1), surface.Deflate(new Thickness(surface.Width * margin, surface.Height * margin)));
            if (GridSize > 0)
            {
                double step = Math.Max(12, GridSize * Transform.UnitScale);
                var pen = new Pen(new SolidColorBrush(Color.Parse("#25303B")), .5);
                for (double x = surface.X; x < surface.Right; x += step) context.DrawLine(pen, new Point(x, surface.Y), new Point(x, surface.Bottom));
                for (double y = surface.Y; y < surface.Bottom; y += step) context.DrawLine(pen, new Point(surface.X, y), new Point(surface.Right, y));
            }
            for (int i = 0; i < Labels.Length; i++)
            {
                if(RadarOnly && i!=4) continue;
                var element = History.Draft.Elements[HudProfileDefaults.ElementIds[i]];
                Rect rect = ElementBounds(i);
                var preview = Preview;
                bool visible = element.Enabled && (element.Contexts & (preview.Spectator ? HudContext.SpectatorFree : HudContext.Playing)) != 0
                    && (element.Visibility switch { HudVisibility.Spectator => preview.Spectator, HudVisibility.Damaged => preview.Health<99,
                        HudVisibility.AmmoNotFull => preview.Ammo<80,HudVisibility.Objective => preview.Objective,HudVisibility.Combat => preview.Combat,_ => true });
                using (context.PushOpacity(visible ? element.Opacity * History.Draft.GlobalOpacity : .18))
                {
                    if (i == 0) DrawCrosshair(context, rect.Center);
                    else if(i==3 && (History.Draft.Inventory.Native || History.Draft.Mode==HudMode.Classic))
                    {
                        double unit=5.625*Transform.UnitScale*element.Scale*History.Draft.GlobalScale*History.Draft.IconScale;
                        _native.Draw(context,MphRead.Hud.HudElements.HunterObjects[PreviewHunter].WeaponIcon,Math.Max(0,Weapon),rect.TopLeft,unit,unit);
                    }
                    else if(i==4) DrawRadar(context,rect);
                    else if (i is 1 or 2 or 12 or 13) DrawMeter(context, rect, i is 1 or 12,i>=12);
                    else
                    {
                        context.DrawRectangle(new SolidColorBrush(Color.Parse("#50334455")), new Pen(Brushes.SlateGray, 1), rect);
                        string label = i == 8 && Scenario == HudPreviewScenario.Objective ? "OCTOLITH CAPTURED" : Labels[i];
                        var text = new FormattedText(label, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                            new Typeface("monospace"), Math.Max(8, 30 * Transform.UnitScale * element.Scale * History.Draft.GlobalScale), Brushes.White);
                        context.DrawText(text, rect.TopLeft + new Vector(4, 4));
                    }
                }
                if (Selected == i || Selection.Contains(i)) context.DrawRectangle(null, new Pen(Brushes.Cyan, 2), rect.Inflate(3));
            }
            if (_guideX is double gx) context.DrawLine(new Pen(Brushes.Cyan, 1), new Point(gx, surface.Top), new Point(gx, surface.Bottom));
            if (_guideY is double gy) context.DrawLine(new Pen(Brushes.Cyan, 1), new Point(surface.Left, gy), new Point(surface.Right, gy));
        }
    }
    private void DrawRadar(DrawingContext context,Rect bounds)
    {
        var p=History.Draft; var style=new HudRadarRuntime(p.Radar);
        double scale=Transform.UnitScale*p.GlobalScale*p.Elements["core.radar"].Scale;
        float radius=113.724f*style.RadiusScale;
        var pal=Radar.PaletteOf;
        System.Numerics.Vector4 Color(OpenTK.Mathematics.Vector4 c)=>new(c.X,c.Y,c.Z,c.W);
        Span<HudShapePrimitive> primitives=stackalloc HudShapePrimitive[HudRadarGeometry.FrameCapacity];
        const float time=1.125f;
        int count=HudRadarGeometry.Build(primitives,style,radius,p.RadarBackground,p.RadarOutlines,Color(pal.Background),Color(pal.Ring),Color(pal.Cone),5.85f,time,p.ReduceMotion,p.ReduceTransparency);
        DrawRadarShapes(context,bounds.Center,scale,primitives[..count]);
        var facing=new System.Numerics.Vector3(.5f,0,.8660254f);
        var basis=HudRadarProjection.BuildBasis(facing,style.Orientation);
        if(!p.ReduceMotion && style.Hunters) foreach(var contact in HudRadarPreview.Contacts)
        {
            if(contact.Kind!=RadarContactKind.Hunter || !HudRadarProjection.Project(contact,default,basis,style,radius,out _)) continue;
            for(int i=style.TrailSamples;i>0;i--)
            {
                var trail=contact with { Position=contact.Position-new System.Numerics.Vector3(i*.7f,0,i*.4f),Alpha=.35f*(1-(i-1)/4f) };
                if(!HudRadarProjection.Project(trail,default,basis,style,radius,out var projectedTrail)) continue;
                count=HudRadarGeometry.BuildContact(primitives,trail,projectedTrail,basis,style,5.625f,time,p.ReduceMotion);
                DrawRadarShapes(context,bounds.Center,scale,primitives[..count]);
            }
        }
        for(int pass=0;pass<3;pass++) foreach(var contact in HudRadarPreview.Contacts)
        {
            if(HudRadarGeometry.ContactLayer(contact.Kind,style.Style)!=pass || !HudRadarProjection.Visible(contact.Kind,style)
                || !HudRadarProjection.Project(contact,default,basis,style,radius,out var projected)) continue;
            count=HudRadarGeometry.BuildContact(primitives,contact,projected,basis,style,5.625f,time,p.ReduceMotion);
            DrawRadarShapes(context,bounds.Center,scale,primitives[..count]);
        }
        count=HudRadarGeometry.BuildSelfMarker(primitives,style,MathF.PI/6,basis,5.625f,Color(pal.Player));
        DrawRadarShapes(context,bounds.Center,scale,primitives[..count]);
        if(style.Cardinals) for(int i=0;i<4;i++)
        {
            var direction=basis.Project(new(i==1 ? 1 : i==3 ? -1 : 0,0,i==0 ? 1 : i==2 ? -1 : 0))*radius*.83f;
            var text=new FormattedText("NESW"[i].ToString(),System.Globalization.CultureInfo.InvariantCulture,FlowDirection.LeftToRight,
                new Typeface("monospace"),Math.Max(1,18*scale),Brushes.White);
            context.DrawText(text,new(bounds.Center.X+direction.X*scale-text.Width/2,bounds.Center.Y-direction.Y*scale-text.Height/2));
        }
    }
    private void DrawRadarShapes(DrawingContext context,Point center,double scale,ReadOnlySpan<HudShapePrimitive> primitives)
    {
        Span<Vector2> vertices=stackalloc Vector2[6];
        var tint=HudColor.Parse(History.Draft.Elements["core.radar"].Color);
        foreach(var primitive in primitives)
        {
            var c=primitive.Color;
            if(History.Draft.ReduceTransparency && c.W>0) c.W=MathF.Max(.85f,c.W);
            var brush=new SolidColorBrush(Avalonia.Media.Color.FromArgb((byte)(Math.Clamp(c.W,0,1)*255),
                (byte)(Math.Clamp(c.X*tint.X,0,1)*255),(byte)(Math.Clamp(c.Y*tint.Y,0,1)*255),(byte)(Math.Clamp(c.Z*tint.Z,0,1)*255)));
            var pen=new Pen(brush,primitive.Thickness*scale);
            double r=primitive.Radius*scale;
            Point position=new(center.X+primitive.A.X*scale,center.Y-primitive.A.Y*scale);
            switch(primitive.Kind)
            {
                case HudShapeKind.Disc: context.DrawEllipse(brush,null,position,r,r); break;
                case HudShapeKind.Ring: context.DrawEllipse(null,pen,position,r,r); break;
                case HudShapeKind.Square: context.FillRectangle(brush,new Rect(position.X-r,position.Y-r,r*2,r*2)); break;
                case HudShapeKind.Line: context.DrawLine(pen,position,new(center.X+primitive.B.X*scale,center.Y-primitive.B.Y*scale)); break;
                default:
                    int count=HudRadarGeometry.Polygon(primitive,vertices);
                    var geometry=new StreamGeometry();
                    using(var path=geometry.Open())
                    {
                        path.BeginFigure(new(position.X+vertices[0].X*scale,position.Y-vertices[0].Y*scale),true);
                        for(int i=1;i<count;i++) path.LineTo(new(position.X+vertices[i].X*scale,position.Y-vertices[i].Y*scale));
                        path.EndFigure(true);
                    }
                    context.DrawGeometry(brush,null,geometry); break;
            }
        }
    }
    private void DrawMeter(DrawingContext context, Rect bounds, bool health, bool gaugeOnly)
    {
        var p = History.Draft;
        if(!gaugeOnly && (p.Mode==HudMode.Classic || (health ? p.Health.Native : p.Ammo.Native)))
        { DrawNativeMeter(context,bounds,health); return; }
        if(gaugeOnly && !p.IndependentGauges) return;
        var style = new HudMeterRuntime(health ? p.Health : p.Ammo);
        float fraction = health ? Preview.Health / 99f : Preview.Ammo / 80f;
        var color = style.ColorFor(fraction);
        double unit = 5.625 * Transform.UnitScale * p.GlobalScale * p.Elements[gaugeOnly ? health ? "core.healthGauge" : "core.ammoGauge" : health ? "core.health" : "core.ammo"].Scale;
        IBrush ink = new SolidColorBrush(Color.FromRgb((byte)(color.X*255),(byte)(color.Y*255),(byte)(color.Z*255)));
        if (!gaugeOnly && style.Background) context.FillRectangle(new SolidColorBrush(Color.FromArgb(128,0,0,0)),bounds);
        if (!gaugeOnly && style.Number)
        {
            var text = new FormattedText((health ? Preview.Health : Preview.Ammo).ToString(),System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,new Typeface("monospace"),12*unit*style.NumberScale*p.TextScale,ink);
            context.DrawText(text,bounds.TopLeft+new Vector(4*unit,2*unit));
        }
        if (!style.Gauge || !gaugeOnly && p.IndependentGauges) return;
        Span<HudRectPrimitive> primitives = stackalloc HudRectPrimitive[3];
        int count = HudPrimitives.Meter(primitives,gaugeOnly ? 0 : 2,gaugeOnly ? 0 : 16,(health ? 40 : 54)*style.GaugeScale,style.GaugeThickness,
            fraction,new(color,1),style.Vertical);
        foreach (var r in primitives[..count])
        {
            var brush = new SolidColorBrush(Color.FromArgb((byte)(r.Color.W*255),(byte)(r.Color.X*255),(byte)(r.Color.Y*255),(byte)(r.Color.Z*255)));
            context.FillRectangle(brush,new Rect(bounds.X+r.X*unit,bounds.Y+r.Y*unit,r.Width*unit,r.Height*unit));
        }
    }
    private void DrawNativeMeter(DrawingContext context,Rect bounds,bool health)
    {
        var p=History.Draft;
        var objects=MphRead.Hud.HudElements.HunterObjects[PreviewHunter];
        var meter=health ? MphRead.Hud.HudElements.MainHealthbars[PreviewHunter] : MphRead.Hud.HudElements.AmmoBars[PreviewHunter];
        string asset=health ? objects.HealthBarA : objects.AmmoBar;
        double uy=5.625*Transform.UnitScale*p.GlobalScale*p.Elements[health ? "core.health" : "core.ammo"].Scale;
        double ux=uy*PreviewWidth/PreviewHeight*.75;
        int filled=(int)(meter.Length*Math.Clamp(health ? Preview.Health/99f : Preview.Ammo/80f,0,1));
        for(int tile=0;tile<(meter.Length+7)/8;tile++)
        {
            int amount=Math.Clamp(filled-tile*8,0,8);
            _native.Draw(context,asset,8-amount,new Point(bounds.X+(meter.Horizontal ? tile*8*ux : 0),bounds.Y-(meter.Horizontal ? 0 : tile*8*uy)),ux,uy);
        }
        var number=new FormattedText((health ? Preview.Health : Preview.Ammo).ToString("00"),System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,new Typeface("monospace"),8*uy*p.TextScale,Brushes.White);
        context.DrawText(number,new Point(bounds.X+meter.BarOffsetX*ux,bounds.Y+meter.BarOffsetY*uy));
        if(_native.Error is {} error)
            context.DrawText(new FormattedText(error,System.Globalization.CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface("sans-serif"),12,Brushes.Orange),bounds.TopLeft);
    }
    private void DrawCrosshair(DrawingContext context, Point center)
    {
        if (!_crosshair.Enabled) return;
        if(_crosshair.Native || History.Draft.Mode==HudMode.Classic)
        {
            var objects=MphRead.Hud.HudElements.HunterObjects[PreviewHunter];
            double unit=Surface.Width/256*History.Draft.GlobalScale*History.Draft.Elements["core.crosshair"].Scale*_crosshair.NativeScale*History.Draft.IconScale;
            using(context.PushOpacity(_crosshair.Opacity*History.Draft.NativeReticleOpacity))
                _native.Draw(context,Zoom ? objects.SniperReticle : objects.Reticle,0,center,unit,unit,true);
            return;
        }
        float scale = History.Draft.GlobalScale * History.Draft.Elements["core.crosshair"].Scale;
        // Preview uses physical crosshair pixels, as does the game.
        scale *= (float)(Surface.Width / PreviewWidth);
        for(int pass=0; pass<2; pass++)
        foreach(var crosshair in _crosshair.Parts)
        {
        if(pass==0 && crosshair.Outline<=0) continue;
        var c = crosshair.Color;
        IBrush brush = crosshair.HealthColor ? (Preview.Health>60 ? Brushes.Lime : Preview.Health>33 ? Brushes.Orange : Brushes.Red) : new SolidColorBrush(Color.FromRgb((byte)(c.X*255), (byte)(c.Y*255), (byte)(c.Z*255)));
        using (context.PushOpacity(crosshair.Opacity))
        {
            float outline = pass == 0 ? crosshair.Outline : 0;
            var oc = crosshair.OutlineColor;
            IBrush ink = pass == 0 ? new SolidColorBrush(Color.FromRgb((byte)(oc.X*255),(byte)(oc.Y*255),(byte)(oc.Z*255))) : brush;
            using var outlineOpacity = context.PushOpacity(pass == 0 ? crosshair.OutlineOpacity : 1);
            var dot = pass == 0 ? crosshair.OutlineDot : crosshair.Dot;
            for (int i=0; i<dot.Length; i+=3)
            {
                var geometry = new StreamGeometry();
                using (var g=geometry.Open())
                {
                    Point P(HudCrosshairVertex v) => new(center.X+v.X*scale,center.Y-v.Y*scale);
                    g.BeginFigure(P(dot[i]),true);g.LineTo(P(dot[i+1]));g.LineTo(P(dot[i+2]));g.EndFigure(true);
                }
                context.DrawGeometry(ink,null,geometry);
            }
            foreach (var bar in crosshair.Bars)
            {
                var (l,r,b,t) = Crosshair.EdgesOf(bar);
                context.FillRectangle(ink, new Rect(center.X+(l-outline)*scale, center.Y-(t+outline)*scale, (r-l+2*outline)*scale, (t-b+2*outline)*scale));
            }
            var ring = pass == 0 ? crosshair.OutlineRing : crosshair.Ring;
            for (int i = 2; i < ring.Length; i += 2)
            {
                var geometry = new StreamGeometry();
                using (var g = geometry.Open())
                {
                    Point P(HudCrosshairVertex v) => new(center.X+v.X*scale, center.Y-v.Y*scale);
                    g.BeginFigure(P(ring[i-2]), true); g.LineTo(P(ring[i-1])); g.LineTo(P(ring[i+1])); g.LineTo(P(ring[i])); g.EndFigure(true);
                }
                context.DrawGeometry(ink, null, geometry);
            }
        }
        }
    }
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e); Focus(); Point point = e.GetPosition(this);
        if (e.Pointer.Type == PointerType.Touch)
        {
            _touches[e.Pointer] = point;
            if (_touches.Count == 2 && _before != null)
            {
                _pinchDistance = TouchDistance(); _pinchScale = History.Draft.Elements[HudProfileDefaults.ElementIds[Selected]].Scale;
                e.Pointer.Capture(this); e.Handled = true; return;
            }
        }
        // Alt-click cycles behind the current selection through overlapping elements.
        bool cycle = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
        for (int step = 0; step < Labels.Length; step++)
        {
            int i = cycle ? (Selected - 1 - step + Labels.Length * 2) % Labels.Length : Labels.Length - 1 - step;
            if (ElementBounds(i).Inflate(8).Contains(point))
            {
                if(e.KeyModifiers.HasFlag(KeyModifiers.Shift)) { if(!Selection.Add(i)) Selection.Remove(i); }
                else if(!Selection.Contains(i)) { Selection.Clear(); Selection.Add(i); }
                Selected = i; Changed?.Invoke(); InvalidateVisual();
                if(RadarOnly && i!=4) continue;
                var element = History.Draft.Elements[HudProfileDefaults.ElementIds[i]];
                if (i == 0 || element.Locked) break;
                _before = History.Capture(); _start = point; _offset = new(element.OffsetX, element.OffsetY);
                _groupOffsets.Clear();
                foreach(int index in Selection)
                {
                    var member=History.Draft.Elements[HudProfileDefaults.ElementIds[index]];
                    if(index!=0 && !member.Locked) _groupOffsets[index]=new(member.OffsetX,member.OffsetY);
                }
                e.Pointer.Capture(this); e.Handled = true; break;
            }
        }
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e); if (_before == null) return;
        if (_touches.ContainsKey(e.Pointer)) _touches[e.Pointer] = e.GetPosition(this);
        if (_touches.Count >= 2 && _pinchDistance > 0)
        {
            History.Draft.Elements[HudProfileDefaults.ElementIds[Selected]].Scale = Math.Clamp(_pinchScale * (float)(TouchDistance() / _pinchDistance), .1f, 8);
            History.Draft.Mode = HudMode.Custom; InvalidateVisual(); return;
        }
        Vector delta = e.GetPosition(this) - _start;
        float fine = e.KeyModifiers.HasFlag(KeyModifiers.Control) ? .1f : 1;
        float x = _offset.X + (float)delta.X / Transform.UnitScale * fine, y = _offset.Y + (float)delta.Y / Transform.UnitScale * fine;
        if (GridSize > 0 && !e.KeyModifiers.HasFlag(KeyModifiers.Shift)) { x = MathF.Round(x/GridSize)*GridSize; y = MathF.Round(y/GridSize)*GridSize; }
        var element = History.Draft.Elements[HudProfileDefaults.ElementIds[Selected]]; element.OffsetX=x; element.OffsetY=y; History.Draft.Mode = HudMode.Custom;
        _guideX = _guideY = null;
        if (SnapGuides && !e.KeyModifiers.HasFlag(KeyModifiers.Shift)) SnapToGuides();
        var movement=new Vector2(element.OffsetX,element.OffsetY)-_offset;
        foreach(var member in _groupOffsets)
        {
            if(member.Key==Selected) continue;
            var layout=History.Draft.Elements[HudProfileDefaults.ElementIds[member.Key]];
            layout.OffsetX=member.Value.X+movement.X; layout.OffsetY=member.Value.Y+movement.Y;
        }
        InvalidateVisual();
    }
    internal void SnapToGuides()
    {
        if (Selected == 0 || Transform.UnitScale <= 0) return;
        Rect moving = ElementBounds(Selected), surface = Surface;
        Rect safe = surface.Deflate(new Thickness(surface.Width * History.Draft.SafeArea, surface.Height * History.Draft.SafeArea));
        double dx = 7, dy = 7;
        _guideX = _guideY = null;
        void Target(Rect rect)
        {
            foreach (double target in new[] { rect.Left, rect.Center.X, rect.Right })
                foreach (double source in new[] { moving.Left, moving.Center.X, moving.Right })
                    if (Math.Abs(target-source) < Math.Abs(dx)) { dx=target-source; _guideX=target; }
            foreach (double target in new[] { rect.Top, rect.Center.Y, rect.Bottom })
                foreach (double source in new[] { moving.Top, moving.Center.Y, moving.Bottom })
                    if (Math.Abs(target-source) < Math.Abs(dy)) { dy=target-source; _guideY=target; }
        }
        Target(surface); Target(safe);
        for (int i=1; i<Labels.Length; i++)
            if (i != Selected && !Selection.Contains(i) && History.Draft.Elements[HudProfileDefaults.ElementIds[i]].Enabled) Target(ElementBounds(i));
        var element = History.Draft.Elements[HudProfileDefaults.ElementIds[Selected]];
        if (_guideX != null) element.OffsetX += (float)dx / Transform.UnitScale;
        if (_guideY != null) element.OffsetY += (float)dy / Transform.UnitScale;
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e); _touches.Remove(e.Pointer); _pinchDistance = 0; EndDrag(); e.Pointer.Capture(null);
    }
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e) { base.OnPointerCaptureLost(e); _touches.Remove(e.Pointer); _pinchDistance = 0; EndDrag(); }
    private void EndDrag() { _guideX = _guideY = null; InvalidateVisual(); if (_before == null) return; History.Commit(_before); _before = null; Changed?.Invoke(); }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key is Key.Z or Key.Y)
        { if (e.Key == Key.Z) History.Undo(); else History.Redo(); }
        else if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down or Key.Delete or Key.R)
        {
            var id = HudProfileDefaults.ElementIds[Selected];
            var element = History.Draft.Elements[id];
            if (e.Key == Key.R) History.Edit(p => p.ResetElement(id));
            else if (e.Key == Key.Delete) History.Edit(p => p.Elements[id].Enabled = false);
            else if (!element.Locked && Selected != 0) History.Edit(p =>
            {
                float step = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 10 : 1;
                p.Elements[id].OffsetX += e.Key == Key.Left ? -step : e.Key == Key.Right ? step : 0;
                p.Elements[id].OffsetY += e.Key == Key.Up ? -step : e.Key == Key.Down ? step : 0;
            });
        }
        else return;
        if (!(e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key is Key.Z or Key.Y)) History.Draft.Mode = HudMode.Custom;
        e.Handled = true; Refresh(); Changed?.Invoke();
    }
}
