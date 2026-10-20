#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using MphRead.Mods.Launcher.RmlUi.Core;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Render;
using MphRead.Mods.Cosmetics;
using MphRead.Mods.Render.Characters;
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
        int width, int height, LauncherBackdropScene? background = null,
        RmlUiDocumentToken homeDocument = default)
    {
        if (suspended || !host.Active || window.HasScene || width <= 0 || height <= 0)
        {
            Hide();
            return;
        }

        bool home = background == null && page == RmlUiMenuPage.Home && !lobby.Active;
        bool lobbyStage = background == null && page == RmlUiMenuPage.Lobby && lobby.Active;
        bool chamber = background != null || page is RmlUiMenuPage.Home or RmlUiMenuPage.Play
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
            LauncherBackdrop.Set(background ?? (page switch
            {
                RmlUiMenuPage.Play => LauncherBackdropScene.Play,
                RmlUiMenuPage.Offline => LauncherBackdropScene.Offline,
                RmlUiMenuPage.Adventure => LauncherBackdropScene.Adventure,
                RmlUiMenuPage.Theatre => LauncherBackdropScene.ReplayStudio,
                _ => LauncherBackdropScene.Home
            }));
        }

        LauncherPresentation.BeginFrame(plan.HomeHunter, window.IsFocused);
        LauncherBackdropComposer.Enabled = true;
        LauncherStageFx.Enabled = true;
        LauncherHomeStageLayout? homeLayout = home ? ReadHomeLayout(host, homeDocument, width, height) : null;
        LauncherBackdropComposer.HomeLayout = homeLayout;
        _shown = true;
        try
        {
            // Draw the original procedural deployment chamber into the current
            // back buffer *before* rendering RmlUi's final translucent layers.
            RestoreCompositorTarget(width, height);
            LauncherBackdropComposer.Draw(width, height);
            LauncherHunter.Wanted = home && plan.GameFilesReady
                || lobbyStage && plan.Count > 0;
            LauncherHunter.CanPresent = () => host.Active && !window.HasScene;
            LauncherHunter.CinematicLighting = true;
            LauncherHunter.TransparentBackground = true;

            if (home && plan.GameFilesReady)
            {
                ConfigureHome(plan, homeLayout);
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
            LauncherHunter.SourcePreviewRequest = null;
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
            LauncherBackdropComposer.HomeLayout = null;
            LauncherStageFx.Enabled = false;
            RestoreCompositorTarget(width, height);
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
        CosmeticLoadout draft, LauncherHunterPreviewSnapshot preview, int width, int height)
        => DrawPickerCore(window, host, document, hunter, suit, draft, preview, width, height,
            "native_hunter_preview", "native_hunter_panel", "stage", null, -1);

    internal static bool DrawSourcePicker(RenderWindow window, RmlUiHost host, RmlUiDocumentToken document,
        LauncherHunterPreviewSnapshot preview, CharacterSourceStageRequest request, int width, int height)
        => DrawPickerCore(window, host, document, Hunter.Samus, 0, CosmeticLoadout.Default, preview, width, height,
            "source_preview", "source_panel", "source_stage", null, -1, request);

    private static RmlUiHost? _pickerHost;
    private static RenderWindow? _pickerWindow;
    private static RmlUiDocumentToken _pickerDocument;
    private static readonly Func<bool> PickerCanPresent = () => _pickerHost is { Active: true }
        && _pickerWindow is { HasScene: false } && _pickerHost.IsVisible(_pickerDocument)
        && _pickerHost.CurrentInputDocument == _pickerDocument;

    private static readonly string[] CardModels = { "hunter_model0", "hunter_model1", "hunter_model2", "hunter_model3", "hunter_model4", "hunter_model5", "hunter_model6" };
    private static readonly string[] CardChoices = { "hunter_choice0", "hunter_choice1", "hunter_choice2", "hunter_choice3", "hunter_choice4", "hunter_choice5", "hunter_choice6" };
    private static readonly string[] CardStatuses = { "visible:hunter_model_status0", "visible:hunter_model_status1", "visible:hunter_model_status2", "visible:hunter_model_status3", "visible:hunter_model_status4", "visible:hunter_model_status5", "visible:hunter_model_status6" };

    /// <summary>Seven isolated default-look full-body previews over the native lobby selector.</summary>
    public static void DrawLobbyHunterCards(RenderWindow window, RmlUiHost host,
        RmlUiDocumentToken document, int suit, int width, int height)
    {
        if (window.HasScene || !host.Active || !host.IsVisible(document) || host.CurrentInputDocument != document)
            return;
        var preview = new LauncherHunterPreviewSnapshot(SkinContext.Biped, 0f, 1f, false, 0);
        int drawnCards = 0;
        for (int wireId = 0; wireId < CardModels.Length; wireId++)
        {
            bool drawn = DrawPickerCore(window, host, document, (Hunter)wireId, suit,
                CosmeticLoadout.Default, preview, width, height, CardModels[wireId], CardChoices[wireId],
                "identity_cards", "identity_modal", 8 + wireId);
            if (drawn) drawnCards |= 1 << wireId;
        }
        // Do not mutate DOM between card measurements. A visibility change
        // dirties native layout; the next bounds read would update smooth
        // scrolling and move later models away from the already-painted UI.
        for (int wireId = 0; wireId < CardModels.Length; wireId++)
            if ((drawnCards & (1 << wireId)) != 0) host.SetBool(document, CardStatuses[wireId], false);
    }

    private static bool DrawPickerCore(RenderWindow window, RmlUiHost host,
        RmlUiDocumentToken document, Hunter hunter, int suit,
        CosmeticLoadout draft, LauncherHunterPreviewSnapshot preview, int width, int height,
        string previewId, string panelId, string clipId, string? outerId, int previewSlot, CharacterSourceStageRequest? sourceRequest = null)
    {
        if (window.HasScene || !host.Active || !host.IsVisible(document) || host.CurrentInputDocument != document
            || width <= 0 || height <= 0
            || (uint)hunter >= 7 || suit is < 0 or > 3 || !preview.IsValid
            || !host.TryGetElementBounds(document, previewId,
                out float x, out float y, out float w, out float h)
            || !host.TryGetElementBounds(document, panelId,
                out float panelX, out float panelY, out float panelW, out float panelH)
            || !RmlUiPreviewViewport.IsContained(x, y, w, h, panelX, panelY, panelW, panelH)
            || !host.TryGetElementBounds(document, clipId,
                out float stageX, out float stageY, out float stageW, out float stageH)
            || !RmlUiPreviewViewport.IsContained(x, y, w, h, stageX, stageY, stageW, stageH)
            || !RmlUiPreviewViewport.IsContained(x, y, w, h, 0, 0, width, height)
            || outerId != null && (!host.TryGetElementBounds(document, outerId,
                out float outerX, out float outerY, out float outerW, out float outerH)
                || !RmlUiPreviewViewport.IsContained(x, y, w, h, outerX, outerY, outerW, outerH))
            || !RmlUiPreviewViewport.TryCreate(x, y, w, h, width, height,
                out var viewport))
            return false;

        var previousSourceRequest = LauncherHunter.SourcePreviewRequest;
        int previousDraw = GL.GetInteger(GetPName.DrawFramebufferBinding);
        int previousRead = GL.GetInteger(GetPName.ReadFramebufferBinding);
        int previousProgram = GL.GetInteger(GetPName.CurrentProgram);
        int previousMatrixMode = GL.GetInteger(GetPName.MatrixMode);
        GL.PushAttrib(AttribMask.AllAttribBits);
        OpenTK.Graphics.OpenGL.GL.PushClientAttrib(ClientAttribMask.ClientAllAttribBits);
        GL.MatrixMode(MatrixMode.Projection); GL.PushMatrix();
        GL.MatrixMode(MatrixMode.Modelview); GL.PushMatrix();
        GL.ActiveTexture(TextureUnit.Texture0);
        GL.MatrixMode(MatrixMode.Texture); GL.PushMatrix();
        GL.MatrixMode((MatrixMode)previousMatrixMode);
        var previousLauncher = (LauncherHunter.Wanted, LauncherHunter.CanPresent, LauncherHunter.Hunter, LauncherHunter.Suit,
            LauncherHunter.TransparentBackground, LauncherHunter.CinematicLighting, LauncherHunter.DistanceScale,
            LauncherHunter.PreviewSlot, LauncherHunter.Formation, LauncherHunter.Left, LauncherHunter.Top, LauncherHunter.Right, LauncherHunter.Bottom);
        var previousScene = (Scene.LauncherPreview, Scene.LauncherPreviewTransparent, Scene.LauncherPreviewCinematicLighting,
            Scene.LauncherPreviewDistanceScale, Scene.LauncherPreviewSlot, Scene.LauncherPreviewFormation,
            Scene.LauncherHunter, Scene.LauncherSuit, Scene.PreviewWanted, Scene.PreviewLeft, Scene.PreviewTop, Scene.PreviewRight, Scene.PreviewBottom);
        CosmeticLoadout? previousLoadout = CosmeticPreview.Loadout;
        SkinContext previousMode = CosmeticPreview.Mode;
        float previousYaw = CosmeticPreview.Yaw;
        float previousZoom = CosmeticPreview.Zoom;
        bool previousLoop = CosmeticPreview.LoopDeath;
        int previousDeath = CosmeticPreview.DeathRequest;
        try
        {
            CosmeticPreview.Loadout = draft;
            CosmeticPreview.Mode = preview.Mode;
            CosmeticPreview.Yaw = preview.Yaw;
            CosmeticPreview.Zoom = preview.Zoom;
            CosmeticPreview.LoopDeath = preview.LoopDeath;
            CosmeticPreview.DeathRequest = preview.DeathRequest;

            LauncherHunter.Wanted = true;
            _pickerHost = host; _pickerWindow = window; _pickerDocument = document;
            LauncherHunter.CanPresent = PickerCanPresent;
            LauncherHunter.Hunter = hunter;
            LauncherHunter.Suit = suit;
            LauncherHunter.TransparentBackground = true;
            LauncherHunter.CinematicLighting = true;
            LauncherHunter.DistanceScale = .86f;
            LauncherHunter.SourcePreviewRequest = sourceRequest;
            LauncherHunter.PreviewSlot = previewSlot;
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
            LauncherHunter.SourcePreviewRequest = previousSourceRequest;
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
            if (previewSlot >= 8)
            {
                (LauncherHunter.Wanted, LauncherHunter.CanPresent, LauncherHunter.Hunter, LauncherHunter.Suit,
                    LauncherHunter.TransparentBackground, LauncherHunter.CinematicLighting, LauncherHunter.DistanceScale,
                    LauncherHunter.PreviewSlot, LauncherHunter.Formation, LauncherHunter.Left, LauncherHunter.Top, LauncherHunter.Right, LauncherHunter.Bottom) = previousLauncher;
                (Scene.LauncherPreview, Scene.LauncherPreviewTransparent, Scene.LauncherPreviewCinematicLighting,
                    Scene.LauncherPreviewDistanceScale, Scene.LauncherPreviewSlot, Scene.LauncherPreviewFormation,
                    Scene.LauncherHunter, Scene.LauncherSuit, Scene.PreviewWanted, Scene.PreviewLeft, Scene.PreviewTop, Scene.PreviewRight, Scene.PreviewBottom) = previousScene;
            }
            _pickerHost = null; _pickerWindow = null; _pickerDocument = default;
            GL.ActiveTexture(TextureUnit.Texture0);
            GL.MatrixMode(MatrixMode.Texture); GL.PopMatrix();
            GL.MatrixMode(MatrixMode.Modelview); GL.PopMatrix();
            GL.MatrixMode(MatrixMode.Projection); GL.PopMatrix();
            GL.MatrixMode((MatrixMode)previousMatrixMode);
            OpenTK.Graphics.OpenGL.GL.PopClientAttrib();
            GL.PopAttrib();
            GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, previousDraw);
            GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, previousRead);
            GL.UseProgram(previousProgram);
        }
    }

    // The retained map/preview renderer may leave a private framebuffer,
    // viewport or scissor current. Both chamber composition and the terminal
    // picker return to the full native UI target, including failure paths.
    private static void RestoreCompositorTarget(int width, int height)
    {
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        GL.Viewport(0, 0, width, height);
        GL.Disable(EnableCap.ScissorTest);
        GL.ColorMask(true, true, true, true);
        GL.DepthMask(true);
        GL.UseProgram(0);
        GL.ActiveTexture(TextureUnit.Texture0);
    }

    internal static LauncherHomeStageLayout? ReadHomeLayout(RmlUiHost host,
        RmlUiDocumentToken document, int width, int height)
    {
        if (width <= 0 || height <= 0 || !host.IsVisible(document)
            || !host.TryGetElementBounds(document, "home_left_rail", out float lx, out _, out float lw, out _)
            || !host.TryGetElementBounds(document, "home_right_rail", out float rx, out _, out _, out _)
            || !host.TryGetElementBounds(document, "stage", out _, out float y, out _, out float h)) return null;
        return LauncherHomeStageLayout.TryCreate((lx + lw) / width, rx / width,
            y / height, (y + h) / height, LauncherMenuStage.Current, out var layout) ? layout : null;
    }

    private static void ConfigureHome(RmlUiHunterStagePlan plan, LauncherHomeStageLayout? homeLayout)
    {
        MenuStageProfile stage = LauncherMenuStage.Current;
        float half = (stage.HunterRight - stage.HunterLeft) * .5f;
        LauncherHunter.Wanted = true;
        LauncherHunter.Hunter = plan.HomeHunter;
        LauncherHunter.Suit = plan.HomeSuit;
        LauncherHunter.PreviewSlot = -1;
        LauncherHunter.Formation = false;
        LauncherHunter.Left = homeLayout?.HunterLeft ?? .5f - half;
        LauncherHunter.Top = homeLayout?.HunterTop ?? stage.HunterTop;
        LauncherHunter.Right = homeLayout?.HunterRight ?? .5f + half;
        LauncherHunter.Bottom = homeLayout?.HunterBottom ?? stage.HunterBottom;
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
        LauncherBackdropComposer.HomeLayout = null;
        LauncherStageFx.Enabled = false;
        LauncherLobbyVisuals.Reset();
    }
}
#endif
