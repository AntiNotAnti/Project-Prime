using System.IO;

namespace MphRead;

// Strings and scalars only. This payload never owns state, players or a Scene.
internal readonly record struct StorySaveCommit(string Destination, string Json, byte Slot,
    uint Artifacts, int CheckpointRoomId)
{
    internal void Write()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Destination)!);
        File.WriteAllText(Destination, Json);
        if (Mods.DebugLog.Active)
            Mods.DebugLog.Line("save", $"wrote slot {Slot}: artifacts=0x{Artifacts:X8} checkpoint room={CheckpointRoomId}");
    }
}
