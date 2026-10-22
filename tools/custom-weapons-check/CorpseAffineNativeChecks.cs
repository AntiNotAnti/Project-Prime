using System.Text.Json;
using MphRead;
using MphRead.CorpsePhysics;
using MphRead.Formats;
using MphRead.Mods;
using MphRead.Mods.Render;
using OpenTK.Mathematics;
using NM=System.Numerics.Matrix4x4;
using NV=System.Numerics.Vector3;
using NQ=System.Numerics.Quaternion;

// Actual native CPU pose data, copied into independent affine bindings. The
// fifteen rigid frame carriers are not collider fits, a game draw, or gameplay.
internal static class CorpseAffineNativeChecks
{
    private static NM N(Matrix4 m)=>new(m.M11,m.M12,m.M13,m.M14,m.M21,m.M22,m.M23,m.M24,
        m.M31,m.M32,m.M33,m.M34,m.M41,m.M42,m.M43,m.M44);
    private static float[] Values(NM m)=>[m.M11,m.M12,m.M13,m.M14,m.M21,m.M22,m.M23,m.M24,
        m.M31,m.M32,m.M33,m.M34,m.M41,m.M42,m.M43,m.M44];
    private static float Error(NM a,NM b)=>Values(a).Zip(Values(b),(x,y)=>MathF.Abs(x-y)).Max();
    private static CorpsePose[] Carriers(Model m,NM[] nodes,NQ world)
    {
        NV At(string name)=>nodes[m.GetNodeIndexByName(name)].Translation;
        NV Virtual(string elbow,string shoulder)
        {var d=At(elbow)-At(shoulder);return At(elbow)+(d.LengthSquared()>1e-8f?NV.Normalize(d):NV.UnitX)*.25f;}
        NV lh=At("L_hip"),lk=At("L_knee"),la=At("L_ankle"),rh=At("R_hip"),rk=At("R_knee"),ra=At("R_ankle");
        NV ls=At("L_shoulder"),le=At("L_elbow"),rs=At("R_shoulder"),re=At("R_elbow");
        NV lw=m.GetNodeIndexByName("L_wrist")>=0?At("L_wrist"):Virtual("L_elbow","L_shoulder");
        NV rw=m.GetNodeIndexByName("R_wrist")>=0?At("R_wrist"):Virtual("R_elbow","R_shoulder");
        NV pelvis=(lh+rh)*.5f;
        NV[] positions=[pelvis,(pelvis+At("Spine_2"))*.5f,At("Head_1"),(ls+le)*.5f,(le+lw)*.5f,lw,
            (rs+re)*.5f,(re+rw)*.5f,rw,(lh+lk)*.5f,(lk+la)*.5f,la,(rh+rk)*.5f,(rk+ra)*.5f,ra];
        return positions.Select((p,i)=>new CorpsePose(p,NQ.Normalize(world*NQ.CreateFromAxisAngle(NV.UnitX,i*.013f)))).ToArray();
    }
    internal static int Run(string data,string output)
    {
        output=Path.GetFullPath(output);
        string cwd=Directory.GetCurrentDirectory();int checks=0,cases=0,scaled=0;float maxRest=0,maxMoved=0;
        var results=new List<object>();
        void Check(bool pass,string message){checks++;if(!pass)throw new InvalidOperationException(message);}
        try
        {
            Directory.CreateDirectory(output);Directory.SetCurrentDirectory(Path.GetFullPath(data));
            Paths.UpdatePaths();Paths.ChooseMphPath();Headless.Enter();
            var rng=Rng.Current;var rngBefore=(rng.Rng1,rng.Rng2);
            foreach(Hunter hunter in Enum.GetValues<Hunter>().Where(h=>(int)h>=0&&(int)h<7))for(int lod=0;lod<2;lod++)
            {
                var instance=Read.GetModelInstance(Metadata.HunterModels[hunter][lod],noCache:true);var model=instance.Model;
                var profile=CorpseRigProfile.For(model);Check(profile.ProceduralSupported&&!profile.PhysicalQualified,"native layout remains unqualified for physics");
                var names=model.Nodes.Select(n=>n.Name).ToArray();var parents=model.Nodes.Select(n=>n.ParentIndex).ToArray();
                var map=new byte[model.Nodes.Count];Check(CorpseHumanoidMap.TryMap(names,parents,map),$"actual {hunter}/LOD{lod} humanoid mapping");
                var palette=model.NodeMatrixIds.ToArray();int rigCases=0;
                CorpseAffineBinding? retained=null;CorpsePose[]? retainedBodies=null;NM[]? retainedExpected=null;
                for(int bank=0;bank<model.AnimationGroups.Node.Count;bank++)
                {
                    var group=model.AnimationGroups.Node[bank];if(group.FrameCount<=0)continue;instance.SetAnimation(bank);
                    foreach(int frame in new[]{0,group.FrameCount/2,group.FrameCount-1}.Distinct())
                    {
                        instance.AnimInfo.Frame[instance.AnimInfo.Node.Slot]=frame;
                        var rotation=NQ.Normalize(NQ.CreateFromYawPitchRoll(.31f,.13f,-.07f));
                        var world=Matrix4.CreateFromQuaternion(new Quaternion(rotation.X,rotation.Y,rotation.Z,rotation.W))*Matrix4.CreateTranslation(7.25f,-1.5f,12.5f);
                        model.AnimateNodes(0,false,world,Vector3.One,instance.AnimInfo);model.UpdateMatrixStack();
                        var native=model.Nodes.Select(n=>N(n.Animation)).ToArray();var nativePalette=model.MatrixStackValues.ToArray();
                        var carriers=Carriers(model,native,rotation);var immutableInput=(NM[])native.Clone();
                        Check(CorpseAffineBinding.TryCapture(native,map,carriers,out var binding),$"actual affine capture {hunter}/LOD{lod}/{bank}/{frame}");
                        var restored=new NM[native.Length];Check(binding!.TrySample(carriers,restored),"captured pose samples without stepping");
                        float rest=native.Zip(restored,Error).Max();maxRest=Math.Max(maxRest,rest);Check(rest<.00002f,"capture has no visible pose snap within finite float tolerance");
                        if(native.Any(m=>MathF.Abs(new NV(m.M11,m.M12,m.M13).Length()-1)>.001f))scaled++;
                        var deltaQ=NQ.Normalize(NQ.CreateFromYawPitchRoll(-.27f,.19f,.04f));var delta=NM.CreateFromQuaternion(deltaQ)*NM.CreateTranslation(-3,2,1);
                        var moved=carriers.Select(p=>new CorpsePose(NV.Transform(p.Position,delta),NQ.Normalize(deltaQ*p.Orientation))).ToArray();
                        var after=new NM[native.Length];Check(binding.TrySample(moved,after),"independent bodies accept common rigid motion");
                        float error=native.Select(m=>m*delta).Zip(after,Error).Max();maxMoved=Math.Max(maxMoved,error);Check(error<.00003f,"full native affine residual follows rigid carrier motion");
                        var onlyHead=(CorpsePose[])carriers.Clone();onlyHead[2]=onlyHead[2] with{Position=onlyHead[2].Position+new NV(.1f,.05f,0)};
                        Check(binding.TrySample(onlyHead,after),"independent head-carrier sample");
                        Check(map.Select((body,i)=>body==2?Error(after[i],restored[i])>.01f:after[i]==restored[i]).All(p=>p),"head/helpers move only in their mapped group");
                        Check(model.Nodes.Select(n=>N(n.Animation)).SequenceEqual(immutableInput)&&model.MatrixStackValues.SequenceEqual(nativePalette)
                            &&model.NodeMatrixIds.SequenceEqual(palette),"sampling leaves original native composed nodes/palette/skin bindings exact");
                        if(retained==null){retained=binding;retainedBodies=(CorpsePose[])carriers.Clone();retainedExpected=(NM[])restored.Clone();}
                        Array.Fill(native,NM.Identity);Array.Fill(carriers,default);Array.Fill(map,(byte)14);
                        Check(binding.TrySample(moved,after)&&immutableInput.Select(m=>m*delta).Zip(after,Error).Max()<.00003f,"binding owns input matrix/map copies after caller mutation");
                        Check(CorpseHumanoidMap.TryMap(names,parents,map),"next capture restores independently validated node mapping");
                        cases++;rigCases++;
                    }
                }
                var retainedResult=new NM[model.Nodes.Count];Check(retained!=null&&retained.TrySample(retainedBodies!,retainedResult)
                    &&retainedResult.SequenceEqual(retainedExpected!),"later native animations cannot overwrite an earlier corpse-owned pose binding");
                results.Add(new{hunter=hunter.ToString(),lod,rigCases,profile.WeightedHead,profile.PaletteCount});
            }
            Check(results.Count==14&&cases>500&&scaled>0,"all fourteen native rigs and scaled animation cases actually execute");
            Check(ReferenceEquals(rng,Rng.Current)&&(rng.Rng1,rng.Rng2)==rngBefore,"native CPU pose bridge leaves gameplay RNG unchanged");
            File.WriteAllText(Path.Combine(output,"native-affine-results.json"),JsonSerializer.Serialize(new{checks,cases,scaledCases=scaled,maxRest,maxMoved,profiles=results,
                scope="Actual native CPU animation matrices only. Fifteen frame carriers are not collider fits, gameplay death authority, rendered/HD palettes or physical-retarget qualification."},new JsonSerializerOptions{WriteIndented=true}));
            Console.WriteLine($"PASS {checks} native affine checks/{cases} pose cases/{scaled} scaled cases; rest={maxRest}, moved={maxMoved}");return 0;
        }
        finally{Directory.SetCurrentDirectory(cwd);}
    }
}
