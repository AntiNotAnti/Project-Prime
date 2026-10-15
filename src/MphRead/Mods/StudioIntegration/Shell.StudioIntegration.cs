#if MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using MphRead.Mods.Network;
using ProjectPrime.Studio.Protocol;

namespace MphRead.Mods.Launcher.Gui;

internal static partial class Shell
{
    internal static bool ExternalStudioLaunchPending => _pending != null || _matchLoading;
    internal static bool ExternalStudioUiBusy => _front?.ExternalStudioUiBusy != false;
    internal static bool IsExternalStudioPlaytestRunning(string room)
        => _window?.HasScene == true && _played is { IsPlaytest: true } played && played.RoomKey == room;
    internal static bool QueueExternalStudioPlaytest(string room, StudioPlaytestOptions options)
    {
        if (!Active || _window?.HasScene == true || ExternalStudioLaunchPending || ExternalStudioUiBusy || NetSession.Active) return false;
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
        if (!Active || _window?.HasScene == true || ExternalStudioLaunchPending || ExternalStudioUiBusy || NetSession.Active) return false;
        // Broker callers cannot silently replace the user's configured Community authority.
        if (communityAddress != null && communityAddress.TrimEnd('/') != MapGen.MapCommunityClient.DefaultAddress.TrimEnd('/')) return false;
        _front!.OpenStudioHosting(room);
        return true;
    }
}
#endif
