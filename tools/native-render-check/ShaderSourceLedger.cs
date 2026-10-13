using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using OpenTK.Graphics.OpenGL;
using MphRead;

// Existing shader-owner hook only; counts attempted compilation/link callbacks.
// Fixed-size hash ledger avoids retaining source bodies or native owner objects.
sealed class ShaderSourceLedger : IDisposable
{
    private readonly FieldInfo _callback;
    private readonly Action<string, int>? _previous;
    private readonly Dictionary<(string Stage, string Hash), int> _sources = new();
    private int _compileCallbacks, _linkCallbacks, _overflow;
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
        else if (stage.EndsWith("-program", StringComparison.Ordinal) || stage.EndsWith("-link", StringComparison.Ordinal)) _linkCallbacks++;
        _previous?.Invoke(stage, handle);
    }
    internal object Result() => new { compileAttemptCallbacks = _compileCallbacks, linkAttemptCallbacks = _linkCallbacks,
        sourceStageKeys = _sources.Count, uniqueSourceHashes = _sources.Keys.Select(k => k.Hash).Distinct().Count(), overflowCallbacks = _overflow,
        sources = _sources.Select(pair => new { stage = pair.Key.Stage, sourceSha256 = pair.Key.Hash, callbacks = pair.Value }).ToArray(),
        scope = "Existing canonical/world-variant shader-owner callback after ShaderSource and before compilation/link. GraphicsPipeline, outlines, HUD/UI paths without that hook are not profiled here. Attempts are not compile/link success; identical GLSL does not prove identical driver pipeline keys or safe program sharing." };
    public void Dispose() => _callback.SetValue(null, _previous);
}
