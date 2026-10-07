#if !ANDROID && !MPHREAD_SERVER
using System;
using System.Runtime.InteropServices;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace MphRead.Mods.Render;

// Acceptance diagnostics only. Production context creation/fallback keeps its
// original behavior, including propagation of an unavailable legacy context.
internal static class HostedLegacyGlCapabilityCheck
{
    internal static NativeWindow? CreateWindow(NativeWindowSettings settings,
        string? recordedAdapter = null)
    {
        ErrorCode? creationError = null;
        string? creationDescription = null;
        GLFWCallbacks.ErrorCallback creationCallback = (code, description) =>
        {
            creationError = code;
            creationDescription = description;
            // Capture GLFW's typed failure without unwinding its native frames.
            try { Console.Error.WriteLine($"[window] GLFW {code}: {description}"); }
            catch { }
        };
        GLFW.SetErrorCallback(creationCallback);
        try
        {
            return new NativeWindow(settings);
        }
        catch (InvalidOperationException exception) when (
            IsExactConstructorFailure(creationError, creationDescription, exception.Message)
            && IsHostedDiagnosticEnabled()
            && MeasuredLegacyFormatUnavailable(recordedAdapter))
        {
            return null;
        }
        finally
        {
            DesktopGlContext.InstallErrorCallback();
            GC.KeepAlive(creationCallback);
        }
    }

    internal static bool IsExactConstructorFailure(ErrorCode? error,
        string? description, string managedMessage) =>
        error == ErrorCode.FormatUnavailable
        && description == "NSGL: Failed to find a suitable pixel format"
        && managedMessage == "GLFW Format unavailable: NSGL: Failed to find a suitable pixel format";

    private static bool IsHostedDiagnosticEnabled() => HostedDiagnosticEnabled(
        Environment.GetEnvironmentVariable("PRIME_ACCEPTANCE_HOSTED_NSGL_UNAVAILABLE"),
        Environment.GetEnvironmentVariable("GITHUB_ACTIONS"), OperatingSystem.IsMacOS(),
        RuntimeInformation.ProcessArchitecture);

    internal static bool HostedDiagnosticEnabled(string? optIn, string? githubActions,
        bool macOS, Architecture architecture) => optIn == "1" && githubActions == "true"
        && macOS && (architecture == Architecture.Arm64 || architecture == Architecture.X64);

    private static bool MeasuredLegacyFormatUnavailable(string? recordedAdapter)
    {
        if (recordedAdapter == null)
        {
            // The standalone GL reference has no former modern owner. Query a
            // real Metal adapter after the exact constructor failure, and own
            // its disposal; architecture alone cannot establish this capability.
            using var device = ModernGraphicsDevice.Create(GraphicsBackend.Metal);
            recordedAdapter = device.AdapterName;
            Console.WriteLine($"[legacyglcheck] measured backend=Metal adapter=\"{recordedAdapter}\" architecture={RuntimeInformation.ProcessArchitecture}");
        }
        if (recordedAdapter != "Apple Paravirtual device") return false;

        // Independently exclude profile, offline-renderer and restrictive
        // color/depth/stencil hints as explanations for GLFW's failure.
        int[] standard = { 99, 0x1000, 73, 74, 5, 8, 24, 11, 8, 12, 24, 13, 8, 0 };
        int[] offline = { 99, 0x1000, 73, 74, 96, 101, 5, 8, 24, 11, 8, 12, 24, 13, 8, 0 };
        int[] minimal = { 99, 0x1000, 73, 74, 96, 101, 0 };
        int[] defaultProfile = { 73, 74, 96, 101, 0 };
        bool standardAbsent = LegacyFormatAbsent("standard", standard);
        bool offlineAbsent = LegacyFormatAbsent("offline", offline);
        bool minimalAbsent = LegacyFormatAbsent("minimal-offline", minimal);
        bool defaultAbsent = LegacyFormatAbsent("minimal-offline-default-profile", defaultProfile);
        return standardAbsent && offlineAbsent && minimalAbsent && defaultAbsent;
    }

    private static bool LegacyFormatAbsent(string request, int[] attributes)
    {
        nint format = 0;
        try
        {
            int error = CGLChoosePixelFormat(attributes, out format, out int count);
            bool absent = KnownFormatAbsent(error, format, count);
            Console.WriteLine($"[legacyglcheck] CGL legacy format request={request} error={error} count={count} handle={(format != 0 ? "present" : "none")} unavailable={absent}");
            return absent;
        }
        finally
        {
            if (format != 0)
            {
                int releaseError = CGLDestroyPixelFormat(format);
                if (releaseError != 0)
                    throw new InvalidOperationException($"CGL format census release failed: {releaseError}.");
            }
        }
    }

    internal static bool KnownFormatAbsent(int error, nint format, int count) =>
        (error == 0 || error == 10002) && format == 0 && count == 0;

    [DllImport("/System/Library/Frameworks/OpenGL.framework/OpenGL")]
    private static extern int CGLChoosePixelFormat(int[] attributes, out nint format, out int count);

    [DllImport("/System/Library/Frameworks/OpenGL.framework/OpenGL")]
    private static extern int CGLDestroyPixelFormat(nint format);
}
#endif
