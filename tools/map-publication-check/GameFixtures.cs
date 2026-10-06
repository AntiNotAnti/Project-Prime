// Asset-free game policy fixtures. The harness links the production runtime-usage,
// publication transaction and OS lease implementations rather than duplicating them.
namespace MphRead
{
    public sealed class Scene;
    public static class Paths { public static string FileSystem { get; set; } = ""; }
}
namespace MphRead.Mods.MapGen
{
    public static class CustomRooms
    {
        public static string RuntimeNamespace { get; set; } = "";
        public static string RuntimePublicationRoot => Paths.FileSystem;
    }
}
namespace MphRead.Mods.Network
{
    public enum SessionPhase { Lobby, Match }
    public static class NetSession
    {
        public static bool Active { get; set; }
        public static bool IsClient { get; set; }
        public static SessionPhase SessionPhase { get; set; }
    }
}
