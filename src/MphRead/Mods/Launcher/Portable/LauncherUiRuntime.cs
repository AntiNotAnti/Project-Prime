#if MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Render;

namespace MphRead.Mods.Launcher;

/// <summary>Compile availability and accepted-platform policy for the current desktop process.</summary>
public static class LauncherUiRuntime
{
    // Applied only with the RML-18 CI/performance-approved cutover candidate.
    // Unsupported runtime identifiers require an explicit trial or compatibility package.
    private static readonly string[] AcceptedRuntimeIdentifiers = ["win-x64", "linux-x64", "osx-arm64", "osx-x64"];
    private static LauncherUiSelectionPolicy? _policy;
    private static LauncherUiDecision? _decision;
    private static LauncherUiSelectionPolicy Policy => _policy ??= new(
#if MPHREAD_RMLUI_POC || MPHREAD_RMLUI
        nativeAvailable: true,
#else
        nativeAvailable: false,
#endif
#if MPHREAD_AVALONIA
        legacyAvailable: true,
#else
        legacyAvailable: false,
#endif
        rid: RuntimeInformation.RuntimeIdentifier, acceptedRids: AcceptedRuntimeIdentifiers,
        statePath: Path.Combine(LauncherPrefs.Directory, "ui-startup-" + RuntimeInformation.RuntimeIdentifier + ".json"),
        build: Mods.Update.BuildVersion.Display, persistenceEnabled: !DiagnosticRun);
    private static bool DiagnosticRun => LauncherUiPerformance.Enabled
#if MPHREAD_RMLUI_POC || MPHREAD_RMLUI
        || Gui.RmlUiPrototype.CaptureRequested
#endif
        ;
    public static LauncherUiDecision Decision => _decision ??= Policy.Resolve(Environment.GetCommandLineArgs());
    public static bool UseNative => Decision.Selected == LauncherUiMode.RmlUi;
    public static void BeginNativeAttempt() { if (!DiagnosticRun) Policy.BeginNativeAttempt(); }
    public static void RecordNativeFailure(LauncherUiFailure failure) { if (!DiagnosticRun) Policy.RecordNativeFailure(failure); }
    public static void CompleteCleanShutdown() { if (!DiagnosticRun) Policy.CompleteCleanShutdown(); }
    public static void ObservePresented(bool native, string actualRenderer)
    { if (!DiagnosticRun) Policy.ObservePresented(native ? LauncherUiMode.RmlUi : LauncherUiMode.Legacy, actualRenderer); }

    public static void ApplyAutomaticRendererRollback()
    {
        var decision = Decision; // Validate explicit choices before opening a window.
        if (decision.RendererRollback is not { } renderer) return;
        string[] arguments = Environment.GetCommandLineArgs();
        if (arguments.Any(arg => arg.Equals("-renderer", StringComparison.OrdinalIgnoreCase)
            || arg.Equals("--renderer", StringComparison.OrdinalIgnoreCase) || arg.StartsWith("-renderer=", StringComparison.OrdinalIgnoreCase)
            || arg.StartsWith("--renderer=", StringComparison.OrdinalIgnoreCase))) return;
        GraphicsBackendPolicy.Configure(renderer);
    }
}
#endif
