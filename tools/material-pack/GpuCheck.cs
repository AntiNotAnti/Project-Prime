using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Collections;
using System.Linq;
using MphRead.Mods.MapEditor;
using N = System.Numerics;
using MphRead;
using MphRead.Mods.Render.Materials;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;

internal static class GpuCheck
{
    internal static void Run(string root, MaterialAssetKey key)
    {
        // Existing uploader only permits paths inside the runtime default pack.
        // Use a synthetic isolated test-process root, preserving any existing directory.
        string packRoot = Path.Combine(AppContext.BaseDirectory, "texture-packs", "default");
        if (Directory.Exists(packRoot)) throw new InvalidOperationException("GPU check requires an unused test-output default pack directory.");
        Directory.CreateDirectory(packRoot);
        try
        {
            string path = Path.Combine(packRoot, "pixel.png");
            File.Copy(Path.Combine(root, "pixel.png"), path);
            var image = MaterialPack.ValidateImage(path);
            using var window = new NativeWindow(new NativeWindowSettings
            {
                ClientSize = new Vector2i(32, 32), StartVisible = false, StartFocused = false,
                Flags = ContextFlags.Default, Profile = OperatingSystem.IsMacOS() ? ContextProfile.Any : ContextProfile.Compatability,
                APIVersion = OperatingSystem.IsMacOS() ? new Version(2, 1) : new Version(3, 2)
            });
            window.Context.MakeCurrent();
            GL.LoadBindings(new OpenTK.Windowing.GraphicsLibraryFramework.GLFWBindingsContext());
            var uploader = typeof(Scene).Assembly.GetType("MphRead.Mods.Render.TextureReplacementPack", true)!;
            var upload = uploader.GetMethod("UploadCompanions", BindingFlags.Public | BindingFlags.Static)!;
            var live = new HashSet<int>();
            int allocated = 0, released = 0;
            Func<int> allocate = () => { int id = GL.GenTexture(); live.Add(id); allocated++; return id; };
            Action<int> release = id => { if (!live.Remove(id)) throw new Exception("Double release"); GL.DeleteTexture(id); released++; };
            for (int cycle = 0; cycle < 12; cycle++)
            {
                // Failure happens after allocation, proving the uploader releases it.
                var bad = new MaterialImage(Path.Combine(packRoot, "missing.png"), 1, 1);
                var material = new ResolvedMaterial(key, null, image, image, cycle % 2 == 0 ? image : bad);
                object maps = upload.Invoke(null, new object[] { material, allocate, release })!;
                foreach (string property in new[] { "Normal", "Specular", "Emissive" })
                {
                    int id = (int)maps.GetType().GetProperty(property)!.GetValue(maps)!;
                    if (id == 0) continue;
                    if (!GL.IsTexture(id)) throw new Exception("Successful upload has no texture");
                    release(id);
                    if (GL.IsTexture(id)) throw new Exception("Texture survived release");
                }
                if (live.Count != 0 || allocated != released) throw new Exception("Companion upload leaked texture handles");
                if (GL.GetError() != ErrorCode.NoError) throw new Exception("OpenGL upload error");
            }
            int attempts = 0;
            Func<int> faultingAllocate = () => ++attempts == 3 ? throw new InvalidOperationException("synthetic allocation failure") : allocate();
            bool rejected = false;
            try { upload.Invoke(null, new object[] { new ResolvedMaterial(key, null, image, image, image), faultingAllocate, release }); }
            catch (TargetInvocationException ex) when (ex.InnerException is InvalidOperationException) { rejected = true; }
            if (!rejected || live.Count != 0 || allocated != released) throw new Exception("Partial multi-map allocation leaked");
            Console.WriteLine($"Material GPU checks PASS: 12 cycles + allocation fault, {allocated} allocations / {released} releases, success and failed uploads");
            // Isolate the static catalog from installed/user maps for this content-free process.
            string emptyMapDirectory = Path.Combine(root, "empty-maps"); Directory.CreateDirectory(emptyMapDirectory);
            MphRead.Mods.MapGen.CustomRooms.MapDirectory = emptyMapDirectory;
            MphRead.Mods.MapGen.CustomRooms.UserMapDirectory = emptyMapDirectory;
            var scene = Scene.CreateEditorRenderer(new Vector2i(32, 32));
            try
            {
                Guid objectId = Guid.NewGuid();
                var face = new MapViewportFace(objectId, new[] { new N.Vector3(-1,-1,0), new N.Vector3(1,-1,0), new N.Vector3(0,1,0) },
                    1, 0, true, Texcoords: new[] { new N.Vector2(0,0), new N.Vector2(1,0), new N.Vector2(.5f,1) });
                var mesh = new MapViewportMesh(objectId, new[] { face });
                var frame = new MapRenderFrame(new(32,32), new(new N.Vector3(0,0,5), N.Vector3.Zero, true),
                    new[] { mesh }, new HashSet<Guid>(), new Dictionary<Guid,N.Matrix4x4>(), false, false);
                var enhanced = new ResolvedMaterial(key, image, image, image, image);
                IDictionary Maps() => (IDictionary)typeof(Scene).GetField("_materialMaps", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(scene)!;
                int[] Bindings() => Maps().Keys.Cast<int>().Concat(Maps().Values.Cast<object>().SelectMany(value =>
                    new[] { "Normal", "Specular", "Emissive" }.Select(name => (int)value.GetType().GetProperty(name)!.GetValue(value)!))).ToArray();
                for (int cycle = 0; cycle < 4; cycle++)
                {
                    int[] previous = Bindings();
                    var material = new MapViewportMaterial("synthetic/" + cycle, 1, 1, new[] { new ColorRgba((byte)255,(byte)255,(byte)255,(byte)255) },
                        CoordinateWidth: 16, CoordinateHeight: 16, Enhanced: enhanced);
                    frame = frame with { Materials = new Dictionary<(bool,int),MapViewportMaterial> { [(false,0)] = material } };
                    scene.DrawEditorFrame(frame, new Vector2i(32,32));
                    if (scene.EditorMeshUploads != 1 || scene.EditorMeshCount != 1 || Maps().Count != 1 || Bindings().Length != 4)
                        throw new Exception("Editor material refresh changed geometry or failed to own companion textures");
                    // GL may reuse deleted names, so only require old names absent from the active binding set to be retired.
                    var current = Bindings().ToHashSet();
                    if (previous.Any(id => !current.Contains(id) && GL.IsTexture(id))) throw new Exception("Editor refresh leaked old material texture");
                    int program = (int)typeof(Scene).GetField("_shaderProgramId", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(scene)!;
                    foreach (string uniform in new[] { "advanced_materials", "use_light", "use_normal_map", "use_specular_map", "use_emissive_map" })
                    {
                        GL.GetUniform(program, GL.GetUniformLocation(program, uniform), out int enabled);
                        if (enabled != 1) throw new Exception("Editor did not enable shared shader semantic: " + uniform);
                    }
                    if ((bool)typeof(Scene).GetField("_editorMaterialPreview", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(scene)!)
                        throw new Exception("Editor preview scope escaped its draw");
                    if (GL.GetError() != ErrorCode.NoError) throw new Exception("Editor material draw GL error");
                }
                int[] final = Bindings();
                scene.UnloadGl();
                if (final.Any(GL.IsTexture) || scene.EditorMeshCount != 0) throw new Exception("Editor teardown retained material resources");
                Console.WriteLine("Material editor GPU checks PASS: four lit material refreshes preserve one mesh; teardown releases all map textures");
            }
            finally { scene.UnloadGl(); }

        }
        finally { Directory.Delete(packRoot, true); }
    }
}
