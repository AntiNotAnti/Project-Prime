namespace MphRead.Sound
{
    // Playback handles and device-derived queries belong exclusively to client presentation.
    public static class ClientSoundSourceExtensions
    {
        public static int PlayFreeSfxHandle(this SoundSource source, SfxId id) => Sfx.Instance.PlayFreeSfx(id);
        public static int PlayFreeSfxHandle(this SoundSource source, int id) => Sfx.Instance.PlayFreeSfx(id);
        public static void StopSfxByHandle(this SoundSource source, int handle) => Sfx.Instance.StopSoundByHandle(handle);
        public static bool IsHandlePlaying(this SoundSource source, int handle) => Sfx.Instance.IsHandlePlaying(handle);
        public static int CountPlayingSfx(this SoundSource source, SfxId id) => Sfx.Instance.CountPlayingSfx((int)id);
        public static int CountPlayingSfx(this SoundSource source, int id) => Sfx.Instance.CountPlayingSfx(id);
    }
}
