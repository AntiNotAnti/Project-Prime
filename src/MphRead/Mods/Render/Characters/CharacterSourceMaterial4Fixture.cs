#if !MPHREAD_SERVER && !ANDROID
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using MphRead.Mods.Render;
namespace MphRead.Mods.Render.Characters;

internal static partial class CharacterModelRuntime
{
    // Ephemeral acceptance ticket. GPU objects are owned only by the enclosing
    // Scene draw transaction, so no second resource cache/lifetime is invented.
    internal sealed class Material4FixtureTicket
    {
        internal readonly CharacterSourceMaterial4Recipe Recipe;
        internal readonly System.Collections.ObjectModel.ReadOnlyCollection<CharacterMaterial4PaletteSnapshot> Segments;
        private readonly Action<Scene> _require;
        internal Material4FixtureTicket(CharacterSourceMaterial4Recipe recipe,CharacterWeightedRenderSegment[] segments,Action<Scene> require)
        {Recipe=recipe;Segments=Array.AsReadOnly(segments.Select(s=>new CharacterMaterial4PaletteSnapshot(s.ListId,s.SourceJointPalette!.Count,s.SourcePresentationPalette!)).ToArray());_require=require;}
        internal void RequireCurrent(Scene scene) => _require(scene);
    }
    internal static Material4FixtureTicket CaptureMaterial4Fixture(Scene scene,CharacterSourcePreviewInstance instance,CharacterWeightedRenderModel model)
    {
        if(!OperatingSystem.IsMacOS()||!_scenes.TryGetValue(scene,out var resources)
            ||!ReferenceEquals(instance.PublishedContext,resources)||scene.Services.IsReplica
            ||Network.NetSession.Active||Network.DemoPlayback.IsActive)
            throw new InvalidDataException("Explicit current offline source context required.");
        var entries=resources.SourceModels.Where(x=>ReferenceEquals(x.Key.Instance,instance)&&ReferenceEquals(x.Value.Model,model)).ToArray();
        if(entries.Length!=1||entries[0].Value.Attachment is not {} attachment||model.SourceRig==null)
            throw new InvalidDataException("Existing attached source model required.");
        var pair=entries[0];var entry=pair.Value;var asset=model.Asset;
        void Require(Scene current)
        {
            if(!ReferenceEquals(current,scene)||!_scenes.TryGetValue(scene,out var live)||!ReferenceEquals(live,resources)
                ||!ReferenceEquals(instance.PublishedContext,resources)||!resources.SourceModels.TryGetValue(pair.Key,out var now)
                ||!ReferenceEquals(now,entry)||scene.Services.IsReplica||Network.NetSession.Active||Network.DemoPlayback.IsActive)
                throw new InvalidDataException("Material4 source entry/context retired.");
            attachment.RequireCurrent(scene,resources,entry.Lease,asset,entry.NativeModel,model.SourceRig);
        }
        Require(scene);entry.Lease.Require(asset);
        string root=Path.GetDirectoryName(asset.ModelPath)!;
        byte[] Read(string leaf,int maximum)
        {
            if(leaf!=Path.GetFileName(leaf)||leaf.Contains('\\')||leaf.Contains(':'))throw new InvalidDataException("Flat private material witness required.");
            string path=Path.Combine(root,leaf);
            if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Material witness cannot be linked.");
            return CharacterSourceProofIO.ReadFile(path,maximum);
        }
        byte[] provenance=entry.Lease.ReadProvenanceSnapshot();
        using var receipt=JsonDocument.Parse(provenance);
        var proof=receipt.RootElement.GetProperty("sourceMaterial4Fixture");
        if(proof.GetProperty("scope").GetString()!="diagnostic-nearest-clamp-material4-v1"
            ||proof.GetProperty("materialsSha256").GetString()!=CharacterSourceMaterial4Recipe.MaterialsSha
            ||proof.GetProperty("imageSha256").GetString()!=CharacterSourceMaterial4Recipe.ImageSha)
            throw new InvalidDataException("Explicit source-bound material4 diagnostic proof required.");
        var recipe=CharacterSourceMaterial4Recipe.Read(Read("source-materials.json",1024*1024),Read("source-material4.png",1024*1024));
        if(model.Segments.Count!=attachment.Geometry.Primitives.Count)throw new InvalidDataException("Source segment inventory changed.");
        var indices=Enumerable.Range(0,model.Segments.Count).Where(i=>model.Segments[i].SourceMaterialName==CharacterSourceMaterial4Recipe.MaterialName).ToArray();
        if(indices.Length==0||indices.Sum(i=>attachment.Geometry.Primitives[i].Indices.Length)!=438*3)
            throw new InvalidDataException("Complete captured source material4 geometry required.");
        foreach(int i in indices)
        {
            var p=attachment.Geometry.Primitives[i];var s=model.Segments[i];
            if(p.MaterialName!=CharacterSourceMaterial4Recipe.MaterialName||p.Albedo==null
                ||CharacterSourceMaterial4Recipe.Sha(p.Albedo.Image)!=CharacterSourceMaterial4Recipe.ImageSha
                ||s.ListId==0||s.SourceJointPalette?.Count is not (>0 and <=32)
                ||s.SourcePresentationPalette==null||s.SourcePresentationPalette.Length!=16*s.SourceJointPalette.Count)
                throw new InvalidDataException("Source material4 image/list/palette identity mismatch.");
        }
        entry.Lease.Require(asset);Require(scene);
        var guard=new CharacterMaterial4Guard(scene,resources,model,entry.Lease,OpenGlContextPolicy.Generation,Environment.CurrentManagedThreadId);
        var segments=indices.Select(i=>model.Segments[i]).ToArray();
        return new(recipe,segments,current=>
        {
            guard.Require(current,resources,model,entry.Lease,OpenGlContextPolicy.Generation,Environment.CurrentManagedThreadId);
            Require(current);
            for(int n=0;n<indices.Length;n++)
                if(!ReferenceEquals(model.Segments[indices[n]],segments[n]))throw new InvalidDataException("Source material4 segment retired.");
        });
    }
}
#endif
