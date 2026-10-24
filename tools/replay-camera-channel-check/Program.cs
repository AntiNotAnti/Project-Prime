using MphRead.Mods.Replay;
using OpenTK.Mathematics;
int checks=0;void Check(bool condition,string message){if(!condition)throw new Exception(message);checks++;}
var smooth=new ReplayCameraChannelCurves(ReplayCameraChannelMode.Smooth,ReplayCameraChannelEase.Inherit,ReplayCameraChannelMode.Smooth,ReplayCameraChannelEase.Inherit,ReplayCameraChannelMode.Smooth);
Quaternion Neg(Quaternion q)=>new(-q.X,-q.Y,-q.Z,-q.W);
Vector3 Angular(Quaternion from,Quaternion to,double dt)
{
 var delta=to*Quaternion.Invert(from);if(delta.W<0)delta=Neg(delta);
 double length=Math.Sqrt((double)delta.X*delta.X+(double)delta.Y*delta.Y+(double)delta.Z*delta.Z);
 return length<1e-9?Vector3.Zero:new Vector3(delta.X,delta.Y,delta.Z)*(float)(2*Math.Atan2(length,delta.W)/length/dt);
}
ReplayCameraTrack Track(uint[] frames,Quaternion[] rotations)
{
 var result=new ReplayCameraTrack();for(int i=0;i<frames.Length;i++)Check(result.Put(new(frames[i],new(i*2,i%2,i),rotations[i],1+i*.1f,-1,i*.2f,ReplayCameraInterpolation.Spline,ReplayCameraEase.None,Curves:smooth)),"channel fixture key");return result;
}
void Continuity(ReplayCameraTrack track)
{
 const double epsilon=.0001;
 for(int i=1;i+1<track.Keys.Count;i++)
 {
  uint frame=track.Keys[i].Frame;track.Sample(frame-epsilon,out var before);track.Sample(frame,out var at);track.Sample(frame+epsilon,out var after);
  var incoming=Angular(before.Rotation,at.Rotation,epsilon);var outgoing=Angular(at.Rotation,after.Rotation,epsilon);
  Check((incoming-outgoing).Length<.005f+Math.Max(incoming.Length,outgoing.Length)*.04f,$"C1 angular velocity at {frame}: {incoming} vs {outgoing}");
  double fovIn=(at.Fov-before.Fov)/epsilon,fovOut=(after.Fov-at.Fov)/epsilon;
  Check(Math.Abs(fovIn-fovOut)<.003+Math.Max(Math.Abs(fovIn),Math.Abs(fovOut))*.04,"C1 lens velocity");
  double rollIn=(at.Roll-before.Roll)/epsilon,rollOut=(after.Roll-at.Roll)/epsilon;
  Check(Math.Abs(rollIn-rollOut)<.003+Math.Max(Math.Abs(rollIn),Math.Abs(rollOut))*.04,"C1 roll velocity");
 }
 for(int segment=0;segment+1<track.Keys.Count;segment++)
 {
  double start=track.Keys[segment].Frame,end=track.Keys[segment+1].Frame;track.Sample(start,out var previous);
  for(int i=1;i<=512;i++)
  {
   track.Sample(start+(end-start)*i/512,out var current);
   double angle=Angular(previous.Rotation,current.Rotation,1).Length;
   Check(angle<.1,$"No hidden spherical branch jump: segment {segment} sample {i} angle {angle}");
   Check(float.IsFinite(current.Rotation.LengthSquared)&&Math.Abs(current.Rotation.LengthSquared-1)<1e-4,"finite normalized orientation");previous=current;
  }
 }
}
var ordinary=Track(new uint[]{0,23,107,250},new[]{Quaternion.Identity,Quaternion.FromEulerAngles(.4f,.7f,.2f),Quaternion.FromEulerAngles(.7f,1.2f,-.2f),Quaternion.FromEulerAngles(.1f,1.8f,.3f)});Continuity(ordinary);
var nearHalfTurn=Track(new uint[]{0,1,200,201},new[]{Quaternion.Identity,Neg(Quaternion.FromAxisAngle(new Vector3(1,2,3).Normalized(),3.1415f)),Quaternion.FromAxisAngle(new Vector3(-2,1,1).Normalized(),3.1414f),Neg(Quaternion.Identity)});Continuity(nearHalfTurn);

// The quaternion lift is independent of the file author's arbitrary q/-q sign choices.
var flipped=new ReplayCameraTrack();for(int i=0;i<ordinary.Keys.Count;i++)Check(flipped.Put(ordinary.Keys[i] with{Rotation=i%2==0?Neg(ordinary.Keys[i].Rotation):ordinary.Keys[i].Rotation}),"sign fixture");
for(int i=0;i<=1000;i++){double frame=i*.25;ordinary.Sample(frame,out var a,true);flipped.Sample(frame,out var b,true);Check(Angular(a.Rotation,b.Rotation,1).Length<1e-5,"quaternion sign invariant");}
var entranceExit=new ReplayCameraTrack();foreach(var key in ordinary.Keys)entranceExit.Put(key with{Ease=ReplayCameraEase.InOut});Continuity(entranceExit);
entranceExit.Sample(0,out var entrance);entranceExit.Sample(.001,out var entranceNext);entranceExit.Sample(250,out var exit);entranceExit.Sample(249.999,out var exitPrevious);
Check(Angular(entrance.Rotation,entranceNext.Rotation,.001).Length<.001&&Angular(exitPrevious.Rotation,exit.Rotation,.001).Length<.001,"inherited smooth entrance and exit ease stops angular velocity");
Continuity(Track(new uint[]{0,1,200,201},new[]{Quaternion.Identity,new Quaternion(1,0,0,0),new Quaternion(0,1,0,0),Quaternion.Identity}));
var random=new Random(1907);
for(int fixture=0;fixture<12;fixture++)
{
 uint[] frames={0,(uint)random.Next(1,4),(uint)random.Next(100,400),500};
 var rotations=new Quaternion[4];for(int i=0;i<4;i++){rotations[i]=Quaternion.FromEulerAngles((float)(random.NextDouble()*6-3),(float)(random.NextDouble()*6-3),(float)(random.NextDouble()*6-3));if(i%2==0)rotations[i]=Neg(rotations[i]);}
 Continuity(Track(frames,rotations));
}
// Only opted channels change; inherited channels retain legacy position arc timing and exact float bits.
var legacy=new ReplayCameraTrack();foreach(var key in ordinary.Keys)legacy.Put(key with{Curves=default});
foreach(var channel in new[]{0,1,2})
{
 var isolated=new ReplayCameraTrack();foreach(var key in legacy.Keys)isolated.Put(key with{Curves=channel switch{0=>new(ReplayCameraChannelMode.Smooth),1=>new(Fov:ReplayCameraChannelMode.Smooth),_=>new(Roll:ReplayCameraChannelMode.Smooth)}});
 for(int i=1;i<999;i++)
 {
  double frame=i*.25;legacy.Sample(frame,out var original,true);isolated.Sample(frame,out var changed,true);isolated.Sample(frame,out var normal,false);
  Check(original.Position==changed.Position,"channel never changes position path");
  Check(channel==0||original.Rotation==changed.Rotation,"unselected orientation inherits exact bits");
  Check(channel==1||original.Fov==changed.Fov,"unselected FOV inherits exact bits");Check(channel==2||original.Roll==changed.Roll,"unselected roll inherits exact bits");
  Check(channel switch{0=>changed.Rotation==normal.Rotation,1=>changed.Fov==normal.Fov,_=>changed.Roll==normal.Roll},"opted channel uses canonical time independent of position arc remapping");
 }
}
// PCHIP is bounded within each pair even through turning points, and scalar Bezier cannot exceed the valid lens domain.
var turning=new ReplayCameraTrack();float[] lens={.02f,2.9f,.5f,3f,.1f};
for(int i=0;i<lens.Length;i++)turning.Put(new((uint)(i*i*20),new(i,0,0),Quaternion.Identity,lens[i],Roll:lens[i]-1,Interpolation:ReplayCameraInterpolation.Spline,Ease:ReplayCameraEase.None,Curves:smooth));
for(int segment=0;segment+1<turning.Keys.Count;segment++)for(int i=0;i<=1000;i++)
{
 var left=turning.Keys[segment];var right=turning.Keys[segment+1];turning.Sample(left.Frame+(right.Frame-left.Frame)*i/1000d,out var sample);
 Check(sample.Fov>=Math.Min(left.Fov,right.Fov)-1e-6&&sample.Fov<=Math.Max(left.Fov,right.Fov)+1e-6,"monotone lens avoids overshoot");
 Check(sample.Roll>=Math.Min(left.Roll,right.Roll)-1e-6&&sample.Roll<=Math.Max(left.Roll,right.Roll)+1e-6,"monotone roll avoids overshoot");
}

var eased=new ReplayCameraTrack();eased.Put(new(0,Vector3.Zero,Quaternion.Identity,1,Roll:0,Interpolation:ReplayCameraInterpolation.Spline,Ease:ReplayCameraEase.None,
 Curves:new(ReplayCameraChannelMode.Linear,ReplayCameraChannelEase.In,ReplayCameraChannelMode.Linear,ReplayCameraChannelEase.Out,ReplayCameraChannelMode.Hold,ReplayCameraChannelEase.None)));
eased.Put(new(60,Vector3.One,Quaternion.FromAxisAngle(Vector3.UnitY,1),2,Roll:1));eased.Sample(30,out var easeSample,true);
Check(Math.Abs(Angular(Quaternion.Identity,easeSample.Rotation,1).Length-.25)<1e-5&&Math.Abs(easeSample.Fov-1.75)<1e-6&&easeSample.Roll==0,"independent orientation/FOV/roll easing follows canonical time");
var independent=new ReplayCameraTrack();independent.Put(new(0,Vector3.Zero,Quaternion.Identity,1,Roll:0,Interpolation:ReplayCameraInterpolation.Spline,Ease:ReplayCameraEase.InOut,
 FovOutgoingTangent:float.MaxValue,RollOutgoingTangent:-float.MaxValue,Curves:new(ReplayCameraChannelMode.Linear,ReplayCameraChannelEase.None,ReplayCameraChannelMode.Bezier,ReplayCameraChannelEase.None,ReplayCameraChannelMode.Bezier,ReplayCameraChannelEase.None)));
independent.Put(new(60,Vector3.One,Quaternion.FromAxisAngle(Vector3.UnitY,1),2,Roll:1));
for(int i=0;i<=240;i++){independent.Sample(i*.25,out var sample,true);Check(float.IsFinite(sample.Fov)&&sample.Fov>=MathHelper.DegreesToRadians(1)&&sample.Fov<=MathHelper.DegreesToRadians(175),"advanced Bezier FOV finite and clamped");Check(float.IsFinite(sample.Roll)&&Math.Abs(sample.Roll)<=MathHelper.TwoPi,"advanced Bezier roll finite and clamped");}
var hold=new ReplayCameraTrack();hold.Put(independent.Keys[0] with{Interpolation=ReplayCameraInterpolation.Hold});hold.Put(independent.Keys[1]);
hold.Sample(59.99,out var held);Check(held.Position==hold.Keys[0].Position&&held.Rotation==hold.Keys[0].Rotation&&held.Fov==hold.Keys[0].Fov&&held.Roll==hold.Keys[0].Roll,"deliberate position Hold dominates all channels");
hold.Sample(60,out held);Check(held.Position==hold.Keys[1].Position&&held.Rotation==hold.Keys[1].Rotation&&held.Fov==hold.Keys[1].Fov,"Hold cut lands exactly");
Check(!ordinary.Put(ordinary.Keys[0] with{Curves=new(ReplayCameraChannelMode.Bezier)}),"orientation rejects scalar-only Bezier");
Check(!ordinary.Put(ordinary.Keys[0] with{Curves=new(Fov:(ReplayCameraChannelMode)255)}),"unknown channel mode rejected");
Check(!ordinary.Put(ordinary.Keys[0] with{Curves=new(FovEase:(ReplayCameraChannelEase)255)}),"unknown channel easing rejected");

var editedTrack=new ReplayCameraTrack();foreach(var key in ordinary.Keys)editedTrack.Put(key);editedTrack.Sample(50.5,out var oldCached,true);
Check(editedTrack.Put(editedTrack.Keys[1] with{Rotation=Quaternion.FromEulerAngles(-.4f,.2f,.8f),Fov=2.5f,Roll=-1}),"edit cached controls");
var freshTrack=new ReplayCameraTrack();foreach(var key in editedTrack.Keys)freshTrack.Put(key);
for(int i=0;i<500;i++){editedTrack.Sample(i*.5,out var a,true);freshTrack.Sample(i*.5,out var b,true);Check(a==b,"key edit invalidates cached angular/scalar controls");}
Check(editedTrack.Remove(107),"remove cached key");freshTrack.Clear();foreach(var key in editedTrack.Keys)freshTrack.Put(key);
for(int i=0;i<500;i++){editedTrack.Sample(i*.5,out var a,true);freshTrack.Sample(i*.5,out var b,true);Check(a==b,"key removal invalidates cached angular/scalar controls");}
Check(editedTrack.ImportState(ordinary.ExportState()),"restore cached authoring state");for(int i=0;i<500;i++){editedTrack.Sample(i*.5,out var a,true);ordinary.Sample(i*.5,out var b,true);Check(a==b,"state restore invalidates cached controls");}
// Fractional preview/export/cropped sampling is one evaluator at 30/60/120/240 FPS.
var cropped=ordinary.Crop(7,240);
foreach(double invalidTime in new[]{double.NaN,double.PositiveInfinity,double.NegativeInfinity})
{
 Check(!ordinary.Sample(invalidTime,out _)&&!cropped.Sample(invalidTime,out _),"nonfinite canonical time fails closed before normal or cropped evaluation");
}var portable=new ReplayCameraTrack();Check(portable.ImportState(cropped.ExportState()),"advanced cropped portable import");
foreach(int fps in new[]{30,60,120,240})for(int i=0;i<233*fps/60;i++)
{
 double frame=i*60d/fps;ordinary.Sample(7+frame,out var a,true);portable.Sample(frame,out var b,true);
 Check(a.Position==b.Position&&a.Rotation==b.Rotation&&a.Fov==b.Fov&&a.Roll==b.Roll,"preview/export/crop channel equality at "+fps);
}
string directory=Path.Combine(Path.GetTempPath(),"prime-channel-check-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);string replay=Path.Combine(directory,"source.ppdemo");File.WriteAllBytes(replay,new byte[]{1,2,3});
try
{
 byte[] state=ordinary.ExportState();Check(state[4]==3,"advanced state selects v3");var roundtrip=new ReplayCameraTrack();Check(roundtrip.ImportState(state)&&roundtrip.Keys.SequenceEqual(ordinary.Keys),"advanced state preserves all channel choices and native bits");
 Check(ordinary.Save(replay)&&File.ReadAllBytes(replay+".camera")[4]==7,"advanced sidecar selects v7");
 Check(roundtrip.Load(replay)&&roundtrip.Keys.SequenceEqual(ordinary.Keys),"v7 source-bound sidecar round trip");

 byte[] invalidSidecar=File.ReadAllBytes(replay+".camera");int firstKey=invalidSidecar.Length-32-ordinary.Keys.Count*91;invalidSidecar[firstKey+85]=255;
 System.Security.Cryptography.SHA256.HashData(invalidSidecar.AsSpan(0,invalidSidecar.Length-32)).CopyTo(invalidSidecar.AsSpan(invalidSidecar.Length-32));File.WriteAllBytes(replay+".camera",invalidSidecar);
 Check(!roundtrip.Load(replay),"checksummed unknown channel mode rejected by v7 sidecar reader");Check(ordinary.Save(replay),"restore advanced sidecar after malformed check");
 Check(ordinary.EncodeReviewed(ReplayCameraTrackIdentity.Create(replay)).SequenceEqual(File.ReadAllBytes(replay+".camera")),"v7 reviewed encoder matches atomic source-bound Save");
 Check(legacy.ExportState()[4]==2&&legacy.Save(replay)&&File.ReadAllBytes(replay+".camera")[4]==6,"all-inherit retains v6/state2 formats");
 // Mutate six-byte enum metadata while retaining a valid outer checksum; the decoded document must still fail closed.
 byte[] invalid=(byte[])state.Clone();invalid[10+85]=3;System.Security.Cryptography.SHA256.HashData(invalid.AsSpan(0,invalid.Length-32)).CopyTo(invalid.AsSpan(invalid.Length-32));
 Check(!roundtrip.ImportState(invalid),"checksummed orientation Bezier rejected on state import");
 invalid=(byte[])state.Clone();invalid[10+86]=255;System.Security.Cryptography.SHA256.HashData(invalid.AsSpan(0,invalid.Length-32)).CopyTo(invalid.AsSpan(invalid.Length-32));Check(!roundtrip.ImportState(invalid),"checksummed unknown easing rejected");
 var large=new ReplayCameraTrack();for(uint i=0;i<1024;i++)Check(large.Put(ordinary.Keys[(int)(i%4)] with{Frame=i*3}),"1024 advanced keys");
 Check(large.ExportState().Length<=ReplayCameraTrack.MaxStateBytes&&roundtrip.ImportState(large.ExportState())&&roundtrip.Keys.SequenceEqual(large.Keys),"1024 advanced state bound and round trip");
 Check(large.Save(replay)&&new FileInfo(replay+".camera").Length<=ReplayCameraTrack.MaxSidecarBytes&&roundtrip.Load(replay),"1024 advanced sidecar bound and round trip");
 var crop=large.Crop(4,3064);var adopt=new ReplayCameraTrack();Check(adopt.ImportState(crop.ExportState()),"1024 advanced cropped state");
 for(int i=0;i<200;i++){double frame=i*15.125;large.Sample(4+frame,out var a,true);adopt.Sample(frame,out var b,true);Check(a.Rotation==b.Rotation&&a.Fov==b.Fov&&a.Roll==b.Roll,"large fractional cropped channels identical");}
 for(int i=0;i<1023;i++)large.Sample(i*3+1.25,out _,true);long allocation=GC.GetAllocatedBytesForCurrentThread();
 for(int i=0;i<100000;i++)large.Sample((i%1023)*3+1.25,out _,true);
 Check(GC.GetAllocatedBytesForCurrentThread()-allocation<4096,"cached smooth controls allocate no per-sample objects");
 const int batches=400,batchSize=128;var timings=new double[batches];long ticks=System.Diagnostics.Stopwatch.GetTimestamp();double checksum=0;
 long allocationStart=GC.GetAllocatedBytesForCurrentThread();
 for(int batch=0;batch<batches;batch++)
 {
  long start=System.Diagnostics.Stopwatch.GetTimestamp();for(int i=0;i<batchSize;i++){large.Sample(((batch*batchSize+i)%1023)*3+1.25,out var sample,true);checksum+=sample.Fov+sample.Roll+sample.Rotation.X;}
  timings[batch]=(System.Diagnostics.Stopwatch.GetTimestamp()-start)*1e9/System.Diagnostics.Stopwatch.Frequency/batchSize;
 }
 long bytes=GC.GetAllocatedBytesForCurrentThread()-allocationStart;Array.Sort(timings);
 Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new{Kind="Advanced channels, 1024 keys, cached angular and scalar controls",SampleCount=batches*batchSize,P50Ns=timings[200],P95Ns=timings[380],P99Ns=timings[396],AllocationBytes=bytes,Checksum=checksum}));
}
finally{Directory.Delete(directory,true);}
SceneProbeChecks.Run(Check);
Console.WriteLine($"PASS {checks} channel math checks");
namespace MphRead.Mods.Replay
{
 internal sealed record ReplayVirtualClipDocument(string SourceReplay,uint StartFrame,uint EndFrame);
 internal static class ReplayVirtualClips{internal const string Extension=".ppclip";internal static bool TryLoad(string path,out ReplayVirtualClipDocument? clip){clip=null;return false;}}
}
