using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.VisualTree;
using MphRead.Mods.MapEditor;
using MphRead.Mods.MapGen;
using MphRead.Mods.Render;
#if !ANDROID && !MPHREAD_SERVER
using MphRead.Mods.StudioReplay;
#endif

namespace MphRead.Mods.Launcher.Gui;

internal sealed record MapBrowserThumbnailPixels(int Width,int Height,byte[] Rgba,string Description);
internal sealed class MapBrowserThumbnail : UserControl
{
    private readonly Func<CancellationToken,Task<MapBrowserThumbnailPixels>> _load;
    private readonly Action<Task> _track;
    private readonly Func<CancellationToken> _owner;
    private readonly Image _image=new(){Width=128,Height=80,Stretch=Avalonia.Media.Stretch.Uniform,HorizontalAlignment=HorizontalAlignment.Left};
    private readonly TextBlock _description=new(){Text="Loading thumbnail…",FontSize=11,TextWrapping=Avalonia.Media.TextWrapping.Wrap};
    private CancellationTokenSource? _request;
    private WriteableBitmap? _bitmap;
    public MapBrowserThumbnail(Func<CancellationToken,Task<MapBrowserThumbnailPixels>> load,Action<Task> track,Func<CancellationToken> owner)
    {
        _load=load;_track=track;_owner=owner;
        Content=new StackPanel {Spacing=4,Children={_image,_description}};
    }
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs args)
    {
        base.OnAttachedToVisualTree(args);_track(LoadAsync());
    }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs args)
    {
        _request?.Cancel();_image.Source=null;_bitmap?.Dispose();_bitmap=null;
        base.OnDetachedFromVisualTree(args);
    }
    internal void ResumePendingPreview()
    {
        if(_image.Source is null && this.IsAttachedToVisualTree()) _track(LoadAsync());
    }
    private async Task LoadAsync()
    {
        _request?.Cancel();var request=CancellationTokenSource.CreateLinkedTokenSource(_owner());_request=request;
        _description.Text="Loading thumbnail…";
        try
        {
            var pixels=await _load(request.Token);request.Token.ThrowIfCancellationRequested();
            if(!ReferenceEquals(_request,request))return;
            var bitmap=new WriteableBitmap(new PixelSize(pixels.Width,pixels.Height),new Vector(96,96),PixelFormat.Bgra8888,AlphaFormat.Unpremul);
            using(var buffer=bitmap.Lock())
            {
                var row=new byte[pixels.Width*4];
                for(int y=0;y<pixels.Height;y++)
                {
                    for(int x=0;x<pixels.Width;x++){int p=(y*pixels.Width+x)*4;row[x*4]=pixels.Rgba[p+2];row[x*4+1]=pixels.Rgba[p+1];row[x*4+2]=pixels.Rgba[p];row[x*4+3]=pixels.Rgba[p+3];}
                    Marshal.Copy(row,0,buffer.Address+y*buffer.RowBytes,row.Length);
                }
            }
            _bitmap?.Dispose();_bitmap=bitmap;_image.Source=bitmap;_description.Text=pixels.Description;
        }
        catch(OperationCanceledException) { }
        catch(Exception error){if(ReferenceEquals(_request,request))_description.Text="Thumbnail unavailable · "+error.Message;}
        finally{if(ReferenceEquals(_request,request))_request=null;request.Dispose();}
    }
}

internal sealed partial class MapStudioScreen
{
    private CancellationTokenSource _thumbnailLifetime=new();
    private readonly SemaphoreSlim _thumbnailGate=new(2);
    private readonly List<Task> _thumbnailTasks=new();
    private readonly List<WeakReference<MapBrowserThumbnail>> _thumbnailControls=new();
    private bool _thumbnailClosing,_thumbnailResourcesDisposed,_thumbnailPaused;
    private MapBrowserThumbnail NewBrowserThumbnail(Func<CancellationToken,Task<MapBrowserThumbnailPixels>> load)
    {
        _thumbnailControls.RemoveAll(reference=>!reference.TryGetTarget(out _));
        var control=new MapBrowserThumbnail(load,TrackBrowserThumbnailTask,()=>_thumbnailLifetime.Token);
        _thumbnailControls.Add(new(control));return control;
    }
    private void PauseAssetThumbnails()
    {
        if(_thumbnailResourcesDisposed || _thumbnailClosing || _thumbnailPaused)return;
        _thumbnailPaused=true;_thumbnailLifetime.Cancel();
    }
    internal void ResumeAssetThumbnails()
    {
        if(_thumbnailResourcesDisposed || _thumbnailClosing || !_thumbnailPaused)return;
        _thumbnailLifetime.Dispose();_thumbnailLifetime=new();_thumbnailPaused=false;
        foreach(var reference in _thumbnailControls.ToArray())
            if(reference.TryGetTarget(out var control))control.ResumePendingPreview();
    }
    private void TrackBrowserThumbnailTask(Task task)
    {_thumbnailTasks.RemoveAll(task=>task.IsCompleted);_thumbnailTasks.Add(task);}
    private Task<MapBrowserThumbnailPixels> LoadBrowserThumbnailAsync(Func<CancellationToken,MapBrowserThumbnailPixels> prepare,CancellationToken cancellation)
    {
        if(_thumbnailClosing || _thumbnailPaused) return Task.FromCanceled<MapBrowserThumbnailPixels>(new CancellationToken(true));
        async Task<MapBrowserThumbnailPixels> Run()
        {
            using var linked=CancellationTokenSource.CreateLinkedTokenSource(cancellation,_thumbnailLifetime.Token);
            MapBrowserThumbnailPixels? pixels=null;
            await _services.RunJobAsync("Prepare map asset thumbnail",async token=>
            {
                await _thumbnailGate.WaitAsync(token);
                try{pixels=await Task.Run(()=>prepare(token),token);}
                finally{_thumbnailGate.Release();}
            },linked.Token);
            linked.Token.ThrowIfCancellationRequested();
            return pixels??throw new InvalidOperationException("Thumbnail preparation returned no pixels.");
        }
        var task=Run();_thumbnailTasks.RemoveAll(task=>task.IsCompleted);_thumbnailTasks.Add(task);return task;
    }
    internal Task WaitForAssetThumbnailsAsync()=>Task.WhenAll(_thumbnailTasks.ToArray().Select(DrainThumbnailAsync));
    private static async Task DrainThumbnailAsync(Task task)
    {
        // Rebuilding or closing a browser cancels its old preview requests. Those
        // requests must finish, but their cancellation does not cancel the owner.
        try { await task; }catch(OperationCanceledException) { }
    }
    private async Task StopAssetThumbnailsAsync()
    {
        _thumbnailClosing=true;_thumbnailLifetime.Cancel();
        try{await Task.WhenAll(_thumbnailTasks.ToArray());}catch(OperationCanceledException){}catch(Exception){}
    }
    private void DisposeAssetThumbnails()
    {
        if(_thumbnailResourcesDisposed)return;_thumbnailResourcesDisposed=true;
        var drain=StopAssetThumbnailsAsync();
        if(drain.IsCompleted){_thumbnailGate.Dispose();_thumbnailLifetime.Dispose();}
        else _=drain.ContinueWith(_=>{_thumbnailGate.Dispose();_thumbnailLifetime.Dispose();},CancellationToken.None,TaskContinuationOptions.ExecuteSynchronously,TaskScheduler.Default);
    }
    private void AddTextureThumbnail(Panel rows,MapAsset asset)
    {
        var definition=new MapDefinition {BaseDirectory=_document!.Project.Definition.BaseDirectory??_services.MapLibraryDirectory,BundlePath=_document.Project.Definition.BundlePath};
        string path=asset.Path;
        rows.Children.Add(NewBrowserThumbnail(cancellation=>LoadBrowserThumbnailAsync(token=>
        {
            token.ThrowIfCancellationRequested();byte[] bytes=MapAssets.Read(definition,path);
            if(Path.GetExtension(path).Equals(".tex",StringComparison.OrdinalIgnoreCase))
            {
                var texture=MapTexturePack.Load(bytes,path).Entries.Single();int width=Math.Min(128,(int)texture.Width),height=Math.Min(128,(int)texture.Height);byte[] rgba=new byte[width*height*4];
                for(int y=0;y<height;y++)for(int x=0;x<width;x++){int i=y*width+x;var color=new ColorRgba(texture.Palette[texture.Pixels[y*texture.Height/height*texture.Width+x*texture.Width/width]]);rgba[i*4]=color.Red;rgba[i*4+1]=color.Green;rgba[i*4+2]=color.Blue;rgba[i*4+3]=color.Alpha;}
                return new(width,height,rgba,$"Native texture · {texture.Width} × {texture.Height}");
            }
            var dimensions=ModernTextureAsset.ProbeDimensions(bytes);using var stream=new MemoryStream(bytes,false);
            var image=PreparedTextureCodec.DecodeRgba(stream,path,TextureAssetClass.Ui,TextureAssetChannel.Albedo,128);
            token.ThrowIfCancellationRequested();return new(image.Width,image.Height,image.Pixels,$"HD texture · {dimensions.Width} × {dimensions.Height}");
        },cancellation)));
    }
    private void AddGeometryThumbnail(Panel rows,MapBuildSnapshot snapshot,ISet<Guid>? ids=null)
        =>rows.Children.Add(NewBrowserThumbnail(cancellation=>LoadBrowserThumbnailAsync(token=>GeometryThumbnail(snapshot.CreateDefinition(),ids,token),cancellation)));
    private void AddPrefabThumbnail(Panel rows,string path)
        =>rows.Children.Add(NewBrowserThumbnail(cancellation=>LoadBrowserThumbnailAsync(token=>GeometryThumbnail(MapDefinition.Load(path),null,token),cancellation)));
    private static MapBrowserThumbnailPixels GeometryThumbnail(MapDefinition definition,ISet<Guid>? ids,CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();var scene=MapViewportScene.Create(definition,ids);
        var faces=scene.Faces.Where(face=>!face.CollisionOnly).ToArray();
        var points=faces.SelectMany(face=>face.Points).Select(point=>(X:(double)(point.X-point.Z*.6f),Y:(double)(point.Y+point.Z*.35f))).ToArray();
        if(points.Length==0)throw new IOException("No visible authored geometry.");
        const int width=128,height=80;byte[] pixels=ThumbnailBackground(width,height);
        double minX=points.Min(point=>point.X),maxX=points.Max(point=>point.X),minY=points.Min(point=>point.Y),maxY=points.Max(point=>point.Y);
        double scale=Math.Min((width-12)/Math.Max(.01,maxX-minX),(height-12)/Math.Max(.01,maxY-minY));
        (int X,int Y) Project(System.Numerics.Vector3 point)=>((int)Math.Round(width/2d+(point.X-point.Z*.6f-(minX+maxX)/2)*scale),(int)Math.Round(height/2d-(point.Y+point.Z*.35f-(minY+maxY)/2)*scale));
        int stride=Math.Max(1,faces.Length/2000);
        for(int f=0;f<faces.Length;f+=stride)
        {
            cancellation.ThrowIfCancellationRequested();var face=faces[f];
            for(int i=0;i<face.Points.Length;i++){var a=Project(face.Points[i]);var b=Project(face.Points[(i+1)%face.Points.Length]);ThumbnailLine(pixels,width,height,a.X,a.Y,b.X,b.Y,105,192,222);}
        }
        return new(width,height,pixels,$"Authored geometry · {faces.Length} faces");
    }
    private void AddAudioThumbnail(Panel rows,MapAsset asset)
    {
#if !ANDROID && !MPHREAD_SERVER
        if(!asset.Path.EndsWith(".wav",StringComparison.OrdinalIgnoreCase))return;
        var definition=new MapDefinition {BaseDirectory=_document!.Project.Definition.BaseDirectory??_services.MapLibraryDirectory,BundlePath=_document.Project.Definition.BundlePath};
        string path=asset.Path;
        rows.Children.Add(NewBrowserThumbnail(cancellation=>LoadBrowserThumbnailAsync(token=>
        {
            token.ThrowIfCancellationRequested();using var stream=new MemoryStream(MapAssets.Read(definition,path),false);var audio=StudioPcmAudio.ReadWave(stream);
            const int width=128,height=64;byte[] pixels=ThumbnailBackground(width,height);
            for(int x=0;x<width;x++)
            {
                token.ThrowIfCancellationRequested();int start=(int)((long)x*audio.Samples.Length/width),end=(int)((long)(x+1)*audio.Samples.Length/width);float peak=0;
                for(int i=start;i<end;i++)peak=Math.Max(peak,Math.Abs(audio.Samples[i]));int radius=(int)Math.Round(Math.Min(1,peak)*(height/2-4));
                ThumbnailLine(pixels,width,height,x,height/2-radius,x,height/2+radius,114,210,158);
            }
            return new(width,height,pixels,$"WAV · {audio.SampleRate} Hz · {audio.Channels} channels · {audio.Frames/(double)audio.SampleRate:0.00}s");
        },cancellation)));
#endif
    }
    private static byte[] ThumbnailBackground(int width,int height)
    {var pixels=new byte[width*height*4];for(int i=0;i<pixels.Length;i+=4){pixels[i]=16;pixels[i+1]=24;pixels[i+2]=39;pixels[i+3]=255;}return pixels;}
    private static void ThumbnailLine(byte[] pixels,int width,int height,int x0,int y0,int x1,int y1,byte r,byte g,byte b)
    {
        int dx=Math.Abs(x1-x0),sx=x0<x1?1:-1,dy=-Math.Abs(y1-y0),sy=y0<y1?1:-1,error=dx+dy;
        for(int step=0;step<512;step++)
        {
            if(x0>=0&&y0>=0&&x0<width&&y0<height){int i=(y0*width+x0)*4;pixels[i]=r;pixels[i+1]=g;pixels[i+2]=b;}
            if(x0==x1&&y0==y1)break;int twice=2*error;if(twice>=dy){error+=dy;x0+=sx;}if(twice<=dx){error+=dx;y0+=sy;}
        }
    }
}
