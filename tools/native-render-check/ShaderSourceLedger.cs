using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenTK.Graphics.OpenGL;
using MphRead;

// Actual operation counts come ONLY from facade attempt/status events. Owner
// hooks label pending source/program ownership; nested hooks are not operations.
sealed class ShaderSourceLedger : IDisposable
{
    readonly FieldInfo _callback;
    readonly Action<string, int>? _previous;
    readonly Func<int, (string Hash, string Stage)> _source;
    readonly Func<int, int> _compileStatus, _linkStatus;
    readonly Dictionary<int, string> _pendingSources = new();
    readonly Dictionary<(string Stage, string Hash), int> _sources = new();
    readonly Dictionary<int, int> _compilePending = new(), _linkPending = new();
    int _compileCallbacks, _linkCallbacks, _overflow, _compileStatusCallbacks, _compileSuccesses, _compileFailures,
        _linkStatusCallbacks, _linkSuccesses, _linkFailures, _unexpectedStatuses;

    internal ShaderSourceLedger(Func<int, (string Hash, string Stage)>? source = null,
        Func<int, int>? compileStatus = null, Func<int, int>? linkStatus = null)
    {
        _source = source ?? ReadSource;
        _compileStatus = compileStatus ?? (shader => { GL.GetShader(shader, ShaderParameter.CompileStatus, out int status); return status; });
        _linkStatus = linkStatus ?? (program => { GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int status); return status; });
        _callback = typeof(Scene).Assembly.GetType("MphRead.Mods.Render.OpenGlFailureInjection")!
            .GetField("ShaderObjectCallback", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new NotSupportedException("This client lacks the shader-owner diagnostic hook.");
        _previous = (Action<string, int>?)_callback.GetValue(null);
        _callback.SetValue(null, (Action<string, int>)Observe);
    }
    static (string Hash, string Stage) ReadSource(int shader)
    {
        GL.GetShader(shader, ShaderParameter.ShaderSourceLength, out int size);
        GL.GetShaderSource(shader, size, out _, out string source);
        GL.GetShader(shader, ShaderParameter.ShaderType, out int type);
        if (string.IsNullOrEmpty(source)) throw new InvalidOperationException("Compile attempt has no nonempty shader source.");
        return (Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source))),
            "graphics-api-" + (type == (int)ShaderType.VertexShader ? "vertex" : "fragment"));
    }
    void Observe(string stage, int handle)
    {
        if (stage is "graphics-api-shader-create" or "graphics-api-shader-delete")
        {
            _pendingSources.Remove(handle); _compilePending.Remove(handle);
        }
        else if (stage is "graphics-api-program-create" or "graphics-api-program-delete") _linkPending.Remove(handle);
        else if (stage.EndsWith("-vertex", StringComparison.Ordinal) || stage.EndsWith("-fragment", StringComparison.Ordinal))
        {
            // Every facade ShaderSource replaces prior attribution, even if set
            // twice or a deleted native name is reused. A subsequent owner hook
            // provides the preferred route label for the current source body.
            _pendingSources[handle] = stage;
        }
        else if (stage == "graphics-api-compile-attempt")
        {
            var current = _source(handle); // Exact current source on EVERY compile.
            string owner = _pendingSources.GetValueOrDefault(handle, current.Stage);
            _compileCallbacks++;
            _compilePending[handle] = _compilePending.GetValueOrDefault(handle) + 1;
            var key = (owner, current.Hash);
            if (_sources.ContainsKey(key)) _sources[key]++;
            else if (_sources.Count < 64) _sources.Add(key, 1);
            else _overflow++;
        }
        else if (stage == "graphics-api-compiled")
        {
            int status = _compileStatus(handle); _compileStatusCallbacks++;
            if (status == 0) _compileFailures++; else _compileSuccesses++;
            ObserveStatus(_compilePending, handle);
        }
        else if (stage == "graphics-api-link-attempt")
        {
            _linkCallbacks++; _linkPending[handle] = _linkPending.GetValueOrDefault(handle) + 1;
        }
        else if (stage == "graphics-api-linked")
        {
            int status = _linkStatus(handle); _linkStatusCallbacks++;
            if (status == 0) _linkFailures++; else _linkSuccesses++;
            ObserveStatus(_linkPending, handle);
        }
        // Other owner program/compiled/linked markers are attribution only and
        // intentionally do not increment attempts or trigger status queries.
        _previous?.Invoke(stage, handle);
    }
    void ObserveStatus(Dictionary<int, int> pending, int handle)
    {
        int count = pending.GetValueOrDefault(handle);
        if (count == 0) _unexpectedStatuses++;
        else if (count == 1) pending.Remove(handle);
        else pending[handle] = count - 1;
    }
    internal object Result() => new {
        counterSemantics = "facade-operation-v2", compileAttemptCallbacks = _compileCallbacks, linkAttemptCallbacks = _linkCallbacks,
        compileStatusCallbacks = _compileStatusCallbacks, compileSuccesses = _compileSuccesses, compileFailures = _compileFailures,
        linkStatusCallbacks = _linkStatusCallbacks, linkSuccesses = _linkSuccesses, linkFailures = _linkFailures,
        unexpectedStatusCallbacks = _unexpectedStatuses,
        compileStatusCoverage = _compileStatusCallbacks == _compileCallbacks && _unexpectedStatuses == 0 ? "COMPLETE_OBSERVED" : "UNVERIFIED",
        linkStatusCoverage = _linkStatusCallbacks == _linkCallbacks && _unexpectedStatuses == 0 ? "COMPLETE_OBSERVED" : "UNVERIFIED",
        sourceStageKeys = _sources.Count, uniqueSourceHashes = _sources.Keys.Select(k => k.Hash).Distinct().Count(), overflowCallbacks = _overflow,
        sources = _sources.Select(pair => new { stage = pair.Key.Stage, sourceSha256 = pair.Key.Hash, callbacks = pair.Value }).ToArray(),
        scope = "Actual desktop GraphicsApi CompileShader/LinkProgram attempts and immediate status observations counted once. Owner source hooks label canonical/world variants, GraphicsPipeline, PlayerOutlines, launcher and diagnostics; current sources are hashed at each actual compile. Generic-only routes keep graphics-api labels. Repeated source assignment is not compilation; repeated compile/link is counted. Direct calls bypassing GraphicsApi, GLES/GlEs and external native UI libraries are NOT_RUN. Missing status coverage is UNVERIFIED; attempts are not native allocation or driver pipeline-key counts." };
    public void Dispose() => _callback.SetValue(null, _previous);

    internal static int SelfTest()
    {
        bool pass = true; int sourceReads = 0, compileReads = 0, linkReads = 0;
        string digest = new('A', 64);
        Type hooks = typeof(Scene).Assembly.GetType("MphRead.Mods.Render.OpenGlFailureInjection")!;
        MethodInfo emit = hooks.GetMethod("ShaderObject", BindingFlags.NonPublic | BindingFlags.Static)!;
        void Event(string stage, int handle) => emit.Invoke(null, new object[] { stage, handle });
        using (var ledger = new ShaderSourceLedger(_ => { sourceReads++; return (digest, "graphics-api-vertex"); },
            _ => { compileReads++; return 1; }, _ => { linkReads++; return 1; }))
        {
            Event("graphics-api-shader-create", 1); Event("graphics-api-vertex", 1); Event("main-vertex", 1);
            pass &= sourceReads == 0; // Attribution alone never queries or counts.
            Event("graphics-api-compile-attempt", 1); Event("graphics-api-compiled", 1); Event("main-vertex-compiled", 1);
            digest = new('B', 64);
            Event("graphics-api-vertex", 1); Event("graphics-api-vertex", 1); Event("main-vertex", 1);
            Event("graphics-api-compile-attempt", 1); Event("graphics-api-compiled", 1);
            Event("graphics-api-compile-attempt", 1); Event("graphics-api-compiled", 1); // Same source, real recompilation.
            Event("graphics-api-shader-delete", 1); Event("graphics-api-shader-create", 1);
            digest = new('C', 64); Event("graphics-api-vertex", 1); // Reused name, no old owner attribution.
            Event("graphics-api-compile-attempt", 1); Event("graphics-api-compiled", 1);
            Event("graphics-api-program-create", 7); Event("main-program", 7);
            Event("graphics-api-link-attempt", 7); Event("graphics-api-linked", 7); Event("main-linked", 7);
            Event("graphics-api-link-attempt", 7); Event("graphics-api-linked", 7); Event("main-linked", 7);
            var result = JsonSerializer.SerializeToNode(ledger.Result())!;
            pass &= result["compileAttemptCallbacks"]!.GetValue<int>() == 4 && result["compileStatusCallbacks"]!.GetValue<int>() == 4
                && result["linkAttemptCallbacks"]!.GetValue<int>() == 2 && result["linkStatusCallbacks"]!.GetValue<int>() == 2
                && result["sourceStageKeys"]!.GetValue<int>() == 3 && result["compileStatusCoverage"]!.GetValue<string>() == "COMPLETE_OBSERVED"
                && sourceReads == 4 && compileReads == 4 && linkReads == 2;
            var entries = result["sources"]!.AsArray();
            pass &= entries.Count(entry => entry!["stage"]!.GetValue<string>() == "main-vertex") == 2
                && entries.Single(entry => entry!["sourceSha256"]!.GetValue<string>() == new string('B', 64))!["callbacks"]!.GetValue<int>() == 2
                && entries.Single(entry => entry!["sourceSha256"]!.GetValue<string>() == new string('C', 64))!["stage"]!.GetValue<string>() == "graphics-api-vertex";
        }
        Event("graphics-api-compile-attempt", 1); Event("graphics-api-compiled", 1);
        pass &= sourceReads == 4 && compileReads == 4; // Disabled callback: no queries.
        using (var ledger = new ShaderSourceLedger(_ => (new string('D', 64), "graphics-api-fragment"), _ => 0, _ => 0))
        {
            Event("graphics-api-compile-attempt", 2); Event("graphics-api-compiled", 2);
            Event("graphics-api-link-attempt", 8); Event("graphics-api-linked", 8);
            var result = JsonSerializer.SerializeToNode(ledger.Result())!;
            pass &= result["compileFailures"]!.GetValue<int>() == 1 && result["linkFailures"]!.GetValue<int>() == 1;
            Event("graphics-api-compiled", 2); // An extra status must expose unknown coverage.
            result = JsonSerializer.SerializeToNode(ledger.Result())!;
            pass &= result["unexpectedStatusCallbacks"]!.GetValue<int>() == 1 && result["compileStatusCoverage"]!.GetValue<string>() == "UNVERIFIED";
        }
        Console.WriteLine("SHADERSOURCELEDGER " + (pass ? "PASS" : "FAIL") + " facadeOnce=True ownerAttribution=True repeatedSourceAndCompile=True relink=True reusedNames=True genericRoutes=True disabledNoQuery=True failureAndUnexpectedStatus=True deviceFree=True");
        return pass ? 0 : 1;
    }
}
