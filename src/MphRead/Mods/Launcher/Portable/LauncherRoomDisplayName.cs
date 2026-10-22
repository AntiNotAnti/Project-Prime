using System;

namespace MphRead.Mods.Launcher;

/// <summary>Converts a room key to its in-game title at UI presentation boundaries.</summary>
public static class LauncherRoomDisplayName
{
    public static string Get(string? roomKey)
    {
        if (String.IsNullOrWhiteSpace(roomKey)) return String.Empty;
        var rooms = MphRead.Metadata.RoomMetadata;
        if (rooms.TryGetValue(roomKey, out MphRead.RoomMetadata? room))
            return String.IsNullOrWhiteSpace(room.InGameName) ? roomKey : room.InGameName;
        // Network/replay keys can differ in case. Keep the normal exact-key
        // path allocation-free, and never guess a title from the code name.
        foreach (var candidate in rooms)
            if (String.Equals(candidate.Key, roomKey, StringComparison.OrdinalIgnoreCase))
                return String.IsNullOrWhiteSpace(candidate.Value.InGameName) ? roomKey : candidate.Value.InGameName;
        return roomKey;
    }
}
