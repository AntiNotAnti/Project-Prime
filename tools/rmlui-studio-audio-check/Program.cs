using MphRead.Mods.Replay;
int checks=0;void Check(bool pass,string label){if(!pass)throw new Exception(label);checks++;Console.WriteLine("PASS "+label);}
var root=Path.Combine(Path.GetTempPath(),"rml97-audio-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
try
{
 Check(StudioOfflineAudio.SampleCount(1,60)==800,"sample count uses exact reviewed video duration");
 var pcm=new StudioPcmAudio(48000,1,Enumerable.Repeat(.5f,48000).ToArray());
 string path=Path.Combine(root,"mix.wav");StudioOfflineAudio.WriteWave(path,0,60,60,new[]{new StudioAudioClip(0,StudioAudioBus.Game,pcm)},new(Game:.5f));
 var mixed=StudioPcmAudio.ReadWave(path);Check(mixed.Channels==2&&mixed.SampleRate==48000&&mixed.Frames==48000,"output format is real stereo PCM16 at reviewed duration");
 Check(Math.Abs(mixed.Samples[0]-.25f)<.0001,"bus gain changes actual rendered samples");
 StudioOfflineAudio.WriteWave(path,0,60,60,new[]{new StudioAudioClip(0,StudioAudioBus.Music,pcm,Pan:1)},new(Music:1));
 mixed=StudioPcmAudio.ReadWave(path);Check(mixed.Samples[0]==0&&mixed.Samples[1]>.49,"pan routes samples to the shared stereo layout");
 StudioOfflineAudio.WriteWave(path,0,120,60,new[]{new StudioAudioClip(0,StudioAudioBus.Replay,pcm,Loop:true)});
 mixed=StudioPcmAudio.ReadWave(path);Check(mixed.Frames==96000&&mixed.Samples[^1]>.49,"looping sources render past their original duration");
 using var cancellation=new CancellationTokenSource();cancellation.Cancel();
 try{StudioOfflineAudio.WriteWave(Path.Combine(root,"cancelled.wav"),0,60,60,new[]{new StudioAudioClip(0,StudioAudioBus.Game,pcm)},cancellation:cancellation.Token);throw new Exception("cancellation ignored");}catch(OperationCanceledException){checks++;}
 Check(!File.Exists(Path.Combine(root,"cancelled.wav"))&&!Directory.EnumerateFiles(root,"*.staging").Any(),"cancelled output leaves no published or staging WAV");
 try{StudioOfflineAudio.WriteWave(path,0,60,60,new[]{new StudioAudioClip(0,StudioAudioBus.Game,pcm,Gain:float.NaN)});throw new Exception("invalid gain accepted");}catch(InvalidDataException){checks++;}
 Console.WriteLine($"{checks} offline audio checks passed.");
}
finally{Directory.Delete(root,true);}
