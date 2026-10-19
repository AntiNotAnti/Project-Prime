using System.Text;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Network;
using MphRead.Mods.Replay;

/// <summary>Actual queued audio owner over tiny synthetic WAVs. No device, scene, ROM, process or encoder.</summary>
internal static class QueuedReplayAudioAcceptance
{
    internal static void Run()
    {
        string root=Path.Combine(Path.GetTempPath(),"prime-queued-audio-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        int checks=0;void Check(bool value,string label){if(!value)throw new InvalidOperationException("QUEUED AUDIO: "+label);checks++;Console.WriteLine("QUEUED AUDIO PASS "+label);}
        try
        {
            string positive=Path.Combine(root,"positive.wav"),negative=Path.Combine(root,"negative.wav");
            Wave(positive,1600,_=>.25f);Wave(negative,1600,_=>-.25f);
            var silence=ReplayExportAudioInput.Capture(new(GameEvents:false,CombatFeedback:false,MusicGain:0),Array.Empty<ReplayEvent>(),Array.Empty<ReplayPlayerInfo>());
            foreach(int fps in new[]{24,30,48,60,90,120,144})
            {
                var segments=new[]{Segment(10,12),Segment(1,3),Segment(10,12)};
                var job=Job("duration-"+fps,fps,segments,silence);
                long video=ReplayExportAudio.TotalVideoFrames(segments,fps),expected=(video*48000+fps-1)/fps;
                string output=ReplayExportAudio.Write(job,CancellationToken.None);var pcm=StudioPcmAudio.ReadWave(output);
                Check(pcm.SampleRate==48000&&pcm.Channels==2&&pcm.Frames==expected&&new FileInfo(output).Length==44+expected*4,
                    fps+" FPS PCM duration equals inclusive ordered video count without per-cut rounding drift");
                long prior=0,total=0;foreach(var segment in segments){long frames=new ReplayExportSampler(segment.StartFrame,segment.EndFrame,fps).Count;total+=ReplayExportAudio.SegmentSamples(prior,frames,fps);prior+=frames;}
                Check(total==expected&&pcm.Samples.All(x=>x==0),fps+" FPS cumulative allocation retains exact silent spans");
            }
            var cues=new List<TheatreAudioCue>{new("Kill",positive,2),new("Damage",negative,2)};
            var events=new List<ReplayEvent>{new(100,ReplayEventType.Kill),new(0,ReplayEventType.Damage)};
            var players=new List<ReplayPlayerInfo>();
            var captured=ReplayExportAudioInput.Capture(new(GameEvents:false,CombatFeedback:false,MusicGain:0,Cues:cues),events,players);
            cues.Clear();events.Clear();
            Check(captured.Settings.Cues?.Count==2&&captured.Events.Length==2,"accepted cues and events are immutable after editor collection mutation");
            var order=new[]{Segment(100,101),Segment(0,1),Segment(100,101)};
            var ordered=StudioPcmAudio.ReadWave(ReplayExportAudio.Write(Job("ordered",60,order,captured),CancellationToken.None));
            int segmentSamples=(int)StudioOfflineAudio.SampleCount(new ReplayExportSampler(100,101,60).Count,60);
            Check(ordered.Samples[0]>.24f&&ordered.Samples[segmentSamples*2]<-.24f&&ordered.Samples[segmentSamples*4]>.24f,
                "reverse and repeated reel ranges preserve recorded cue order without sorting or deduplication");
            var tails=ReplayExportAudioInput.Capture(new(GameEvents:false,CombatFeedback:false,MusicGain:0,Cues:new[]{new TheatreAudioCue("Kill",positive,2)}),new[]{new ReplayEvent(99,ReplayEventType.Kill)},Array.Empty<ReplayPlayerInfo>());
            var tail=StudioPcmAudio.ReadWave(ReplayExportAudio.Write(Job("tail",60,new[]{Segment(100,101)},tails),CancellationToken.None));
            Check(tail.Samples[0]>.24f&&tail.Samples[^1]==0,"a cue beginning before range-in retains its overlapping tail then silence");
            string music=Path.Combine(root,"music.wav");Wave(music,4800,i=>i<2400?.2f:-.2f);
            var musicInput=ReplayExportAudioInput.Capture(new(GameEvents:false,CombatFeedback:false,MusicGain:1,MusicFile:music),Array.Empty<ReplayEvent>(),Array.Empty<ReplayPlayerInfo>());
            var musicPcm=StudioPcmAudio.ReadWave(ReplayExportAudio.Write(Job("music",60,new[]{Segment(100,102),Segment(0,2)},musicInput),CancellationToken.None));
            Check(musicPcm.Samples[0]>.19f&&musicPcm.Samples[2400*2]<-.19f,"music phase continues across a backward reel cut instead of restarting at each source range");
            var changed=ReplayExportAudioInput.Capture(new(GameEvents:false,CombatFeedback:false,Cues:new[]{new TheatreAudioCue("Kill",positive)}),new[]{new ReplayEvent(0,ReplayEventType.Kill)},Array.Empty<ReplayPlayerInfo>());
            File.SetLastWriteTimeUtc(positive,File.GetLastWriteTimeUtc(positive).AddSeconds(3));
            var staleJob=Job("stale",60,new[]{Segment(0,1)},changed);Reject(()=>ReplayExportAudio.Write(staleJob,CancellationToken.None),"changed accepted WAV facts reject publication");
            Check(!File.Exists(Path.Combine(Path.GetDirectoryName(staleJob.FramePattern)!,"offline.wav")),"stale audio source produces no final output");
            using(var cancellation=new CancellationTokenSource())
            {
                cancellation.Cancel();var job=Job("cancel",60,new[]{Segment(0,1)},silence);
                Reject(()=>ReplayExportAudio.Write(job,cancellation.Token),"canceled queued mix rejects publication");
                Check(!File.Exists(Path.Combine(Path.GetDirectoryName(job.FramePattern)!,"offline.wav"))&&!Directory.EnumerateDirectories(Path.GetDirectoryName(job.FramePattern)!,".audio-work-*").Any(),"cancellation cleans bounded working files and leaves no final WAV");
            }
            var encode=Job("encoder",60,new[]{Segment(0,1)},silence) with{EncoderExecutable="/synthetic/path with spaces/ffmpeg",EncoderCodec="libx265"};
            var arguments=ReplayExportAudio.EncoderArguments(encode,"/synthetic/audio path/offline.wav").ToArray();
            int codec=Array.IndexOf(arguments,"-c:v"),audio=Array.IndexOf(arguments,"-c:a");
            Check(codec>=0&&arguments[codec+1]=="libx265"&&audio>=0&&arguments[audio+1]=="aac"
                &&arguments.Contains("/synthetic/audio path/offline.wav")&&!arguments.Contains(encode.EncoderExecutable)
                &&arguments[^1]==encode.SuggestedOutput,"encoder executable stays separate from bounded codec and unquoted argument tokens");
            var pngOnly=encode with{EncoderExecutable=""};
            Check(pngOnly.EncoderExecutable.Length==0&&File.Exists(ReplayExportAudio.Write(pngOnly,CancellationToken.None)),"blank encoder executable permits PNG-sequence-owned audio preparation without starting a process");
            Console.WriteLine("QUEUED AUDIO CPU PASS "+checks+" assertions.");
        }
        finally{Directory.Delete(root,true);}
        ReplayVideoExportManifest Job(string name,int fps,IReadOnlyList<ReplayVideoSegment> segments,ReplayExportAudioInput audio)
        {
            string directory=Path.Combine(root,name);Directory.CreateDirectory(directory);
            return new(3,"synthetic.ppdemo",segments[0].StartFrame,segments[0].EndFrame,640,360,fps,true,false,false,
                Path.Combine(directory,"frame_%08d.png"),Path.Combine(directory,"movie with spaces.mp4"),"",segments,Audio:audio);
        }
        void Reject(Action operation,string label){bool rejected=false;try{operation();}catch(Exception error)when(error is IOException or ArgumentException or InvalidOperationException or OperationCanceledException){rejected=true;}Check(rejected,label);}
    }
    private static ReplayVideoSegment Segment(uint start,uint end)=>new(start,end,"Synthetic",ReplaySegmentCamera.Current);
    private static void Wave(string path,int frames,Func<int,float> sample)
    {
        using var stream=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None);using var writer=new BinaryWriter(stream,Encoding.ASCII);
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));writer.Write(36+frames*2);writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));writer.Write(16);writer.Write((ushort)1);writer.Write((ushort)1);writer.Write(48000);writer.Write(96000);writer.Write((ushort)2);writer.Write((ushort)16);writer.Write(Encoding.ASCII.GetBytes("data"));writer.Write(frames*2);
        for(int i=0;i<frames;i++)writer.Write((short)Math.Round(sample(i)*32767));
    }
}
