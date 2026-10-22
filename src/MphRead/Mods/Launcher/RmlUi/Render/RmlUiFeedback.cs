#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Sound;

namespace MphRead.Mods.Launcher.RmlUi.Render;
internal static partial class RmlUiOpenGlSmoke
{
    private static RmlUiDocumentToken _feedbackDocument;
    private static string _feedbackHover = "", _feedbackFocus = "";
    private static void NativeIntentFeedback(RmlUiIntent intent)
    {
        UiFeedbackAudio.Play(intent.Kind switch
        {
            RmlUiIntentKind.Navigate or RmlUiIntentKind.OpenStudio or RmlUiIntentKind.SettingsCategory => UiFeedbackCue.Tab,
            RmlUiIntentKind.SettingsClose or RmlUiIntentKind.HunterCancel or RmlUiIntentKind.PlayCancel
                or RmlUiIntentKind.LobbyRulesClose or RmlUiIntentKind.LobbyAdminClose => UiFeedbackCue.Back,
            _ => UiFeedbackCue.Confirm
        });
    }
    private static void PresentNativeFeedback(RmlUiHost host)
    {
        var document = host.CurrentInputDocument;
        if (document == default) return;
        bool changed = _feedbackDocument != document;
        host.SetBool(document, "class:@document:reduce-motion", LauncherPrefs.ReduceMotion);
        if (changed) host.SetBool(document, "class:@document:native-page-enter", true);
        string hover = host.HoveredAction(), focus = host.FocusedElement();
        if (_feedbackDocument == document && _inputWindow is { IsFocused: true }
            && (hover.Length > 0 && hover != _feedbackHover || focus.Length > 0 && focus != _feedbackFocus))
            UiFeedbackAudio.Play(UiFeedbackCue.Navigate);
        _feedbackDocument = document; _feedbackHover = hover; _feedbackFocus = focus;
    }
}
#endif
