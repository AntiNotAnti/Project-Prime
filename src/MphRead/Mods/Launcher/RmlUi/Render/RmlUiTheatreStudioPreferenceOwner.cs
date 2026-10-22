#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using MphRead.Mods.Launcher.Core;
namespace MphRead.Mods.Launcher.RmlUi.Render;
internal static partial class RmlUiOpenGlSmoke
{
    private sealed partial class CurrentReplayEditor : ITheatreStudioPresentationPreferencesOwner
    {
        public bool WhatShooterSaw { get => _workspaceShooterView; set => _workspaceShooterView=value; }
    }
}
#endif
