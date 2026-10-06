using MphRead.Mods.Replay;
using OpenTK.Mathematics;
using System.Text.Json;

internal static partial class Program
{
    private static async Task CheckCanonicalCameraWindowsAsync(string root)
    {

        bool SameCamera(ReplayCameraKeyframe a,ReplayCameraKeyframe b) => (a.Position-b.Position).Length<.00001f
            && Math.Abs((a.Rotation.X*b.Rotation.X+a.Rotation.Y*b.Rotation.Y+a.Rotation.Z*b.Rotation.Z+a.Rotation.W*b.Rotation.W))>.999999f && Math.Abs(a.Fov-b.Fov)<.00001f
            && Math.Abs(a.Roll-b.Roll)<.00001f && a.LookAtSlot==b.LookAtSlot;
        string dir=Path.Combine(root,"camera-window-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
        try {
            string replay=Path.Combine(dir,"source.ppdemo");File.WriteAllBytes(replay,[1,2,3]);
            foreach(var mode in Enum.GetValues<ReplayCameraInterpolation>()) foreach(var ease in Enum.GetValues<ReplayCameraEase>()) {
                var original=new ReplayCameraTrack();
                foreach(uint frame in new uint[]{0,60,120,180})Check(original.Put(new(frame,new(frame/20f,(frame%80)/10f,frame/30f),
                    Quaternion.FromAxisAngle(Vector3.UnitY,frame/300f),1+frame/500f,(sbyte)(frame/60),frame/180f,
                    mode,ease,new(1,4,-2),new(-2,8,1),.3f,-.2f,.4f,-.1f)),"fixture");
                var crop=original.Crop(23,157);
                string clip=Path.Combine(dir,"crop.ppclip");File.WriteAllText(clip,JsonSerializer.Serialize(new ReplayVirtualClipDocument(1,replay,23,157,DateTime.UtcNow,"Crop")));
                Check(crop.Save(clip),crop.LastError??"save");Check(File.ReadAllBytes(clip+".camera")[4]==5,"v5 window");
                var loaded=new ReplayCameraTrack();Check(loaded.Load(clip),loaded.LastError??"load");
                var transferred=new ReplayCameraTrack(); Check(transferred.ImportState(crop.ExportState()),"state import");
                foreach(bool constant in new[]{false,true})for(int i=0;i<=536;i++) {
                    double relative=i/4d;Check(original.Sample(23+relative,out var expected,constant)&&crop.Sample(relative,out var actual,constant)&&SameCamera(expected,actual),$"crop {mode} {ease} {relative}");
                    Check(loaded.Sample(relative,out var round,constant)&&SameCamera(expected,round),"roundtrip");
                    Check(transferred.Sample(relative,out var transfer,constant)&&SameCamera(expected,transfer),"identity-free transfer");
                }
                var nested=loaded.Crop(7,99);
                foreach(bool constant in new[]{false,true})for(int i=0;i<=184;i++) {
                    double relative=i/2d;Check(original.Sample(30+relative,out var expected,constant)&&nested.Sample(relative,out var actual,constant)&&SameCamera(expected,actual),"flattened nested");
                }
                ReplayCameraKeyframe sourceBefore=default; Check(loaded.Sample(37,out var before)&&original.Sample(60,out sourceBefore),"edit before");
                var edit=loaded.Keys.Single(k=>k.Frame==37) with {Position=new(9,8,7)};Check(loaded.Put(edit),"rebased edit");
                Check(loaded.Sample(37,out var changed)&&changed.Position==edit.Position,"own changed");
                Check(original.Sample(60,out var sourceAfter)&&SameCamera(sourceBefore,sourceAfter),"original unchanged");
                Check(loaded.Save(clip),"edit save");var edited=new ReplayCameraTrack();Check(edited.Load(clip),"edited reload");
                Check(edited.Sample(37,out var after)&&SameCamera(changed,after),"edited exact");
                var editedNested=edited.Crop(7,99);Check(editedNested.Sample(30,out var nestedEdit)&&SameCamera(after,nestedEdit),"edited second crop");
                byte[] good=edited.ExportState(); var damaged=(byte[])good.Clone(); damaged[6]^=1;
                Check(!edited.ImportState(damaged)&&edited.Sample(37,out var preserved)&&SameCamera(after,preserved),"state checksum rejects without changing track");
                Check(!edited.ImportState(new byte[9000]),"state bound");
            }
            using(var entered=new ManualResetEventSlim())using(var release=new ManualResetEventSlim()) {
                byte[]? persisted=null; int calls=0; var queue=new ReplayCameraSidecarQueue((bytes,publish)=>{if(Interlocked.Increment(ref calls)==1){entered.Set();release.Wait();}publish(()=>persisted=bytes);});
                queue.Enqueue([1]); Check(entered.Wait(TimeSpan.FromSeconds(2)),"writer entered");
                for(int i=2;i<=200;i++)queue.Enqueue([(byte)i]);
                using var cancel=new CancellationTokenSource();cancel.Cancel();
                try{await queue.FlushAsync(cancel.Token);throw new Exception("cancel waiter accepted");}catch(OperationCanceledException){Check(queue.Dirty,"cancellation retains edits");}
                release.Set();await queue.FlushAsync();Check(persisted!.SequenceEqual(new byte[]{200})&&!queue.Dirty&&calls==2,"bounded coalescing commits latest accepted state");
                for(int i=0;i<100;i++){queue.Enqueue([(byte)i]);await queue.FlushAsync();Check(persisted.SequenceEqual(new byte[]{(byte)i}),"return/next enqueue handoff");}
            }
            bool fail=true;var failed=new ReplayCameraSidecarQueue((_,publish)=>{if(fail)throw new IOException("fixture failure");publish(()=>{});});failed.Enqueue([1]);
            try{await failed.FlushAsync();throw new Exception("save failure accepted");}catch(IOException){Check(failed.Dirty&&failed.Error=="fixture failure","observable accepted save error");}
            fail=false;failed.Retry();await failed.FlushAsync();Check(!failed.Dirty&&failed.Error==null,"retry commits accepted state");
            using(var entered=new ManualResetEventSlim())using(var release=new ManualResetEventSlim()) {
                string durable=Path.Combine(dir,"durable.camera");File.WriteAllBytes(durable,[9]);
                var pending=new ReplayCameraSidecarQueue((bytes,publish)=>{entered.Set();release.Wait();publish(()=>File.WriteAllBytes(durable,bytes));});
                pending.Enqueue([1]);Check(entered.Wait(TimeSpan.FromSeconds(2)),"discard in-flight staged");pending.Enqueue([2]);
                Task discarding=pending.DiscardAsync();
                var wait=System.Diagnostics.Stopwatch.StartNew();while(pending.Dirty&&wait.Elapsed<TimeSpan.FromSeconds(2))await Task.Delay(5);
                Check(!pending.Dirty,"explicit discard invalidates unpublished generations");release.Set();await discarding;
                Check(File.ReadAllBytes(durable).SequenceEqual(new byte[]{9})&&pending.Error==null,"discard retains durable sidecar");
            }
            Console.WriteLine("Canonical fractional camera crops and detached authoring queue passed.");
        }finally{Directory.Delete(dir,true);}
    }
}
