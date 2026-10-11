using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using MphRead;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Mods;
using MphRead.Mods.Network;
using MphRead.Mods.Render;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;

// Independent authority-backed asset fixture. A real native damage call authors
// the fact; this local GL check does not claim UDP delivery or collision replay.
sealed class NetImpactPresentationCheck
{
    const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
    static readonly Type Manager = typeof(Scene).Assembly.GetType("MphRead.Mods.Network.NetConfirmedImpacts")!;
    static readonly Type FactType = typeof(Scene).Assembly.GetType("MphRead.Mods.Network.ConfirmedImpact")!;
    static readonly string[] CameraFields = { "_cameraMode", "_cameraPosition", "_cameraFacing", "_cameraRight", "_cameraUp", "_freeCam", "_inputMode", "_cameraFov", "_viewModelFov" };
    readonly Scene _scene;
    readonly string _directory;
    readonly List<object> _cases = new();
    readonly List<string> _failures = new();
    object? _published;
    int _publishedCount;
    public object? Evidence { get; private set; }

    public NetImpactPresentationCheck(Scene scene, string directory)
    {
        _scene = scene; _directory = Path.GetFullPath(directory);
        if (Directory.Exists(_directory)) throw new IOException("Impact GPU evidence already exists: " + _directory);
        Directory.CreateDirectory(_directory);
    }

    public void Run()
    {
        if (_scene.Players.Items.Count < 2) throw new InvalidOperationException("Impact GPU check needs two real players.");
        var owner = _scene.Players.Items[0]; var victim = _scene.Players.Items[1];
        var sessionNames = new[] { nameof(NetSession.Role), nameof(NetSession.IsAuthority), nameof(NetSession.LocalSlot),
            nameof(NetSession.NetFrame), nameof(NetSession.ServerMatch), nameof(NetSession.ConnectionLost), "ConfirmedImpactSink" };
        var session = sessionNames.ToDictionary(n => n, n => typeof(NetSession).GetProperty(n, Static)!.GetValue(null));
        var snapshotField = typeof(NetSession).GetField("_lastSnapshotFrame", Static)!;
        object? oldSnapshot = snapshotField.GetValue(null);
        var trackers = (NetLifecycleTracker[])typeof(NetPlayerLifecycle).GetField("_slots", Static)!.GetValue(null)!;
        var oldTrackers = trackers.ToArray();
        var camera = CameraFields.Select(n => typeof(Scene).GetField(n, Instance)!.GetValue(_scene)).ToArray();
        var managerFields = Manager.GetFields(Static).Where(f => !f.IsLiteral).ToArray();
        var managerState = managerFields.Select(f => f.GetValue(null) is Array a ? a.Clone()
            : f.GetValue(null) is IList list ? list.Cast<object>().ToArray() : f.GetValue(null)).ToArray();
        bool allNodes = _scene.ShowAllNodes; int cap = FrameTiming.FrameRateCap;
        uint rng1 = _scene.Random.Rng1, rng2 = _scene.Random.Rng2;
        int oldHealth = victim.Health;
        var originalDraw = _scene.Entities.WhereEnumerable().OfType<BeamProjectileEntity>()
            .ToDictionary(b => b, b => b.ShouldDraw);
        var spawned = new List<BeamProjectileEntity>();
        object? fact = null;
        try
        {
            (Vector3 endpoint, Vector3 positive, Vector3 negative, Vector3 wall) = FindCameras();
            Vector3 direction = Vector3.Cross(endpoint - positive, Vector3.UnitY).Normalized();
            Vector3 origin = endpoint - direction * .9f;
            CollisionResult initialRay = default;
            Require(!CollisionDetection.CheckBetweenPoints(origin, endpoint, TestFlags.Beams, _scene, ref initialRay),
                "native fixture flight-to-endpoint segment is collision clear");
            // Source identity is explicit fixture setup, independent of current
            // inventory; all drawing/equipment resources come from native Spawn.
            SetSession(nameof(NetSession.Role), NetRole.Offline);
            var beam = Spawn(owner, origin, direction); spawned.Add(beam);
            var duplicate = Spawn(owner, origin, direction); spawned.Add(duplicate);
            var key = new ShotKey(77, 77, owner.SlotIndex, 1, 1, 77001);
            foreach (var row in spawned)
            {
                SetBeamIdentity(row, key, 1);
                Pose(row, origin, direction);
                row.Effect?.SetDrawEnabled(false); row.MuzzleEffect?.SetDrawEnabled(false);
                typeof(EntityBase).GetProperty(nameof(EntityBase.ShouldDraw))!.SetValue(row, false);
            }
            foreach (var row in originalDraw.Keys)
                typeof(EntityBase).GetProperty(nameof(EntityBase.ShouldDraw))!.SetValue(row, false);
            for (int i = 0; i < trackers.Length; i++)
            { trackers[i] = new NetLifecycleTracker(); trackers[i].SetOccupant(1); trackers[i].BeginLife(); }
            SetSession(nameof(NetSession.Role), NetRole.Host); SetSession(nameof(NetSession.IsAuthority), true);
            SetSession(nameof(NetSession.LocalSlot), owner.SlotIndex); SetSession(nameof(NetSession.NetFrame), (uint)400);
            SetSession(nameof(NetSession.ServerMatch), new MatchStatePacket { MatchId = 77, AuthorityEpoch = 77 });
            SetSession(nameof(NetSession.ConnectionLost), false);
            Manager.GetProperty("Enabled", Static)!.SetValue(null, true);
            Manager.GetMethod("Reset", Static)!.Invoke(null, null);
            var sinkProperty = typeof(NetSession).GetProperty("ConfirmedImpactSink", Static)!;
            var captureMethod = GetType().GetMethod(nameof(CaptureFact), Instance)!.MakeGenericMethod(FactType);
            sinkProperty.SetValue(null, Delegate.CreateDelegate(sinkProperty.PropertyType, this, captureMethod));
            typeof(BeamProjectileEntity).GetField("EnhancedDirectHit", Instance)!.SetValue(beam, true);
            typeof(PlayerEntity).GetMethod("ModReleaseSpawnProtection", Instance)!.Invoke(victim, null);
            typeof(PlayerEntity).GetField("_spawnInvulnTimer", Instance)!.SetValue(victim, (ushort)0);
            int healthBefore = victim.Health;
            var nativeDamage = typeof(BeamProjectileEntity).GetMethod("TakePlayerDamageAt", Instance,
                null, new[] { typeof(PlayerEntity), typeof(uint), typeof(DamageFlags), typeof(Vector3?), typeof(Vector3) }, null)!;
            nativeDamage.Invoke(beam, new object?[] { victim, (uint)1,
                DamageFlags.IgnoreInvuln | DamageFlags.NoDmgInvuln | DamageFlags.NoSfx, null, endpoint });
            Require(victim.Health < healthBefore && _publishedCount == 1 && _published != null,
                "one actual native health reduction publishes one authority-backed impact");
            fact = _published!;
            Require((ShotKey)Get(fact, "Key") == key && (uint)Get(fact, "Component") == 1
                && (Vector3)Get(fact, "Point") == endpoint && (byte)Get(fact, "Weapon") == (byte)beam.Beam,
                "native published fact retains exact source component weapon and endpoint");
            SetSession(nameof(NetSession.Role), NetRole.Client); SetSession(nameof(NetSession.IsAuthority), false);
            SetSession(nameof(NetSession.LocalSlot), victim.SlotIndex);
            // Authority and receiving client clocks are deliberately different.
            SetSession(nameof(NetSession.NetFrame), (uint)1400); snapshotField.SetValue(null, (uint)400);
            Require((bool)Manager.GetMethod("Accept", Static)!.Invoke(null, new[] { fact })!,
                "client accepts exact published fact using authority snapshot clock");
            _scene.ShowAllNodes = true; _scene.SetFreeCamera(true);
            SetFlag("DrawProjectiles", false); SetFlag("DrawCues", false);
            Camera(positive, endpoint);
            _scene.OnDrawFrame(); _scene.OnRenderFrame();
            Cue("confirmed-cue-clear", positive, endpoint, false);
            Cue("confirmed-cue-wall", negative, endpoint, true);

            Camera(positive, endpoint);
            SetFlag("DrawCues", false);
            typeof(EntityBase).GetProperty(nameof(EntityBase.ShouldDraw))!.SetValue(beam, true);
            Projectile("exact-component-clear", beam, true);
            Camera(negative, endpoint);
            Projectile("exact-component-wall", beam, true, occluded: true);
            Camera(positive, endpoint);
            typeof(BeamProjectileEntity).GetProperty(nameof(BeamProjectileEntity.ModComponentId))!.SetValue(beam, (uint)2);
            Projectile("wrong-component", beam, false);
            Cue("wrong-component-cue-fallback", positive, endpoint, false);
            typeof(BeamProjectileEntity).GetProperty(nameof(BeamProjectileEntity.ModComponentId))!.SetValue(beam, (uint)1);
            BeamType weapon = beam.Beam; beam.Beam = BeamType.Missile;
            Projectile("wrong-weapon", beam, false);
            beam.Beam = weapon;
            Pose(beam, origin, -direction);
            Projectile("ray-direction-outside-sixty-degree-cone", beam, false);
            Cue("wrong-ray-direction-cue-fallback", positive, endpoint, false);
            Pose(beam, origin, direction);
            typeof(EntityBase).GetProperty(nameof(EntityBase.ShouldDraw))!.SetValue(duplicate, true);
            Projectile("ambiguous-two-exact-components", beam, false);
            Cue("ambiguous-cue-fallback", positive, endpoint, false);
            typeof(EntityBase).GetProperty(nameof(EntityBase.ShouldDraw))!.SetValue(duplicate, false);
            Pose(beam, negative, (endpoint - negative).Normalized());
            CollisionResult blocked = default;
            Require(CollisionDetection.CheckBetweenPoints(beam.Position, endpoint, TestFlags.Beams, _scene, ref blocked),
                "fallback flight ray crosses actual room wall");
            Projectile("blocked-flight-ray", beam, false);
            Cue("blocked-flight-cue-fallback", positive, endpoint, false);
            Pose(beam, origin, direction);
            SetFlag("DrawProjectiles", true); SetFlag("DrawCues", true);
            Camera(positive, endpoint);
            _scene.OnDrawFrame(); _scene.OnRenderFrame();
            string baseline = SimulationState();
            foreach (int hz in new[] { 60, 120, 144, 240, 360 })
            {
                FrameTiming.FrameRateCap = hz;
                for (int i = 0; i < hz / 60; i++)
                { _scene.OnDrawFrame(); _scene.OnRenderFrame(); Require(SimulationState() == baseline, $"{hz} Hz redraw {i + 1} preserves simulation entities clocks and RNG"); }
            }
            Evidence = new { schema = 1, status = _failures.Count == 0 ? "PASS" : "FAIL",
                scope = "isolated authority-backed native asset/framebuffer fixture; explicit fixture source identity; native TakePlayerDamageAt endpoint; no UDP delivery or physical collision replay claim",
                assemblySha256 = Hash(File.ReadAllBytes(typeof(Scene).Assembly.Location)), source = Environment.GetEnvironmentVariable("PRIME_NET2_SOURCE") ?? "unknown",
                authorityFrame = (uint)400, receivingLocalFrame = (uint)1400, authoritySnapshotFrame = NetSession.LastSnapshotFrame,
                sourceIdentity = key, component = 1, healthBefore, healthAfter = victim.Health,
                publishedFact = new { Key = (ShotKey)Get(fact, "Key"), Component = (uint)Get(fact, "Component"),
                    Phase = (uint)Get(fact, "Phase"), Victim = (byte)Get(fact, "Victim"),
                    VictimGeneration = (ushort)Get(fact, "VictimGeneration"), VictimLife = (ushort)Get(fact, "VictimLife"),
                    DamageEvent = (ushort)Get(fact, "DamageEvent"), Frame = (uint)Get(fact, "Frame"),
                    Weapon = (byte)Get(fact, "Weapon"), Flags = Get(fact, "Flags").ToString(),
                    Provenance = Get(fact, "Provenance").ToString(), Point = V((Vector3)Get(fact, "Point")) },
                positiveCamera = V(positive), negativeCamera = V(negative), wall = V(wall),
                invariance = "frozen redraw at 60/120/144/240/360 Hz; scene/net frames, every entity pose, beam flight data, player HP and both RNG streams",
                cases = _cases, failures = _failures };
            File.WriteAllText(Path.Combine(_directory, "evidence.json"), JsonSerializer.Serialize(Evidence,
                new JsonSerializerOptions { WriteIndented = true }) + "\n");
            if (_failures.Count != 0) throw new InvalidDataException("Impact GPU failure: " + string.Join("; ", _failures));

            void Cue(string name, Vector3 cameraPosition, Vector3 point, bool occluded)
            {
                Camera(cameraPosition, point); SetFlag("DrawProjectiles", false); SetFlag("DrawCues", false);
                var before = Capture(name + "-off-before"); var depth = Depth(point);
                SetFlag("DrawCues", true); var enabled = Capture(name + "-cue-only");
                SetFlag("DrawCues", false); var after = Capture(name + "-off-after");
                int changed = Different(before.Pixels, enabled.Pixels), unstable = Different(before.Pixels, after.Pixels);
                bool geometry = occluded ? depth.Sample > 0 && depth.Sample < depth.Expected - .00001f
                    : depth.Sample > depth.Expected - .00001f;
                bool pass = geometry && unstable == 0 && (occluded ? changed == 0 : changed > 0);
                if (!pass) _failures.Add(name + $": geometry={geometry}, changed={changed}, unstable={unstable}");
                _cases.Add(new { name, arm = "cue-only", status = pass ? "PASS" : "FAIL", occluded,
                    changedPixels = changed, unstablePixels = unstable, geometryVerified = geometry,
                    worldDepth = depth.Sample, endpointDepth = depth.Expected, captures = new[] { before.Metadata, enabled.Metadata, after.Metadata } });
            }
            void Projectile(string name, BeamProjectileEntity source, bool expectedAnchor, bool occluded = false)
            {
                SetFlag("DrawCues", false); SetFlag("DrawProjectiles", false);
                string state = SimulationState(); var before = Capture(name + "-unanchored-before");
                var depth = Depth(endpoint);
                SetFlag("DrawProjectiles", true);
                Manager.GetMethod("BeginDraw", Static)!.Invoke(null, new object[] { _scene });
                object?[] args = { source, Vector3.Zero };
                bool anchored = (bool)Manager.GetMethod("TryAnchor", Static)!.Invoke(null, args)!;
                var enabled = Capture(name + "-projectile-only");
                SetFlag("DrawProjectiles", false); var after = Capture(name + "-unanchored-after");
                int changed = Different(before.Pixels, enabled.Pixels), unstable = Different(before.Pixels, after.Pixels);
                bool geometry = occluded ? depth.Sample > 0 && depth.Sample < depth.Expected - .00001f
                    : depth.Sample > depth.Expected - .00001f;
                bool pass = anchored == expectedAnchor && (!anchored || (Vector3)args[1]! == endpoint)
                    && (!expectedAnchor || geometry) && unstable == 0
                    && (expectedAnchor && !occluded ? changed > 0 : changed == 0) && SimulationState() == state;
                if (!pass) _failures.Add(name + $": anchored={anchored}, changed={changed}, unstable={unstable}, statePreserved={SimulationState() == state}");
                _cases.Add(new { name, arm = "exact-projectile-only; fallback cues disabled", expectedAnchor, anchored,
                    status = pass ? "PASS" : "FAIL", occluded, geometryVerified = geometry,
                    worldDepth = depth.Sample, endpointDepth = depth.Expected, sourceKey = source.ModLaunchKey, source.ModComponentId,
                    anchor = anchored ? V((Vector3)args[1]!) : null, changedPixels = changed, unstablePixels = unstable,
                    captures = new[] { before.Metadata, enabled.Metadata, after.Metadata } });
            }
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(_directory, "failure.txt"), ex.ToString() + "\n");
            throw;
        }
        finally
        {
            // Native recycle pairs destruction with removal: a destroyed trail
            // has no model and must not enter another draw before Process runs.
            foreach (var beam in spawned) { beam.Destroy(); _scene.RemoveEntity(beam); }
            foreach (var pair in originalDraw) typeof(EntityBase).GetProperty(nameof(EntityBase.ShouldDraw))!.SetValue(pair.Key, pair.Value);
            victim.Health = oldHealth;
            Array.Copy(oldTrackers, trackers, trackers.Length);
            for (int i = 0; i < managerFields.Length; i++)
                if (managerState[i] is Array saved && managerFields[i].GetValue(null) is Array current) Array.Copy(saved, current, saved.Length);
                else if (managerState[i] is object[] rows && managerFields[i].GetValue(null) is IList list)
                { list.Clear(); foreach (var row in rows) list.Add(row); }
                else if (!managerFields[i].IsInitOnly) managerFields[i].SetValue(null, managerState[i]);
            foreach (var pair in session) SetSession(pair.Key, pair.Value);
            snapshotField.SetValue(null, oldSnapshot);
            for (int i = 0; i < CameraFields.Length; i++) typeof(Scene).GetField(CameraFields[i], Instance)!.SetValue(_scene, camera[i]);
            _scene.ShowAllNodes = allNodes; FrameTiming.FrameRateCap = cap;
            _scene.Random.SetRng1(rng1); _scene.Random.SetRng2(rng2);
        }
    }

    void CaptureFact<T>(T fact) { _published = fact; _publishedCount++; }
    static object Get(object fact, string property) => FactType.GetProperty(property)!.GetValue(fact)!;
    static void SetSession(string property, object? value) => typeof(NetSession).GetProperty(property, Static)!.SetValue(null, value);
    static void SetFlag(string field, bool value) => Manager.GetField(field, Static)!.SetValue(null, value);
    void Require(bool value, string label)
    { if (!value) throw new InvalidDataException(label); Console.WriteLine("IMPACTGPU PASS " + label); }
    BeamProjectileEntity Spawn(PlayerEntity owner, Vector3 origin, Vector3 direction)
    {
        var alive = owner.EquipInfo.Beams.Where(b => b.Lifespan > 0).ToHashSet();
        var equip = new EquipInfo(_scene.WeaponRules[(int)BeamType.PowerBeam], owner.EquipInfo.Beams) { InfiniteAmmo = true };
        if (BeamProjectileEntity.Spawn(owner, equip, origin, direction, BeamSpawnFlags.NoMuzzle, owner.NodeRef, _scene) == BeamResultFlags.NoSpawn)
            throw new InvalidDataException("Real asset projectile spawn failed.");
        return owner.EquipInfo.Beams.Single(b => b.Lifespan > 0 && !alive.Contains(b));
    }
    static void SetBeamIdentity(BeamProjectileEntity beam, ShotKey key, uint component)
    {
        typeof(BeamProjectileEntity).GetProperty(nameof(BeamProjectileEntity.ModLaunchKey))!.SetValue(beam, key);
        typeof(BeamProjectileEntity).GetProperty(nameof(BeamProjectileEntity.ModComponentId))!.SetValue(beam, component);
        beam.ModShotId = key.ShotId; beam.ModLaunchMatch = key.MatchId; beam.ModLaunchAuthority = key.AuthorityEpoch;
        beam.ModLaunchGeneration = key.Generation; beam.ModLaunchLife = key.LifeId;
        typeof(BeamProjectileEntity).GetProperty(nameof(EntityBase.ShouldDraw))!.SetValue(beam, true);
    }
    static void Pose(BeamProjectileEntity beam, Vector3 position, Vector3 direction)
    {
        beam.Position = position; beam.BackPosition = position - direction * .5f; beam.Direction = direction;
        for (int i = 0; i < beam.PastPositions.Length; i++) beam.PastPositions[i] = beam.BackPosition;
    }
    (Vector3, Vector3, Vector3, Vector3) FindCameras()
    {
        foreach (float height in new[] { 2.5f, 1.5f, .8f })
        foreach (Vector3 direction in new[] { Vector3.UnitZ, -Vector3.UnitZ, Vector3.UnitX, -Vector3.UnitX })
        {
            Vector3 point = _scene.Players.Main.Position + Vector3.UnitY * height;
            Vector3 positive = point + direction * 5;
            CollisionResult clear = default, wall = default, blocked = default;
            if (CollisionDetection.CheckBetweenPoints(point, positive, TestFlags.Beams, _scene, ref clear)) continue;
            if (!CollisionDetection.CheckBetweenPoints(point, point - direction * 80, TestFlags.Beams, _scene, ref wall)) continue;
            Vector3 negative = wall.Position - direction * 2;
            if ((negative - point).LengthSquared >= 9
                && CollisionDetection.CheckBetweenPoints(negative, point, TestFlags.Beams, _scene, ref blocked))
                return (point, positive, negative, wall.Position);
        }
        throw new InvalidOperationException("No actual clear/wall camera pair; impact visibility unexercised.");
    }
    void Camera(Vector3 position, Vector3 target)
    {
        Vector3 facing = (target - position).Normalized(), right = Vector3.Cross(facing, Vector3.UnitY).Normalized();
        foreach (var pair in new[] { ("_cameraPosition", position), ("_cameraFacing", facing), ("_cameraRight", right), ("_cameraUp", Vector3.Cross(right, facing)) })
            typeof(Scene).GetField(pair.Item1, Instance)!.SetValue(_scene, pair.Item2);
    }
    string SimulationState()
    {
        var entities = new List<object>();
        foreach (var entity in _scene.Entities)
            entities.Add(entity is BeamProjectileEntity b
                ? new { entity.Id, Type = entity.Type.ToString(), Position = V(entity.Position),
                    Transform = entity.Transform.ToString(), Beam = new { b.ModLaunchKey, b.ModComponentId, b.ModShotId,
                        b.Lifespan, b.Age, Flags = (ushort)b.Flags, b.Damage, b.Speed, Velocity = V(b.Velocity),
                        Acceleration = V(b.Acceleration), Direction = V(b.Direction), Back = V(b.BackPosition),
                        Spawn = V(b.SpawnPosition), Past = b.PastPositions.Select(V).ToArray() } } as object
                : new { entity.Id, Type = entity.Type.ToString(), Position = V(entity.Position),
                    Speed = entity.GetType().GetProperty("Speed")?.GetValue(entity) is Vector3 velocity ? V(velocity) : null,
                    Health = entity is PlayerEntity p ? p.Health : -1 });
        return JsonSerializer.Serialize(new { _scene.FrameCount, _scene.ProcessFrame, NetSession.NetFrame, NetSession.LastSnapshotFrame,
            _scene.Random.Rng1, _scene.Random.Rng2, FacadeRng1 = Rng.Rng1, FacadeRng2 = Rng.Rng2, entities });
    }
    (byte[] Pixels, object Metadata) Capture(string name)
    {
        _scene.OnDrawFrame(); _scene.OnRenderFrame();
        byte[] pixels = _scene.ReadSceneTarget(out int width, out int height) ?? throw new InvalidDataException("Impact RGB readback failed.");
        if (pixels.Length != width * height * 3 || width <= 0 || height <= 0) throw new InvalidDataException("Impact RGB dimensions invalid.");
        string path = Path.Combine(_directory, name + ".png");
        if (!ScreenCapture.Save(_scene, path)) throw new IOException("Impact PNG write failed.");
        if (GL.GetError() is var error && error != ErrorCode.NoError) throw new InvalidOperationException("Impact GL error: " + error);
        return (pixels, new { path, width, height, rgbSha256 = Hash(pixels), pngSha256 = Hash(File.ReadAllBytes(path)) });
    }
    (float Sample, float Expected) Depth(Vector3 point)
    {
        Vector4 clip = Vector4.TransformRow(new Vector4(point, 1), _scene.ViewMatrix * _scene.PerspectiveMatrix);
        if (clip.W <= 0) throw new InvalidDataException("Impact depth sample behind camera.");
        Vector3 ndc = clip.Xyz / clip.W;
        var size = (Vector2i)typeof(Scene).GetField("_targetSize", Instance)!.GetValue(_scene)!;
        int x = (int)((ndc.X + 1) * .5f * size.X), y = (int)((ndc.Y + 1) * .5f * size.Y);
        if (x < 0 || y < 0 || x >= size.X || y >= size.Y) throw new InvalidDataException("Impact depth sample outside framebuffer.");
        var value = new float[1];
        GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, (int)typeof(Scene).GetField("_frameBuffer", Instance)!.GetValue(_scene)!);
        GL.ReadPixels(x, y, 1, 1, PixelFormat.DepthComponent, PixelType.Float, value);
        GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, 0);
        return (value[0], (ndc.Z + 1) * .5f);
    }
    static int Different(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) throw new InvalidDataException("Impact framebuffer size changed.");
        int count = 0;
        for (int i = 0; i < a.Length; i += 3)
            if (Math.Abs(a[i] - b[i]) > 2 || Math.Abs(a[i + 1] - b[i + 1]) > 2 || Math.Abs(a[i + 2] - b[i + 2]) > 2) count++;
        return count;
    }
    static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    static float[] V(Vector3 value) => new[] { value.X, value.Y, value.Z };
}

static class ImpactFixtureEntityIteration
{
    // Scene.Entities deliberately exposes only its allocation-free iterator.
    internal static IEnumerable<EntityBase> WhereEnumerable(this LinkedListIterator<EntityBase> source)
    { foreach (var entity in source) yield return entity; }
}
