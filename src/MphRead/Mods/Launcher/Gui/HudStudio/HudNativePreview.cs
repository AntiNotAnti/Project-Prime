using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using MphRead.Hud;

namespace MphRead.Mods.Launcher.Gui;

/// <summary>View-owned, decoded cartridge sprites. No game state or GL resources.</summary>
internal sealed class HudNativePreview : IDisposable
{
    private readonly Dictionary<string,HudObject> _objects=new();
    private readonly Dictionary<(string,int,string?),WriteableBitmap> _frames=new();
    internal string? Error { get; private set; }
    private string? _root;
    private readonly Dictionary<(string,int,string?,double,double,float),WriteableBitmap> _reticles=new();
    internal bool Prepare()
    {
        if(_root!=null) return true;
        var paths=Paths.AllPaths;
        _root=paths.TryGetValue(Paths.MphKey,out var root) && Directory.Exists(root) ? root
            : paths.FirstOrDefault(p=>p.Key.StartsWith("AMH",StringComparison.Ordinal) && Directory.Exists(p.Value)).Value;
        if(string.IsNullOrEmpty(_root)) { _root=null; Error="Native preview needs extracted game files configured in the launcher.";return false; }
        Error=null;return true;
    }
    internal Bitmap? Frame(string path,int frame,string? tint=null)
    {
        if(!Prepare()) return null;
        try
        {
            if(!_objects.TryGetValue(path,out var asset)) _objects[path]=asset=HudInfo.GetHudObject(path,_root);
            int frameCount=asset.CharacterData.Count/(asset.Width*asset.Height);
            frame=Math.Clamp(frame,0,Math.Max(0,frameCount-1));
            if(_frames.TryGetValue((path,frame,tint),out var bitmap)) return bitmap;
            bitmap=new WriteableBitmap(new PixelSize(asset.Width,asset.Height),new Vector(96,96),PixelFormat.Bgra8888,AlphaFormat.Unpremul);
            byte[] row=new byte[asset.Width*4];
            using(var buffer=bitmap.Lock())
            for(int y=0;y<asset.Height;y++)
            {
                Array.Clear(row);
                for(int x=0;x<asset.Width;x++)
                {
                    int index=frame*asset.Width*asset.Height+(y/8)*(asset.Width/8)*64+(x/8)*64+(y%8)*8+x%8;
                    int palette=asset.CharacterData[index];
                    if(palette==0 || palette>=asset.PaletteData.Count) continue;
                    var c=asset.PaletteData[palette]; int at=x*4;
                    if(tint!=null) { var t=MphRead.Mods.Render.Hud.HudColor.Parse(tint);float light=Math.Max(c.Red,Math.Max(c.Green,c.Blue));row[at]=(byte)(light*t.Z);row[at+1]=(byte)(light*t.Y);row[at+2]=(byte)(light*t.X); }
                    else { row[at]=c.Blue;row[at+1]=c.Green;row[at+2]=c.Red; } row[at+3]=255;
                }
                Marshal.Copy(row,0,buffer.Address+y*buffer.RowBytes,row.Length);
            }
            _frames[(path,frame,tint)]=bitmap;return bitmap;
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or ArgumentException or IndexOutOfRangeException)
        { Error="Could not read native HUD sprite: "+ex.Message;return null; }
    }
    internal void Draw(DrawingContext context,string asset,int frame,Point origin,double unitX,double unitY,bool centered=false,string? tint=null)
    {
        if(Frame(asset,frame,tint) is not {} bitmap) return;
        double w=bitmap.PixelSize.Width*unitX,h=bitmap.PixelSize.Height*unitY;
        context.DrawImage(bitmap,new Rect(centered ? origin.X-w/2 : origin.X,centered ? origin.Y-h/2 : origin.Y,w,h));
    }
    internal void DrawReticle(DrawingContext context,string path,int frame,Point center,double unitX,double unitY,
        float thickness,double previewRatio,string? tint=null)
    {
        if (!Prepare()) return;
        try
        {
            if (!_objects.TryGetValue(path,out var asset)) _objects[path]=asset=HudInfo.GetHudObject(path,_root);
            int count=asset.CharacterData.Count/(asset.Width*asset.Height);
            frame=Math.Clamp(frame,0,Math.Max(0,count-1));
            var key=(path,frame,tint,unitX,unitY,thickness);
            var field=MphRead.Mods.Render.Hud.NativeReticleField.Get(asset.CharacterData,asset.Width,asset.Height,frame);
            double width=field.Width/(double)MphRead.Mods.Render.Hud.NativeReticleField.Samples*unitX;
            double height=field.Height/(double)MphRead.Mods.Render.Hud.NativeReticleField.Samples*unitY;
            if (!_reticles.TryGetValue(key,out var bitmap))
            {
                if (_reticles.Count>=32) { foreach(var old in _reticles.Values)old.Dispose();_reticles.Clear(); }
                int w=Math.Clamp((int)Math.Ceiling(width),1,2048),h=Math.Clamp((int)Math.Ceiling(height),1,2048);
                bitmap=new WriteableBitmap(new PixelSize(w,h),new Vector(96,96),PixelFormat.Bgra8888,AlphaFormat.Unpremul);
                float pixel=(float)(previewRatio/Math.Max(.00001,Math.Min(unitX,unitY)));
                var ink=tint==null ? System.Numerics.Vector3.One : MphRead.Mods.Render.Hud.HudColor.Parse(tint);
                byte[] row=new byte[w*4];
                using(var buffer=bitmap.Lock())for(int y=0;y<h;y++)
                {
                    for(int x=0;x<w;x++)
                    {
                        float fx=(x+.5f)*field.Width/w-.5f,fy=(y+.5f)*field.Height/h-.5f;
                        int x0=Math.Clamp((int)MathF.Floor(fx),0,field.Width-1),y0=Math.Clamp((int)MathF.Floor(fy),0,field.Height-1);
                        int x1=Math.Min(x0+1,field.Width-1),y1=Math.Min(y0+1,field.Height-1);
                        float tx=Math.Clamp(fx-x0,0,1),ty=Math.Clamp(fy-y0,0,1);
                        float a=field.Distance[y0*field.Width+x0]*(1-tx)+field.Distance[y0*field.Width+x1]*tx;
                        float b=field.Distance[y1*field.Width+x0]*(1-tx)+field.Distance[y1*field.Width+x1]*tx;
                        float v=Math.Clamp((a*(1-ty)+b*ty+thickness*pixel)/pixel+.5f,0,1);
                        v=v*v*(3-2*v);
                        int source=field.Source[y0*field.Width+x0],at=x*4;
                        ColorRgba color=default;
                        if(source>=0)
                        {
                            int sx=source%asset.Width,sy=source/asset.Width;
                            int index=frame*asset.Width*asset.Height+(sy/8)*(asset.Width/8)*64+(sx/8)*64+(sy%8)*8+sx%8;
                            int palette=asset.CharacterData[index];
                            if(palette<asset.PaletteData.Count)color=asset.PaletteData[palette];
                        }
                        float light=Math.Max(color.Red,Math.Max(color.Green,color.Blue));
                        row[at]=tint==null?color.Blue:(byte)(light*ink.Z);
                        row[at+1]=tint==null?color.Green:(byte)(light*ink.Y);
                        row[at+2]=tint==null?color.Red:(byte)(light*ink.X);row[at+3]=(byte)(v*255);
                    }
                    Marshal.Copy(row,0,buffer.Address+y*buffer.RowBytes,row.Length);
                }
                _reticles[key]=bitmap;
            }
            context.DrawImage(bitmap,new Rect(center.X-width/2,center.Y-height/2,width,height));
        }
        catch(Exception ex) when(ex is IOException or ArgumentException or IndexOutOfRangeException)
        { Error="Could not render native reticle: "+ex.Message; }
    }
    public void Dispose()
    { foreach(var bitmap in _reticles.Values) bitmap.Dispose();_reticles.Clear();foreach(var bitmap in _frames.Values) bitmap.Dispose();_frames.Clear();_objects.Clear();_root=null; }
}
