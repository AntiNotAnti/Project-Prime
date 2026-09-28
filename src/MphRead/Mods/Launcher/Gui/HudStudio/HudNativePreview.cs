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
    private readonly Dictionary<(string,int),WriteableBitmap> _frames=new();
    internal string? Error { get; private set; }
    private string? _root;
    internal bool Prepare()
    {
        if(_root!=null) return true;
        var paths=Paths.AllPaths;
        _root=paths.TryGetValue(Paths.MphKey,out var root) && Directory.Exists(root) ? root
            : paths.FirstOrDefault(p=>p.Key.StartsWith("AMH",StringComparison.Ordinal) && Directory.Exists(p.Value)).Value;
        if(string.IsNullOrEmpty(_root)) { _root=null; Error="Native preview needs extracted game files configured in the launcher.";return false; }
        Error=null;return true;
    }
    internal Bitmap? Frame(string path,int frame)
    {
        if(!Prepare()) return null;
        try
        {
            if(!_objects.TryGetValue(path,out var asset)) _objects[path]=asset=HudInfo.GetHudObject(path,_root);
            int frameCount=asset.CharacterData.Count/(asset.Width*asset.Height);
            frame=Math.Clamp(frame,0,Math.Max(0,frameCount-1));
            if(_frames.TryGetValue((path,frame),out var bitmap)) return bitmap;
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
                    row[at]=c.Blue;row[at+1]=c.Green;row[at+2]=c.Red;row[at+3]=255;
                }
                Marshal.Copy(row,0,buffer.Address+y*buffer.RowBytes,row.Length);
            }
            _frames[(path,frame)]=bitmap;return bitmap;
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or ArgumentException or IndexOutOfRangeException)
        { Error="Could not read native HUD sprite: "+ex.Message;return null; }
    }
    internal void Draw(DrawingContext context,string asset,int frame,Point origin,double unitX,double unitY,bool centered=false)
    {
        if(Frame(asset,frame) is not {} bitmap) return;
        double w=bitmap.PixelSize.Width*unitX,h=bitmap.PixelSize.Height*unitY;
        context.DrawImage(bitmap,new Rect(centered ? origin.X-w/2 : origin.X,centered ? origin.Y-h/2 : origin.Y,w,h));
    }
    public void Dispose()
    { foreach(var bitmap in _frames.Values) bitmap.Dispose();_frames.Clear();_objects.Clear();_root=null; }
}
