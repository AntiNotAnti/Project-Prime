using System;
using System.Reflection;
using MphRead;
using MphRead.Sound;
using MphRead.Formats.Sound;

static class Program
{
    const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
    static void Main()
    {
        var backend = new SfxInstance();
        var instances = (SfxInstance.SoundInstance[])typeof(SfxInstance).GetField("_instances", Private)!.GetValue(backend)!;
        var channels = (SfxInstance.SoundChannel[])typeof(SfxInstance).GetField("_channels", Private)!.GetValue(backend)!;
        for (int i = 0; i < instances.Length; i++)
            instances[i] = new SfxInstance.SoundInstance { PlayTime = i + 1 };
        // A saturated instance pool must release old sample ownership and script/loop state.
        var sample = SoundSample.CreateNull(0);
        sample.References = 1;
        var victim = instances[^1];
        victim.Samples[0] = sample;
        victim.Loop[0] = true;
        victim.ScriptIndex = 7;
        object?[] args = { 0, null, false, -1f, false, false, null };
        typeof(SfxInstance).GetMethod("SetUpInstance", Private)!.Invoke(backend, args);
        Require(ReferenceEquals(args[^1], victim), "Oldest voice was not selected");
        Require(sample.References == 0 && !victim.Loop[0] && victim.ScriptIndex == -1,
            "Recycled instance leaked prior ownership");

        // Exhausted physical channels must roll back the provisional sample reference.
        for (int i = 0; i < channels.Length; i++)
            channels[i] = new SfxInstance.SoundChannel(0) { InUse = true };
        typeof(SfxInstance).GetField("_samples", Private)!.SetValue(backend, new[] { sample });
        for (int i = 0; i < 10000; i++)
        {
            int handle = backend.PlaySample(0, null, false, false, -1, false, false);
            Require(handle == -1 && sample.References == 0, "Failed allocation leaked ownership");
        }
        var buffers = (SfxInstance.SoundBuffer[])typeof(SfxInstance).GetField("_buffers", Private)!.GetValue(backend)!;
        var busySample = SoundSample.CreateNull(1);
        busySample.References = 1;
        for (int i = 0; i < buffers.Length; i++)
            buffers[i] = new SfxInstance.SoundBuffer(i + 1) { Sample = busySample };
        bool buffered = (bool)typeof(SfxInstance).GetMethod("BufferData", Private)!.Invoke(backend, new object[] { sample })!;
        Require(!buffered && busySample.References == 1, "Buffer saturation evicted live ownership");
        AudioMixer.Register(23, AudioBus.Weapons);
        Require(AudioMixer.Classify(23, AudioBus.Player) == AudioBus.Weapons, "Weapon routing");
        Require(AudioMixer.Classify(24, AudioBus.Player) == AudioBus.Player, "Source routing");
        AudioMixer.SetVolume(AudioBus.Player, 0);
        Require(AudioMixer.GetVolume(AudioBus.Weapons) == 1, "Bus isolation");
        AudioMixer.SetVolume(AudioBus.Player, float.NaN);
        Require(AudioMixer.GetVolume(AudioBus.Player) == 1, "Invalid gain");
        var defaults = new MenuSettings();
        Require(defaults.PlayerVolume == "1" && defaults.WeaponVolume == "1"
            && defaults.NotificationVolume == "1" && defaults.EffectsVolume == "1", "Legacy defaults");
        Console.WriteLine("PASS: pool recycling, 10,000 failed allocations, routing, gains, legacy defaults");
    }
}
