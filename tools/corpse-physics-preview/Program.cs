using System.Buffers.Binary;
using System.IO.Compression;
using System.Numerics;
using System.Text;
using System.Text.Json;
using MphRead.CorpsePhysics;
using OpenTK.Graphics.OpenGL;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;
using V = System.Numerics.Vector3;
if (!args.Contains("--enable-experimental-physics"))
{ Console.Error.WriteLine("Explicit --enable-experimental-physics required."); return 2; }
string output = args.FirstOrDefault(a => !a.StartsWith("--")) ?? "corpse-preview"; Directory.CreateDirectory(output);
if (OperatingSystem.IsMacOS()) GLFW.InitHint(InitHintBool.CocoaChdirResources, false);
using var window = new NativeWindow(new NativeWindowSettings { ClientSize = new(1000, 750), Title = "CW-09 isolated primitive physics pilot", StartVisible = false,
    APIVersion = new(2, 1), Profile = ContextProfile.Any, Flags = ContextFlags.Default });
window.Context.MakeCurrent(); GL.LoadBindings(new GLFWBindingsContext());
var world = ReferenceScene.Floor(true); var rig = ReferenceScene.Samus();
var captures = new List<object>(); var poses = new CorpsePose[15];
for (int variant = 0; variant < 3; variant++)
{
    using IRagdollPresentationBackend backend = variant == 2 ? new BepuCorpseBackend(world, CorpseQuality.High, true) : new ProceduralDeathFallback(CorpseQuality.High, variant == 1);
    var handles = new CorpseHandle[8];
    for (int n = 0; n < 8; n++)
    {
        var spawn = new CorpseSpawnDescriptor(new(new(1, 0, 0, (byte)n, 0, 1, 0), new(1, (ulong)n + 1, 0, 0)), rig,
            new(new((n % 4 - 1.5f) * 2, n >= 4 ? .85f : .05f, n >= 4 ? 3 : -3), Quaternion.Identity), new(.1f, 0, .2f), V.Zero, null, (uint)n, 0, 240);
        if (!backend.TrySpawn(spawn, out handles[n])) throw new InvalidOperationException("Preview spawn failed.");
    }
    for (uint tick = 0; tick <= 180; tick++)
    {
        if (tick > 0 && !backend.Step(tick)) throw new InvalidOperationException("Preview step failed.");
        if (tick is not (0 or 12 or 30 or 60 or 120 or 180)) continue;
        foreach (float alpha in new[] { 0f, .5f, 1f })
        {
            GL.Viewport(0, 0, 1000, 750); GL.ClearColor(.035f, .045f, .07f, 1); GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
            GL.Enable(EnableCap.DepthTest); GL.MatrixMode(MatrixMode.Projection); GL.LoadIdentity(); GL.Ortho(-8, 8, -6, 6, -30, 30);
            GL.MatrixMode(MatrixMode.Modelview); GL.LoadIdentity(); GL.Rotate(25, 1, 0, 0); GL.Rotate(-24, 0, 1, 0); GL.Translate(0, -.5f, 0);
            GL.Begin(PrimitiveType.Triangles);
            foreach (var t in world.Triangles) { float h = Math.Clamp((t.A.Y + t.B.Y + t.C.Y) / 3, 0, 1); GL.Color3(.19f + h * .18f, .25f + h * .18f, .29f + h * .18f); Vertex(t.A); Vertex(t.B); Vertex(t.C); } GL.End();
            foreach (var handle in handles)
            {
                if (!backend.Sample(handle, alpha, poses)) throw new InvalidOperationException("Preview interpolation failed.");
                for (int p = 0; p < 15; p++)
                {
                    var pose = poses[p]; if (!pose.IsValid) throw new InvalidOperationException("Invalid render pose.");
                    GL.Color3(p == 2 ? .9f : .95f, p == 2 ? .8f : .35f, .1f);
                    GL.Begin(PrimitiveType.Quads);
                    var half = rig.Parts[p].Size * .5f;
                    foreach (int v in new[] { 0, 1, 3, 2, 4, 6, 7, 5, 0, 4, 5, 1, 2, 3, 7, 6, 0, 2, 6, 4, 1, 5, 7, 3 })
                        Vertex(pose.Position + V.Transform(new V((v & 1) == 0 ? -half.X : half.X, (v & 2) == 0 ? -half.Y : half.Y, (v & 4) == 0 ? -half.Z : half.Z), pose.Orientation));
                    GL.End();
                }
                GL.Color3(.2f, .8f, 1f); GL.Begin(PrimitiveType.Lines);
                for (int p = 1; p < 15; p++) { Vertex(poses[p].Position); Vertex(poses[rig.Parts[p].Parent].Position); } GL.End();
            }
            GL.Finish(); var error = GL.GetError(); if (error != OpenTK.Graphics.OpenGL.ErrorCode.NoError) throw new InvalidOperationException($"GL error {error}.");
            var pixels = new byte[1000 * 750 * 3]; GL.PixelStore(PixelStoreParameter.PackAlignment, 1);
            GL.ReadPixels(0, 0, 1000, 750, PixelFormat.Rgb, PixelType.UnsignedByte, pixels);
            string file = $"{variant}-{tick:D3}-{alpha:F1}.png"; WritePng(Path.Combine(output, file), pixels, 1000, 750);
            captures.Add(new { backend = backend.Name, tick, alpha, file, statistics = backend.Statistics });
        }
    }
}
File.WriteAllText(Path.Combine(output, "receipt.json"), JsonSerializer.Serialize(new { schema = 1, proxyOnly = true, renderer = GL.GetString(StringName.Renderer), version = GL.GetString(StringName.Version), captures }, new JsonSerializerOptions { WriteIndented = true }));
return 0;
static void Vertex(V p) => GL.Vertex3(p.X, p.Y, p.Z);
static void WritePng(string path, byte[] pixels, int width, int height)
{
    using var file = File.Create(path); file.Write(new byte[] { 137,80,78,71,13,10,26,10 });
    void Chunk(string type, byte[] bytes)
    {
        Span<byte> word = stackalloc byte[4]; BinaryPrimitives.WriteInt32BigEndian(word, bytes.Length); file.Write(word);
        byte[] name = Encoding.ASCII.GetBytes(type); file.Write(name); file.Write(bytes); uint crc = uint.MaxValue;
        foreach (byte b in name.Concat(bytes)) { crc ^= b; for (int bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0); }
        BinaryPrimitives.WriteUInt32BigEndian(word, ~crc); file.Write(word);
    }
    var header = new byte[13]; BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0,4), width); BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4,4), height); header[8] = 8; header[9] = 2; Chunk("IHDR", header);
    using var compressed = new MemoryStream(); using (var z = new ZLibStream(compressed, CompressionLevel.Fastest, true))
        for (int y = height - 1; y >= 0; y--) { z.WriteByte(0); z.Write(pixels, y * width * 3, width * 3); }
    Chunk("IDAT", compressed.ToArray()); Chunk("IEND", Array.Empty<byte>());
}
