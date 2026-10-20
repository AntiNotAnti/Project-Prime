#if ANDROID
using System;
using ES = OpenTK.Graphics.ES30;

namespace MphRead.Mods.Render;

/// <summary>
/// Optional cached C ABI GLES calls for the measured scene submission surface.
/// Only the GL-owner thread resolves pointers; the binding generation invalidates
/// per-thread caches after context changes. Missing symbols use OpenTK GLES.
/// </summary>
internal static unsafe class AndroidGlFastCalls
{
    private static readonly bool Enabled = OpenGlOptimizationPolicy.AndroidPerformanceOptedIn
        && Environment.GetEnvironmentVariable("PROJECT_PRIME_ANDROID_GLES_DIRECT") != "0";

    [ThreadStatic] private static int _generation;
    [ThreadStatic] private static bool _resolved;
    [ThreadStatic] private static delegate* unmanaged[Cdecl]<uint, void> _bindVertexArray;
    [ThreadStatic] private static delegate* unmanaged[Cdecl]<uint, int, uint, nint, void> _drawElements;
    [ThreadStatic] private static delegate* unmanaged[Cdecl]<int, int, void> _uniform1i;
    [ThreadStatic] private static delegate* unmanaged[Cdecl]<int, float, float, float, float, void> _uniform4f;

    private static void Resolve()
    {
        int generation = OpenGlContextPolicy.Generation;
        if (_resolved && generation == _generation) return;
        _generation = generation;
        _resolved = true;
        _bindVertexArray = (delegate* unmanaged[Cdecl]<uint, void>)OpenGlContextPolicy.Resolve("glBindVertexArray");
        _drawElements = (delegate* unmanaged[Cdecl]<uint, int, uint, nint, void>)OpenGlContextPolicy.Resolve("glDrawElements");
        _uniform1i = (delegate* unmanaged[Cdecl]<int, int, void>)OpenGlContextPolicy.Resolve("glUniform1i");
        _uniform4f = (delegate* unmanaged[Cdecl]<int, float, float, float, float, void>)OpenGlContextPolicy.Resolve("glUniform4f");
    }

    internal static void BindVertexArray(int vao)
    {
        if (Enabled)
        {
            Resolve();
            if (_bindVertexArray != null) { _bindVertexArray((uint)vao); return; }
        }
        ES.GL.BindVertexArray(vao);
    }

    internal static void DrawElements(ES.PrimitiveType mode, int count,
        ES.DrawElementsType indexType, nint indexOffset)
    {
        if (Enabled)
        {
            Resolve();
            if (_drawElements != null)
            {
                _drawElements((uint)mode, count, (uint)indexType, indexOffset);
                return;
            }
        }
        ES.GL.DrawElements(mode, count, indexType, indexOffset);
    }

    internal static void Uniform1(int location, int value)
    {
        if (Enabled)
        {
            Resolve();
            if (_uniform1i != null) { _uniform1i(location, value); return; }
        }
        ES.GL.Uniform1(location, value);
    }

    internal static void Uniform4(int location, float x, float y, float z, float w)
    {
        if (Enabled)
        {
            Resolve();
            if (_uniform4f != null) { _uniform4f(location, x, y, z, w); return; }
        }
        ES.GL.Uniform4(location, x, y, z, w);
    }
}
#endif
