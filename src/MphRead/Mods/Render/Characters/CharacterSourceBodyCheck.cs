using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MphRead.Entities;
using MphRead.Mods.Launcher;
using MphRead.Mods.Network;
using MphRead.Mods.Render;

namespace MphRead.Mods.Render.Characters;

// CPU policy and launcher-draft checks. No Scene, GPU, source pack, protected
// media, profile file, transport or replay reader is created by this fixture.
internal static class CharacterSourceBodyCheck
{
    private static int _checks, _failures;

    internal static int Run()
    {
        _checks = _failures = 0;
        try
        {
            Check(!NetSession.Active && !DemoPlayback.IsActive && !ReplayCapture.LiveRecordingActive,
                "fixture starts without a live session, playback or recording");
            if (_failures != 0) return Finish();
            CheckIdentityAndPolicy();
            CheckRuntimeFences();
            CheckRandomLaunchPlans();
            CheckDrafts();
            CheckPosePolicy();
            CheckCannonPolicy();
            CheckRetainedBarrier();
        }
        catch (Exception exception)
        {
            _failures++;
            Console.WriteLine("[sourcebodycheck] FAIL unexpected " + exception);
        }
        return Finish();
    }

    private static int Finish()
    {
        Console.WriteLine($"[sourcebodycheck] {(_failures == 0 ? "PASS" : "FAIL")} {_checks} CPU assertions; {_failures} failures; no owned media or GPU");
        return _failures;
    }

    private static void CheckIdentityAndPolicy()
    {
        Check(LocalBodyAppearanceRules.Available ==
#if MPHREAD_SERVER || ANDROID
            false,
#else
            CharacterSourceRootWitness.QualifiedForCurrentProcess,
#endif
            "appearance availability retains the qualified physical-proof platform fence");
        Check(new LauncherHunterChoice(Hunter.Samus, 2).Body == LocalBodyAppearance.Native
            && default(LaunchPlan).LocalBody == LocalBodyAppearance.Native,
            "existing two-argument choices and default launch plans remain native");
        Check((int)LocalBodyAppearance.Native == 0 && (int)LocalBodyAppearance.DarkSamusEchoes == 1
            && (int)LocalBodyAppearance.DarkSamusOvercharged == 2 && (int)LocalBodyAppearance.TrooperPower == 3
            && (int)LocalBodyAppearance.TrooperIce == 4 && (int)LocalBodyAppearance.TrooperWave == 5
            && (int)LocalBodyAppearance.TrooperPlasma == 6, "appended Trooper choices preserve every existing serialized identity");
        foreach (LocalBodyAppearance body in Enum.GetValues<LocalBodyAppearance>())
        {
            Check(LocalBodyAppearanceRules.Canonical(body) == body, "known appearance canonicalizes: " + body);
            foreach (Hunter hunter in LauncherHunterChoiceController.Playable)
            {
                bool matches = body == LocalBodyAppearance.Native || hunter == NativeHunter(body);
                Check(LocalBodyAppearanceRules.MatchesHunter(body, hunter) == matches
                    && LocalBodyAppearanceRules.CanonicalForHunter(body, hunter) == (matches ? body : LocalBodyAppearance.Native),
                    "body/native Hunter compatibility and canonicalization agree: " + body + "/" + hunter);
            }
        }
        foreach (int invalid in new[] { -1, 7, int.MinValue, int.MaxValue })
        {
            var body = (LocalBodyAppearance)invalid;
            Check(LocalBodyAppearanceRules.Canonical(body) == LocalBodyAppearance.Native
                && LocalBodyAppearanceRules.BundleId(body) == null,
                "unknown appearance has no imported bundle authority: " + invalid);
            foreach (Hunter hunter in LauncherHunterChoiceController.Playable)
                Check(!LocalBodyAppearanceRules.MatchesHunter(body, hunter)
                    && LocalBodyAppearanceRules.CanonicalForHunter(body, hunter) == LocalBodyAppearance.Native,
                    "invalid body cannot acquire a native Hunter match: " + invalid + "/" + hunter);
            Refused(Plan(body), "unknown launch identity is refused: " + invalid);
        }
        Check(LocalBodyAppearanceRules.BundleId(LocalBodyAppearance.Native) == null
            && LocalBodyAppearanceRules.BundleId(LocalBodyAppearance.DarkSamusEchoes) == "darksamus-echoes"
            && LocalBodyAppearanceRules.BundleId(LocalBodyAppearance.DarkSamusOvercharged) == "darksamus-overcharged-echoes"
            && LocalBodyAppearanceRules.BundleId(LocalBodyAppearance.TrooperPower) == "trooper-power"
            && LocalBodyAppearanceRules.BundleId(LocalBodyAppearance.TrooperIce) == "trooper-ice"
            && LocalBodyAppearanceRules.BundleId(LocalBodyAppearance.TrooperWave) == "trooper-wave"
            && LocalBodyAppearanceRules.BundleId(LocalBodyAppearance.TrooperPlasma) == "trooper-plasma",
            "all six imported appearances bind distinct canonical source bundles");

        foreach (LaunchKind kind in Enum.GetValues<LaunchKind>())
        foreach (Hunter hunter in LauncherHunterChoiceController.Playable)
            Check(LocalBodyAppearanceRules.TryAdmit(Plan(LocalBodyAppearance.Native) with
                { Kind = kind, Hunter = hunter, Mode = GameMode.SinglePlayer,
                    OfflineCharacter = OfflineCharacterMode.TrooperArsenalPractice,
                    MatchRules = new() { RiftlineM7Available = true } }, out string reason)
                && reason.Length == 0, "native identity preserves launch policy: " + kind + "/" + hunter);

        foreach (LocalBodyAppearance body in ImportedBodies)
        {
            LaunchPlan baseline = Plan(body);
            bool admitted = LocalBodyAppearanceRules.TryAdmit(baseline, out string reason);
            Check(admitted == LocalBodyAppearanceRules.Available
                && (admitted ? reason.Length == 0 : reason == CharacterSourceRootWitness.UnsupportedPlatformMessage),
                "eligible offline body obeys platform admission: " + body);
            foreach (LaunchKind kind in Enum.GetValues<LaunchKind>())
                if (kind != LaunchKind.Offline)
                    Refused(baseline with { Kind = kind }, "imported body cannot enter " + kind);
            foreach (Hunter hunter in LauncherHunterChoiceController.Playable)
                if (hunter != NativeHunter(body))
                    Refused(baseline with { Hunter = hunter }, "imported body cannot replace " + hunter);
            Refused(baseline with { Mode = GameMode.SinglePlayer }, "campaign mode is refused");
            foreach (OfflineCharacterMode mode in Enum.GetValues<OfflineCharacterMode>())
                if (mode != OfflineCharacterMode.Native)
                    Refused(baseline with { OfflineCharacter = mode }, "practice capability is refused: " + mode);
            Refused(baseline with { MatchRules = new() { RiftlineM7Available = true } },
                "custom rifle availability is refused even before weapon selection");
        }
    }

    private static readonly LocalBodyAppearance[] ImportedBodies =
        { LocalBodyAppearance.DarkSamusEchoes, LocalBodyAppearance.DarkSamusOvercharged,
            LocalBodyAppearance.TrooperPower, LocalBodyAppearance.TrooperIce, LocalBodyAppearance.TrooperWave, LocalBodyAppearance.TrooperPlasma };

    private static Hunter NativeHunter(LocalBodyAppearance body) => body is >= LocalBodyAppearance.TrooperPower and <= LocalBodyAppearance.TrooperPlasma
        ? Hunter.Weavel : Hunter.Samus;

    private static LaunchPlan Plan(LocalBodyAppearance body) => new()
    {
        Kind = LaunchKind.Offline, Hunter = NativeHunter(body), Mode = GameMode.Battle,
        OfflineCharacter = OfflineCharacterMode.Native, LocalBody = body,
        MatchRules = new() { Mode = GameMode.Battle }
    };

    private static void Refused(LaunchPlan plan, string label)
    {
        bool accepted = LocalBodyAppearanceRules.TryAdmit(plan, out string reason);
        // Check the policy reason as well as refusal: Windows platform rejection
        // must not mask a missing online/replay/weapon or character guard.
        Check(!accepted && !string.IsNullOrWhiteSpace(reason)
            && reason != CharacterSourceRootWitness.UnsupportedPlatformMessage, label);
    }

    private static void CheckRuntimeFences()
    {
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
        PropertyInfo role = typeof(NetSession).GetProperty(nameof(NetSession.Role))!;
        PropertyInfo playing = typeof(ReplayPlaybackSession).GetProperty(nameof(ReplayPlaybackSession.IsActive))!;
        FieldInfo pending = typeof(DemoRecorder).GetField("_pending", flags)!;
        var session = DemoPlayback.Session;
        object? oldRole = role.GetValue(null), oldPlaying = playing.GetValue(session), oldPending = pending.GetValue(null);
        try
        {
            role.SetValue(null, NetRole.Client);
            RuntimeRefused("active client");
            role.SetValue(null, oldRole);
            playing.SetValue(session, true);
            RuntimeRefused("active replay playback");
            playing.SetValue(session, oldPlaying);
            pending.SetValue(null, true);
            Check(ReplayCapture.LiveRecordingActive, "pending recording is already a live-recording fence");
            RuntimeRefused("pending live recording");
        }
        finally
        {
            pending.SetValue(null, oldPending);
            playing.SetValue(session, oldPlaying);
            role.SetValue(null, oldRole);
        }
        Check(!NetSession.Active && !DemoPlayback.IsActive && !ReplayCapture.LiveRecordingActive,
            "runtime fence fixture restores every singleton flag");
    }

    private static void RuntimeRefused(string label)
    {
        foreach (LocalBodyAppearance body in ImportedBodies)
            Refused(Plan(body), label + " rejects " + body);
        Check(LocalBodyAppearanceRules.TryAdmit(Plan(LocalBodyAppearance.Native), out string reason)
            && reason.Length == 0, label + " preserves native admission");
    }

    private static void CheckRandomLaunchPlans()
    {
        var savedBody = LauncherPrefs.LocalBody;
        uint rng1 = Rng.Rng1, rng2 = Rng.Rng2;
        try
        {
            foreach (LocalBodyAppearance body in ImportedBodies)
            {
                LauncherPrefs.LocalBody = body;
                for (int draw = 0; draw < 256; draw++)
                {
                    bool lowTier = draw % 2 == 0;
                    var settings = new MenuSettings { LowTier = lowTier ? "on" : "off", MovementModel = "Native" };
                    var plan = OfflineLaunch.Preview(settings, "MP3 PROVING GROUND", GameMode.Battle, Hunter.Random, 0, 0, 0);
                    LocalBodyAppearance expected = LocalBodyAppearanceRules.Available && plan.Hunter == NativeHunter(body)
                        ? body : LocalBodyAppearance.Native;
                    Check(plan.LocalBody == expected && LocalBodyAppearanceRules.MatchesHunter(plan.LocalBody, plan.Hunter)
                        && Multiplayer.HunterRules.Allowed(plan.Hunter, lowTier)
                        && plan.OfflineCharacter == OfflineCharacterMode.Native && !plan.IsPlaytest,
                        "Random Preview binds appearance to the same resolved native Hunter: " + body + "/" + draw);
                }
            }
            Check(Rng.Rng1 == rng1 && Rng.Rng2 == rng2, "Random menu-plan resolution does not consume native gameplay RNG");
        }
        finally { LauncherPrefs.LocalBody = savedBody; }
    }

    private static void CheckDrafts()
    {
        foreach (LocalBodyAppearance body in ImportedBodies)
        {
            Hunter native = NativeHunter(body);
            var store = new MemoryStore(new(native, 1));
            using var draft = new LauncherHunterChoiceController(store);
            var untouched = draft.Current();
            Check(!draft.SelectBody(-1) && !draft.SelectBody(7) && !draft.SelectBody(int.MaxValue)
                && draft.Current() == untouched && store.Writes == 0,
                "invalid body choices cannot mutate draft, status, revision or profile");
            Check(!draft.SelectBody((int)(native == Hunter.Samus ? LocalBodyAppearance.TrooperPower : LocalBodyAppearance.DarkSamusEchoes))
                && draft.Current() == untouched && store.Writes == 0, "a known but mismatched body cannot alter this native Hunter draft");
            bool selected = draft.SelectBody((int)body);
            Check(selected == LocalBodyAppearanceRules.Available && store.Writes == 0
                && (selected ? draft.Current().Choice.Body == body && draft.Current().Dirty
                    : draft.Current() == untouched), "body selection is platform-gated and draft-only: " + body);
            if (selected)
            {
                var beforePreflight = draft.Current();
                Check(draft.CanApply(out _) && draft.Current() == beforePreflight && store.Writes == 0,
                    "body preflight does not mutate draft or persisted profile");
                Check(draft.Apply() && store.Writes == 1 && store.Value.Body == body && !draft.Current().Dirty,
                    "Apply persists the exact imported appearance once: " + body);
                Check(draft.Apply() && store.Writes == 1, "repeated Apply does not write again");
            }
            Check(draft.SelectBody(0) && draft.Current().Choice.Body == LocalBodyAppearance.Native,
                "native selection is always available");
            draft.Discard();
            Check(draft.Current().Choice == store.Value && !draft.Current().Dirty,
                "discard restores the complete saved identity");
            using var reopened = new LauncherHunterChoiceController(store);
            Check(reopened.Current().Choice == store.Value && !reopened.Current().Dirty,
                "reopening captures the exact saved native Hunter/body pair: " + body);
        }

        // Seed persisted bodies without bypassing SelectBody's platform fence.
        // This exercises cancellation, persistence and conflict detection even
        // on platforms that cannot activate an imported model.
        var persisted = new MemoryStore(new(Hunter.Samus, 2, LocalBodyAppearance.DarkSamusEchoes));
        using (var draft = new LauncherHunterChoiceController(persisted))
        {
            Check(draft.Current().Choice == persisted.Value && !draft.Current().Dirty,
                "reopening preserves a saved body identity without loading source media");
            draft.SelectBody(0);
            draft.Discard();
            Check(draft.Current().Choice.Body == LocalBodyAppearance.DarkSamusEchoes && persisted.Writes == 0,
                "cancel does not overwrite an imported appearance");
            draft.SelectSuit(3);
            Check(draft.Apply() && persisted.Value == new LauncherHunterChoice(Hunter.Samus, 3, LocalBodyAppearance.DarkSamusEchoes),
                "suit persistence retains the saved body identity");
            draft.SelectHunter(Array.IndexOf(LauncherHunterChoiceController.Playable, Hunter.Weavel));
            Check(draft.Current().Choice.Body == LocalBodyAppearance.Native && !draft.SelectBody(1) && !draft.SelectBody(2),
                "changing to Weavel clears the Echoes body and refuses both Samus appearances");
            draft.SelectHunter(Array.IndexOf(LauncherHunterChoiceController.Playable, Hunter.Samus));
            Check(draft.Current().Choice.Body == LocalBodyAppearance.Native,
                "returning to Samus does not resurrect another hunter's body draft");
        }

        foreach (var body in ImportedBodies.Where(b => NativeHunter(b) == Hunter.Weavel))
        {
            var saved = new MemoryStore(new(Hunter.Weavel, 2, body));
            using var draft = new LauncherHunterChoiceController(saved);
            Check(draft.Current().Choice == saved.Value && !draft.Current().Dirty, "reopening retains saved Trooper identity: " + body);
            draft.SelectBody(0); draft.Discard();
            Check(draft.Current().Choice.Body == body && saved.Writes == 0, "Discard restores the saved Trooper without writing: " + body);
            draft.SelectSuit(3);
            Check(draft.Apply() && saved.Value == new LauncherHunterChoice(Hunter.Weavel, 3, body), "suit-only Apply retains exact Trooper identity: " + body);
            draft.SelectHunter(Array.IndexOf(LauncherHunterChoiceController.Playable, Hunter.Samus));
            var native = draft.Current();
            Check(native.Choice.Body == LocalBodyAppearance.Native && !draft.SelectBody((int)body) && draft.Current() == native,
                "switching Trooper draft to Samus clears it and refuses cross-Hunter reselection: " + body);
            draft.SelectHunter(Array.IndexOf(LauncherHunterChoiceController.Playable, Hunter.Weavel));
            Check(draft.Current().Choice.Body == LocalBodyAppearance.Native, "returning to Weavel does not resurrect a cleared Trooper draft: " + body);
        }

        var concurrent = new MemoryStore(new(Hunter.Samus, 0, LocalBodyAppearance.DarkSamusEchoes));
        using (var draft = new LauncherHunterChoiceController(concurrent))
        {
            draft.SelectSuit(1);
            concurrent.Value = concurrent.Value with { Body = LocalBodyAppearance.DarkSamusOvercharged };
            var before = draft.Current();
            Check(!draft.CanApply(out string reason) && reason.Contains("CHANGED ELSEWHERE", StringComparison.Ordinal)
                && draft.Current() == before, "body-only concurrent save blocks pure preflight");
            Check(!draft.Apply() && concurrent.Writes == 0 && draft.Current().Dirty
                && concurrent.Value.Body == LocalBodyAppearance.DarkSamusOvercharged,
                "stale body draft cannot overwrite another profile edit");
            draft.Discard();
            Check(draft.Current().Choice == concurrent.Value && !draft.Current().Dirty,
                "discard adopts the current concurrent body identity");
        }

        var failing = new MemoryStore(new(Hunter.Samus, 0, LocalBodyAppearance.DarkSamusOvercharged)) { FailSave = true };
        using (var draft = new LauncherHunterChoiceController(failing))
        {
            draft.SelectBody(0);
            Check(!draft.Apply() && draft.Current().Dirty && failing.Writes == 0
                && failing.Value.Body == LocalBodyAppearance.DarkSamusOvercharged,
                "failed profile write retains exact saved appearance and retryable draft");
            failing.FailSave = false;
            Check(draft.Apply() && failing.Writes == 1 && failing.Value.Body == LocalBodyAppearance.Native
                && !draft.Current().Dirty, "retry commits retained native-body draft exactly once");
        }
    }

    private sealed class MemoryStore(LauncherHunterChoice initial) : ILauncherHunterChoiceStore
    {
        internal LauncherHunterChoice Value = initial;
        internal int Writes;
        internal bool FailSave;
        public LauncherHunterChoice Capture() => Value;
        public void Save(LauncherHunterChoice choice)
        {
            if (FailSave) throw new InvalidOperationException("invented profile write failure");
            Value = choice;
            Writes++;
        }
    }

    private static void CheckPosePolicy()
    {
        var locomotion = new Dictionary<PlayerAnimation, string>
        {
            [PlayerAnimation.Idle] = "B_ready_dark_samus",
            [PlayerAnimation.Turn] = "B_ready_dark_samus",
            [PlayerAnimation.LandNeutral] = "B_ready_dark_samus",
            [PlayerAnimation.LandLeft] = "B_ready_dark_samus",
            [PlayerAnimation.LandRight] = "B_ready_dark_samus",
            [PlayerAnimation.WalkForward] = "B_glideForward_dark_samus",
            [PlayerAnimation.WalkBackward] = "B_glideBackwards_dark_samus",
            [PlayerAnimation.WalkLeft] = "B_glideLeft_dark_samus",
            [PlayerAnimation.WalkRight] = "B_glideRight_dark_samus"
        };
        var firing = new Dictionary<PlayerAnimation, string>
        {
            [PlayerAnimation.Shoot] = "A_MissileBarrage_dark_samus",
            [PlayerAnimation.Charge] = "A_BoostBall_ChargeReady_dark_samus",
            [PlayerAnimation.ChargeShoot] = "A_BoostBall_ChargeShot_dark_samus"
        };
        Check(CharacterSourceBodyPose.RequiredClips.Length == 8
            && CharacterSourceBodyPose.RequiredClips.Distinct(StringComparer.Ordinal).Count() == 8
            && CharacterSourceBodyPose.RequiredClips.ToHashSet(StringComparer.Ordinal)
                .SetEquals(locomotion.Values.Concat(firing.Values)),
            "load-barrier inventory contains every required locomotion and firing clip exactly once");
        foreach (PlayerAnimation state in Enum.GetValues<PlayerAnimation>())
        {
            locomotion.TryGetValue(state, out string? lower);
            firing.TryGetValue(state, out string? upper);
            Check(CharacterSourceBodyPose.LowerClip(state) == lower,
                "lower adapter selects only declared source locomotion: " + state);
            Check(CharacterSourceBodyPose.UpperClip(state) == upper,
                "upper adapter selects only declared source firing: " + state);
            foreach (PlayerAnimation torso in Enum.GetValues<PlayerAnimation>())
                Check(CharacterSourceBodyPose.Supports(state, torso)
                    == (locomotion.ContainsKey(state) && (locomotion.ContainsKey(torso) || firing.ContainsKey(torso))),
                    "split-body admission preserves unsupported native fallback: " + state + "/" + torso);
        }
        foreach (PlayerAnimation invalid in new[] { (PlayerAnimation)(-128), (PlayerAnimation)127 })
            Check(CharacterSourceBodyPose.LowerClip(invalid) == null
                && CharacterSourceBodyPose.UpperClip(invalid) == null
                && !CharacterSourceBodyPose.Supports(invalid, PlayerAnimation.Idle)
                && !CharacterSourceBodyPose.Supports(PlayerAnimation.Idle, invalid),
                "unknown animation state never inherits imported pose authority");
    }

    private static void CheckCannonPolicy()
    {
        foreach (GunAnimation animation in Enum.GetValues<GunAnimation>())
            Check(PlayerEntity.SourceCannonAnimationSupported(animation)
                == (animation is GunAnimation.Idle or GunAnimation.Shot or GunAnimation.Charging
                    or GunAnimation.FullCharge or GunAnimation.ChargeShot),
                "fixed normal source cannon preserves native switch/raise/missile animations: " + animation);
        Check(!PlayerEntity.SourceCannonAnimationSupported((GunAnimation)255), "unknown gun animation selects native presentation");
    }

    private static void CheckRetainedBarrier()
    {
        static RenderItem Mesh(int id) => new()
        {
            Type = RenderItemType.Mesh, ListId = id, Alpha = 1,
            RetainedRoomOwned = true, PolygonId = 0,
            PolygonMode = PolygonMode.Modulate, RenderMode = RenderMode.Normal
        };
        RenderItem first = Mesh(901), source = Mesh(902), last = Mesh(903);
        var world = new RetainedRenderWorld();
        world.Capture(new[] { first, source, last }, Array.Empty<RenderItem>(), Array.Empty<RenderItem>(),
            RetainedOpaqueOrder.Submission);
        Check(world.OpaqueBatches.Count == 1 && world.OpaqueBatches[0].Count == 3,
            "control packets otherwise qualify for one shared-material run");
        // An inert marker is enough for the pure presence-based exclusion. Its
        // absent Scene/program are never used by this CPU graph fixture.
        source.SourceGx = new(new(null!, null!, 0, Array.Empty<int>()), 0);
        source.WeightedSkinning = true;
        world.Capture(new[] { first, source, last }, Array.Empty<RenderItem>(), Array.Empty<RenderItem>(),
            RetainedOpaqueOrder.Submission);
        Check(!world.Opaque[1].Batchable && !world.Opaque[1].ReorderableOpaque,
            "source GX packets are neither batchable nor reorderable native packets");
        Check(world.OpaqueBatches.Count == 3 && world.OpaqueBatches.All(batch => batch.Count == 1)
            && ReferenceEquals(world.Opaque[0].Item, first) && ReferenceEquals(world.Opaque[1].Item, source)
            && ReferenceEquals(world.Opaque[2].Item, last),
            "source GX breaks material reuse on both sides without changing submission order");
        Check(!RetainedOpaqueBatchPolicy.CanMerge(world.Opaque[0], world.Opaque[1])
            && !RetainedOpaqueBatchPolicy.CanMerge(world.Opaque[1], world.Opaque[2]),
            "source palette cannot enter retained opaque or depth-replay batching");
    }

    private static void Check(bool condition, string label)
    {
        _checks++;
        if (condition) return;
        _failures++;
        Console.WriteLine("[sourcebodycheck] FAIL " + label);
    }
}
