using MphRead.Entities;
namespace MphRead.Mods.Network
{
    public interface ILocalMatchHost
    {
        bool Running { get; }
        string? LastError { get; }
        bool StartAndJoin(bool friendlyFire, int port, string playerName, Hunter hunter,
            string roomKey, GameMode mode, float timeLimit, int pointGoal,
            int maxPlayers = PlayerEntity.SlotCapacity,
            (string Host, int Port, string Name)? listing = null, bool practice = false);
        void Stop();
    }

    public static class NetHostSession
    {
        public static ILocalMatchHost? Host { get; set; }
        public static bool Available => Host != null;
        public static bool Running => Host?.Running == true;
        public static string? LastError => Host?.LastError ?? "Local hosting is unavailable on this platform.";
        public static bool StartAndJoin(bool friendlyFire, int port, string playerName, Hunter hunter,
            string roomKey, GameMode mode, float timeLimit, int pointGoal,
            int maxPlayers = PlayerEntity.SlotCapacity,
            (string Host, int Port, string Name)? listing = null, bool practice = false)
            => Host?.StartAndJoin(friendlyFire, port, playerName, hunter, roomKey, mode,
                timeLimit, pointGoal, maxPlayers, listing, practice) ?? false;
        public static void Stop() => Host?.Stop();
    }
}
