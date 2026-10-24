using System.Diagnostics;
using System.Text.Json;
using MphRead.Mods.Replay;
using OpenTK.Mathematics;

const int Keys=64, Batch=128, Batches=400;
foreach(var interpolation in new[]{ReplayCameraInterpolation.Spline,ReplayCameraInterpolation.Bezier})
{
 var track=new ReplayCameraTrack();
 for(uint i=0;i<Keys;i++)
 {
  float phase=i*.7f;
  if(!track.Put(new(i*60,new(MathF.Sin(phase)*30,i%7,MathF.Cos(phase)*20),
      Quaternion.FromAxisAngle(Vector3.UnitY,phase),1.2f,-1,0,interpolation,ReplayCameraEase.InOut,
      new Vector3(-2,4,1),new Vector3(3,1,-2))))throw new InvalidOperationException(track.LastError);
 }
 double checksum=0;
 // Warm runtime/tiered JIT and all segments before measuring steady-state camera sampling.
 for(int i=0;i<100000;i++)track.Sample((i%63)*60+17.125,out _,true);
 var samples=new double[Batches];Stopwatch.GetTimestamp();long allocation=GC.GetAllocatedBytesForCurrentThread();
 for(int batch=0;batch<Batches;batch++)
 {
  long start=Stopwatch.GetTimestamp();
  for(int i=0;i<Batch;i++)
  {
   if(!track.Sample(((batch*Batch+i)%63)*60+17.125,out var sample,true))throw new InvalidOperationException("Missing sample");
   checksum+=sample.Position.X+sample.Position.Y+sample.Position.Z+sample.Fov;
  }
  samples[batch]=(Stopwatch.GetTimestamp()-start)*1e9/Stopwatch.Frequency/Batch;
 }
 long bytes=GC.GetAllocatedBytesForCurrentThread()-allocation;Array.Sort(samples);
 Console.WriteLine(JsonSerializer.Serialize(new{Interpolation=interpolation.ToString(),Keys,SampleCount=Batch*Batches,
  P50Ns=samples[Batches/2],P95Ns=samples[(int)(Batches*.95)],P99Ns=samples[(int)(Batches*.99)],AllocationBytes=bytes,Checksum=checksum}));
}
namespace MphRead.Mods.Replay
{
 internal sealed record ReplayVirtualClipDocument(string SourceReplay,uint StartFrame,uint EndFrame);
 internal static class ReplayVirtualClips
 {
  internal const string Extension=".ppclip";
  internal static bool TryLoad(string path,out ReplayVirtualClipDocument? clip){clip=null;return false;}
 }
}
