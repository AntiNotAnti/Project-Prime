using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using MphRead.Mods;
using MphRead.Mods.Input;

namespace MphRead.NetTest;

// Fresh-process production gate test. No Scene, window, simulated game or
// physical input is constructed. Headless.Enter is intentionally never reset.
internal static class HeadlessGamepadTests
{
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static int _checks, _failures;
    private static void Check(bool value, string label)
    {
        _checks++; if (!value) _failures++;
        Console.WriteLine($"[headless-gamepad] {(value ? "PASS" : "FAIL")} {label}");
    }
    private static object? Field(Type type, string name) => type.GetField(name, Static)!.GetValue(null);
    private static object?[] Slots()
    {
        var slots = (Array)Field(typeof(GamepadDesktop), "Slots")!;
        return slots.Cast<object>().SelectMany(slot => slot.GetType().GetFields(Instance)
            .OrderBy(field => field.Name, StringComparer.Ordinal).Select(field => field.GetValue(slot))).ToArray();
    }
    private static string[] Capabilities(IDictionary dictionary) => dictionary.Keys.Cast<string>()
        .OrderBy(key => key, StringComparer.Ordinal).Select(key => key + ":" + dictionary[key]).ToArray();
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static long PollAllocationSample()
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10000; i++) GamepadDesktop.Poll();
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }
    public static int Run()
    {
        try
        {
            Check(!Headless.Active, "fresh process has not entered Headless before this fixture");
            Check(!(bool)Field(typeof(GamepadMappings), "_loaded")!, "actual mapping owner begins cold");
            Check(GamepadManager.Devices.Count == 0, "fresh process has no registered physical devices");
            Headless.Enter();
            // A pending reload and one metadata entry make silent clearing
            // observable. Neither creates a device, invokes GLFW or reads files.
            GamepadMappings.ReloadRequested = true;
            var capabilities = (IDictionary)Field(typeof(GamepadMappings), "MappingCapabilities")!;
            capabilities.Add("headless-gate-fixture", GamepadCapabilities.AnalogLeftStick);
            var expectedCapabilities = Capabilities(capabilities);
            var expectedSlots = Slots();
            object? expectedUnavailable = Field(typeof(GamepadDesktop), "_unavailable");
            string expectedSummary = GamepadMappings.Summary;
            var expectedSnapshot = GamepadManager.Snapshot;
            var expectedPresentation = GamepadManager.PresentationSnapshot;
            var expectedDevices = GamepadManager.Devices.ToArray();
            var expectedActive = GamepadManager.ActiveDevice;
            string? expectedSelected = GamepadManager.SelectedDeviceId, expectedLast = GamepadManager.LastInputDevice;
            var haptics = (IDictionary)Field(typeof(GamepadHaptics), "Backends")!;
            var expectedHaptics = haptics.Keys.Cast<object>().Select(key => new DictionaryEntry(key, haptics[key])).ToArray();
            int notifications = 0;
            Action<GamepadDeviceSnapshot> deviceChanged = _ => notifications++;
            Action activeChanged = () => notifications++;
            GamepadManager.DeviceAdded += deviceChanged;
            GamepadManager.DeviceRemoved += deviceChanged;
            GamepadManager.ActiveChanged += activeChanged;
            try
            {
                GamepadDesktop.Poll();
                Check(!(bool)Field(typeof(GamepadMappings), "_loaded")!, "headless Poll leaves actual mapping table cold");
                Check(GamepadMappings.ReloadRequested, "headless Poll preserves pending mapping reload");
                Check(Capabilities(capabilities).SequenceEqual(expectedCapabilities), "headless Poll preserves mapping capability entries");
                Check(GamepadMappings.Summary == expectedSummary, "headless Poll preserves mapping summary");
                Check(Slots().SequenceEqual(expectedSlots) && Equals(Field(typeof(GamepadDesktop), "_unavailable"), expectedUnavailable),
                    "headless Poll preserves every desktop device slot and physical availability flag");
                Check(GamepadManager.Devices.SequenceEqual(expectedDevices) && GamepadManager.Snapshot.Equals(expectedSnapshot)
                    && Nullable.Equals(GamepadManager.PresentationSnapshot, expectedPresentation)
                    && Equals(GamepadManager.ActiveDevice, expectedActive) && GamepadManager.SelectedDeviceId == expectedSelected
                    && GamepadManager.LastInputDevice == expectedLast && notifications == 0,
                    "headless Poll preserves device registry, snapshots, selection and notification owners");
                Check(haptics.Keys.Cast<object>().Select(key => new DictionaryEntry(key, haptics[key])).SequenceEqual(expectedHaptics), "headless Poll preserves registered haptics backends");
                long initial = PollAllocationSample(), subsequent = PollAllocationSample();
                for (int i = 0; i < 64; i++) PollAllocationSample();
                Console.WriteLine($"[headless-gamepad] default-runtime fixed helper warmup: initial={initial}, subsequent={subsequent}; 64 further warmups");
                for (int i = 0; i < 5; i++)
                {
                    long allocated = PollAllocationSample();
                    Check(allocated == 0, $"warmed headless Poll sample {i + 1}/5 allocates zero bytes for 10000 actual calls (actual {allocated})");
                }
                Check(!(bool)Field(typeof(GamepadMappings), "_loaded")! && GamepadMappings.ReloadRequested
                    && Capabilities(capabilities).SequenceEqual(expectedCapabilities), "repeated headless Poll retains cold/pending mapping state");
            }
            finally
            {
                GamepadManager.DeviceAdded -= deviceChanged;
                GamepadManager.DeviceRemoved -= deviceChanged;
                GamepadManager.ActiveChanged -= activeChanged;
            }
            Check(Headless.Active, "Headless remains entered monotonically");
            Console.WriteLine($"[headless-gamepad] {_checks} checks; failures={_failures}. Actual production Poll gate only; no Scene or game acceptance.");
            return _failures == 0 ? 0 : 1;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
