#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Render.Hud;

namespace MphRead.Mods.Launcher.RmlUi.Render;

internal static partial class RmlUiOpenGlSmoke
{
    private static HudRuntimeProfile? _typographyProfile;
    private static RmlUiDocumentToken _typographyLifetime;

    private static void PresentNativeTypography(RmlUiHost host)
    {
        var runtime = HudProfiles.Runtime;
        if (ReferenceEquals(runtime, _typographyProfile) && _typographyLifetime == host.HomeDocument) return;
        // The resolved runtime already applies Global -> Interface -> role
        // font precedence. Compare publication identity, never mutate per frame.
        var typography = runtime.Typography;
        int Selected(string role)
        {
            var style = typography.Get(role);
            return style.Enabled ? HudFonts.Index(style.Font) : -1;
        }
        host.SetTypography(Selected("ui.body"), Selected("ui.headings"), Selected("ui.data"));
        _typographyProfile = runtime;
        _typographyLifetime = host.HomeDocument;
    }
}
#endif
