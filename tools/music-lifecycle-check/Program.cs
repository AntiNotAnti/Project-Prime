using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Diagnostics;
using SoundFlow.Abstracts;
using SoundFlow.Components;
using System.Threading;
using System.Threading.Tasks;
using MphRead;
using MphRead.Sound;
using SoundFlow.Enums;
using SoundFlow.Providers;
using SoundFlow.Structs;

static class Program
{
    static int _checks;
    static void Require(bool condition, string message)
    { _checks++; if (!condition) throw new Exception(message); }
    static void Await(Task task, string message)
    { Require(task.Wait(TimeSpan.FromSeconds(5)), message); }

    static void Main(string[] args)
    {
        CoalescingAndStaleFence();
        PublicationCancellation();
        PartialConstructionFailures();
        CallbackRetirement();
        PcmAndPlayStates();
        StaleSoundFlowSnapshot();
        LifecycleStress();
        ReadinessTimeout();
        CallbackAllocations();
        if (args.Length == 2 && args[0] == "--native") NativePlayback(args[1]);
        else if (args.Length != 0) throw new ArgumentException("Usage: music-lifecycle-check [--native <extracted AMHE1 filesystem>]");
        Console.WriteLine($"PASS: {_checks} music lifecycle checks; 10,000 latest requests; 120 lifecycle cycles; private construction/stale fencing; partial failures; callback retirement; PCM/play states; nonblocking removal");
    }

    // Explicit opt-in only: CI never opens an audio device or needs retail assets.
    static void NativePlayback(string assetRoot)
    {
        Paths.MphKey = "AMHE1"; Paths.SetPath(Paths.MphKey, assetRoot);
        Require(Directory.Exists(Path.Combine(Paths.FileSystem, "_seq")), "Native fixture needs extracted sequence assets");
        NcsfPcmParity();
        Require(MusicPlayer.Available, "Native MiniAudio playback device unavailable");
        var owner = (MusicLoadOwner)typeof(MusicPlayer).GetField("_loads", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        var device = MusicPlayer.PlaybackDevice!;
        using var cueProvider = new RawDataProvider(new float[128], 32728);
        using var cue = new SoundPlayer(MusicPlayer.Engine!, MusicPlayer.Format, cueProvider);
        device.MasterMixer.AddComponent(cue);
        try
        {
            for (int cycle = 0; cycle < 120; cycle++)
            {
                ushort tracks = (ushort)(1 << (cycle % 16));
                MusicPlayer.Load(cycle % 2 == 0 ? SeqId.GUMBO : SeqId.OREGANO, tracks, .5f);
                Await(owner.Completion, "Native load did not finish");
                var graph = owner.Current as MusicPlaybackGraph;
                Require(graph != null && !MusicPlayer.Loading, "Native graph did not publish");
                Require(device.MasterMixer.Components.Contains(graph!.Component), "Published graph missing from native mixer");
                for (int trackId = 0; trackId < 16; trackId++)
                {
                    var track = MusicPlayer.GetTrack(trackId);
                    if (track != null && (tracks & (1 << trackId)) == 0)
                        Require(track.Mute && track.Volume == 0, "Native initial track mask changed");
                }
                MusicPlayer.Play(.4f); Thread.Sleep(20);
                Require(MusicPlayer.State == PlaybackState.Playing, "Native playback did not start");
                MusicPlayer.Tempo = 300; MusicPlayer.Volume = .3f;
                Require(MusicPlayer.Tempo == 300 && MusicPlayer.Volume == .3f, "Native tempo/volume controls changed");
                MusicPlayer.Pause(); Require(MusicPlayer.State == PlaybackState.Paused, "Native pause changed");
                MusicPlayer.Play(.3f); Thread.Sleep(10);
                long started = Stopwatch.GetTimestamp(); MusicPlayer.Remove(shutdown: true);
                Require(Stopwatch.GetElapsedTime(started) < TimeSpan.FromMilliseconds(100), "Native scene Remove blocked");
                Await(owner.Completion, "Native callback retirement did not drain");
                Require(owner.Current == null && graph.Component.IsDisposed && !device.MasterMixer.Components.Contains(graph.Component),
                    "Native graph or mixer component retained after removal");
                Require(device.MasterMixer.Components.Contains(cue) && device.IsRunning,
                    "Music removal disrupted process-shared output/cue");
                using var process = Process.GetCurrentProcess();
                Console.WriteLine($"NATIVE cycle={cycle + 1} rss={process.WorkingSet64} managed={GC.GetTotalMemory(false)} components={device.MasterMixer.Components.Count}");
            }
            MusicPlayer.Load(SeqId.None); Await(owner.Completion, "Native SeqNone did not drain");
            Require(owner.Current == null && !MusicPlayer.Loading, "Native SeqNone constructed a graph");
        }
        finally
        {
            device.MasterMixer.RemoveComponent(cue);
            AudioLifetime.Shutdown();
        }
        Require(owner.Completion.IsCompletedSuccessfully && !MusicPlayer.Available, "Native final shutdown retained worker/engine");
        Console.WriteLine("PASS: native MiniAudio/NCSF 120 load/play/pause/control/remove cycles; shared output retained; final owner drained");
    }

    static void NcsfPcmParity()
    {
        var format = new AudioFormat { Channels = 2, SampleRate = 32728, Format = SampleFormat.F32 };
        foreach (ushort mask in new ushort[] { ushort.MaxValue, 0x5555, 0xAAAA, 1 })
        {
            NCSF123.NCSFPlayerStream CreateStream()
            {
                var stream = new NCSF123.NCSFPlayerStream(Path.Combine(Paths.FileSystem, "_seq", Metadata.SequenceFiles[(int)SeqId.GUMBO]),
                    32728, NCSFPlayer.Interpolation.None, 5, 115000, 5000, NCSF123.VolumeType.ReplayGainAlbum,
                    NCSF123.PeakType.ReplayGainTrack, true, .5f, 0, 0, false);
                for (int i = 0; i < 16; i++)
                    if ((mask & (1 << i)) == 0 && stream.Player.GetTrack(i) is { } track)
                    { track.Volume = 0; track.Mute = true; }
                return stream;
            }
            using var baselineStream = CreateStream(); using var candidateStream = CreateStream();
            using var baselineProvider = new RawDataProvider(baselineStream, SampleFormat.F32, 32728);
            using var candidateProvider = new RawDataProvider(candidateStream, SampleFormat.F32, 32728);
            using var baseline = new SoundPlayer(null!, format, baselineProvider);
            using var candidate = new MusicCallbackComponent(null!, format, candidateProvider);
            foreach (PlaybackState state in new[] { PlaybackState.Playing, PlaybackState.Paused, PlaybackState.Playing,
                PlaybackState.Stopped, PlaybackState.Playing })
            {
                if (state == PlaybackState.Playing) baseline.Play();
                else if (state == PlaybackState.Paused) baseline.Pause(); else baseline.Stop();
                candidate.SetState(state);
                baselineStream.VolumeModification = candidateStream.VolumeModification = .3f;
                baselineStream.Player.TempoRatio = candidateStream.Player.TempoRatio = 300;
                for (int block = 0; block < 8; block++)
                {
                    float[] before = new float[1024], after = new float[1024];
                    baseline.Process(before, 2); candidate.Process(after, 2);
                    Require(before.SequenceEqual(after) && baseline.State == candidate.State,
                        $"Actual NCSF PCM differed for mask={mask:X4}, state={state}");
                    Require(baselineProvider.Position == candidateProvider.Position, "Actual NCSF nonseekable position differed");
                    if (mask == ushort.MaxValue && state == PlaybackState.Playing && block == 0)
                        Require(before.Any(sample => sample != 0), "Actual NCSF parity fixture produced only silence");
                }
            }
            candidate.BeginRetirement(); candidate.ForgetProvider();
        }
        Console.WriteLine("PASS: actual NCSF PCM parity against SoundFlow SoundPlayer; four 16-track masks; gain/tempo/pause/stop/resume");
    }

    static void CoalescingAndStaleFence()
    {
        using var entered = new ManualResetEventSlim();
        using var unblock = new ManualResetEventSlim();
        var built = new List<Graph>();
        int active = 0, peak = 0;
        var owner = new MusicLoadOwner((request, current) =>
        {
            int value = Interlocked.Increment(ref active); peak = Math.Max(peak, value);
            try
            {
                var graph = new Graph(request);
                lock (built) built.Add(graph);
                if (built.Count == 1) { entered.Set(); unblock.Wait(); }
                return graph;
            }
            finally { Interlocked.Decrement(ref active); }
        }, ex => throw new Exception("Unexpected owner failure", ex));
        owner.Load(SeqId.WIN, 1, .3f);
        Require(entered.Wait(5000), "First construction did not enter");
        Task worker = owner.Completion;
        for (int i = 0; i < 10000; i++) owner.Load(SeqId.WIN, (ushort)i, .7f);
        Require(ReferenceEquals(worker, owner.Completion) && owner.Loading, "Requests created extra worker or false readiness");
        unblock.Set(); Await(owner.Completion, "Coalesced worker did not drain");
        Require(built.Count == 2 && peak == 1, "Decode work was unbounded or overlapped");
        Require(built[0].Retired == 1 && built[0].Attached == 0, "Stale private graph was published or retained");
        var newest = owner.Current as Graph;
        Require(newest?.Request.Tracks == 9999 && newest.Request.Volume == .7f && !owner.Loading,
            "Latest request arguments/readiness were not preserved");
        owner.Remove(shutdown: true); Await(owner.Completion, "Shutdown did not drain");
        owner.Load(SeqId.WIN, 0, 1); Require(owner.Current == null && !owner.Loading && built.Count == 2, "Shutdown admitted new work");
    }

    static void PublicationCancellation()
    {
        using var attaching = new ManualResetEventSlim();
        using var unblock = new ManualResetEventSlim();
        Graph? graph = null;
        var owner = new MusicLoadOwner((request, _) => graph = new Graph(request)
            { OnAttach = () => { attaching.Set(); unblock.Wait(); } }, ex => throw ex);
        owner.Load(SeqId.WIN, ushort.MaxValue, 1);
        Require(attaching.Wait(5000), "Attach did not enter");
        Task remove = Task.Run(() => owner.Remove());
        try { Await(remove, "Removal blocked behind attachment"); }
        finally { unblock.Set(); }
        Await(owner.Completion, "Cancelled attachment did not drain");
        Require(graph!.Retired == 1 && owner.Current == null && !owner.Loading, "Cancelled attachment published after Remove");
        owner.Load(SeqId.None, 0, 0);
        Require(owner.Current == null && !owner.Loading, "SeqNone created a track or reported loading");

        int errors = 0;
        Graph? failed = null;
        var attachmentFailure = new MusicLoadOwner((request, _) => failed = new Graph(request)
            { OnAttach = () => throw new InvalidOperationException("Injected device start failure") }, _ => errors++);
        attachmentFailure.Load(SeqId.WIN, 1, 1); Await(attachmentFailure.Completion, "Failed attachment did not drain");
        Require(errors == 1 && failed!.Retired == 1 && attachmentFailure.Current == null && !attachmentFailure.Loading,
            "Failed attachment retained its graph or readiness");
    }

    static void PartialConstructionFailures()
    {
        for (int stage = 0; stage < 3; stage++)
        {
            int alive = 0, errors = 0;
            int failAt = stage;
            var owner = new MusicLoadOwner((request, current) =>
            {
                using var construction = new MusicGraphConstruction();
                var stream = new Resource(() => alive++, () => alive--);
                construction.Stream = stream;
                if (failAt == 0) throw new InvalidOperationException("After stream acquisition");
                construction.Provider = new Resource(() => alive++, () => { alive--; stream.Dispose(); });
                if (failAt == 1) throw new InvalidOperationException("After provider acquisition");
                construction.Component = new Resource(() => alive++, () => alive--);
                throw new InvalidOperationException("After component acquisition");
            }, _ => errors++);
            owner.Load(SeqId.WIN, 0xAAAA, .5f); Await(owner.Completion, "Failed construction did not finish");
            Require(alive == 0 && errors == 1 && owner.Current == null && !owner.Loading, "Failure retained private resources or readiness");
        }
        using var entered = new ManualResetEventSlim(); using var unblock = new ManualResetEventSlim();
        int retained = 0;
        var cancelled = new MusicLoadOwner((_, current) =>
        {
            using var resource = new Resource(() => retained++, () => retained--);
            entered.Set(); unblock.Wait();
            if (!current()) throw new OperationCanceledException();
            throw new Exception("Cancellation was lost");
        }, ex => throw ex);
        cancelled.Load(SeqId.WIN, 1, 1); Require(entered.Wait(5000), "Cancellation fixture did not enter");
        cancelled.CancelLoading(true); Require(!cancelled.Loading && cancelled.StopLoading, "Cancellation did not update newest readiness");
        unblock.Set(); Await(cancelled.Completion, "Cancelled worker did not finish");
        Require(retained == 0 && cancelled.Current == null, "Cancelled private resources leaked");

        int cleanupAlive = 0;
        var cleanup = new MusicGraphConstruction
        {
            Stream = new Resource(() => cleanupAlive++, () => { cleanupAlive--; throw new IOException("stream dispose"); }),
            Provider = new Resource(() => cleanupAlive++, () => { cleanupAlive--; throw new IOException("provider dispose"); }),
            Component = new Resource(() => cleanupAlive++, () => cleanupAlive--)
        };
        try { cleanup.Dispose(); throw new Exception("Cleanup failure was hidden"); }
        catch (AggregateException ex)
        { Require(ex.InnerExceptions.Count == 2 && cleanupAlive == 0, "Cleanup failure skipped another owned resource"); }
    }

    static void CallbackRetirement()
    {
        using var entered = new ManualResetEventSlim(); using var unblock = new ManualResetEventSlim();
        var lifetime = new MusicCallbackLifetime();
        Task callback = Task.Run(() =>
        {
            Require(lifetime.TryEnter(), "Initial callback rejected"); entered.Set(); unblock.Wait(); lifetime.Exit();
        });
        Require(entered.Wait(5000), "Callback did not enter"); lifetime.BeginRetirement();
        Require(!lifetime.TryEnter() && !lifetime.IsIdle, "Retirement admitted another callback or missed active reader");
        unblock.Set(); Await(callback, "Callback did not finish");
        Require(lifetime.IsIdle && !lifetime.TryEnter(), "Retired callback gate became reusable");

        Graph? graph = null;
        using var release = new ManualResetEventSlim();
        var owner = new MusicLoadOwner((request, _) => graph = new Graph(request)
            { OnRetire = () => release.Wait() }, ex => throw ex);
        owner.Load(SeqId.WIN, 1, 1); Await(owner.Completion, "Graph did not publish");
        Task remove = Task.Run(() => owner.Remove());
        try { Await(remove, "Scene removal waited for callback retirement"); }
        finally { release.Set(); }
        Await(owner.Completion, "Retirement did not resume");
        Require(owner.Current == null && graph!.Retired == 1, "Retired graph remained published");
    }

    static void PcmAndPlayStates()
    {
        var format = new AudioFormat { Channels = 2, SampleRate = 32728, Format = SampleFormat.F32 };
        float[] samples = Enumerable.Range(0, 4096).Select(i => (i - 2048) / 4096f).ToArray();
        byte[] pcm = MemoryMarshal.AsBytes(samples.AsSpan()).ToArray();
        foreach (int maximumRead in new[] { 16, 64, 2048 })
        {
            using var beforeProvider = new RawDataProvider(new PcmStream(pcm, maximumRead), SampleFormat.F32, 32728);
            using var afterProvider = new RawDataProvider(new PcmStream(pcm, maximumRead), SampleFormat.F32, 32728);
            // These default non-solo component constructors/process paths do not
            // use the engine. No native playback device or engine is opened.
            using var before = new SoundPlayer(null!, format, beforeProvider);
            var after = new MusicCallbackComponent(null!, format, afterProvider);
            foreach (PlaybackState state in new[] { PlaybackState.Stopped, PlaybackState.Playing,
                PlaybackState.Paused, PlaybackState.Playing, PlaybackState.Stopped, PlaybackState.Playing })
            {
                if (state == PlaybackState.Playing) before.Play();
                else if (state == PlaybackState.Paused) before.Pause();
                else before.Stop();
                after.SetState(state);
                float[] baseline = new float[128], candidate = new float[128];
                before.Process(baseline, 2); after.Process(candidate, 2);
                Require(candidate.SequenceEqual(baseline) && after.State == before.State,
                    $"PCM/state parity failed for {state}, partial read={maximumRead}");
                Require(beforeProvider.Position == afterProvider.Position,
                    "Pause/stop/resume changed the nonseekable stream position");
            }
            before.Volume = after.Volume = .4f;
            before.Pan = after.Pan = .2f;
            // Finish the live (unknown length) stream: transient/end zero reads
            // stay playing and emit silence, matching SoundPlayer's live path.
            for (int i = 0; i < 40; i++)
            {
                float[] baseline = new float[128], candidate = new float[128];
                before.Process(baseline, 2); after.Process(candidate, 2);
                Require(candidate.SequenceEqual(baseline) && after.State == before.State, "End/live-stream PCM parity failed");
            }
            after.BeginRetirement(); Require(after.IsIdle, "Idle PCM graph had a reader");
            after.SetState(PlaybackState.Playing);
            float[] late = new float[128]; after.Process(late, 2);
            Require(late.All(v => v == 0), "Late play resurrected a retired callback");
            after.ForgetProvider(); after.Dispose();
        }
        using var finiteBeforeProvider = new RawDataProvider(samples.Take(200).ToArray(), 32728);
        using var finiteAfterProvider = new RawDataProvider(samples.Take(200).ToArray(), 32728);
        using var finiteBefore = new SoundPlayer(null!, format, finiteBeforeProvider);
        var finiteAfter = new MusicCallbackComponent(null!, format, finiteAfterProvider);
        finiteBefore.Play(); finiteAfter.SetState(PlaybackState.Playing);
        for (int i = 0; i < 4; i++)
        {
            float[] baseline = new float[128], candidate = new float[128];
            finiteBefore.Process(baseline, 2); finiteAfter.Process(candidate, 2);
            Require(candidate.SequenceEqual(baseline) && finiteAfter.State == finiteBefore.State,
                "Finite partial/EOS path differed from SoundPlayer");
        }
        finiteAfter.BeginRetirement(); finiteAfter.ForgetProvider(); finiteAfter.Dispose();

        using var zeroBefore = new RawDataProvider(new PcmStream(pcm, 64) { ZeroAtRead = 2 }, SampleFormat.F32, 32728);
        using var zeroAfter = new RawDataProvider(new PcmStream(pcm, 64) { ZeroAtRead = 2 }, SampleFormat.F32, 32728);
        using var liveBefore = new SoundPlayer(null!, format, zeroBefore);
        var liveAfter = new MusicCallbackComponent(null!, format, zeroAfter);
        liveBefore.Play(); liveAfter.SetState(PlaybackState.Playing);
        for (int i = 0; i < 4; i++)
        {
            float[] baseline = new float[128], candidate = new float[128];
            liveBefore.Process(baseline, 2); liveAfter.Process(candidate, 2);
            Require(candidate.SequenceEqual(baseline) && liveAfter.State == liveBefore.State,
                "Transient zero-read live path changed readiness or PCM");
        }
        liveAfter.BeginRetirement(); liveAfter.ForgetProvider(); liveAfter.Dispose();
    }

    static void StaleSoundFlowSnapshot()
    {
        var format = new AudioFormat { Channels = 2, SampleRate = 32728, Format = SampleFormat.F32 };
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        using var provider = new RawDataProvider(new PcmStream(new byte[1024], 64), SampleFormat.F32, 32728);
        var component = new MusicCallbackComponent(null!, format, provider);
        var blocked = new BlockingInput(format, entered, release);
        component.ConnectInput(blocked);
        component.SetState(PlaybackState.Playing);
        var output = new float[64];
        Task callback = Task.Run(() => component.Process(output, 2));
        Require(entered.Wait(5000), "Base Process did not enter before GenerateAudio");
        // Process already passed IsDisposed/Enabled and captured its input
        // snapshot; no provider read has entered the lease yet.
        component.BeginRetirement(); Require(component.IsIdle, "Pre-Generate snapshot had a provider reader");
        provider.Dispose(); component.ForgetProvider(); component.Dispose();
        release.Set(); Await(callback, "Stale base Process did not finish safely");
        Require(output.All(v => v == 0), "Stale base snapshot emitted retired samples");
        blocked.Dispose();

        using var readEntered = new ManualResetEventSlim(); using var readRelease = new ManualResetEventSlim();
        using var activeProvider = new RawDataProvider(new PcmStream(new byte[1024], 64,
            readEntered, readRelease), SampleFormat.F32, 32728);
        var active = new MusicCallbackComponent(null!, format, activeProvider);
        active.SetState(PlaybackState.Playing);
        Task read = Task.Run(() => active.Process(new float[64], 2));
        Require(readEntered.Wait(5000), "Provider read did not enter"); active.BeginRetirement();
        Require(!active.IsIdle, "Retirement missed an in-flight provider read");
        readRelease.Set(); Await(read, "Provider callback did not finish");
        Require(active.IsIdle, "Reader was not released after callback");
        activeProvider.Dispose(); active.ForgetProvider(); active.Dispose();
    }

    static void LifecycleStress()
    {
        int created = 0, retired = 0;
        var owner = new MusicLoadOwner((request, _) =>
        { created++; return new Graph(request) { OnRetire = () => retired++ }; }, ex => throw ex);
        for (int i = 0; i < 120; i++)
        {
            owner.Load(SeqId.WIN, (ushort)(1 << (i % 16)), .8f); Await(owner.Completion, "Stress load did not finish");
            Require(owner.Current != null && !owner.Loading, "Stress readiness failed");
            owner.Remove(); owner.Remove(); Await(owner.Completion, "Stress retirement did not finish");
            Require(owner.Current == null && created == retired, "Stress retained a graph or disposed it twice");
        }
    }

    static void ReadinessTimeout()
    {
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var owner = new MusicLoadOwner((request, current) =>
        { entered.Set(); release.Wait(); if (!current()) throw new OperationCanceledException(); return new Graph(request); }, ex => throw ex);
        owner.Load(SeqId.WIN, 1, 1); Require(entered.Wait(5000), "Readiness fixture did not enter");
        Require(!owner.WaitForLoad(TimeSpan.FromMilliseconds(50), 10), "Uncancellable decode ignored readiness deadline");
        owner.CancelLoading(true); owner.Remove(shutdown: true);
        Require(!owner.Completion.IsCompleted && !owner.Loading && owner.Current == null,
            "Shutdown hid the active decoder or exposed stale readiness");
        release.Set(); Await(owner.Completion, "Timed out decoder did not finish safely");
        Require(owner.Current == null, "Timed out decode was published later");
    }

    static void CallbackAllocations()
    {
        var format = new AudioFormat { Channels = 2, SampleRate = 32728, Format = SampleFormat.F32 };
        using var baselineProvider = new RawDataProvider(new PcmStream(new byte[1024 * 1024], 2048), SampleFormat.F32, 32728);
        using var candidateProvider = new RawDataProvider(new PcmStream(new byte[1024 * 1024], 2048), SampleFormat.F32, 32728);
        using var baseline = new SoundPlayer(null!, format, baselineProvider);
        var candidate = new MusicCallbackComponent(null!, format, candidateProvider);
        baseline.Play(); candidate.SetState(PlaybackState.Playing);
        float[] output = new float[128];
        for (int i = 0; i < 32; i++) { Array.Clear(output); baseline.Process(output, 2); Array.Clear(output); candidate.Process(output, 2); }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) { Array.Clear(output); baseline.Process(output, 2); }
        long baselineBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) { Array.Clear(output); candidate.Process(output, 2); }
        long candidateBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Require(candidateBytes == 0 && baselineBytes == 0,
            $"Callback allocations changed: baseline={baselineBytes}, candidate={candidateBytes}");
        candidate.BeginRetirement(); candidate.ForgetProvider(); candidate.Dispose();
    }

    sealed class BlockingInput : SoundComponent
    {
        readonly ManualResetEventSlim _entered, _release;
        internal BlockingInput(AudioFormat format, ManualResetEventSlim entered, ManualResetEventSlim release)
            : base(null!, format) { _entered = entered; _release = release; }
        protected override void GenerateAudio(Span<float> output, int channels)
        { _entered.Set(); _release.Wait(); output.Clear(); }
    }
    sealed class PcmStream : Stream
    {
        readonly byte[] _data;
        readonly int _maxRead;
        readonly ManualResetEventSlim? _entered, _release;
        int _position, _reads;
        bool _disposed;
        internal int ZeroAtRead;
        internal PcmStream(byte[] data, int maxRead, ManualResetEventSlim? entered = null, ManualResetEventSlim? release = null)
        { _data = data; _maxRead = maxRead; _entered = entered; _release = release; }
        public override int Read(Span<byte> buffer)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (++_reads == ZeroAtRead) return 0;
            _entered?.Set(); _release?.Wait();
            ObjectDisposedException.ThrowIf(_disposed, this);
            int count = Math.Min(Math.Min(buffer.Length, _maxRead), _data.Length - _position);
            _data.AsSpan(_position, count).CopyTo(buffer); _position += count; return count;
        }
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => _position; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { _disposed = true; base.Dispose(disposing); }
    }

    sealed class Graph : IMusicLoadGraph
    {
        internal readonly MusicLoadRequest Request;
        internal int Attached, Retired;
        internal Action? OnAttach, OnRetire;
        internal Graph(MusicLoadRequest request) { Request = request; }
        public void Attach() { Attached++; OnAttach?.Invoke(); }
        public void BeginRetirement() { }
        public void Retire() { OnRetire?.Invoke(); Retired++; }
    }
    sealed class Resource : IDisposable
    {
        readonly Action _release;
        bool _disposed;
        internal Resource(Action acquire, Action release) { acquire(); _release = release; }
        public void Dispose() { if (!_disposed) { _disposed = true; _release(); } }
    }
}
