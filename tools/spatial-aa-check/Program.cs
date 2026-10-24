using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using MphRead;
using MphRead.Mods;
using MphRead.Mods.Render;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using OpenTK.Windowing.Desktop;
using G = MphRead.Mods.Render.GraphicsApi;

bool requested = args.Contains("--enabled");
bool forceLegacy = args.Contains("--force-legacy")
    || Environment.GetEnvironmentVariable("PROJECT_PRIME_GL_FORCE_LEGACY") == "1";
Environment.SetEnvironmentVariable("PROJECT_PRIME_GL_SPATIAL_AA", requested ? "1" : null);
Environment.SetEnvironmentVariable("PROJECT_PRIME_GL_FORCE_LEGACY", forceLegacy ? "1" : null);
bool enabled = requested && !forceLegacy
    && !args.Any(a => a.Equals("-gllegacy", StringComparison.OrdinalIgnoreCase));
return Run(enabled);

// Keep Scene and graphics type initialization behind a non-inlined boundary.
// Scene is beforefieldinit; JIT compilation of this method must follow policy setup.
[MethodImpl(MethodImplOptions.NoInlining)]
static int Run(bool enabled)
{
GraphicsBackendPolicy.Configure("opengl");
var settings = DesktopGlContext.Settings(background: true);
settings.ClientSize = new(65, 65);
using var window = new NativeWindow(settings);
using var session = new DesktopGraphicsSession(window);
const int width = 64, height = 64;
var scene = (Scene)RuntimeHelpers.GetUninitializedObject(typeof(Scene));
var applySpatial = (Action)typeof(Scene).GetMethod("ApplySpatialAntialiasing", BindingFlags.Instance | BindingFlags.NonPublic)!
    .CreateDelegate(typeof(Action), scene);
scene.Size = new(width, height);
// Uninitialized test scenes must still own the collections read by aggregate target accounting.
foreach (string name in new[] { "_frameTransientTextures", "_supersampleResolveTargets" })
{
    var field = typeof(Scene).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!;
    field.SetValue(scene, Activator.CreateInstance(field.FieldType));
}
int input = G.GenTexture(), framebuffer = G.GenFramebuffer();
try
{
    Set("_screenTexture", input); Set("_frameBuffer", framebuffer); Set("_targetSize", new Vector2i(width, height));
    G.ActiveTexture(TextureUnit.Texture0); G.BindTexture(TextureTarget.Texture2D, input);
    G.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
    G.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
    G.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
    G.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);
    G.BindFramebuffer(FramebufferTarget.Framebuffer, framebuffer);
    byte[] flat = Pixels((x, y) => new byte[] { 64, 128, 192, 128 });
    Upload(flat, width, height);
    G.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, input, 0);
    Check(G.CheckFramebufferStatus(FramebufferTarget.Framebuffer) == FramebufferErrorCode.FramebufferComplete, "input framebuffer complete");
    Apply();
    if (!enabled)
    {
        Check(Get<int>("_spatialAaProgram") == 0 && Get<int>("_spatialAaTexture") == 0, "default off allocates no AA owners");
        Check(Read(width, height).SequenceEqual(flat), "default off preserves original RGBA bytes");
        Console.WriteLine("SPATIAL_AA_DEFAULT_OFF PASS");
        return 0;
    }
    Check(Get<int>("_spatialAaProgram") != 0 && Get<int>("_spatialAaTexture") != 0, "optional shader and scratch target published");
    Check(Read(width, height).SequenceEqual(flat), "flat encoded RGB/alpha preserved byte for byte");

    var diagonal = Pixels((x, y) => { byte c = (byte)(x > y ? 255 : 0); return new byte[] { c, c, c, 255 }; });
    Upload(diagonal, width, height);
    G.Enable(EnableCap.ScissorTest); G.Scissor(0, 0, 1, 1);
    G.Enable(EnableCap.Blend); G.Enable(EnableCap.DepthTest); G.DepthMask(true);
    G.ColorMask(true, false, true, false);
    G.PolygonMode(TriangleFace.FrontAndBack, OpenTK.Graphics.OpenGL.PolygonMode.Line);
    G.ActiveTexture(TextureUnit.Texture1);
    Apply();
    Check(G.GetInteger(GetPName.ActiveTexture) == (int)TextureUnit.Texture1 && G.IsEnabled(EnableCap.ScissorTest)
        && G.IsEnabled(EnableCap.Blend) && G.IsEnabled(EnableCap.DepthTest) && G.GetInteger(GetPName.DepthWritemask) != 0,
        "incoming texture unit, enables and depth mask restored");
    int[] mask = new int[4], modes = new int[2];
    G.GetInteger(GetPName.ColorWritemask, mask); G.GetInteger(GetPName.PolygonMode, modes);
    Check(mask.SequenceEqual(new[] { 1, 0, 1, 0 }) && modes.All(v => v == (int)OpenTK.Graphics.OpenGL.PolygonMode.Line), "write mask and wireframe restored");
    G.Disable(EnableCap.ScissorTest); G.Disable(EnableCap.Blend); G.Disable(EnableCap.DepthTest);
    G.ColorMask(true, true, true, true); G.PolygonMode(TriangleFace.FrontAndBack, OpenTK.Graphics.OpenGL.PolygonMode.Fill);
    G.ActiveTexture(TextureUnit.Texture0);
    byte[] filtered = Read(width, height);
    int changed = Enumerable.Range(0, width * height).Count(i => filtered[i * 4] > 0 && filtered[i * 4] < 255);
    Check(changed > 0 && changed < width * 4, "diagonal edge filtered locally without full-frame wash");
    Check(Enumerable.Range(0, width * height).All(i => filtered[i * 4 + 3] == 255), "opaque alpha retained");
    Check(filtered[(1 * width + 1) * 4] < 255 && filtered[(1 * width + width - 2) * 4] == 255, "edge border clamps without repeat-wrap contamination");
    string? captures = Environment.GetEnvironmentVariable("PRIME_SPATIAL_AA_CAPTURES");
    if (captures != null)
    {
        Directory.CreateDirectory(captures);
        Save(diagonal, Path.Combine(captures, "diagonal-original.png"));
        Save(filtered, Path.Combine(captures, "diagonal-spatial.png"));
    }

    byte[] alphaEdges = Pixels((x, y) => { byte c = (byte)(((x + y) % 2) * 255); return new byte[] { c, 40, (byte)(255 - c), c }; });
    Upload(alphaEdges, width, height); Apply();
    Check(Read(width, height).SequenceEqual(alphaEdges), "alpha-discontinuous neighborhoods bypass RGB smoothing and retain alpha");
    Upload(flat, width, height); Apply();
    G.Enable(EnableCap.ScissorTest); G.Scissor(width / 2, 0, 1, height);
    G.ClearColor(1, 1, 1, 1); G.Clear(ClearBufferMask.ColorBufferBit); G.Disable(EnableCap.ScissorTest);
    var overlay = Read(width, height);
    Check(overlay[(height / 2 * width + width / 2) * 4] == 255 && overlay[(height / 2 * width + width / 2 - 1) * 4] == 64,
        "post-AA one-pixel HUD overlay remains sharp");

    int oldTexture = Get<int>("_spatialAaTexture"), oldFramebuffer = Get<int>("_spatialAaFramebuffer");
    int failures = 0;
    OpenGlFailureInjection.Callback = stage => { if (stage == "spatial-aa-target-before-publish") { failures++; throw new InvalidOperationException("injected target refusal"); } };
    Set("_targetSize", new Vector2i(63, 63)); Apply(); Apply();
    Check(failures == 1 && Get<int>("_spatialAaTexture") == oldTexture && Get<int>("_spatialAaFramebuffer") == oldFramebuffer,
        "failed replacement retains valid owner and suppresses same-size retry");
    OpenGlFailureInjection.Callback = null;
    Set("_targetSize", new Vector2i(62, 62)); Apply();
    Check(Get<int>("_spatialAaTexture") != oldTexture && !GL.IsTexture(oldTexture) && !GL.IsFramebuffer(oldFramebuffer), "changed-size retry publishes and retires old bundle");
    int accepted = Get<int>("_spatialAaTexture");
    Set("_targetSize", new Vector2i(8192, 8192)); Apply();
    Check(Get<int>("_spatialAaTexture") == accepted && Get<bool>("_spatialAaRefused"), "aggregate target budget refuses oversized AA without replacing valid owner");

    // GPU query waits are diagnostic-only and occur after the measured call.
    // Production AA never creates, polls or waits on a query.
    const int costWidth = 1280, costHeight = 720;
    Set("_targetSize", new Vector2i(costWidth, costHeight));
    byte[] costPixels = new byte[costWidth * costHeight * 4];
    for (int y = 0; y < costHeight; y++) for (int x = 0; x < costWidth; x++)
    {
        int i = (y * costWidth + x) * 4; byte v = (byte)((x % 32 > y % 32) ? 255 : 0);
        costPixels[i] = costPixels[i + 1] = costPixels[i + 2] = v; costPixels[i + 3] = 255;
    }
    Upload(costPixels, costWidth, costHeight); Apply(); GL.Finish();
    for (int i = 0; i < 8; i++) Apply();
    long allocationStart = GC.GetAllocatedBytesForCurrentThread();
    for (int i = 0; i < 32; i++) Apply();
    Check(GC.GetAllocatedBytesForCurrentThread() == allocationStart, "warmed production AA path allocates no managed bytes");
    var cpuMs = new List<double>(); var gpuMs = new List<double>();
    for (int i = 0; i < 30; i++)
    {
        Upload(costPixels, costWidth, costHeight); GL.Finish();
        int query = GL.GenQuery(); GL.BeginQuery(QueryTarget.TimeElapsed, query);
        long start = Stopwatch.GetTimestamp(); Apply();
        cpuMs.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        GL.EndQuery(QueryTarget.TimeElapsed); GL.Finish();
        GL.GetQueryObject((uint)query, GetQueryObjectParam.QueryResult, out ulong nanos);
        gpuMs.Add(nanos / 1_000_000.0); GL.DeleteQuery(query);
    }
    var evidence = new { schema = 1, status = "NO_ROLLOUT", diagnosticOnly = true, width = costWidth, height = costHeight,
        scope = "five-tap world AA + exact-size color copy; synthetic repeated diagonal edges; uploads and explicit test waits excluded",
        encoding = "native-encoded-rgba8", samples = cpuMs.Count, cpuP50Ms = cpuMs.Order().ElementAt(14), cpuP95Ms = cpuMs.Order().ElementAt(28),
        gpuP50Ms = gpuMs.Order().ElementAt(14), gpuP95Ms = gpuMs.Order().ElementAt(28), scratchBytes = costWidth * costHeight * 4,
        renderer = G.GetString(StringName.Renderer), version = G.GetString(StringName.Version), assemblyMvid = typeof(Scene).Module.ModuleVersionId };
    Console.WriteLine("SPATIAL_AA_COST " + JsonSerializer.Serialize(evidence));
    if (captures != null) File.WriteAllText(Path.Combine(captures, "cost.json"), JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));
    Invoke("DisposeSpatialAntialiasing");
    Check(Get<int>("_spatialAaTexture") == 0 && Get<int>("_spatialAaFramebuffer") == 0 && Get<int>("_spatialAaProgram") == 0, "scene disposal retires all AA owners");
    Check(G.GetError() == ErrorCode.NoError, "no remaining GL error");
    Console.WriteLine("SPATIAL_AA_NATIVE PASS (experimental NO_ROLLOUT)");
    return 0;
}
finally
{
    OpenGlFailureInjection.Callback = null;
    Invoke("DisposeSpatialAntialiasing");
    G.BindFramebuffer(FramebufferTarget.Framebuffer, 0); G.DeleteFramebuffer(framebuffer); G.DeleteTexture(input);
}
void Apply() => applySpatial();
void Invoke(string method) => typeof(Scene).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(scene, null);
void Set(string name, object value) => typeof(Scene).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(scene, value);
T Get<T>(string name) => (T)typeof(Scene).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(scene)!;
void Upload(byte[] pixels, int w, int h)
{
    G.ActiveTexture(TextureUnit.Texture0); G.BindTexture(TextureTarget.Texture2D, input);
    GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, w, h, 0, PixelFormat.Rgba, PixelType.UnsignedByte, pixels);
}
byte[] Read(int w, int h)
{
    var result = new byte[w * h * 4]; G.BindFramebuffer(FramebufferTarget.ReadFramebuffer, framebuffer);
    GL.ReadPixels(0, 0, w, h, PixelFormat.Rgba, PixelType.UnsignedByte, result); return result;
}
byte[] Pixels(Func<int, int, byte[]> pixel)
{
    var result = new byte[width * height * 4];
    for (int y = 0; y < height; y++) for (int x = 0; x < width; x++) pixel(x, y).CopyTo(result, (y * width + x) * 4);
    return result;
}
void Save(byte[] rgba, string path)
{
    using var stream = File.Create(path); ReFuel.Stb.StbImage.FlipVerticallyOnSave = true;
    MphRead.Mods.PngEncoder.Write<byte>(rgba, width, height, ReFuel.Stb.StbiImageFormat.Rgba, stream);
}
static void Check(bool condition, string message) { if (!condition) throw new Exception(message); Console.WriteLine("PASS " + message); }

}
