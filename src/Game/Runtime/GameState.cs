using MphRead.Formats;
using MphRead.Entities;

namespace MphRead
{
    public static class GameState
    {
        private static string[] BuildDefaultNicknames()
        {
            string[] names = new string[PlayerEntity.SlotCapacity];
            for (int i = 0; i < names.Length; i++)
            {
                names[i] = $"Player{i + 1}";
            }
            return names;
        }

        public static string[] Nicknames { get; } = BuildDefaultNicknames();

        public static void Reset(bool preserveNicknames = false)
        {
            if (!preserveNicknames)
            {
                for (int i = 0; i < Nicknames.Length; i++) { Nicknames[i] = $"Player{i + 1}"; }
            }
            PlayerEntity.Reset();
            CamSeqEntity.Current = null;
            CameraSequence.Current = null;
        }
    }

}
