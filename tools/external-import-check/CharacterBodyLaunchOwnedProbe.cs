using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MphRead;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Mods;
using MphRead.Mods.Import;
using MphRead.Mods.Input;
using MphRead.Mods.Launcher;
using MphRead.Mods.Network;
using MphRead.Mods.Platform;
using MphRead.Mods.Render;
using MphRead.Mods.Render.Characters;
using OpenTK.Graphics.OpenGL;
using OpenTK.Windowing.GraphicsLibraryFramework;

// Real product launch/load/retirement calls; no substitute launcher service or
// Headless.Enter. All destructive fixture mutations target isolated imports.
internal static class CharacterBodyLaunchOwnedProbe
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly Action<KeyboardState, Keys, bool> Key = typeof(KeyboardState)
        .GetMethod("SetKeyState", Hidden | BindingFlags.Public)!.CreateDelegate<Action<KeyboardState, Keys, bool>>();
    private const string Normal = "darksamus-echoes", Final = "darksamus-overcharged-echoes";

    internal static int Run(string rvz, bool trooper = false)
    {
        string primaryId = trooper ? "trooper-power" : Normal, secondaryId = trooper ? "trooper-ice" : Final;
        string primarySkin = trooper ? "MP1PowerTrooper" : "MP2DarkSamus", secondarySkin = trooper ? "MP1IceTrooper" : "MP2DarkSamusFinal";
        Hunter nativeHunter = trooper ? Hunter.Weavel : Hunter.Samus;
        LocalBodyAppearance primaryBody = trooper ? LocalBodyAppearance.TrooperPower : LocalBodyAppearance.DarkSamusEchoes,
            secondaryBody = trooper ? LocalBodyAppearance.TrooperIce : LocalBodyAppearance.DarkSamusOvercharged;
        string temporary = Path.Combine(Path.GetTempPath(), "prime-body-launch-" + Guid.NewGuid().ToString("N"));
        string cwd = Environment.CurrentDirectory, oldFiles = GameFiles.Root, oldPrefsDirectory = LauncherPrefs.Directory;
        string? oldData = Environment.GetEnvironmentVariable("PROJECT_PRIME_USER_DATA");
        var oldBody = LauncherPrefs.LocalBody; byte oldSave = Menu.SaveSlot;
        bool oldHd = RenderOptions.CharacterModelReplacements, oldBright = RenderOptions.BrightSkins,
            oldCosmetics = RenderOptions.ShowCustomCosmetics, oldForce = MapAudit.ForceEveryone;
        var oldShadow = RenderOptions.Shadows;
        bool oldFocus = GamepadContexts.Focused, oldMenu = GamepadContexts.MenuVisible,
            oldCapturing = GamepadContexts.Capturing, oldText = GamepadContexts.TextEntryActive;
        RenderWindow? window = null;
        string? native = Environment.GetEnvironmentVariable("PRIME_BODY_TEST_ASSETS"), before = null, after = null,
            nativeBefore = null, nativeAfter = null, failed = null, error = null, errorHash = null;
        int checks = 0; bool cleaned = false;
        var events = new List<object>(); var retired = new List<CharacterSourceBodyOwner>();
        string coreHash = HashFile(typeof(Scene).Assembly.Location), toolHash = HashFile(typeof(CharacterBodyLaunchOwnedProbe).Assembly.Location);
        void Check(bool value, string label)
        { if (!value) { failed = label; throw new InvalidDataException(label); } checks++; }
        try
        {
            Check(OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64
                && !Headless.Active && !NetSession.Active && !DemoPlayback.IsActive && !ReplayCapture.LiveRecordingActive,
                "fresh Windows x64 real graphics offline launch process");
            Check(!string.IsNullOrWhiteSpace(native) && Directory.Exists(native), "existing owned AMHE1 fixture is required");
            rvz = Path.GetFullPath(rvz); native = Path.GetFullPath(native!);
            before = HashFile(rvz); nativeBefore = Inventory(native);
            if (trooper)
            {
                using var disc = GameCubeDiscReader.Open(rvz);
                Check(disc.GameId == "GM8E01" && disc.Revision == 2 && disc.DiscNumber == 0
                    && ExternalSourceRequirements.VerifyKnown(disc).All(r => r.Status == ExternalArchiveStatus.Verified),
                    "Trooper launch requires the actual pinned Prime USA Rev2 source before import");
            }
            string user = Path.Combine(temporary, "user"); Directory.CreateDirectory(user);
            File.WriteAllText(Path.Combine(user, "paths.txt"), "0.19.0.0\n" + Ver.AMHE1 + "=" + native + "\nExport=" + Path.Combine(user, "Export") + "\n");
            Environment.SetEnvironmentVariable("PROJECT_PRIME_USER_DATA", user);
            Directory.SetCurrentDirectory(user); GameFiles.Root = user; LauncherPrefs.Directory = user; Menu.SaveSlot = 0;
            ConsoleSetup.Run(); Paths.UpdatePaths(); Paths.MphKey = Ver.AMHE1;
            GraphicsBackendPolicy.Configure("opengl");
            RenderOptions.CharacterModelReplacements = false; RenderOptions.BrightSkins = false;
            RenderOptions.ShowCustomCosmetics = true; RenderOptions.Shadows = ShadowQuality.Off;
            MapAudit.ForceEveryone = true; GamepadManager.ClearAll(); GamepadContexts.Focused = true;
            GamepadContexts.MenuVisible = GamepadContexts.Capturing = GamepadContexts.TextEntryActive = false;
            Check(GameFiles.Ready, "actual configured-files owner accepts isolated versioned paths");
            string imports = RetroCharacterPreviewExporter.DefaultRoot;
            Check(Path.GetFullPath(imports).StartsWith(Path.GetFullPath(temporary) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase), "actual default importer root is isolated");
            var settings = new MenuSettings { TeamPlay = "off", ThirdPersonCamera = "on", SpawnProtection = "off" };
            LauncherPrefs.LocalBody = primaryBody;
            LaunchPlan normalPlan = OfflineLaunch.Preview(settings, "MP3 PROVING GROUND", GameMode.Battle, nativeHunter, 0, 0, 0);
            LauncherPrefs.LocalBody = secondaryBody;
            LaunchPlan finalPlan = OfflineLaunch.Preview(settings, "MP3 PROVING GROUND", GameMode.Battle, nativeHunter, 0, 0, 0);
            var extraPlans = new List<(string Id, string Skin, LaunchPlan Plan)>();
            if (trooper)
            {
                foreach (var appearance in new[] { (LocalBodyAppearance.TrooperWave, "trooper-wave", "MP1WaveTrooper"),
                    (LocalBodyAppearance.TrooperPlasma, "trooper-plasma", "MP1PlasmaTrooper") })
                {
                    LauncherPrefs.LocalBody = appearance.Item1;
                    var plan = OfflineLaunch.Preview(settings, "MP3 PROVING GROUND", GameMode.Battle, nativeHunter, 0, 0, 0);
                    extraPlans.Add((appearance.Item2, appearance.Item3, plan));
                }
            }
            LauncherPrefs.LocalBody = LocalBodyAppearance.Native;
            LaunchPlan nativePlan = OfflineLaunch.Preview(settings, "MP3 PROVING GROUND", GameMode.Battle, nativeHunter, 0, 0, 0);
            Check(normalPlan.LocalBody == primaryBody
                && finalPlan.LocalBody == secondaryBody && nativePlan.LocalBody == LocalBodyAppearance.Native,
                "actual Offline preview captures detached normal, Final and native plans before later preference edits");
            if (trooper)
                Check(new[] { normalPlan, finalPlan }.Concat(extraPlans.Select(p => p.Plan)).Select(p => (int)p.LocalBody).SequenceEqual(new[] { 3, 4, 5, 6 })
                    && new[] { normalPlan, finalPlan, nativePlan }.Concat(extraPlans.Select(p => p.Plan)).All(p => p.Hunter == Hunter.Weavel
                        && p.OfflineCharacter == OfflineCharacterMode.Native && !p.IsPlaytest),
                    "all four detached Trooper plans retain native Weavel and never enable Trooper practice gameplay");
            window = new RenderWindow(shell: true); window.ClientSize = new(640, 480); window.Context.MakeCurrent();
            GLFW.SwapInterval(0); Check(!window.HasScene, "actual shell RenderWindow initially owns no match Scene");
            Refused(normalPlan, "missing-normal");
            BeginNative();
            Export(new[] { primaryId, secondaryId }.Concat(extraPlans.Select(p => p.Id)).ToArray());
            var normalInspection = Inspect(primaryId); var finalInspection = Inspect(secondaryId);
            string normalGeneration = normalInspection.Receipt!.Generation, finalGeneration = finalInspection.Receipt!.Generation;
            CharacterSourceBodyOwner first = BeginBody(normalPlan, normalGeneration, primarySkin);
            EndBody(first, "normal-end");

            string damaged = normalInspection.Files.First(path => Path.GetExtension(path) == ".glb");
            Check(Path.GetFullPath(damaged).StartsWith(Path.GetFullPath(imports) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase), "corruption fixture targets isolated derived geometry only");
            using (var file = new FileStream(damaged, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            { file.Position = file.Length - 1; int value = file.ReadByte(); file.Position--; file.WriteByte((byte)(value ^ 1)); file.Flush(true); }
            Check(LocalImportBundle.Inspect(imports, primaryId, Spec(primaryId)).State == ImportBundleState.Failed,
                "actual local inventory detects modified component integrity");
            Refused(normalPlan, "corrupt-normal");
            Export(primaryId);
            string repairedGeneration = Inspect(primaryId).Receipt!.Generation;
            Check(repairedGeneration != normalGeneration, "actual Update repairs into a fresh immutable generation");
            CharacterSourceBodyOwner repaired = BeginBody(normalPlan, repairedGeneration, primarySkin);
            Check(!ReferenceEquals(first, repaired) && !first.Current, "updated launch never resurrects the ended original owner");
            Check(LocalImportBundle.Remove(imports, primaryId), "actual Remove withdraws discovery");
            Check(repaired.Current, "Remove retains the current Scene's admitted immutable generation");
            EndBody(repaired, "removed-generation-end");
            Refused(normalPlan, "removed-normal");
            BeginNative();
            CharacterSourceBodyOwner final = BeginBody(finalPlan, finalGeneration, secondarySkin);
            EndBody(final, "final-end");
            foreach (var extra in extraPlans)
            {
                var owner = BeginBody(extra.Plan, Inspect(extra.Id).Receipt!.Generation, extra.Skin);
                EndBody(owner, extra.Id + "-end");
            }
            Export(primaryId);
            CharacterSourceBodyOwner reinstalled = BeginBody(normalPlan, Inspect(primaryId).Receipt!.Generation, primarySkin);
            Check(retired.All(owner => !owner.Current) && !ReferenceEquals(first, reinstalled),
                "reinstall and relaunch create fresh owner while every retired owner stays retired");
            EndBody(reinstalled, "reinstalled-end");
            BeginNative();
            Check(retired.All(owner => !owner.Current) && !window.HasScene && MatchStart.LastError == null,
                "same real launcher window ends cleanly with native recovery and no stale source owner");

            void Export(params string[] ids)
            {
                RetroCharacterPreviewExporter.ExportFromOwnedGame(rvz, selectedIds: ids);
                events.Add(new { Phase = "Update", Ids = ids });
            }
            ImportBundleSpec Spec(string id) => RetroCharacterPreviewExporter.BundleSpec(id,
                trooper ? ExternalSourceEdition.PrimeUsaRevision2 : ExternalSourceEdition.OriginalRecipe);
            ImportBundleInspection Inspect(string id)
            {
                var state = LocalImportBundle.Inspect(imports, id, Spec(id));
                Check(state.State == ImportBundleState.Ready && state.Receipt != null, "actual current import is complete: " + id);
                return state;
            }
            void Refused(LaunchPlan plan, string phase)
            {
                Check(!window.HasScene && !MatchStart.Begin(window, settings, plan)
                    && !window.HasScene && !string.IsNullOrWhiteSpace(MatchStart.LastError),
                    "actual Begin refuses unavailable selected body before allocating Scene: " + phase);
                Check(retired.All(owner => !owner.Current), "failed launch leaves all retired owners retired");
                events.Add(new { Phase = phase, Refused = true, SceneAllocated = window.HasScene,
                    ErrorSha256 = Sha(Encoding.UTF8.GetBytes(MatchStart.LastError!)) });
            }
            void BeginNative()
            {
                Check(MatchStart.Begin(window, settings, nativePlan), "actual native Begin recovers independently of source imports: " + MatchStart.LastError);
                Check(window.HasScene && window.Scene.Players.Main.ModSourceBody == null, "native captured plan attaches no source owner");
                if (trooper) Check(window.Scene.Players.Main.Hunter == Hunter.Weavel, "native recovery keeps the captured Weavel gameplay actor");
                window.EndScene(); Check(!window.HasScene, "native EndScene returns same window to shell");
            }
            CharacterSourceBodyOwner BeginBody(LaunchPlan plan, string generation, string appearance)
            {
                Check(MatchStart.Begin(window, settings, plan), "actual imported Begin and LoadScene succeed: " + MatchStart.LastError);
                var scene = window.Scene; var player = scene.Players.Main;
                var owner = player.ModSourceBody ?? throw new InvalidDataException("Begin did not install the selected body owner.");
                Check(owner.Current && owner.Generation == generation && owner.Model.Asset.PrivateAppearance?.SkinSource == appearance
                    && scene.Room != null && player.Health == 0 && !player.IsBot,
                    "real load barrier binds captured appearance/generation before native life spawn");
                if (trooper)
                    Check(player.Hunter == Hunter.Weavel && plan.OfflineCharacter == OfflineCharacterMode.Native && !plan.IsPlaytest
                        && owner.CannonCurrent && owner.CannonModel is { } cannon && cannon.IndexCount == 206 * 3
                        && cannon.SourceCannon is { IsGenerated: true, TriangleCount: 206 }
                        && owner.Model.Joints.Count == 35 && owner.Model.IndexCount == 1970 * 3,
                        "Trooper load barrier preserves native Weavel gameplay, complete35-joint body and its exact206-triangle generated cannon owner");
                var mode = typeof(Scene).GetField("_inputMode", Hidden)!;
                mode.SetValue(scene, Enum.Parse(mode.FieldType, "All"));
                foreach (var button in player.Controls.All) { button.Type = ButtonType.Key; button.Key = Keys.Unknown; }
                player.Controls.Shoot.Key = Keys.Enter; player.Controls.MouseAim = false; player.Controls.KeyboardAim = false;
                scene.GameState.MatchTime = -1; scene.GameState.PointGoal = 10000;
                Key(window.KeyboardState, Keys.Enter, true);
                for (int n = 0; n < 600 && !player.LoadFlags.TestFlag(LoadFlags.Spawned); n++) scene.OnSimulationFrame();
                Key(window.KeyboardState, Keys.Enter, false); scene.CameraSequences.Current?.End();
                for (int n = 0; n < 240; n++) scene.OnSimulationFrame();
                scene.CameraSequences.Current?.End(); scene.OnDrawFrame();
                var packets = (List<RenderItem>)typeof(Scene).GetField("_sourceBodyItems", Hidden)!.GetValue(scene)!;
                Check(player.Health > 0 && owner.Ready && packets.Count == owner.Model.Segments.Count && packets.Count > 0,
                    "actual launcher-created living Scene submits a complete selected body");
                Check(scene.OnRenderFrame() && GL.GetError() == OpenTK.Graphics.OpenGL.ErrorCode.NoError,
                    "launcher-created body renders through real world GL without errors");
                scene.AfterRenderFrame();
                events.Add(new { Phase = "Begin", Appearance = appearance, Generation = generation,
                    SceneGeneration = scene.LifetimeGeneration, SourcePackets = packets.Count, NativeHunter = player.Hunter.ToString(),
                    OfflineCharacter = plan.OfflineCharacter.ToString(), HasSourceCannon = owner.CannonModel != null });
                return owner;
            }
            void EndBody(CharacterSourceBodyOwner owner, string phase)
            {
                int[] lists = owner.Model.Segments.Select(segment => segment.ListId).ToArray();
                window.EndScene(); retired.Add(owner);
                Check(!window.HasScene && !owner.Current && !owner.Ready && lists.All(id => !GL.IsList(id)),
                    "actual EndScene retires source owner and every owned geometry list: " + phase);
                events.Add(new { Phase = phase, Retired = true });
            }
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("OWNED BODY LAUNCH failure: " + exception);
            error = exception.GetType().Name; errorHash = Sha(Encoding.UTF8.GetBytes(exception.Message));
        }
        finally
        {
            try
            {
                window?.EndScene(); window?.Dispose();
                Directory.SetCurrentDirectory(cwd); Environment.SetEnvironmentVariable("PROJECT_PRIME_USER_DATA", oldData);
                string target = Path.GetFullPath(temporary), parent = Path.GetFullPath(Path.GetTempPath());
                if (!target.StartsWith(parent, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(target).StartsWith("prime-body-launch-", StringComparison.Ordinal))
                    throw new InvalidDataException("Derived cleanup target escaped temporary workspace.");
                if (Directory.Exists(target)) Directory.Delete(target, true);
                cleaned = !Directory.Exists(target);
                if (before != null) after = HashFile(rvz);
                if (nativeBefore != null) nativeAfter = Inventory(native!);
                Check(cleaned && before != null && before == after && nativeBefore != null && nativeBefore == nativeAfter,
                    "all derived imports removed and both owned sources unchanged");
            }
            catch (Exception exception) { error ??= exception.GetType().Name; errorHash ??= Sha(Encoding.UTF8.GetBytes(exception.Message)); }
            GameFiles.Root = oldFiles; LauncherPrefs.Directory = oldPrefsDirectory; LauncherPrefs.LocalBody = oldBody;
            Menu.SaveSlot = oldSave; RenderOptions.CharacterModelReplacements = oldHd; RenderOptions.BrightSkins = oldBright;
            RenderOptions.ShowCustomCosmetics = oldCosmetics; RenderOptions.Shadows = oldShadow; MapAudit.ForceEveryone = oldForce;
            GamepadContexts.Focused = oldFocus; GamepadContexts.MenuVisible = oldMenu;
            GamepadContexts.Capturing = oldCapturing; GamepadContexts.TextEntryActive = oldText;
            Console.WriteLine(JsonSerializer.Serialize(new { Pass = error == null, Checks = checks, FailedCheck = failed,
                ErrorType = error, ErrorMessageSha256 = errorHash, CoreSha256 = coreHash, ToolSha256 = toolHash,
                RvzSha256Before = before, RvzSha256After = after, NativeSha256Before = nativeBefore, NativeSha256After = nativeAfter,
                TemporaryDerivedDataDeleted = cleaned, Trooper = trooper, Events = events,
                Scope = "Actual RenderWindow/OfflineLaunch.Preview/MatchStart.Begin/LoadScene/EndScene and importer Update/Remove; isolated owned-source imports, no media captures",
                Unqualified = new[] { "native menu clicks", "physical controls", "online/replay", "Android/macOS", "combat or visual art completeness" } }));
        }
        return error == null ? 0 : 1;
    }
    private static string HashFile(string path) { using var stream = File.OpenRead(path); return Convert.ToHexStringLower(SHA256.HashData(stream)); }
    private static string Sha(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static string Inventory(string root)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var pending = new Stack<string>(); pending.Push(root); int count = 0; long bytes = 0;
        while (pending.Count > 0)
        {
            string directory = pending.Pop();
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Native inventory rejects aliases.");
            foreach (string child in Directory.EnumerateDirectories(directory).Order(StringComparer.Ordinal).Reverse()) pending.Push(child);
            foreach (string file in Directory.EnumerateFiles(directory).Order(StringComparer.Ordinal))
            {
                if (++count > 65536 || (bytes += new FileInfo(file).Length) > 1024L * 1024 * 1024
                    || (File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Native inventory exceeds bounded source scope.");
                hash.AppendData(Encoding.UTF8.GetBytes(Path.GetRelativePath(root, file))); hash.AppendData(new byte[] { 0 });
                hash.AppendData(Convert.FromHexString(HashFile(file)));
            }
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
}
