using MphRead;
using MphRead.Formats.Sound;
using MphRead.Sound;
using OpenTK.Audio.OpenAL;
using OpenTK.Mathematics;

// Delegate every operation to the real loaded backend. This is instrumentation,
// not an audio replacement: no synthetic handle, channel, sample or cue result.
internal sealed class TracedAudio(SfxInstance inner, List<object> report) : SfxInstanceBase
{
    private readonly List<(string Operation, int Id)> _calls = new();
    private void Note(string operation, int id, bool? loop = null)
    {
        if (_calls.Count >= 4096) throw new InvalidOperationException("Audio evidence overflow; no silent truncation.");
        _calls.Add((operation, id));
        report.Add(new { sequence = _calls.Count, operation, id, loop });
    }
    internal int FreeCalls(int id) => _calls.Count(c => c.Operation == "free" && c.Id == id);
    internal object NativeSources()
    {
        var instances = (SfxInstance.SoundInstance[])Reflection.Get(inner, "_instances")!;
        var rows = new List<object>();
        foreach (var instance in instances.Where(i => i.SfxId >= 0))
        {
            var channels = new List<object>();
            foreach (var channel in instance.Channels.Where(c => c != null))
            {
                int id = channel.Id;
                AL.GetSource(id, ALGetSourcei.SourceState, out int value);
                var state = (ALSourceState)value;
                channels.Add(new { id, state = state.ToString() });
            }
            rows.Add(new { instance.SfxId, instance.Handle, instance.Count, instance.Paused, channels });
        }
        if (AL.GetError() != ALError.NoError)
            throw new InvalidOperationException("Real OpenAL source-state readback failed.");
        return new { backend = inner.GetType().FullName, activeInstances = rows.Count, instances = rows };
    }
    internal bool NativeHandlePlaying(int handle)
    {
        var instances = (SfxInstance.SoundInstance[])Reflection.Get(inner, "_instances")!;
        foreach (var instance in instances.Where(i => i.Handle == handle && handle >= 0))
            foreach (var channel in instance.Channels.Where(c => c != null))
            {
                AL.GetSource(channel.Id, ALGetSourcei.SourceState, out int value);
                if ((ALSourceState)value == ALSourceState.Playing) return true;
            }
        return false;
    }
    public override IReadOnlyList<Sound3dEntry> RangeData => inner.RangeData;
    public override Vector3 GetListenerPosition() => inner.GetListenerPosition();
    public override Vector3 GetListenerUp() => inner.GetListenerUp();
    public override Vector3 GetListenerFacing() => inner.GetListenerFacing();
    public override void SetListenerScene(Scene? scene) => inner.SetListenerScene(scene);
    public override void QueueStream(int id, float delay, float expiration) => inner.QueueStream(id, delay, expiration);
    public override void PlayFreeStream(int id) => inner.PlayFreeStream(id);
    public override void StopSoundByHandle(int handle) { Note("stop-handle", handle); inner.StopSoundByHandle(handle); }
    public override void PlayDgn(int id, SoundSource? source, bool loop, bool noUpdate, float recency,
        bool cancellable, float amountA, float amountB)
    { Note("dgn", id, loop); inner.PlayDgn(id, source, loop, noUpdate, recency, cancellable, amountA, amountB); }
    public override void PlayScript(int id, SoundSource? source, bool noUpdate, float recency,
        bool sourceOnly, bool cancellable)
    { Note("script", id); inner.PlayScript(id, source, noUpdate, recency, sourceOnly, cancellable); }
    public override int PlaySample(int id, SoundSource? source, bool? loop, bool noUpdate,
        float recency, bool sourceOnly, bool cancellable)
    { Note("sample", id, loop); return inner.PlaySample(id, source, loop, noUpdate, recency, sourceOnly, cancellable); }
    public override int PlayFreeSfx(SfxId id)
    { Note("free", (int)id); return inner.PlayFreeSfx(id); }
    public override int PlayFreeSfx(int id)
    { Note("free", id); return inner.PlayFreeSfx(id); }
    public override void PlayEnvironmentSfx(int id, SoundSource source) => inner.PlayEnvironmentSfx(id, source);
    public override bool CheckEnvironmentSfx(int id) => inner.CheckEnvironmentSfx(id);
    public override void StopSoundFromSource(SoundSource source, bool force)
    { Note("stop-source", -1); inner.StopSoundFromSource(source, force); }
    public override void StopSoundFromSource(SoundSource source, int id)
    { Note("stop", id); inner.StopSoundFromSource(source, id); }
    public override void StopEnvironmentSfx() => inner.StopEnvironmentSfx();
    public override void StopAllSound(bool force = false) => inner.StopAllSound(force);
    public override void StopSoundById(int id) { Note("stop-id", id); inner.StopSoundById(id); }
    public override void StopFreeSfxScripts() => inner.StopFreeSfxScripts();
    public override void SetPausedFreeSfxScripts(bool paused) => inner.SetPausedFreeSfxScripts(paused);
    public override bool IsHandlePlaying(int handle) => inner.IsHandlePlaying(handle);
    public override int CountPlayingSfx(int id) => inner.CountPlayingSfx(id);
    public override int CountSourcePlayingSfx(int id, SoundSource source) => inner.CountSourcePlayingSfx(id, source);
    public override void Update(float time) => inner.Update(time);
    public override void Load(Scene scene) => inner.Load(scene);
    public override void ShutDown() => inner.ShutDown();
}
