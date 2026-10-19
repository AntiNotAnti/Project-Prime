#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
namespace MphRead.Mods.Launcher.RmlUi.Render;

internal static partial class RmlUiOpenGlSmoke
{
    private static bool ApplyNativePartyJoin()
    {
        if (_party is not { } party || !party.TryTakeJoin(out var target)) return false;
        if (_controller?.ConnectParty(target, HubState.Capture().PlayerName) != true)
        {
            party.ReportJoinRefused("PARTY JOIN REFUSED // FINISH THE CURRENT REQUEST OR LEAVE THE CURRENT SESSION, THEN REFRESH PARTY");
            return false;
        }
        CloseNativeSocial();
        return true;
    }
}
#endif
