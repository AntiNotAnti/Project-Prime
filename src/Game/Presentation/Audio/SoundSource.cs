using System;
using OpenTK.Mathematics;

namespace MphRead.Sound
{
    public sealed class SoundSource
    {
        private readonly AudioRequests _requests;
        public SoundSource(Scene scene) : this(scene.Audio) { }
        public SoundSource(AudioRequests requests) { _requests = requests; }
        public Vector3 Position { get; set; }
        public float ReferenceDistance { get; set; } = 1;
        public float MaxDistance { get; set; } = Single.MaxValue;
        public float RolloffFactor { get; set; } = 1;
        public float Volume { get; set; } = 1;
        public bool Self { get; set; }

        public void Update(Vector3 position, int rangeIndex)
        {
            Position = position;
            Self = rangeIndex == -1;
            _requests.Emit(new(AudioRequestKind.UpdateSource, this, rangeIndex));
        }
        public void PlaySfx(SfxId id, bool loop = false, bool noUpdate = false,
            float recency = -1, bool sourceOnly = false, bool cancellable = false, float amountA = 0, float amountB = 0)
            => PlaySfx((int)id, loop, noUpdate, recency, sourceOnly, cancellable, amountA, amountB);
        public void PlaySfx(int id, bool loop = false, bool noUpdate = false,
            float recency = -1, bool sourceOnly = false, bool cancellable = false, float amountA = 0, float amountB = 0)
            => _requests.Emit(new(AudioRequestKind.Play, this, id, loop, noUpdate, recency,
                sourceOnly, cancellable, amountA, amountB));
        public void PlayFreeSfx(SfxId id) => PlayFreeSfx((int)id);
        public void PlayFreeSfx(int id) => _requests.Emit(new(AudioRequestKind.PlayFree, Id: id));
        public void PlayEnvironmentSfx(int id) => _requests.Emit(new(AudioRequestKind.PlayEnvironment, this, id));
        public void StopAllSfx(bool force = false) => _requests.Emit(new(AudioRequestKind.StopSource, this, Force: force));
        public void StopSfx(SfxId id) => StopSfx((int)id);
        public void StopSfx(int id) => _requests.Emit(new(AudioRequestKind.StopSourceSound, this, id));
        public void StopFreeSfx(SfxId id) => StopFreeSfx((int)id);
        public void StopFreeSfx(int id) => _requests.Emit(new(AudioRequestKind.StopFreeSound, Id: id));
        public void StopFreeSfxScripts() => _requests.Emit(new(AudioRequestKind.StopFreeScripts));
        public void SetPausedFreeSfxScripts(bool paused) => _requests.Emit(new(AudioRequestKind.PauseFreeScripts, Force: paused));
        public void QueueStream(VoiceId id, float delay = 0, float expiration = 0) => _requests.QueueStream(id, delay, expiration);
    }
}
