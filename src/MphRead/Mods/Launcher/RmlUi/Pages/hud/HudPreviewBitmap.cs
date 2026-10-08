#if MPHREAD_RMLUI_POC && !MPHREAD_SERVER
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using MphRead.Hud;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Render;
using MphRead.Mods.Render.Hud;

namespace MphRead.Mods.Launcher.RmlUi.Pages.Hud;

public readonly record struct HudPreviewResult(string Path, string Warning);

/// <summary>Native texture preview using the same crosshair, meter and radar geometry as the game.</summary>
public sealed class HudPreviewBitmap
{
    private readonly object _gate = new();
    private readonly string _directory;
    private readonly string? _root;
    private readonly Dictionary<string,HudObject> _objects = new(StringComparer.Ordinal);
    public HudPreviewBitmap(string? directory = null, string? nativeRoot = null)
    {
        _directory = directory ?? Path.Combine(LauncherPrefs.Directory,"rmlui-hud-preview-cache");
        if(nativeRoot != null) _root=nativeRoot;
        else
        {
            var paths=Paths.AllPaths;
            _root=paths.TryGetValue(Paths.MphKey,out string? primary)&&Directory.Exists(primary) ? primary
                : paths.FirstOrDefault(p=>p.Key.StartsWith("AMH",StringComparison.Ordinal)&&Directory.Exists(p.Value)).Value;
        }
    }
    public HudPreviewResult Build(HudEditorSnapshot state,CancellationToken cancellation)
    {
        lock(_gate) return BuildCore(state,cancellation);
    }
    private HudPreviewResult BuildCore(HudEditorSnapshot state,CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        var p=HudProfileStore.Parse(state.ProfileJson);
        string signature=state.ProfileJson+$"|{_root}|{state.CanvasWidth}|{state.CanvasHeight}|{state.PreviewWidth}|{state.PreviewHeight}|{state.PreviewHunter}|{state.Scenario}|{state.CrosshairTarget}|{state.GridSize}";
        string name=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(signature)));
        string path=Path.Combine(_directory,name+".tga");
        float down=Math.Min(1,1024f/Math.Max(state.CanvasWidth,state.CanvasHeight));
        int width=Math.Max(1,(int)Math.Round(state.CanvasWidth*down)),height=Math.Max(1,(int)Math.Round(state.CanvasHeight*down));
        var canvas=new Pixels(width,height,down,state.Surface,cancellation);
        string warning="";
        var preview=HudPreviewState.For(state.Scenario);
        var transform=new HudTransform(state.Surface.Width,state.Surface.Height,p.SafeArea);
        canvas.Rect(state.Surface,new(.063f,.094f,.125f,1));
        float step=Math.Max(12,state.GridSize*transform.UnitScale);
        if(state.GridSize>0)
        {
            for(float x=state.Surface.X;x<state.Surface.Right;x+=step)canvas.Line(new(x,state.Surface.Y),new(x,state.Surface.Bottom),.5f,new(.145f,.188f,.231f,1));
            for(float y=state.Surface.Y;y<state.Surface.Bottom;y+=step)canvas.Line(new(state.Surface.X,y),new(state.Surface.Right,y),.5f,new(.145f,.188f,.231f,1));
        }
        canvas.Border(new(state.Surface.X+state.Surface.Width*p.SafeArea,state.Surface.Y+state.Surface.Height*p.SafeArea,state.Surface.Width*(1-2*p.SafeArea),state.Surface.Height*(1-2*p.SafeArea)),1,new(.5f,.5f,.5f,1));
        Span<HudRectPrimitive> rectangles=stackalloc HudRectPrimitive[3];
        foreach(var element in state.Elements)
        {
            cancellation.ThrowIfCancellationRequested();
            float alpha=element.Opacity;
            if(element.Index==0)
            {
                CrosshairProfile crosshair=state.CrosshairTarget==0 ? p.Crosshair : state.CrosshairTarget==10 ? CrosshairProperties.Resolve(CrosshairProperties.Resolve(p.Crosshair,p.WeaponCrosshairs[4]),p.ZoomCrosshair)
                    : CrosshairProperties.Resolve(p.Crosshair,p.WeaponCrosshairs[state.CrosshairTarget-1]);
                if(state.Scenario==HudPreviewScenario.Zoomed)crosshair=CrosshairProperties.Resolve(CrosshairProperties.Resolve(p.Crosshair,p.WeaponCrosshairs[4]),p.ZoomCrosshair);
                var runtime=new CrosshairRuntime(crosshair);if(!runtime.Enabled)continue;
                if(runtime.Native||p.Mode==HudMode.Classic)
                {
                    var objects=HudElements.HunterObjects[state.PreviewHunter];
                    float unit=state.Surface.Width/256*p.GlobalScale*p.Elements["core.crosshair"].Scale*runtime.NativeScale*p.IconScale;
                    Sprite(canvas,state.Scenario==HudPreviewScenario.Zoomed||state.CrosshairTarget==10 ? objects.SniperReticle : objects.Reticle,0,
                        element.Bounds.Center,unit,unit,alpha*runtime.Opacity*p.NativeReticleOpacity,true,ref warning);
                }
                else DrawCrosshair(canvas,runtime,element.Bounds.Center,p.GlobalScale*p.Elements["core.crosshair"].Scale*state.Surface.Width/state.PreviewWidth,alpha,preview.Health);
            }
            else if(element.Index==3&&(p.Inventory.Native||p.Mode==HudMode.Classic))
            {
                float unit=5.625f*transform.UnitScale*element.Bounds.Width/Math.Max(16,248*transform.UnitScale)*p.IconScale;
                Sprite(canvas,HudElements.HunterObjects[state.PreviewHunter].WeaponIcon,Math.Max(0,state.CrosshairTarget-1),new(element.Bounds.X,element.Bounds.Y),unit,unit,alpha,false,ref warning);
            }
            else if(element.Index==4)DrawRadar(canvas,p,element,transform.UnitScale,alpha);
            else if(element.Index is 1 or 2 or 12 or 13)
            {
                bool health=element.Index is 1 or 12,gaugeOnly=element.Index>=12;
                if(!gaugeOnly&&(p.Mode==HudMode.Classic||(health?p.Health.Native:p.Ammo.Native)))
                {
                    var objects=HudElements.HunterObjects[state.PreviewHunter];var meter=health?HudElements.MainHealthbars[state.PreviewHunter]:HudElements.AmmoBars[state.PreviewHunter];
                    float uy=5.625f*transform.UnitScale*p.GlobalScale*p.Elements[element.Id].Scale;
                    float ux=uy*state.PreviewWidth/state.PreviewHeight*.75f;int filled=(int)(meter.Length*Math.Clamp(health?preview.Health/99f:preview.Ammo/80f,0,1));
                    for(int tile=0;tile<(meter.Length+7)/8;tile++)Sprite(canvas,health?objects.HealthBarA:objects.AmmoBar,8-Math.Clamp(filled-tile*8,0,8),
                        new(element.Bounds.X+(meter.Horizontal?tile*8*ux:0),element.Bounds.Y-(meter.Horizontal?0:tile*8*uy)),ux,uy,alpha,false,ref warning);
                }
                else
                {
                    if(gaugeOnly&&!p.IndependentGauges)continue;
                    var style=new HudMeterRuntime(health?p.Health:p.Ammo);float fraction=health?preview.Health/99f:preview.Ammo/80f;
                    float unit=5.625f*transform.UnitScale*p.GlobalScale*p.Elements[element.Id].Scale;
                    if(!gaugeOnly&&style.Background)canvas.Rect(element.Bounds,new(0,0,0,.5f*alpha));
                    if(!style.Gauge||!gaugeOnly&&p.IndependentGauges)continue;
                    int count=HudPrimitives.Meter(rectangles,gaugeOnly?0:2,gaugeOnly?0:16,(health?40:54)*style.GaugeScale,style.GaugeThickness,fraction,new(style.ColorFor(fraction),1),style.Vertical);
                    foreach(var rect in rectangles[..count])canvas.Rect(new(element.Bounds.X+rect.X*unit,element.Bounds.Y+rect.Y*unit,rect.Width*unit,rect.Height*unit),rect.Color with { W=rect.Color.W*alpha });
                }
            }
            else
            {
                canvas.Rect(element.Bounds,new(.2f,.267f,.333f,.31f*alpha));canvas.Border(element.Bounds,1,new(.44f,.5f,.565f,alpha));
            }
        }
        cancellation.ThrowIfCancellationRequested();
        Directory.CreateDirectory(_directory);string temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try { File.WriteAllBytes(temporary,canvas.Tga());cancellation.ThrowIfCancellationRequested();File.Move(temporary,path,true); }
        finally { if(File.Exists(temporary))File.Delete(temporary); }
        return new(path,warning);
    }
    private void Sprite(Pixels canvas,string path,int frame,Vector2 origin,float ux,float uy,float alpha,bool center,ref string warning)
    {
        if(String.IsNullOrEmpty(_root)) { warning="Native preview needs extracted game files configured in the launcher.";return; }
        try
        {
            if(!_objects.TryGetValue(path,out HudObject? asset))_objects[path]=asset=HudInfo.GetHudObject(path,_root);
            int frames=asset.CharacterData.Count/(asset.Width*asset.Height);frame=Math.Clamp(frame,0,Math.Max(0,frames-1));
            if(center)origin-=new Vector2(asset.Width*ux,asset.Height*uy)/2;
            for(int y=0;y<asset.Height;y++)for(int x=0;x<asset.Width;x++)
            {
                int index=frame*asset.Width*asset.Height+(y/8)*(asset.Width/8)*64+(x/8)*64+(y%8)*8+x%8;
                int palette=asset.CharacterData[index];if(palette==0||palette>=asset.PaletteData.Count)continue;
                var color=asset.PaletteData[palette];canvas.Rect(new(origin.X+x*ux,origin.Y+y*uy,ux,uy),new(color.Red/255f,color.Green/255f,color.Blue/255f,alpha));
            }
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or ArgumentException or IndexOutOfRangeException)
        { warning="Could not read native HUD sprite: "+ex.Message; }
    }
    private static void DrawCrosshair(Pixels canvas,CrosshairRuntime runtime,Vector2 center,float scale,float alpha,int health)
    {
        for(int pass=0;pass<2;pass++)foreach(var part in runtime.Parts)
        {
            if(pass==0&&part.Outline<=0)continue;
            Vector3 color=pass==0?part.OutlineColor:part.HealthColor ? health>60?new(0,1,0):health>33?new(1,.65f,0):new(1,0,0) : part.Color;
            var ink=new Vector4(color,alpha*part.Opacity*(pass==0?part.OutlineOpacity:1));float outline=pass==0?part.Outline:0;
            Vector2 Point(HudCrosshairVertex vertex)=>center+new Vector2(vertex.X,-vertex.Y)*scale;
            var dot=pass==0?part.OutlineDot:part.Dot;for(int i=0;i<dot.Length;i+=3)canvas.Triangle(Point(dot[i]),Point(dot[i+1]),Point(dot[i+2]),ink);
            foreach(var bar in part.Bars) { var (left,right,bottom,top)=Crosshair.EdgesOf(bar);canvas.Rect(new(center.X+(left-outline)*scale,center.Y-(top+outline)*scale,(right-left+2*outline)*scale,(top-bottom+2*outline)*scale),ink); }
            var ring=pass==0?part.OutlineRing:part.Ring;for(int i=2;i<ring.Length;i+=2) { canvas.Triangle(Point(ring[i-2]),Point(ring[i-1]),Point(ring[i+1]),ink);canvas.Triangle(Point(ring[i-2]),Point(ring[i+1]),Point(ring[i]),ink); }
        }
    }
    private static void DrawRadar(Pixels canvas,HudProfile p,HudEditorElement element,float unit,float alpha)
    {
        var style=new HudRadarRuntime(p.Radar);float scale=unit*p.GlobalScale*p.Elements[element.Id].Scale;float radius=113.724f*style.RadiusScale;
        var palette=Radar.PaletteOf;Vector4 Color(OpenTK.Mathematics.Vector4 value)=>new(value.X,value.Y,value.Z,value.W);
        Span<HudShapePrimitive> shapes=stackalloc HudShapePrimitive[HudRadarGeometry.FrameCapacity];
        void Draw(ReadOnlySpan<HudShapePrimitive> primitives)
        {
            Span<Vector2> polygon=stackalloc Vector2[6];var tint=HudColor.Parse(element.Color);
            foreach(var primitive in primitives)
            {
                var color=primitive.Color;if(p.ReduceTransparency&&color.W>0)color.W=Math.Max(.85f,color.W);
                color=new(color.X*tint.X,color.Y*tint.Y,color.Z*tint.Z,color.W*alpha);
                Vector2 position=element.Bounds.Center+new Vector2(primitive.A.X,-primitive.A.Y)*scale;float r=primitive.Radius*scale;
                switch(primitive.Kind)
                {
                    case HudShapeKind.Disc:canvas.Disc(position,r,0,color);break;
                    case HudShapeKind.Ring:canvas.Disc(position,r,primitive.Thickness*scale,color);break;
                    case HudShapeKind.Square:canvas.Rect(new(position.X-r,position.Y-r,2*r,2*r),color);break;
                    case HudShapeKind.Line:canvas.Line(position,element.Bounds.Center+new Vector2(primitive.B.X,-primitive.B.Y)*scale,primitive.Thickness*scale,color);break;
                    default:int count=HudRadarGeometry.Polygon(primitive,polygon);for(int i=1;i<count-1;i++)canvas.Triangle(position+new Vector2(polygon[0].X,-polygon[0].Y)*scale,position+new Vector2(polygon[i].X,-polygon[i].Y)*scale,position+new Vector2(polygon[i+1].X,-polygon[i+1].Y)*scale,color);break;
                }
            }
        }
        int count=HudRadarGeometry.Build(shapes,style,radius,p.RadarBackground,p.RadarOutlines,Color(palette.Background),Color(palette.Ring),Color(palette.Cone),5.85f,1.125f,p.ReduceMotion,p.ReduceTransparency);Draw(shapes[..count]);
        var basis=HudRadarProjection.BuildBasis(new(.5f,0,.8660254f),style.Orientation);
        if(!p.ReduceMotion&&style.Hunters)foreach(var contact in HudRadarPreview.Contacts)
        {
            if(contact.Kind!=RadarContactKind.Hunter)continue;
            for(int i=style.TrailSamples;i>0;i--) { var trail=contact with { Position=contact.Position-new Vector3(i*.7f,0,i*.4f),Alpha=.35f*(1-(i-1)/4f) };if(!HudRadarProjection.Project(trail,default,basis,style,radius,out var projected))continue;count=HudRadarGeometry.BuildContact(shapes,trail,projected,basis,style,5.625f,1.125f,p.ReduceMotion);Draw(shapes[..count]); }
        }
        for(int pass=0;pass<3;pass++)foreach(var contact in HudRadarPreview.Contacts)
        { if(HudRadarGeometry.ContactLayer(contact.Kind,style.Style)!=pass||!HudRadarProjection.Visible(contact.Kind,style)||!HudRadarProjection.Project(contact,default,basis,style,radius,out var projected))continue;count=HudRadarGeometry.BuildContact(shapes,contact,projected,basis,style,5.625f,1.125f,p.ReduceMotion);Draw(shapes[..count]); }
        count=HudRadarGeometry.BuildSelfMarker(shapes,style,MathF.PI/6,basis,5.625f,Color(palette.Player));Draw(shapes[..count]);
    }
    private sealed class Pixels
    {
        private readonly int _width,_height;private readonly float _scale;private readonly HudEditorRect _clip;private readonly byte[] _rgb;private readonly CancellationToken _cancellation;
        public Pixels(int width,int height,float scale,HudEditorRect clip,CancellationToken cancellation) { _width=width;_height=height;_scale=scale;_clip=clip;_rgb=new byte[width*height*3];_cancellation=cancellation; }
        private void Blend(int x,int y,Vector4 color)
        {
            if(x<0||y<0||x>=_width||y>=_height||!_clip.Contains(new(x/_scale,y/_scale)))return;
            int at=(y*_width+x)*3;float alpha=Math.Clamp(color.W,0,1);_rgb[at]=(byte)Math.Clamp(_rgb[at]*(1-alpha)+color.X*255*alpha,0,255);_rgb[at+1]=(byte)Math.Clamp(_rgb[at+1]*(1-alpha)+color.Y*255*alpha,0,255);_rgb[at+2]=(byte)Math.Clamp(_rgb[at+2]*(1-alpha)+color.Z*255*alpha,0,255);
        }
        private void Area(float x,float y,float w,float h,Action<int,int,Vector2> pixel)
        {
            int left=Math.Clamp((int)MathF.Floor(x*_scale),0,_width),right=Math.Clamp((int)MathF.Ceiling((x+w)*_scale),0,_width),top=Math.Clamp((int)MathF.Floor(y*_scale),0,_height),bottom=Math.Clamp((int)MathF.Ceiling((y+h)*_scale),0,_height);
            for(int row=top;row<bottom;row++) { _cancellation.ThrowIfCancellationRequested();for(int column=left;column<right;column++)pixel(column,row,new((column+.5f)/_scale,(row+.5f)/_scale)); }
        }
        public void Rect(HudEditorRect rect,Vector4 color)=>Area(rect.X,rect.Y,rect.Width,rect.Height,(x,y,_)=>Blend(x,y,color));
        public void Border(HudEditorRect rect,float thickness,Vector4 color)
        { Rect(new(rect.X,rect.Y,rect.Width,thickness),color);Rect(new(rect.X,rect.Bottom-thickness,rect.Width,thickness),color);Rect(new(rect.X,rect.Y,thickness,rect.Height),color);Rect(new(rect.Right-thickness,rect.Y,thickness,rect.Height),color); }
        public void Line(Vector2 a,Vector2 b,float thickness,Vector4 color)
        { Vector2 delta=b-a;float length=delta.LengthSquared();float r=Math.Max(.5f,thickness/2);Area(Math.Min(a.X,b.X)-r,Math.Min(a.Y,b.Y)-r,Math.Abs(a.X-b.X)+2*r,Math.Abs(a.Y-b.Y)+2*r,(x,y,p)=> {float t=length==0?0:Math.Clamp(Vector2.Dot(p-a,delta)/length,0,1);if(Vector2.DistanceSquared(p,a+t*delta)<=r*r)Blend(x,y,color);}); }
        public void Disc(Vector2 center,float radius,float thickness,Vector4 color)
        { float outer=radius+thickness/2,inner=thickness==0?0:Math.Max(0,radius-thickness/2);Area(center.X-outer,center.Y-outer,outer*2,outer*2,(x,y,p)=> {float d=Vector2.DistanceSquared(p,center);if(d<=outer*outer&&d>=inner*inner)Blend(x,y,color);}); }
        public void Triangle(Vector2 a,Vector2 b,Vector2 c,Vector4 color)
        { static float Cross(Vector2 u,Vector2 v)=>u.X*v.Y-u.Y*v.X;float orientation=Cross(b-a,c-a);if(Math.Abs(orientation)<.00001f)return;float x=Math.Min(a.X,Math.Min(b.X,c.X)),y=Math.Min(a.Y,Math.Min(b.Y,c.Y));Area(x,y,Math.Max(a.X,Math.Max(b.X,c.X))-x,Math.Max(a.Y,Math.Max(b.Y,c.Y))-y,(px,py,p)=> {float aa=Cross(b-a,p-a),bb=Cross(c-b,p-b),cc=Cross(a-c,p-c);if(orientation>0?aa>=0&&bb>=0&&cc>=0:aa<=0&&bb<=0&&cc<=0)Blend(px,py,color);}); }
        public byte[] Tga()
        { byte[] output=new byte[18+_rgb.Length];output[2]=2;output[12]=(byte)_width;output[13]=(byte)(_width>>8);output[14]=(byte)_height;output[15]=(byte)(_height>>8);output[16]=24;output[17]=0x20;for(int i=0;i<_rgb.Length;i+=3) { output[18+i]=_rgb[i+2];output[19+i]=_rgb[i+1];output[20+i]=_rgb[i]; }return output; }
    }
}
#endif
