#if !MPHREAD_SERVER
using System;
using System.Runtime.CompilerServices;
using MphRead.CorpsePhysics;
using MphRead.Entities;
using MphRead.Formats;
using OpenTK.Mathematics;
using NM=System.Numerics.Matrix4x4;
using NV=System.Numerics.Vector3;

namespace MphRead.Mods.Render;

internal static class CorpseMatrixConversion
{
    internal static NM Copy(Matrix4 m)=>new(m.M11,m.M12,m.M13,m.M14,m.M21,m.M22,m.M23,m.M24,
        m.M31,m.M32,m.M33,m.M34,m.M41,m.M42,m.M43,m.M44);
    internal static Matrix4 Copy(NM m)=>new(m.M11,m.M12,m.M13,m.M14,m.M21,m.M22,m.M23,m.M24,
        m.M31,m.M32,m.M33,m.M34,m.M41,m.M42,m.M43,m.M44);
    internal static NV Copy(Vector3 v)=>new(v.X,v.Y,v.Z);
}

// One reusable, owner-local submission lease. Renderer.AddRenderItem copies
// the palette before this lease restores the shared native model exactly.
internal sealed class NativeCorpsePalette : IDisposable
{
    internal readonly Model Model;
    private readonly CorpseAffineBinding _binding;
    private readonly NM[] _output;
    private readonly Matrix4[] _saved;
    private readonly float[] _palette;
    private bool _inUse;
    internal NativeCorpsePalette(Model model,CorpseAffineBinding binding)
    {
        Model=model;_binding=binding;_output=new NM[model.Nodes.Count];_saved=new Matrix4[model.Nodes.Count];
        _palette=new float[model.MatrixStackValues.Count];
    }
    internal NativeCorpsePalette? Begin(ReadOnlySpan<CorpsePose> bodies)
    {
        if(_inUse||Model.Nodes.Count!=_saved.Length||Model.MatrixStackValues.Count!=_palette.Length
            ||!_binding.TrySample(bodies,_output))return null;
        for(int i=0;i<_saved.Length;i++)_saved[i]=Model.Nodes[i].Animation;
        for(int i=0;i<_palette.Length;i++)_palette[i]=Model.MatrixStackValues[i];
        _inUse=true;
        try
        {
            for(int i=0;i<_saved.Length;i++)Model.Nodes[i].Animation=CorpseMatrixConversion.Copy(_output[i]);
            Model.UpdateMatrixStack();return this;
        }
        catch{Dispose();throw;}
    }
    public void Dispose()
    {
        if(!_inUse)return;
        try
        {
            for(int i=0;i<_saved.Length;i++)Model.Nodes[i].Animation=_saved[i];
            if(Model.MatrixStackValues is float[] palette&&palette.Length==_palette.Length)
            {Array.Copy(_palette,palette,_palette.Length);MatrixPaletteRevision.Changed(palette);}
            else Model.UpdateMatrixStack();
        }
        finally{_inUse=false;}
    }
}

internal sealed class NativeCorpseCapture : IDisposable
{
    internal readonly CapturedCorpseRig Primary;
    internal readonly NativeCorpsePalette[] Palettes;
    internal NativeCorpseCapture(CapturedCorpseRig primary,NativeCorpsePalette[] palettes)
    {Primary=primary;Palettes=palettes;}
    public void Dispose(){foreach(var p in Palettes)p.Dispose();}
}

internal static class NativeCorpseCalibration
{
    private sealed class Calibration {internal CorpseHumanoidProfile? Profile;}
    private static readonly ConditionalWeakTable<Model,Calibration> Profiles=new();
    internal static CorpseHumanoidProfile? For(Model source,Hunter hunter)
        =>Profiles.GetValue(source,_=>Build(source,hunter)).Profile;
    private static Calibration Build(Model source,Hunter hunter)
    {
        var result=new Calibration();var known=CorpseRigProfile.For(source);
        if(!known.ProceduralSupported||!known.WeightedHead||known.Hunter!=hunter)return result;
        // An independent CPU-only instance; never borrow current actor controls
        // to manufacture calibration. No GL upload, muzzle or gameplay query.
        var instance=Read.GetModelInstance(source.Name,noCache:true);instance.SetAnimation((int)PlayerAnimation.Idle);
        var model=instance.Model;
        model.AnimateNodes(0,false,Matrix4.CreateScale(Metadata.HunterScales[hunter]),Vector3.One,instance.AnimInfo);
        var names=new string[model.Nodes.Count];var parents=new int[names.Length];var neutral=new NM[names.Length];
        for(int i=0;i<names.Length;i++)
        {names[i]=model.Nodes[i].Name;parents[i]=model.Nodes[i].ParentIndex;neutral[i]=CorpseMatrixConversion.Copy(model.Nodes[i].Animation);}
        CorpseHumanoidProfile.TryCreate(source.Name+" experimental primitives",names,parents,neutral,NV.UnitY,-NV.UnitZ,
            hunter is Hunter.Trace or Hunter.Noxus?.40f:.28125f,out result.Profile);
        return result;
    }
}

// Used only while composing the other LOD once at capture. Split animation
// temporarily changes spine controls; every original value must be restored.
internal sealed class NativeCorpseModelState
{
    private readonly Model _model;
    private readonly Matrix4[] _animation,_transform;
    private readonly (Matrix4? Before,Matrix4? After,bool Parent,bool Child)[] _control;
    private readonly float[] _palette;
    internal NativeCorpseModelState(Model model)
    {
        _model=model;_animation=new Matrix4[model.Nodes.Count];_transform=new Matrix4[_animation.Length];
        _control=new (Matrix4?,Matrix4?,bool,bool)[_animation.Length];_palette=new float[model.MatrixStackValues.Count];
        for(int i=0;i<_animation.Length;i++)
        {var n=model.Nodes[i];_animation[i]=n.Animation;_transform[i]=n.Transform;_control[i]=(n.BeforeTransform,n.AfterTransform,n.AnimIgnoreParent,n.AnimIgnoreChild);}
        for(int i=0;i<_palette.Length;i++)_palette[i]=model.MatrixStackValues[i];
    }
    internal void Restore()
    {
        for(int i=0;i<_animation.Length;i++)
        {
            var n=_model.Nodes[i];n.Animation=_animation[i];n.Transform=_transform[i];
            (n.BeforeTransform,n.AfterTransform,n.AnimIgnoreParent,n.AnimIgnoreChild)=_control[i];
        }
        if(_model.MatrixStackValues is float[] palette&&palette.Length==_palette.Length)
        {Array.Copy(_palette,palette,_palette.Length);MatrixPaletteRevision.Changed(palette);}
        else _model.UpdateMatrixStack();
    }
}
#endif
