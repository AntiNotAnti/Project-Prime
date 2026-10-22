// Only unrelated engine symbols needed to compile the actual byte owner.
// No fixture invokes Begin/Read or a StorySave serializer; management uses real files.
namespace MphRead;
public enum Hunter { Samus, Kanden, Trace, Sylux, Noxus, Spire, Weavel }
public sealed class StorySave
{
    public int CheckpointRoomId = -1, Health = 99, HealthMax = 99;
    public int CountFoundOctoliths() => 0;
}
public sealed class SceneGameState
{
    public StorySave StorySave = new();
    public void StartNewSave() { }
    public void LoadSave() { }
}
public static class GameState
{
    public static SceneGameState Current = new();
    public static StorySave? PeekSave(byte slot) => null;
    public static void CommitSave() { }
}
public static class Menu { public static byte SaveSlot; }
public static class Paths { public static string Combine(params string[] parts) => Path.Combine(parts); }
public sealed class RoomMetadata { public string Name => ""; }
public static class Metadata
{
    public static int GetAreaInfo(int room) => 0;
    public static RoomMetadata? GetRoomById(int room, bool noThrow) => null;
}
