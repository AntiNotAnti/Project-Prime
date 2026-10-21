namespace MphRead.Mods.Launcher;

// This source-linked party adapter harness does not load game metadata or the UI.
// The production LauncherRoomDisplayName uses the metadata catalog. The adapter
// only needs a room-name display fallback for the invitation-search predicate.
internal static class LauncherRoomDisplayName
{
    public static string Get(string? roomKey) => roomKey ?? string.Empty;
}
