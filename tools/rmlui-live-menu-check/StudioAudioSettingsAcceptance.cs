using System.Text.Json;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Replay;

internal static class StudioAudioSettingsAcceptance
{
    internal static void Run()
    {
        string directory=Path.Combine(Path.GetTempPath(),"prime-audio-settings-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string logical=Path.Combine(directory,"private.ppdemo"),path=logical+ReplayStudioAudioSettingsStore.Extension;
            string source="synthetic-facts:SOURCE-SHA:"+new string('A',64)+":PLAYBACK-SHA:"+new string('B',64)+":RANGE:0:0";
            var cue=new TheatreAudioCue("WeaponFired",Path.Combine(directory,"not-decoded-cue.wav"),2,7,.4f);
            File.WriteAllText(path,JsonSerializer.Serialize(new{Version=1,SourceRevision=source,Cues=new[]{cue}}));
            var imported=ReplayStudioAudioSettingsStore.Read(logical,source);
            Require(imported.Settings.Enabled&&imported.Settings.GameEvents&&imported.Settings.MusicGain==.5f
                &&imported.Settings.Cues!.Single()==cue,"version-one cue sidecar imports default preferences");
            File.WriteAllText(path,JsonSerializer.Serialize(new{Version=2,SourceRevision=source,Cues=new[]{cue},Settings=imported.Settings}));
            imported=ReplayStudioAudioSettingsStore.Read(logical,source);
            Require(imported.Settings.Cues!.Single()==cue,"version-two full preference sidecar remains readable");
            var mutable=new List<TheatreAudioCue>{cue};
            var desired=new TheatreStudioAudioSettings(false,false,true,.2f,.3f,.4f,.7f,Path.Combine(directory,"not-decoded-music.wav"),mutable);
            int ownerChecks=0;
            var saved=ReplayStudioAudioSettingsStore.Save(imported,source,desired,()=>ownerChecks++);
            mutable.Clear();
            var restored=ReplayStudioAudioSettingsStore.Read(logical,source);
            Require(!restored.Settings.Enabled&&!restored.Settings.GameEvents&&restored.Settings.CombatFeedback
                &&restored.Settings.GameGain==.2f&&restored.Settings.CombatGain==.3f
                &&restored.Settings.ReplayGain==.4f&&restored.Settings.MusicGain==.7f
                &&restored.Settings.MusicFile==desired.MusicFile&&restored.Settings.Cues!.Single()==cue
                &&ownerChecks>=2,"version-two settings roundtrip every preference with detached cue snapshot");
            using(var document=JsonDocument.Parse(File.ReadAllText(path)))
                Require(document.RootElement.GetProperty("Version").GetInt32()==3
                    &&document.RootElement.GetProperty("Cues").GetArrayLength()==1,"version-three retains top-level cue compatibility");
            string newCache=source.Replace("synthetic-facts","new-materialization-facts").Replace(new string('B',64),new string('C',64));
            Require(ReplayStudioAudioSettingsStore.Read(logical,newCache).Settings.Cues!.Single()==cue,
                "durable binding survives playback-cache path/facts and materialized-byte changes");
            Throws(()=>ReplayStudioAudioSettingsStore.Read(logical,source.Replace(new string('A',64),new string('D',64))),"different source hash rejected");
            Throws(()=>ReplayStudioAudioSettingsStore.Read(logical,source.Replace(":RANGE:0:0",":RANGE:1:10")),"different clip range rejected");
            Throws(()=>ReplayStudioAudioSettingsStore.Read(logical,"different-source"),"different source rejected");
            string rebound=Path.Combine(directory,"private-selection.ppclip");
            var targetBinding=ReplayStudioAudioSettingsStore.CreateBinding(Convert.FromHexString(new string('A',64)),10,50);
            Require(ReplayStudioAudioSettingsStore.StageRebound(rebound,restored,targetBinding,CancellationToken.None),"reviewed audio staged for new project");
            var reboundSettings=ReplayStudioAudioSettingsStore.ReadForCopy(rebound,targetBinding).Settings;
            Require(reboundSettings==restored.Settings||reboundSettings.Cues!.SequenceEqual(restored.Settings.Cues!)
                &&reboundSettings.MusicFile==restored.Settings.MusicFile&&reboundSettings.MusicGain==restored.Settings.MusicGain,
                "event bindings and bus/music preferences rebind without timestamp retiming");
            Throws(()=>ReplayStudioAudioSettingsStore.ReadForCopy(rebound,ReplayStudioAudioSettingsStore.CreateBinding(Convert.FromHexString(new string('A',64)),11,50)),"rebound audio rejects another range");
            string before=File.ReadAllText(path);
            Throws(()=>ReplayStudioAudioSettingsStore.Save(restored,source,desired,()=>throw new InvalidOperationException("retired-owner")),"retired owner rejected");
            Require(File.ReadAllText(path)==before,"retired owner cannot publish");
            File.AppendAllText(path," ");string external=File.ReadAllText(path);
            Throws(()=>ReplayStudioAudioSettingsStore.Save(saved,source,desired,()=>{}),"external revision conflict rejected");
            Require(File.ReadAllText(path)==external,"external metadata preserved on conflict");
            Throws(()=>ReplayStudioAudioSettingsStore.Freeze(desired with{MusicGain=float.NaN}),"nonfinite bus gain rejected");
            Throws(()=>ReplayStudioAudioSettingsStore.Freeze(desired with{MusicFile="relative.wav"}),"relative music rejected");
            Console.WriteLine("RML03 PASS persisted Studio audio: v1/v2 reads, v3 durable preferences/rebind, source identity, immutable snapshots and stale publication guards; no audio device or WAV decoding");
        }
        finally{Directory.Delete(directory,true);}
    }
    private static void Require(bool value,string message)
    {if(!value)throw new InvalidOperationException("Studio audio settings: "+message);}
    private static void Throws(Action action,string message)
    {try{action();}catch(Exception error)when(error is InvalidDataException or IOException or InvalidOperationException or ArgumentException){return;}throw new Exception("Expected rejection: "+message);}
}
