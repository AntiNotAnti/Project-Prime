using System;
using MphRead;
using Xunit;

namespace ProjectPrime.Renderer.Tests;

public sealed class DesktopGpuBackendTests
{
    [Fact]
    public void DeviceOptionsDefaultToAutomaticDriverAndDebugOff()
    {
        SdlGpuDeviceOptions options = new();

        Assert.False(options.DebugMode);
        Assert.Null(options.PreferredDriver);
        Assert.False(SdlGpuDeviceOptions.ForPlatform(
            System.Runtime.InteropServices.OSPlatform.Windows).DebugMode);
    }

    [Theory]
    [InlineData("auto", DesktopGpuBackend.Auto)]
    [InlineData("automatic", DesktopGpuBackend.Auto)]
    [InlineData("d3d12", DesktopGpuBackend.Direct3D12)]
    [InlineData("direct3d12", DesktopGpuBackend.Direct3D12)]
    [InlineData("vulkan", DesktopGpuBackend.Vulkan)]
    [InlineData("metal", DesktopGpuBackend.Metal)]
    public void ParsesSupportedBackendSpellings(string value,
        DesktopGpuBackend expected)
    {
        Assert.True(DesktopGpuBackendResolver.TryParse(value, out DesktopGpuBackend actual));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void RejectsUnknownBackendSpelling()
    {
        Assert.False(DesktopGpuBackendResolver.TryParse("dx11",
            out DesktopGpuBackend _));
        Assert.Throws<ArgumentException>(() => DesktopGpuBackendResolver.Parse("dx11"));
    }

    [Fact]
    public void ResolvesAutomaticAndExplicitDriversPerPlatform()
    {
        Assert.Equal("vulkan", DesktopGpuBackendResolver.ResolvePreferredDriver(
            DesktopGpuBackend.Auto, System.Runtime.InteropServices.OSPlatform.Windows));
        Assert.Equal("direct3d12", DesktopGpuBackendResolver.ResolvePreferredDriver(
            DesktopGpuBackend.Direct3D12, System.Runtime.InteropServices.OSPlatform.Windows));
        Assert.Equal("vulkan", DesktopGpuBackendResolver.ResolvePreferredDriver(
            DesktopGpuBackend.Vulkan, System.Runtime.InteropServices.OSPlatform.Windows));

        Assert.Equal("metal", DesktopGpuBackendResolver.ResolvePreferredDriver(
            DesktopGpuBackend.Auto, System.Runtime.InteropServices.OSPlatform.OSX));
        Assert.Equal("metal", DesktopGpuBackendResolver.ResolvePreferredDriver(
            DesktopGpuBackend.Metal, System.Runtime.InteropServices.OSPlatform.OSX));
        Assert.Throws<PlatformNotSupportedException>(()
            => DesktopGpuBackendResolver.ResolvePreferredDriver(
                DesktopGpuBackend.Vulkan, System.Runtime.InteropServices.OSPlatform.OSX));

        Assert.Null(DesktopGpuBackendResolver.ResolvePreferredDriver(
            DesktopGpuBackend.Auto, System.Runtime.InteropServices.OSPlatform.Linux));
        Assert.Equal("vulkan", DesktopGpuBackendResolver.ResolvePreferredDriver(
            DesktopGpuBackend.Vulkan, System.Runtime.InteropServices.OSPlatform.Linux));
        Assert.Throws<PlatformNotSupportedException>(()
            => DesktopGpuBackendResolver.ResolvePreferredDriver(
                DesktopGpuBackend.Direct3D12, System.Runtime.InteropServices.OSPlatform.Linux));
    }

    [Fact]
    public void RuntimeConfigurationDefaultsDebugOffAndCanReset()
    {
        SdlGpuRuntimeConfiguration.ResetForTests();
        try
        {
            Assert.False(SdlGpuRuntimeConfiguration.GpuDebug);
            Assert.Equal(DesktopGpuBackend.Auto,
                SdlGpuRuntimeConfiguration.RequestedBackend);
            Assert.False(SdlGpuRuntimeConfiguration.HasCliBackendOverride);

            Assert.True(SdlGpuRuntimeConfiguration.ApplyArguments(
                new[] { "-gpu", "vulkan", "-gpu-debug", "on" },
                out string? error));
            Assert.Null(error);
            Assert.Equal(DesktopGpuBackend.Vulkan,
                SdlGpuRuntimeConfiguration.RequestedBackend);
            Assert.True(SdlGpuRuntimeConfiguration.GpuDebug);

            SdlGpuRuntimeConfiguration.ResetForTests();
            Assert.Equal(DesktopGpuBackend.Auto,
                SdlGpuRuntimeConfiguration.RequestedBackend);
            Assert.False(SdlGpuRuntimeConfiguration.GpuDebug);
            Assert.False(SdlGpuRuntimeConfiguration.HasCliBackendOverride);
        }
        finally
        {
            SdlGpuRuntimeConfiguration.ResetForTests();
        }
    }

    [Theory]
    [InlineData("on", true)]
    [InlineData("true", true)]
    [InlineData("off", false)]
    [InlineData("false", false)]
    public void ParsesExplicitGpuDebugValues(string value, bool expected)
    {
        SdlGpuRuntimeConfiguration.ResetForTests();
        try
        {
            Assert.True(SdlGpuRuntimeConfiguration.ApplyArguments(
                new[] { "-gpu-debug", value }, out string? error));
            Assert.Null(error);
            Assert.Equal(expected, SdlGpuRuntimeConfiguration.GpuDebug);

            SdlGpuRuntimeConfiguration.ResetForTests();
            Assert.True(SdlGpuRuntimeConfiguration.ApplyArguments(
                new[] { $"-gpu-debug={value}" }, out error));
            Assert.Null(error);
            Assert.Equal(expected, SdlGpuRuntimeConfiguration.GpuDebug);
        }
        finally
        {
            SdlGpuRuntimeConfiguration.ResetForTests();
        }
    }

    [Fact]
    public void BareGpuDebugDoesNotConsumePositionalArguments()
    {
        SdlGpuRuntimeConfiguration.ResetForTests();
        try
        {
            Assert.True(SdlGpuRuntimeConfiguration.ApplyArguments(
                new[] { "-gpu-debug", "room-name", "-gpu", "vulkan" },
                out string? error));
            Assert.Null(error);
            Assert.True(SdlGpuRuntimeConfiguration.GpuDebug);
            Assert.Equal(DesktopGpuBackend.Vulkan,
                SdlGpuRuntimeConfiguration.RequestedBackend);
        }
        finally
        {
            SdlGpuRuntimeConfiguration.ResetForTests();
        }
    }

    [Fact]
    public void InvalidInlineGpuDebugValueIsRejected()
    {
        SdlGpuRuntimeConfiguration.ResetForTests();
        try
        {
            Assert.False(SdlGpuRuntimeConfiguration.ApplyArguments(
                new[] { "-gpu-debug=maybe" }, out string? error));
            Assert.Contains("Unknown GPU debug value", error);
        }
        finally
        {
            SdlGpuRuntimeConfiguration.ResetForTests();
        }
    }

    [Fact]
    public void LastBackendAndDebugOccurrencesWinAndCliBackendPrecedesPersistence()
    {
        SdlGpuRuntimeConfiguration.ResetForTests();
        try
        {
            Assert.True(SdlGpuRuntimeConfiguration.ApplyArguments(
                new[] { "-gpu", "auto", "-gpu=vulkan", "-gpu-debug", "off",
                    "-gpu-debug" }, out string? error));
            Assert.Null(error);
            Assert.Equal(DesktopGpuBackend.Vulkan,
                SdlGpuRuntimeConfiguration.RequestedBackend);
            Assert.True(SdlGpuRuntimeConfiguration.GpuDebug);

            SdlGpuRuntimeConfiguration.ApplyPersistedBackend("d3d12");
            Assert.Equal(DesktopGpuBackend.Vulkan,
                SdlGpuRuntimeConfiguration.RequestedBackend);

            SdlGpuRuntimeConfiguration.ResetForTests();
            SdlGpuRuntimeConfiguration.ApplyPersistedBackend("vulkan");
            Assert.Equal(DesktopGpuBackend.Vulkan,
                SdlGpuRuntimeConfiguration.RequestedBackend);
        }
        finally
        {
            SdlGpuRuntimeConfiguration.ResetForTests();
        }
    }

    [Fact]
    public void RenderBackendInfoCarriesRequestedDriverAndGpuDebugDiagnostics()
    {
        RenderBackendInfo info = new(
            "sdl-gpu", "vulkan", "SPIR-V", "B8G8R8A8_UNORM", "vsync",
            SupportsFinalComposite: true,
            SupportsStaticMeshCache: true,
            SupportsTextureCache: true)
        {
            RequestedDriver = "vulkan",
            GpuDebug = true
        };

        Assert.Equal("vulkan", info.Driver);
        Assert.Equal("vulkan", info.RequestedDriver);
        Assert.True(info.GpuDebug);

        RenderBackendInfo defaults = new(
            "fake", "unknown", "none", "unknown", "vsync", false, false, false);
        Assert.Null(defaults.RequestedDriver);
        Assert.False(defaults.GpuDebug);
    }
}
