using System.Reflection;
using MphRead;
using MphRead.Formats;
using MphRead.Mods;
using OpenTK.Mathematics;
using Source=MphRead.Mods.Campaign.CampaignCreditsModelSource;
using Binding=MphRead.Mods.Campaign.CampaignCreditsAnimationBinding;
using Duration=MphRead.Mods.Campaign.CampaignCreditsAnimationDuration;
using F=CreditsRenderFixture;

internal static class CreditsDecodeFixtures
{
    private static int checks;
    internal static void Run()
    {
        checks=0;bool priorFixes=Read.ApplyFixes,priorHeadless=Headless.Active;string priorKey=Paths.MphKey;
        var paths=(Dictionary<string,string>)typeof(Paths).GetField("_allPaths",BindingFlags.NonPublic|BindingFlags.Static)!.GetValue(null)!;
        var priorPaths=new Dictionary<string,string>(paths,StringComparer.Ordinal);var priorCached=Read.CachedModels.ToArray();
        string temp=Path.Combine(Path.GetTempPath(),"credits-exact-decode-"+Guid.NewGuid().ToString("N"));
        try
        {
            var sourceData=F.Model();var animationData=F.Animation();animationData.Bytes[animationData.At["colors"]]=0;animationData.Bytes[animationData.At["colors"]+2]=31;
            var model=Source.Read(sourceData.Bytes);var binding=Binding.Read(model,Duration.Read(animationData.Bytes),animationData.Bytes);
            Array.Fill(sourceData.Bytes,(byte)0);Array.Fill(animationData.Bytes,(byte)0);
            Paths.MphKey="__credits_decode_fixture__";paths[Paths.MphKey]=Path.Combine(temp,"missing");
            Model? first=null;Model? second=null;
            foreach(bool fixes in new[]{false,true})
            {
                Read.ApplyFixes=fixes;
                foreach(bool headless in new[]{false,true})
                {
                    typeof(Headless).GetProperty(nameof(Headless.Active))!.SetValue(null,headless);
                    var decoded=Read.ReadCreditsModel(model,binding);
                    Check(Read.ApplyFixes==fixes&&Headless.Active==headless,"exact decoder does not publish global fix or Headless policy");
                    Check(decoded.Nodes.Count==1&&decoded.Materials.Count==1&&decoded.RenderInstructionLists.Count==1
                        &&decoded.RenderInstructionLists[0].Count>0&&decoded.GetPixels(0,0,0).Count==64,
                        "exact captured bytes decode instructions and all64texels even in Headless with missing configured path");
                    Check(decoded.GetPixels(0,0,0).All(pixel=>pixel is {Red:8,Green:0,Blue:0,Alpha:255}),
                        "invented short-palette index1 produces independent expected opaque nativeRGB555 red8");
                    Check(decoded.AnimationGroups.Node.Count==1&&decoded.AnimationGroups.Material.Count==1
                        &&decoded.AnimationGroups.Node[0].Count==1&&decoded.AnimationGroups.Material[0].Count==1,
                        "supplied slot1 node/material groups retained rather than dropped by null-path/Headless shortcuts");
                    var instance=new ModelInstance(decoded);instance.SetAnimation(0);
                    for(int frame=0;frame<3;frame++)
                    {
                        instance.AnimInfo.Frame[0]=frame;decoded.AnimateNodes(0,true,Matrix4.Identity,decoded.Scale,instance.AnimInfo);
                        decoded.AnimateMaterials(instance.AnimInfo);decoded.UpdateMatrixStack();
                        Check(decoded.Nodes.All(node=>Finite(node.Animation))&&decoded.MatrixStackValues.All(float.IsFinite),
                            "each admitted synthetic frame evaluates finite node and matrix-stack values");
                        float expected=frame==0?0:frame==1?15/31f:1;
                        Check(MathF.Abs(decoded.Materials[0].CurrentAlpha-expected)<.00001f&&decoded.Materials[0].CurrentDiffuse==new Vector3(expected),
                            "all three CPU material frames sample independent invented0/15/31color and alpha values");
                    }
                    if(first==null)first=decoded;else if(second==null)second=decoded;
                }
            }
            Check(!Directory.Exists(temp),"missing configured source location was never created or used");
            Directory.CreateDirectory(temp);string blocked=Path.Combine(temp,"not-a-directory");File.WriteAllText(blocked,"invented marker");paths[Paths.MphKey]=blocked;
            Check(Read.ReadCreditsModel(model,binding).GetPixels(0,0,0).Count==64,"unusable configured source root file is not consulted by exact decoder");
            Check(first!=null&&second!=null&&!ReferenceEquals(first,second)&&!ReferenceEquals(first!.Nodes[0],second!.Nodes[0])
                &&!ReferenceEquals(first.Materials[0],second.Materials[0])&&!ReferenceEquals(first.MatrixStackValues,second.MatrixStackValues)
                &&!ReferenceEquals(first.AnimationGroups.Node[0],second.AnimationGroups.Node[0])&&!ReferenceEquals(first.AnimationGroups.Material[0],second.AnimationGroups.Material[0]),
                "separate decodes own mutable model/node/material/matrix/group state independently");
            Model left=first??throw new InvalidOperationException("missing first decoded fixture"),right=second??throw new InvalidOperationException("missing second decoded fixture");
            var a=new ModelInstance(left);var b=new ModelInstance(right);a.SetAnimation(0);b.SetAnimation(0);
            var secondMatrix=right.MatrixStackValues.ToArray();float secondAlpha=right.Materials[0].CurrentAlpha;
            left.Nodes[0].Animation=Matrix4.CreateTranslation(7,8,9);left.UpdateMatrixStack();left.Materials[0].CurrentAlpha=.125f;
            left.AnimationGroups.Node[0].CurrentFrame=99;a.AnimInfo.Frame[0]=88;
            Check(right.MatrixStackValues.SequenceEqual(secondMatrix)&&right.Materials[0].CurrentAlpha==secondAlpha
                &&right.AnimationGroups.Node[0].CurrentFrame!=99&&b.AnimInfo.Frame[0]==0&&!ReferenceEquals(a.AnimInfo,b.AnimInfo),
                "mutating first appearance matrix/material/group/AnimInfo leaves second appearance unchanged");
            var still=Read.ReadCreditsModel(model);
            Check(!still.AnimationGroups.Any&&still.AnimationGroups.Node.Count==0&&still.AnimationGroups.Material.Count==0
                &&still.AnimationGroups.Texcoord.Count==0&&still.AnimationGroups.Texture.Count==0&&still.GetPixels(0,0,0).Count==64,
                "still font-style decode creates no invented animation groups while retaining source texels");
            var untexturedData=F.Model();F.Half(untexturedData.Bytes,untexturedData.At["material0"]+68,65535);F.Half(untexturedData.Bytes,untexturedData.At["material0"]+70,65535);
            var untextured=Read.ReadCreditsModel(Source.Read(untexturedData.Bytes));
            Check(untextured.Materials[0].TextureId==-1&&untextured.Materials[0].PaletteId==-1&&untextured.RenderInstructionLists[0].Count>0,
                "exact untextured material decodes absent IDs and geometry without attempting a material pixel lookup");
            var otherData=F.Model();F.Word(otherData.Bytes,0,2);var otherModel=Source.Read(otherData.Bytes);
            Expect(()=>Read.ReadCreditsModel(otherModel,binding),"binding cannot be paired with a different model hash despite compatible counts/names");
            Check(Read.CachedModels.SequenceEqual(priorCached),"exact decoder does not adopt mutable appearances into global model caches");
            Console.WriteLine($"CREDITS DECODE PASS {checks} own synthetic checks; CPU supplied-byte decode/evaluation only, no filesystem asset reads or GPU.");
        }
        finally
        {
            Read.ApplyFixes=priorFixes;typeof(Headless).GetProperty(nameof(Headless.Active))!.SetValue(null,priorHeadless);
            paths.Clear();foreach(var pair in priorPaths)paths.Add(pair.Key,pair.Value);Paths.MphKey=priorKey;
            if(Directory.Exists(temp))Directory.Delete(temp,true);
        }
    }
    private static bool Finite(Matrix4 matrix)=>Finite(matrix.Row0)&&Finite(matrix.Row1)&&Finite(matrix.Row2)&&Finite(matrix.Row3);
    private static bool Finite(Vector4 v)=>float.IsFinite(v.X)&&float.IsFinite(v.Y)&&float.IsFinite(v.Z)&&float.IsFinite(v.W);
    private static void Check(bool value,string label){checks++;if(!value)throw new InvalidOperationException("Exact decode fixture: "+label);}
    private static void Expect(Action action,string label){checks++;try{action();}catch(InvalidDataException){return;}throw new InvalidOperationException("Exact decode expected refusal: "+label);}
}
