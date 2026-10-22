using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using MphRead.Entities;
using MphRead.Mods;
using MphRead.Mods.Input;
using MphRead.Mods.Network;
using MphRead.Mods.Render;
using MphRead.Mods.Render.Hud;
using MphRead.Mods.Replay;
using OpenTK.Mathematics;
using NVector3 = System.Numerics.Vector3;

namespace MphRead.NetTest;

// Two fresh-process modes. No loaded room, OnLoad, window, audio device or
// network. The headless mode never queries HudProfiles fields or Runtime.
internal static class HeadlessRadarHistoryTests
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
    private static int _checks, _failures;
    private static void Check(bool value, string label)
    {
        _checks++; if (!value) _failures++;
        Console.WriteLine($"[radar-history] {(value ? "PASS" : "FAIL")} {label}");
    }
    private static readonly FieldInfo Contacts = typeof(PlayerEntity).GetField("_radarContacts", Instance)!;
    private static readonly FieldInfo History = typeof(PlayerEntity).GetField("_radarHistory", Instance)!;
    private static readonly FieldInfo Epoch = typeof(PlayerEntity).GetField("_radarEpoch", Instance)!;
    private static readonly FieldInfo Match = typeof(PlayerEntity).GetField("_radarMatch", Instance)!;
    private static readonly FieldInfo Frame = typeof(Scene).GetField("_frameCount", Instance)!;
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ConfigureRendered()
    {
        var profile = HudProfileDefaults.Create("Project Prime");
        profile.Radar.Hunters = true; profile.Radar.TrailSamples = 4; profile.ReduceMotion = false;
        profile.Elements["core.radar"].Enabled = true;
        HudProfiles.Publish(profile);
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static long Sample(PlayerEntity actor)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        actor.ModCaptureRadarHistory();
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static long CollectorSample(PlayerEntity actor, HudRadarRuntime style)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) _ = actor.CollectRadarContacts(style).Length;
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }
    internal static int Run(bool headless)
    {
        var oldState = GameState.Current; var oldPlayers = PlayerEntity.LegacyRegistry; var oldRandom = Rng.Current;
        bool radar = Radar.Enabled;
        try
        {
            Check(!Headless.Active, "fresh process begins outside Headless");
            if (headless) Headless.Enter(); else ConfigureRendered();
            var scene = new Scene(new Vector2i(32, 32), SyntheticInput.CreateKeyboard(), SyntheticInput.CreateMouse(),
                _ => { }, () => { }, initializeRuntime: false);
            var actor = new PlayerEntity(0, scene) { Health = 0, LoadFlags = LoadFlags.Active | LoadFlags.Spawned };
            Check(((RadarContact[])Contacts.GetValue(actor)!).Length == 0,
                "actual player construction leaves unused HUD contact buffer empty");
            Check(scene.Players.Items.All(player => ((RadarContact[])Contacts.GetValue(player)!).Length == 0),
                "actual metadata Scene registry constructs all actor contact buffers empty");
            Frame.SetValue(scene, 22UL);
            // Warm this actual invocation while the old eligible expression
            // short-circuits before HudProfiles. No fragile cctor-state probe.
            Radar.Enabled = false;
            for (int i = 0; i < 100; i++) Sample(actor);
            actor.Health = 100; Radar.Enabled = true; actor.Position = new Vector3(3, 2, 3);
            ulong identity = ((ulong)NetPlayerLifecycle.Generation(0) << 16) | NetPlayerLifecycle.Get(0);
            var seed = new RadarTrailState();
            seed.Sample(new NVector3(1, 2, 3), 20, identity, true);
            seed.Sample(new NVector3(2, 2, 3), 21, identity, true);
            ulong epoch = headless ? 0xc0ffeeUL : NetSession.AuthorityEpoch;
            ushort match = headless ? (ushort)1234 : NetSession.CurrentMatchId;
            History.SetValue(actor, seed); Epoch.SetValue(actor, epoch); Match.SetValue(actor, match);
            long coldEligible = Sample(actor);
            Console.WriteLine($"[radar-history] first eligible actual call allocated {coldEligible} bytes; mode={(headless ? "headless" : "rendered")}");
            var actual = (RadarTrailState)History.GetValue(actor)!;
            if (headless)
            {
                Check(actual.Equals(seed) && (ulong)Epoch.GetValue(actor)! == epoch && (ushort)Match.GetValue(actor)! == match,
                    "headless actual radar capture preserves every seeded history/epoch/match value");
                Check(coldEligible == 0, "headless first eligible call allocates zero bytes without a test-side HudProfiles access");
                for (int i = 0; i < 100; i++) actor.ModCaptureRadarHistory();
                Check(((RadarTrailState)History.GetValue(actor)!).Equals(seed) && (ulong)Epoch.GetValue(actor)! == epoch
                    && (ushort)Match.GetValue(actor)! == match, "repeated headless radar capture remains a no-op");
            }
            else
            {
                Check(actual.Count == 2 && actual.Get(0) == new NVector3(2, 2, 3) && actual.Get(1) == new NVector3(1, 2, 3),
                    "ordinary rendered branch still advances actual radar trail history");
                Check((ulong)Epoch.GetValue(actor)! == epoch && (ushort)Match.GetValue(actor)! == match,
                    "ordinary rendered radar history retains its exact stream identity");
            }
            const string contract = "A4512223EC0CC49516DB00DD3733168DA6969DEA059E9DDE84B31062ADEA675A";
            Check(ReplayWorldCheckpoint.ComputeContract(stable: true) == contract && ReplayCheckpointAccessors.Contract == contract,
                "historical world schema and generated accessor fingerprint remain exact");
            var fields = (Dictionary<Type, FieldInfo[]>)typeof(ReplayWorldCheckpoint).GetField("Fields", Static)!.GetValue(null)!;
            var types = (Dictionary<string, Type>)typeof(ReplayWorldCheckpoint).GetField("Types", Static)!.GetValue(null)!;
            Check(!types.ContainsValue(typeof(RadarTrailState)) && !types.ContainsValue(typeof(RadarContact[]))
                && fields.Values.SelectMany(f => f)
                .All(f => f.Name is not ("_radarHistory" or "_radarEpoch" or "_radarMatch" or "_radarContacts")),
                "radar presentation history is absent from archive field/type closure");
            var references = new Dictionary<object, int>(ReferenceEqualityComparer.Instance);
            int Reference(object? value)
            {
                if (value == null) return -1;
                if (!references.TryGetValue(value, out int id)) references.Add(value, id = references.Count);
                return id;
            }
            byte[] Block(bool bound)
            {
                using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
                if (bound)
                {
                    if (!ReplayCheckpointAccessors.Write(writer, actor, Reference)) throw new InvalidOperationException("Missing actual player accessor writer.");
                }
                else foreach (var field in fields[typeof(PlayerEntity)])
                    ReplayWorldCheckpoint.WriteValue(writer, field.FieldType, field.GetValue(actor), Reference);
                return stream.ToArray();
            }
            byte[] before = Block(true);
            Check(before.SequenceEqual(Block(false)), "production bound/reflection player archive bytes agree");
            actor.ModCaptureRadarHistory();
            Check(before.SequenceEqual(Block(true)) && before.SequenceEqual(Block(false)),
                "actual radar capture preserves complete production player field-block bytes");
            // Register one genuinely constructed, living metadata Hunter.
            // Collection itself needs no room assets, OnLoad or rendering.
            foreach (var player in scene.Players.Items)
            { player.Health = 0; player.LoadFlags = LoadFlags.None; }
            var contactPlayer = scene.Players.Items[1];
            contactPlayer.Health = 100; contactPlayer.LoadFlags = LoadFlags.Active | LoadFlags.Spawned;
            contactPlayer.Position = new Vector3(9, 2, 3);
            var style = new HudRadarRuntime(new HudRadarProfile
            { Hunters = true, Weapons = false, Powerups = false, Objectives = false });
            byte[] beforeLazy = Block(true);
            var initiallyEmpty = (RadarContact[])Contacts.GetValue(actor)!;
            var contacts = actor.CollectRadarContacts(style);
            var buffer = (RadarContact[])Contacts.GetValue(actor)!;
            Check(buffer.Length == 4096 && !ReferenceEquals(initiallyEmpty, buffer),
                "first actual contact collection creates exactly the 4096-entry HUD buffer");
            Check(contacts.Length == 1 && contacts[0].Kind == RadarContactKind.Hunter
                && contacts[0].StableId == contactPlayer.SlotIndex && contacts[0].Position == new NVector3(9, 2, 3),
                "actual collector reports the registered metadata Hunter with exact identity and position");
            actor.CollectRadarContacts(style);
            Check(ReferenceEquals(buffer, Contacts.GetValue(actor)), "repeated actual collection reuses the same HUD buffer");
            Check(beforeLazy.SequenceEqual(Block(true)) && beforeLazy.SequenceEqual(Block(false)),
                "lazy buffer creation and actual positive collection preserve complete bound/reflection archive bytes");
            long initialCollector = CollectorSample(actor, style), subsequentCollector = CollectorSample(actor, style);
            for (int i = 0; i < 64; i++) CollectorSample(actor, style);
            Console.WriteLine($"[radar-history] fixed collector helper warmup: initial={initialCollector}, subsequent={subsequentCollector}; 64 additional warmups");
            for (int i = 0; i < 5; i++)
            {
                long allocated = CollectorSample(actor, style);
                Check(allocated == 0, $"warmed actual positive collector sample {i + 1}/5 allocates zero bytes for 1000 calls (actual {allocated})");
            }
            // The caller must still execute canonical transform capture after
            // the presentation hook returns, even in the headless mode.
            actor.ModResetDrawState(); actor.Position = new Vector3(4, 2, 3); actor.ModCaptureDrawState();
            var current = typeof(EntityBase).GetField("_drawCurrent", Instance)!;
            Check((Matrix4)current.GetValue(actor)! == actor.Transform,
                "actual ModCaptureDrawState still updates the archival transform after radar capture");
            Console.WriteLine($"[radar-history] schema={contract}; playerBlockSha256={Convert.ToHexString(SHA256.HashData(before)).ToLowerInvariant()}");
            Console.WriteLine($"[radar-history] {_checks} checks; failures={_failures}; mode={(headless ? "headless" : "rendered")}. Content-free actual player/metadata Scene only; no world or frame-time acceptance.");
            return _failures == 0 ? 0 : 1;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally
        {
            Radar.Enabled = radar; GameState.Current = oldState; PlayerEntity.LegacyRegistry = oldPlayers; Rng.Current = oldRandom;
            // Headless is process-monotonic; these metadata-only owners have
            // no native resources and retire with this fresh fixture process.
        }
    }
}
