using System;
using System.Globalization;
using System.Resources;

namespace MphRead.Mods.Launcher.Resources;

/// <summary>
/// Strongly named access to player-facing launcher copy. Domain terminology
/// remains in <c>PrimeGameText</c>; this class owns interface wording and is
/// deliberately small so future locale resources can be added without
/// rewriting presentation builders.
/// </summary>
internal static class PrimeUiCopy
{
    private static readonly ResourceManager ResourceManager = new(
        "MphRead.Mods.Launcher.Resources.PrimeUiCopy",
        typeof(PrimeUiCopy).Assembly);

    public static string Get(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        return ResourceManager.GetString(key, CultureInfo.CurrentUICulture)
            ?? throw new MissingManifestResourceException(
                $"UI copy resource '{key}' is not available.");
    }

    public static string Common_Retry => Get(nameof(Common_Retry));
    public static string Common_Cancel => Get(nameof(Common_Cancel));
    public static string Common_Close => Get(nameof(Common_Close));
    public static string Common_Back => Get(nameof(Common_Back));
    public static string Common_Continue => Get(nameof(Common_Continue));
    public static string Common_Refresh => Get(nameof(Common_Refresh));
    public static string Common_ViewAll => Get(nameof(Common_ViewAll));
    public static string Common_LearnMore => Get(nameof(Common_LearnMore));

    public static string Play_Title => Get(nameof(Play_Title));
    public static string Play_Subtitle => Get(nameof(Play_Subtitle));
    public static string Play_QuickPlay_Title => Get(nameof(Play_QuickPlay_Title));
    public static string Play_QuickPlay_Description => Get(nameof(Play_QuickPlay_Description));
    public static string Play_HostLobby_Title => Get(nameof(Play_HostLobby_Title));
    public static string Play_HostLobby_Description => Get(nameof(Play_HostLobby_Description));
    public static string Play_BrowseLobbies_Title => Get(nameof(Play_BrowseLobbies_Title));
    public static string Play_BrowseLobbies_Description => Get(nameof(Play_BrowseLobbies_Description));
    public static string Play_NoLobbies_Title => Get(nameof(Play_NoLobbies_Title));
    public static string Play_NoLobbies_Description => Get(nameof(Play_NoLobbies_Description));
    public static string Play_OnlinePlayers_Title => Get(nameof(Play_OnlinePlayers_Title));
    public static string Play_OnlinePlayers_Empty => Get(nameof(Play_OnlinePlayers_Empty));

    public static string Settings_Network_Title => Get(nameof(Settings_Network_Title));
    public static string Settings_Network_Description => Get(nameof(Settings_Network_Description));
    public static string Settings_Network_AutomaticRegion_Description
        => Get(nameof(Settings_Network_AutomaticRegion_Description));
    public static string Settings_Updates_Title => Get(nameof(Settings_Updates_Title));
    public static string Settings_Updates_LocalBuild => Get(nameof(Settings_Updates_LocalBuild));
    public static string Settings_Updates_Unavailable => Get(nameof(Settings_Updates_Unavailable));
    public static string Settings_Updates_Disabled => Get(nameof(Settings_Updates_Disabled));

    public static string Theatre_Empty_Title => Get(nameof(Theatre_Empty_Title));
    public static string Theatre_Empty_Description => Get(nameof(Theatre_Empty_Description));
    public static string Rankings_SignIn_Title => Get(nameof(Rankings_SignIn_Title));
    public static string Rankings_SignIn_Description => Get(nameof(Rankings_SignIn_Description));
    public static string Gateway_SessionMissing => Get(nameof(Gateway_SessionMissing));
    public static string Gateway_GuestNotice => Get(nameof(Gateway_GuestNotice));
    public static string Maps_Empty_Title => Get(nameof(Maps_Empty_Title));
    public static string Maps_Empty_Description => Get(nameof(Maps_Empty_Description));
    public static string Hunter_Empty_Title => Get(nameof(Hunter_Empty_Title));
    public static string Hunter_Empty_Description => Get(nameof(Hunter_Empty_Description));

    public static string Update_Checking => Get(nameof(Update_Checking));
    public static string Update_UpToDate => Get(nameof(Update_UpToDate));
    public static string Update_Available => Get(nameof(Update_Available));
    public static string Update_LocalBuild => Get(nameof(Update_LocalBuild));
    public static string Update_FeedUnavailable => Get(nameof(Update_FeedUnavailable));
    public static string Update_Disabled => Get(nameof(Update_Disabled));
    public static string Update_NetworkUnavailable => Get(nameof(Update_NetworkUnavailable));
    public static string Update_VersionUnknown => Get(nameof(Update_VersionUnknown));
    public static string Update_InstallUnavailable => Get(nameof(Update_InstallUnavailable));
    public static string Update_VerificationFailed => Get(nameof(Update_VerificationFailed));

    public static string Network_Unavailable => Get(nameof(Network_Unavailable));
    public static string Account_SignInFailed => Get(nameof(Account_SignInFailed));
    public static string Account_ConfirmationRequired => Get(nameof(Account_ConfirmationRequired));
    public static string Account_ServiceUnavailable => Get(nameof(Account_ServiceUnavailable));
    public static string Account_RateLimited => Get(nameof(Account_RateLimited));
    public static string Account_AlreadyExists => Get(nameof(Account_AlreadyExists));
    public static string Account_NetworkUnavailable => Get(nameof(Account_NetworkUnavailable));
    public static string Error_Generic => Get(nameof(Error_Generic));
}
