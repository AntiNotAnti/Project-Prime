using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using OpenTK.Graphics.OpenGL;
using MphRead;

// Opt-in source/attempt/status observations on desktop GL owner thread.
// Fixed-size hash ledger avoids retaining source bodies or native owner objects.
sealed class ShaderSourceLedger : IDisposable
{
    private readonly FieldInfo _callback;
    private readonly Action<string, int>? _previous;
    private readonly Dictionary<(string Stage, string Hash), int> _sources = new();
    private int _compileCallbacks, _linkCallbacks, _overflow, _compileStatusCallbacks, _compileSuccesses, _compileFailures, _linkStatusCallbacks, _linkSuccesses, _linkFailures;
    internal ShaderSourceLedger()
    {
        _callback = typeof(Scene).Assembly.GetType("MphRead.Mods.Render.OpenGlFailureInjection")!
            .GetField("ShaderObjectCallback", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new NotSupportedException("This client lacks the existing shader-owner diagnostic hook.");
        _previous = (Action<string, int>?)_callback.GetValue(null);
        _callback.SetValue(null, (Action<string, int>)Observe);
    }
    private void Observe(string stage, int handle)
    {
        if (stage.EndsWith("-vertex", StringComparison.Ordinal) || stage.EndsWith("-fragment", StringComparison.Ordinal))
        {
            GL.GetShader(handle, ShaderParameter.ShaderSourceLength, out int size);
            GL.GetShaderSource(handle, size, out int length, out string source);
            if (string.IsNullOrEmpty(source)) throw new InvalidOperationException("Shader source hook fired before nonempty source: " + stage);
            _compileCallbacks++;
            var key = (stage, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source))));
            if (_sources.ContainsKey(key)) _sources[key]++;
            else if (_sources.Count < 64) _sources.Add(key, 1);
            else _overflow++;
        }
        else if (stage.EndsWith("-compiled", StringComparison.Ordinal))
        {
            GL.GetShader(handle, ShaderParameter.CompileStatus, out int status);
            _compileStatusCallbacks++; if (status == 0) _compileFailures++; else _compileSuccesses++;
        }
        else if (stage.EndsWith("-linked", StringComparison.Ordinal))
        {
            GL.GetProgram(handle, GetProgramParameterName.LinkStatus, out int status);
            _linkStatusCallbacks++; if (status == 0) _linkFailures++; else _linkSuccesses++;
        }
        else if (stage.EndsWith("-program", StringComparison.Ordinal) || stage.EndsWith("-link", StringComparison.Ordinal)) _linkCallbacks++;
        _previous?.Invoke(stage, handle);
    }
    internal object Result() => new { compileAttemptCallbacks = _compileCallbacks, linkAttemptCallbacks = _linkCallbacks,
        compileStatusCallbacks = _compileStatusCallbacks, compileSuccesses = _compileSuccesses, compileFailures = _compileFailures,
        linkStatusCallbacks = _linkStatusCallbacks, linkSuccesses = _linkSuccesses, linkFailures = _linkFailures,
        compileStatusCoverage = _compileStatusCallbacks == _compileCallbacks ? "COMPLETE_OBSERVED" : "UNVERIFIED",
        linkStatusCoverage = _linkStatusCallbacks == _linkCallbacks ? "COMPLETE_OBSERVED" : "UNVERIFIED",
        sourceStageKeys = _sources.Count, uniqueSourceHashes = _sources.Keys.Select(k => k.Hash).Distinct().Count(), overflowCallbacks = _overflow,
        sources = _sources.Select(pair => new { stage = pair.Key.Stage, sourceSha256 = pair.Key.Hash, callbacks = pair.Value }).ToArray(),
        scope = "Desktop source/attempt/status hooks: canonical scene/world variants, GraphicsPipeline, PlayerOutlines, launcher photo, launcher/thumbnail diagnostics and GraphicsApi desktop wrappers (including OpenGlVisualCheck). Owner thread only; paths not exercised are NOT_RUN. GLES/GlEs and external native UI libraries are NOT_RUN. Counts are observed attempts and immediate GL status checks, not GL-object census, pipeline variants or proof of safe program sharing; missing status coverage remains UNVERIFIED." };
    public void Dispose() => _callback.SetValue(null, _previous);
}
