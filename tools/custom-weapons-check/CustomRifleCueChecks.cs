using System.Buffers.Binary;
using System.Security.Cryptography;
using MphRead;
using MphRead.Mods.CustomWeapons;

// CPU signal/format acceptance only; no audio device, playback/mix, scene cue
// ownership, network dedupe or subjective sound-quality qualification.
internal static class CustomRifleCueChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var previous=Rng.Current;var random=new MatchRandom();
        random.SetRng1(0x12345678);random.SetRng2(0x87654321);Rng.Current=random;
        try
        {
            var before=(random.Rng1,random.Rng2);var hashes=new HashSet<string>();int count=0;
            void Verify(bool ok,string label){check(ok,"Original M7 cue: "+label);count++;}
            int[] frames={4410,2646,8820,1764},gains={22000,17000,18000,11000};
            // Independently calculated with Python integer arithmetic, explicit
            // truncation toward zero, struct.pack PCM and hashlib SHA256.
            string[] expectedHashes={"4acfe8178ac3a70a696c09dac47b74aac3540100146fc8e1b9a5848df90b67ac",
                "a5de5abd8be91e3f95e752b5b1eb824ae84b3a370c60465cf59c1985b986a375",
                "def1813ffd1ad7a952c21e51759ef11ddcb0701f6e48b211b85fba6427b4210c",
                "dfcbc29a5093b7d8b9500a9fc770ffa424fd4525c87c309a62eb97ded31afc43"};
            foreach(var cue in Enum.GetValues<CustomRifleCue>())
            {
                byte[] bytes=CustomRifleCues.BuildWave(cue),repeat=CustomRifleCues.BuildWave(cue);
                int index=(int)cue;
                Verify(bytes.Length==44+frames[index]*2&&bytes.Length<=44+22050,
                    cue+" exact authored duration and half-second byte budget");
                Verify(bytes.AsSpan(0,4).SequenceEqual("RIFF"u8)&&bytes.AsSpan(8,8).SequenceEqual("WAVEfmt "u8)
                    &&bytes.AsSpan(36,4).SequenceEqual("data"u8),cue+" independent RIFF/WAVE/fmt/data tags");
                Verify(BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4))==bytes.Length-8
                    &&BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(16))==16
                    &&BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(40))==frames[index]*2,
                    cue+" exact container and uncompressed data lengths");
                Verify(BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(20))==1
                    &&BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(22))==1
                    &&BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(24))==22050
                    &&BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(28))==44100
                    &&BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(32))==2
                    &&BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(34))==16,
                    cue+" PCM mono22050Hz16-bit layout decoded independently");
                int peak=0,nonzero=0,positive=0,negative=0;long sum=0;double energy=0;
                for(int i=0;i<frames[index];i++)
                {
                    short sample=BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(44+i*2));
                    peak=Math.Max(peak,Math.Abs((int)sample));if(sample!=0)nonzero++;
                    if(sample>0)positive++;if(sample<0)negative++;sum+=sample;
                    double normalized=sample/32768d;energy+=normalized*normalized;
                }
                double rms=Math.Sqrt(energy/frames[index]);
                Verify(peak>1000&&peak<=gains[index]&&nonzero>frames[index]/5&&positive>0&&negative>0
                    &&double.IsFinite(rms)&&rms is >.005 and <.5&&Math.Abs(sum/(32768d*frames[index]))<.08,
                    cue+" nonempty bipolar finite PCM has bounded peak/RMS/DC and no clipping");
                Verify(BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(44))==0
                    &&bytes.AsSpan(bytes.Length-400).IndexOfAnyExcept((byte)0)<0,
                    cue+" silence at attack boundary and final200samples avoids abrupt tail cut");
                string digest=Convert.ToHexStringLower(SHA256.HashData(bytes));
                Verify(digest==expectedHashes[index],cue+" bytes match independent original signal golden SHA256");
                Verify(bytes.AsSpan().SequenceEqual(repeat)&&digest==Convert.ToHexStringLower(SHA256.HashData(repeat))
                    &&hashes.Add(digest),cue+" deterministic SHA256 and signal distinct from every other cue");
                Array.Fill(bytes,(byte)0);
                Verify(CustomRifleCues.BuildWave(cue).AsSpan().SequenceEqual(repeat),
                    cue+" mutable returned buffer has no alias to another generation");
                Console.WriteLine($"M7 CUE {cue}:frames={frames[index]} sha256={digest} peak={peak} rms={rms:F6}");
            }
            for(int value=4;value<=255;value++)
            {
                bool rejected=false;
                try{CustomRifleCues.BuildWave((CustomRifleCue)value);}catch(ArgumentOutOfRangeException){rejected=true;}
                Verify(rejected,"undefined cue byte"+value+" refuses before creating a signal");
            }
            Verify(ReferenceEquals(Rng.Current,random)&&(random.Rng1,random.Rng2)==before,
                "all generations and refused values leave both foreground gameplay RNG streams untouched");
            Console.WriteLine($"Original M7 cue CPU checks passed:{count}; playback/scene integration is separate.");
        }
        finally{Rng.Current=previous;}
    }
}
