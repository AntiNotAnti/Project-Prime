using MphRead.Mods.CustomWeapons;

// Device-free PCM voice acceptance. No mixer/device startup, scene ownership,
// audible quality, spatialization or callback-device teardown is qualified here.
internal static class CustomRifleAudioChecks
{
    private static float[][] Waves()=>new[]
    {
        new[]{.25f,.75f,-.5f,0f},new[]{-.75f,.25f,.5f,-.25f,0f},
        new[]{.125f,-.25f,.5f,-.75f,.25f,0f},new[]{1f,0f,-1f,0f}
    };
    private static bool Silence(float[] output)=>output.All(value=>value==0);
    private static bool Near(float actual,float expected)=>float.IsFinite(actual)&&Math.Abs(actual-expected)<.000002f;
    private static float Expected(float[] wave,int rate,float gain,int frame)
    {
        double source=frame*22050d/rate;int index=(int)Math.Floor(source);
        if(index>=wave.Length)return 0;
        double fraction=source-index;int next=Math.Min(index+1,wave.Length-1);
        return (float)((wave[index]*(1-fraction)+wave[next]*fraction)*gain);
    }
    internal static void Run(Action<bool,string> check)
    {
        int count=0;void Verify(bool ok,string label){check(ok,"M7 PCM voice: "+label);count++;}
        var waves=Waves();
        Verify(waves.Length==4&&waves.All(w=>w.Length>0&&w.All(float.IsFinite)),
            "four independent tiny finite signal fixtures require no audio engine or original cue generator");
        foreach(int rate in new[]{22050,32728,48000})
            foreach(int channels in new[]{1,2})
                foreach(var cue in Enum.GetValues<CustomRifleCue>())
                {
                    var voice=new CustomRiflePcmVoice(waves,rate);
                    var idle=Enumerable.Repeat(float.NaN,channels*4+1).ToArray();voice.Render(idle,channels);
                    Verify(Silence(idle),$"idle clears every sample including partial trailing frame at{rate}/{channels}");
                    voice.Play(cue,.5f);
                    var first=new float[channels*2];voice.Render(first,channels);
                    bool firstExact=true;
                    for(int frame=0;frame<2;frame++)for(int channel=0;channel<channels;channel++)
                        firstExact&=Near(first[frame*channels+channel],Expected(waves[(int)cue],rate,.5f,frame));
                    Verify(firstExact,$"{cue} starts with independent linear interpolation at{rate}/{channels}");
                    var rest=Enumerable.Repeat(float.NaN,channels*32+channels-1).ToArray();voice.Render(rest,channels);
                    bool remainingExact=true;
                    for(int frame=0;frame<32;frame++)for(int channel=0;channel<channels;channel++)
                        remainingExact&=Near(rest[frame*channels+channel],Expected(waves[(int)cue],rate,.5f,frame+2));
                    for(int i=channels*32;i<rest.Length;i++)remainingExact&=rest[i]==0;
                    Verify(remainingExact,$"{cue} cursor spans callback blocks and clears completed/tail frames at{rate}/{channels}");
                    Array.Fill(rest,1);voice.Render(rest,channels);
                    Verify(Silence(rest),$"{cue} stays silent after natural end at{rate}/{channels}");
                }
        Commands(waves,Verify);Refusal(waves,Verify);Allocations(waves,Verify);Concurrent(waves,Verify);ConcurrentPairs(Verify);
        Console.WriteLine($"M7 device-free PCM voice checks passed:{count}; concurrency stress has no allocation claim.");
    }
    private static void Commands(float[][] waves,Action<bool,string> check)
    {
        var voice=new CustomRiflePcmVoice(waves,22050);var output=new float[3];
        voice.Play(CustomRifleCue.Shot,1);voice.Play(CustomRifleCue.Reload,.25f);voice.Render(output,1);
        check(output.SequenceEqual(new[]{.03125f,-.0625f,.125f}),"newest unrendered play command coalesces exact cue and gain");
        voice.Play(CustomRifleCue.Shot,.5f);voice.Render(output,1);
        check(output.SequenceEqual(new[]{.125f,.375f,-.25f}),"play restarts the selected cue at sample zero");
        voice.Play(CustomRifleCue.Shot,.5f);voice.Render(output,1);
        check(output.SequenceEqual(new[]{.125f,.375f,-.25f}),"repeating the same cue restarts instead of resuming old cursor");
        voice.Stop();Array.Fill(output,1);voice.Render(output,1);
        check(Silence(output),"stop command silences an active cursor at the next block");
        voice.Play(CustomRifleCue.Empty,1);voice.Stop();voice.Render(output,1);
        check(Silence(output),"newest Stop supersedes an unrendered Play");
        voice.Stop();voice.Play(CustomRifleCue.Empty,.5f);voice.Render(output,1);
        check(output.SequenceEqual(new[]{.5f,0f,-.5f}),"newest Play supersedes an unrendered Stop");
        voice.Play(CustomRifleCue.Shot,1);Array.Fill(output,float.NaN);voice.Render(output,0);
        check(Silence(output),"zero-channel callback clears output without consuming a queued cue");
        voice.Render(output,1);
        check(output.SequenceEqual(new[]{.25f,.75f,-.5f}),"valid callback subsequently consumes retained cue after zero channels");
        var first=new CustomRiflePcmVoice(waves,22050);var second=new CustomRiflePcmVoice(waves,22050);
        var a=new float[2];var b=new float[2];first.Play(CustomRifleCue.Shot,1);first.Render(a,1);second.Render(b,1);
        check(a.SequenceEqual(new[]{.25f,.75f})&&Silence(b),"one player's command cannot start another player's cursor");
        second.Play(CustomRifleCue.Bolt,1);second.Render(b,1);first.Render(a,1);
        check(a.SequenceEqual(new[]{-.5f,0f})&&b.SequenceEqual(new[]{-.75f,.25f}),"separate voices retain independent cue/gain/position");
        first.Stop();first.Render(a,1);second.Render(b,1);
        check(Silence(a)&&b.SequenceEqual(new[]{.5f,-.25f}),"one player's stop cannot interrupt another player");
    }
    private static void Refusal(float[][] waves,Action<bool,string> check)
    {
        foreach(float gain in new[]{float.NaN,float.PositiveInfinity,float.NegativeInfinity})
        {
            var voice=new CustomRiflePcmVoice(waves,22050);var output=new float[1];
            voice.Play(CustomRifleCue.Shot,1);voice.Render(output,1);voice.Play(CustomRifleCue.Empty,gain);voice.Render(output,1);
            check(output[0]==.75f,"nonfinite gain is refused without restarting or poisoning current cursor");
        }
        foreach(var cue in new[]{(CustomRifleCue)4,(CustomRifleCue)255})
        {
            var voice=new CustomRiflePcmVoice(waves,22050);var output=new float[1];
            voice.Play(CustomRifleCue.Shot,.5f);voice.Play(cue,1);voice.Render(output,1);
            check(output[0]==.125f,"undefined cue cannot overwrite a valid pending command");
        }
        foreach(var gain in new[]{-1f,0f,2f,float.MaxValue})
        {
            var voice=new CustomRiflePcmVoice(waves,22050);var output=new float[1];voice.Play(CustomRifleCue.Empty,gain);voice.Render(output,1);
            check(output[0]==Math.Clamp(gain,0,1),"finite gain clamps to normalized range without clipping");
        }
        foreach(int rate in new[]{0,7999,192001})
        {
            bool rejected=false;try{_ = new CustomRiflePcmVoice(waves,rate);}catch(ArgumentException){rejected=true;}
            check(rejected,"unsupported output rate refuses before a voice is usable");
        }
        foreach(int rate in new[]{8000,192000})
        {
            var voice=new CustomRiflePcmVoice(waves,rate);var output=new float[16];voice.Play(CustomRifleCue.Shot,1);voice.Render(output,2);
            check(output.All(float.IsFinite),"inclusive minimum/maximum output rates produce finite stereo PCM");
        }
        foreach(int count in new[]{3,5})
        {
            bool rejected=false;try{_ = new CustomRiflePcmVoice(new float[count][],22050);}catch(ArgumentException){rejected=true;}
            check(rejected,"cue table must contain exactly four signals");
        }
        for(int row=0;row<4;row++)
        {
            float[]?[] badRows={null,Array.Empty<float>(),new float[11026],new[]{float.NaN},new[]{float.PositiveInfinity},
                new[]{float.NegativeInfinity},new[]{1.00001f},new[]{-1.00001f},new[]{float.MaxValue}};
            foreach(var bad in badRows)
            {
                var malformed=Waves();malformed[row]=bad!;bool rejected=false;
                try{_ = new CustomRiflePcmVoice(malformed,22050);}catch(ArgumentException){rejected=true;}
                check(rejected,"every cue row refuses null/empty/overbudget/nonfinite/out-of-range PCM before callback use");
            }
            var minimum=Waves();minimum[row]=new[]{row%2==0?-1f:1f};
            var minVoice=new CustomRiflePcmVoice(minimum,22050);var output=new float[2];
            minVoice.Play((CustomRifleCue)row,1);minVoice.Render(output,1);
            check(output[0]==minimum[row][0]&&output[1]==0,
                "one-sample row with inclusive normalized amplitude boundary remains valid");
            var maximum=Waves();maximum[row]=new float[11025];
            var maxVoice=new CustomRiflePcmVoice(maximum,22050);maxVoice.Play((CustomRifleCue)row,1);maxVoice.Render(output,1);
            check(Silence(output),"exact half-second11025-sample row remains valid");
        }
    }
    private static void Allocations(float[][] waves,Action<bool,string> check)
    {
        var voice=new CustomRiflePcmVoice(waves,32728);var output=new float[32];
        for(int i=0;i<10000;i++){voice.Play((CustomRifleCue)(i&3),.5f);voice.Render(output,2);voice.Stop();}
        long before=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<100000;i++){voice.Play((CustomRifleCue)(i&3),.5f);voice.Render(output,2);voice.Stop();}
        long allocated=GC.GetAllocatedBytesForCurrentThread()-before;
        check(allocated==0&&output.All(float.IsFinite),"100k warmed Play/Render/Stop cycles allocate zero bytes on calling thread");
        Console.WriteLine($"M7 PCM allocation:100000 warmed Play/Render/Stop={allocated}B");
    }
    private static void Concurrent(float[][] waves,Action<bool,string> check)
    {
        var voice=new CustomRiflePcmVoice(waves,48000);var output=new float[64];
        using var start=new ManualResetEventSlim();Exception? commandError=null,renderError=null;int invalid=0;
        var commands=new Thread(()=>
        {
            try{start.Wait();for(int i=0;i<50000;i++){voice.Play((CustomRifleCue)(i&3),(i&255)/255f);if(i%7==0)voice.Stop();}}
            catch(Exception error){commandError=error;}
        });
        var audio=new Thread(()=>
        {
            try{start.Wait();for(int i=0;i<50000;i++){voice.Render(output,2);foreach(float value in output)
                if(!float.IsFinite(value)||Math.Abs(value)>1)Interlocked.Exchange(ref invalid,1);}}
            catch(Exception error){renderError=error;}
        });
        commands.IsBackground=audio.IsBackground=true;commands.Start();audio.Start();start.Set();
        bool commandsDone=commands.Join(10000),audioDone=audio.Join(10000);
        check(commandsDone&&audioDone&&commandError==null&&renderError==null&&invalid==0,
            "bounded owner-command/audio-render concurrency stress completes with finite bounded output and no exceptions");
        if(!commandsDone||!audioDone)return;
        voice.Stop();Array.Fill(output,1);voice.Render(output,2);
        check(Silence(output),"final owner Stop silences the voice after concurrent commands finish");
    }
    private static void ConcurrentPairs(Action<bool,string> check)
    {
        var waves=new[]{Enumerable.Repeat(1f,64).ToArray(),Enumerable.Repeat(.5f,64).ToArray(),
            Enumerable.Repeat(-.25f,64).ToArray(),Enumerable.Repeat(.125f,64).ToArray()};
        var voice=new CustomRiflePcmVoice(waves,22050);var output=new float[32];
        voice.Play(CustomRifleCue.Shot,.25f);voice.Render(output,2);
        bool first=output.All(value=>value==.25f);
        voice.Play(CustomRifleCue.Bolt,.75f);voice.Render(output,2);
        check(first&&output.All(value=>value==.375f),
            "constant cue/gain fixtures independently yield only.25/.375; crossed pairs would yield.125/.75");
        voice.Stop();
        using var start=new ManualResetEventSlim();using var posted=new ManualResetEventSlim();
        Exception? commandError=null,renderError=null;int crossed=0,nonzero=0;
        var commands=new Thread(()=>
        {
            try{start.Wait();voice.Play(CustomRifleCue.Shot,.25f);posted.Set();for(int i=0;i<100000;i++)
                {voice.Play(CustomRifleCue.Shot,.25f);voice.Play(CustomRifleCue.Bolt,.75f);}}
            catch(Exception error){commandError=error;}
        });
        var audio=new Thread(()=>
        {
            // Harness startup barrier only; Render itself never waits.
            try{start.Wait();if(!posted.Wait(10000))throw new TimeoutException("Command producer did not start.");for(int i=0;i<100000;i++)
            {
                voice.Render(output,2);
                foreach(float value in output)
                {
                    if(value!=0&&value!=.25f&&value!=.375f)Interlocked.Exchange(ref crossed,1);
                    if(value!=0)nonzero++;
                }
            }}catch(Exception error){renderError=error;}
        });
        commands.IsBackground=audio.IsBackground=true;commands.Start();audio.Start();start.Set();
        bool commandsDone=commands.Join(10000),audioDone=audio.Join(10000);
        check(commandsDone&&audioDone&&commandError==null&&renderError==null&&crossed==0&&nonzero>0,
            "100k alternating paired-command/concurrent callback blocks contain only silence or complete cue/gain pairs");
        Console.WriteLine("M7 paired-command concurrency: bounded device-free stress; no allocation or real-device claim.");
    }
}
