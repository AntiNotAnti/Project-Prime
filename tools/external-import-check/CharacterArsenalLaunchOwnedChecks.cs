using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using MphRead;
using MphRead.Entities;
using MphRead.Mods.Import;
using MphRead.Mods.Launcher;
using MphRead.Mods.Multiplayer;
using MphRead.Mods.Render.Characters;
using OpenTK.Windowing.GraphicsLibraryFramework;

// Called by the owned runtime probe in its disposable import tree/window.
// No exporter, substitute launcher, GPU readback or media output lives here.
internal static class CharacterArsenalLaunchOwnedChecks
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly Action<KeyboardState, Keys, bool> Key = typeof(KeyboardState)
        .GetMethod("SetKeyState", Hidden | BindingFlags.Public)!.CreateDelegate<Action<KeyboardState, Keys, bool>>();
    // Literal existing body/cannon qualification pins, not generated from the
    // new Arsenal factory. Arsenal does not introduce an authoring migration.
    private sealed record Pins(string Id, string Skin, string Model, string Material, string Bank, string Cannon);
    private static readonly Pins[] Profiles =
    [
        new("trooper-power", "MP1PowerTrooper",
            "54f298805409fa12f34c34203cf7db2f5085f5b5ec4410ac04b3d329da71f3e8",
            "50f5acc05dc966847c96f05e9bafa045777510e77d6e3a3f224227020a9c553a",
            "a1b05e777345420456b1c2ace0c961a07bb2c54992536d30ef656e78593257b8",
            "2b128462891c9aa85ebae92b68306345386c7dcaeabdee043ff522de69a4ecc5"),
        new("trooper-ice", "MP1IceTrooper",
            "c3f7c8f9c77824937522db1199970a5f9d0be94e8dbfdbe35a66b7c5adfac307",
            "16f6f537954145940030874afb6d8dae3187de08e4f5997568085c3ad96701f0",
            "63ef357c9c0c945bb3ff97bf23b891cc028cd3de27b45dd9148d574cf927ce44",
            "2074a896f47f0bd72402076afa1347287958bfef5682a225a287e4a83afe9363"),
        new("trooper-wave", "MP1WaveTrooper",
            "6a307e91a5bfdd82a0f1ae904b3179917fc7f3da70e2c3546ae2feffec32653b",
            "d84ae5a8ecfe71703020dfc0348fcac23713726e5af03c9fc26942654c1d2c6b",
            "6b95ba35dce96cd3fbfd6de5709b51bef89bbea1777ae3725556d9f484f648d7",
            "1c1cb397a741ebab61d1a36d785c194b4bad7f764c9d6684d86ec9f50e42c79b"),
        new("trooper-plasma", "MP1PlasmaTrooper",
            "7877c9f7b7a27dc8c804c04ea5dc03e364504e71f3c7e744d58e95e688e4e96f",
            "0eb01263a7dad10519af2c70e3158fd623b3a56bf18251c625e7761e651a5933",
            "3ee7cd22d0c64374c28c08ba321c4b9d62d8b3ee653c04cd922836e089aeb7b2",
            "1f8a1dc3eaf7d5a43cdc3d7e2fde28cdff63b95040f066fad57e1eb2d86584c1")
    ];

    internal static void RunCpu(string imports, Action<bool, string> check)
    {
        Isolated(imports);
        var rows = new List<object>();
        foreach (var profile in Profiles)
        {
            var state = Ready(imports, profile.Id, check);
            string before = Inventory(state);
            byte[] pointerBefore = File.ReadAllBytes(Pointer(imports, profile.Id));
            var selection = CharacterSourcePreviewSelection.LoadForArsenalBody(profile.Id, CancellationToken.None, imports);
            var ordinary = CharacterSourcePreviewSelection.LoadForLocalBody(profile.Id, CancellationToken.None, imports);
            var viewer = CharacterSourcePreviewSelection.Load(profile.Id, CancellationToken.None, imports);
            check(selection.RuntimeArsenalBody && selection.RuntimeCannon && selection.HasCannon
                && !selection.HasBall && !selection.RuntimeBall && selection.NativeHunter == Hunter.Weavel
                && selection.SelectedAppearance == new PrivateCharacterAppearance("trooper", profile.Skin),
                "explicit Arsenal CPU capture has exactly its own finite Trooper body/cannon capability");
            check(!ordinary.RuntimeArsenalBody && ordinary.RuntimeCannon && !viewer.RuntimeArsenalBody
                && !viewer.RuntimeCannon && selection.Generation == state.Receipt!.Generation
                && ordinary.Generation == selection.Generation && viewer.Generation == selection.Generation,
                "ordinary local body and viewer cannot self-grant Arsenal despite identical source generation");
            var pack = selection.Pack;
            check(pack.TryResolvePrivate(Hunter.Weavel, selection.SelectedAppearance, CharacterModelPart.Biped, 0, out var body)
                && pack.TryResolvePrivate(Hunter.Weavel, selection.SelectedAppearance, CharacterModelPart.ViewModel, 0, out _),
                "same captured pack resolves its explicit Biped and ViewModel parts");
            pack.TryResolvePrivate(Hunter.Weavel, selection.SelectedAppearance, CharacterModelPart.ViewModel, 0, out var cannon);
            check(pack.TryCreateSourcePreviewLease(body, out var bodyLease)
                && pack.TryCreateSourcePreviewLease(cannon, out _), "both exact part leases are issued by the captured pack");
            pack.TryCreateSourcePreviewLease(cannon, out var cannonLease);
            string folder = Path.GetDirectoryName(state.Files[0])!;
            check(body.ModelPath == cannon.ModelPath && Path.GetDirectoryName(body.ModelPath) == folder
                && Path.GetFileName(folder) == selection.Generation
                && ReferenceEquals(bodyLease.SourceMaterials, cannonLease.SourceMaterials)
                && bodyLease.SourceAnimations?.Sha256 == profile.Bank && cannonLease.SourceAnimations?.Sha256 == profile.Bank,
                "body and cannon retain the same immutable geometry/material authority and own raw animation bank");
            var geometry = CharacterWeightedModelLoader.LoadSourcePreview(body, bodyLease);
            var cut = CharacterWeightedModelLoader.LoadSourcePreview(cannon, cannonLease);
            check(geometry.Joints.Count == 35 && geometry.Primitives.Sum(p => p.Indices.Length) == 1970 * 3
                && cut.Joints.Count == 35 && cut.Primitives.Sum(p => p.Indices.Length) == 206 * 3
                && cut.Primitives.Select(p => p.Indices.Length / 3).SequenceEqual(new[] { 10, 32, 164 })
                && cut.SourceCannon is { IsGenerated: true, TriangleCount: 206, ClipOrdinal: 1, ClipResourceId: 0x5528972b }
                && cut.SourceCannon.GeneratedTrooper?.Id == profile.Id,
                "Arsenal reuses the complete original35 rig/1970 body and genuine own206 whole-triangle cut");
            check(HashFile(Path.Combine(folder, profile.Id + ".source.glb")) == profile.Model
                && HashFile(Path.Combine(folder, profile.Id + ".materials.bin")) == profile.Material
                && HashFile(Path.Combine(folder, profile.Id + ".animations.bin")) == profile.Bank
                && HashFile(Path.Combine(folder, profile.Id + ".cannon-source.json")) == profile.Cannon
                && HashFile(Path.Combine(folder, profile.Id + ".cinf.bin")) == "937aa40d2074d819a6d4d89946eb7a059f44db73f6f2b54854de7b914b7ddbf9"
                && HashFile(Path.Combine(folder, profile.Id + ".ancs.bin")) == "7f9ae5c119a9d9a1ddca105be582e9914e51b8c36d1435ea8ba742b7da78c5d4",
                "all preexisting source geometry/rawGX/bank/cannon/rig/ANCS pins remain unchanged");

            var clip = CharacterSourceClip.Load(cannonLease, cannon, cut.SourceRig!, 1);
            check(clip.ContentSha256 == "5b6540fbfdcbf37edd24133b4475468e0f0199a744e3159df2f0b942c498ccc7",
                "cannon fixed source primitive remains its own original raw ANIM");
            var locks = new List<FileStream>();
            try
            {
                foreach (string file in state.Files) locks.Add(new(file, FileMode.Open, FileAccess.Read, FileShare.None));
                for (int n = 0; n < 4; n++) check(clip.Sample(0).Count == 36,
                    "captured full35 plus mesh node samples while every generation component is exclusively locked");
            }
            finally { foreach (var file in locks) file.Dispose(); }
            CharacterSourcePreviewSelection? published = null;
            using (var cancelled = new CancellationTokenSource())
            {
                cancelled.Cancel();
                Reject<OperationCanceledException>(() => published = CharacterSourcePreviewSelection.LoadForArsenalBody(
                    profile.Id, cancelled.Token, imports), check, "pre-cancelled CPU capture never publishes a partial selection");
            }
            check(published == null, "cancelled capture does not publish a capability-bearing partial result");
            bool beforeCommit = false;
            using (var cancelled = new CancellationTokenSource())
            {
                Reject<OperationCanceledException>(() => LocalImportBundle.Publish(imports, Spec(profile.Id),
                    state.Files.ToDictionary(f => Path.GetFileName(f)!, File.ReadAllBytes, StringComparer.Ordinal), cancelled.Token,
                    new InlineProgress(stage => { if (stage == ImportPublishStage.BeforeCommit) { beforeCommit = true; cancelled.Cancel(); } }),
                    replaceAllComponents: true), check, "real same-source transaction cancels at BeforeCommit");
            }
            check(beforeCommit && pointerBefore.SequenceEqual(File.ReadAllBytes(Pointer(imports, profile.Id)))
                && before == Inventory(Ready(imports, profile.Id, check))
                && !Directory.EnumerateDirectories(Path.GetDirectoryName(Pointer(imports, profile.Id))!, ".stage-*").Any(),
                "cancelled publication retains exact old pointer/all13 bytes and no staged partial payload");
            rows.Add(new { profile.Id, selection.Generation, Components = 13, BodyTriangles = 1970, CannonTriangles = 206,
                profile.Model, profile.Material, profile.Bank, profile.Cannon, CancelledCapturePublished = false,
                BeforeCommitCancelled = beforeCommit, ComponentInventorySha256 = before });
        }
        string missing = Path.Combine(imports, "arsenal-refusal-only-" + Guid.NewGuid().ToString("N"));
        foreach (string id in new[] { "darksamus-echoes", "darksamus-overcharged-echoes", "trooper-drone-alpha",
            "trooper-drone-gamma", "trooper", "TROOPER-POWER", "unknown" })
            Reject<NotSupportedException>(() => CharacterSourcePreviewSelection.LoadForArsenalBody(id, CancellationToken.None, missing),
                check, "wrong source/alias cannot enter Arsenal capture before IO: " + id);
        check(!Directory.Exists(missing), "wrong-profile capture never creates a derived import tree");
        Console.WriteLine(JsonSerializer.Serialize(new { Probe = "arsenal-body-owned-cpu-launch-inputs", Profiles = rows,
            SourceExports = 0, RuntimeRenderingClaim = false, AllFourExistingBundlesPreserved = true }));
    }

    internal static void RunLaunch(RenderWindow window, MenuSettings settings, LaunchPlan arsenalPlan,
        string imports, Action<bool, string> check)
    {
        Isolated(imports);
        check(!window.HasScene && Path.GetFullPath(imports) == Path.GetFullPath(RetroCharacterPreviewExporter.DefaultRoot),
            "Arsenal launch lifecycle uses the caller's existing shell and isolated product import root");
        string id = LocalBodyAppearanceRules.BundleId(arsenalPlan.LocalArsenalBody)
            ?? throw new InvalidDataException("Explicit nonnative Arsenal plan required for launch checks.");
        var state = Ready(imports, id, check);
        var original = state.Files.ToDictionary(f => Path.GetFileName(f)!, File.ReadAllBytes, StringComparer.Ordinal);
        string originalInventory = Inventory(state), originalGeneration = state.Receipt!.Generation;
        var selection = CharacterSourcePreviewSelection.LoadForArsenalBody(id, CancellationToken.None, imports);
        var ordinary = CharacterSourcePreviewSelection.LoadForLocalBody(id, CancellationToken.None, imports);
        var nativePlan = arsenalPlan with { LocalArsenalBody = LocalBodyAppearance.Native,
            LocalBody = LocalBodyAppearance.Native, LocalDrone = LocalDroneAppearance.Native,
            OfflineCharacter = OfflineCharacterMode.Native, IsPlaytest = false };
        var retired = new List<CharacterSourceArsenalBodyOwner>();
        var failures = new List<object>();
        try
        {
            check(MatchStart.Begin(window, settings, nativePlan), "real native Begin establishes prior Scene before import failure fixtures: " + MatchStart.LastError);
            var oldScene = window.Scene;
            check(oldScene.Players.Main.Initialized && oldScene.Players.Main.ModCharacterGameplay == CharacterGameplayRules.Native,
                "prior native initialized actor owns no Arsenal gameplay capability");
            Reject<InvalidOperationException>(() => { using var forbidden = new CharacterSourceBodyOwner(oldScene,
                oldScene.Players.Main, selection); }, check, "explicit Arsenal selection cannot enter the Native-only body constructor");

            string pointer = Pointer(imports, id);
            Change(pointer, () => File.Delete(pointer), "missing-discovery", ImportBundleState.NotInstalled);
            string material = state.Files.Single(f => Path.GetFileName(f) == id + ".materials.bin");
            Change(material, () => File.Delete(material), "missing-material-component", ImportBundleState.Incomplete);
            string recipe = state.Files.Single(f => Path.GetFileName(f) == id + ".cannon-source.json");
            Change(recipe, () => { byte[] bytes = File.ReadAllBytes(recipe); bytes[^1] ^= 1; File.WriteAllBytes(recipe, bytes); },
                "corrupt-cannon-recipe", ImportBundleState.Failed);
            // Declared outdated discovery metadata over retained current bytes;
            // this is not a claim to reproduce an old exporter generation.
            Change(pointer, () => File.WriteAllBytes(pointer, JsonSerializer.SerializeToUtf8Bytes(state.Receipt with
            { Version = 5, RecipeHash = PrimeRevision2RecipeChecks.PreviousRev5(id).RecipeHash })),
                "declared-outdated-receipt", ImportBundleState.Outdated);
            check(originalInventory == Inventory(Ready(imports, id, check)), "all four launch refusal fixtures restore exact original13 component bytes");
            window.EndScene();

            check(MatchStart.Begin(window, settings, arsenalPlan), "real explicit Arsenal Begin binds selected residents before native Spawn: " + MatchStart.LastError);
            var scene = window.Scene; var player = scene.Players.Main;
            var owner = player.ModSourceArsenalBody ?? throw new InvalidDataException("Real Begin installed no Arsenal owner.");
            check(player.Initialized && player.Health == 0 && !player.LoadFlags.TestFlag(LoadFlags.Spawned)
                && !owner.Ready && !owner.CannonReady && !owner.Current && owner.Generation == originalGeneration
                && owner.Model.IndexCount == 1970 * 3 && owner.CannonModel.IndexCount == 206 * 3,
                "real load barrier binds structural body/cannon residents without inventing native life or readiness");
            Reject<InvalidOperationException>(() => { using var forbidden = new CharacterSourceArsenalBodyOwner(scene,
                player, ordinary, arsenalPlan.Bots); }, check, "ordinary LoadForLocalBody cannot grant Arsenal constructor authority");
            check(ReferenceEquals(player.ModSourceArsenalBody, owner), "rejected ordinary selection leaves the actual current Arsenal attachment intact");
            typeof(Scene).GetField("_inputMode", Hidden)!.SetValue(scene,
                Enum.Parse(typeof(Scene).GetField("_inputMode", Hidden)!.FieldType, "All"));
            foreach (var control in player.Controls.All) { control.Type = ButtonType.Key; control.Key = Keys.Unknown; }
            player.Controls.Shoot.Key = Keys.Enter; player.Controls.MouseAim = player.Controls.KeyboardAim = false;
            scene.GameState.MatchTime = -1; scene.GameState.PointGoal = 10000;
            Key(window.KeyboardState, Keys.Enter, true);
            try { for (int n = 0; n < 600 && !player.LoadFlags.TestFlag(LoadFlags.Spawned); n++) scene.OnSimulationFrame(); }
            finally { Key(window.KeyboardState, Keys.Enter, false); }
            scene.CameraSequences.Current?.End();
            scene.OnSimulationFrame();
            check(player.Health > 0 && owner.Current && scene.PirateBlaster?.Available(player) == true,
                "only genuine native shoot-to-Spawn establishes the captured Arsenal weapon life/currentness");

            check(LocalImportBundle.Remove(imports, id), "actual Remove atomically withdraws Arsenal discovery");
            check(owner.Current && owner.Generation == originalGeneration && state.Files.All(File.Exists),
                "Remove retains the already captured immutable body/cannon residents for this live Scene");
            RefusedWithScene("removed-discovery", scene);
            LocalImportBundle.Publish(imports, Spec(id), original, replaceAllComponents: true);
            check(Ready(imports, id, check).Receipt!.Generation == originalGeneration && owner.Current,
                "identical reinstallation preserves content generation without replacing the live owner");

            byte[] saved = File.ReadAllBytes(material);
            try
            {
                byte[] damaged = saved.ToArray(); damaged[^1] ^= 1; File.WriteAllBytes(material, damaged);
                check(LocalImportBundle.Inspect(imports, id, Spec(id)).State == ImportBundleState.Failed,
                    "isolated old generation corruption is diagnosed before repair publication");
                LocalImportBundle.Publish(imports, Spec(id), original, replaceAllComponents: true);
            }
            finally { File.WriteAllBytes(material, saved); }
            var repaired = Ready(imports, id, check);
            check(repaired.Receipt!.Generation != originalGeneration && Inventory(repaired) == originalInventory
                && owner.Current && owner.Generation == originalGeneration,
                "actual repair publishes fresh immutable generation with all13 original bytes; active owner retains old capture");
            string repairedGeneration = repaired.Receipt.Generation;
            window.EndScene(); retired.Add(owner);
            check(!owner.Current && !owner.CannonCurrent, "actual EndScene retires both actor and sealed ViewModel currentness");
            check(MatchStart.Begin(window, settings, arsenalPlan), "real relaunch discovers independently verified repaired Arsenal generation: " + MatchStart.LastError);
            var next = window.Scene.Players.Main.ModSourceArsenalBody;
            check(next != null && !ReferenceEquals(next, owner) && next.Generation == repairedGeneration && !owner.Current,
                "repaired relaunch allocates fresh structural capability without resurrecting the ended actor");
            window.EndScene(); if (next != null) retired.Add(next);
            check(!window.HasScene && retired.All(o => !o.Current && !o.CannonCurrent), "Arsenal launch lifecycle ends at shell with no resurrected owners");
            Console.WriteLine(JsonSerializer.Serialize(new { Probe = "arsenal-body-real-launch-lifecycle", Id = id,
                OriginalGeneration = originalGeneration, RepairedGeneration = repairedGeneration, Failures = failures,
                PreSpawnStructuralBind = true, NativeSpawnRequiredForCurrentness = true, OrdinarySelectionRefused = true,
                ArsenalSelectionRefusedByNativeOwner = true, OldComponentBytesRetained = true, NoSceneAtEnd = !window.HasScene }));

            void Change(string file, Action mutate, string label, ImportBundleState expected)
            {
                LocalImportBundle.CheckPath(file, imports);
                byte[] bytes = File.ReadAllBytes(file);
                try { mutate(); check(LocalImportBundle.Inspect(imports, id, Spec(id)).State == expected, label + " has its exact inventory diagnosis"); RefusedWithScene(label, oldScene); }
                finally { File.WriteAllBytes(file, bytes); }
                check(Ready(imports, id, check).Receipt!.Generation == originalGeneration,
                    "restored launch refusal fixture retains original generation: " + label);
            }
            void RefusedWithScene(string label, Scene expected)
            {
                ulong frame = expected.FrameCount; float time = expected.GlobalElapsedTime;
                check(!MatchStart.Begin(window, settings, arsenalPlan) && window.HasScene
                    && ReferenceEquals(window.Scene, expected) && !expected.Exiting
                    && expected.FrameCount == frame && expected.GlobalElapsedTime == time
                    && !string.IsNullOrWhiteSpace(MatchStart.LastError),
                    "actual Begin refuses unavailable explicit import before replacing/advancing prior Scene: " + label);
                failures.Add(new { Phase = label, PriorScenePreserved = true,
                    ErrorSha256 = Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(MatchStart.LastError!))) });
            }
        }
        finally { Key(window.KeyboardState, Keys.Enter, false); if (window.HasScene) window.EndScene(); }
    }

    private static ImportBundleSpec Spec(string id) => RetroCharacterPreviewExporter.BundleSpec(id, ExternalSourceEdition.PrimeUsaRevision2);
    private static string Pointer(string root, string id) => Path.Combine(root, LocalImportBundle.StorageDirectory, id, "current.json");
    private static string HashFile(string file) { using var input = File.OpenRead(file); return Convert.ToHexStringLower(SHA256.HashData(input)); }
    private static string Inventory(ImportBundleInspection state) => Convert.ToHexStringLower(SHA256.HashData(
        JsonSerializer.SerializeToUtf8Bytes(state.Files.OrderBy(Path.GetFileName, StringComparer.Ordinal)
            .Select(f => new { File = Path.GetFileName(f), Bytes = new FileInfo(f).Length, Sha256 = HashFile(f) }))));
    private static ImportBundleInspection Ready(string root, string id, Action<bool, string> check)
    {
        var state = LocalImportBundle.Inspect(root, id, Spec(id));
        var names = PrimeRevision2RecipeChecks.FullComponents(id);
        check(state.State == ImportBundleState.Ready && state.Receipt is { Version: 6 }
            && state.Files.Count == 13 && state.Files.Select(Path.GetFileName).ToHashSet().SetEquals(names)
            && state.Files.All(f => Path.GetDirectoryName(f) == Path.GetDirectoryName(state.Files[0]))
            && Path.GetFileName(Path.GetDirectoryName(state.Files[0])) == state.Receipt.Generation,
            "unchanged existing Trooper v6 bundle has exact13 files in one verified committed generation: " + id);
        return state;
    }
    private static void Isolated(string root)
    {
        string full = Path.GetFullPath(root), temporary = Path.GetFullPath(Path.GetTempPath());
        if (!full.StartsWith(Path.TrimEndingDirectorySeparator(temporary) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Arsenal mutation fixtures require the caller's disposable temporary import root.");
        LocalImportBundle.CheckPath(full, full);
    }
    private static void Reject<T>(Action action, Action<bool, string> check, string label) where T : Exception
    {
        bool rejected = false;
        try { action(); } catch (T) { rejected = true; }
        check(rejected, label);
    }
    private sealed class InlineProgress(Action<ImportPublishStage> action) : IProgress<ImportPublishStage>
    { public void Report(ImportPublishStage stage) => action(stage); }
}
