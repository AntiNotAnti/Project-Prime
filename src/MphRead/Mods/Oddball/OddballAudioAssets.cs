using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using SoundFlow.Backends.MiniAudio;
using SoundFlow.Enums;

namespace MphRead.Mods.Oddball;

/// <summary>Bounded data-only acquisition. Never opens a playback device or changes user settings.</summary>
internal static class OddballAudioAssets
{
    internal sealed record Pcm(byte[] Samples, int Rate);
    internal static readonly string[] CueNames = { "oddball", "play_ball", "ball_taken", "round_over", "game_over",
        "one_minute_remaining", "thirty_seconds_remaining", "ten_seconds_remaining", "gained_the_lead", "lost_the_lead" };
    private static readonly ConcurrentDictionary<string, Pcm> Decoded = new();
    private static readonly CancellationTokenSource Cancellation = new();
    private static int _started, _revision;
    internal static int Revision => Volatile.Read(ref _revision);
    internal static string State { get; private set; } = "not loaded";
    internal static string CacheDirectory => Path.Combine(Platform.AppPaths.UserDataDirectory, "audio", "oddball");
    internal static IEnumerable<KeyValuePair<string, Pcm>> Prepared => Decoded;

    internal static Pcm Decode(byte[] encoded)
    {
        if (encoded.Length < 44 || encoded.Length > 2_000_000) throw new InvalidDataException("Invalid announcer file size");
        using var input = new MemoryStream(encoded, false);
        // Decoder only: do not require MusicPlayer, WASAPI, or a second output device.
        using var decoder = new MiniAudioCodecFactory().TryCreateDecoder(input, out _, new SoundFlow.Structs.AudioFormat { Format=SampleFormat.F32, Channels=1, SampleRate=44100 }) ?? throw new InvalidDataException("Unsupported announcer encoding");
        using var pcm = new MemoryStream();
        float[] samples = new float[4096]; byte[] bytes = new byte[8192]; bool nonzero = false;
        int count;
        while ((count = decoder.Decode(samples)) > 0)
        {
            if (count > samples.Length || pcm.Length + count * 2 > 44100 * 2 * 15)
                throw new InvalidDataException("Announcer duration exceeds 15 seconds");
            for (int i = 0; i < count; i++)
            {
                if (!float.IsFinite(samples[i])) throw new InvalidDataException("Non-finite audio sample");
                short value = (short)Math.Clamp((int)MathF.Round(samples[i] * 32767f), -32768, 32767);
                nonzero |= value != 0; bytes[i*2] = (byte)value; bytes[i*2+1] = (byte)(value >> 8);
            }
            pcm.Write(bytes, 0, count * 2);
        }
        if (pcm.Length < 1000 || !nonzero) throw new InvalidDataException("Empty or silent announcer clip");
        return new Pcm(pcm.ToArray(), 44100);
    }
    internal static void LoadLocal()
    {
        foreach (var name in CueNames)
        {
            if (Decoded.ContainsKey(name)) continue;
            foreach (string folder in new[] {CacheDirectory, Path.Combine(AppContext.BaseDirectory, "audio", "oddball")})
            {
                foreach (string ext in new[]{".wav", ".mp3"})
                {
                    string path = Path.Combine(folder, name + ext);
                    if (!File.Exists(path)) continue;
                    try { if (Decoded.TryAdd(name, Decode(File.ReadAllBytes(path)))) Interlocked.Increment(ref _revision); break; }
                    catch (Exception e) { Console.WriteLine($"[oddball-audio] invalid {name}{ext}: {e.Message}"); }
                }
                if (Decoded.ContainsKey(name)) break;
            }
        }
        if (Decoded.Count == CueNames.Length) State = "ready";
    }
    internal static void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0) return;
        // Launch-time work is off the UI/simulation thread. The cached local path
        // is also read by Warm before the match if it finishes sooner than this job.
        _ = Task.Run(async () => {
            try
            {
                LoadLocal();
                if (Decoded.Count == CueNames.Length) return;
                if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("ODDBALL_AUDIO_NO_FETCH") == "1")
                { State = "clips missing"; return; }
                State = "downloading";
                Directory.CreateDirectory(CacheDirectory);
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
                http.DefaultRequestHeaders.UserAgent.ParseAdd("ProjectPrime-OddballPlaytest/1.2");
                using var limit = new SemaphoreSlim(3);
                var jobs = new List<Task>();
                foreach (var name in CueNames) if (!Decoded.ContainsKey(name)) jobs.Add(Fetch(name));
                await Task.WhenAll(jobs);
                State = Decoded.Count == CueNames.Length ? "ready" : "download incomplete";
                File.WriteAllText(Path.Combine(CacheDirectory, "audio-status.txt"),
                    $"Oddball 1.2: {Decoded.Count}/{CueNames.Length} decoded clips. {State}.\n"+
                    "No permanent failure marker is used. Missing/invalid clips retry on the next launch.\n"+
                    "Source: https://halo.bungie.org/misc/h2dialogue.html?cat=multiplayer\n");
                async Task Fetch(string name)
                {
                    await limit.WaitAsync(Cancellation.Token);
                    try
                    {
                        string remote = name == "gained_the_lead" ? "gainedthelead" : name;
                        for (int attempt=0; attempt<2; attempt++)
                        {
                            try
                            {
                                using var attemptTimeout = CancellationTokenSource.CreateLinkedTokenSource(Cancellation.Token);
                                attemptTimeout.CancelAfter(TimeSpan.FromSeconds(8));
                                var requestToken = attemptTimeout.Token;
                                using var response = await http.GetAsync("https://halo.bungie.org/misc/h2dialogue/multiplayer/"+remote+".mp3", HttpCompletionOption.ResponseHeadersRead, requestToken);
                                response.EnsureSuccessStatusCode();
                                if(response.Content.Headers.ContentLength is > 2_000_000) throw new InvalidDataException("Oversized download");
                                using var stream = await response.Content.ReadAsStreamAsync(requestToken);
                                using var data = new MemoryStream(); byte[] block = new byte[16384]; int read;
                                while((read=await stream.ReadAsync(block,requestToken))>0)
                                { if(data.Length+read>2_000_000)throw new InvalidDataException("Oversized download");data.Write(block,0,read); }
                                byte[] encoded=data.ToArray();var decoded=Decode(encoded);
                                string target=Path.Combine(CacheDirectory,name+".mp3"), temp=target+"."+Guid.NewGuid().ToString("N")+".tmp";
                                try { File.WriteAllBytes(temp,encoded); File.Move(temp,target,true); }
                                finally { if(File.Exists(temp))File.Delete(temp); }
                                if(Decoded.TryAdd(name,decoded))Interlocked.Increment(ref _revision);
                                Console.WriteLine($"[oddball-audio] downloaded and decoded {name}: {encoded.Length} bytes");return;
                            }
                            catch(Exception e) when(e is not OutOfMemoryException)
                            { Console.WriteLine($"[oddball-audio] download {name} attempt {attempt+1}: {e.Message}"); if(Cancellation.IsCancellationRequested)return; }
                        }
                    }
                    finally {limit.Release();}
                }
            }
            catch(Exception e) { State="clips unavailable";Console.WriteLine("[oddball-audio] "+e.Message); }
        });
    }
    internal static void Stop() => Cancellation.Cancel();
}
