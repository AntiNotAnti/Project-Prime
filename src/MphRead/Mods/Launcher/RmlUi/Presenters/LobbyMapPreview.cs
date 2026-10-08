#if MPHREAD_RMLUI_POC
using System;
using System.IO;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Launcher.RmlUi.Pages.Theatre;
namespace MphRead.Mods.Launcher.RmlUi.Presenters;

internal sealed class LobbyMapPreview
{
    private readonly TheatreImageCache _images;
    private string _source="", _image="";
    private DateTime _stamp, _nextRead;
    private Task? _render;
    private string _renderRoom="";
    private readonly HashSet<string> _requested = new(StringComparer.Ordinal);
    internal LobbyMapPreview(TheatreImageCache? images=null) => _images=images??new();
    internal void Present(string room,Action<string,string> text,Action<string,bool> flag,CancellationToken lifetime=default)
    {
        string path=ThumbnailGenerator.PathFor(room);
        if(path!=_source||DateTime.UtcNow>=_nextRead)
        {
            _nextRead=DateTime.UtcNow.AddSeconds(2);
            var file=new FileInfo(path);
            if(path!=_source||(file.Exists?file.LastWriteTimeUtc:default)!=_stamp)
            {
                _source=path;_stamp=file.Exists?file.LastWriteTimeUtc:default;_image="";
                if(file.Exists)try{_image=_images.Load(path,default);}catch(Exception ex){DebugLog.Line("lobby-preview",ex.Message);}
            }
        }
        if (_render?.IsCanceled == true) { _requested.Remove(_renderRoom); _render=null; }
        if (_image.Length == 0 && !lifetime.IsCancellationRequested && _render?.IsCompleted != false && GameFiles.Ready
            && ThumbnailHost.CanRender && !LauncherUiPerformance.Enabled && _requested.Add(room))
        { _renderRoom=room; _render = Render(room,lifetime); }
        text("lobby_map_image",_image);flag("lobby_map_image_ready",_image.Length>0);
        text("lobby_map_preview_status",_image.Length>0?room:_render?.IsCompleted == false ? "Preparing map preview..." : "Preview unavailable. Regenerate in Settings / Maintenance.");
    }
    private static async Task Render(string room,CancellationToken lifetime)
    {
        try
        {
            lifetime.ThrowIfCancellationRequested();
            void Report(string message) => DebugLog.Line("lobby-preview",message);
            if (ThumbnailHost.Current is {} host) await host.RenderAsync(new[]{room},Report);
            else await Task.Run(()=>ThumbnailBatch.Run(new[]{room},1,ThumbnailGenerator.ThumbnailWidth,
                ThumbnailGenerator.ThumbnailHeight,Report,cancel:lifetime),lifetime);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception error) { DebugLog.Line("lobby-preview",error.Message); }
    }
}
#endif
