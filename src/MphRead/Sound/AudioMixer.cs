using System;
using System.Collections.Generic;

namespace MphRead.Sound
{
    public enum AudioBus { Player, Weapons, Notifications, SoundEffects }

    /// <summary>
    /// Category gains beneath the legacy game-audio master. Music retains its
    /// independent streaming gain. Routes are captured by each playing instance.
    /// </summary>
    public static class AudioMixer
    {
        private static readonly float[] _volumes = { 1, 1, 1, 1 };
        private static readonly Dictionary<int, AudioBus> _routes = new();

        public static float GetVolume(AudioBus bus) => _volumes[(int)bus];

        public static void SetVolume(AudioBus bus, float volume)
            => _volumes[(int)bus] = float.IsFinite(volume) ? Math.Clamp(volume, 0, 1) : 1;

        public static AudioBus Classify(int id, AudioBus fallback)
            => _routes.TryGetValue(id, out AudioBus bus) ? bus : fallback;

        public static void Register(int id, AudioBus bus)
        {
            if (id >= 0)
                _routes[id] = bus;
        }

        internal static void RegisterHunterSounds(int[,] sounds)
        {
            for (int row = 0; row < sounds.GetLength(0); row++)
                for (int column = 0; column < sounds.GetLength(1); column++)
                    Register(sounds[row, column], column >= (int)HunterSfx.BeamSwitch
                        ? AudioBus.Weapons : AudioBus.Player);
        }

        internal static void RegisterWeaponSounds(int[,] sounds)
        {
            foreach (int id in sounds)
                Register(id, AudioBus.Weapons);
        }
    }
}
