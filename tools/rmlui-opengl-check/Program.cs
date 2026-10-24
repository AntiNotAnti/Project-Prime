using System;
using System.Globalization;
using System.IO;
using MphRead.Mods.Render;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Render;
using OpenTK.Graphics.OpenGL;
using OpenTK.Windowing.Desktop;

static void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException(name);
    Console.WriteLine("RMLUI GL PASS " + name);
}

if (args.Length == 1 && args[0] == "--workspace-evidence")
{
    Console.WriteLine($"PASS {EditorGlAcceptance.CheckWorkspaceEvidence()} headless workspace diagnostic checks; no native host or GL");
    return 0;
}

if (args.Length == 2 && args[0] == "--lobby-geometry")
    return LobbyGeometryAcceptance.Run(args[1]);

if (args.Length == 2 && args[0] == "--hunter-presenter-contract")
    return HunterPresenterContractAcceptance.Run(args[1]);

if (args.Length == 1 && args[0] == "--map-registry-publication")
    return MapRegistryPublicationAcceptance.Run();

if (args.Length == 1 && args[0] == "--detached-ticket")
    return DetachedExportTicketAcceptance.Run();

if (args.Length == 2 && args[0] == "--license-star-art")
    return LicenseStarArtAcceptance.Run(args[1]);

if (args.Length == 2 && args[0] == "--license-portrait")
    return LicensePortraitAcceptance.Run(args[1]);

if (args.Length == 2 && args[0] == "--map-picker-previews")
    return MapPickerPreviewsAcceptance.Run(args[1]);

if (args.Length == 2 && args[0] == "--community-import-file")
    return ActualCommunityImportAcceptance.Run(args[1]);

if (args.Length == 2 && args[0] == "--community-host-file")
    return ActualCommunityHostingAcceptance.Run(args[1]);

if (args.Length == 2 && args[0] == "--community-preview")
    return CommunityPreviewAcceptance.Run(args[1]);

if (args.Length == 3 && args[0] == "--thumbnail-room-owner")
    return ThumbnailRoomOwnerAcceptance.Run(args);

if (args.Length == 2 && args[0] == "--map-preview-source")
    return MapPreviewSourceAcceptance.Run(args[1]);

if (args.Length > 0 && args[0] is "--replay-window" or "--replay-reopen")
    return ReplayWindowAcceptance.Run(args);

if(args.Length>0&&args[0]=="--settings-live-window")
    return NativeOwnedSettingsAcceptance.Run(args);

if (args.Length == 1 && args[0] == "--replay-files")
{
    ReplayFileAcceptance.Run();
    return 0;
}

if (args.Length == 1 && args[0] == "--studio-owner")
{
    StudioEditorOwnerAcceptance.Run();
    return 0;
}

if (args.Length is < 1 or > 3 || (args.Length >= 2 && args[1] is not ("--native-settings-popups" or "--menu-environment" or "--canvas" or "--startup" or "--random-match" or "--license" or "--license-card" or "--offline-rules" or "--offline-character" or "--ingame" or "--party" or "--social-invites" or "--input" or "--replay-workspace" or "--theatre-library" or "--playback" or "--map-preview" or "--campaign-ending" or "--campaign-ship" or "--native-hunters" or "--map-picker" or "--lobby-interactions" or "--community-import" or "--community-host-gl" or "--community-report" or "--community-library" or "--community-history" or "--community-transfers" or "--community-publish" or "--studio-workspace" or "--studio-editor" or "--studio-build" or "--party-queue" or "--adventure-saves" or "--adventure-save-work" or "--lobby-restoration" or "--lobby-countdown"))
    || !File.Exists(Path.Combine(args[0], "prime_home.rml")))
    throw new ArgumentException("Pass --replay-files or --studio-owner for headless acceptance, or the RmlUi asset directory containing prime_home.rml, optionally followed by --menu-environment, --startup, --random-match, --license, --license-card, --offline-rules, --ingame, --party, --social-invites, --input, --theatre-library, --replay-workspace, --playback, --map-preview, --campaign-ending, --campaign-ship, --native-hunters, --map-picker, --lobby-interactions, --community-import, --community-host-gl, --community-report, --community-library, --community-history, --community-transfers, --community-publish, --studio-workspace or --studio-editor or --studio-build, --party-queue or --adventure-saves, --adventure-save-work or --lobby-restoration or --offline-character [WIDTHxHEIGHT].");

int requestedWidth = 960, requestedHeight = 600;
if (args.Length == 3)
{
    string[] dimensions = args[2].Split('x');
    if (dimensions.Length != 2
        || !int.TryParse(dimensions[0], NumberStyles.None, CultureInfo.InvariantCulture, out requestedWidth)
        || !int.TryParse(dimensions[1], NumberStyles.None, CultureInfo.InvariantCulture, out requestedHeight)
        || requestedWidth is < 64 or > 4096 || requestedHeight is < 64 or > 4096)
        throw new ArgumentException("Framebuffer size must be WIDTHxHEIGHT, with each dimension between 64 and 4096.");
}

if (args.Length >= 2 && args[1] == "--offline-character")
    return OfflineCharacterGlAcceptance.Run(args[0], requestedWidth, requestedHeight);

if (args.Length >= 2 && args[1] == "--native-hunters")
    return NativeHunterStageGlAcceptance.Run(args[0], requestedWidth, requestedHeight);

if (args.Length >= 2 && args[1] == "--lobby-countdown")
    return LobbyCountdownGlAcceptance.RunWindow(args[0], requestedWidth, requestedHeight);

if (args.Length >= 2 && args[1] == "--lobby-restoration")
    return LobbyRestorationGlAcceptance.RunWindow(args[0], requestedWidth, requestedHeight);

Environment.SetEnvironmentVariable("PROJECT_PRIME_GL_PROFILE", "1");
GraphicsBackendPolicy.Configure("opengl");
var settings = DesktopGlContext.Settings(background: true);
settings.ClientSize = new(requestedWidth, requestedHeight);
using var window = new NativeWindow(settings);
using var context = new DesktopGraphicsSession(window);
int width = window.FramebufferSize.X;
int height = window.FramebufferSize.Y;
Check(width > 0 && height > 0, "GL framebuffer created");
Console.WriteLine($"RMLUI GL DIMENSIONS requestedClient={requestedWidth}x{requestedHeight} framebuffer={width}x{height}");
float density = 1;
string? requestedDensity = Environment.GetEnvironmentVariable("PROJECT_PRIME_RMLUI_CHECK_DENSITY");
if (requestedDensity != null && (!float.TryParse(requestedDensity, NumberStyles.Float, CultureInfo.InvariantCulture, out density)
    || !float.IsFinite(density) || density is < 0.5f or > 4))
    throw new ArgumentException("PROJECT_PRIME_RMLUI_CHECK_DENSITY must be between 0.5 and 4.");
Console.WriteLine($"RMLUI GL DENSITY {density.ToString(CultureInfo.InvariantCulture)}");

using var host = new RmlUiHost();
Check(host.Initialize(width, height, density, Path.GetFullPath(args[0]), RmlUiRenderBackend.OpenGl),
    "native GL2 bridge and document initialize");

if (args.Length >= 2 && args[1] == "--native-settings-popups")
{
    NativeSettingsPopupGlAcceptance.Run(host, width, height);
    return 0;
}

if (args.Length >= 2 && args[1] == "--menu-environment")
{
    MenuEnvironmentGlAcceptance.Run(host, width, height);
    return 0;
}

if (args.Length >= 2 && args[1] == "--canvas")
{
    CanvasGlAcceptance.Run(host, width, height, Path.GetFullPath(args[0]));
    return 0;
}

if (args.Length >= 2 && args[1] == "--startup")
{
    StartupGlAcceptance.Run(host, width, height);
    return 0;
}

// The bridge's compatibility Home has an authored arrival animation. Let its
// first layout and animation settle before measuring painted pixels; otherwise
// fast initialization can capture the deliberately transparent opening frame.
var arrival = System.Diagnostics.Stopwatch.StartNew();
do
{
    host.Update();
    System.Threading.Thread.Sleep(5);
}
while (arrival.Elapsed < TimeSpan.FromMilliseconds(220));

GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
GL.Viewport(0, 0, width, height);
GL.Disable(EnableCap.ScissorTest);
GL.ClearColor(0.2f, 0.3f, 0.4f, 1f);
GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit | ClearBufferMask.StencilBufferBit);

// Deliberately leave nondefault state for RmlUi to restore.
GL.Enable(EnableCap.DepthTest);
GL.DepthMask(true);
GL.Enable(EnableCap.CullFace);
GL.Enable(EnableCap.ScissorTest);
GL.Scissor(9, 11, width - 18, height - 22);
GL.Viewport(3, 5, width - 6, height - 10);
GL.Disable(EnableCap.Blend);
GL.BlendFunc(BlendingFactor.One, BlendingFactor.Zero);
GL.ActiveTexture(TextureUnit.Texture1);
int texture = GL.GenTexture();
GL.BindTexture(TextureTarget.Texture2D, texture);
int readFbo = GL.GenFramebuffer();
GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, readFbo);
GL.MatrixMode(MatrixMode.Projection);
GL.PushMatrix();
GL.LoadIdentity();
GL.Translate(0.25f, 0.5f, 0);
int matrix = GL.GetInteger(GetPName.MatrixMode);
int active = GL.GetInteger(GetPName.ActiveTexture);
int program = GL.GetInteger(GetPName.CurrentProgram);
int draw = GL.GetInteger(GetPName.DrawFramebufferBinding);
int read = GL.GetInteger(GetPName.ReadFramebufferBinding);
int boundTexture = GL.GetInteger(GetPName.TextureBinding2D);
var viewport = new int[4];
GL.GetInteger(GetPName.Viewport, viewport);

host.Update();
RmlUiOpenGlFrame.Draw(host, width, height);
Check(GL.GetInteger(GetPName.ActiveTexture) == active
    && GL.GetInteger(GetPName.TextureBinding2D) == boundTexture
    && GL.GetInteger(GetPName.CurrentProgram) == program,
    "program, active texture and texture binding restored");
Check(GL.GetInteger(GetPName.DrawFramebufferBinding) == draw
    && GL.GetInteger(GetPName.ReadFramebufferBinding) == read,
    "read/draw framebuffer bindings restored");
Check(GL.GetInteger(GetPName.MatrixMode) == matrix
    && GL.IsEnabled(EnableCap.DepthTest)
    && GL.IsEnabled(EnableCap.CullFace)
    && GL.IsEnabled(EnableCap.ScissorTest)
    && !GL.IsEnabled(EnableCap.Blend)
    && GL.GetInteger(GetPName.DepthWritemask) != 0,
    "fixed-function matrix mode and depth/cull/scissor/blend states restored");
int[] afterViewport = new int[4];
GL.GetInteger(GetPName.Viewport, afterViewport);
Check(viewport.AsSpan().SequenceEqual(afterViewport), "viewport restored");

// A clean read FBO is necessary for readback; the GL2 render was drawn to
// default output even though a different READ framebuffer was bound at entry.
GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, 0);
GL.Disable(EnableCap.ScissorTest);
var picture = FinalCompositeCapture.Read(width, height);
Check(picture.Length == width * height * 3, "framebuffer readback dimensions");
// At least part of the native Home must differ from the pre-rendered clear.
// Pixel formats are RGB, captured from the actual final framebuffer.
int changed = 0;
for (int i = 0; i < picture.Length; i += 3)
{
    if (Math.Abs(picture[i] - 51) > 24
        || Math.Abs(picture[i + 1] - 77) > 24
        || Math.Abs(picture[i + 2] - 102) > 24) changed++;
}
Check(changed > width * height / 300, "native RmlUi painted actual Home pixels");
Check(GL.GetError() == ErrorCode.NoError, "no OpenGL errors after native frame");
if (args.Length >= 2 && args[1] == "--license") LicenseAcceptance.Run(host, width, height);
if (args.Length >= 2 && args[1] == "--random-match") RandomMatchGlAcceptance.Run(host, width, height);
if (args.Length >= 2 && args[1] == "--license-card") LicenseCardGlAcceptance.Run(host, width, height);
if (args.Length >= 2 && args[1] == "--offline-rules") OfflineRulesGlAcceptance.Run(host, width, height);
if (args.Length >= 2 && args[1] == "--ingame") InGameGlAcceptance.Run(host, width, height);
if (args.Length >= 2 && args[1] == "--party") PartyGlAcceptance.Run(host, width, height);
if (args.Length >= 2 && args[1] == "--social-invites") SocialGlAcceptance.Run(host, width, height, window.ClientSize.X, window.ClientSize.Y);
if (args.Length >= 2 && args[1] == "--input") InputGlAcceptance.Run(host, width, height, args[0]);
if (args.Length >= 2 && args[1] == "--theatre-library") TheatreLibraryGlAcceptance.Run(host, width, height);
if (args.Length >= 2 && args[1] == "--playback") PlaybackAcceptance.Run(host, width, height);
if (args.Length >= 2 && args[1] == "--map-preview") MapPreviewAcceptance.Run(host, width, height);
if (args.Length >= 2 && args[1] == "--campaign-ending") EndingGlAcceptance.Run(host, width, height);
if (args.Length >= 2 && args[1] == "--campaign-ship") ShipGlAcceptance.Run(host, width, height);
if (args.Length >= 2 && args[1] == "--map-picker") MapPickerGlAcceptance.Run(host, width, height);
if (args.Length >= 2 && args[1] == "--lobby-interactions") LobbyInteractionsGlAcceptance.Run(host, width, height);
if (args.Length >= 2 && args[1] == "--community-host-gl") CommunityHostingGlAcceptance.Run(host, width, height);
if (args.Length >= 2 && args[1] == "--community-import") CommunityImportGlAcceptance.Run(host, width, height);
if (args.Length >= 2 && args[1] == "--community-report") CommunityReportGlAcceptance.Run(host, width, height);
if (args.Length >= 2 && args[1] == "--community-library") CommunityLibraryGlAcceptance.Run(host, width, height);
if (args.Length >= 2 && args[1] == "--community-history") CommunityHistoryGlAcceptance.Run(host, width, height);
if (args.Length >= 2 && args[1] == "--community-transfers") CommunityTransfersGlAcceptance.Run(host, width, height, window.ClientSize.X, window.ClientSize.Y);
if (args.Length >= 2 && args[1] == "--community-publish") CommunityPublishGlAcceptance.Run(host, width, height);
if (args.Length >= 2 && args[1] == "--replay-workspace") EditorGlAcceptance.RunWorkspace(host, width, height,density);
if (args.Length >= 2 && args[1] == "--studio-workspace") { StudioGlAcceptance.Run(host, width, height); EditorGlAcceptance.RunWorkspace(host, width, height,density); }
if (args.Length >= 2 && args[1] == "--studio-build") StudioBuildGlAcceptance.Run(host, width, height);
if (args.Length >= 2 && args[1] == "--studio-editor") StudioEditorGlAcceptance.Run(host, width, height);
if (args.Length >= 2 && args[1] == "--party-queue") PartyQueueGlAcceptance.Run(host, width, height);
if (args.Length >= 2 && args[1] == "--adventure-saves") AdventureSaveGlAcceptance.Run(host, width, height);
if (args.Length >= 2 && args[1] == "--adventure-save-work") AdventureSaveAsyncGlAcceptance.Run(host, width, height);
host.Shutdown();
GL.MatrixMode(MatrixMode.Projection);
GL.PopMatrix();
GL.MatrixMode(MatrixMode.Modelview);
GL.DeleteTexture(texture);
GL.DeleteFramebuffer(readFbo);
Console.WriteLine("RMLUI GL2 native-context acceptance PASS");
return 0;
