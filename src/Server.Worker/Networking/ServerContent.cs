using System;
namespace MphRead.Mods.Network
{
    /// <summary>Server composition compatibility entry to neutral content loading.</summary>
    public static class ServerContent
    {
        internal static ReadOnlySpan<ServerContentScenario> ValidatedScenarios => ContentEnvironment.ValidatedScenarios;
        public static byte[] ReadBytes(string path) => ContentFiles.ReadBytes(path);
        internal static void RecordModel(string path) => ContentFiles.RecordModel(path);
        internal static IDisposable TraceReads(Action<string, byte[]> record, Action<string>? recordModel = null)
            => ContentFiles.ObserveReads(record, recordModel);
        internal static IDisposable PreserveContext(string version) => ContentEnvironment.PreserveContext(version);
        public static int ResolveRoomPlayerCount(int? requested, int fallback)
            => ContentEnvironment.ResolveRoomPlayerCount(requested, fallback);
        public static void RequireRoom(string room, GameMode mode) => ContentEnvironment.RequireRoom(room, mode);
        public static void Open(string directory, string version) => ContentEnvironment.Open(directory, version);
    }
}
