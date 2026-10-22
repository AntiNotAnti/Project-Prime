using System.Diagnostics;
using System.Reflection;
using MphRead;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Formats.Collision;
using MphRead.Formats.Culling;
using MphRead.Mods.Render;
using OpenTK.Mathematics;
using OpenTK.Graphics.OpenGL;
using OpenTK.Windowing.Desktop;
using GL = OpenTK.Graphics.OpenGL.GL;

static partial class Program
{
    static int checks;
    static void Check(bool value, string message)
    {
        checks++;
        if (!value) throw new InvalidOperationException(message);
    }
    static FieldInfo Field(object owner, string name) => owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!;
    static T Get<T>(object owner, string name) => (T)Field(owner, name).GetValue(owner)!;
    static void Set(object owner, string name, object value) => Field(owner, name).SetValue(owner, value);
    static object? Call(object owner, string name, params object[] args) => owner.GetType()
        .GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(owner, args);
    static readonly Vector3[] Square = [new(-1,-1,0), new(1,-1,0), new(1,1,0), new(-1,1,0)];

    // Independent, unbounded reference clipping establishes ordered-point parity,
    // including polygons that the production bounded path must reject safely.
    static List<Vector3> Reference(FrustumInfo info, IReadOnlyList<Vector3> polygon)
    {
        var input = polygon.ToList();
        for (int p = 0; p < info.Count && input.Count > 0; p++)
        {
            var output = new List<Vector3>();
            Vector4 plane = info.Planes[p].Plane;
            for (int i = 0; i < input.Count; i++)
            {
                Vector3 a = input[i], b = input[(i + 1) % input.Count];
                float da = Vector3.Dot(a, plane.Xyz) - plane.W;
                float db = Vector3.Dot(b, plane.Xyz) - plane.W;
                if (da >= 0) output.Add(a);
                if ((da >= 0) != (db >= 0)) output.Add(a + (b - a) * (-da / (db - da)));
            }
            input = output;
        }
        return input;
    }
    static void Clipping()
    {
        var scratch = new PortalClipScratch();
        var random = new Random(4519);
        for (int sample = 0; sample < 2000; sample++)
        {
            var frustum = new FrustumInfo { Count = 1 + sample % 6 };
            for (int i = 0; i < frustum.Count; i++)
            {
                double angle = random.NextDouble() * Math.Tau;
                frustum.Planes[i].Plane = new Vector4((float)Math.Cos(angle), (float)Math.Sin(angle), 0, -.8f);
            }
            var expected = Reference(frustum, Square);
            int count = scratch.Clip(frustum, Square);
            Check(count == expected.Count, "clipped polygon count parity");
            for (int i = 0; i < count; i++) Check(scratch.Result[i] == expected[i], "clipped ordered vertex parity");
        }
        var pass = new FrustumInfo { Count = 1 };
        pass.Planes[0].Plane = new Vector4(1, 0, 0, -2);
        Check(scratch.Clip(pass, []) == 0, "empty portal");
        Check(scratch.Clip(pass, new Vector3[15]) == -1, "oversize input requests conservative fallback");
        pass.Count = 11;
        Check(scratch.Clip(pass, Square) == -1, "oversize frustum requests conservative fallback");
        pass.Count = 1; pass.Planes[0].Plane = new Vector4(1, 0, 0, -.99f);
        var polygon = Enumerable.Range(0, 14).Select(i => new Vector3(
            MathF.Cos(i * MathF.Tau / 14), MathF.Sin(i * MathF.Tau / 14), 0)).ToArray();
        Check(Reference(pass, polygon).Count == 15, "expansion fixture genuinely adds a point");
        Check(scratch.Clip(pass, polygon) == -1, "expansion cannot overrun scratch");
        // A second view may clip between this view's calls without sharing storage.
        var other = new PortalClipScratch();
        scratch.Clip(pass, Square); Vector3 saved = scratch.Result[0];
        other.Clip(pass, polygon);
        Check(scratch.Result[0] == saved, "independent view scratch");
        pass.Count = 5;
        for (int i = 1; i < pass.Count; i++) pass.Planes[i] = pass.Planes[0];
        Vector3[] destination = new Vector3[14];
        for (int i = 0; i < 1000; i++) LegacyClip(pass, Square, destination);
        long legacyStart = Stopwatch.GetTimestamp(), legacyAllocation = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100000; i++) LegacyClip(pass, Square, destination);
        long legacyBytes = GC.GetAllocatedBytesForCurrentThread() - legacyAllocation;
        double legacyMs = Stopwatch.GetElapsedTime(legacyStart).TotalMilliseconds;
        Console.WriteLine($"clip legacy: iterations=100000 allocated_bytes={legacyBytes} elapsed_ms={legacyMs:F3}");
        var metrics = MeasureClip(scratch, pass);
        Console.WriteLine($"clip candidate: iterations=100000 allocated_bytes={metrics.Bytes} elapsed_ms={metrics.Ms:F3}");
        Check(metrics.Bytes == 0, "zero warmed clipping allocation");
        Check(legacyBytes >= 100000L * 2 * 14 * 12, "legacy allocation negative control is active");
    }
    // The audited allocation/loop shape, retained as a controlled CPU negative
    // control. It is never called by production; inputs here cannot overflow it.
    static int LegacyClip(FrustumInfo info, Vector3[] input, Vector3[] destination)
    {
        Vector3[] first = new Vector3[14], second = new Vector3[14];
        int count = input.Length;
        for (int i = 0; i < info.Count; i++)
        {
            Vector3[] output = i == info.Count - 1 ? destination : i % 2 == 0 ? first : second;
            Vector4 plane = info.Planes[i].Plane;
            int written = 0;
            float da = Vector3.Dot(input[0], plane.Xyz) - plane.W;
            bool inside = da >= 0;
            for (int j = 0; j < count; j++)
            {
                Vector3 a = input[j], b = input[j == count - 1 ? 0 : j + 1];
                if (inside) output[written++] = a;
                float db = Vector3.Dot(b, plane.Xyz) - plane.W;
                bool nextInside = db >= 0;
                if (inside != nextInside) output[written++] = a + (b-a) * (-da/(db-da));
                da = db; inside = nextInside;
            }
            if (written == 0) return 0;
            count = written; input = output;
        }
        return count;
    }
    static (long Bytes, double Ms) MeasureClip(PortalClipScratch scratch, FrustumInfo frustum)
    {
        for (int i = 0; i < 1000; i++) scratch.Clip(frustum, Square);
        long start = Stopwatch.GetTimestamp();
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100000; i++) scratch.Clip(frustum, Square);
        long bytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
        return (bytes, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
    }
    static (RoomEntity Room, RoomFrustumItem Root) Room(int count)
    {
        var scene = new Scene(default, null!, null!, _ => { }, () => { }, initializeRuntime: false);
        var room = new RoomEntity(scene);
        Set(room, "_meta", Metadata.GetRoomById(0)!);
        Set(room, "_nextRoomPartId", count);
        var sides = Get<List<List<(Portal Portal, bool OtherSide)>>>(room, "_portalSides");
        for (int i = 0; i < count; i++) sides.Add([]);
        var root = Get<RoomFrustumItem[]>(room, "_roomFrustumItems")[0];
        root.NodeRef = new NodeRef("fixture", 0, 0, 0);
        root.Info.Count = 1; root.Info.Index = 0;
        root.Info.Planes[0].Plane = new Vector4(0, 0, 1, -1);
        Call(room, "GetPartVisInfo", root.NodeRef);
        Get<bool[]>(room, "_activeRoomParts")[0] = true;
        Set(room, "_roomFrustumIndex", 1);
        return (room, root);
    }
    static Portal Connect(RoomEntity room, int a, int b, bool active = true, int model = 0,
        IReadOnlyList<Vector3>? points = null, Vector4? plane = null)
    {
        var portal = new Portal("a", "b", points ?? Square, Array.Empty<Vector4>(), plane ?? new Vector4(0,0,1,0))
        {
            NodeRef1 = new NodeRef("fixture", a, a, 0),
            NodeRef2 = new NodeRef("fixture", b, b, model), Active = active
        };
        Get<List<Portal>>(room, "_portals").Add(portal);
        var sides = Get<List<List<(Portal, bool)>>>(room, "_portalSides");
        sides[a].Add((portal, false)); sides[b].Add((portal, true));
        return portal;
    }
    static void Traversal()
    {
        var (room, root) = Room(4);
        Connect(room, 0, 1, model: 1); // connector full identity
        Connect(room, 0, 2, active: false);
        Portal field = Connect(room, 0, 3);
        typeof(Portal).GetField("<IsForceField>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(field, true);
        typeof(Portal).GetField("<Position>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(field, new Vector3(20,0,0));
        Call(room, "FindVisibleRoomParts", root, root.NodeRef);
        bool[] visible = Get<bool[]>(room, "_activeRoomParts");
        Check(visible[0] && visible[1] && !visible[2] && !visible[3], "connector reachable, inactive and opaque forcefield excluded");
        Check(!Get<bool>(room, "_portalVisibilityOverflow"), "ordinary fixture retains authoritative visibility");
        Check(Get<RoomPartVisInfo>(room, "_partVisInfoHead").NodeRef.PartIndex == 1, "ordered traversal retains connector head");
        (room, root) = Room(10);
        for (int a = 0; a < 10; a++) for (int b = a + 1; b < 10; b++) Connect(room, a, b);
        Call(room, "FindVisibleRoomParts", root, root.NodeRef);
        Check(Get<bool>(room, "_portalVisibilityOverflow"), "dense cyclic graph exhausts bounded work safely");
        Check(Get<RoomPartVisInfo?>(room, "_partVisInfoHead") == null, "overflow discards partial visibility");
        for (int i = 0; i < 10; i++) Check(room.IsNodeRefVisible(new NodeRef("fixture", i,i,0)), "overflow cannot hide room or entities");
        Check(Get<int>(room, "_roomFrustumIndex") <= 32, "bounded frustum pool");
        (room, root) = Room(2);
        root.Info.Index = 1;
        Vector3[] decagon = Enumerable.Range(0, 10).Select(i => new Vector3(
            MathF.Cos(i * MathF.Tau / 10), MathF.Sin(i * MathF.Tau / 10), 0)).ToArray();
        Connect(room, 0, 1, points: decagon, plane: new Vector4(0,0,1,-1));
        Call(room, "FindVisibleRoomParts", root, root.NodeRef);
        Check(Get<bool>(room, "_portalVisibilityOverflow") && room.IsNodeRefVisible(new NodeRef("fixture",1,1,0)),
            "expanded plane count abandons culling before plane-array write");
        (room, root) = Room(2);
        root.Info.Planes[0].Plane = new Vector4(1,0,0,-.99f);
        Vector3[] fourteen = Enumerable.Range(0, 14).Select(i => new Vector3(
            MathF.Cos(i * MathF.Tau / 14), MathF.Sin(i * MathF.Tau / 14), 0)).ToArray();
        Connect(room, 0, 1, points: fourteen, plane: new Vector4(0,0,1,-1));
        Call(room, "FindVisibleRoomParts", root, root.NodeRef);
        Check(Get<bool>(room, "_portalVisibilityOverflow") && room.IsNodeRefVisible(new NodeRef("fixture",1,1,0)),
            "clipping expansion requests whole-view conservative fallback");
        (room, root) = Room(1);
        Connect(room, 0, 0); // malformed self-loop formerly bypassed depth cutoff
        Call(room, "FindAudibleRoomParts", root.NodeRef, root.NodeRef);
        Check(Get<bool>(room, "_portalVisibilityOverflow"), "audio cycle has a release-mode recursion bound");
        Check(room.IsNodeRefAudible(root.NodeRef), "audio overflow remains conservative");
        Check(room.IsNodeRefAudible(new NodeRef("fixture", 500,0,0)), "unsupported part index safe");
        (room, _) = Room(33);
        Call(room, "UpdateRoomParts", true);
        Check(Get<bool>(room, "_portalVisibilityOverflow"), "unsupported map part count draws all before indexing");
    }
    static void Keys()
    {
        var original = new GeometryCompileKey(1,32,64,false,false,4,0);
        var variants = new[] { original with { SourceId = 2 }, original with { TextureWidth = 64 },
            original with { TextureHeight = 32 }, original with { NormalTexgen = true },
            original with { RoomLayout = true }, original with { MatrixCount = 2 }, original with { Isolation = 1 } };
        foreach (var variant in variants) Check(original != variant, "compile-changing input must split geometry");
        Check(new HashSet<GeometryCompileKey>(variants.Append(original)).Count == 8, "compile identity preserves all independent variants");
    }
    static T Raw<T>(params (string Name, object Value)[] values) where T : struct
    {
        object boxed = default(T);
        foreach (var value in values) typeof(T).GetField(value.Name)!.SetValue(boxed, value.Value);
        return (T)boxed;
    }
    static Model Geometry(bool failing = false)
    {
        RawMaterial Material(short texture, TexgenMode mode = TexgenMode.None) => Raw<RawMaterial>(
            (nameof(RawMaterial.Name), new byte[64]), (nameof(RawMaterial.TextureId), texture),
            (nameof(RawMaterial.PaletteId), (short)-1), (nameof(RawMaterial.Alpha), (byte)31),
            (nameof(RawMaterial.TexcoordTransformMode), mode));
        IReadOnlyList<RenderInstruction> triangle = [new(InstructionCode.BEGIN_VTXS, 0),
            new(InstructionCode.NORMAL, 511u << 20), new(InstructionCode.COLOR, 31),
            new(InstructionCode.TEXCOORD, 512u | (512u << 16)),
            new(InstructionCode.VTX_16, 0, 0), new(InstructionCode.VTX_16, 3072, 0),
            new(InstructionCode.VTX_16, 3072u << 16, 0), new(InstructionCode.END_VTXS)];
        var sources = new List<IReadOnlyList<RenderInstruction>> { triangle,
            failing ? new[] { new RenderInstruction(InstructionCode.BEGIN_VTXS, 9) } : triangle };
        var meshes = failing
            ? new[] { default(RawMesh), Raw<RawMesh>((nameof(RawMesh.DlistId), (ushort)1)) }
            : new[] { default(RawMesh), default(RawMesh),
                Raw<RawMesh>((nameof(RawMesh.MaterialId), (ushort)1)),
                Raw<RawMesh>((nameof(RawMesh.MaterialId), (ushort)2)),
                Raw<RawMesh>((nameof(RawMesh.DlistId), (ushort)1)) };
        Texture[] textures = [new(TextureFormat.DirectRgb,32,32), new(TextureFormat.DirectRgb,64,32)];
        var recolor = new Recolor("fixture", textures, [],
            new IReadOnlyList<TextureData>[] { [], [] }, []);
        return new Model("compile-fixture", false, default, [], meshes,
            new[] { Material(0), Material(1), Material(0, TexgenMode.Normal) }, [], sources,
            new AnimationResults(), [], new[] { recolor }, [], [], [], [], []);
    }
    static byte[] Picture(int list)
    {
        GL.Viewport(0,0,32,32); GL.Disable(EnableCap.Texture2D); GL.Disable(EnableCap.DepthTest);
        GL.MatrixMode(MatrixMode.Projection); GL.LoadIdentity(); GL.MatrixMode(MatrixMode.Modelview); GL.LoadIdentity();
        GL.ClearColor(0,0,0,1); GL.Clear(ClearBufferMask.ColorBufferBit); GL.CallList(list); GL.Finish();
        byte[] pixels = new byte[32 * 32 * 4];
        GL.ReadPixels(0,0,32,32, PixelFormat.Rgba, PixelType.UnsignedByte, pixels);
        return pixels;
    }
    static void CompileWithoutReuse(Scene scene, Model model)
    {
        foreach (Mesh mesh in model.Meshes)
        {
            Material material = model.Materials[mesh.MaterialId];
            Texture texture = model.Recolors[0].Textures[material.TextureId];
            int id = GraphicsApi.GenLists(1);
            GraphicsApi.RegisterRoomGeometryList(id);
            GraphicsApi.NewList(id, ListMode.Compile);
            try { Call(scene, "DoDlist", model, mesh, (int)texture.Width, (int)texture.Height,
                material.TexgenMode == TexgenMode.Normal, true); }
            finally { GraphicsApi.EndList(); }
            mesh.ListId = id;
        }
    }
    static void NativeGeometry()
    {
        if (!OperatingSystem.IsWindows()) throw new InvalidOperationException("Native qualification is Windows-only for this campaign.");
        var settings = DesktopGlContext.Settings(background: true);
        settings.ClientSize = new Vector2i(32,32); settings.StartVisible = false;
        using var window = new NativeWindow(settings); window.Context.MakeCurrent();
        GL.LoadBindings(new OpenTK.Windowing.GraphicsLibraryFramework.GLFWBindingsContext());
        Console.WriteLine($"native renderer={GL.GetString(StringName.Renderer)} gl={GL.GetString(StringName.Version)} mvid={typeof(Scene).Module.ModuleVersionId}");
        for (int cycle = 0; cycle < 3; cycle++)
        {
            var scene = new Scene(default, null!, null!, _ => { }, () => { }, initializeRuntime: false);
            Model model = Geometry();
            long resident = DesktopRetainedGeometry.ResidentBytes;
            int retained = DesktopRetainedGeometry.RetainedListCount;
            Model baseline = Geometry();
            long baselineStart = Stopwatch.GetTimestamp(); CompileWithoutReuse(scene, baseline);
            double baselineMs = Stopwatch.GetElapsedTime(baselineStart).TotalMilliseconds;
            long baselineBytes = DesktopRetainedGeometry.ResidentBytes - resident;
            Check(DesktopRetainedGeometry.RetainedListCount == retained + 5, "negative control compiles five separate retained objects");
            byte[] baselinePicture = Picture(baseline.Meshes[0].ListId);
            SharedModelResources.Retain(baseline); SharedModelResources.Release(baseline);
            long start = Stopwatch.GetTimestamp();
            Call(scene, "GenerateLists", model, true);
            double coldMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            int[] ids = model.Meshes.Select(mesh => mesh.ListId).ToArray();
            Check(ids.Distinct().Count() == 4 && ids[0] == ids[1], "identical source/input compiles once; dimensions, texgen, source split");
            Check(DesktopRetainedGeometry.RetainedListCount - retained == 4, "compatible meshes share retained object too");
            long candidateBytes = DesktopRetainedGeometry.ResidentBytes - resident;
            Check(candidateBytes < baselineBytes, "same synthetic geometry uses fewer retained bytes than per-mesh baseline");
            Check(Picture(ids[0]).SequenceEqual(baselinePicture), "candidate pixels match compile-without-reuse baseline");
            start = Stopwatch.GetTimestamp(); Call(scene, "GenerateLists", model, true);
            double warmMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            Check(model.Meshes.Select(mesh => mesh.ListId).SequenceEqual(ids), "warm load does not recompile existing mesh IDs");
            Model side = model.CreateSceneCopy(); Call(scene, "GenerateLists", side, true);
            Check(side.Meshes[0].ListId != ids[0], "side scene model owns independent resources");
            byte[] shared = Picture(ids[0]); byte[] separate = Picture(side.Meshes[0].ListId);
            Check(shared.SequenceEqual(separate) && shared.Where((_,i) => i % 4 == 0).Any(r => r > 0), "shared and independently compiled source have identical nonempty pixels");
            Console.WriteLine($"geometry cycle={cycle} meshes=5 baseline_compiled=5 compiled=4 baseline_retained_bytes={baselineBytes} retained_bytes={candidateBytes} baseline_ms={baselineMs:F3} cold_ms={coldMs:F3} warm_ms={warmMs:F3}");
            SharedModelResources.Retain(model); SharedModelResources.Retain(model);
            SharedModelResources.Release(model);
            Check(GL.IsList(ids[0]), "first scene lease cannot delete shared geometry");
            SharedModelResources.Release(model); SharedModelResources.Release(model);
            Check(ids.All(id => !GL.IsList(id)) && model.Meshes.All(mesh => mesh.ListId == 0), "last lease deletes unique IDs once and clears every alias");
            SharedModelResources.Retain(side); SharedModelResources.Release(side);
            Check(DesktopRetainedGeometry.ResidentBytes == resident && DesktopRetainedGeometry.RetainedListCount == retained,
                "main/side unload restores retained storage baseline");
            Model failure = Geometry(failing: true);
            bool threw = false;
            try { Call(scene, "GenerateLists", failure, true); }
            catch (TargetInvocationException e) when (e.InnerException is ProgramException) { threw = true; }
            Check(threw && failure.Meshes[0].ListId != 0 && failure.Meshes[1].ListId == 0,
                "injected compiler failure never publishes failed ID");
            Check(DesktopRetainedGeometry.RetainedListCount == retained + 1, "failed capture retains no extra compiled geometry");
            ((List<IReadOnlyList<RenderInstruction>>)failure.RenderInstructionLists)[1] = failure.RenderInstructionLists[0];
            Call(scene, "GenerateLists", failure, true);
            Check(failure.Meshes[1].ListId != 0, "compile state recovers after injected failure");
            SharedModelResources.Retain(failure); SharedModelResources.Release(failure);
            Check(DesktopRetainedGeometry.ResidentBytes == resident && GL.GetError() == ErrorCode.NoError,
                "failure/retry/unload restores storage with no GL error");
        }
    }
    static int Main(string[] args)
    {
        try { if (args.Contains("--dynamic-shadow-native")) { Environment.SetEnvironmentVariable("PROJECT_PRIME_GL_DYNAMIC_SHADOW_CASTERS","1"); DynamicShadowChecks(); Console.WriteLine($"PASS: {checks} detached dynamic shadow checks"); return 0; } Clipping(); Traversal(); Keys(); if (args.Contains("--native")) { NativeGeometry(); ShadowChecks(); } Console.WriteLine($"PASS: {checks} renderer preparation checks; native={args.Contains("--native")}"); return 0; }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
}
