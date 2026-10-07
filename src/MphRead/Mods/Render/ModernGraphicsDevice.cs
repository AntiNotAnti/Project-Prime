#if !MPHREAD_SERVER
using System;
using System.Collections.Generic;
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
    /// Prime backend policy. The active modern gameplay renderer uses this
    /// device through the shared GL-compatible facade; wgpu owns native backend
    /// command submission, synchronization, and resource allocation.
    /// </summary>
    public sealed unsafe class ModernGraphicsDevice : IDisposable
    {
        private static readonly object _requestLock = new();
        private static Adapter* _requestedAdapter;
        private static Device* _requestedDevice;
        private static string? _requestError;
        private static nint _macVulkanLoader;
        // wgpu stores this process-wide, including after a device is disposed.
        private static readonly LogCallback _nativeLog = OnNativeLog;
        private static bool _nativeValidationDiagnosticsEnabled;
        private static readonly object _nativeValidationDiagnosticLock = new();
        private static readonly Queue<string> _nativeValidationErrors = new();
        private static readonly Queue<string> _nativeValidationWarnings = new();
        private static int _nativeValidationErrorsForwarded;
        private static int _nativeValidationWarningsForwarded;
        private const int NativeValidationErrorLimit = 64;
        private const int NativeValidationWarningLimit = 32;
        private const int NativeValidationMessageLimit = 4096;

        private static void OnNativeLog(LogLevel level, byte* message, void* userdata)
        {
            try
            {
                string detail = PtrString(message, "no detail");
                try { Mods.DebugLog.Checkpoint("wgpu", $"{level}: {detail}"); }
                catch { /* An optional disk-log failure must not discard the priority lane. */ }
                if (System.Threading.Volatile.Read(ref _nativeValidationDiagnosticsEnabled)
                    && level is LogLevel.Error or LogLevel.Warn)
                {
                    // Info-level generated HLSL can fill the ordinary diagnostic
                    // tail. Preserve native errors separately and forward them
                    // before a later device-loss/map failure obscures their cause.
                    string bounded = detail.Length <= NativeValidationMessageLimit
                        ? detail : detail[..NativeValidationMessageLimit] + " [truncated]";
                    string record = $"[wgpu-validation] {level}: {bounded}";
                    bool error = level == LogLevel.Error;
                    int limit = error ? NativeValidationErrorLimit : NativeValidationWarningLimit;
                    lock (_nativeValidationDiagnosticLock)
                    {
                        Queue<string> records = error ? _nativeValidationErrors : _nativeValidationWarnings;
                        records.Enqueue(record);
                        while (records.Count > limit) records.Dequeue();
                    }
                    int forwarded = error
                        ? System.Threading.Interlocked.Increment(ref _nativeValidationErrorsForwarded)
                        : System.Threading.Interlocked.Increment(ref _nativeValidationWarningsForwarded);
                    if (forwarded <= limit) Console.Error.WriteLine(record);
                    else if (forwarded == limit + 1)
                        Console.Error.WriteLine($"[wgpu-validation] {level}: immediate diagnostic limit reached; latest records remain retained");
                }
            }
            catch { /* Never unwind a logging failure through native frames. */ }
        }

        internal static void AppendNativeValidationDiagnostics(System.Text.StringBuilder output)
        {
            if (!System.Threading.Volatile.Read(ref _nativeValidationDiagnosticsEnabled)) return;
            lock (_nativeValidationDiagnosticLock)
            {
                if (_nativeValidationErrors.Count == 0 && _nativeValidationWarnings.Count == 0) return;
                output.AppendLine("Prioritized native validation diagnostics:");
                foreach (string warning in _nativeValidationWarnings) output.AppendLine(warning);
                foreach (string error in _nativeValidationErrors) output.AppendLine(error);
            }
        }

        private readonly WebGPU _api;
        private readonly Wgpu _native;
        private Instance* _instance;
        private Adapter* _adapter;
        private Device* _device;
        private Surface* _surface;
        private bool _disposed;
        private SurfaceFactory? _surfaceFactory;
        private readonly DeviceErrors _errors;
        private readonly HashSet<(string Operation, NativeGraphicsOutcome Outcome)> _reportedNativeOutcomes = new();

        // Native callbacks cannot throw into Rust/C. Keep delegates rooted for
        // the device lifetime and surface errors at managed submission boundaries.
        private sealed class DeviceErrors
        {
            internal readonly ErrorCallback Error;
            internal readonly DeviceLostCallback Lost;
            private string? _failure;
            internal volatile bool DeviceLost;

            internal DeviceErrors()
            {
                Error = (type, message, _) => Record($"WebGPU {type}: {PtrString(message, "no detail")}");
                Lost = (reason, message, _) =>
                {
                    DeviceLost = true;
                    Record($"WebGPU device lost ({reason}): {PtrString(message, "no detail")}");
                };
            }

            private void Record(string message)
            {
                System.Threading.Interlocked.CompareExchange(ref _failure, message, null);
                try
                {
                    Console.Error.WriteLine($"[render] {message}");
                    Mods.DebugLog.Checkpoint("render", message);
                }
                catch { /* Never unwind across a native callback boundary. */ }
            }

            internal void ThrowIfFailed()
            {
                string? failure = System.Threading.Volatile.Read(ref _failure);
                if (failure != null) throw new InvalidOperationException(failure);
            }
        }

        internal void ThrowIfFailed() => _errors.ThrowIfFailed();
        internal bool IsLost => _errors.DeviceLost;
        internal bool ObserveNativeResult(NativeGraphicsResult result, string operation, bool teardown = false)
        {
            if (result.Succeeded) return true;
            if (result.Outcome == NativeGraphicsOutcome.DeviceLost)
                _errors.DeviceLost = true;
            string next = result.Outcome switch
            {
                NativeGraphicsOutcome.DeviceLost => "reconstruct the device at the next frame boundary",
                NativeGraphicsOutcome.Timeout => "skip this frame",
                NativeGraphicsOutcome.Outdated => "reconfigure the surface before the next acquire",
                NativeGraphicsOutcome.SurfaceLost => "recreate the surface before the next acquire",
                _ => "stop rendering and inspect the native diagnostic"
            };
            string message = $"{Backend} on {AdapterName}: {operation} returned {result.Outcome}; "
                + $"next action: {next}. {result.Detail}";
            try
            {
                if (_reportedNativeOutcomes.Add((operation, result.Outcome)))
                    Mods.DebugLog.Line("render", message);
            }
            catch { /* Diagnostics must not interrupt recovery or teardown. */ }
            if (result.Outcome is NativeGraphicsOutcome.Timeout or NativeGraphicsOutcome.Outdated
                or NativeGraphicsOutcome.SurfaceLost or NativeGraphicsOutcome.DeviceLost)
                return false;
            if (teardown)
            {
                try { Console.Error.WriteLine($"[render] {message}"); } catch { }
                return false;
            }
            throw new InvalidOperationException(message);
        }
        internal void DestroyForCheck()
        {
            _api.DeviceDestroy(_device);
            _native.DevicePoll(_device, true, null);
            // Explicit destruction need not deliver an unexpected-loss callback.
            _errors.DeviceLost = true;
        }
        internal ModernGraphicsDevice CreateReplacement() => CreateCore(Backend, _surfaceFactory);

        private unsafe delegate Surface* SurfaceFactory(WebGPU api, Instance* instance);

        private ModernGraphicsDevice(WebGPU api, Wgpu native, Instance* instance, Adapter* adapter, Device* device,
            Surface* surface, GraphicsBackend backend, uint nativeVersion, string adapterName,
            string driverDescription, DeviceErrors errors, bool compressionBc,
            bool compressionEtc2, bool compressionAstc, bool multiDrawIndirect,
            bool multiDrawIndirectCount, bool timestampQueries)
        {
            _errors = errors;
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
            SupportsTextureCompressionBc = compressionBc;
            SupportsTextureCompressionEtc2 = compressionEtc2;
            SupportsTextureCompressionAstc = compressionAstc;
            SupportsMultiDrawIndirect = multiDrawIndirect;
            SupportsMultiDrawIndirectCount = multiDrawIndirectCount;
            SupportsTimestampQueries = timestampQueries;
            SupportedLimits limits = default;
            _api.DeviceGetLimits(_device, &limits);
            MaxTextureDimension2D = GraphicsResourceLimits.ToCompatibilityTextureLimit(
                limits.Limits.MaxTextureDimension2D);
        }

        public GraphicsBackend Backend { get; }
        public uint NativeVersion { get; }
        public string AdapterName { get; }
        public string DriverDescription { get; }
        internal int MaxTextureDimension2D { get; }
        internal bool SupportsTextureCompressionBc { get; }
        internal bool SupportsTextureCompressionEtc2 { get; }
        internal bool SupportsTextureCompressionAstc { get; }
        internal bool SupportsMultiDrawIndirect { get; }
        internal bool SupportsMultiDrawIndirectCount { get; }
        internal bool SupportsTimestampQueries { get; }

        internal WebGPU Api => _api;
        internal Wgpu Native => _native;
        internal Adapter* Adapter => _adapter;
        internal Device* Device => _device;
        internal Surface* Surface => _surface;
        internal Instance* Instance => _instance;

        // Studio owns independently hosted Avalonia native surfaces. The device
        // only borrows the active replay target; the surface owner releases it.
        internal void BorrowStudioSurface(Surface* surface) => _surface = surface;
        internal void ClearBorrowedStudioSurface() => _surface = null;

        public static ModernGraphicsDevice Create(GraphicsBackend requested = GraphicsBackend.Auto)
        {
            return CreateCore(requested, null);
        }

#if !ANDROID
        internal static ModernGraphicsDevice CreateForWindow(NativeWindow window,
            GraphicsBackend requested = GraphicsBackend.Auto)
        {
            return CreateCore(requested, (api, instance) =>
                ModernGraphicsSurface.Create(window, api, instance,
                    GraphicsBackendPolicy.Resolve(GraphicsBackendPolicy.CurrentPlatform, requested)));
        }
#endif

#if ANDROID
        internal static ModernGraphicsDevice CreateForAndroidWindow(nint window) =>
            CreateCore(GraphicsBackend.Vulkan, (api, instance) => ModernGraphicsAndroidSurface.Create(api, instance, window));

        internal void SetAndroidWindow(nint window)
        {
            if (_surface != null)
            {
                _api.SurfaceUnconfigure(_surface);
                _api.SurfaceRelease(_surface);
                _surface = null;
            }
            _surfaceFactory = window == 0 ? null : (api, instance) => ModernGraphicsAndroidSurface.Create(api, instance, window);
            if (_surfaceFactory != null) _surface = _surfaceFactory(_api, _instance);
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

                Mods.DebugLog.Checkpoint("render", $"{backend} startup: loading wgpu-native");
                WebGPU api = WebGPU.GetApi();
                Wgpu? native = null;
                Instance* instance = null;
                Adapter* adapter = null;
                Device* device = null;
                Surface* surface = null;
                try
                {
                    ModernGraphicsNativeBridge.Initialize(api);
                    if (!api.TryGetDeviceExtension(null, out Wgpu nativeExtension))
                    {
                        throw new DllNotFoundException(
                            "Silk.NET loaded WebGPU without the wgpu-native extension.");
                    }
                    native = nativeExtension;
                    bool nativeValidation = Environment.GetEnvironmentVariable("PRIME_WGPU_VALIDATION") == "1";
                    System.Threading.Volatile.Write(ref _nativeValidationDiagnosticsEnabled, nativeValidation);
                    native.SetLogCallback(new PfnLogCallback(_nativeLog), null);
                    native.SetLogLevel(nativeValidation || Mods.DebugLog.Active ? LogLevel.Info : LogLevel.Error);
                    Mods.DebugLog.Checkpoint("render",
                        $"{backend} startup: creating instance (wgpu=0x{native.GetVersion():x8})");

                    InstanceExtras extras = default;
                    extras.Chain.SType = (SType)NativeSType.STypeInstanceExtras;
                    extras.Chain.Next = null;
                    extras.Backends = ToInstanceBackend(backend);
                    if (nativeValidation)
                    {
                        // Diagnostic opt-in: validate native commands without the
                        // DEBUG flag's changes to shader compiler optimization.
                        extras.Flags = (uint)InstanceFlag.Validation;
                        Console.Error.WriteLine($"[render] native validation requested backend={GraphicsBackendPolicy.DisplayName(backend)} flags={(InstanceFlag)extras.Flags}");
                    }

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
                        Mods.DebugLog.Checkpoint("render", $"{backend} startup: creating window surface");
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
                    Mods.DebugLog.Checkpoint("render", $"{backend} startup: requesting compatible adapter");
                    api.InstanceRequestAdapter(instance, &adapterOptions,
                        new PfnRequestAdapterCallback(OnAdapterRequested), null);
                    PumpCallbacks(api, instance, () => _requestedAdapter != null || _requestError != null);
                    adapter = _requestedAdapter;
                    if (adapter == null && platform == GraphicsPlatform.MacOS
                        && backend == GraphicsBackend.Vulkan)
                    {
                        adapter = TryEnumeratedAdapter(api, native, instance, surface, backend,
                            out string enumeration);
                        if (adapter != null)
                        {
                            Mods.DebugLog.Line("render",
                                "wgpu-native requestAdapter rejected MoltenVK; "
                                + $"using enumerated portability adapter ({enumeration})");
                            _requestError = null;
                        }
                        else if (!string.IsNullOrWhiteSpace(enumeration))
                        {
                            _requestError = string.IsNullOrWhiteSpace(_requestError)
                                ? enumeration : $"{_requestError}; {enumeration}";
                        }
                    }
                    if (adapter == null)
                    {
                        throw new InvalidOperationException(
                            $"No {GraphicsBackendPolicy.DisplayName(backend)} adapter was available"
                            + (string.IsNullOrWhiteSpace(_requestError) ? "." : $": {_requestError}"));
                    }

                    AdapterProperties selected = default;
                    api.AdapterGetProperties(adapter, &selected);
                    Mods.DebugLog.Checkpoint("render",
                        $"{backend} startup: requesting device on {PtrString(selected.Name, "Unknown GPU")} "
                        + $"(driver={PtrString(selected.DriverDescription, "Unknown driver")})");
                    _requestedDevice = null;
                    _requestError = null;
                    bool compressionBc = api.AdapterHasFeature(adapter, FeatureName.TextureCompressionBC);
                    bool compressionEtc2 = api.AdapterHasFeature(adapter, FeatureName.TextureCompressionEtc2);
                    bool compressionAstc = api.AdapterHasFeature(adapter, FeatureName.TextureCompressionAstc);
                    FeatureName multiDrawFeature =
                        (FeatureName)NativeFeature.MultiDrawIndirect;
                    FeatureName multiDrawCountFeature =
                        (FeatureName)NativeFeature.MultiDrawIndirectCount;
                    bool multiDrawBackend =
                        platform != GraphicsPlatform.Android
                        && (backend is GraphicsBackend.DirectX12
                            or GraphicsBackend.Vulkan);
                    bool multiDrawIndirect = multiDrawBackend
                        && api.AdapterHasFeature(adapter, multiDrawFeature);
                    bool multiDrawIndirectCount = multiDrawIndirect
                        && api.AdapterHasFeature(adapter, multiDrawCountFeature);
                    bool timestampQueries = GraphicsTimingPolicy.Enabled
                        && api.AdapterHasFeature(adapter, FeatureName.TimestampQuery);
                    FeatureName* requiredFeatures = stackalloc FeatureName[6];
                    int requiredFeatureCount = 0;
                    if (compressionBc) requiredFeatures[requiredFeatureCount++] = FeatureName.TextureCompressionBC;
                    if (compressionEtc2) requiredFeatures[requiredFeatureCount++] = FeatureName.TextureCompressionEtc2;
                    if (compressionAstc) requiredFeatures[requiredFeatureCount++] = FeatureName.TextureCompressionAstc;
                    if (multiDrawIndirect)
                        requiredFeatures[requiredFeatureCount++] = multiDrawFeature;
                    if (multiDrawIndirectCount)
                        requiredFeatures[requiredFeatureCount++] = multiDrawCountFeature;
                    if (timestampQueries)
                        requiredFeatures[requiredFeatureCount++] = FeatureName.TimestampQuery;
                    var errors = new DeviceErrors();
                    var deviceDescriptor = new DeviceDescriptor
                    {
                        RequiredFeatureCount = (nuint)requiredFeatureCount,
                        RequiredFeatures = requiredFeatureCount == 0 ? null : requiredFeatures,
                        DeviceLostCallback = new PfnDeviceLostCallback(errors.Lost)
                    };
                    api.AdapterRequestDevice(adapter, &deviceDescriptor,
                        new PfnRequestDeviceCallback(OnDeviceRequested), null);
                    PumpCallbacks(api, instance, () => _requestedDevice != null || _requestError != null);
                    device = _requestedDevice;
                    if (device == null)
                    {
                        throw new InvalidOperationException(
                            $"The {GraphicsBackendPolicy.DisplayName(backend)} adapter could not create a device"
                            + (string.IsNullOrWhiteSpace(_requestError) ? "." : $": {_requestError}"));
                    }

                    Mods.DebugLog.Checkpoint("render", $"{backend} startup: device created");
                    api.DeviceSetUncapturedErrorCallback(device, new PfnErrorCallback(errors.Error), null);
                    errors.ThrowIfFailed();
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
                        native.GetVersion(), name, driver, errors, compressionBc,
                        compressionEtc2, compressionAstc, multiDrawIndirect,
                        multiDrawIndirectCount, timestampQueries)
                        { _surfaceFactory = surfaceFactory };
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

        internal void RecreateSurface()
        {
            if (_surface != null)
            {
                _api.SurfaceUnconfigure(_surface);
                _api.SurfaceRelease(_surface);
                _surface = null;
            }
            if (_surfaceFactory == null) throw new InvalidOperationException("No presentation surface factory is available.");
            _surface = _surfaceFactory(_api, _instance);
            if (_surface == null) throw new InvalidOperationException("Could not recreate the presentation surface.");
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_surface != null)
            {
                // A configured surface still refers to this device. Unconfigure
                // and release it before releasing the device/adapter it was
                // configured against.
                _api.SurfaceUnconfigure(_surface);
                _api.SurfaceRelease(_surface);
                _surface = null;
            }
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
            if (_instance != null)
            {
                _api.InstanceRelease(_instance);
                _instance = null;
            }
            // _native is an extension view over _api.Context, not a second owner.
            _api.Dispose();
            GC.KeepAlive(_errors);
        }

        private static Adapter* TryEnumeratedAdapter(WebGPU api, Wgpu native,
            Instance* instance, Surface* surface, GraphicsBackend backend, out string description)
        {
            description = "";
            nuint count = native.InstanceEnumerateAdapters(instance, null, null);
            if (count == 0)
            {
                description = "Vulkan instance enumerated zero adapters";
                return null;
            }

            Adapter** adapters = stackalloc Adapter*[(int)count];
            nuint written = native.InstanceEnumerateAdapters(instance, null, adapters);
            Adapter* selected = null;
            var seen = new System.Text.StringBuilder();
            for (nuint i = 0; i < written; i++)
            {
                Adapter* candidate = adapters[i];
                if (candidate == null) continue;

                AdapterProperties properties = default;
                api.AdapterGetProperties(candidate, &properties);
                string name = PtrString(properties.Name, "Unknown GPU");
                if (seen.Length != 0) seen.Append(", ");
                seen.Append(name).Append('/').Append(properties.BackendType);

                bool backendMatches = properties.BackendType == ToBackendType(backend);
                bool surfaceMatches = true;
                if (backendMatches && surface != null)
                {
                    SurfaceCapabilities capabilities = default;
                    api.SurfaceGetCapabilities(surface, candidate, ref capabilities);
                    surfaceMatches = capabilities.FormatCount > 0;
                    api.SurfaceCapabilitiesFreeMembers(capabilities);
                }

                if (selected == null && backendMatches && surfaceMatches)
                {
                    selected = candidate;
                    continue;
                }
                api.AdapterRelease(candidate);
            }

            description = selected != null
                ? $"selected {seen}"
                : $"enumerated {written} adapter(s): {seen}";
            return selected;
        }

        private static void PumpCallbacks(WebGPU api, Instance* instance, Func<bool> complete)
        {
            // Silk 2.23 targets the older wgpu-native callback ABI. Requests
            // normally complete inline, but pumping events makes that contract
            // explicit and keeps the probe correct if a driver defers work.
            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            int iterations = 0;
            for (; iterations < 256 && !complete(); iterations++)
            {
                api.InstanceProcessEvents(instance);
                System.Threading.Thread.Yield();
            }
            Mods.DebugLog.Checkpoint("render", $"native callback pump: {iterations} iterations, "
                + $"{System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds:0.000} ms, completed={complete()}");
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
