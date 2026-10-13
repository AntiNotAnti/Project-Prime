using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using MphRead;
using MphRead.Sound;

// Diagnostic-only code. Reuse the scene's existing presentation seam during
// AddRoom, then restore the live host before initialization and simulation.
static class ResourceAttribution
{
    private const BindingFlags PrivateStatic = BindingFlags.NonPublic | BindingFlags.Static;
    internal static bool MusicStartupProbeClosed { get; private set; }
    internal static void DisableMusic()
    {
        // Access runs MusicPlayer's existing static device probe once. Closing
        // it prevents repeat Music.Init calls from opening/decoding audio. This
        // is explicitly disclosed, rather than claiming zero startup audio.
        if (MusicPlayer.Available)
            typeof(MusicPlayer).GetMethod("Shutdown", PrivateStatic)!.Invoke(null, null);
        MusicStartupProbeClosed = !MusicPlayer.Available;
        if (!MusicStartupProbeClosed) throw new InvalidOperationException("Music device did not close for isolation arm.");
    }
    internal static void AddRoomWithoutAudio(Scene scene, Action addRoom)
    {
        var property = typeof(Scene).GetProperty("Services")!;
        ISceneServices original = scene.Services;
        typeof(Sfx).GetProperty("Instance")!.SetValue(null, new SfxInstanceBase());
        property.SetValue(scene, new WithoutAudioServices(original));
        try { addRoom(); }
        finally { property.SetValue(scene, original); }
        if (Sfx.Instance.GetType() != typeof(SfxInstanceBase) || MusicPlayer.Available)
            throw new InvalidOperationException("Audio isolation did not preserve a silent stub and closed music device.");
    }
    private sealed class WithoutAudioServices(ISceneServices inner) : ISceneServices
    {
        public bool AllowsPresentationSideEffects => false;
        public bool IsReplica => inner.IsReplica;
        public bool MayEndOnScore => inner.MayEndOnScore;
        public bool ShouldLeaveAfterMatch => inner.ShouldLeaveAfterMatch;
        public bool SuppressDamage => inner.SuppressDamage;
        public MphRead.Mods.Network.IPlayerReplicationHost PlayerReplication => inner.PlayerReplication;
        public bool DisablePowerups => inner.DisablePowerups;
        public MphRead.Mods.Multiplayer.MatchWorldProfile? NetworkWorldProfile => inner.NetworkWorldProfile;
        public bool ReplicatesHealthSpawns => inner.ReplicatesHealthSpawns;
        public bool TryGetHealthSpawn(short id, out MphRead.Mods.Network.HealthSpawnState state) => inner.TryGetHealthSpawn(id, out state);
    }
    internal static object MemorySample()
    {
        using var process = Process.GetCurrentProcess(); process.Refresh();
        var info = GC.GetGCMemoryInfo(); int? threads = null, handles = null;
        try { threads = process.Threads.Count; } catch (PlatformNotSupportedException) { }
        try { handles = process.HandleCount; } catch (PlatformNotSupportedException) { }
        var generations = info.GenerationInfo.ToArray();
        return new { timestamp = Stopwatch.GetTimestamp(), utc = DateTimeOffset.UtcNow,
            workingSetBytes = process.WorkingSet64,
            privateBytes = process.PrivateMemorySize64 > 0 ? (long?)process.PrivateMemorySize64 : null,
            virtualBytes = process.VirtualMemorySize64 > 0 ? (long?)process.VirtualMemorySize64 : null,
            managedLiveBytes = GC.GetTotalMemory(false), gcCommittedBytes = info.TotalCommittedBytes,
            gcHeapSizeBytes = info.HeapSizeBytes, gcFragmentedBytes = info.FragmentedBytes, gcMemoryInfoCollectionIndex = info.Index,
            gcTotalPauseMs = GC.GetTotalPauseDuration().TotalMilliseconds,
            gcLatestPauseMs = info.PauseDurations.ToArray().Select(p => p.TotalMilliseconds).ToArray(),
            gcLatestGeneration = info.Generation, gcLatestConcurrent = info.Concurrent, gcLatestCompacted = info.Compacted,
            lohSizeBytes = generations.Length > 3 ? (long?)generations[3].SizeAfterBytes : null,
            lohFragmentedBytes = generations.Length > 3 ? (long?)generations[3].FragmentationAfterBytes : null,
            allocatedBytes = GC.GetTotalAllocatedBytes(false), collectionCounts = Enumerable.Range(0, 3).Select(GC.CollectionCount).ToArray(),
            processThreadCount = threads, processHandleCount = handles > 0 ? handles : null,
            threadPoolThreads = ThreadPool.ThreadCount, threadPoolPendingWorkItems = ThreadPool.PendingWorkItemCount,
            threadPoolCompletedWorkItems = ThreadPool.CompletedWorkItemCount };
    }
    internal static object AudioOwnedSample()
    {
        object? instance = Sfx.Instance;
        object? Field(string name) => instance?.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(instance);
        object? owner = Field("_resources");
        object? OwnerField(string name) => owner?.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(owner);
        int Count(string name) => Field(name) is Array array ? array.Cast<object?>().Count(v => v != null) : 0;
        int? OwnedCount(string name) => owner?.GetType().GetProperty(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)?.GetValue(owner) is Array array
            ? array.Cast<object?>().Count(v => v is int id && id != 0) : null;
        bool native = instance is SfxInstance;
        return new { backend = instance?.GetType().FullName, nativeSfx = native,
            device = (OwnerField("_device") ?? Field("_device"))?.ToString(), context = (OwnerField("_context") ?? Field("_context"))?.ToString(),
            ownershipShape = owner?.GetType().FullName ?? instance?.GetType().FullName,
            ownedBufferIds = OwnedCount("Buffers"), ownedSourceIds = OwnedCount("Sources"),
            bufferSlots = native ? Count("_buffers") : 0, sourceSlots = native ? Count("_channels") : 0,
            streamBuffer = Field("_streamBuffer"), streamSource = Field("_streamInstance"),
            musicDeviceAvailable = MusicPlayer.Available, musicLoading = MusicPlayer.Loading,
            meaning = "Owner fields/pool slots, not a driver enumeration. Completed shutdown tasks are observations of native close, not an independent OpenAL handle census." };
    }
    internal static Task ShutdownTask() => (Task)typeof(Sfx).GetProperty("ShutdownCompletion", PrivateStatic)!.GetValue(null)!;
    internal static void ValidateQuiescence(int samples, int intervalMs)
    {
        if (samples < 2 || samples > 120 || intervalMs < 10 || intervalMs > 5000)
            throw new ArgumentException("Quiescence requires 2..120 samples, interval 10..5000 ms.");
    }
    internal static void QuiescentSamples(List<object> samples, int count, int intervalMs, Func<object> audio)
    {
        long started = Stopwatch.GetTimestamp();
        for (int i = 0; i < count; i++)
        {
            Thread.Sleep(intervalMs);
            samples.Add(new { phase = "quiescent", sample = i, elapsedSeconds = Stopwatch.GetElapsedTime(started).TotalSeconds,
                memory = MemorySample(), audio = audio() });
        }
    }
    internal static void FullGcDrain()
    {
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
    }
    internal static object Manifest()
    {
        string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        string client = typeof(Scene).Assembly.Location, harness = typeof(ResourceAttribution).Assembly.Location;
        return new { processId=Environment.ProcessId, command = Environment.GetCommandLineArgs(), workingDirectory = Environment.CurrentDirectory,
            runtime = RuntimeInformation.FrameworkDescription, os = RuntimeInformation.OSDescription,
            architecture = RuntimeInformation.ProcessArchitecture.ToString(), cpuCount = Environment.ProcessorCount,
            stopwatchFrequency = Stopwatch.Frequency, clientPath = client, clientSha256 = Hash(client),
            harnessPath = harness, harnessSha256 = Hash(harness), assemblyMvid = typeof(Scene).Module.ModuleVersionId,
            musicStartupProbeClosed = MusicStartupProbeClosed,
            environment = Environment.GetEnvironmentVariables().Cast<DictionaryEntry>()
                .Where(e => e.Key.ToString()!.StartsWith("PROJECT_PRIME_GL_", StringComparison.Ordinal)
                    || e.Key.ToString() is "PROJECT_PRIME_USER_DATA" or "DOTNET_GCHeapHardLimit" or "DOTNET_gcServer")
                .ToDictionary(e => e.Key.ToString()!, e => e.Value?.ToString()),
            limitations = "RSS/private/virtual are process accounting, not native allocation attribution. VM categories/allocation stacks/GPU driver residency are not sampled. Native resource counters describe existing owner fields. GC and readbacks invalidate FPS interpretation. Replay-only and full Studio UI are NOT_RUN." };
    }
}
