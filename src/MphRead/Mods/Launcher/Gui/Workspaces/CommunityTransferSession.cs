#if MPHREAD_AVALONIA
using System;
using System.IO;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.Launcher.Gui;

/// <summary>
/// Game and opt-in creator workspace share exactly one persistent transfer
/// manager and receipt index. Two independent managers over the same directory
/// would race each other's snapshots and persistence.
/// </summary>
internal static class CommunityTransferSession
{
    private static readonly Lazy<CommunityTransferManager> SharedQueue = new(() =>
        new CommunityTransferManager(
            Path.Combine(LauncherPrefs.Directory, "community-transfers"),
            new DelegateCommunityCredentialSource(
                HunterLicenseClient.GetCommunityMapTicketAsync,
                HunterLicenseClient.RefreshCommunityMapTicketAsync)));

    internal static CommunityTransferManager Shared => SharedQueue.Value;
}
#endif
