#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using MphRead.Mods.Launcher.RmlUi.Render;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace MphRead.Mods.Launcher.Gui;

internal static partial class Shell
{
    private enum NativeShellShotStep
    {
        AwaitStartup,
        CaptureStartup,
        PressStartTab,
        ReleaseStartTab,
        AwaitStartFocus,
        PressStart,
        ReleaseStart,
        AwaitLanding,
        CaptureGameFiles,
        PressSetupBack,
        ReleaseSetupBack,
        AwaitSetupRemains,
        ResizeSetup,
        AwaitSetupResize,
        CaptureSetupResized,
        DismissGameFiles,
        ReleaseGameFilesBack,
        AwaitHome,
        CaptureHome,
        MoveToNews,
        CheckNewsHover,
        PressNews,
        ReleaseNews,
        AwaitNews,
        CaptureNews,
        PressNewsBack,
        ReleaseNewsBack,
        AwaitHomeAfterNews,
        CaptureHomeAfterNews,
        MoveToSettings,
        CheckSettingsHover,
        PressSettings,
        ReleaseSettings,
        AwaitSettings,
        CaptureSettings,
        PressSettingsBack,
        ReleaseSettingsBack,
        AwaitHomeAfterSettings,
        CaptureHomeAfterSettings,
        ResizeHome,
        AwaitResize,
        CaptureResized
    }

    private static NativeShellShotStep _nativeShellShotStep;
    private static int _nativeShellShotFrames, _nativeShellShotWait, _nativeShellShotPhaseFrames;
    private static long _nativeShellShotDeadlineTimestamp;
    private static string _nativeShellShotTarget = "";
    private static double _nativeShellShotPointerX, _nativeShellShotPointerY;
    private static Vector2i _nativeShellShotOriginalSize, _nativeShellShotRequestedSize;

    private static bool UseNativeShellShot
    {
        get
        {
            string[] arguments = Environment.GetCommandLineArgs();
            return RmlUiOpenGlSmoke.NativeUiRequired
                && !arguments.Contains("-samusreturncheck")
                && !arguments.Contains("-shelllifecycle")
                && !arguments.Contains("-shelllifecycleonly");
        }
    }

    private static void ResetNativeShellShot()
    {
        _nativeShellShotStep = NativeShellShotStep.AwaitStartup;
        _nativeShellShotFrames = 0;
        _nativeShellShotWait = 0;
        _nativeShellShotPhaseFrames = 0;
        _nativeShellShotDeadlineTimestamp = 0;
        _nativeShellShotTarget = "";
        _nativeShellShotPointerX = _nativeShellShotPointerY = 0;
        _nativeShellShotOriginalSize = _nativeShellShotRequestedSize = default;
    }

    private static void AfterDrawNativeShellShot(RenderWindow window)
    {
        if (_nativeShellShotDeadlineTimestamp == 0)
            _nativeShellShotDeadlineTimestamp = Stopwatch.GetTimestamp() + 90L * Stopwatch.Frequency;
        if (++_nativeShellShotFrames > 18000
            || Stopwatch.GetTimestamp() >= _nativeShellShotDeadlineTimestamp)
        {
            FailNativeShellShot(window, "native page sequence timed out before completion");
            return;
        }
        if (_nativeShellShotWait > 0)
        {
            _nativeShellShotWait--;
            return;
        }
        if (!RmlUiOpenGlSmoke.TryGetNativeShellShotView(window, out var view))
        {
            RetryNativeShellShot();
            return;
        }

        switch (_nativeShellShotStep)
        {
            case NativeShellShotStep.AwaitStartup:
                if (view.Route == "startup") SetNativeShellShotStep(NativeShellShotStep.CaptureStartup);
                else RetryNativeShellShot();
                return;
            case NativeShellShotStep.CaptureStartup:
                if (CaptureNativeShellShot(window, view, "startup", "shell-native-startup"))
                    SetNativeShellShotStep(NativeShellShotStep.PressStartTab);
                return;
            case NativeShellShotStep.PressStartTab:
                window.FeedKey(KeyEvent(Keys.Tab));
                SetNativeShellShotStep(NativeShellShotStep.ReleaseStartTab);
                RetryNativeShellShot();
                return;
            case NativeShellShotStep.ReleaseStartTab:
                window.FeedKeyUp(KeyEvent(Keys.Tab));
                SetNativeShellShotStep(NativeShellShotStep.AwaitStartFocus);
                RetryNativeShellShot();
                return;
            case NativeShellShotStep.AwaitStartFocus:
                if (view.Route == "startup" && view.FocusedElement == "native_startup_continue")
                    SetNativeShellShotStep(NativeShellShotStep.PressStart);
                else if (++_nativeShellShotPhaseFrames > 120)
                    FailNativeShellShot(window,
                        $"native startup Tab did not focus the Continue button (actual '{view.FocusedElement}')");
                else RetryNativeShellShot();
                return;
            case NativeShellShotStep.PressStart:
                window.FeedKey(KeyEvent(Keys.Enter));
                SetNativeShellShotStep(NativeShellShotStep.ReleaseStart);
                RetryNativeShellShot();
                return;
            case NativeShellShotStep.ReleaseStart:
                window.FeedKeyUp(KeyEvent(Keys.Enter));
                SetNativeShellShotStep(NativeShellShotStep.AwaitLanding);
                return;
            case NativeShellShotStep.AwaitLanding:
                if (view.Route == "game-files")
                {
                    if (!view.GameFilesOwnsInput || view.GameFilesReady != GameFiles.Ready
                        || view.GameFilesReady != view.GameFilesCanBack)
                    {
                        FailNativeShellShot(window,
                            "native Game Files route has inconsistent input ownership or readiness state");
                    }
                    else SetNativeShellShotStep(NativeShellShotStep.CaptureGameFiles);
                }
                else if (view.Route == "home")
                {
                    if (!GameFiles.Ready)
                        FailNativeShellShot(window,
                            "native Home became available before required game files were configured");
                    else SetNativeShellShotStep(NativeShellShotStep.CaptureHome);
                }
                else RetryNativeShellShot();
                return;
            case NativeShellShotStep.CaptureGameFiles:
                if (!view.GameFilesOwnsInput || view.GameFilesReady != GameFiles.Ready
                    || view.GameFilesReady != view.GameFilesCanBack
                    || !HasNativeSetupBounds(view, window))
                {
                    FailNativeShellShot(window,
                        "refused Game Files capture because setup readiness, input ownership, or page bounds changed");
                    return;
                }
                string setupCapture = view.GameFilesReady
                    ? "shell-native-configured-game-files" : "shell-native-assetless-setup";
                if (CaptureNativeShellShot(window, view, "game-files", setupCapture))
                    SetNativeShellShotStep(view.GameFilesReady
                        ? NativeShellShotStep.DismissGameFiles
                        : NativeShellShotStep.PressSetupBack);
                return;
            case NativeShellShotStep.PressSetupBack:
                window.FeedKey(KeyEvent(Keys.Escape));
                SetNativeShellShotStep(NativeShellShotStep.ReleaseSetupBack);
                RetryNativeShellShot();
                return;
            case NativeShellShotStep.ReleaseSetupBack:
                window.FeedKeyUp(KeyEvent(Keys.Escape));
                SetNativeShellShotStep(NativeShellShotStep.AwaitSetupRemains);
                RetryNativeShellShot();
                return;
            case NativeShellShotStep.AwaitSetupRemains:
                if (view.Route == "game-files" && view.GameFilesOwnsInput
                    && !view.GameFilesReady && !view.GameFilesCanBack)
                    SetNativeShellShotStep(NativeShellShotStep.ResizeSetup);
                else if (++_nativeShellShotPhaseFrames > 120)
                    FailNativeShellShot(window,
                        "native Escape bypassed or lost ownership of required Game Files setup");
                else RetryNativeShellShot();
                return;
            case NativeShellShotStep.ResizeSetup:
                _nativeShellShotOriginalSize = window.ClientSize;
                _nativeShellShotRequestedSize = window.ClientSize.X > 1120
                    ? new Vector2i(1100, 740) : new Vector2i(1120, 760);
                window.ClientSize = _nativeShellShotRequestedSize;
                SetNativeShellShotStep(NativeShellShotStep.AwaitSetupResize);
                RetryNativeShellShot();
                return;
            case NativeShellShotStep.AwaitSetupResize:
                if (view.Route == "game-files" && view.GameFilesOwnsInput
                    && !view.GameFilesReady && !view.GameFilesCanBack
                    && window.ClientSize != _nativeShellShotOriginalSize
                    && window.ClientSize == _nativeShellShotRequestedSize
                    && window.ClientSize.X > 0 && window.ClientSize.Y > 0
                    && window.FramebufferSize.X > 0 && window.FramebufferSize.Y > 0
                    && HasNativeSetupBounds(view, window))
                    SetNativeShellShotStep(NativeShellShotStep.CaptureSetupResized);
                else if (++_nativeShellShotPhaseFrames > 120)
                    FailNativeShellShot(window,
                        "the native Game Files page did not retain input ownership in a resized viewport");
                else RetryNativeShellShot();
                return;
            case NativeShellShotStep.CaptureSetupResized:
                if (CaptureNativeShellShot(window, view, "game-files",
                    "shell-native-assetless-setup-resized"))
                    CompleteNativeShellShot(window, "assetless-setup", view,
                        escapeBlocked: true);
                return;
            case NativeShellShotStep.DismissGameFiles:
                window.FeedKey(KeyEvent(Keys.Escape));
                SetNativeShellShotStep(NativeShellShotStep.ReleaseGameFilesBack);
                RetryNativeShellShot();
                return;
            case NativeShellShotStep.ReleaseGameFilesBack:
                window.FeedKeyUp(KeyEvent(Keys.Escape));
                SetNativeShellShotStep(NativeShellShotStep.AwaitHome);
                return;
            case NativeShellShotStep.AwaitHome:
                if (view.Route == "home") SetNativeShellShotStep(NativeShellShotStep.CaptureHome);
                else if (view.Route == "game-files" && ++_nativeShellShotPhaseFrames > 120)
                    FailNativeShellShot(window,
                        "native game-files setup remains open; Home is unavailable until game files are configured");
                else RetryNativeShellShot();
                return;
            case NativeShellShotStep.CaptureHome:
                if (CaptureNativeShellShot(window, view, "home", "shell-native-home"))
                    SetNativeShellShotStep(NativeShellShotStep.MoveToNews);
                return;
            case NativeShellShotStep.MoveToNews:
                MoveToNativeShellShotTarget(window, view, "home_feature_open",
                    view.HasNewsLink, view.NewsX, view.NewsY, view.NewsWidth, view.NewsHeight,
                    NativeShellShotStep.CheckNewsHover);
                return;
            case NativeShellShotStep.CheckNewsHover:
                if (CheckNativeShellShotHover(window, view))
                    SetNativeShellShotStep(NativeShellShotStep.PressNews);
                return;
            case NativeShellShotStep.PressNews:
                if (PressNativeShellShotTarget(window))
                {
                    SetNativeShellShotStep(NativeShellShotStep.ReleaseNews);
                    RetryNativeShellShot();
                }
                return;
            case NativeShellShotStep.ReleaseNews:
                if (ReleaseNativeShellShotTarget(window))
                {
                    SetNativeShellShotStep(NativeShellShotStep.AwaitNews);
                    RetryNativeShellShot();
                }
                return;
            case NativeShellShotStep.AwaitNews:
                if (view.Route == "news") SetNativeShellShotStep(NativeShellShotStep.CaptureNews);
                else if (++_nativeShellShotPhaseFrames > 120)
                    FailNativeShellShot(window, "clicking the native Home dispatch did not open News");
                else RetryNativeShellShot();
                return;
            case NativeShellShotStep.CaptureNews:
                if (CaptureNativeShellShot(window, view, "news", "shell-native-news"))
                    SetNativeShellShotStep(NativeShellShotStep.PressNewsBack);
                return;
            case NativeShellShotStep.PressNewsBack:
                window.FeedKey(KeyEvent(Keys.Escape));
                SetNativeShellShotStep(NativeShellShotStep.ReleaseNewsBack);
                RetryNativeShellShot();
                return;
            case NativeShellShotStep.ReleaseNewsBack:
                window.FeedKeyUp(KeyEvent(Keys.Escape));
                SetNativeShellShotStep(NativeShellShotStep.AwaitHomeAfterNews);
                return;
            case NativeShellShotStep.AwaitHomeAfterNews:
                if (view.Route == "home") SetNativeShellShotStep(NativeShellShotStep.CaptureHomeAfterNews);
                else if (++_nativeShellShotPhaseFrames > 120)
                    FailNativeShellShot(window, "native Escape did not return from News to Home");
                else RetryNativeShellShot();
                return;
            case NativeShellShotStep.CaptureHomeAfterNews:
                if (CaptureNativeShellShot(window, view, "home", "shell-native-home-return"))
                    SetNativeShellShotStep(NativeShellShotStep.MoveToSettings);
                return;
            case NativeShellShotStep.MoveToSettings:
                MoveToNativeShellShotTarget(window, view, "header_settings",
                    view.HasSettingsButton, view.SettingsX, view.SettingsY,
                    view.SettingsWidth, view.SettingsHeight,
                    NativeShellShotStep.CheckSettingsHover);
                return;
            case NativeShellShotStep.CheckSettingsHover:
                if (CheckNativeShellShotHover(window, view))
                    SetNativeShellShotStep(NativeShellShotStep.PressSettings);
                return;
            case NativeShellShotStep.PressSettings:
                if (PressNativeShellShotTarget(window))
                {
                    SetNativeShellShotStep(NativeShellShotStep.ReleaseSettings);
                    RetryNativeShellShot();
                }
                return;
            case NativeShellShotStep.ReleaseSettings:
                if (ReleaseNativeShellShotTarget(window))
                {
                    SetNativeShellShotStep(NativeShellShotStep.AwaitSettings);
                    RetryNativeShellShot();
                }
                return;
            case NativeShellShotStep.AwaitSettings:
                if (view.Route == "settings") SetNativeShellShotStep(NativeShellShotStep.CaptureSettings);
                else if (++_nativeShellShotPhaseFrames > 120)
                    FailNativeShellShot(window, "clicking the native Settings header did not open Settings");
                else RetryNativeShellShot();
                return;
            case NativeShellShotStep.CaptureSettings:
                if (CaptureNativeShellShot(window, view, "settings", "shell-native-settings"))
                    SetNativeShellShotStep(NativeShellShotStep.PressSettingsBack);
                return;
            case NativeShellShotStep.PressSettingsBack:
                window.FeedKey(KeyEvent(Keys.Escape));
                SetNativeShellShotStep(NativeShellShotStep.ReleaseSettingsBack);
                RetryNativeShellShot();
                return;
            case NativeShellShotStep.ReleaseSettingsBack:
                window.FeedKeyUp(KeyEvent(Keys.Escape));
                SetNativeShellShotStep(NativeShellShotStep.AwaitHomeAfterSettings);
                return;
            case NativeShellShotStep.AwaitHomeAfterSettings:
                if (view.Route == "home") SetNativeShellShotStep(NativeShellShotStep.CaptureHomeAfterSettings);
                else if (++_nativeShellShotPhaseFrames > 120)
                    FailNativeShellShot(window, "native Escape did not return from Settings to Home");
                else RetryNativeShellShot();
                return;
            case NativeShellShotStep.CaptureHomeAfterSettings:
                if (CaptureNativeShellShot(window, view, "home", "shell-native-settings-return"))
                    SetNativeShellShotStep(NativeShellShotStep.ResizeHome);
                return;
            case NativeShellShotStep.ResizeHome:
                _nativeShellShotOriginalSize = window.ClientSize;
                _nativeShellShotRequestedSize = window.ClientSize.X > 1120
                    ? new Vector2i(1100, 740) : new Vector2i(1120, 760);
                window.ClientSize = _nativeShellShotRequestedSize;
                SetNativeShellShotStep(NativeShellShotStep.AwaitResize);
                RetryNativeShellShot();
                return;
            case NativeShellShotStep.AwaitResize:
                if (view.Route == "home" && window.ClientSize != _nativeShellShotOriginalSize
                    && window.ClientSize == _nativeShellShotRequestedSize
                    && window.FramebufferSize.X > 0 && window.FramebufferSize.Y > 0)
                    SetNativeShellShotStep(NativeShellShotStep.CaptureResized);
                else if (++_nativeShellShotPhaseFrames > 120)
                    FailNativeShellShot(window, "the native shell did not reach a resized viewport");
                else RetryNativeShellShot();
                return;
            case NativeShellShotStep.CaptureResized:
                if (CaptureNativeShellShot(window, view, "home", "shell-native-resized"))
                    CompleteNativeShellShot(window, "configured-navigation", view,
                        escapeBlocked: false);
                return;
        }
    }

    private static bool CaptureNativeShellShot(RenderWindow window,
        RmlUiOpenGlSmoke.NativeShellShotView view, string expectedRoute, string name)
    {
        if (view.Route != expectedRoute)
        {
            FailNativeShellShot(window,
                $"refused {name} capture because native route was '{view.Route}', expected '{expectedRoute}'");
            return false;
        }
        if (Shot(window, name)) return true;
        ShotFailure = $"could not save native {name} screenshot";
        FinishShotSequence(window, completed: false);
        return false;
    }

    private static void CompleteNativeShellShot(RenderWindow window, string scope,
        RmlUiOpenGlSmoke.NativeShellShotView view, bool escapeBlocked)
    {
        try
        {
            bool assetlessSetup = scope == "assetless-setup";
            if (assetlessSetup && (view.Route != "game-files" || !view.GameFilesOwnsInput
                || view.GameFilesReady || view.GameFilesCanBack
                || _nativeShellShotOriginalSize == window.ClientSize
                || window.ClientSize != _nativeShellShotRequestedSize
                || window.FramebufferSize.X <= 0 || window.FramebufferSize.Y <= 0
                || !HasNativeSetupBounds(view, window)))
                throw new InvalidOperationException("Assetless setup completion lost its readiness, input, or resize witness.");
            if (!assetlessSetup && (scope != "configured-navigation" || view.Route != "home"
                || !GameFiles.Ready || escapeBlocked
                || _nativeShellShotOriginalSize == window.ClientSize
                || window.ClientSize != _nativeShellShotRequestedSize
                || window.FramebufferSize.X <= 0 || window.FramebufferSize.Y <= 0))
                throw new InvalidOperationException("Configured navigation completion lost its Home or readiness witness.");

            string[] claims = assetlessSetup
                ? new[] { "startup", "required Game Files setup", "Escape did not bypass setup", "resized setup viewport" }
                : new[] { "Home", "News", "Settings", "Back navigation", "resized Home viewport" };
            string manifest = JsonSerializer.Serialize(new
            {
                schema = 1,
                kind = "native-shellshot",
                scope,
                outcome = "passed",
                claims,
                gameFilesReady = GameFiles.Ready,
                gameFilesOwnsInput = assetlessSetup ? view.GameFilesOwnsInput : false,
                setupEscapeBlocked = assetlessSetup && escapeBlocked,
                originalClientSize = new { width = _nativeShellShotOriginalSize.X, height = _nativeShellShotOriginalSize.Y },
                requestedClientSize = new { width = _nativeShellShotRequestedSize.X, height = _nativeShellShotRequestedSize.Y },
                observedClientSize = new { width = window.ClientSize.X, height = window.ClientSize.Y },
                observedFramebufferSize = new { width = window.FramebufferSize.X, height = window.FramebufferSize.Y },
                gameFilesChooseButtonBounds = new
                {
                    present = view.HasGameFilesChooseButton,
                    x = view.GameFilesChooseX,
                    y = view.GameFilesChooseY,
                    width = view.GameFilesChooseWidth,
                    height = view.GameFilesChooseHeight
                },
                captures = _shotFiles.Select(Path.GetFileName).ToArray()
            });
            File.WriteAllText(Path.Combine(_shotDirectory!, "shellshot.json"), manifest);
            ShotScope = scope;
            ShotManifestWritten = true;
            Console.WriteLine(assetlessSetup
                ? "[shellshot] scope=assetless-setup; verified startup, required setup ownership, Escape blocking, and resized setup; Home, News, Settings, and gameplay were not exercised"
                : "[shellshot] scope=configured-navigation; verified native Home, News, Settings, Back navigation, and resize");
            FinishShotSequence(window, completed: true);
        }
        catch (Exception ex)
        {
            FailNativeShellShot(window, "could not write a valid native capture scope manifest: " + ex.Message);
        }
    }

    private static bool HasNativeSetupBounds(RmlUiOpenGlSmoke.NativeShellShotView view,
        RenderWindow window)
        => view.HasGameFilesChooseButton && view.GameFilesChooseX >= 0 && view.GameFilesChooseY >= 0
            && view.GameFilesChooseWidth > 0 && view.GameFilesChooseHeight > 0
            && view.GameFilesChooseX + view.GameFilesChooseWidth <= window.FramebufferSize.X
            && view.GameFilesChooseY + view.GameFilesChooseHeight <= window.FramebufferSize.Y;

    private static void MoveToNativeShellShotTarget(RenderWindow window,
        RmlUiOpenGlSmoke.NativeShellShotView view, string target, bool hasBounds,
        float x, float y, float width, float height, NativeShellShotStep hoverStep)
    {
        if (view.Route != "home" || !hasBounds || width <= 0 || height <= 0
            || window.FramebufferSize.X <= 0 || window.FramebufferSize.Y <= 0
            || window.ClientSize.X <= 0 || window.ClientSize.Y <= 0)
        {
            FailNativeShellShot(window, $"native Home element '{target}' has no visible bounds");
            return;
        }
        _nativeShellShotTarget = target;
        _nativeShellShotPointerX = (x + width / 2d) * window.ClientSize.X / window.FramebufferSize.X;
        _nativeShellShotPointerY = (y + height / 2d) * window.ClientSize.Y / window.FramebufferSize.Y;
        if (!RmlUiOpenGlSmoke.PointerMoved(_nativeShellShotPointerX, _nativeShellShotPointerY))
        {
            FailNativeShellShot(window, $"native pointer could not reach '{target}'");
            return;
        }
        SetNativeShellShotStep(hoverStep);
        RetryNativeShellShot();
    }

    private static bool CheckNativeShellShotHover(RenderWindow window,
        RmlUiOpenGlSmoke.NativeShellShotView view)
    {
        if (view.Route != "home")
        {
            FailNativeShellShot(window, $"native route changed before hovering '{_nativeShellShotTarget}'");
            return false;
        }
        if (view.HoveredElement == _nativeShellShotTarget) return true;
        if (++_nativeShellShotPhaseFrames > 60)
            FailNativeShellShot(window,
                $"native pointer did not hover '{_nativeShellShotTarget}' (actual '{view.HoveredElement}')");
        else RetryNativeShellShot();
        return false;
    }

    private static bool PressNativeShellShotTarget(RenderWindow window)
    {
        if (RmlUiOpenGlSmoke.PointerButton(MouseButton.Button1,
            _nativeShellShotPointerX, _nativeShellShotPointerY, down: true)) return true;
        FailNativeShellShot(window, $"native pointer press was rejected for '{_nativeShellShotTarget}'");
        return false;
    }

    private static bool ReleaseNativeShellShotTarget(RenderWindow window)
    {
        if (RmlUiOpenGlSmoke.PointerButton(MouseButton.Button1,
            _nativeShellShotPointerX, _nativeShellShotPointerY, down: false)) return true;
        FailNativeShellShot(window, $"native pointer release was rejected for '{_nativeShellShotTarget}'");
        return false;
    }

    private static KeyboardKeyEventArgs KeyEvent(Keys key)
        => new(key, scanCode: GLFW.GetKeyScancode(key), 0, isRepeat: false);

    private static void SetNativeShellShotStep(NativeShellShotStep step)
    {
        _nativeShellShotStep = step;
        _nativeShellShotPhaseFrames = 0;
        _nativeShellShotWait = 0;
    }

    private static void RetryNativeShellShot() => _nativeShellShotWait = 1;

    private static void FailNativeShellShot(RenderWindow window, string message)
    {
        ShotMisses++;
        ShotFailure = message;
        Console.WriteLine("[shellshot] " + message);
        FinishShotSequence(window, completed: false);
    }
}
#endif
