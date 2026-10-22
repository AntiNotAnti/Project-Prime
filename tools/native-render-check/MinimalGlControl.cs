using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using MphRead;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;

// Diagnostic-only compiler control. No Scene, assets, audio, preferences or caches.
static class MinimalGlControl
{
    static string? _glfwError;
    static readonly GLFWCallbacks.ErrorCallback GlfwError = (code, description) => {
        _glfwError = $"GLFW {code}: {description}";
        try { Console.Error.WriteLine("MINIMALGL " + _glfwError); } catch { /* Preserve the callback error for the managed receipt; never unwind native frames. */ }
    };
    internal sealed record Options(string Mode, string Variant, int Iterations, bool DepthVertex,
        string[] Defines, KeyValuePair<string, int>[] Bindings, int SamplerUnit, int HoldMs,
        string? AckDirectory, string Output, string? FailAt);
    internal sealed record Sources(string Variant, string Vertex, string Fragment, string VertexSha256,
        string FragmentSha256, string LinkKeySha256);
    internal static readonly string[] FailureStages = { "vertex-allocated", "fragment-allocated", "vertex-compiled",
        "fragment-compiled", "program-allocated", "attached", "linked", "bound", "drawn" };
    static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    static string FileHash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    internal static Options Parse(string[] args)
    {
        var values = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        string[] flags = { "--minimal-gl", "--minimal-gl-describe", "--minimal-gl-selftest", "--minimal-gl-depth-vertex" };
        string[] names = { "--minimal-gl-mode", "--minimal-gl-variant", "--minimal-gl-iterations", "--minimal-gl-define",
            "--minimal-gl-bind", "--minimal-gl-sampler-unit", "--minimal-gl-hold-ms", "--minimal-gl-phase-acks", "--minimal-gl-fail-at", "-output" };
        for (int i = 0; i < args.Length; i++)
        {
            string key = args[i];
            if (flags.Contains(key)) { if (values.ContainsKey(key)) throw new ArgumentException("Duplicate flag: " + key); values[key] = new(); continue; }
            if (!names.Contains(key) || i + 1 == args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException("Unknown or missing minimal-GL option: " + key);
            if (!values.TryGetValue(key, out var list)) values[key] = list = new();
            if (list.Count != 0 && key is not ("--minimal-gl-define" or "--minimal-gl-bind")) throw new ArgumentException("Duplicate option: " + key);
            list.Add(args[++i]);
        }
        string Get(string key, string fallback) => values.TryGetValue(key, out var list) && list.Count != 0 ? list[0] : fallback;
        string mode = Get("--minimal-gl-mode", "compile-delete"), variant = Get("--minimal-gl-variant", "canonical");
        if (mode is not ("no-shader" or "compile-only" or "compile-delete" or "fresh-contexts" or "rebind-draw")) throw new ArgumentException("Unknown minimal-GL mode.");
        if (variant is not ("tiny" or "canonical" or "native" or "plain" or "unlit" or "four-prewarm")) throw new ArgumentException("Unknown shader variant.");
        int iterations = int.Parse(Get("--minimal-gl-iterations", "30"));
        int sampler = int.Parse(Get("--minimal-gl-sampler-unit", "0")), hold = int.Parse(Get("--minimal-gl-hold-ms", "0"));
        if (iterations < 1 || iterations > 900 || sampler < 0 || sampler > 15 || hold < 0 || hold > 120000)
            throw new ArgumentException("Use iterations 1..900, sampler unit 0..15 and hold-ms 0..120000.");
        string[] defines = values.GetValueOrDefault("--minimal-gl-define", new()).ToArray();
        foreach (string define in defines)
            if (!System.Text.RegularExpressions.Regex.IsMatch(define, @"^[A-Za-z_][A-Za-z0-9_]*(?:=[A-Za-z0-9_.+-]+)?$")) throw new ArgumentException("Defines require NAME or NAME=scalar.");
        if (defines.Select(d => d.Split('=')[0]).Distinct().Count() != defines.Length) throw new ArgumentException("Duplicate define key.");
        var bindings = values.GetValueOrDefault("--minimal-gl-bind", new()).Select(value => {
            string[] parts = value.Split(':');
            if (parts.Length != 2 || !System.Text.RegularExpressions.Regex.IsMatch(parts[0], @"^[A-Za-z_][A-Za-z0-9_]*$")
                || !int.TryParse(parts[1], out int index) || index < 0 || index > 15) throw new ArgumentException("Binding requires attribute:0..15.");
            return new KeyValuePair<string, int>(parts[0], index);
        }).ToArray();
        if (bindings.Select(b => b.Key).Distinct().Count() != bindings.Length || bindings.Select(b => b.Value).Distinct().Count() != bindings.Length)
            throw new ArgumentException("Duplicate attribute name/index.");
        string? fail = Get("--minimal-gl-fail-at", "") is string f && f != "" ? f : null;
        if (fail != null && !FailureStages.Contains(fail)) throw new ArgumentException("Unknown failure stage.");
        if (fail is "bound" or "drawn" && mode is "compile-only" or "no-shader") throw new ArgumentException("Bind/draw failure stages require a drawing mode.");
        if (fail != null && mode == "no-shader") throw new ArgumentException("No-shader control cannot inject shader failure.");
        string? ack = Get("--minimal-gl-phase-acks", "") is string a && a != "" ? Path.GetFullPath(a) : null;
        return new(mode, variant, iterations, values.ContainsKey("--minimal-gl-depth-vertex"), defines,
            bindings, sampler, hold, ack, Path.GetFullPath(Get("-output", "minimal-gl.json")), fail);
    }

    internal static Sources[] SourceSet(Options options)
    {
        string Expand(string source)
        {
            int version = source.IndexOf("#version 120", StringComparison.Ordinal);
            if (version < 0) throw new InvalidOperationException("Expected GLSL 1.20 source.");
            int end = source.IndexOf('\n', version);
            return source.Insert(end + 1, string.Join("", options.Defines.Select(d => "#define " + d.Replace('=', ' ') + "\n")));
        }
        return (options.Variant == "four-prewarm" ? new[] { "canonical", "native", "plain", "unlit" } : new[] { options.Variant }).Select(variant => {
            string vertex = variant == "unlit" ? (options.DepthVertex ? Shaders.UnlitVertexShader : Shaders.UnlitLegacyVertexShader)
                : options.DepthVertex ? Shaders.VertexShader : Shaders.LegacyVertexShader;
            string fragment = variant == "canonical" ? Shaders.FragmentShader : variant == "native" ? Shaders.NativeFragmentShader : Shaders.NativeNoCosmeticsFragmentShader;
            if (variant == "tiny")
            {
                vertex = "#version 120\nvoid main() { gl_Position = gl_Vertex; }\n";
                fragment = "#version 120\nuniform vec4 override_color;\nvoid main() { gl_FragColor = override_color; }\n";
            }
            vertex = Expand(vertex); fragment = Expand(fragment);
            string vs = Hash(vertex), fs = Hash(fragment);
            string key = Hash(JsonSerializer.Serialize(new { vertexSha256 = vs, fragmentSha256 = fs,
                attributeBindings = options.Bindings.OrderBy(b => b.Key, StringComparer.Ordinal).ToArray(), linkOptions = "default" }));
            return new Sources(variant, vertex, fragment, vs, fs, key);
        }).ToArray();
    }

    // Small facade allows device-free tests of actual production rollback ordering.
    internal interface IGl
    {
        int CreateShader(bool vertex); void Source(int shader, string source); int Compile(int shader);
        int CreateProgram(); void Attach(int program, int shader); void BindAttribute(int program, string name, int index);
        int Link(int program); void Detach(int program, int shader); void DeleteShader(int shader); void DeleteProgram(int program);
        bool IsShader(int shader); bool IsProgram(int program); void Use(int program); object Draw(int program, int samplerUnit, int iteration); void Check();
    }
    sealed class NativeGl : IGl
    {
        readonly NativeWindow _window; readonly int _thread = Environment.CurrentManagedThreadId; readonly Dictionary<int, byte[]> _previousPixels = new();
        public NativeGl(NativeWindow window) { _window = window; }
        public void Check()
        {
            if (_thread != Environment.CurrentManagedThreadId || !_window.Context.IsCurrent) throw new InvalidOperationException("GL owner thread/current-context mismatch.");
            OpenTK.Graphics.OpenGL.ErrorCode error = GL.GetError(); if (error != OpenTK.Graphics.OpenGL.ErrorCode.NoError) throw new InvalidOperationException("Minimal GL error: " + error);
        }
        public int CreateShader(bool vertex) => GL.CreateShader(vertex ? ShaderType.VertexShader : ShaderType.FragmentShader);
        public void Source(int shader, string source) => GL.ShaderSource(shader, source);
        public int Compile(int shader) { GL.CompileShader(shader); GL.GetShader(shader, ShaderParameter.CompileStatus, out int status); return status; }
        public int CreateProgram() => GL.CreateProgram();
        public void Attach(int program, int shader) => GL.AttachShader(program, shader);
        public void BindAttribute(int program, string name, int index) => GL.BindAttribLocation(program, index, name);
        public int Link(int program) { GL.LinkProgram(program); GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int status); return status; }
        public void Detach(int program, int shader) => GL.DetachShader(program, shader);
        public void DeleteShader(int shader) => GL.DeleteShader(shader);
        public void DeleteProgram(int program) => GL.DeleteProgram(program);
        public bool IsShader(int shader) => GL.IsShader(shader);
        public bool IsProgram(int program) => GL.IsProgram(program);
        public void Use(int program) => GL.UseProgram(program);
        public object Draw(int program, int samplerUnit, int iteration)
        {
            void Scalar(string name, int value) { int location = GL.GetUniformLocation(program, name); if (location >= 0) GL.Uniform1(location, value); }
            foreach (string name in new[] { "proj_mtx", "view_mtx", "view_inv_mtx", "tex_mtx" }.Concat(Enumerable.Range(0, 32).Select(i => $"mtx_stack[{i}]")))
            { int location = GL.GetUniformLocation(program, name); if (location >= 0) { Matrix4 identity = Matrix4.Identity; GL.UniformMatrix4(location, false, ref identity); } }
            Scalar("use_override", 1); Scalar("use_texture", 0); Scalar("weighted_skinning", 0);
            foreach (string name in new[] { "tex", "normal_tex", "specular_tex", "emissive_tex" }) Scalar(name, samplerUnit);
            int color = GL.GetUniformLocation(program, "override_color");
            if (color >= 0) GL.Uniform4(color, (iteration % 3 + 1) / 3f, 0.5f, 0.25f, 1f);
            GL.Viewport(0, 0, 32, 32); GL.ClearColor(0, 0, 0, 1); GL.Clear(ClearBufferMask.ColorBufferBit);
            GL.Color4(1f, 1f, 1f, 1f); GL.Normal3(0f, 0f, 1f); GL.TexCoord3(0f, 0f, 0f);
            GL.Begin(PrimitiveType.Triangles); GL.Vertex3(-0.5f, -0.5f, 0); GL.Vertex3(0.5f, -0.5f, 0); GL.Vertex3(0, 0.5f, 0); GL.End();
            GL.Finish();
            var pixels = new byte[32 * 32 * 4]; GL.ReadPixels(0, 0, 32, 32, PixelFormat.Rgba, PixelType.UnsignedByte, pixels);
            int coverage = Enumerable.Range(0, 32 * 32).Count(i => pixels[i * 4] != 0 || pixels[i * 4 + 1] != 0 || pixels[i * 4 + 2] != 0);
            int? changed = _previousPixels.TryGetValue(program, out var prior) ? Enumerable.Range(0, 32 * 32).Count(i => !pixels.AsSpan(i * 4, 4).SequenceEqual(prior.AsSpan(i * 4, 4))) : null;
            if (coverage == 0 || changed == 0) throw new InvalidOperationException("Vacuous or unchanged synthetic triangle pixels.");
            _previousPixels[program] = pixels;
            return new { nonBlackPixels = coverage, changedPixels = changed, rgbaSha256 = Convert.ToHexString(SHA256.HashData(pixels)),
                drawKeySha256 = Hash(JsonSerializer.Serialize(new { primitive = "triangle", size = new[] { 32, 32 }, matrices = "identity", texture = false, samplerUnit, overrideRed = (iteration % 3 + 1) / 3f })) };
        }
    }
    internal sealed class ProgramOwner : IDisposable
    {
        readonly IGl _gl; readonly Action<object> _record; readonly int _context, _generation;
        int _vertex, _fragment, _program; bool _vertexAttached, _fragmentAttached;
        internal int Program => _program;
        internal ProgramOwner(IGl gl, Sources source, Options options, int context, int generation, Action<object> record)
        {
            _gl = gl; _record = record; _context = context; _generation = generation;
            void Fail(string stage) { if (options.FailAt == stage) throw new InvalidOperationException("Injected minimal GL failure: " + stage); }
            try
            {
                gl.Check();
                _vertex = gl.CreateShader(true); record(new { context, generation, operation = "create-shader", stage = "vertex", nativeName = _vertex, source.VertexSha256 }); if (_vertex == 0) throw new InvalidOperationException("Vertex allocation failed."); Fail("vertex-allocated");
                _fragment = gl.CreateShader(false); record(new { context, generation, operation = "create-shader", stage = "fragment", nativeName = _fragment, source.FragmentSha256 }); if (_fragment == 0) throw new InvalidOperationException("Fragment allocation failed."); Fail("fragment-allocated");
                gl.Source(_vertex, source.Vertex); gl.Source(_fragment, source.Fragment);
                int vertexStatus = gl.Compile(_vertex);
                record(new { context, generation, thread = Environment.CurrentManagedThreadId, operation = "compile", stage = "vertex", sourceSha256 = source.VertexSha256, status = vertexStatus });
                if (vertexStatus == 0) throw new InvalidOperationException("Vertex compilation failed."); Fail("vertex-compiled");
                int fragmentStatus = gl.Compile(_fragment);
                record(new { context, generation, thread = Environment.CurrentManagedThreadId, operation = "compile", stage = "fragment", sourceSha256 = source.FragmentSha256, status = fragmentStatus });
                if (fragmentStatus == 0) throw new InvalidOperationException("Fragment compilation failed."); Fail("fragment-compiled");
                _program = gl.CreateProgram(); record(new { context, generation, operation = "create-program", nativeName = _program, source.LinkKeySha256 }); if (_program == 0) throw new InvalidOperationException("Program allocation failed."); Fail("program-allocated");
                gl.Attach(_program, _vertex); _vertexAttached = true; gl.Attach(_program, _fragment); _fragmentAttached = true; Fail("attached");
                foreach (var binding in options.Bindings) gl.BindAttribute(_program, binding.Key, binding.Value);
                int linkStatus = gl.Link(_program);
                record(new { context, generation, thread = Environment.CurrentManagedThreadId, operation = "link", source.Variant, source.LinkKeySha256, status = linkStatus });
                if (linkStatus == 0) throw new InvalidOperationException("Program link failed."); Fail("linked");
                gl.Detach(_program, _vertex); _vertexAttached = false; gl.Detach(_program, _fragment); _fragmentAttached = false;
                int vertex = _vertex, fragment = _fragment;
                gl.DeleteShader(vertex); _vertex = 0; gl.DeleteShader(fragment); _fragment = 0;
                if (gl.IsShader(vertex) || gl.IsShader(fragment)) throw new InvalidOperationException("Detached shaders remain recognized.");
                gl.Check();
            }
            catch (Exception original)
            {
                try { Dispose(); } catch (Exception cleanup) { throw new AggregateException("Minimal GL initialization and rollback failed.", original, cleanup); }
                throw;
            }
        }
        internal void Draw(Options options, int iteration)
        {
            _gl.Check(); _gl.Use(_program);
            _record(new { context = _context, generation = _generation, thread = Environment.CurrentManagedThreadId, operation = "use-program", iteration, options.SamplerUnit });
            if (options.FailAt == "bound") throw new InvalidOperationException("Injected minimal GL failure: bound");
            object pixels = _gl.Draw(_program, options.SamplerUnit, iteration);
            if (options.FailAt == "drawn") throw new InvalidOperationException("Injected minimal GL failure: drawn");
            _gl.Check();
            _record(new { context = _context, generation = _generation, thread = Environment.CurrentManagedThreadId, operation = "draw", iteration, options.SamplerUnit, pixels });
        }
        public void Dispose()
        {
            int program = _program, vertex = _vertex, fragment = _fragment;
            // Continue every cleanup attempt even if one deletion fails; retain errors.
            var errors = new List<Exception>();
            void Try(Action action) { try { action(); } catch (Exception error) { errors.Add(error); } }
            Try(() => _gl.Check()); Try(() => _gl.Use(0));
            if (program != 0 && _vertexAttached) Try(() => _gl.Detach(program, vertex));
            if (program != 0 && _fragmentAttached) Try(() => _gl.Detach(program, fragment));
            if (vertex != 0) Try(() => _gl.DeleteShader(vertex));
            if (fragment != 0) Try(() => _gl.DeleteShader(fragment));
            if (program != 0) Try(() => _gl.DeleteProgram(program));
            Try(() => { if ((vertex != 0 && _gl.IsShader(vertex)) || (fragment != 0 && _gl.IsShader(fragment)) || (program != 0 && _gl.IsProgram(program))) throw new InvalidOperationException("Deleted owned GL names remain recognized."); });
            Try(() => _gl.Check());
            _record(new { context = _context, generation = _generation, thread = Environment.CurrentManagedThreadId,
                operation = "retire", programNames = program == 0 ? 0 : 1, remainingRecognized = errors.Count == 0 ? 0 : (int?)null, cleanupErrors = errors.Select(e => e.ToString()).ToArray() });
            _program = _vertex = _fragment = 0; _vertexAttached = _fragmentAttached = false;
            if (errors.Count != 0) throw new AggregateException("Minimal GL cleanup failed.", errors);
        }
    }

    internal static int Run(string[] args)
    {
        Options options = Parse(args);
        Sources[] sources = SourceSet(options);
        object Description() => new { schema = 1, kind = "minimal-gl-compiler-control", options.Mode, options.Variant, options.Iterations,
            options.DepthVertex, options.Defines, options.Bindings, options.SamplerUnit, sharedContexts = "NOT_RUN: only serial fresh unshared contexts are implemented",
            sources = sources.Select(s => new { s.Variant, s.VertexSha256, s.FragmentSha256, s.LinkKeySha256, vertexBytes = Encoding.UTF8.GetByteCount(s.Vertex), fragmentBytes = Encoding.UTF8.GetByteCount(s.Fragment) }) };
        if (args.Contains("--minimal-gl-describe")) { Console.WriteLine(JsonSerializer.Serialize(Description(), new JsonSerializerOptions { WriteIndented = true })); return 0; }
        if (File.Exists(options.Output)) throw new IOException("Minimal GL output must be fresh.");
        if (options.AckDirectory != null) { if (Directory.Exists(options.AckDirectory) || File.Exists(options.AckDirectory)) throw new IOException("Acknowledgement directory must be fresh."); Directory.CreateDirectory(options.AckDirectory); }
        var records = new List<object>(); string? failure = null; int generation = 0, completed = 0;
        Console.WriteLine($"MINIMALGL pid={Environment.ProcessId}");
        void Phase(int context, string phase)
        {
            string stem = $"minimal-{context:D3}-{phase}";
            records.Add(new { context, phase, thread = Environment.CurrentManagedThreadId, memory = ResourceAttribution.MemorySample() });
            Console.WriteLine($"MINIMALGL phase={stem} context={context} pid={Environment.ProcessId}");
            Thread.Sleep(options.HoldMs);
            if (options.AckDirectory != null)
            {
                var started = Stopwatch.StartNew();
                while (!File.Exists(Path.Combine(options.AckDirectory, stem + ".ack")))
                { if (started.Elapsed.TotalSeconds > 90) throw new TimeoutException("External capture acknowledgement missing: " + stem); Thread.Sleep(100); }
            }
        }
        try
        {
            int contexts = options.Mode is "fresh-contexts" or "no-shader" ? options.Iterations : 1;
            for (int context = 0; context < contexts; context++)
            {
                NativeWindow? window = null;
                try
                {
                    // Standalone setup avoids DesktopGlContext.Settings, which loads preferences.
                    GLFWProvider.SetErrorCallback(GlfwError);
                    if (OperatingSystem.IsMacOS()) { GLFW.InitHint(InitHintBool.CocoaChdirResources, false); GLFW.InitHint(InitHintBool.CocoaMenubar, false); }
                    GLFWProvider.EnsureInitialized();
                    unsafe { if (GLFW.GetPrimaryMonitor() == null) throw new InvalidOperationException("Minimal GL requires an active primary display."); }
                    GLFW.DefaultWindowHints();
                    window = new NativeWindow(new NativeWindowSettings { ClientSize = new Vector2i(32, 32), Title = "Prime minimal GL control",
                        API = ContextAPI.OpenGL, APIVersion = OperatingSystem.IsMacOS() ? new Version(2, 1) : new Version(3, 2),
                        Profile = OperatingSystem.IsMacOS() ? ContextProfile.Any : ContextProfile.Compatability, Flags = ContextFlags.Default,
                        SharedContext = null, StartVisible = false, StartFocused = false });
                    window.Context.MakeCurrent(); GL.LoadBindings(new GLFWBindingsContext());
                    var gl = new NativeGl(window); gl.Check();
                    if (_glfwError != null) throw new InvalidOperationException(_glfwError);
                    unsafe { records.Add(new { context, shareGroup = $"unshared-{context}", thread = Environment.CurrentManagedThreadId,
                        nativeWindow = ((IntPtr)window.WindowPtr).ToString("X"), vendor = GL.GetString(StringName.Vendor), renderer = GL.GetString(StringName.Renderer), glVersion = GL.GetString(StringName.Version) }); }
                    Phase(context, "before-workload");
                    int iterations = options.Mode is "compile-delete" or "compile-only" ? options.Iterations : 1;
                    for (int iteration = 0; iteration < iterations; iteration++)
                    {
                        var owners = new List<ProgramOwner>();
                        try
                        {
                            if (options.Mode != "no-shader") foreach (Sources source in sources) owners.Add(new ProgramOwner(gl, source, options, context, ++generation, records.Add));
                            if (options.Mode == "rebind-draw") for (int draw = 0; draw < options.Iterations; draw++) foreach (ProgramOwner owner in owners) owner.Draw(options, draw);
                            else if (options.Mode != "compile-only") foreach (ProgramOwner owner in owners) owner.Draw(options, iteration);
                        }
                        finally
                        {
                            var errors = new List<Exception>();
                            foreach (var owner in owners.AsEnumerable().Reverse()) try { owner.Dispose(); } catch (Exception error) { errors.Add(error); }
                            if (errors.Count != 0) throw new AggregateException(errors);
                        }
                        gl.Check(); completed++;
                    }
                    Phase(context, "after-workload");
                }
                finally
                {
                    if (window != null) { window.Dispose(); if (window.Exists) throw new InvalidOperationException("Disposed minimal GL window still exists."); }
                }
                Phase(context, "after-disposal");
            }
        }
        catch (Exception error) { failure = error.ToString(); }
        string client = typeof(Shaders).Assembly.Location, harness = typeof(MinimalGlControl).Assembly.Location;
        var result = new { schema = 1, kind = "minimal-gl-compiler-control", description = Description(), command = args,
            processId = Environment.ProcessId, runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            os = System.Runtime.InteropServices.RuntimeInformation.OSDescription, openTkVersion = typeof(GL).Assembly.GetName().Version?.ToString(),
            clientSha256 = FileHash(client), harnessSha256 = FileHash(harness), clientMvid = typeof(Shaders).Module.ModuleVersionId, harnessMvid = typeof(MinimalGlControl).Module.ModuleVersionId,
            records, completedUnits = completed, compileProgramGenerations = generation, complete = failure == null, error = failure,
            sharedContexts = "NOT_RUN", drawPipelineCoverage = options.Mode is "compile-only" or "no-shader" ? "NOT_RUN" : "synthetic triangle with nonzero pixel coverage", boundedness = "UNVERIFIED", limitations = "Compiler/lifecycle controls only; GL-name retirement is not allocator closure. Rebind/draw uses a synthetic untextured triangle and changing override color, not gameplay parity or full material/pipeline state. Defines and attribute locations can be inactive; exact hashes are identity, not proof of driver key equivalence. No driver leak or memory improvement conclusion." };
        Directory.CreateDirectory(Path.GetDirectoryName(options.Output)!);
        File.WriteAllText(options.Output, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }) + "\n");
        Console.WriteLine("MINIMALGL " + (failure == null ? "PASS " : "FAIL ") + options.Output);
        return failure == null ? 0 : 1;
    }

    sealed class FakeGl : IGl
    {
        internal readonly HashSet<int> Shaders = new(), Programs = new();
        internal readonly HashSet<(int, int)> Attached = new(); internal int Current, Next, ShaderCreates, CompileCalls, AttachCalls; internal bool CompileFailure, LinkFailure, FragmentCompileFailure; internal string? AllocationFailure;
        public int CreateShader(bool vertex) { ShaderCreates++; if (AllocationFailure == "shader-" + ShaderCreates) return 0; int id = ++Next; Shaders.Add(id); return id; }
        public void Source(int shader, string source) { if (source.Length == 0) throw new Exception("empty source"); }
        public int Compile(int shader) { CompileCalls++; return CompileFailure || (FragmentCompileFailure && CompileCalls == 2) ? 0 : 1; }
        public int CreateProgram() { if (AllocationFailure == "program-zero") return 0; int id = ++Next; Programs.Add(id); return id; }
        public void Attach(int program, int shader) { AttachCalls++; if (AllocationFailure == "attach-second" && AttachCalls == 2) throw new InvalidOperationException("second attachment failed"); Attached.Add((program, shader)); }
        public void BindAttribute(int program, string name, int index) { }
        public int Link(int program) => LinkFailure ? 0 : 1;
        public void Detach(int program, int shader) => Attached.Remove((program, shader));
        public void DeleteShader(int shader) { if (Attached.Any(p => p.Item2 == shader)) throw new Exception("shader still attached"); Shaders.Remove(shader); }
        public void DeleteProgram(int program) { if (Current == program) throw new Exception("program still bound"); Programs.Remove(program); }
        public bool IsShader(int shader) => Shaders.Contains(shader);
        public bool IsProgram(int program) => Programs.Contains(program);
        public void Use(int program) { if (program != 0 && !Programs.Contains(program)) throw new Exception("deleted program reused"); Current = program; }
        public object Draw(int program, int samplerUnit, int iteration) { if (Current != program) throw new Exception("draw without binding"); return new { nonBlackPixels = 128, changedPixels = 128 }; }
        public void Check() { }
    }
    internal static int SelfTest()
    {
        bool pass = true; int cases = 0;
        foreach (string? stage in new string?[] { null }.Concat(FailureStages).Concat(new[] { "compile-status", "fragment-status", "link-status", "shader-1", "shader-2", "program-zero", "attach-second" }))
        {
            Options options = Parse(new[] { "--minimal-gl", "--minimal-gl-mode", "rebind-draw" });
            if (FailureStages.Contains(stage)) options = options with { FailAt = stage };
            var gl = new FakeGl { CompileFailure = stage == "compile-status", FragmentCompileFailure = stage == "fragment-status", LinkFailure = stage == "link-status", AllocationFailure = stage };
            bool threw = false;
            try { using var owner = new ProgramOwner(gl, SourceSet(options)[0], options, 0, 1, _ => { }); owner.Draw(options, 0); }
            catch (InvalidOperationException) { threw = true; }
            pass &= threw == (stage != null) && gl.Shaders.Count == 0 && gl.Programs.Count == 0 && gl.Attached.Count == 0 && gl.Current == 0; cases++;
        }
        Options standard = Parse(new[] { "--minimal-gl", "--minimal-gl-variant", "four-prewarm" });
        Sources[] variants = SourceSet(standard);
        pass &= variants.Length == 4 && variants.Select(s => s.LinkKeySha256).Distinct().Count() == 4;
        var define = SourceSet(standard with { Defines = new[] { "CONTROL=1" } });
        pass &= !define[0].VertexSha256.Equals(variants[0].VertexSha256) && define[0].Vertex.StartsWith("\n#version 120\n#define CONTROL 1\n", StringComparison.Ordinal);
        var binding = SourceSet(standard with { Bindings = new[] { new KeyValuePair<string, int>("probe", 1) } });
        pass &= binding[0].VertexSha256 == variants[0].VertexSha256 && binding[0].LinkKeySha256 != variants[0].LinkKeySha256;
        foreach (string[] invalid in new[] { new[] { "--minimal-gl", "--minimal-gl-iterations", "0" }, new[] { "--minimal-gl", "--minimal-gl-variant", "unknown" },
            new[] { "--minimal-gl", "--minimal-gl-define", "BAD\nBODY" }, new[] { "--minimal-gl", "--minimal-gl-bind", "a:0", "--minimal-gl-bind", "b:0" },
            new[] { "--minimal-gl", "--minimal-gl-mode", "no-shader", "--minimal-gl-fail-at", "linked" } })
            try { Parse(invalid); pass = false; } catch (ArgumentException) { cases++; }
        Console.WriteLine($"MINIMALGL SELFTEST {(pass ? "PASS" : "FAIL")} rollbackCases={cases} exactSources=True linkKeyInputs=True deviceFree=True sharedContexts=NOT_RUN");
        return pass ? 0 : 1;
    }
}
