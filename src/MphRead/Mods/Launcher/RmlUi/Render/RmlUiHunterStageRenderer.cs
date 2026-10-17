#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using MphRead.Mods.Launcher.RmlUi.Core;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Render;
using MphRead.Mods.Cosmetics;
using OpenTK.Graphics.OpenGL;

namespace MphRead.Mods.Launcher.RmlUi.Render;

/// <summary>
/// The same scene-free GL window owns the 3D chamber, engine Hunter previews,
/// atmospheric foreground and final RmlUi UI pass. This never owns a second
/// game Scene/network session or a second GL context.
/// </summary>
internal static class RmlUiHunterStageRenderer
{
    // Farther pads first; local/front player is deliberately rendered last.
    private static readonly int[] BackToFront = { 7, 5, 6, 3, 4, 1, 2, 0 };
    private static bool _shown;

    public static void Draw(RenderWindow window, RmlUiHost host,
        RmlUiMenuPage page, bool suspended, HubSnapshot hub,
        RmlUiLobbyView lobby, RmlUiAdminSnapshot? roster,
        int width, int height)
    {
        if (suspended || !host.Active || window.HasScene || width <= 0 || height <= 0)
        {
            Hide();
            return;
        }

        bool home = page == RmlUiMenuPage.Home && !lobby.Active;
        bool lobbyStage = page == RmlUiMenuPage.Lobby && lobby.Active;
        bool chamber = page is RmlUiMenuPage.Home or RmlUiMenuPage.Play
            or RmlUiMenuPage.Offline or RmlUiMenuPage.Adventure
            or RmlUiMenuPage.Theatre || lobbyStage;
        if (!chamber)
        {
            Hide();
            return;
        }

        RmlUiHunterStagePlan plan = RmlUiHunterStagePlan.Build(hub, lobby, roster);
        if (lobbyStage)
        {
            // Roster identities and native labels share one formation order.
            // A missing authoritative roster yields empty pads, never invented
            // Hunters or another player's look.
            PublishFormation(host, plan, lobby);
            LauncherBackdrop.Set(LauncherBackdropScene.Lobby,
                String.IsNullOrWhiteSpace(lobby.Map) ? null : lobby.Map);
        }
        else
        {
            if (_shown && LauncherLobbyVisuals.Active) LauncherLobbyVisuals.Reset();
            LauncherBackdrop.Set(page switch
            {
                RmlUiMenuPage.Play => LauncherBackdropScene.Play,
                RmlUiMenuPage.Offline => LauncherBackdropScene.Offline,
                RmlUiMenuPage.Adventure => LauncherBackdropScene.Adventure,
                RmlUiMenuPage.Theatre => LauncherBackdropScene.ReplayStudio,
                _ => LauncherBackdropScene.Home
            });
        }

        LauncherPresentation.BeginFrame(plan.HomeHunter, window.IsFocused);
        LauncherBackdropComposer.Enabled = true;
        LauncherStageFx.Enabled = true;
        _shown = true;
        try
        {
            // Draw the original procedural deployment chamber into the current
            // back buffer *before* rendering RmlUi's final translucent layers.
            LauncherBackdropComposer.Draw(width, height);
            LauncherHunter.Wanted = home && plan.GameFilesReady
                || lobbyStage && plan.Count > 0;
            LauncherHunter.CanPresent = () => host.Active && !window.HasScene;
            LauncherHunter.CinematicLighting = true;
            LauncherHunter.TransparentBackground = true;

            if (home && plan.GameFilesReady)
            {
                ConfigureHome(plan);
                LauncherStageFx.DrawUnderHunter(width, height);
                LauncherHunter.Draw(window, width, height);
                LauncherStageFx.DrawOverHunter(width, height);
            }
            else if (lobbyStage)
            {
                LauncherStageFx.DrawUnderHunter(width, height);
                foreach (int index in BackToFront)
                {
                    if (index >= plan.Count) continue;
                    ConfigureSlot(index, plan.Formation[index]);
                    LauncherHunter.Draw(window, width, height);
                }
                if (plan.Count > 0)
                {
                    ConfigureSlot(0, plan.Formation[0]);
                    LauncherStageFx.DrawOverHunter(width, height);
                }
            }
        }
        finally
        {
            // No leaked preview permission/GL program can affect the next
            // results screen or the native RmlUi GL2 fixed-function compositor.
            LauncherPresentation.Active = false;
            LauncherHunter.Wanted = false;
            LauncherHunter.CanPresent = null;
            LauncherHunter.TransparentBackground = false;
            LauncherHunter.CinematicLighting = false;
            LauncherHunter.DistanceScale = 1;
            LauncherHunter.PreviewSlot = -1;
            LauncherHunter.Formation = false;
            Scene.LauncherPreview = false;
            Scene.LauncherPreviewTransparent = false;
            Scene.LauncherPreviewCinematicLighting = false;
            Scene.LauncherPreviewDistanceScale = 1;
            Scene.LauncherPreviewSlot = -1;
            Scene.LauncherPreviewFormation = false;
            LauncherBackdropComposer.Enabled = false;
            LauncherStageFx.Enabled = false;
            GL.ActiveTexture(TextureUnit.Texture0);
            GL.UseProgram(0);
        }
    }

    /// <summary>
    /// Render the separately-owned native Hunter picker into its actual RML
    /// preview rectangle. It is a terminal isolated preview drawn after RmlUi
    /// chrome, because the picker card itself is translucent and would otherwise
    /// conceal the actual model. The viewport/scissor prevent touching buttons.
    /// Never re-use the Home/lobby formation entity or take over a match Scene.
    /// </summary>
    public static bool DrawPicker(RenderWindow window, RmlUiHost host,
        RmlUiDocumentToken document, Hunter hunter, int suit,
        CosmeticLoadout draft, int width, int height)
    {
        if (window.HasScene || !host.Active || !host.IsVisible(document)
            || width <= 0 || height <= 0
            || (uint)hunter >= 7 || suit is < 0 or > 3
            || !host.TryGetElementBounds(document, "native_hunter_preview",
                out float x, out float y, out float w, out float h)
            || !RmlUiPreviewViewport.TryCreate(x, y, w, h, width, height,
                out var viewport))
            return false;

        CosmeticLoadout? previousLoadout = CosmeticPreview.Loadout;
        SkinContext previousMode = CosmeticPreview.Mode;
        float previousYaw = CosmeticPreview.Yaw;
        float previousZoom = CosmeticPreview.Zoom;
        bool previousLoop = CosmeticPreview.LoopDeath;
        int previousDeath = CosmeticPreview.DeathRequest;
        try
        {
            CosmeticPreview.Loadout = draft;
            CosmeticPreview.Mode = SkinContext.Biped;
            CosmeticPreview.Yaw = 0f;
            CosmeticPreview.Zoom = 1f;
            CosmeticPreview.LoopDeath = false;
            CosmeticPreview.DeathRequest = 0;

            LauncherHunter.Wanted = true;
            LauncherHunter.CanPresent = () => host.IsVisible(document) && !window.HasScene;
            LauncherHunter.Hunter = hunter;
            LauncherHunter.Suit = suit;
            LauncherHunter.TransparentBackground = true;
            LauncherHunter.CinematicLighting = true;
            LauncherHunter.DistanceScale = .86f;
            LauncherHunter.PreviewSlot = -1;
            LauncherHunter.Formation = false;
            LauncherHunter.Left = viewport.Left;
            LauncherHunter.Top = viewport.Top;
            LauncherHunter.Right = viewport.Right;
            LauncherHunter.Bottom = viewport.Bottom;
            LauncherHunter.Draw(window, width, height);
            return LauncherHunter.Drawn;
        }
        finally
        {
            LauncherHunter.Wanted = false;
            LauncherHunter.CanPresent = null;
            LauncherHunter.TransparentBackground = false;
            LauncherHunter.CinematicLighting = false;
            LauncherHunter.DistanceScale = 1f;
            LauncherHunter.PreviewSlot = -1;
            LauncherHunter.Formation = false;
            Scene.LauncherPreview = false;
            Scene.LauncherPreviewTransparent = false;
            Scene.LauncherPreviewCinematicLighting = false;
            Scene.LauncherPreviewDistanceScale = 1f;
            Scene.LauncherPreviewSlot = -1;
            Scene.LauncherPreviewFormation = false;

            CosmeticPreview.Loadout = previousLoadout;
            CosmeticPreview.Mode = previousMode;
            CosmeticPreview.Yaw = previousYaw;
            CosmeticPreview.Zoom = previousZoom;
            CosmeticPreview.LoopDeath = previousLoop;
            CosmeticPreview.DeathRequest = previousDeath;
            GL.UseProgram(0);
            GL.ActiveTexture(TextureUnit.Texture0);
        }
    }

    private static void ConfigureHome(RmlUiHunterStagePlan plan)
    {
        MenuStageProfile stage = LauncherMenuStage.Current;
        float half = (stage.HunterRight - stage.HunterLeft) * .5f;
        LauncherHunter.Wanted = true;
        LauncherHunter.Hunter = plan.HomeHunter;
        LauncherHunter.Suit = plan.HomeSuit;
        LauncherHunter.PreviewSlot = -1;
        LauncherHunter.Formation = false;
        LauncherHunter.Left = .5f - half;
        LauncherHunter.Top = stage.HunterTop;
        LauncherHunter.Right = .5f + half;
        LauncherHunter.Bottom = stage.HunterBottom;
        LauncherHunter.DistanceScale = stage.HunterDistanceScale;
    }

    private static void ConfigureSlot(int index, RmlUiStageHunter actor)
    {
        LobbyFormationSlot place = LauncherLobbyFormation.At(index);
        LauncherHunter.Wanted = true;
        LauncherHunter.Hunter = actor.Hunter;
        LauncherHunter.Suit = actor.Suit;
        LauncherHunter.PreviewSlot = index;
        LauncherHunter.Formation = true;
        LauncherHunter.Left = place.HunterLeft;
        LauncherHunter.Top = place.HunterTop;
        LauncherHunter.Right = place.HunterRight;
        LauncherHunter.Bottom = place.HunterBottom;
        LauncherHunter.DistanceScale = place.HunterDistance;
    }

    private static void PublishFormation(RmlUiHost host, RmlUiHunterStagePlan plan,
        RmlUiLobbyView lobby)
    {
        LauncherLobbyVisuals.Active = true;
        LauncherLobbyVisuals.Starting = lobby.Starting;
        LauncherLobbyVisuals.CountdownSeconds = 0;
        byte occupied = 0, ready = 0;
        for (int i = 0; i < LauncherLobbyFormation.Capacity; i++)
        {
            LobbyFormationSlot place = LauncherLobbyFormation.At(i);
            host.SetLobbyAnchor(i, place.LabelX, place.LabelY);
            if (i >= plan.Count)
            {
                LauncherLobbyVisuals.SetIdentity(i, -1);
                continue;
            }
            RmlUiStageHunter actor = plan.Formation[i];
            occupied |= (byte)(1 << i);
            if (actor.Ready) ready |= (byte)(1 << i);
            LauncherLobbyVisuals.SetIdentity(i, actor.Identity);
            LauncherLobbyVisuals.SetHunter(i, actor.Hunter);
        }
        LauncherLobbyVisuals.OccupiedMask = occupied;
        LauncherLobbyVisuals.ReadyMask = ready;
    }

    public static void Hide()
    {
        if (!_shown) return;
        _shown = false;
        LauncherPresentation.Active = false;
        LauncherHunter.Wanted = false;
        LauncherHunter.CanPresent = null;
        LauncherHunter.PreviewSlot = -1;
        LauncherHunter.Formation = false;
        Scene.LauncherPreview = false;
        Scene.LauncherPreviewSlot = -1;
        Scene.LauncherPreviewFormation = false;
        LauncherBackdropComposer.Enabled = false;
        LauncherStageFx.Enabled = false;
        LauncherLobbyVisuals.Reset();
    }
}
#endif
