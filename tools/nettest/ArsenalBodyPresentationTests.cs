using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using MphRead.Mods.Launcher;
using MphRead.Mods.Movement;
using MphRead.Mods.Network;

namespace MphRead.NetTest;

// Tests-first, pure launch policy. Reflection keeps this fixture buildable on
// the preceding core: absence of the new request fails before assets, Scene,
// renderer, transport or recorder IO. Existing LocalBody remains Native-only.
internal static class ArsenalBodyPresentationTests
{
    private static int _checks;
    private static PropertyInfo _request = null!;
    private static PropertyInfo _available = null!;
    private static MethodInfo _admit = null!;
    private static readonly LocalBodyAppearance[] Troopers =
    [LocalBodyAppearance.TrooperPower, LocalBodyAppearance.TrooperIce,
        LocalBodyAppearance.TrooperWave, LocalBodyAppearance.TrooperPlasma];

    private static void Check(bool value, string label)
    { _checks++; if (!value) throw new InvalidOperationException(label); }

    internal static int Run()
    {
        _checks = 0;
        var old = (LauncherPrefs.LocalBody, LauncherPrefs.LocalDrone, LauncherPrefs.LastHunter,
            LauncherPrefs.LastColor, LauncherPrefs.Bots, LauncherPrefs.BotLevel, LauncherPrefs.LastKind);
        try
        {
            // Intentionally the baseline's first failure. Do not normalize an
            // absent explicit request into a silently native presentation.
            _request = typeof(LaunchPlan).GetProperty("LocalArsenalBody")!;
            Check(_request is { CanRead: true, CanWrite: true }
                && _request.PropertyType == typeof(LocalBodyAppearance),
                "missing closed Arsenal contract: LaunchPlan.LocalArsenalBody : LocalBodyAppearance");
            Type? rules = typeof(LaunchPlan).Assembly.GetType("MphRead.Mods.Launcher.LocalArsenalBodyRules");
            Check(rules != null, "missing closed Arsenal contract: LocalArsenalBodyRules");
            _available = rules!.GetProperty("Available", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!;
            _admit = rules.GetMethod("TryAdmit", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                null, new[] { typeof(LaunchPlan), typeof(string).MakeByRefType() }, null)!;
            Check(_available?.PropertyType == typeof(bool) && _admit?.ReturnType == typeof(bool),
                "closed Arsenal rules expose Available and TryAdmit(LaunchPlan,out string)");
            Check(!NetSession.Active && !DemoPlayback.IsActive && !ReplayCapture.LiveRecordingActive,
                "pure fixture requires no active transport, playback or recording");

            NativeAndFinitePolicy();
            AdmissionMatrix();
            HuntersCapture();
            TransportAndRecording();
            Console.WriteLine($"ARSENAL BODY PRESENTATION PASS {_checks} pure request/admission/Hunters-capture assertions; no source IO, Scene, gameplay, native UI or GPU");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"ARSENAL BODY PRESENTATION FAIL after {_checks} checks: {error}");
            return 1;
        }
        finally
        {
            (LauncherPrefs.LocalBody, LauncherPrefs.LocalDrone, LauncherPrefs.LastHunter,
                LauncherPrefs.LastColor, LauncherPrefs.Bots, LauncherPrefs.BotLevel, LauncherPrefs.LastKind) = old;
        }
    }

    private static bool Available => (bool)_available.GetValue(null)!;
    private static LocalBodyAppearance Appearance(LaunchPlan plan) => (LocalBodyAppearance)_request.GetValue(plan)!;
    private static LaunchPlan With(LaunchPlan plan, LocalBodyAppearance appearance)
    {
        object boxed = plan;
        _request.SetValue(boxed, appearance);
        return (LaunchPlan)boxed;
    }
    private static bool Admit(LaunchPlan plan, out string reason)
    {
        object?[] args = { plan, null };
        bool result;
        try { result = (bool)_admit.Invoke(null, args)!; }
        catch (TargetInvocationException error) when (error.InnerException != null)
        { throw new InvalidOperationException("Arsenal admission threw instead of returning a refusal", error.InnerException); }
        reason = (string?)args[1] ?? "";
        return result;
    }
    private static LaunchPlan Arsenal(int bots = 0, int level = 0) => new()
    {
        Kind = LaunchKind.Offline, Hunter = Hunter.Weavel, Mode = GameMode.Battle,
        RoomKey = "synthetic-arena", IsPlaytest = true,
        OfflineCharacter = OfflineCharacterMode.TrooperArsenalPractice,
        Bots = bots, BotLevel = level,
        MatchRules = new MatchDefinition { Mode = GameMode.Battle, MovementModel = MovementModel.Native }
    };
    private static void Refuse(LaunchPlan plan, string label)
    {
        Check(!Admit(plan, out string reason) && !string.IsNullOrWhiteSpace(reason), label);
    }

    private static void NativeAndFinitePolicy()
    {
        Check(Appearance(default) == LocalBodyAppearance.Native, "default Arsenal request is Native");
        Check(Available == LocalBodyAppearanceRules.Available,
            "Arsenal source presentation shares the qualified platform boundary");
        foreach (LaunchKind kind in Enum.GetValues<LaunchKind>())
        foreach (OfflineCharacterMode mode in Enum.GetValues<OfflineCharacterMode>())
        {
            var native = Arsenal() with { Kind = kind, OfflineCharacter = mode };
            Check(Admit(native, out string reason) && reason == "",
                "Native Arsenal presentation adds no capability or new restriction: " + kind + "/" + mode);
        }
        foreach (int value in new[] { int.MinValue, -1, 1, 2, 7, 77, 255, int.MaxValue })
            Refuse(With(Arsenal(), (LocalBodyAppearance)value), "only the four finite Trooper identities admit: " + value);
        foreach (var body in Troopers)
        for (int bots = 0; bots <= 7; bots++)
        for (int level = 0; level <= 3; level++)
        {
            var candidate = With(Arsenal(bots, level), body);
            Check(OfflineCharacterGameplay.TryAdmit(candidate, out _),
                "explicit Arsenal native gameplay already admits configured native opponents");
            bool accepted = Admit(candidate, out string reason);
            Check(accepted == Available && (accepted ? reason == "" : !string.IsNullOrWhiteSpace(reason)),
                "own finite Trooper presentation admits iff qualified, preserving Bots0..7 and level0..3");
            Check(candidate.LocalBody == LocalBodyAppearance.Native && candidate.LocalDrone == LocalDroneAppearance.Native,
                "Arsenal presentation does not borrow NativeBody or solo-drone requests");
        }
    }

    private static void AdmissionMatrix()
    {
        var good = With(Arsenal(), LocalBodyAppearance.TrooperPower);
        var bad = new List<LaunchPlan>
        {
            good with { IsPlaytest = false }, good with { Bots = -1 }, good with { Bots = 8 },
            good with { BotLevel = -1 }, good with { BotLevel = 4 },
            good with { OfflineCharacter = (OfflineCharacterMode)(-1) },
            good with { OfflineCharacter = (OfflineCharacterMode)77 },
            good with { LocalBody = LocalBodyAppearance.TrooperPower },
            good with { LocalBody = LocalBodyAppearance.DarkSamusEchoes },
            good with { LocalBody = (LocalBodyAppearance)77 },
            good with { LocalDrone = LocalDroneAppearance.Alpha },
            good with { LocalDrone = LocalDroneAppearance.Gamma },
            good with { LocalDrone = (LocalDroneAppearance)77 },
            good with { MatchRules = good.MatchRules with { Mode = GameMode.Survival } },
            good with { MatchRules = good.MatchRules with { BalancedMode = true } },
            good with { MatchRules = good.MatchRules with { EnhancedHunters = true } },
            good with { MatchRules = good.MatchRules with { LowTier = true } },
            good with { MatchRules = good.MatchRules with { RiftlineM7Available = true } },
            good with { MatchRules = good.MatchRules with { InstaGib = true } },
            good with { MatchRules = good.MatchRules with { OneInTheChamber = true } },
            good with { MatchRules = good.MatchRules with { Fiesta = true } },
            good with { MatchRules = good.MatchRules with { NoImperialist = true } }
        };
        bad.AddRange(Enum.GetValues<LaunchKind>().Where(k => k != LaunchKind.Offline).Select(k => good with { Kind = k }));
        bad.AddRange(Enum.GetValues<OfflineCharacterMode>().Where(m => m != OfflineCharacterMode.TrooperArsenalPractice)
            .Select(m => good with { OfflineCharacter = m }));
        bad.AddRange(LauncherHunterChoiceController.Playable.Where(h => h != Hunter.Weavel).Select(h => good with { Hunter = h }));
        bad.AddRange(Enum.GetValues<GameMode>().Where(m => m != GameMode.Battle)
            .Select(m => good with { Mode = m, MatchRules = good.MatchRules with { Mode = m } }));
        bad.AddRange(Enum.GetValues<MovementModel>().Where(m => m != MovementModel.Native)
            .Select(m => good with { MatchRules = good.MatchRules with { MovementModel = m } }));
        foreach (var candidate in bad) Refuse(candidate, "forged or incompatible explicit Arsenal presentation refuses before source IO");

        foreach (var body in Troopers)
        {
            Check(!LocalBodyAppearanceRules.TryAdmit(Arsenal() with { LocalBody = body }, out _),
                "existing LocalBody native-gameplay fence still refuses Arsenal: " + body);
            Check(!LocalDroneAppearanceRules.TryAdmit(Arsenal() with { LocalDrone = LocalDroneAppearance.Alpha }, out _),
                "existing solo source-drone capability remains separate from Arsenal");
        }
    }

    private static void HuntersCapture()
    {
        var settings = new MenuSettings { RoomKey = "unchanged", MovementModel = "Native",
            BalancedMode = "off", LowTier = "off", RiftlineM7Available = false };
        string before = JsonSerializer.Serialize(settings);
        LauncherPrefs.LocalDrone = LocalDroneAppearance.Gamma; // Inactive saved appearance is not a capability.
        foreach (var body in Troopers)
        {
            var store = new ChoiceStore(new(Hunter.Weavel, 1));
            using var draft = new LauncherHunterChoiceController(store);
            Check(draft.SelectBody((int)body) == Available, "existing finite Hunters body choice respects platform admission");
            if (Available)
            {
                Check(draft.Current().Dirty && store.Choice.Body == LocalBodyAppearance.Native,
                    "unsaved Hunters body draft is not captured by product launch");
                Check(draft.Apply() && store.Choice.Body == body, "existing Hunters Apply commits the selected body choice");
                LauncherPrefs.LocalBody = store.Choice.Body;
            }
            else LauncherPrefs.LocalBody = body;
            var arsenal = OfflineLaunch.Preview(settings, "synthetic-arena", GameMode.Battle,
                Hunter.Weavel, 1, 7, 3, OfflineCharacterMode.TrooperArsenalPractice);
            Check(Appearance(arsenal) == (Available ? body : LocalBodyAppearance.Native)
                && arsenal.LocalBody == LocalBodyAppearance.Native && arsenal.LocalDrone == LocalDroneAppearance.Native,
                "Arsenal captures the applied Hunters BODY into its explicit separate request");
            Check(arsenal.OfflineCharacter == OfflineCharacterMode.TrooperArsenalPractice && arsenal.IsPlaytest
                && arsenal.Bots == 7 && arsenal.BotLevel == 3, "presentation capture preserves explicit private gameplay selection");
            var ordinary = OfflineLaunch.Preview(settings, "synthetic-arena", GameMode.Battle, Hunter.Weavel, 1, 0, 0);
            Check(Appearance(ordinary) == LocalBodyAppearance.Native
                && ordinary.OfflineCharacter == OfflineCharacterMode.Native && !ordinary.IsPlaytest
                && ordinary.LocalBody == (Available ? body : LocalBodyAppearance.Native),
                "body preference alone keeps ordinary native gameplay and the existing NativeBody route");
            foreach (var mode in new[] { OfflineCharacterMode.TrooperWallClingPractice,
                OfflineCharacterMode.TrooperScoutDronePractice, OfflineCharacterMode.TrooperScoutDroneCombatPractice })
                Check(Appearance(OfflineLaunch.Preview(settings, "synthetic-arena", GameMode.Battle,
                    Hunter.Weavel, 1, 0, 0, mode)) == LocalBodyAppearance.Native,
                    "other existing practice choices never infer an Arsenal body capability");
            if (Available)
            {
                Check(draft.SelectBody(0) && draft.Current().Dirty, "existing Native edit remains an unsaved draft");
                draft.Discard();
                Check(draft.Current().Choice.Body == body && !draft.Current().Dirty,
                    "Discard restores the same applied body identity instead of changing launch capture");
            }
        }
        LauncherPrefs.LocalBody = LocalBodyAppearance.Native;
        Check(Appearance(OfflineLaunch.Preview(settings, "synthetic-arena", GameMode.Battle,
            Hunter.Weavel, 0, 0, 0, OfflineCharacterMode.TrooperArsenalPractice)) == LocalBodyAppearance.Native,
            "Native body preference keeps explicit Arsenal presentation native");
        Check(JsonSerializer.Serialize(settings) == before, "all pure preview/capture operations leave caller settings unchanged");
    }

    private static void TransportAndRecording()
    {
        var good = With(Arsenal(), LocalBodyAppearance.TrooperPower);
        var role = typeof(NetSession).GetProperty(nameof(NetSession.Role))!;
        var playback = DemoPlayback.Session;
        var active = playback.GetType().GetProperty("IsActive")!;
        var pending = typeof(DemoRecorder).GetField("_pending", BindingFlags.Static | BindingFlags.NonPublic)!;
        NetRole oldRole = NetSession.Role; bool oldActive = (bool)active.GetValue(playback)!;
        object? oldPending = pending.GetValue(null);
        try
        {
            // Predicate fixtures only: no connection, replay stream or writer is opened.
            foreach (var state in new[] { NetRole.Host, NetRole.Client, NetRole.Server })
            {
                role.SetValue(null, state);
                Check(NetSession.Active, "declared transport metadata exercises actual gate");
                Refuse(good, "active network role refuses Arsenal source appearance: " + state);
            }
            role.SetValue(null, oldRole);
            active.SetValue(playback, true);
            Check(DemoPlayback.IsActive, "declared playback metadata exercises actual gate");
            Refuse(good, "active playback refuses Arsenal source appearance");
            active.SetValue(playback, oldActive);
            pending.SetValue(null, true);
            Check(ReplayCapture.LiveRecordingActive, "declared pending recorder metadata exercises actual gate without a writer");
            Refuse(good, "live recording refuses Arsenal source appearance");
        }
        finally
        {
            pending.SetValue(null, oldPending);
            active.SetValue(playback, oldActive);
            role.SetValue(null, oldRole);
        }
        Check(NetSession.Role == oldRole && DemoPlayback.IsActive == oldActive
            && Equals(pending.GetValue(null), oldPending), "all controlled global metadata restores exactly");
    }

    private sealed class ChoiceStore(LauncherHunterChoice choice) : ILauncherHunterChoiceStore
    {
        internal LauncherHunterChoice Choice = choice;
        public LauncherHunterChoice Capture() => Choice;
        public void Save(LauncherHunterChoice value) => Choice = value;
    }

    // Integration follow-up, not simulated here: actual MatchStart.Begin with a
    // requested absent/corrupt generation must refuse before replacing the old
    // Scene; successful same-generation body+cannon binding follows LoadScene.
}
