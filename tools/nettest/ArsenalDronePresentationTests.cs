using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using MphRead.Mods.Launcher;
using MphRead.Mods.Movement;
using MphRead.Mods.Network;

namespace MphRead.NetTest;

// Tests-first policy/capture only. The preceding core deliberately fails the
// missing explicit request before source, Scene, transport or renderer IO.
internal static class ArsenalDronePresentationTests
{
    private static int _checks;
    private static PropertyInfo _request = null!, _available = null!;
    private static MethodInfo _admit = null!;
    private static readonly LocalBodyAppearance[] Bodies = [LocalBodyAppearance.Native,
        LocalBodyAppearance.TrooperPower, LocalBodyAppearance.TrooperIce,
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
            _request = typeof(LaunchPlan).GetProperty("LocalArsenalDrone")!;
            Check(_request is { CanRead: true, CanWrite: true } && _request.PropertyType == typeof(LocalDroneAppearance),
                "missing closed Arsenal drone contract: LaunchPlan.LocalArsenalDrone : LocalDroneAppearance");
            Type? rules = typeof(LaunchPlan).Assembly.GetType("MphRead.Mods.Launcher.LocalArsenalDroneRules");
            Check(rules != null, "missing closed Arsenal drone contract: LocalArsenalDroneRules");
            _available = rules!.GetProperty("Available", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!;
            _admit = rules.GetMethod("TryAdmit", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                null, new[] { typeof(LaunchPlan), typeof(string).MakeByRefType() }, null)!;
            Check(_available?.PropertyType == typeof(bool) && _admit?.ReturnType == typeof(bool),
                "Arsenal drone exposes Available and TryAdmit(LaunchPlan,out string)");
            Check(!NetSession.Active && !DemoPlayback.IsActive && !ReplayCapture.LiveRecordingActive,
                "pure fixture starts without transport/playback/recording");
            Policy(); Capture(); Transport();
            Console.WriteLine($"ARSENAL DRONE PRESENTATION PASS {_checks} pure admission/Hunters capture assertions; no source IO, Scene, gameplay, native UI or GPU");
            return 0;
        }
        catch (Exception error)
        { Console.Error.WriteLine($"ARSENAL DRONE PRESENTATION FAIL after {_checks} checks: {error}"); return 1; }
        finally
        {
            (LauncherPrefs.LocalBody, LauncherPrefs.LocalDrone, LauncherPrefs.LastHunter,
                LauncherPrefs.LastColor, LauncherPrefs.Bots, LauncherPrefs.BotLevel, LauncherPrefs.LastKind) = old;
        }
    }
    private static bool Available => (bool)_available.GetValue(null)!;
    private static LocalDroneAppearance Appearance(LaunchPlan plan) => (LocalDroneAppearance)_request.GetValue(plan)!;
    private static LaunchPlan With(LaunchPlan plan, LocalDroneAppearance value)
    { object boxed = plan; _request.SetValue(boxed, value); return (LaunchPlan)boxed; }
    private static bool Admit(LaunchPlan plan, out string reason)
    {
        object?[] args = { plan, null };
        bool result;
        try { result = (bool)_admit.Invoke(null, args)!; }
        catch (TargetInvocationException ex) when (ex.InnerException != null)
        { throw new InvalidOperationException("Admission must return a refusal rather than throw", ex.InnerException); }
        reason = (string?)args[1] ?? ""; return result;
    }
    private static void Refuse(LaunchPlan plan, string label)
        => Check(!Admit(plan, out string reason) && !string.IsNullOrWhiteSpace(reason), label);
    private static LaunchPlan Arsenal(int bots = 0, int level = 0) => new()
    {
        Kind = LaunchKind.Offline, Hunter = Hunter.Weavel, Mode = GameMode.Battle,
        RoomKey = "synthetic-arena", IsPlaytest = true, OfflineCharacter = OfflineCharacterMode.TrooperArsenalPractice,
        Bots = bots, BotLevel = level, MatchRules = new MatchDefinition { Mode = GameMode.Battle, MovementModel = MovementModel.Native }
    };
    private static void Policy()
    {
        Check(Appearance(default) == LocalDroneAppearance.Native, "zero request is Native");
        Check(Available == LocalDroneAppearanceRules.Available, "same qualified platform boundary as existing source drone");
        foreach (LaunchKind kind in Enum.GetValues<LaunchKind>())
        foreach (OfflineCharacterMode mode in Enum.GetValues<OfflineCharacterMode>())
            Check(Admit(Arsenal() with { Kind = kind, OfflineCharacter = mode }, out string reason) && reason == "",
                "Native request adds no capabilities or restrictions to existing routes");
        foreach (int value in new[] { int.MinValue, -1, 3, 4, 77, 255, int.MaxValue })
            Refuse(With(Arsenal(), (LocalDroneAppearance)value), "forged Arsenal drone refuses: " + value);
        foreach (var drone in new[] { LocalDroneAppearance.Alpha, LocalDroneAppearance.Gamma })
        foreach (var body in Bodies)
        for (int bots = 0; bots <= 7; bots++)
        for (int level = 0; level <= 3; level++)
        {
            var plan = With(Arsenal(bots, level) with { LocalArsenalBody = body }, drone);
            Check(OfflineCharacterGameplay.TryAdmit(plan, out _), "native Arsenal rule already owns configured0..7 native opponents");
            bool accepted = Admit(plan, out string reason);
            Check(accepted == Available && (accepted ? reason == "" : !string.IsNullOrWhiteSpace(reason)),
                "finite source drone admits independently of Native/four finite Arsenal bodies iff qualified");
            Check(LocalArsenalBodyRules.TryAdmit(plan, out _) == (body == LocalBodyAppearance.Native || Available),
                "independent existing Arsenal body admission also permits the source drone combination");
            Check(plan.LocalBody == LocalBodyAppearance.Native && plan.LocalDrone == LocalDroneAppearance.Native,
                "Arsenal never borrows ordinary body or solo-drone request");
        }
        var good = With(Arsenal(), LocalDroneAppearance.Alpha);
        var bad = new List<LaunchPlan>
        {
            good with { IsPlaytest = false }, good with { Bots = -1 }, good with { Bots = 8 },
            good with { BotLevel = -1 }, good with { BotLevel = 4 },
            good with { Hunter = (Hunter)255 }, good with { OfflineCharacter = (OfflineCharacterMode)77 },
            good with { LocalBody = LocalBodyAppearance.TrooperPower }, good with { LocalBody = (LocalBodyAppearance)77 },
            good with { LocalDrone = LocalDroneAppearance.Alpha }, good with { LocalDrone = LocalDroneAppearance.Gamma },
            good with { LocalDrone = (LocalDroneAppearance)77 },
            good with { LocalArsenalBody = LocalBodyAppearance.DarkSamusEchoes },
            good with { LocalArsenalBody = LocalBodyAppearance.DarkSamusOvercharged },
            good with { LocalArsenalBody = (LocalBodyAppearance)(-1) }, good with { LocalArsenalBody = (LocalBodyAppearance)77 },
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
        bad.AddRange(Enum.GetValues<OfflineCharacterMode>().Where(m => m != OfflineCharacterMode.TrooperArsenalPractice).Select(m => good with { OfflineCharacter = m }));
        bad.AddRange(LauncherHunterChoiceController.Playable.Where(h => h != Hunter.Weavel).Select(h => good with { Hunter = h }));
        bad.AddRange(Enum.GetValues<GameMode>().Where(m => m != GameMode.Battle).Select(m => good with { Mode = m, MatchRules = good.MatchRules with { Mode = m } }));
        bad.AddRange(Enum.GetValues<MovementModel>().Where(m => m != MovementModel.Native).Select(m => good with { MatchRules = good.MatchRules with { MovementModel = m } }));
        foreach (var plan in bad) Refuse(plan, "incompatible or forged Arsenal drone request refuses before source IO");
        foreach (var drone in new[] { LocalDroneAppearance.Alpha, LocalDroneAppearance.Gamma })
        {
            var solo = Arsenal() with { OfflineCharacter = OfflineCharacterMode.TrooperScoutDronePractice, LocalDrone = drone };
            Check(LocalDroneAppearanceRules.TryAdmit(solo, out _) == Available, "existing solo source drone admission remains available");
            Check(!LocalDroneAppearanceRules.TryAdmit(Arsenal() with { LocalDrone = drone }, out _), "existing LocalDrone stays solo-only");
            Check(!LocalDroneAppearanceRules.TryAdmit(solo with { Bots = 1 }, out _), "existing solo source drone never admits combat bots");
        }
    }
    private static void Capture()
    {
        var settings = new MenuSettings { RoomKey = "unchanged", MovementModel = "Native", BalancedMode = "off", LowTier = "off", RiftlineM7Available = false };
        string before = JsonSerializer.Serialize(settings);
        foreach (var drone in new[] { LocalDroneAppearance.Native, LocalDroneAppearance.Alpha, LocalDroneAppearance.Gamma })
        foreach (var body in Bodies)
        {
            var store = new ChoiceStore(new(Hunter.Weavel, 1, body));
            using var draft = new LauncherHunterChoiceController(store);
            Check(draft.SelectDrone((int)drone) == (drone == LocalDroneAppearance.Native || Available), "existing Hunters Drone selection respects platform capability");
            if (drone == LocalDroneAppearance.Native || Available)
            {
                Check(store.Choice.Drone == LocalDroneAppearance.Native, "unsaved draft has not changed persisted Drone");
                Check(draft.Apply() && store.Choice.Drone == drone && store.Choice.Body == body,
                    "existing Hunters Apply preserves Body while saving independent Drone");
            }
            LauncherPrefs.LocalBody = store.Choice.Body;
            LauncherPrefs.LocalDrone = drone == LocalDroneAppearance.Native || Available ? store.Choice.Drone : drone;
            var plan = Preview(OfflineCharacterMode.TrooperArsenalPractice);
            Check(Appearance(plan) == (Available ? drone : LocalDroneAppearance.Native)
                && plan.LocalArsenalBody == (Available ? body : LocalBodyAppearance.Native)
                && plan.LocalDrone == LocalDroneAppearance.Native && plan.LocalBody == LocalBodyAppearance.Native,
                "explicit Arsenal captures existing applied Body and Drone into independent requests");
            Check(plan.OfflineCharacter == OfflineCharacterMode.TrooperArsenalPractice && plan.IsPlaytest && plan.Bots == 7,
                "source preference never creates or changes the explicit private gameplay mode");
            var ordinary = Preview(OfflineCharacterMode.Native);
            Check(Appearance(ordinary) == LocalDroneAppearance.Native && ordinary.LocalArsenalBody == LocalBodyAppearance.Native
                && ordinary.LocalDrone == LocalDroneAppearance.Native && !ordinary.IsPlaytest,
                "saved source Drone cannot enable drone mechanics or Arsenal in ordinary offline play");
            foreach (var mode in new[] { OfflineCharacterMode.TrooperWallClingPractice, OfflineCharacterMode.TrooperScoutDronePractice, OfflineCharacterMode.TrooperScoutDroneCombatPractice })
            {
                var other = Preview(mode, 0);
                Check(Appearance(other) == LocalDroneAppearance.Native && other.LocalArsenalBody == LocalBodyAppearance.Native,
                    "other practice modes never infer Arsenal presentation capability");
                Check(other.LocalDrone == (mode == OfflineCharacterMode.TrooperScoutDronePractice && Available ? drone : LocalDroneAppearance.Native),
                    "existing solo capture stays exactly scoped");
            }
            if (Available)
            {
                Check(draft.SelectDrone(drone == LocalDroneAppearance.Gamma ? 1 : 2), "edit existing Drone draft");
                draft.Discard();
                Check(draft.Current().Choice.Drone == drone && draft.Current().Choice.Body == body && !draft.Current().Dirty,
                    "cancel restores applied independent Body and Drone");
            }
        }
        Check(JsonSerializer.Serialize(settings) == before, "Preview/capture never mutates caller settings");
        LaunchPlan Preview(OfflineCharacterMode mode, int bots = 7) => OfflineLaunch.Preview(settings,
            "synthetic-arena", GameMode.Battle, Hunter.Weavel, 1, bots, 0, mode);
    }
    private static void Transport()
    {
        var good = With(Arsenal(), LocalDroneAppearance.Gamma);
        var role = typeof(NetSession).GetProperty(nameof(NetSession.Role))!;
        var playback = DemoPlayback.Session; var active = playback.GetType().GetProperty("IsActive")!;
        var pending = typeof(DemoRecorder).GetField("_pending", BindingFlags.Static | BindingFlags.NonPublic)!;
        NetRole oldRole = NetSession.Role; bool oldActive = (bool)active.GetValue(playback)!; object? oldPending = pending.GetValue(null);
        try
        {
            foreach (var state in new[] { NetRole.Host, NetRole.Client, NetRole.Server })
            { role.SetValue(null, state); Check(NetSession.Active, "declared active-role predicate"); Refuse(good, "network role refuses source Arsenal drone"); }
            role.SetValue(null, oldRole); active.SetValue(playback, true);
            Check(DemoPlayback.IsActive, "declared playback predicate without stream"); Refuse(good, "playback refuses Arsenal drone");
            active.SetValue(playback, oldActive); pending.SetValue(null, true);
            Check(ReplayCapture.LiveRecordingActive, "declared pending recorder predicate without writer"); Refuse(good, "recording refuses Arsenal drone");
        }
        finally { pending.SetValue(null, oldPending); active.SetValue(playback, oldActive); role.SetValue(null, oldRole); }
        Check(NetSession.Role == oldRole && DemoPlayback.IsActive == oldActive && Equals(pending.GetValue(null), oldPending),
            "all controlled global metadata restored exactly");
    }
    private sealed class ChoiceStore(LauncherHunterChoice choice) : ILauncherHunterChoiceStore
    {
        internal LauncherHunterChoice Choice = choice;
        public LauncherHunterChoice Capture() => Choice;
        public void Save(LauncherHunterChoice value) => Choice = value;
    }
}
