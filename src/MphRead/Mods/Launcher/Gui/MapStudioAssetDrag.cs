using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using MphRead.Mods.MapEditor;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.Launcher.Gui;

internal sealed partial class MapStudioScreen
{
    private readonly Guid _assetDragOwnerId=Guid.NewGuid();
    internal static readonly DataFormat<MapAssetDragData> AssetDragFormat=DataFormat.CreateInProcessFormat<MapAssetDragData>("ProjectPrime.Map.Asset");
    internal MapAssetDragData CreateAssetDragData(MapAssetDragKind kind,string key)
    {
        if(_document is not {} document || _detached)throw new InvalidOperationException("Open an editable map first.");
        var definition=document.Project.Definition;string? revision=null;string[] files=[];
        switch(kind)
        {
            case MapAssetDragKind.Material:
                if(!Guid.TryParse(key,out var id)||!definition.Materials.Any(material=>material.Id==id))throw new IOException("The material is no longer in this map.");
                break;
            case MapAssetDragKind.Texture:
            case MapAssetDragKind.Audio:
                if(!definition.Assets.Any(asset=>asset.Path==key && (kind==MapAssetDragKind.Audio?asset.Kind=="audio":asset.Kind is "texture" or "preview")))
                    throw new IOException("The asset is no longer declared in this map.");
                break;
            case MapAssetDragKind.Model:
                key=Path.GetFullPath(key);
                var source=definition.ModelSources.Find(source=>Path.GetFullPath(source.Source)==key)??throw new IOException("The model source is no longer in this map.");
                revision=source.SourceHash;files=source.Dependencies.Select(dependency=>dependency.Path).Append(key).Distinct(StringComparer.Ordinal).ToArray();
                if(files.Length>512)throw new IOException("The model source has too many dependencies to drag.");
                break;
            case MapAssetDragKind.Prefab: key=Path.GetFullPath(key);files=[key];break;
            default:throw new ArgumentOutOfRangeException(nameof(kind));
        }
        var stamps=files.Select(path=>
        {
            var file=new FileInfo(Path.GetFullPath(path));if(!file.Exists)throw new FileNotFoundException("Locate this browser source before dragging it.",file.FullName);
            return new MapAssetSourceStamp(file.FullName,file.Length,file.LastWriteTimeUtc.Ticks);
        }).ToArray();
        return new(definition.MapId,kind,key) {Context=new(_assetDragOwnerId,document.CurrentStateId,document.FilePath,
            definition.BaseDirectory,definition.SourcePath,definition.BundlePath,revision,stamps)};
    }
    private void ValidateAssetDragData(MapAssetDragData data)
    {
        if(data.Context is not {} previous || previous.OwnerId!=_assetDragOwnerId)
            throw new InvalidOperationException("This browser drag belongs to another editor. Drag the current map's entry again.");
        var current=CreateAssetDragData(data.Kind,data.Key).Context!;
        if(current.State!=previous.State || current.FilePath!=previous.FilePath || current.BaseDirectory!=previous.BaseDirectory
            ||current.SourcePath!=previous.SourcePath || current.BundlePath!=previous.BundlePath ||current.SourceRevision!=previous.SourceRevision
            || !current.SourceFiles.SequenceEqual(previous.SourceFiles))
            throw new InvalidOperationException("The map or browser source changed during this drag. Drag its current entry again.");
    }
    private void AddAssetDrag(Panel rows,string label,MapAssetDragData value)
    {
        if(!_services.IsStandalone)return;
        var handle=new Border
        {
            Padding=new Thickness(8,5),BorderThickness=new Thickness(1),BorderBrush=PrimeTheme.BorderBrush,
            Child=new TextBlock {Text="↗ Drag "+label+" to viewport",FontSize=11,Foreground=PrimeTheme.HighlightBrush},
            Cursor=new Cursor(StandardCursorType.Hand)
        };
        handle.PointerPressed+=async(_,args)=>
        {
            if(!args.GetCurrentPoint(handle).Properties.IsLeftButtonPressed)return;
            try
            {
                args.Handled=true;using var data=new DataTransfer();data.Add(DataTransferItem.Create(AssetDragFormat,CreateAssetDragData(value.Kind,value.Key)));
                await DragDrop.DoDragDropAsync(args,data,DragDropEffects.Copy);
            }
            catch(Exception error){Failure(error);}
        };
        rows.Children.Add(handle);
    }
    private void ConfigureAssetDropTarget()
    {
        DragDrop.SetAllowDrop(_viewportHost,true);
        _viewportHost.AddHandler(DragDrop.DragOverEvent,(_,args)=>
        {
            if(!args.DataTransfer.Contains(AssetDragFormat))return;
            args.DragEffects=_document is not null && _work is null?DragDropEffects.Copy:DragDropEffects.None;args.Handled=true;
        },RoutingStrategies.Tunnel);
        _viewportHost.AddHandler(DragDrop.DropEvent,async(_,args)=>
        {
            if(args.DataTransfer.TryGetValue(AssetDragFormat) is not {} value)return;
            args.Handled=true;
            try{await ApplyAssetDropAsync(value,args.GetPosition(_viewportHost));}
            catch(OperationCanceledException){}
            catch(Exception error){Failure(error);}
        },RoutingStrategies.Tunnel);
    }
    internal async Task ApplyAssetDropAsync(MapAssetDragData data,Point position)
    {
        if(_document is not {} document || _viewport is null || _work is not null)throw new InvalidOperationException("Wait for the current map operation or open an editable map.");
        if(data.MapId!=document.Project.Definition.MapId)throw new InvalidOperationException("This browser entry belongs to another map. Import it into the current map first.");
        ValidateAssetDragData(data);
        var view=_views.FirstOrDefault(view=>_viewportHost.TranslatePoint(position,view) is {} candidate && new Rect(view.Bounds.Size).Contains(candidate))??_viewport;
        Point local=_viewportHost.TranslatePoint(position,view) ?? position;
        var hit=view.PickSurface(local.X,local.Y);var point=hit?.Point??view.GetPlacementPoint();
        if(data.Kind==MapAssetDragKind.Prefab)
        {await InsertPrefabAsync(Path.GetFullPath(data.Key),new(){Position=[point.X,point.Y,point.Z]});return;}
        if(data.Kind==MapAssetDragKind.Model)
        {await ImportModelAtAsync(Path.GetFullPath(data.Key),point);return;}
        if(data.Kind==MapAssetDragKind.Audio)
        {
            if(!document.Project.Definition.Assets.Any(asset=>asset.Path==data.Key&&asset.Kind=="audio"))throw new IOException("The audio asset is no longer declared.");
            document.Edit("Set map music",definition=>{definition.Audio??=new();definition.Audio.Music=data.Key;definition.Audio.GameMusic=null;},MapChangeDomain.Metadata);
            _status.Text="Map music assigned · one Undo step";return;
        }
        int material=data.Kind==MapAssetDragKind.Material && Guid.TryParse(data.Key,out var id)
            ?document.Project.Definition.Materials.FindIndex(material=>material.Id==id)
            :document.Project.Definition.Materials.FindIndex(material=>material.Texture==data.Key||material.Albedo==data.Key);
        if(material<0)throw new IOException("Choose a material that uses this texture before dropping it on a surface.");
        if(hit is not {} surface || surface.ObjectId==Guid.Empty)throw new IOException("Drop the material onto an authored surface.");
        var target=MapObjects.Find(document.Project.Definition,surface.ObjectId)?.Value;
        if(target is MapMesh)document.PaintFaces(surface.ObjectId,[surface.FaceIndex],material);
        else if(target is MapGeometry)document.EditObjects("Assign surface material",[surface.ObjectId],definition=>definition.Geometry.Single(geometry=>geometry.Id==surface.ObjectId).Material=material);
        else throw new IOException("Convert this surface to authored geometry before assigning a material.");
        _status.Text="Surface material assigned · one Undo step";
    }
}
