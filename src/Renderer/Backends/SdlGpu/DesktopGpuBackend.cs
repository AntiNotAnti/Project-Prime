using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace MphRead;

/// <summary>
/// SDL GPU backend requested for the current desktop process. The value is a
/// request, not a claim about the driver SDL eventually creates; callers must
/// use the device's actual driver for diagnostics.
/// </summary>
public enum DesktopGpuBackend
{
    Auto,
    Direct3D12,
    Vulkan,
    Metal
}

/// <summary>
/// Keeps platform policy and SDL driver spellings in one place. SDL's
/// preferred-driver argument is intentionally resolved before device creation
/// so an explicit request can never silently turn into automatic selection.
/// </summary>
public static class DesktopGpuBackendResolver
{
    public static bool TryParse(string? value, out DesktopGpuBackend backend)
    {
        string? normalized = value?.Trim();
        if (String.Equals(normalized, "auto", StringComparison.OrdinalIgnoreCase)
            || String.Equals(normalized, "automatic", StringComparison.OrdinalIgnoreCase))
        {
            backend = DesktopGpuBackend.Auto;
            return true;
        }
        if (String.Equals(normalized, "d3d12", StringComparison.OrdinalIgnoreCase)
            || String.Equals(normalized, "direct3d12", StringComparison.OrdinalIgnoreCase)
            || String.Equals(normalized, "direct3d-12", StringComparison.OrdinalIgnoreCase))
        {
            backend = DesktopGpuBackend.Direct3D12;
            return true;
        }
        if (String.Equals(normalized, "vulkan", StringComparison.OrdinalIgnoreCase))
        {
            backend = DesktopGpuBackend.Vulkan;
            return true;
        }
        if (String.Equals(normalized, "metal", StringComparison.OrdinalIgnoreCase))
        {
            backend = DesktopGpuBackend.Metal;
            return true;
        }
        backend = DesktopGpuBackend.Auto;
        return false;
    }

    public static DesktopGpuBackend Parse(string? value)
    {
        if (TryParse(value, out DesktopGpuBackend backend)) return backend;
        throw new ArgumentException(
            $"Unknown GPU backend '{value}'. Expected auto, d3d12, or vulkan.",
            nameof(value));
    }

    public static string ToCliValue(DesktopGpuBackend backend) => backend switch
    {
        DesktopGpuBackend.Auto => "auto",
        DesktopGpuBackend.Direct3D12 => "d3d12",
        DesktopGpuBackend.Vulkan => "vulkan",
        DesktopGpuBackend.Metal => "metal",
        _ => throw new ArgumentOutOfRangeException(nameof(backend), backend, null)
    };

    /// <summary>
    /// Resolve the SDL preferred-driver name for a concrete platform. Windows
    /// Auto prefers Vulkan because live comparison on the same workload showed
    /// the SDL D3D12 path to be substantially slower. Linux Auto retains SDL's
    /// normal policy, while macOS Auto is explicitly Metal.
    /// </summary>
    public static string? ResolvePreferredDriver(DesktopGpuBackend backend,
        OSPlatform platform)
    {
        bool windows = platform == OSPlatform.Windows;
        bool macOs = platform == OSPlatform.OSX;
        bool linux = platform == OSPlatform.Linux;

        if (macOs)
        {
            return backend switch
            {
                DesktopGpuBackend.Auto or DesktopGpuBackend.Metal => "metal",
                _ => throw Unsupported(backend, platform,
                    "macOS supports Metal for SDL GPU")
            };
        }
        if (windows)
        {
            return backend switch
            {
                DesktopGpuBackend.Auto => "vulkan",
                DesktopGpuBackend.Direct3D12 => "direct3d12",
                DesktopGpuBackend.Vulkan => "vulkan",
                _ => throw Unsupported(backend, platform,
                    "Windows supports Direct3D 12 or Vulkan")
            };
        }
        if (linux)
        {
            return backend switch
            {
                DesktopGpuBackend.Auto => null,
                DesktopGpuBackend.Vulkan => "vulkan",
                _ => throw Unsupported(backend, platform,
                    "Linux supports automatic selection or explicit Vulkan")
            };
        }

        // Keep unknown desktop hosts conservative: automatic selection is
        // still valid, while an explicit API must not be presented as tested.
        if (backend == DesktopGpuBackend.Auto) return null;
        throw Unsupported(backend, platform, "the platform has no known SDL GPU policy");
    }

    public static string RequestedDriverLabel(DesktopGpuBackend backend)
        => ToCliValue(backend);

    private static PlatformNotSupportedException Unsupported(
        DesktopGpuBackend backend, OSPlatform platform, string policy)
        => new($"GPU backend '{ToCliValue(backend)}' is not supported on "
            + $"'{platform}'. {policy}.");
}

/// <summary>
/// Immutable arguments passed down the SDL host/backend/device ownership
/// chain. GPU debug validation is intentionally independent from managed
/// Debug/Release configuration.
/// </summary>
public readonly record struct SdlGpuDeviceOptions(bool DebugMode,
    string? PreferredDriver)
{
    public static SdlGpuDeviceOptions ForPlatform(OSPlatform platform,
        DesktopGpuBackend backend = DesktopGpuBackend.Auto, bool debugMode = false)
        => new(debugMode, DesktopGpuBackendResolver.ResolvePreferredDriver(backend, platform));

    public static SdlGpuDeviceOptions ForCurrentPlatform(
        DesktopGpuBackend backend = DesktopGpuBackend.Auto, bool debugMode = false)
        => ForPlatform(CurrentPlatform(), backend, debugMode);

    private static OSPlatform CurrentPlatform()
    {
        if (OperatingSystem.IsWindows()) return OSPlatform.Windows;
        if (OperatingSystem.IsMacOS()) return OSPlatform.OSX;
        if (OperatingSystem.IsLinux()) return OSPlatform.Linux;
        return OSPlatform.FreeBSD;
    }
}

/// <summary>One process's renderer startup choices, before SDL reports actual capabilities.</summary>
public readonly record struct SdlGpuRuntimeOptions(
    DesktopGpuBackend RequestedBackend, bool GpuDebug)
{
    public string RequestedDriver
        => DesktopGpuBackendResolver.RequestedDriverLabel(RequestedBackend);

    public SdlGpuDeviceOptions DeviceOptions
        => SdlGpuDeviceOptions.ForCurrentPlatform(RequestedBackend, GpuDebug);
}

/// <summary>
/// Process-scoped command-line state. The last occurrence wins, which makes
/// an explicit command-line value deterministic when a launcher appends its
/// own diagnostic arguments. Persisted launcher settings are applied only when
/// this process has no explicit <c>-gpu</c> switch.
/// </summary>
public static class SdlGpuRuntimeConfiguration
{
    private static DesktopGpuBackend _requestedBackend = DesktopGpuBackend.Auto;
    private static bool _gpuDebug;
    private static bool _backendOverriddenByCli;

    public static SdlGpuRuntimeOptions Current
        => new(_requestedBackend, _gpuDebug);

    public static bool HasCliBackendOverride => _backendOverriddenByCli;

    public static DesktopGpuBackend RequestedBackend => _requestedBackend;
    public static bool GpuDebug => _gpuDebug;

    public static bool ApplyArguments(IEnumerable<string> args, out string? error)
    {
        ArgumentNullException.ThrowIfNull(args);
        error = null;
        List<string> values = args is List<string> list ? list : new List<string>(args);
        for (int index = 0; index < values.Count; index++)
        {
            string raw = values[index];
            if (TryGetSwitch(raw, "gpu", out string? inlineValue))
            {
                string? value = inlineValue;
                if (value == null)
                {
                    if (index + 1 >= values.Count || IsSwitch(values[index + 1]))
                    {
                        error = "-gpu requires auto, d3d12, or vulkan.";
                        return false;
                    }
                    value = values[++index];
                }
                if (!DesktopGpuBackendResolver.TryParse(value,
                    out DesktopGpuBackend backend)
                    || backend == DesktopGpuBackend.Metal)
                {
                    error = $"Unknown GPU backend '{value}'. Expected auto, d3d12, or vulkan.";
                    return false;
                }
                _requestedBackend = backend;
                _backendOverriddenByCli = true;
                continue;
            }
            if (TryGetSwitch(raw, "gpu-debug", out inlineValue))
            {
                string? value = inlineValue;
                if (value == null)
                {
                    // The bare flag is the diagnostic opt-in. Only consume a
                    // following token when it is an accepted explicit value;
                    // positional arguments must remain available to their
                    // own parsers and must not turn the bare flag into an
                    // accidental validation error.
                    if (index + 1 < values.Count
                        && TryParseGpuDebugValue(values[index + 1], out bool explicitDebug))
                    {
                        _gpuDebug = explicitDebug;
                        index++;
                    }
                    else
                    {
                        _gpuDebug = true;
                    }
                }
                else if (!TryParseGpuDebugValue(value, out bool inlineDebug))
                {
                    error = $"Unknown GPU debug value '{value}'. Expected on or off.";
                    return false;
                }
                else
                {
                    _gpuDebug = inlineDebug;
                }
            }
        }
        return true;
    }

    /// <summary>Apply the persisted launcher selection unless -gpu won precedence.</summary>
    public static void ApplyPersistedBackend(string? value)
    {
        if (_backendOverriddenByCli || !DesktopGpuBackendResolver.TryParse(value,
            out DesktopGpuBackend backend) || backend == DesktopGpuBackend.Metal)
        {
            return;
        }
        _requestedBackend = backend;
    }

    public static void ResetForTests()
    {
        _requestedBackend = DesktopGpuBackend.Auto;
        _gpuDebug = false;
        _backendOverriddenByCli = false;
    }

    private static bool TryGetSwitch(string raw, string name, out string? value)
    {
        if (!raw.StartsWith('-'))
        {
            value = null;
            return false;
        }
        string trimmed = raw.TrimStart('-');
        string prefix = name + "=";
        if (trimmed.Equals(name, StringComparison.OrdinalIgnoreCase))
        {
            value = null;
            return true;
        }
        if (trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            value = trimmed[prefix.Length..];
            return true;
        }
        value = null;
        return false;
    }

    private static bool IsSwitch(string value) => value.StartsWith('-');

    private static bool TryParseGpuDebugValue(string value, out bool enabled)
    {
        if (String.Equals(value, "on", StringComparison.OrdinalIgnoreCase)
            || String.Equals(value, "true", StringComparison.OrdinalIgnoreCase))
        {
            enabled = true;
            return true;
        }
        if (String.Equals(value, "off", StringComparison.OrdinalIgnoreCase)
            || String.Equals(value, "false", StringComparison.OrdinalIgnoreCase))
        {
            enabled = false;
            return true;
        }
        enabled = false;
        return false;
    }
}
