using System.IO;
using System.Linq;
using System.Threading;
using MphRead.Mods.Import;

namespace MphRead.Mods.Render.Characters;

internal sealed record CharacterSourceDroneSelection(CharacterModelPack Pack, string Generation,
    CharacterGeneratedDroneProfile Profile)
{
    internal PrivateCharacterAppearance Appearance => new("trooper",Profile.Skin);
    internal static CharacterSourceDroneSelection Load(string id,CancellationToken cancellation,string? root=null)
    {
        var profile=CharacterGeneratedDroneProfile.Find(id) ?? throw new InvalidDataException("Unknown drone appearance.");
        cancellation.ThrowIfCancellationRequested();
        string importRoot=root??RetroCharacterPreviewExporter.DefaultRoot;
        var state=LocalImportBundle.Inspect(importRoot,id,RetroCharacterPreviewExporter.BundleSpec(id,ExternalSourceEdition.PrimeUsaRevision2));
        if(state.State!=ImportBundleState.Ready||state.Receipt==null)
            throw new InvalidDataException("Update the selected drone import: "+state.State+". "+state.Message);
        string folder=Path.GetDirectoryName(state.Files.Single(f=>Path.GetFileName(f)=="characters.json"))!;
        if(Path.GetFileName(folder)!=state.Receipt.Generation||state.Files.Any(f=>Path.GetDirectoryName(f)!=folder))
            throw new InvalidDataException("All drone components must share one committed generation.");
        foreach(string file in state.Files)LocalImportBundle.CheckPath(file,importRoot);
        cancellation.ThrowIfCancellationRequested();
        var pack=CharacterModelPack.Load(folder);
        var selection=new CharacterSourceDroneSelection(pack,state.Receipt.Generation,profile);
        if(!pack.TryResolvePrivate(Hunter.Weavel,selection.Appearance,CharacterModelPart.AlternateForm,0,out var asset)
            ||!pack.HasSourceDrone(asset)||!pack.TryCreateSourcePreviewLease(asset,out var lease))
            throw new InvalidDataException("Complete own drone source owner required.");
        var geometry=CharacterWeightedModelLoader.LoadSourcePreview(asset,lease);
        _=CharacterSourceClip.Load(lease,asset,geometry.SourceRig!,CharacterGeneratedDroneProfile.ClipOrdinal)
            .WithPinnedDroneRootMotion().Sample(0);
        lease.Require(asset);cancellation.ThrowIfCancellationRequested();return selection;
    }
}
