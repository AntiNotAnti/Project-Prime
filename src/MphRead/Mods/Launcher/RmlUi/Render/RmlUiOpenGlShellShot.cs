#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using MphRead.Mods.Launcher.Gui;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Pages.Setup;

namespace MphRead.Mods.Launcher.RmlUi.Render;

internal static partial class RmlUiOpenGlSmoke
{
    internal readonly record struct NativeShellShotView(
        string Route,
        string HoveredElement,
        string FocusedElement,
        bool HasNewsLink,
        float NewsX,
        float NewsY,
        float NewsWidth,
        float NewsHeight,
        bool HasSettingsButton,
        float SettingsX,
        float SettingsY,
        float SettingsWidth,
        float SettingsHeight,
        bool GameFilesOwnsInput,
        bool GameFilesReady,
        bool GameFilesCanBack,
        bool HasGameFilesChooseButton,
        float GameFilesChooseX,
        float GameFilesChooseY,
        float GameFilesChooseWidth,
        float GameFilesChooseHeight);

    /// <summary>
    /// Read the native page which currently owns input and the authored bounds
    /// used by the capture script. This does not route an intent or change UI
    /// state; screenshots must follow real window input.
    /// </summary>
    internal static bool TryGetNativeShellShotView(RenderWindow window, out NativeShellShotView view)
    {
        view = default;
        if (window.HasScene || !ReferenceEquals(Shell.Window, window)
            || !ReferenceEquals(_inputWindow, window) || !_shown || _disabled
            || _host is not { Active: true } host || !window.IsFocused)
        {
            return false;
        }

        RmlUiDocumentToken input = host.CurrentInputDocument;
        string route;
        if (NativeStartupOpen)
        {
            route = "startup";
        }
        else if (_gameFiles is { OwnsForeground: true })
        {
            route = "game-files";
        }
        else if (_fullSettings is { OwnsForeground: true })
        {
            route = "settings";
        }
        else if (_news is { } news && news.Document != default
            && input == news.Document && host.IsVisible(news.Document))
        {
            route = "news";
        }
        else if (_launcherPages is { Page: RmlUiMenuPage.Home } launcher
            && launcher.Document != default && input == launcher.Document
            && host.IsAlive(launcher.Document) && host.IsVisible(launcher.Document))
        {
            route = "home";
        }
        else
        {
            // A base page key is not an input-ownership observation. Keep
            // unknown/modal owners from masquerading as Home in the shellshot.
            route = "unowned:" + (_pages?.PageKey ?? "unknown");
        }

        float newsX = 0, newsY = 0, newsWidth = 0, newsHeight = 0;
        bool hasNews = route == "home" && _launcherPages is { } home
            && host.TryGetElementBounds(home.Document, "home_feature_open",
                out newsX, out newsY, out newsWidth, out newsHeight);
        float settingsX = 0, settingsY = 0, settingsWidth = 0, settingsHeight = 0;
        bool hasSettings = route == "home" && _launcherPages is { } settingsHome
            && host.TryGetElementBounds(settingsHome.Document, "header_settings",
                out settingsX, out settingsY, out settingsWidth, out settingsHeight);

        bool gameFilesOwnsInput = _gameFiles?.OwnsForeground == true;
        bool gameFilesReady = false, gameFilesCanBack = false;
        float gameFilesChooseX = 0, gameFilesChooseY = 0;
        float gameFilesChooseWidth = 0, gameFilesChooseHeight = 0;
        bool hasGameFilesChooseButton = false;
        if (gameFilesOwnsInput)
        {
            NativeGameFilesView state = _gameFiles!.Snapshot();
            gameFilesReady = state.Files.Ready;
            gameFilesCanBack = state.CanBack;
            hasGameFilesChooseButton = host.TryGetElementBounds(_gameFiles.Document,
                "native_setup_choose", out gameFilesChooseX, out gameFilesChooseY,
                out gameFilesChooseWidth, out gameFilesChooseHeight);
        }

        view = new(route, host.HoveredElement(), host.FocusedElement(), hasNews, newsX, newsY, newsWidth, newsHeight,
            hasSettings, settingsX, settingsY, settingsWidth, settingsHeight,
            gameFilesOwnsInput, gameFilesReady, gameFilesCanBack,
            hasGameFilesChooseButton, gameFilesChooseX, gameFilesChooseY,
            gameFilesChooseWidth, gameFilesChooseHeight);
        return true;
    }
}
#endif
