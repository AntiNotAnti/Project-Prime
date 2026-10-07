using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.MapEditor;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.Launcher.Gui;

internal sealed partial class MapStudioScreen
{
    private sealed record CapturedModel(ImportedModel Model,string Hash,List<MapSourceDependency> Dependencies);
    private CapturedModel ImportFrozenModel(string path,ModelImportSettings settings,CancellationToken cancellation)
    {
        // Discover references with the canonical importer, then import an immutable copy.
        // The copied bytes also determine source identity; later edits to originals remain detectable.
        var discovery=ModelImportService.Import(path,settings,cancellation);
        string originalRoot=Path.GetDirectoryName(path)!;
        string frozen=Path.Combine(_services.StagingDirectory,"model-source-"+Guid.NewGuid().ToString("N"));
        var captured=new List<MapSourceDependency>();long total=0;
        try
        {
            var dependencies=discovery.Dependencies.Append(path).Select(Path.GetFullPath).Distinct(StringComparer.Ordinal).ToArray();
            if(dependencies.Length>512)throw new IOException("A model import supports up to 512 source dependencies.");
            foreach(string dependency in dependencies)
            {
                cancellation.ThrowIfCancellationRequested();string relative=Path.GetRelativePath(originalRoot,dependency);
                if(Path.IsPathFullyQualified(relative)||relative==".."||relative.StartsWith(".."+Path.DirectorySeparatorChar,StringComparison.Ordinal))
                    throw new InvalidDataException("A model dependency escapes its source folder.");
                byte[] bytes=ReadImportBytes(dependency,MapPackageReader.MaxEntryBytes,cancellation);total+=bytes.LongLength;
                if(total>512L*1024*1024)throw new IOException("Model source dependencies exceed the 512 MiB import limit.");
                captured.Add(new(dependency,Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()));
                AtomicFile.Write(Path.Combine(frozen,relative),bytes);
            }
            cancellation.ThrowIfCancellationRequested();
            var result=ModelImportService.Import(Path.Combine(frozen,Path.GetFileName(path)),settings,cancellation);
            string Original(string dependency)
            {
                string relative=Path.GetRelativePath(frozen,dependency);
                if(Path.IsPathFullyQualified(relative)||relative==".."||relative.StartsWith(".."+Path.DirectorySeparatorChar,StringComparison.Ordinal))
                    throw new InvalidDataException("A frozen model dependency escapes its source snapshot.");
                return Path.GetFullPath(Path.Combine(originalRoot,relative));
            }
            result=result with
            {
                Dependencies=result.Dependencies.Select(Original).Order(StringComparer.Ordinal).ToArray(),
                AssetSources=result.AssetSources.ToDictionary(pair=>pair.Key,pair=>Original(pair.Value),StringComparer.Ordinal)
            };
            var used=result.Dependencies.ToHashSet(StringComparer.Ordinal);
            captured=captured.Where(item=>used.Contains(item.Path)).OrderBy(item=>item.Path,StringComparer.Ordinal).ToList();
            if(used.Any(dependency=>captured.All(item=>item.Path!=dependency)))throw new IOException("Model references changed while preparing the import. Preview again.");
            return new(result,MapSourceFingerprint.Hash(captured),captured);
        }
        finally
        {
            try{if(Directory.Exists(frozen))Directory.Delete(frozen,true);}catch(IOException){}catch(UnauthorizedAccessException){}
        }
    }
    internal Task ImportModelAsync(string path,ModelImportSettings? settings=null,Guid? sourceId=null,CancellationToken cancellation=default)
        =>ImportModelCoreAsync(path,settings,sourceId,cancellation);
    private Task ImportModelAtAsync(string path,System.Numerics.Vector3 point)
        =>ImportModelCoreAsync(path,null,null,default,point);
    private Task ImportModelCoreAsync(string path,ModelImportSettings? settings,Guid? sourceId,CancellationToken cancellation,System.Numerics.Vector3? placement=null)
    {
        path=Path.GetFullPath(path);var context=CaptureAssetContext();
        var snapshot=context.Document.CaptureBuildSnapshot();
        var options=settings ?? (sourceId is {} id?context.Document.Project.Definition.ModelSources.FirstOrDefault(source=>source.Id==id)?.Settings:null) ?? new();
        return Job(sourceId is null?"Importing model":"Reimporting model",async token=>
        {
            var captured=await Task.Run(()=>ImportFrozenModel(path,options,token),token);
            GuardAssetContext(context,token);
            await ApplyCapturedModelAsync(context,snapshot,captured,path,options,sourceId,token,placement);
            _viewport?.FrameAll();ShowInspectorPage("Assets & music");
        },cancellation,propagateErrors:true);
    }
    private async Task ApplyCapturedModelAsync(AssetEditContext context,MapBuildSnapshot snapshot,CapturedModel captured,
        string path,ModelImportSettings settings,Guid? sourceId,CancellationToken cancellation,System.Numerics.Vector3? placement=null)
    {
        var proposal=await Task.Run(()=>
        {
            cancellation.ThrowIfCancellationRequested();var definition=snapshot.CreateDefinition();
            var previous=definition.ModelSources.FirstOrDefault(source=>source.Id==sourceId);
            if(previous is not null && previous.NormalizedHash==ModelReimport.NormalizedHash(captured.Model)
                && previous.Settings==settings && previous.Source==path && previous.SourceHash==captured.Hash)return (Definition:definition,Unchanged:true);
            // Preview viewports may still read the importer output. Resolve the proposal
            // from detached authoring copies instead of mutating those live preview nodes.
            var importedDefinition=new MapDefinition {Geometry=captured.Model.Meshes.Cast<MapGeometry>().ToList(),Materials=captured.Model.Materials.ToList()};
            var importedCopy=MapBuildSnapshot.Capture(new MapProject(importedDefinition)).CreateDefinition();
            var imported=captured.Model with {Meshes=importedCopy.Geometry.OfType<MapMesh>().ToArray(),Materials=importedCopy.Materials};
            var importedSource=ModelReimport.Apply(definition,imported,path,captured.Hash,settings,sourceId,captured.Dependencies);
            if(placement is {} point && sourceId is null)
            {
                var ids=importedSource.Objects.Select(item=>item.Id).ToHashSet();
                foreach(var geometry in definition.Geometry.Where(geometry=>ids.Contains(geometry.Id)))
                {geometry.Transform.Position[0]+=point.X;geometry.Transform.Position[1]+=point.Y;geometry.Transform.Position[2]+=point.Z;}
            }
            definition.BaseDirectory=context.Root;cancellation.ThrowIfCancellationRequested();return(Definition:definition,Unchanged:false);
        },cancellation);
        GuardAssetContext(context,cancellation);
        if(proposal.Unchanged){_status.Text="Model and referenced materials are unchanged.";return;}
        await AdoptAssetFilesAsync(context,captured.Model.Assets,cancellation,()=>context.Document.Edit(sourceId is null?"Import 3D model":"Reimport 3D model",definition=>
        {
            definition.BaseDirectory=context.Root;definition.Geometry=proposal.Definition.Geometry;
            definition.Materials=proposal.Definition.Materials;definition.Assets=proposal.Definition.Assets;definition.ModelSources=proposal.Definition.ModelSources;
        },MapChangeDomain.Geometry|MapChangeDomain.Material|MapChangeDomain.Metadata));
        foreach(string dependency in captured.Model.Dependencies)_changedSources.Remove(dependency);
        _status.Text="Model applied · one Undo step";
    }
}
