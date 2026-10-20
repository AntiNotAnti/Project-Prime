using System.Security.Cryptography;
using System.Text.Json;
using MphRead;
using MphRead.CorpsePhysics;
using MphRead.Formats;
using MphRead.Mods;
using OpenTK.Mathematics;
using M=System.Numerics.Matrix4x4;
using V=System.Numerics.Vector3;
using Q=System.Numerics.Quaternion;

// Native14 CPU calibration/pose fitting only. Virtual anatomy and primitive
// colliders are authored; this is not a native draw or accepted gameplay death.
internal static class CorpseFitNativeChecks
{
    private static M N(Matrix4 m)=>new(m.M11,m.M12,m.M13,m.M14,m.M21,m.M22,m.M23,m.M24,m.M31,m.M32,m.M33,m.M34,m.M41,m.M42,m.M43,m.M44);
    private static float[] Values(M m)=>[m.M11,m.M12,m.M13,m.M14,m.M21,m.M22,m.M23,m.M24,m.M31,m.M32,m.M33,m.M34,m.M41,m.M42,m.M43,m.M44];
    private static float Error(M a,M b)=>Values(a).Zip(Values(b),(x,y)=>Math.Abs(x-y)).Max();
    private static V Axis(CorpsePose p,V axis)=>V.Transform(axis,p.Orientation);
    private static (V From,V To) Ends(CapturedCorpseRig c,int part)
    {var p=c.InitialBodies[part];var d=Axis(p,V.UnitY)*c.Rig.Parts[part].Size.Y*.5f;return(p.Position-d,p.Position+d);}
    private static float Bottom(CorpseRig rig,ReadOnlySpan<CorpsePose> poses)
    {
        float result=float.PositiveInfinity;
        for(int i=0;i<15;i++)foreach(int x in new[]{-1,1})foreach(int y in new[]{-1,1})foreach(int z in new[]{-1,1})
            result=Math.Min(result,(poses[i].Position+V.Transform(rig.Parts[i].Size*new V(x,y,z)*.5f,poses[i].Orientation)).Y);
        return result;
    }
    internal static int Run(string data,string output)
    {
        output=Path.GetFullPath(output);string cwd=Directory.GetCurrentDirectory();int checks=0,calibrations=0,neutrals=0,supported=0,refused=0;
        var failures=new List<string>();var results=new List<object>();float maximumRestError=0,maximumJointDrift=0;
        void Check(bool good,string label){checks++;if(!good){failures.Add(label);Console.Error.WriteLine("NATIVE FIT FAIL "+label);}}
        try
        {
            Directory.CreateDirectory(output);Directory.SetCurrentDirectory(Path.GetFullPath(data));Paths.UpdatePaths();Paths.ChooseMphPath();Headless.Enter();
            var rng=Rng.Current;var rngBefore=(rng.Rng1,rng.Rng2);
            foreach(Hunter hunter in Enum.GetValues<Hunter>().Where(h=>(int)h>=0&&(int)h<7))for(int lod=0;lod<2;lod++)
            {
                string label=hunter+"/LOD"+lod;var inst=Read.GetModelInstance(Metadata.HunterModels[hunter][lod],noCache:true);var model=inst.Model;
                var names=model.Nodes.Select(n=>n.Name).ToArray();var parents=model.Nodes.Select(n=>n.ParentIndex).ToArray();var paletteIds=model.NodeMatrixIds.ToArray();
                int Find(string name)=>Array.IndexOf(names,name);
                void Pose(int bank,int frame,Matrix4 world)
                {inst.SetAnimation(bank);inst.AnimInfo.Frame[inst.AnimInfo.Node.Slot]=frame;model.AnimateNodes(0,false,Matrix4.Identity,Vector3.One,inst.AnimInfo);foreach(var n in model.Nodes)n.Animation*=world;model.UpdateMatrixStack();}
                Pose(0,0,Matrix4.Identity);var neutralNodes=model.Nodes.Select(n=>N(n.Animation)).ToArray();var neutralPalette=model.MatrixStackValues.ToArray();
                float virtualLength=hunter is Hunter.Trace or Hunter.Noxus?.4f:.28125f;
                bool calibrated=CorpseHumanoidProfile.TryCreate(label,names,parents,neutralNodes,V.UnitY,-V.UnitZ,virtualLength,out var profile);
                Check(calibrated,label+" independent native bank0/frame0 profile calibration");
                Check(model.Nodes.Select(n=>N(n.Animation)).SequenceEqual(neutralNodes)&&model.MatrixStackValues.SequenceEqual(neutralPalette),label+" calibration leaves native matrices/palette exact");
                if(!calibrated){results.Add(new{hunter=hunter.ToString(),lod,calibrated,reason="TryCreate refused native neutral metadata"});continue;}calibrations++;
                bool neutralOkay=profile!.TryCapture(neutralNodes,V.Zero,out var neutral);
                Check(neutralOkay,label+" native neutral capture");
                if(!neutralOkay){results.Add(new{hunter=hunter.ToString(),lod,calibrated,neutralOkay,reason="TryCapture refused neutral"});continue;}neutrals++;
                var neutralBodies=neutral!.InitialBodies.ToArray();var neutralRestored=new M[names.Length];
                Check(neutral.Binding.TrySample(neutralBodies,neutralRestored)&&neutralNodes.Zip(neutralRestored,Error).Max()<.00003f,label+" neutral affine rest preservation");
                var localHandCenters=new V[2];var localHandAxes=new V[2];var localFootCenters=new V[2];var localFootForward=new V[2];var localFootUp=new V[2];
                for(int side=0;side<2;side++)
                {
                    int arm=side==0?3:6,foot=side==0?11:14;string prefix=side==0?"L_":"R_";
                    int anchor=Find(prefix+"wrist");if(anchor<0)anchor=Find(prefix+"elbow");M.Invert(neutralNodes[anchor],out var inverseHand);
                    localHandCenters[side]=V.Transform(neutralBodies[arm+2].Position,inverseHand);localHandAxes[side]=V.TransformNormal(Axis(neutralBodies[arm+2],V.UnitY),inverseHand);
                    M.Invert(neutralNodes[Find(prefix+"ankle")],out var inverseFoot);
                    localFootCenters[side]=V.Transform(neutralBodies[foot].Position,inverseFoot);localFootForward[side]=V.TransformNormal(Axis(neutralBodies[foot],V.UnitY),inverseFoot);localFootUp[side]=V.TransformNormal(Axis(neutralBodies[foot],V.UnitZ),inverseFoot);
                    var footOffset=neutralBodies[foot].Position-neutralNodes[Find(prefix+"ankle")].Translation;
                    Check(V.Dot(footOffset,V.UnitY)<0&&V.Dot(footOffset,-V.UnitZ)>0&&V.Dot(Axis(neutralBodies[foot],V.UnitY),-V.UnitZ)>.9999f,label+" neutral sole is below/forward of ankle and faces native-Z");
                }
                int profileSupported=0,profileRefused=0,bentVirtualCases=0,handFootChecks=0;float maxProfileRest=0,maxHandFoot=0;var cases=new List<object>();
                var q=Q.Normalize(Q.CreateFromYawPitchRoll(.31f,.13f,-.07f));V origin=new(7.25f,-1.5f,12.5f);
                var world=Matrix4.CreateFromQuaternion(new Quaternion(q.X,q.Y,q.Z,q.W))*Matrix4.CreateTranslation(origin.X,origin.Y,origin.Z);
                for(int bank=0;bank<model.AnimationGroups.Node.Count;bank++)
                {
                    int frames=model.AnimationGroups.Node[bank].FrameCount;if(frames<=0)continue;
                    foreach(int frame in new[]{0,frames/2,frames-1}.Distinct())
                    {
                        Pose(bank,frame,world);var current=model.Nodes.Select(n=>N(n.Animation)).ToArray();var nativePalette=model.MatrixStackValues.ToArray();
                        var transforms=model.Nodes.Select(n=>n.Transform).ToArray();var controls=model.Nodes.Select(n=>(n.BeforeTransform,n.AfterTransform,n.AnimIgnoreParent,n.AnimIgnoreChild)).ToArray();
                        bool okay=profile.TryCapture(current,origin,out var capture);
                        if(!okay){profileRefused++;refused++;cases.Add(new{bank,frame,supported=false,reason="profile refused calibrated scale/segment/rig policy",minimumBasisLength=current.Min(m=>new V(m.M11,m.M12,m.M13).Length()),maximumBasisLength=current.Max(m=>new V(m.M11,m.M12,m.M13).Length())});}
                        else
                        {
                            profileSupported++;supported++;var fitted=capture!;var bodies=fitted.InitialBodies.ToArray();var parts=fitted.Rig.Parts.ToArray();
                            Check(parts.Length==15&&bodies.Length==15,label+"15 independent fitted parts/bodies");
                            for(int part=0;part<15;part++)
                            {
                                var p=parts[part];var b=bodies[part];var matrix=b.Matrix;
                                Check(p.Rest.IsValid&&b.IsValid&&float.IsFinite(p.Size.LengthSquared())&&p.Size.X is >=.03f and <=2&&p.Size.Y is >=.03f and <=2&&p.Size.Z is >=.03f and <=2
                                    &&p.Joint.LengthSquared()<=16&&p.Rest.Position.LengthSquared()<=16&&Math.Abs(p.JointBasis.LengthSquared()-1)<.001f
                                    &&Math.Abs(matrix.GetDeterminant()-1)<.001f&&V.Distance(b.Position,origin+p.Rest.Position)<.00001f,label+" finite bounded positive rigid part frame"+part);
                            }
                            var restored=new M[names.Length];bool sampled=fitted.Binding.TrySample(bodies,restored);Check(sampled,label+" fitted affine sample");
                            float rest=sampled?current.Zip(restored,Error).Max():float.MaxValue;maximumRestError=Math.Max(maximumRestError,rest);maxProfileRest=Math.Max(maxProfileRest,rest);
                            Check(rest<.00003f,label+" full composed native rest pose preserved");
                            float endpointError=0,handFootError=0;int bent=0;
                            for(int side=0;side<2;side++)
                            {
                                int arm=side==0?3:6,foot=side==0?11:14;string prefix=side==0?"L_":"R_";
                                var elbow=current[Find(prefix+"elbow")];int wristIndex=Find(prefix+"wrist");var forearm=Ends(fitted,arm+1);var hand=Ends(fitted,arm+2);
                                V wrist=wristIndex>=0?current[wristIndex].Translation:V.Transform(new V(virtualLength,0,0),elbow);
                                endpointError=Math.Max(endpointError,Math.Max(V.Distance(forearm.From,elbow.Translation),Math.Max(V.Distance(forearm.To,wrist),V.Distance(hand.From,wrist))));
                                if(wristIndex<0)
                                {
                                    var upper=elbow.Translation-current[Find(prefix+"shoulder")].Translation;var distal=wrist-elbow.Translation;
                                    if(V.Cross(V.Normalize(upper),V.Normalize(distal)).Length()>.2f){bent++;bentVirtualCases++;}
                                }
                                var handAnchor=current[wristIndex>=0?wristIndex:Find(prefix+"elbow")];var ankle=current[Find(prefix+"ankle")];
                                V expectedHand=V.Transform(localHandCenters[side],handAnchor),expectedFoot=V.Transform(localFootCenters[side],ankle);
                                handFootError=Math.Max(handFootError,Math.Max(V.Distance(expectedHand,bodies[arm+2].Position),V.Distance(expectedFoot,bodies[foot].Position)));
                                Check(V.Dot(V.Normalize(V.TransformNormal(localHandAxes[side],handAnchor)),Axis(bodies[arm+2],V.UnitY))>.9999f,label+" hand follows its own captured wrist/forearm frame");
                                Check(V.Dot(V.Normalize(V.TransformNormal(localFootForward[side],ankle)),Axis(bodies[foot],V.UnitY))>.9999f
                                    &&V.Dot(V.Normalize(V.TransformNormal(localFootUp[side],ankle)),Axis(bodies[foot],V.UnitZ))>.9999f,label+" foot forward/up follow captured ankle frame");handFootChecks+=2;
                            }
                            Check(endpointError<.00002f,label+" actual/virtual wrist uses forearm local-X (not straight upper-arm extrapolation)");
                            Check(handFootError<.00003f,label+" hand/sole centers transform with their calibration anchors");maxHandFoot=Math.Max(maxHandFoot,handFootError);
                            cases.Add(new{bank,frame,supported=true,restError=rest,endpointError,handFootError,bentVirtualSides=bent});
                        }
                        Check(model.Nodes.Select(n=>N(n.Animation)).SequenceEqual(current)&&model.MatrixStackValues.SequenceEqual(nativePalette)&&model.Nodes.Select(n=>n.Transform).SequenceEqual(transforms)
                            &&model.Nodes.Select(n=>(n.BeforeTransform,n.AfterTransform,n.AnimIgnoreParent,n.AnimIgnoreChild)).SequenceEqual(controls)&&model.NodeMatrixIds.SequenceEqual(paletteIds),label+" fit/sample preserve native node controls/palette/skin binding");
                    }
                }
                Check(profileSupported>0,label+" supported actual posed cases");Check(bentVirtualCases>0,label+" missing wrist exercised in an actually bent native forearm");
                // A separately declared synthetic floor/spawn tests fitted joint
                // frames numerically, not native room or accepted-death policy.
                float floor=Bottom(neutral.Rig,neutralBodies)-.05f;
                var triangles=ReferenceScene.Floor().Triangles.ToArray().Select(t=>new StaticTriangle(t.A+new V(0,floor,0),t.B+new V(0,floor,0),t.C+new V(0,floor,0))).ToArray();
                var anchorsA=new V[15];var anchorsB=new V[15];var rigParts=neutral.Rig.Parts.ToArray();
                for(int i=1;i<15;i++)
                {
                    V joint=neutral.Origin.Position+rigParts[i].Joint;int parent=rigParts[i].Parent;
                    anchorsA[i]=V.Transform(joint-neutralBodies[parent].Position,Q.Conjugate(neutralBodies[parent].Orientation));
                    anchorsB[i]=V.Transform(joint-neutralBodies[i].Position,Q.Conjugate(neutralBodies[i].Orientation));
                }
                float jointDrift=0,minBottom=float.PositiveInfinity;int maxContactConstraints=0;bool finite=true;
                using(var backend=new BepuCorpseBackend(new(triangles),CorpseQuality.Low,true))
                {
                    var spawn=new CorpseSpawnDescriptor(new(new(1,0,0,0,0,1,0),new(1,0,0,0)),neutral.Rig,neutral.Origin,V.Zero,V.Zero,null,(uint)((int)hunter*2+lod),0,180);
                    bool started=backend.TrySpawn(spawn,out var handle);Check(started,label+" synthetic fitted neutral spawn");var poses=new CorpsePose[15];
                    for(uint tick=1;tick<=120&&started;tick++)
                    {
                        bool step=backend.Step(tick)&&backend.Sample(handle,1,poses)&&poses.All(p=>p.IsValid);finite&=step;if(!step)break;
                        minBottom=Math.Min(minBottom,Bottom(neutral.Rig,poses));maxContactConstraints=Math.Max(maxContactConstraints,backend.SolverConstraints-42);
                        for(int i=1;i<15;i++){var a=poses[rigParts[i].Parent];var b=poses[i];jointDrift=Math.Max(jointDrift,V.Distance(a.Position+V.Transform(anchorsA[i],a.Orientation),b.Position+V.Transform(anchorsB[i],b.Orientation)));}
                    }
                    Check(started&&finite&&float.IsFinite(jointDrift)&&backend.Statistics.Faults==0,label+" fitted120step synthetic CPU probe stays finite");maximumJointDrift=Math.Max(maximumJointDrift,jointDrift);
                    Check(maxContactConstraints>0&&float.IsFinite(minBottom)&&minBottom>=floor-.15f,label+" synthetic fitted floor genuinely contacts and supports all body corners");
                    results.Add(new{hunter=hunter.ToString(),lod,calibrated,neutralOkay,authoredVirtualForearmLocal=virtualLength,profileSupported,profileRefused,bentVirtualCases,handFootChecks,maxProfileRest,maxHandFoot,
                        syntheticPhysics=new{steps=120,floor,minimumBoxCornerY=float.IsFinite(minBottom)?(float?)minBottom:null,jointDrift,maxContactConstraints,finite,stats=backend.Statistics},cases});
                }
                Console.WriteLine($"NATIVE FIT {label} supported={profileSupported} refused={profileRefused} bentVirtual={bentVirtualCases} jointDrift={jointDrift}");
            }
            Check(calibrations==14&&neutrals==14,"all14 independent native calibrations/neutral captures succeeded");
            Check(ReferenceEquals(rng,Rng.Current)&&(rng.Rng1,rng.Rng2)==rngBefore,"fit/affine/independent physics leave gameplay RNG unchanged");
            File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new{checks,calibrations,neutrals,supported,refused,maximumRestError,maximumJointDrift,failures,profiles=results,
                gameAssemblySha256=Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(typeof(Scene).Assembly.Location))),coreAssemblySha256=Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(typeof(CorpseHumanoidProfile).Assembly.Location))),
                scope="Actual14 native CPU rigs and perLOD calibration; current full-bank single-clip world poses, explicit authored virtual endpoints; no PlayerDraw/crouch blend, genuine accepted death, native room barrier, art/render/device/game qualification. Physics uses separately declared synthetic neutral floor/spawn; drift is a measurement, not an art-quality limit."},new JsonSerializerOptions{WriteIndented=true}));
            Console.WriteLine($"NATIVE FIT checks={checks} calibrations={calibrations} neutral={neutrals} supported={supported} refused={refused} failures={failures.Count}");return failures.Count==0?0:1;
        }
        finally{Directory.SetCurrentDirectory(cwd);}
    }
}
