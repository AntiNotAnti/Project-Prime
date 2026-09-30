#if !MPHREAD_SERVER
using System;
using System.IO;
using System.Runtime.InteropServices;
using Silk.NET.Core.Native;
using Silk.NET.WebGPU;
using Silk.NET.WebGPU.Extensions.WGPU;
#if !ANDROID
using OpenTK.Windowing.Desktop;
#endif

namespace MphRead.Mods.Render
{
    /// <summary>
    /// Owns a wgpu-native instance/adapter/device selected through the Project
    /// Prime backend policy. The gameplay renderer still uses the legacy GL
    /// facade while it is being ported; this object is the native-device seam
    /// the compatibility renderer will consume.
    /// </summary>
    public sealed unsafe class ModernGraphicsDevice : IDisposable
    {
        private static readonly object _requestLock = new();
        private static Adapter* _requestedAdapter;
        private static Device* _requestedDevice;
        private static string? _requestError;
        private static nint _macVulkanLoader;

        private readonly WebGPU _api;
        private readonly Wgpu _native;
        private Instance* _instance;
        private Adapter* _adapter;
        private Device* _device;
        private Surface* _surface;
        private bool _disposed;

        private unsafe delegate Surface* SurfaceFactory(WebGPU api, Instance* instance);

        private ModernGraphicsDevice(WebGPU api, Wgpu native, Instance* instance, Adapter* adapter, Device* device,
            Surface* surface, GraphicsBackend backend, uint nativeVersion, string adapterName, string driverDescription)
        {
            _api = api;
            _native = native;
            _instance = instance;
            _adapter = adapter;
            _device = device;
            _surface = surface;
            Backend = backend;
            NativeVersion = nativeVersion;
            AdapterName = adapterName;
            DriverDescription = driverDescription;
        }

        public GraphicsBackend Backend { get; }
        public uint NativeVersion { get; }
        public string AdapterName { get; }
        public string DriverDescription { get; }

        internal WebGPU Api => _api;
        internal Wgpu Native => _native;
        internal Adapter* Adapter => _adapter;
        internal Device* Device => _device;
        internal Surface* Surface => _surface;

        public static ModernGraphicsDevice Create(GraphicsBackend requested = GraphicsBackend.Auto)
        {
            return CreateCore(requested, null);
        }

#if !ANDROID
        internal static ModernGraphicsDevice CreateForWindow(NativeWindow window,
            GraphicsBackend requested = GraphicsBackend.Auto)
        {
            return CreateCore(requested, (api, instance) =>
                ModernGraphicsSurface.Create(window, api, instance));
        }
#endif

        private static ModernGraphicsDevice CreateCore(GraphicsBackend requested, SurfaceFactory? surfaceFactory)
        {
            GraphicsPlatform platform = GraphicsBackendPolicy.CurrentPlatform;
            GraphicsBackend backend = GraphicsBackendPolicy.Resolve(platform, requested);
            if (!GraphicsBackendPolicy.IsModern(backend))
            {
                throw new InvalidOperationException(
                    $"{GraphicsBackendPolicy.DisplayName(backend)} is the legacy renderer, not a wgpu-native backend.");
            }

            lock (_requestLock)
            {
                if (platform == GraphicsPlatform.MacOS && backend == GraphicsBackend.Vulkan)
                {
                    PrepareMoltenVK();
                }

                WebGPU api = WebGPU.GetApi();
                Wgpu? native = null;
                Instance* instance = null;
                Adapter* adapter = null;
                Device* device = null;
                Surface* surface = null;
                try
                {
                    if (!api.TryGetDeviceExtension(null, out Wgpu nativeExtension))
                    {
                        throw new DllNotFoundException(
                            "Silk.NET loaded WebGPU without the wgpu-native extension.");
                    }
                    native = nativeExtension;

                    InstanceExtras extras = default;
                    extras.Chain.SType = (SType)NativeSType.STypeInstanceExtras;
                    extras.Chain.Next = null;
                    extras.Backends = ToInstanceBackend(backend);

                    InstanceDescriptor instanceDescriptor = default;
                    instanceDescriptor.NextInChain = (ChainedStruct*)&extras;
                    instance = api.CreateInstance(&instanceDescriptor);
                    if (instance == null)
                    {
                        throw new InvalidOperationException(
                            $"wgpu-native could not create a {GraphicsBackendPolicy.DisplayName(backend)} instance.");
                    }

                    if (surfaceFactory != null)
                    {
                        surface = surfaceFactory(api, instance);
                    }

                    RequestAdapterOptions adapterOptions = default;
                    adapterOptions.CompatibleSurface = surface;
                    // wgpu-native 33133da rejects backendType here and explicitly
                    // asks callers to restrict backends through InstanceExtras.
                    // The instance above is already DX12/Vulkan/Metal-only.
                    adapterOptions.PowerPreference = PowerPreference.HighPerformance;

                    _requestedAdapter = null;
                    _requestError = null;
                    api.InstanceRequestAdapter(instance, &adapterOptions,
                        new PfnRequestAdapterCallback(OnAdapterRequested), null);
                    PumpCallbacks(api, instance, () => _requestedAdapter != null || _requestError != null);
                    adapter = _requestedAdapter;
                    if (adapter == null)
                    {
                        throw new InvalidOperationException(
                            $"No {GraphicsBackendPolicy.DisplayName(backend)} adapter was available"
                            + (string.IsNullOrWhiteSpace(_requestError) ? "." : $": {_requestError}"));
                    }

                    _requestedDevice = null;
                    _requestError = null;
                    api.AdapterRequestDevice(adapter, null,
                        new PfnRequestDeviceCallback(OnDeviceRequested), null);
                    PumpCallbacks(api, instance, () => _requestedDevice != null || _requestError != null);
                    device = _requestedDevice;
                    if (device == null)
                    {
                        throw new InvalidOperationException(
                            $"The {GraphicsBackendPolicy.DisplayName(backend)} adapter could not create a device"
                            + (string.IsNullOrWhiteSpace(_requestError) ? "." : $": {_requestError}"));
                    }

                    AdapterProperties properties = default;
                    api.AdapterGetProperties(adapter, &properties);
                    if (properties.BackendType != ToBackendType(backend))
                    {
                        throw new InvalidOperationException(
                            $"Requested {GraphicsBackendPolicy.DisplayName(backend)}, but wgpu-native selected "
                            + $"{properties.BackendType}.");
                    }

                    string name = PtrString(properties.Name, "Unknown GPU");
                    string driver = PtrString(properties.DriverDescription, "Unknown driver");
                    return new ModernGraphicsDevice(api, native, instance, adapter, device, surface, backend,
                        native.GetVersion(), name, driver);
                }
                catch
                {
                    if (device != null) api.DeviceRelease(device);
                    if (adapter != null) api.AdapterRelease(adapter);
                    if (surface != null) api.SurfaceRelease(surface);
                    if (instance != null) api.InstanceRelease(instance);
                    // The Wgpu extension shares WebGPU's native context. Disposing
                    // both wrappers double-disposes that context on Vulkan/Unix.
                    api.Dispose();
                    throw;
                }
                finally
                {
                    _requestedAdapter = null;
                    _requestedDevice = null;
                    _requestError = null;
                }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_device != null)
            {
                _api.DeviceRelease(_device);
                _device = null;
            }
            if (_adapter != null)
            {
                _api.AdapterRelease(_adapter);
                _adapter = null;
            }
            if (_surface != null)
            {
                _api.SurfaceRelease(_surface);
                _surface = null;
            }
            if (_instance != null)
            {
                _api.InstanceRelease(_instance);
                _instance = null;
            }
            // _native is an extension view over _api.Context, not a second owner.
            _api.Dispose();
        }

        private static void PumpCallbacks(WebGPU api, Instance* instance, Func<bool> complete)
        {
            // Silk 2.23 targets the older wgpu-native callback ABI. Requests
            // normally complete inline, but pumping events makes that contract
            // explicit and keeps the probe correct if a driver defers work.
            for (int i = 0; i < 256 && !complete(); i++)
            {
                api.InstanceProcessEvents(instance);
                System.Threading.Thread.Yield();
            }
        }

        private static void OnAdapterRequested(RequestAdapterStatus status, Adapter* received,
            byte* message, void* userdata)
        {
            if (status == RequestAdapterStatus.Success)
            {
                _requestedAdapter = received;
                return;
            }
            _requestError = PtrString(message, status.ToString());
        }

        private static void OnDeviceRequested(RequestDeviceStatus status, Device* received,
            byte* message, void* userdata)
        {
            if (status == RequestDeviceStatus.Success)
            {
                _requestedDevice = received;
                return;
            }
            _requestError = PtrString(message, status.ToString());
        }

        private static string PtrString(byte* value, string fallback)
        {
            if (value == null) return fallback;
            return SilkMarshal.PtrToString((nint)value) ?? fallback;
        }

        private static InstanceBackend ToInstanceBackend(GraphicsBackend backend)
        {
            return backend switch
            {
                GraphicsBackend.DirectX12 => InstanceBackend.DX12,
                GraphicsBackend.Vulkan => InstanceBackend.Vulkan,
                GraphicsBackend.Metal => InstanceBackend.Metal,
                _ => throw new ArgumentOutOfRangeException(nameof(backend), backend, null)
            };
        }

        private static BackendType ToBackendType(GraphicsBackend backend)
        {
            return backend switch
            {
                GraphicsBackend.DirectX12 => BackendType.D3D12,
                GraphicsBackend.Vulkan => BackendType.Vulkan,
                GraphicsBackend.Metal => BackendType.Metal,
                _ => throw new ArgumentOutOfRangeException(nameof(backend), backend, null)
            };
        }

        private static void PrepareMoltenVK()
        {
            string root = AppContext.BaseDirectory;
            string loader = Path.Combine(root, "libvulkan.dylib");
            string driver = Path.Combine(root, "libMoltenVK.dylib");
            string manifest = Path.Combine(root, "MoltenVK_icd.json");
            if (!File.Exists(manifest))
            {
                // Inside a .app, non-code resources cannot live in Contents/MacOS
                // without upsetting the bundle's code seal. package-macos.sh
                // moves the ICD manifest to Contents/Resources.
                manifest = Path.GetFullPath(Path.Combine(root, "..", "Resources", "MoltenVK_icd.json"));
            }
            if (!File.Exists(loader) || !File.Exists(driver) || !File.Exists(manifest))
            {
                throw new DllNotFoundException(
                    "Vulkan on macOS requires the packaged Vulkan Loader, libMoltenVK.dylib, "
                    + "and MoltenVK_icd.json. Run tools/wgpu/build-native.sh for the target RID before publishing.");
            }

            if (_macVulkanLoader == 0)
            {
                _macVulkanLoader = NativeLibrary.Load(loader);
            }
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("VK_DRIVER_FILES")))
            {
                Environment.SetEnvironmentVariable("VK_DRIVER_FILES", manifest);
            }
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("VK_ICD_FILENAMES")))
            {
                Environment.SetEnvironmentVariable("VK_ICD_FILENAMES", manifest);
            }
        }
    }

    public static class ModernGraphicsBackendProbe
    {
        public static int Run(string? value)
        {
            try
            {
                GraphicsBackendPolicy.Configure(value);
                GraphicsBackend backend = GraphicsBackendPolicy.Resolved;
                if (!GraphicsBackendPolicy.IsModern(backend))
                {
                    Console.Error.WriteLine(
                        "[renderbackendprobe] OpenGL is the existing renderer; choose dx12, vulkan, metal, or auto.");
                    return 2;
                }

                using ModernGraphicsDevice device = ModernGraphicsDevice.Create(backend);
                string renderProof = ModernGraphicsRenderCheck.Run(device);
                Console.WriteLine(
                    $"[renderbackendprobe] PASS backend={GraphicsBackendPolicy.DisplayName(device.Backend)} "
                    + $"adapter=\"{device.AdapterName}\" driver=\"{device.DriverDescription}\" "
                    + $"wgpu=0x{device.NativeVersion:X8} {renderProof}");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[renderbackendprobe] FAIL {ex.GetType().Name}: {ex.Message}");
                return 1;
            }
        }
    }
}
#endif
