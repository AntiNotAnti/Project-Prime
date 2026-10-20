using System;
using System.Collections.Generic;

namespace MphRead.Mods.Render;

internal enum OpenGlVertexArrayApi { None, Core, Apple }

/// <summary>Immutable facts from one live context. Extension names are whole tokens.</summary>
internal sealed class OpenGlCapabilities
{
    internal string Version { get; }
    internal bool IsEs { get; }
    internal int Major { get; }
    internal int Minor { get; }
    private readonly HashSet<string> _extensions;
    private readonly Func<string, bool> _entryPoint;
    internal OpenGlCapabilities(string version, string extensions, Func<string, bool> entryPoint)
    {
        Version = version; IsEs = version.StartsWith("OpenGL ES", StringComparison.Ordinal);
        string numeric = IsEs ? version[9..].TrimStart(' ', '-', 'C', 'M', 'L') : version;
        string token = numeric.Split(' ', StringSplitOptions.RemoveEmptyEntries) is { Length: > 0 } tokens ? tokens[0] : "";
        string[] parts = token.Split('.');
        Major = parts.Length >= 2 && int.TryParse(parts[0], out int major) ? major : 0;
        Minor = parts.Length >= 2 && int.TryParse(parts[1], out int minor) ? minor : 0;
        _extensions = new HashSet<string>(extensions.Split(' ', StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal);
        _entryPoint = entryPoint;
    }
    internal bool Has(string extension) => _extensions.Contains(extension);
    internal bool AtLeast(int major, int minor) => Major > major || Major == major && Minor >= minor;
    private bool Entries(params string[] names) => Array.TrueForAll(names, name => _entryPoint(name));
    internal bool VertexArrays => (AtLeast(3, 0) || !IsEs && Has("GL_ARB_vertex_array_object"))
        && Entries("glGenVertexArrays", "glBindVertexArray", "glDeleteVertexArrays");
    internal bool AppleVertexArrays => !IsEs && Has("GL_APPLE_vertex_array_object")
        && Entries("glGenVertexArraysAPPLE", "glBindVertexArrayAPPLE", "glDeleteVertexArraysAPPLE", "glIsVertexArrayAPPLE");
    internal OpenGlVertexArrayApi DesktopVertexArrayApi => IsEs ? OpenGlVertexArrayApi.None
        : VertexArrays ? OpenGlVertexArrayApi.Core
        : AppleVertexArrays ? OpenGlVertexArrayApi.Apple : OpenGlVertexArrayApi.None;
    internal bool BufferRange => (AtLeast(3, 0) || !IsEs && Has("GL_ARB_map_buffer_range"))
        && Entries("glMapBufferRange", "glUnmapBuffer");
    internal bool Sync => (AtLeast(IsEs ? 3 : 3, IsEs ? 0 : 2) || !IsEs && Has("GL_ARB_sync"))
        && Entries("glFenceSync", "glClientWaitSync", "glDeleteSync");
    internal bool TimerQueries => !IsEs && (AtLeast(3, 3) || Has("GL_ARB_timer_query"))
        && Entries("glGenQueries", "glBeginQuery", "glEndQuery", "glGetQueryObjectiv", "glGetQueryObjectui64v", "glDeleteQueries");
    internal bool DisjointTimerQueries => IsEs && Has("GL_EXT_disjoint_timer_query")
        && Entries("glGenQueriesEXT", "glBeginQueryEXT", "glEndQueryEXT", "glGetQueryObjectivEXT", "glGetQueryObjectui64vEXT", "glDeleteQueriesEXT");
    internal bool FramebufferBlit => (AtLeast(3, 0) || !IsEs
        && (Has("GL_ARB_framebuffer_object") || Has("GL_EXT_framebuffer_blit")))
        && Entries("glBlitFramebuffer", "glBindFramebuffer", "glRenderbufferStorage");
    internal bool UniformBuffers => (AtLeast(3, IsEs ? 0 : 1) || !IsEs && Has("GL_ARB_uniform_buffer_object"))
        && Entries("glGetUniformBlockIndex", "glUniformBlockBinding", "glBindBufferRange");
    internal bool Instancing => (AtLeast(3, IsEs ? 0 : 3) || !IsEs && Has("GL_ARB_instanced_arrays"))
        && Entries("glDrawElementsInstanced", "glVertexAttribDivisor");
    internal bool PersistentStorage => !IsEs && (AtLeast(4, 4) || Has("GL_ARB_buffer_storage"))
        && BufferRange && Sync && Entries("glBufferStorage");
    internal bool DebugLabels => (AtLeast(IsEs ? 3 : 4, IsEs ? 2 : 3) || Has("GL_KHR_debug"))
        && Entries("glObjectLabel");
}

internal static class OpenGlOptimizationPolicy
{
    internal static bool Select(bool supported, bool forcedLegacy, string? experiment, bool validatedDefault = false)
        => supported && !forcedLegacy && (experiment == "1" || experiment != "0" && validatedDefault);
    // A single Android-specific enrollment controls both existing and newly
    // introduced OpenGL optimization gates. Individual PROJECT_PRIME_GL_* = 0
    // overrides remain authoritative; force-legacy always wins.
    internal static bool AndroidPerformanceOptedIn
    {
        get
        {
#if ANDROID && PROJECTPRIME_ANDROID_PERF_OPT_IN
            return Environment.GetEnvironmentVariable("PROJECT_PRIME_ANDROID_PERF_OPT_IN") != "0";
#else
            return false;
#endif
        }
    }
    internal static bool DefaultEnabled(string name) => name is "VAO" or "DYNAMIC_STREAM"
        or "UNIFORM_REVISIONS" or "STATIC_CACHE" or "SHADOW_FAST" or "HUD_SAMPLERS" or "DEPTH_BATCH"
        or "ATLAS_EVICTION" or "HUD_DISCARD" or "DEPTH_SHADER" or "GLES_UNIFORMS" or "HUD_GLYPHS" or "HUD_SPRITES" or "DEPTH_PREP" or "PROCEDURAL_BATCH" or "ROOM_CACHE" or "DEPTH_KEEP" or "SHADER_VARIANTS" or "ROOM_GROUPS" or "PLAIN_SHADER" or "SHADOW_PREP" or "ROOM_IDENTITY" or "MESH_OBJECT" or "UNLIT_SHADER" or "SHADOW_GROUPS";
    internal static bool Enabled(string name, bool supported = true, bool forcedLegacy = false)
        => Select(supported, ForceLegacy || forcedLegacy,
            Environment.GetEnvironmentVariable("PROJECT_PRIME_GL_" + name),
            DefaultEnabled(name) || AndroidPerformanceOptedIn);
    internal static bool Requested(string name) => Environment.GetEnvironmentVariable("PROJECT_PRIME_GL_" + name) == "1";
    internal static bool ForceLegacy => Requested("FORCE_LEGACY") || Array.Exists(Environment.GetCommandLineArgs(), a => a.Equals("-gllegacy", StringComparison.OrdinalIgnoreCase));
}

#if !MPHREAD_SERVER
internal static class OpenGlContextPolicy
{
    [ThreadStatic] private static OpenGlCapabilities? _current;
    [ThreadStatic] private static Func<string, IntPtr>? _resolve;
    [ThreadStatic] private static int _generation;
    internal static int Generation => _generation;
    internal static void Reset(Func<string, IntPtr>? resolve = null)
    { _current = null; _generation++; DepthReplaySnapshot.ResetContext(); if (resolve != null) _resolve = resolve; }
    internal static OpenGlCapabilities Current => _current ??= Probe();
    internal static IntPtr Resolve(string name)
    {
#if !ANDROID
        _resolve ??= new OpenTK.Windowing.GraphicsLibraryFramework.GLFWBindingsContext().GetProcAddress;
#endif
        return _resolve?.Invoke(name) ?? IntPtr.Zero;
    }
    internal static bool Vao => OpenGlOptimizationPolicy.Enabled("VAO",
        Current.DesktopVertexArrayApi != OpenGlVertexArrayApi.None, !DesktopVboEnabled);
    internal static OpenGlVertexArrayApi VaoApi => Vao ? Current.DesktopVertexArrayApi : OpenGlVertexArrayApi.None;
    private static bool DesktopVboEnabled
    {
        get
        {
#if ANDROID
            return false;
#else
            return DesktopRetainedGeometry.Enabled;
#endif
        }
    }
    private static OpenGlCapabilities Probe()
    {
        string version = GraphicsApi.GetString(OpenTK.Graphics.OpenGL.StringName.Version) ?? "";
        // Existing desktop contexts are compatibility, GLES3 supports the extension string.
        string extensions = GraphicsApi.GetString(OpenTK.Graphics.OpenGL.StringName.Extensions) ?? "";
        var facts = new OpenGlCapabilities(version, extensions, name => Resolve(name) != IntPtr.Zero);
        Console.WriteLine($"[glcapabilities] version={version}; vao={facts.VertexArrays}; appleVao={facts.AppleVertexArrays}; desktopVaoApi={facts.DesktopVertexArrayApi}; timer={facts.TimerQueries}; disjointTimer={facts.DisjointTimerQueries}; sync={facts.Sync}; ubo={facts.UniformBuffers}; storage={facts.PersistentStorage}; optimizations=capability-and-policy; forceLegacy={OpenGlOptimizationPolicy.ForceLegacy}");
        return facts;
    }
}
#endif
