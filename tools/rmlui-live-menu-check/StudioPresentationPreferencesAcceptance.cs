using MphRead.Mods.Replay;

internal static class StudioPresentationPreferencesAcceptance
{
    internal static void Run()
    {
        string directory=Path.Combine(Path.GetTempPath(),"prime-studio-prefs-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string logical=Path.Combine(directory,"recording.ppdemo");string source="EXPLICIT-SYNTHETIC-CACHE:SOURCE-SHA:"+new string('A',64)+":PLAYBACK-SHA:"+new string('B',64)+":RANGE:0:100";
        try
        {
            var missing=ReplayStudioPresentationPreferencesStore.ReadAsync(logical,source).GetAwaiter().GetResult();
            Check(missing.Preferences==null,"missing preferences do not fabricate a stored camera");
            var pose=new ReplayStudioPreferencePose(1,2,3,0,0,0,2,MathF.PI/3,.1f);
            var settings=new ReplayStudioPresentationPreferences(ReplayCameraMode.Free,7,150,false,true,false,false,false,pose);
            var frozen=ReplayStudioPresentationPreferencesStore.Freeze(settings);int verifications=0;
            var saved=ReplayStudioPresentationPreferencesStore.SaveAsync(missing,settings,()=>verifications++).GetAwaiter().GetResult();
            var restored=ReplayStudioPresentationPreferencesStore.ReadAsync(logical,source).GetAwaiter().GetResult();
            Check(restored.Preferences==frozen&&verifications>=1,"camera, slot, normalized pose, full FOV, HUD and combat choices round-trip under owner verification");
            var relocated=ReplayStudioPresentationPreferencesStore.Read(logical,source.Replace("EXPLICIT-SYNTHETIC-CACHE","NEW-EXTRACTION-CACHE").Replace(new string('B',64),new string('C',64)));
            Check(relocated.Preferences==frozen,"same source and range restore despite regenerated playback-cache facts and bytes");
            string copied=Path.Combine(directory,"copied.ppclip");
            var copiedBinding=ReplayStudioPresentationPreferencesStore.CreateBinding(Enumerable.Repeat((byte)0xAA,32).ToArray(),10,90);
            Check(ReplayStudioPresentationPreferencesStore.StageRebound(copied,restored,copiedBinding)
                &&ReplayStudioPresentationPreferencesStore.ReadForCopy(copied,copiedBinding).Preferences==frozen,
                "contained Save As range retains frozen view without retiming camera pose");
            Reject(()=>ReplayStudioPresentationPreferencesStore.Freeze(settings with{Fov=float.NaN}),"non-finite FOV rejected");
            Reject(()=>ReplayStudioPresentationPreferencesStore.Freeze(settings with{PlayerSlot=8}),"unsupported subject rejected");
            Reject(()=>ReplayStudioPresentationPreferencesStore.Freeze(settings with{FreePose=null}),"free camera without pose rejected");
            Reject(()=>ReplayStudioPresentationPreferencesStore.Read(logical,"DIFFERENT-SOURCE"),"changed recording source cannot inherit preferences");
            using(var cancellation=new CancellationTokenSource())
            {
                cancellation.Cancel();
                Reject(()=>ReplayStudioPresentationPreferencesStore.ReadAsync(logical,source,cancellation.Token).GetAwaiter().GetResult(),"cancelled load cannot publish");
                Reject(()=>ReplayStudioPresentationPreferencesStore.SaveAsync(restored,settings,()=>{},cancellation.Token).GetAwaiter().GetResult(),"cancelled save cannot publish");
            }
            Reject(()=>ReplayStudioPresentationPreferencesStore.SaveAsync(restored,settings,()=>throw new InvalidOperationException("stale owner")).GetAwaiter().GetResult(),"retired owner cannot save");
            Check(ReplayStudioPresentationPreferencesStore.Read(logical,source).Reviewed.Revision==saved.Reviewed.Revision,"cancelled and stale-owner jobs preserve saved bytes");
            File.WriteAllText(logical+ReplayStudioPresentationPreferencesStore.Extension,"FOREIGN EDIT MUST SURVIVE");
            Reject(()=>ReplayStudioPresentationPreferencesStore.SaveAsync(restored,settings,()=>{}).GetAwaiter().GetResult(),"concurrent preference replacement cannot be overwritten");
            Check(File.ReadAllText(logical+ReplayStudioPresentationPreferencesStore.Extension)=="FOREIGN EDIT MUST SURVIVE","foreign bytes remain intact after rejected save");
            File.WriteAllBytes(logical+ReplayStudioPresentationPreferencesStore.Extension,new byte[65537]);
            Reject(()=>ReplayStudioPresentationPreferencesStore.Read(logical,source),"oversized preference sidecar is bounded before JSON decode");
        }
        finally { Directory.Delete(directory,true); }
    }
    private static void Reject(Action operation,string label)
    {
        try { operation(); }
        catch(Exception error) when(error is InvalidDataException or IOException or InvalidOperationException or ArgumentException or OperationCanceledException)
        { Console.WriteLine("STUDIO PRESENTATION PASS "+label);return; }
        throw new InvalidOperationException("Expected rejection: "+label);
    }
    private static void Check(bool value,string label)
    {if(!value)throw new InvalidOperationException(label);Console.WriteLine("STUDIO PRESENTATION PASS "+label);}
}
