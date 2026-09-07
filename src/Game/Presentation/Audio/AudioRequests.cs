using System;

namespace MphRead.Sound
{
    public enum AudioRequestKind
    {
        UpdateSource, Play, PlayFree, PlayEnvironment, StopSource, StopSourceSound,
        StopFreeSound, StopFreeScripts, PauseFreeScripts, QueueStream, StopAll,
        PlayRoomMusic, PlayMusic, PlayMusicSequence, PauseMusic, ResumeMusic, ResetSoundMutes, ChangeForceFieldMute, UpdateMusicTempo
    }

    public readonly record struct AudioRequest(AudioRequestKind Kind, SoundSource? Source = null,
        int Id = 0, bool Loop = false, bool NoUpdate = false, float Recency = -1,
        bool SourceOnly = false, bool Cancellable = false, float AmountA = 0, float AmountB = 0,
        bool Force = false, float Delay = 0, float Expiration = 0, int Track = 0);

    // Game publishes presentation intent; absence of a listener requires no audio engine.
    public sealed class AudioRequests
    {
        public event Action<AudioRequest>? Requested;
        public void Emit(AudioRequest request) => Requested?.Invoke(request);
        public void QueueStream(VoiceId id, float delay = 0, float expiration = 0)
            => Emit(new(AudioRequestKind.QueueStream, Id: (int)id, Delay: delay, Expiration: expiration));
        public void PlayRoomMusic(int roomId, int track = 0) => Emit(new(AudioRequestKind.PlayRoomMusic, Id: roomId, Track: track));
        public void PlayMusic(MusicId id) => Emit(new(AudioRequestKind.PlayMusic, Id: (int)id));
        public void PlayMusicSequence(SeqId id) => Emit(new(AudioRequestKind.PlayMusicSequence, Id: (int)id));
        public void UpdateMusicTempo(ushort tempo, float seconds) => Emit(new(AudioRequestKind.UpdateMusicTempo, Id: tempo, Delay: seconds));
        public void PauseMusic() => Emit(new(AudioRequestKind.PauseMusic));
        public void ResumeMusic() => Emit(new(AudioRequestKind.ResumeMusic));
        public void ResetSoundMutes() => Emit(new(AudioRequestKind.ResetSoundMutes));
        public void ChangeForceFieldMute(bool increase) => Emit(new(AudioRequestKind.ChangeForceFieldMute, Force: increase));
        public void StopFreeSound(SfxId id) => Emit(new(AudioRequestKind.StopFreeSound, Id: (int)id));
        public void StopFreeScripts() => Emit(new(AudioRequestKind.StopFreeScripts));
        public void PlayScript(int id) => Emit(new(AudioRequestKind.Play, Id: id | 0x4000));
        public void StopAll(bool force = true) => Emit(new(AudioRequestKind.StopAll, Force: force));
    }
}
