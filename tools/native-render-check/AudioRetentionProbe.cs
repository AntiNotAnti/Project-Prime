using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Tasks;
using MphRead;
using MphRead.Formats.Sound;
using MphRead.Mods.Input;
using MphRead.Sound;
using OpenTK.Mathematics;

// Opt-in diagnostic. Returning through a non-inlined workload boundary removes
// possible JIT roots from all SFX-load/sample locals before the final drain.
static class AudioRetentionProbe
{
    internal sealed record Observation(int Cycle, int Load, string Kind, int Item, long? Bytes, WeakReference Reference);
    private const BindingFlags InstanceFields = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;

    internal static int SelfTest()
    {
        var observations = new List<Observation>();
        var tasks = new List<Task>();
        CreateTestTargets(observations, tasks);
        string snapshotWhileTargetsRecentlyObserved = JsonSerializer.Serialize(Snapshot(observations));
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        bool noTargetsRetained = observations.All(o => !o.Reference.IsAlive);
        bool noInlining = (typeof(AudioRetentionProbe).GetMethod(nameof(RunWorkload), BindingFlags.Static | BindingFlags.NonPublic)!
            .GetMethodImplementationFlags() & MethodImplAttributes.NoInlining) != 0;
        bool bankFieldsVisible = typeof(SfxInstance).GetField("_soundData", InstanceFields)?.FieldType == typeof(SoundData)
            && typeof(SfxInstance).GetField("_samples", InstanceFields) != null;
        GC.KeepAlive(tasks);
        GC.KeepAlive(snapshotWhileTargetsRecentlyObserved);
        Console.WriteLine($"AUDIORETENTION {(noTargetsRetained && noInlining && bankFieldsVisible ? "PASS" : "FAIL")} weakOnly={noTargetsRetained} workloadNoInlining={noInlining} publicPrivateBankFields={bankFieldsVisible} observations={observations.Count}");
        return noTargetsRetained && noInlining && bankFieldsVisible ? 0 : 1;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void CreateTestTargets(List<Observation> observations, List<Task> tasks)
    {
        byte[] payload = new byte[1024 * 1024];
        object owner = new();
        observations.Add(new(0, 0, "stream-payload", 0, payload.LongLength, new WeakReference(payload)));
        observations.Add(new(0, 0, "resource-owner", -1, null, new WeakReference(owner)));
        Task task = Task.Run(() => { GC.KeepAlive(payload); GC.KeepAlive(owner); });
        task.GetAwaiter().GetResult();
        tasks.Add(task);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void RunWorkload(int cycles, int frames, bool serial, List<object> records,
        List<object> drain, List<Task> tasks, List<Observation> observations, Func<object> audio)
    {
        var scene = new Scene(new Vector2i(320, 180), SyntheticInput.CreateKeyboard(), SyntheticInput.CreateMouse(), _ => { }, () => { }, initializeRuntime: false);
        drain.Add(new { phase = "workload-start", memory = ResourceAttribution.MemorySample() });
        for (int cycle = 0; cycle < cycles; cycle++)
        {
            long started = Stopwatch.GetTimestamp();
            for (int load = 0; load < 2; load++)
            {
                Sfx.Load(scene);
                if (Sfx.Instance is not SfxInstance) throw new InvalidOperationException("Native audio unavailable; stub cannot pass audio lifecycle attribution.");
                for (int step = 0; step < frames; step++) Sfx.Update(1f / 60);
                Capture(cycle, load, observations);
                object owned = ResourceAttribution.AudioOwnedSample();
                Sfx.ShutDown();
                Task task = ResourceAttribution.ShutdownTask(); tasks.Add(task);
                if (serial && !task.Wait(TimeSpan.FromSeconds(15))) throw new TimeoutException("Audio shutdown exceeded 15 seconds.");
                GC.Collect();
                records.Add(new { cycle, candidate = load == 1, audioUpdateSteps = frames, audioOwnedBefore = owned,
                    elapsedMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds, memory = ResourceAttribution.MemorySample(), audio = audio() });
            }
            Console.WriteLine($"NATIVERENDER resource arm=audio cycle={cycle + 1}/{cycles}");
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Capture(int cycle, int load, List<Observation> observations)
    {
        object instance = Sfx.Instance;
        void Observe(string kind, object? value, int item = -1)
        {
            if (value != null) observations.Add(new(cycle, load, kind, item, value is byte[] bytes ? bytes.LongLength : null, new WeakReference(value)));
        }
        object? Field(string name) => instance.GetType().GetField(name, InstanceFields)?.GetValue(instance);
        Observe("sfx-instance", instance);
        Observe("resource-owner", Field("_resources"));
        foreach (string field in new[] { "_samples", "_rangeData", "_dgnFiles", "_sfxScripts", "_soundData" })
            Observe(field, Field(field));
        // Observe already-decoded payloads. Do not force an uncreated Lazy or
        // add decoding work to the measurement fixture.
        if (Field("_samples") is IReadOnlyList<SoundSample> samples)
        {
            for (int item = 0; item < samples.Count; item++)
            {
                SoundSample sample = samples[item];
                Observe("sound-sample", sample, item);
                Observe("sample-encoded-data", sample.Data, item);
                if (sample.WaveData.IsValueCreated) Observe("sample-payload", sample.WaveData.Value, item);
            }
        }
        if (Field("_soundData") is SoundData soundData)
        {
            for (int item = 0; item < soundData.Streams.Count; item++)
            {
                SoundStream stream = soundData.Streams[item];
                Observe("sound-stream", stream, item);
                for (int channel = 0; channel < stream.Channels.Count; channel++)
                    Observe("stream-channel", stream.Channels[channel], item * 4 + channel);
                if (stream.BufferData.IsValueCreated) Observe("stream-payload", stream.BufferData.Value, item);
            }
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static object Snapshot(List<Observation> observations) => new
    {
        observations = observations.Count,
        kinds = observations.GroupBy(o => o.Kind).Select(group => new
        {
            kind = group.Key, observed = group.Count(), alive = group.Count(o => o.Reference.IsAlive),
            observedPayloadBytes = group.Sum(o => o.Bytes ?? 0), alivePayloadBytes = group.Where(o => o.Reference.IsAlive).Sum(o => o.Bytes ?? 0),
            aliveLoads = group.Where(o => o.Reference.IsAlive).Select(o => new { cycle = o.Cycle, load = o.Load, item = o.Item, bytes = o.Bytes }).ToArray()
        }).ToArray(),
        meaning = "Weak-reference observations after workload helper return. Counts are per load, not deduplicated object identities; shared targets can appear more than once. PreparedAudio wrapper is not accessible after Sfx.Load and is not observed here. IsAlive is diagnostic, not proof of a retaining path."
    };
}
