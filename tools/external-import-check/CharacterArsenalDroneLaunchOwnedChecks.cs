using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using MphRead;
using MphRead.Entities;
using MphRead.Mods.Import;
using MphRead.Mods.Launcher;
using MphRead.Mods.Render.Characters;
using OpenTK.Windowing.GraphicsLibraryFramework;

// Caller owns the disposable import root and real product shell. This helper
// performs no export, Draw/Render, pixel readback, native damage or source pose writes.
internal static class CharacterArsenalDroneLaunchOwnedChecks
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly Action<KeyboardState, Keys, bool> Key = typeof(KeyboardState)
        .GetMethod("SetKeyState", Hidden | BindingFlags.Public)!.CreateDelegate<Action<KeyboardState, Keys, bool>>();
    private static readonly string[] DroneIds = ["trooper-drone-alpha", "trooper-drone-gamma"];
    private static readonly LocalBodyAppearance[] Bodies = [LocalBodyAppearance.Native, LocalBodyAppearance.TrooperPower,
        LocalBodyAppearance.TrooperIce, LocalBodyAppearance.TrooperWave, LocalBodyAppearance.TrooperPlasma];

    internal static void RunCpu(string imports, Action<bool, string> check)
    {
        Isolated(imports);
        var body = Ready(imports, "trooper-power", check); string bodyBefore = Inventory(body);
        check(CharacterSourceDronePose.RequiredOrdinals.SequenceEqual(new[] { 13, 75, 73, 74, 29, 4, 3, 2, 5, 6, 1, 18 }),
            "Arsenal reuses the existing exact twelve-clip drone presentation authority");
        check(typeof(CharacterSourceArsenalDroneSelection).IsSealed
            && !typeof(CharacterSourceArsenalDroneSelection).IsAssignableFrom(typeof(CharacterSourceDroneSelection))
            && typeof(CharacterSourceArsenalDroneSelection).GetConstructors(BindingFlags.Public | BindingFlags.Instance).Length == 0,
            "Arsenal capture is a separate sealed factory capability rather than an ordinary solo selection alias");
        var rows = new List<object>();
        foreach (string id in DroneIds)
        {
            var state = Ready(imports, id, check); string before = Inventory(state);
            byte[] pointer = File.ReadAllBytes(Pointer(imports, id));
            var selection = CharacterSourceArsenalDroneSelection.Load(id, CancellationToken.None, imports);
            var solo = CharacterSourceDroneSelection.Load(id, CancellationToken.None, imports);
            check(selection.Source.Generation == state.Receipt!.Generation && solo.Generation == selection.Source.Generation
                && selection.Source.Profile.Id == id && selection.Source.Appearance == solo.Appearance,
                "distinct Arsenal capability retains exactly the preexisting own drone generation and appearance");
            check(selection.Source.Pack.TryResolvePrivate(Hunter.Weavel, selection.Source.Appearance,
                CharacterModelPart.AlternateForm, 0, out var asset) && selection.Source.Pack.HasSourceDrone(asset)
                && selection.Source.Pack.TryCreateSourcePreviewLease(asset, out _),
                "captured Arsenal source resolves its own complete AlternateForm proof with no body/cannon alias");
            CharacterSourceArsenalDroneSelection? published = null;
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                Reject<OperationCanceledException>(() => published = CharacterSourceArsenalDroneSelection.Load(id,
                    cancellation.Token, imports), check, "pre-cancelled Arsenal capture publishes no partial source capability");
            }
            check(published == null, "cancelled source capture remains unpublished");
            bool beforeCommit = false;
            using (var cancellation = new CancellationTokenSource())
            {
                Reject<OperationCanceledException>(() => LocalImportBundle.Publish(imports, Spec(id),
                    state.Files.ToDictionary(f => Path.GetFileName(f)!, File.ReadAllBytes, StringComparer.Ordinal),
                    cancellation.Token, new InlineProgress(stage =>
                    { if (stage == ImportPublishStage.BeforeCommit) { beforeCommit = true; cancellation.Cancel(); } }),
                    replaceAllComponents: true), check, "actual drone publication cancels transactionally at BeforeCommit");
            }
            check(beforeCommit && pointer.SequenceEqual(File.ReadAllBytes(Pointer(imports, id)))
                && Inventory(Ready(imports, id, check)) == before && Inventory(Ready(imports, "trooper-power", check)) == bodyBefore
                && !Directory.EnumerateDirectories(Path.GetDirectoryName(Pointer(imports, id))!, ".stage-*").Any(),
                "cancel retains exact old twelve drone bytes/pointer and independently captured thirteen body bytes");
            foreach (var bodyChoice in Bodies)
            {
                var plan = new LaunchPlan { Kind = LaunchKind.Offline, Hunter = Hunter.Weavel, Mode = GameMode.Battle,
                    RoomKey = "synthetic-arena", Bots = 1, BotLevel = 0, IsPlaytest = true,
                    OfflineCharacter = OfflineCharacterMode.TrooperArsenalPractice, LocalArsenalBody = bodyChoice,
                    LocalArsenalDrone = id == DroneIds[0] ? LocalDroneAppearance.Alpha : LocalDroneAppearance.Gamma,
                    MatchRules = new() { Mode = GameMode.Battle, MovementModel = MphRead.Mods.Movement.MovementModel.Native } };
                check(LocalArsenalBodyRules.TryAdmit(plan, out _) && LocalArsenalDroneRules.TryAdmit(plan, out _)
                    && plan.LocalBody == LocalBodyAppearance.Native && plan.LocalDrone == LocalDroneAppearance.Native,
                    "closed independent Arsenal BODY/Drone combinations never widen either ordinary request");
            }
            rows.Add(new { Id = id, selection.Source.Generation, Components = state.Files.Count,
                ComponentInventorySha256 = before, BodyInventorySha256 = bodyBefore, BeforeCommitCancelled = true });
        }
        string missing = Path.Combine(imports, "arsenal-drone-refusal-only");
        foreach (string id in new[] { "trooper-power", "darksamus-echoes", "trooper", "TROOPER-DRONE-ALPHA", "unknown" })
            Reject<InvalidDataException>(() => CharacterSourceArsenalDroneSelection.Load(id, CancellationToken.None, missing),
                check, "wrong source/alias cannot acquire Arsenal drone capability before IO: " + id);
        check(!Directory.Exists(missing) && Inventory(Ready(imports, "trooper-power", check)) == bodyBefore,
            "closed refusal creates no import tree and leaves the independent source body unchanged");
        Console.WriteLine(JsonSerializer.Serialize(new { Probe = "arsenal-drone-owned-cpu-launch-inputs", Profiles = rows,
            SourceExports = 0, RuntimeRenderingClaim = false, IndependentBodyPreserved = true }));
    }

    internal static void RunLaunch(RenderWindow window, MenuSettings settings, LaunchPlan arsenalPlan,
        string imports, Action<bool, string> check)
    {
        Isolated(imports);
        check(!window.HasScene && Path.GetFullPath(imports) == Path.GetFullPath(RetroCharacterPreviewExporter.DefaultRoot),
            "drone lifecycle uses caller's existing shell and isolated product root");
        string id = LocalDroneAppearanceRules.BundleId(arsenalPlan.LocalArsenalDrone)
            ?? throw new InvalidDataException("Explicit Arsenal source drone required.");
        string bodyId = LocalBodyAppearanceRules.BundleId(arsenalPlan.LocalArsenalBody)
            ?? throw new InvalidDataException("Independent explicit source body required for paired lifecycle.");
        var state = Ready(imports, id, check); var bodyState = Ready(imports, bodyId, check);
        var original = state.Files.ToDictionary(f => Path.GetFileName(f)!, File.ReadAllBytes, StringComparer.Ordinal);
        string before = Inventory(state), generation = state.Receipt!.Generation, bodyBefore = Inventory(bodyState);
        string bodyGeneration = bodyState.Receipt!.Generation;
        var selection = CharacterSourceArsenalDroneSelection.Load(id, CancellationToken.None, imports);
        var solo = CharacterSourceDroneSelection.Load(id, CancellationToken.None, imports);
        var nativePlan = arsenalPlan with { LocalArsenalBody = LocalBodyAppearance.Native,
            LocalArsenalDrone = LocalDroneAppearance.Native, LocalBody = LocalBodyAppearance.Native,
            LocalDrone = LocalDroneAppearance.Native, OfflineCharacter = OfflineCharacterMode.Native, IsPlaytest = false };
        var failures = new List<object>();
        try
        {
            check(MatchStart.Begin(window, settings, nativePlan), "prior actual Native Scene establishes preflight transaction: " + MatchStart.LastError);
            var prior = window.Scene;
            Reject<InvalidOperationException>(() => { using var invalid = new CharacterSourceArsenalDroneOwner(prior,
                prior.Players.Main, selection, arsenalPlan.Bots); }, check, "sealed Arsenal selection cannot bind an ordinary native actor");
            string pointer = Pointer(imports, id);
            Change(id, pointer, () => File.Delete(pointer), "missing-drone-discovery", ImportBundleState.NotInstalled);
            string material = state.Files.Single(f => f.EndsWith(".materials.bin", StringComparison.Ordinal));
            Change(id, material, () => File.Delete(material), "missing-drone-material", ImportBundleState.Incomplete);
            string graph = state.Files.Single(f => f.EndsWith(".ancs-graph.json", StringComparison.Ordinal));
            Change(id, graph, () => File.Delete(graph), "missing-drone-graph", ImportBundleState.Incomplete);
            Change(id, material, () => { var bytes = File.ReadAllBytes(material); bytes[^1] ^= 1; File.WriteAllBytes(material, bytes); },
                "corrupt-drone-material", ImportBundleState.Failed);
            // No historical drone exporter is asserted: valid discovery structure with a deliberately stale recipe only.
            Change(id, pointer, () => File.WriteAllBytes(pointer, JsonSerializer.SerializeToUtf8Bytes(state.Receipt with
                { RecipeHash = new string('0', 64) })), "declared-outdated-drone-recipe", ImportBundleState.Outdated);
            string bodyMaterial = bodyState.Files.Single(f => f.EndsWith(".materials.bin", StringComparison.Ordinal));
            Change(bodyId, bodyMaterial, () => File.Delete(bodyMaterial), "missing-independent-body-material", ImportBundleState.Incomplete);
            check(Inventory(Ready(imports, id, check)) == before && Inventory(Ready(imports, bodyId, check)) == bodyBefore,
                "failed combined launch restores all independent twelve-plus-thirteen bytes");
            window.EndScene();
            check(MatchStart.Begin(window, settings, arsenalPlan), "actual paired Arsenal Begin succeeds: " + MatchStart.LastError);
            var scene = window.Scene; var player = scene.Players.Main;
            var owner = player.ModSourceArsenalDrone ?? throw new InvalidDataException("Missing captured Arsenal drone.");
            var body = player.ModSourceArsenalBody ?? throw new InvalidDataException("Missing independent Arsenal body.");
            check(player.Initialized && player.Health == 0 && !player.LoadFlags.TestFlag(LoadFlags.Spawned)
                && !owner.Current && !owner.Ready && !body.Current && !body.Ready && !body.CannonReady
                && owner.Generation == generation && body.Generation == bodyGeneration
                && owner.Model.Joints.Count == 33 && owner.Model.IndexCount == 1390 * 3 && owner.Model.Segments.Count == 2
                && body.Model.Joints.Count == 35 && body.CannonModel.IndexCount == 206 * 3,
                "paired structural residents remain complete and noncurrent before genuine native Spawn");
            Reject<InvalidOperationException>(() => { using var invalid = new CharacterSourceDroneOwner(scene, player, solo); },
                check, "ordinary solo owner cannot grant Arsenal presentation authority");
            check(ReferenceEquals(player.ModSourceArsenalDrone, owner) && ReferenceEquals(player.ModSourceArsenalBody, body),
                "wrong-owner refusal leaves both actual attachments unchanged");
            var mode = typeof(Scene).GetField("_inputMode", Hidden)!; mode.SetValue(scene, Enum.Parse(mode.FieldType, "All"));
            foreach (var control in player.Controls.All) { control.Type = ButtonType.Key; control.Key = Keys.Unknown; }
            player.Controls.Shoot.Key = Keys.Enter; player.Controls.MouseAim = player.Controls.KeyboardAim = false;
            scene.GameState.MatchTime = -1; scene.GameState.PointGoal = 10000;
            Key(window.KeyboardState, Keys.Enter, true);
            try { for (int n = 0; n < 600 && !player.LoadFlags.TestFlag(LoadFlags.Spawned); n++) scene.OnSimulationFrame(); }
            finally { Key(window.KeyboardState, Keys.Enter, false); }
            scene.CameraSequences.Current?.End(); scene.OnSimulationFrame();
            check(player.Health > 0 && scene.PirateBlaster!.Available(player) && owner.Current && body.Current,
                "only genuine shoot-to-Spawn establishes the shared native weapon-life authority");
            check(LocalImportBundle.Remove(imports, id) && owner.Current && body.Current
                && owner.Generation == generation && body.Generation == bodyGeneration && state.Files.All(File.Exists),
                "Remove withdraws only drone discovery while both live immutable captures remain resident");
            Refuse("removed-drone-discovery", scene);
            LocalImportBundle.Publish(imports, Spec(id), original, replaceAllComponents: true);
            check(Ready(imports, id, check).Receipt!.Generation == generation && owner.Current && body.Current,
                "identical reinstall does not replace either live captured owner");
            byte[] saved = File.ReadAllBytes(material);
            try { var damaged = saved.ToArray(); damaged[^1] ^= 1; File.WriteAllBytes(material, damaged);
                check(LocalImportBundle.Inspect(imports, id, Spec(id)).State == ImportBundleState.Failed, "isolated old drone corruption diagnosed");
                LocalImportBundle.Publish(imports, Spec(id), original, replaceAllComponents: true); }
            finally { File.WriteAllBytes(material, saved); }
            var repaired = Ready(imports, id, check); string repairedGeneration = repaired.Receipt!.Generation;
            check(repairedGeneration != generation && Inventory(repaired) == before && owner.Generation == generation && owner.Current
                && body.Current && body.Generation == bodyGeneration && Inventory(Ready(imports, bodyId, check)) == bodyBefore,
                "repair gives drone a fresh immutable generation and preserves active old drone plus independent body");
            window.EndScene(); check(!owner.Current && !owner.Ready && !body.Current && !body.CannonCurrent,
                "EndScene retires both independently captured presentation capabilities");
            check(MatchStart.Begin(window, settings, arsenalPlan), "relaunch discovers the repaired drone: " + MatchStart.LastError);
            var nextDrone = window.Scene.Players.Main.ModSourceArsenalDrone;
            var nextBody = window.Scene.Players.Main.ModSourceArsenalBody;
            check(nextDrone != null && nextBody != null && !ReferenceEquals(nextDrone, owner) && !ReferenceEquals(nextBody, body)
                && nextDrone.Generation == repairedGeneration && nextBody.Generation == bodyGeneration && !owner.Current && !body.Current,
                "relaunch binds fresh owners for new drone and unchanged body without resurrection");
            window.EndScene(); check(!window.HasScene && !nextDrone!.Current && !nextBody!.Current,
                "paired lifecycle ends in shell with both replacement owners retired");
            Console.WriteLine(JsonSerializer.Serialize(new { Probe = "arsenal-drone-real-launch-lifecycle", Id = id,
                OriginalGeneration = generation, RepairedGeneration = repairedGeneration, BodyGeneration = bodyGeneration,
                Failures = failures, IndependentBodyPreserved = true, PreSpawnCurrent = false, GenuineNativeSpawnRequired = true,
                NoSceneAtEnd = !window.HasScene, RenderingClaim = false }));

            void Change(string changedId, string file, Action mutate, string label, ImportBundleState expected)
            {
                LocalImportBundle.CheckPath(file, imports); byte[] bytes = File.ReadAllBytes(file);
                try { mutate(); check(LocalImportBundle.Inspect(imports, changedId, Spec(changedId)).State == expected,
                    "exact isolated combined preflight diagnosis: " + label); Refuse(label, prior); }
                finally { File.WriteAllBytes(file, bytes); }
                check(Ready(imports, id, check).Receipt!.Generation == generation
                    && Ready(imports, bodyId, check).Receipt!.Generation == bodyGeneration,
                    "restored preflight failure retains both discovery generations: " + label);
            }
            void Refuse(string label, Scene expected)
            {
                ulong frame = expected.FrameCount; float time = expected.GlobalElapsedTime;
                check(!MatchStart.Begin(window, settings, arsenalPlan) && window.HasScene && ReferenceEquals(window.Scene, expected)
                    && !expected.Exiting && expected.FrameCount == frame && expected.GlobalElapsedTime == time
                    && !string.IsNullOrWhiteSpace(MatchStart.LastError), "whole paired launch refuses before replacing/advancing prior Scene: " + label);
                failures.Add(new { Phase = label, PriorScenePreserved = true,
                    ErrorSha256 = Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(MatchStart.LastError!))) });
            }
        }
        finally { Key(window.KeyboardState, Keys.Enter, false); if (window.HasScene) window.EndScene(); }
    }

    private static ImportBundleSpec Spec(string id) => RetroCharacterPreviewExporter.BundleSpec(id, ExternalSourceEdition.PrimeUsaRevision2);
    private static string Pointer(string root, string id) => Path.Combine(root, LocalImportBundle.StorageDirectory, id, "current.json");
    private static ImportBundleInspection Ready(string root, string id, Action<bool, string> check)
    {
        var spec = Spec(id); var state = LocalImportBundle.Inspect(root, id, spec); bool drone = DroneIds.Contains(id, StringComparer.Ordinal);
        check(state.State == ImportBundleState.Ready && state.Receipt != null && state.Receipt.Version == (drone ? 1 : 6)
            && state.Files.Count == (drone ? 12 : 13) && state.Files.Select(Path.GetFileName).ToHashSet().SetEquals(spec.RequiredComponents)
            && state.Files.All(f => Path.GetDirectoryName(f) == Path.GetDirectoryName(state.Files[0]))
            && Path.GetFileName(Path.GetDirectoryName(state.Files[0])) == state.Receipt.Generation,
            "exact unchanged drone-v1 or body-v6 components share one verified immutable generation: " + id);
        return state;
    }
    private static string Inventory(ImportBundleInspection state) => Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(
        state.Files.OrderBy(Path.GetFileName, StringComparer.Ordinal).Select(f => new { File = Path.GetFileName(f),
            Bytes = new FileInfo(f).Length, Sha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(f))) }))));
    private static void Isolated(string root)
    { if (!Path.GetFullPath(root).StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()))
        + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Disposable import tree required.");
        LocalImportBundle.CheckPath(root, root); }
    private static void Reject<T>(Action action, Action<bool, string> check, string label) where T : Exception
    { bool refused = false; try { action(); } catch (T) { refused = true; } check(refused, label); }
    private sealed class InlineProgress(Action<ImportPublishStage> action) : IProgress<ImportPublishStage>
    { public void Report(ImportPublishStage stage) => action(stage); }
}
