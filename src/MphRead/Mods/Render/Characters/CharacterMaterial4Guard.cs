using System;
using System.IO;
namespace MphRead.Mods.Render.Characters;

// Pure identity gate. Only CharacterModelRuntime mints actual runtime tickets;
// this value alone is never source provenance or GL context authority.
internal sealed class CharacterMaterial4Guard
{
    private readonly object _scene, _resources, _model, _lease;
    private readonly int _generation, _thread;
    internal CharacterMaterial4Guard(object scene,object resources,object model,object lease,int generation,int thread)
    {
        ArgumentNullException.ThrowIfNull(scene);ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(model);ArgumentNullException.ThrowIfNull(lease);
        (_scene,_resources,_model,_lease,_generation,_thread)=(scene,resources,model,lease,generation,thread);
    }
    internal void Require(object scene,object resources,object model,object lease,int generation,int thread)
    {
        if(!ReferenceEquals(_scene,scene)||!ReferenceEquals(_resources,resources)||!ReferenceEquals(_model,model)
            ||!ReferenceEquals(_lease,lease)||generation!=_generation||thread!=_thread)
            throw new InvalidDataException("Source material4 scene/context/model/lease/thread owner changed.");
    }
}

internal sealed class CharacterMaterial4PaletteSnapshot
{
    internal readonly int ListId;
    private readonly float[] _palette;
    internal int MatrixCount => _palette.Length/16;
    internal CharacterMaterial4PaletteSnapshot(int listId,int matrixCount,float[] preparedPalette)
    {
        if(listId<=0||matrixCount is <1 or >32||preparedPalette.Length!=matrixCount*16)
            throw new InvalidDataException("Existing source list and prepared palette32 required.");
        foreach(float value in preparedPalette)
            if(!float.IsFinite(value)||Math.Abs(value)>100000)throw new InvalidDataException("Invalid source palette value.");
        for(int i=0;i<preparedPalette.Length;i+=16)
            if(preparedPalette[i+3]!=0||preparedPalette[i+7]!=0||preparedPalette[i+11]!=0||preparedPalette[i+15]!=1)
                throw new InvalidDataException("Prepared source affine presentation palette required.");
        ListId=listId;_palette=(float[])preparedPalette.Clone();
    }
    internal float[] CopyPalette() => (float[])_palette.Clone();
}
