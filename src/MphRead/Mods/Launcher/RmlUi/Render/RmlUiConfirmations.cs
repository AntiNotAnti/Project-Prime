#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using MphRead.Mods.Launcher.Gui;
using MphRead.Mods.Launcher.RmlUi.Host;
namespace MphRead.Mods.Launcher.RmlUi.Render;
internal static partial class RmlUiOpenGlSmoke
{
    private static RmlUiDocumentToken _quitDocument;
    private static bool OpenNativeQuit()
    {
        if (_quitDocument != default) return true;
        if (_host is not { Active: true } || _pages is not { ModalCount: 0 } || _pages.Page == default
            || !Shell.Active || Shell.Window is not { HasScene: false } || !PrepareFullSettingsNavigation()
            || _hunterChoice is { } hunter && !hunter.PrepareNavigation()) return false;
        _quitDocument = _pages.OpenModal(new("quit-confirmation", "pages/common/quit.rml", "quit_cancel"));
        return true;
    }
    private static bool HandleNativeQuit(in RmlUiIntent intent)
    {
        if (_quitDocument == default) return false;
        if (_pages?.Top != _quitDocument || intent.Document != _quitDocument || _pages.Accept(intent) != true) return true;
        if (intent.Kind is not (RmlUiIntentKind.SettingsApply or RmlUiIntentKind.SettingsClose)) return true;
        bool accepted = intent.Kind == RmlUiIntentKind.SettingsApply;
        CloseNativeQuit();
        if (accepted) Shell.RequestQuit();
        return true;
    }
    private static void CloseNativeQuit()
    {
        if (_quitDocument != default && _pages?.Top == _quitDocument) _pages.CloseModal();
        _quitDocument = default;
    }
}
#endif
