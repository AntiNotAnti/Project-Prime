#if !ANDROID && !MPHREAD_SERVER
using MphRead.Mods.Network;
using ProjectPrime.Studio.Protocol;

namespace MphRead.Mods.Launcher.Gui;

internal static partial class Shell
{
    internal static bool ExternalStudioLaunchPending => _pending != null;
    internal static bool IsExternalStudioPlaytestRunning(string room)
        => _window?.HasScene == true && _played is { IsPlaytest: true } played && played.RoomKey == room;
    internal static bool QueueExternalStudioPlaytest(string room, StudioPlaytestOptions options)
    {
        if (!Active || _window?.HasScene == true || _pending != null || NetSession.Active) return false;
        _settings.RoomKey = room;
        _pending = new LaunchPlan { Kind = LaunchKind.Offline, RoomKey = room, IsPlaytest = true,
            Hunter = (Hunter)options.Hunter, Mode = GameMode.Battle, Bots = options.Bots,
            BotLevel = options.BotLevel, PlayerName = "Map author" };
        return true;
    }
    internal static void StopExternalStudioPlaytest(string room)
    {
        if (_pending is { IsPlaytest: true } pending && pending.RoomKey == room) _pending = null;
        if (IsExternalStudioPlaytestRunning(room)) RequestEndMatch();
    }
    internal static bool OpenExternalStudioHosting(string room, string? communityAddress = null)
    {
        if (!Active || _window?.HasScene == true || _pending != null || _front == null || NetSession.Active) return false;
        if (communityAddress != null)
        {
            using var validation = new MphRead.Mods.MapGen.MapCommunityClient(communityAddress);
            // Preserve the selected source through the canonical host/lobby advertisement path.
            System.IO.Directory.CreateDirectory(LauncherPrefs.Directory);
            System.IO.File.WriteAllText(System.IO.Path.Combine(LauncherPrefs.Directory, "map-community.txt"), communityAddress.Trim());
            System.Environment.SetEnvironmentVariable("PROJECT_PRIME_MAP_COMMUNITY", communityAddress.Trim());
        }
        _front.OpenStudioHosting(room);
        return true;
    }
}
#endif
