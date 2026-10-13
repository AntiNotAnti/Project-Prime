using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MphRead;

// Explicit diagnostic control of only the renderer's two existing GC calls.
// No ordinary client settings/environment flag enables this hook.
internal sealed class TransitionGcObservation : IDisposable
{
    private const int MaxRecords = 4096;
    private readonly List<object> _records = new();
    private readonly string _policy;
    private readonly FieldInfo _observer, _policyField;
    private readonly object? _previousObserver, _previousPolicy;
    private TransitionGcObservation(string policy, int bits)
    {
        _policy = policy;
        Type type = typeof(Scene).Assembly.GetType("MphRead.Mods.Render.TransitionGcDiagnostics")!;
        _observer = type.GetField("Observer", BindingFlags.NonPublic | BindingFlags.Static)!;
        _policyField = type.GetField("Policy", BindingFlags.NonPublic | BindingFlags.Static)!;
        _previousObserver = _observer.GetValue(null); _previousPolicy = _policyField.GetValue(null);
        _policyField.SetValue(null, Enum.ToObject(_policyField.FieldType, bits));
        _observer.SetValue(null, (Action<object>)(record =>
        {
            if (_records.Count == MaxRecords) throw new InvalidOperationException("Transition GC diagnostic record bound exceeded.");
            _records.Add(record);
        }));
    }
    internal static TransitionGcObservation? Create(string[] args)
    {
        int index = Array.IndexOf(args, "-transition-gc");
        if (index < 0) return null;
        if (index + 1 >= args.Length) throw new ArgumentException("-transition-gc requires keep|skip-load|skip-end|skip-both.");
        string policy = args[index + 1];
        int bits = policy switch { "keep" => 0, "skip-load" => 1, "skip-end" => 2, "skip-both" => 3,
            _ => throw new ArgumentException("-transition-gc requires keep|skip-load|skip-end|skip-both.") };
        if (args.Contains("-resourcestress"))
            throw new ArgumentException("Transition GC controls require ordinary completed-frame runs with weak-only cleanup; resource stress retains scenes for separate ownership inspection.");
        if (args.Contains("-resource-drain"))
            throw new ArgumentException("Transition GC comparisons exclude the diagnostic full-GC/finalizer drain; omit -resource-drain.");
        return new(policy, bits);
    }
    internal object Result(bool complete)
    {
        int loads = _records.Count(record => (string?)record.GetType().GetProperty("Phase")?.GetValue(record) == "SceneLoad");
        int ends = _records.Count(record => (string?)record.GetType().GetProperty("Phase")?.GetValue(record) == "SceneEnd");
        if (complete && (loads == 0 || ends == 0))
            throw new InvalidOperationException("Completed transition GC evidence requires observed load and end calls.");
        return new
        {
        schema = 1, policy = _policy, maxRecords = MaxRecords, observedLoadCalls = loads, observedEndCalls = ends,
        complete, records = _records,
        scope = "Only original Scene.OnLoad Gen2 forced blocking compacting collection and RenderWindow.EndScene default collection; no added GC.",
        limits = "Collection wall excludes memory probes/callback; stage wall includes observer overhead and GL/audio/driver work. Stage scopes cover Scene.OnLoad and EndScene, not scene construction/AddRoom. GCMemoryInfo describes the indexed latest collection, which may predate skipped calls. Managed live bytes are an estimate; RSS includes native/runtime/driver memory. Readback/resource-stress frames are lifecycle evidence, never FPS. Frame tails require separate ordinary completed-frame runs on the same binary/fixture. Observation retains bounded value-only records identically across arms."
        };
    }
    public void Dispose()
    {
        _observer.SetValue(null, _previousObserver); _policyField.SetValue(null, _previousPolicy);
    }
}
