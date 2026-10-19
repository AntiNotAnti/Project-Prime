using MphRead.Mods.Replay;
using OpenTK.Mathematics;

internal static class PresentationChecks
{
    public static void Run()
    {
        int checks = 0;
        void Check(bool value, string message)
        { if (!value) throw new InvalidOperationException("Presented camera: " + message); checks++; }
        foreach (var item in new (float Yaw, float Pitch, float Roll, float Fov)[] { (.7f, .4f, .6f, 42f), (-1.2f, -.9f, -1.7f, 120f),
            (0f, 0f, MathF.PI, 55f), (2f, 1.56f, -.3f, 140f) })
        {
            Vector3 position = new(23, -7, 91);
            var rotation = (Quaternion.FromAxisAngle(Vector3.UnitY, item.Yaw)
                * Quaternion.FromAxisAngle(Vector3.UnitX, item.Pitch)).Normalized();
            Vector3 facing = Vector3.Transform(-Vector3.UnitZ, rotation);
            Vector3 up = Vector3.Transform(Vector3.Transform(Vector3.UnitY, rotation), Quaternion.FromAxisAngle(facing, item.Roll));
            Matrix4 view = Matrix4.LookAt(position, position + facing, up);
            Matrix4 projection = Matrix4.CreatePerspectiveFieldOfView(MathHelper.DegreesToRadians(item.Fov), 16f / 9, .05f, 10000);
            Check(ReplayCameraPresentationMath.TryDecode(view, projection, out var decoded), "actual view/projection matrix decodes");
            Check((decoded.Position - position).Length < .0002f, "inverse view supplies displayed position rather than canonical CameraInfo");
            Check(Math.Abs(MathHelper.RadiansToDegrees(decoded.Fov) - item.Fov) < .0001f,
                "projection supplies actual scoped/clamped/restored FOV independent of global camera preference");
            Vector3 restoredFacing = Vector3.Transform(-Vector3.UnitZ, decoded.Rotation);
            Vector3 restoredUp = Vector3.Transform(Vector3.Transform(Vector3.UnitY, decoded.Rotation),
                Quaternion.FromAxisAngle(restoredFacing, decoded.Roll));
            Matrix4 restored = Matrix4.LookAt(decoded.Position, decoded.Position + restoredFacing, restoredUp);
            float maximum = 0;
            for (int row = 0; row < 4; row++)
                for (int column = 0; column < 4; column++) maximum = Math.Max(maximum, Math.Abs(view[row, column] - restored[row, column]));
            Check(maximum < .003f, "saved yaw/pitch quaternion plus separate roll reconstructs displayed basis without double roll");
        }
        var cache = new ReplayCameraPresentationCache<object>();
        object scene = new(), transport = new();
        Matrix4 validView = Matrix4.LookAt(new Vector3(4, 5, 6), Vector3.Zero, Vector3.UnitY);
        Matrix4 validProjection = Matrix4.CreatePerspectiveFieldOfView(1, 1.5f, .1f, 1000);
        Check(cache.Publish(scene, transport, 3, 60, "logical.ppclip", "cache.ppdemo", "SETTINGS1", validView, validProjection),
            "successful rendered matrix publication stores weak presentation owner");
        Check(cache.TryRead(scene, transport, 3, 60, "logical.ppclip", "cache.ppdemo", "SETTINGS1", out var saved)
            && saved!.Revision > 0, "exact scene/transport/seek/frame/settings may review published pose");
        Check(!cache.TryRead(new object(), transport, 3, 60, "logical.ppclip", "cache.ppdemo", "SETTINGS1", out _), "another scene cannot reuse presented picture");
        Check(!cache.TryRead(scene, new object(), 3, 60, "logical.ppclip", "cache.ppdemo", "SETTINGS1", out _), "replacement transport cannot reuse picture");
        Check(!cache.TryRead(scene, transport, 4, 60, "logical.ppclip", "cache.ppdemo", "SETTINGS1", out _), "changed seek generation rejects picture");
        Check(!cache.TryRead(scene, transport, 3, 61, "logical.ppclip", "cache.ppdemo", "SETTINGS1", out _), "different replay frame rejects picture");
        Check(!cache.TryRead(scene, transport, 3, 60, "other.ppclip", "cache.ppdemo", "SETTINGS1", out _)
            && !cache.TryRead(scene, transport, 3, 60, "logical.ppclip", "other.ppdemo", "SETTINGS1", out _), "logical and physical paths fence picture");
        Check(!cache.TryRead(scene, transport, 3, 60, "logical.ppclip", "cache.ppdemo", "SETTINGS2", out _), "unpresented lens/mode settings cannot review older picture");
        Check(!cache.Publish(scene, transport, 3, 60, "logical.ppclip", "cache.ppdemo", "SETTINGS1", default, validProjection)
            && !cache.TryRead(scene, transport, 3, 60, "logical.ppclip", "cache.ppdemo", "SETTINGS1", out _), "invalid matrix publication cannot retain a valid-looking stale picture");
        Matrix4 nonfinite = validView; nonfinite.M11 = float.NaN;
        Check(!ReplayCameraPresentationMath.TryDecode(nonfinite, validProjection, out _)
            && !ReplayCameraPresentationMath.TryDecode(validView, Matrix4.Identity, out _), "nonfinite view and nonperspective projection are rejected");
        var pending = new ReplayPresentedCamera(0, transport, 3, 60, "logical.ppclip", "cache.ppdemo", "SETTINGS1", saved!.Pose);
        cache.QueueRendered(scene, pending);
        Check(!cache.TryRead(scene, transport, 3, 60, "logical.ppclip", "cache.ppdemo", "SETTINGS1", out _),
            "successful draw alone cannot expose a picture before window presentation succeeds");
        cache.DiscardRendered(scene);
        Check(!cache.TryTakeRendered(scene, out _), "next draw discards a prior candidate whose presentation failed");
        cache.QueueRendered(scene, pending);
        Check(cache.TryTakeRendered(scene, out var complete) && !cache.TryTakeRendered(scene, out _),
            "successful window presentation takes its exact candidate once");
        cache.Publish(scene, complete!);
        Check(cache.TryRead(scene, transport, 3, 60, "logical.ppclip", "cache.ppdemo", "SETTINGS1", out var displayed)
            && displayed!.Revision > saved.Revision, "only presented candidate advances reviewed-picture revision");
        string root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "src/MphRead/Renderer.cs"))) root = Directory.GetParent(root)?.FullName
            ?? throw new InvalidOperationException("Repository root unavailable for renderer integration check.");
        string renderer = File.ReadAllText(Path.Combine(root, "src/MphRead/Renderer.cs"));
        int stage = renderer.IndexOf("var nativeReplayCamera = StageNativeReplayCamera();", StringComparison.Ordinal);
        int draw = renderer.IndexOf("rendered = RenderFrameContent();", stage, StringComparison.Ordinal);
        int cleanup = renderer.IndexOf("EndRenderDiagnostics();", draw, StringComparison.Ordinal);
        int queue = renderer.IndexOf("if (rendered) QueueRenderedNativeReplayCamera(nativeReplayCamera);", cleanup, StringComparison.Ordinal);
        Check(stage >= 0 && draw > stage && cleanup > draw && queue > cleanup,
            "production renderer stages actual prepared matrices and queues only after successful draw and cleanup");
        int present = renderer.IndexOf("try { PresentFrame(); }", StringComparison.Ordinal);
        int publish = renderer.IndexOf("Scene.PublishNativeReplayPresentedFrame();", present, StringComparison.Ordinal);
        Check(present >= 0 && publish > present && renderer.IndexOf("PresentFrame();\n#endif", present, StringComparison.Ordinal) < publish,
            "production window publishes candidate only after successful platform swap, before subsequent frame input");
        string native = File.ReadAllText(Path.Combine(root, "src/MphRead/Mods/Replay/ReplayCamera.Native.cs"));
        Check(native.Contains("internal static readonly ReplayCameraPresentationCache<Scene>", StringComparison.Ordinal)
            && !native.Contains("CameraInfo", StringComparison.Ordinal) && !native.Contains("ModPrepareFirstPerson", StringComparison.Ordinal)
            && !native.Contains("GL.", StringComparison.Ordinal), "production capture uses external weak state without raw camera fallback, draw preparation or GL queries");
        Console.WriteLine($"PASS {checks} actual-matrix presentation decode/cache/integration checks; no worlds or GL context");
    }
}
