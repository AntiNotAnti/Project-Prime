using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using MphRead.Entities;
using MphRead.Mods;
using MphRead.Mods.Input;
using MphRead.Mods.Launcher;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

internal static class PostMatchSaveTests
{
    private static readonly JsonSerializerOptions LegacyOptions = new(JsonSerializerDefaults.General)
        { WriteIndented = true, Converters = { new LegacyByteArrayConverter() } };
    internal static int Run()
    {
        Headless.Enter();
        string previousDirectory = Directory.GetCurrentDirectory();
        string root = Path.Combine(Path.GetTempPath(), "prime-postmatch-" + Guid.NewGuid().ToString("N"));
        var previousState = GameState.Current;
        var previousPlayers = PlayerEntity.LegacyRegistry;
        var previousRandom = Rng.Current;
        byte previousSlot = Menu.SaveSlot;
        SaveWhen previousRequest = Menu.NeededSave;
        int checks = 0;
        void Check(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException("POSTMATCH FAIL " + label);
            checks++; Console.WriteLine("POSTMATCH PASS " + label);
        }
        try
        {
            Directory.CreateDirectory(root); Directory.SetCurrentDirectory(root);
            var state = NewState();
            byte[] expected = LegacySave(Fixture());
            Menu.SaveSlot = 1;
            state.CommitSave();
            byte[] immediate = File.ReadAllBytes("Savedata/save001.json");
            Check(immediate.SequenceEqual(expected), "immediate CommitSave preserves baseline normalized JSON bytes");
            File.Delete("Savedata/save001.json");
            state = NewState();
            var commit = PostMatchCommitContext.Capture(state, 1, SaveWhen.Prompt, Menu.SaveRequestVersion);
            Check(commit.State == PostMatchCommitState.CommitPending && commit.HasPayload && !File.Exists("Savedata/save001.json"),
                "handoff captures the Prompt-as-yes payload without writing before teardown");
            bool rejected = false;
            try { commit.Release(); } catch (InvalidOperationException) { rejected = true; }
            Check(rejected && commit.HasPayload, "pending save cannot release its only owner");

            state.StorySave.Stats.EnemyKills = 0; state.StorySave.Artifacts = 0;
            state.StorySave.RoomState[0][0] = 0;
            string next = Path.Combine(root, "next-profile");
            Directory.CreateDirectory(next); Directory.SetCurrentDirectory(next);
            Menu.SaveSlot = 2; Menu.NeededSave = SaveWhen.Always;
            commit.Commit();
            byte[] saved = File.ReadAllBytes(Path.Combine(root, "Savedata/save001.json"));
            Check(saved.SequenceEqual(expected) && !Directory.Exists("Savedata"),
                "old save writes captured bytes and slot/path after mutations and profile/CWD switch");
            Check(commit.State == PostMatchCommitState.Committed && commit.WriteAttempts == 1,
                "successful commit reaches its terminal state with one write");
            File.WriteAllText(Path.Combine(root, "Savedata/save001.json"), "duplicate-write sentinel");
            commit.Commit();
            Check(File.ReadAllText(Path.Combine(root, "Savedata/save001.json")) == "duplicate-write sentinel"
                && commit.WriteAttempts == 1, "duplicate success cannot overwrite the file or award again");
            commit.Release(); commit.Release(); commit.Commit();
            Check(commit.State == PostMatchCommitState.Released && !commit.HasPayload,
                "release drops serialized save and terminal operations remain idempotent");

            using (var document = JsonDocument.Parse(saved))
            {
                var save = document.RootElement;
                Check(save.GetProperty("Stats").GetProperty("EnemyKills").GetUInt32() == 0xF1234567
                    && save.GetProperty("Stats").GetProperty("Deaths").GetUInt32() == 13,
                    "progression Stats omitted by StorySave.CopyTo survive the handoff");
                Check(save.GetProperty("Weapons").GetUInt16() == 0xFF
                    && save.GetProperty("WeaponSlots")[2].GetInt32() == (int)BeamType.None
                    && save.GetProperty("RoomState")[64].EnumerateArray().All(b => b.GetInt32() == 0)
                    && save.GetProperty("RoomState")[65].EnumerateArray().All(b => b.GetInt32() == 0)
                    && save.GetProperty("Logbook")[1].GetInt32() == 128,
                    "legacy weapon/room normalization and numeric byte serializer remain exact");
            }
            Console.WriteLine("POSTMATCH baseline sha256=" + Convert.ToHexString(SHA256.HashData(saved)).ToLowerInvariant());

            foreach (var (slot, request) in new[] { ((byte)0, SaveWhen.Always), ((byte)1, SaveWhen.Never) })
            {
                var noSave = PostMatchCommitContext.Capture(new SceneGameState(new ScenePlayerRegistry()), slot, request, 0);
                noSave.Commit(); noSave.Release();
                Check(noSave.WriteAttempts == 0 && !Directory.Exists("Savedata"),
                    $"no-save gate slot={slot} request={request} does not touch lazy StorySave or filesystem");
            }

            // Real filesystem failure: Savedata is a file, so the actual writer
            // cannot create its destination directory. No fake serializer/writer.
            Directory.SetCurrentDirectory(root);
            Directory.Delete("Savedata", recursive: true);
            File.WriteAllText("Savedata", "block destination");
            Menu.SaveSlot = 1; Menu.NeededSave = SaveWhen.Always;
            state = NewState();
            var failed = MatchStart.PrepareAfterMatch(state);
            try { MatchStart.AfterMatch(failed); } catch (IOException) { }
            Check(failed.State == PostMatchCommitState.Failed && failed.HasPayload && failed.WriteAttempts == 1
                && Menu.NeededSave == SaveWhen.Always, "real write failure retains original request and immutable payload");
            rejected = false;
            try { failed.Release(); } catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "failed save cannot be silently released");
            state.StorySave.Health = 1; state.StorySave.Stats.EnemyKills = 1;
            Check(ReferenceEquals(MatchStart.PrepareAfterMatch(state), failed),
                "same failed request reuses its captured owner instead of recomputing progression");
            Check(!MatchStart.Begin(null!, new MenuSettings(), default)
                && MatchStart.LastError?.StartsWith("The previous Adventure save is still pending:", StringComparison.Ordinal) == true
                && failed.State == PostMatchCommitState.Failed,
                "new launch cannot overwrite a still-failing save owner or reach world/window construction");
            File.Delete("Savedata");
            Menu.SaveSlot = 2; Menu.NeededSave = SaveWhen.Always; // distinct request, same value
            MatchStart.RetryPendingCommit();
            Check(File.ReadAllBytes("Savedata/save001.json").SequenceEqual(expected)
                && failed.State == PostMatchCommitState.Released && failed.WriteAttempts == 3,
                "retry writes original captured bytes once after failure and releases the owner");
            Check(Menu.SaveSlot == 2 && Menu.NeededSave == SaveWhen.Always,
                "old completion cannot clear the replacement owner's same-value save request");
            Check(!File.Exists("Savedata/save002.json"), "retry cannot redirect into replacement slot");
            state = NewState();
            var current = MatchStart.PrepareAfterMatch(state);
            MatchStart.AfterMatch(current);
            Check(Menu.NeededSave == SaveWhen.Never && File.ReadAllBytes("Savedata/save002.json").SequenceEqual(expected),
                "current owner clears only its completed request and saves exact bytes");
            Menu.SaveSlot = 0; Menu.NeededSave = SaveWhen.Prompt;
            MatchStart.CommitAdventureSave();
            Check(Menu.NeededSave == SaveWhen.Never, "legacy Android-facing wrapper preserves slot-zero request clearing");

            var transitioning = new Scene(new Vector2i(32, 32), SyntheticInput.CreateKeyboard(), SyntheticInput.CreateMouse(),
                _ => { }, () => { }, initializeRuntime: false);
            typeof(SceneGameState).GetProperty(nameof(SceneGameState.StorySave))!.SetValue(transitioning.GameState, Fixture());
            transitioning.GameState.TransitionState = TransitionState.Process;
            Menu.SaveSlot = 3; Menu.NeededSave = SaveWhen.Always;
            Check(MatchStart.PrepareBeforeTeardown(transitioning.GameState) == null && !File.Exists("Savedata/save003.json"),
                "unsupported active transition skips the raced pre-teardown snapshot");
            transitioning.DoCleanup(); transitioning.UnloadGl();
            transitioning.GameState.StorySave.Health = 777; // value appearing at the legacy boundary
            MatchStart.AfterMatch();
            using (var legacy = JsonDocument.Parse(File.ReadAllBytes("Savedata/save003.json")))
                Check(legacy.RootElement.GetProperty("Health").GetInt32() == 777,
                    "active-transition fallback captures after cleanup/cancellation at the original AfterMatch point");

            (PostMatchCommitContext retained, WeakReference<Scene> world) = CaptureEndedWorld();
            RestoreFacades(previousState, previousPlayers, previousRandom);
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            Check(!IsAlive(world) && retained.HasPayload,
                "pending save handoff does not retain scene/player graph after diagnostic facade release");
            MatchStart.AfterMatch(retained);

            Type shell = typeof(Scene).Assembly.GetType("MphRead.Mods.Launcher.Gui.Shell")!;
            Check(InOrder(Calls(shell, "EndMatch"), "PrepareBeforeTeardown", "EndScene", "Stop", "Stop", "AfterMatch", "ShowFrontScreen"),
                "compiled shell leave/disconnect/rematch path captures before teardown and preserves network/commit order");
            Check(InOrder(Calls(shell, "EndNetworkMatchToLobby"), "PrepareBeforeTeardown", "EndScene", "AfterMatch", "ResetMatchState", "Reset", "ResumeLobby"),
                "compiled persistent-lobby exit preserves commit before match reset/lobby return");
            Check(!Calls(shell, "RunSession").Contains("AfterMatch") && !Calls(typeof(RenderWindow), "OnUnload").Contains("CommitAdventureSave"),
                "full shell shutdown keeps existing cleanup-only save semantics");
            Console.WriteLine($"POSTMATCH summary checks={checks} scope=real serializer/filesystem and compiled exit order; no native exit campaign");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally
        {
            Directory.SetCurrentDirectory(root);
            // Cleanup an owned pending test save if an assertion interrupted its
            // retry. Production never exposes an abandon/reset for pending bytes.
            if (File.Exists("Savedata")) File.Delete("Savedata");
            MatchStart.RetryPendingCommit();
            RestoreFacades(previousState, previousPlayers, previousRandom);
            Menu.SaveSlot = previousSlot; Menu.NeededSave = previousRequest;
            Directory.SetCurrentDirectory(previousDirectory);
            Directory.Delete(root, recursive: true);
        }
    }

    private static SceneGameState NewState()
    {
        var state = new SceneGameState(new ScenePlayerRegistry());
        typeof(SceneGameState).GetProperty(nameof(SceneGameState.StorySave))!.SetValue(state, Fixture());
        return state;
    }
    // Avoid StorySave's asset-dependent logbook constructor while exercising
    // the real public save fields and production normalization/serializer.
    private static StorySave Fixture()
    {
        var save = (StorySave)RuntimeHelpers.GetUninitializedObject(typeof(StorySave));
        void Set(string name, object value) => typeof(StorySave).GetProperty(name)!.SetValue(save, value);
        byte[] Bytes(int count) => Enumerable.Range(0, count).Select(i => (byte)(i % 3 == 0 ? 0 : i % 3 == 1 ? 128 : 255)).ToArray();
        Set("RoomState", Enumerable.Range(0, 66).Select(_ => Bytes(60)).ToArray());
        Set("EnemyEncounters", Enumerable.Range(0, 8).Select(_ => Bytes(8)).ToArray());
        Set("VisitedRooms", Bytes(9)); Set("VisitedConnectors", Enumerable.Range(0, 9).Select(i => -i * 7919).ToArray());
        Set("TriggerState", Bytes(4)); Set("Logbook", Bytes(68)); Set("AreaHunters", Bytes(4));
        Set("Ammo", new[] { 2468, 12 }); Set("AmmoMax", new[] { 4000, 950 });
        Set("WeaponSlots", new[] { (int)BeamType.PowerBeam, (int)BeamType.Missile, (int)BeamType.OmegaCannon });
        Set("Stats", new StorySave.SaveStats { HunterKills = 11, Deaths = 13, EnemyHunterDeaths = 17, EnemyKills = 0xF1234567 });
        save.ScanCount = 37; save.EquipmentCount = 19; save.CheckpointEntityId = -1; save.CheckpointRoomId = 45;
        save.Health = 237; save.HealthMax = 799; save.Weapons = 0xFFFF; save.Artifacts = 0xA1B2C3D4;
        save.FoundOctoliths = 0xAB; save.CurrentOctoliths = 0x98; save.LostOctoliths = 0xF1234567;
        save.Areas = 0x137; save.BossFlags = (BossFlags)19; save.DefeatedHunters = 0xE7;
        return save;
    }
    // Frozen source oracle: SceneGameState.CommitSave from baseline
    // 16b1d89bfb6ed7776b801b3e39d513e740ee5e58, GameState.cs:1818–1845.
    // Keep independent of CaptureSaveCommit so a shared-helper regression fails.
    private static byte[] LegacySave(StorySave save)
    {
        save.Weapons &= 0xFF;
        if (save.WeaponSlots[2] == (int)BeamType.OmegaCannon) save.WeaponSlots[2] = (int)BeamType.None;
        for (int r = 91; r <= 92; r++) for (int b = 0; b < 60; b++) save.RoomState[r - 27][b] = 0;
        string path = Path.Combine(Directory.GetCurrentDirectory(), "baseline.json");
        File.WriteAllText(path, JsonSerializer.Serialize(save, LegacyOptions));
        return File.ReadAllBytes(path);
    }
    private sealed class LegacyByteArrayConverter : JsonConverter<byte[]>
    {
        public override byte[] Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
            => throw new NotSupportedException("Frozen baseline writer only.");
        public override void Write(Utf8JsonWriter writer, byte[] value, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            foreach (byte val in value) writer.WriteNumberValue(val);
            writer.WriteEndArray();
        }
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (PostMatchCommitContext, WeakReference<Scene>) CaptureEndedWorld()
    {
        var scene = new Scene(new Vector2i(32, 32), SyntheticInput.CreateKeyboard(), SyntheticInput.CreateMouse(),
            _ => { }, () => { }, initializeRuntime: false);
        typeof(SceneGameState).GetProperty(nameof(SceneGameState.StorySave))!.SetValue(scene.GameState, Fixture());
        Menu.SaveSlot = 3; Menu.NeededSave = SaveWhen.Always;
        var commit = MatchStart.PrepareAfterMatch(scene.GameState);
        scene.DoCleanup(); scene.UnloadGl();
        return (commit, new(scene));
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool IsAlive(WeakReference<Scene> world) => world.TryGetTarget(out _);
    private static void RestoreFacades(SceneGameState state, ScenePlayerRegistry players, MatchRandom random)
    { GameState.Current = state; PlayerEntity.LegacyRegistry = players; Rng.Current = random; }
    private static bool InOrder(IReadOnlyList<string> calls, params string[] required)
    {
        int at = 0;
        foreach (string call in calls) if (at < required.Length && call == required[at]) at++;
        return at == required.Length;
    }
    private static IReadOnlyList<string> Calls(Type type, string name)
    {
        var method = type.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)!;
        byte[] il = method.GetMethodBody()!.GetILAsByteArray()!;
        var opcodes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(f => (OpCode)f.GetValue(null)!).ToDictionary(o => unchecked((ushort)o.Value));
        var calls = new List<string>();
        for (int at = 0; at < il.Length;)
        {
            ushort code = il[at++]; if (code == 0xFE) code = (ushort)(0xFE00 | il[at++]);
            OpCode opcode = opcodes[code];
            if (opcode.OperandType == OperandType.InlineMethod)
                calls.Add(method.Module.ResolveMethod(BitConverter.ToInt32(il, at))!.Name);
            at += opcode.OperandType switch {
                OperandType.InlineNone => 0, OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2, OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(il, at), _ => 4 };
        }
        return calls;
    }
}
