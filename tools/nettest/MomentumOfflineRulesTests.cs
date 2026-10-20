using System;
using System.Text.Json;
using MphRead.Mods;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.RmlUi.Core;
using MphRead.Mods.Movement;
using MphRead.Mods.Network;

namespace MphRead.NetTest;

internal static class MomentumOfflineRulesTests
{
    private static int _checks;
    private static void Check(bool condition, string message)
    { _checks++; if (!condition) throw new InvalidOperationException(message); }
    private static bool Refused(Action action)
    { try { action(); return false; } catch (ArgumentException) { return true; } }
    private static SceneGameState State() => new(new ScenePlayerRegistry())
    { Mode = GameMode.Battle, MovementModel = MovementModel.Native, PointGoal = 27, MatchTime = 91, FriendlyFire = true };
    private static void Unchanged(SceneGameState state) => Check(state.MovementModel == MovementModel.Native
        && state.PointGoal == 27 && state.MatchTime == 91 && state.FriendlyFire, "invalid saved rules reject before scene mutation");

    internal static int Run()
    {
        try
        {
            _checks = 0;
            Check(!NetSession.Active, "pure offline configuration tests");
            var absent = JsonSerializer.Deserialize<MenuSettings>("{}")!;
            Check(absent.MovementModel == "Native", "missing settings property defaults Native");
            var state = State();
            GameSettings.ApplyMatchRules(absent, state);
            Check(state.MovementModel == MovementModel.Native, "absent saved model applies Native");
            foreach (string invalid in new[] { "Unknown", "1", "Momentum " })
            {
                var settings = new MenuSettings { MovementModel = invalid, PointGoal = "3", FriendlyFire = "off" };
                state = State();
                Check(Refused(() => GameSettings.ApplyMatchRules(settings, state)), "unknown saved model refused");
                Unchanged(state);
                Check(Refused(() => OfflineLaunch.Preview(settings, "fixture", GameMode.Battle, Hunter.Samus, 0, 0, 0)), "unknown preview refused");
                Check(settings.RoomKey != "fixture", "preview does not persist room");
            }
            var conflict = new MenuSettings { MovementModel = "Momentum", BalancedMode = "on", PointGoal = "3", FriendlyFire = "off" };
            state = State();
            Check(Refused(() => GameSettings.ApplyMatchRules(conflict, state)), "Momentum plus Balanced saved rules refused");
            Unchanged(state);
            Check(Refused(() => OfflineLaunch.Preview(conflict, "fixture", GameMode.Battle, Hunter.Samus, 0, 0, 0)), "Momentum plus Balanced preview refused");
            state.Mode = GameMode.SinglePlayer; state.MovementModel = MovementModel.Momentum;
            GameSettings.ApplyMatchRules(conflict, state);
            Check(state.MovementModel == MovementModel.Native && state.PointGoal == 27, "campaign forces Native without applying multiplayer preferences");
            Check(OfflineLaunch.Preview(conflict, "fixture", GameMode.SinglePlayer, Hunter.Samus, 0, 0, 0)
                .MatchRules.MovementModel == MovementModel.Native, "campaign preview cannot select Momentum");

            var momentum = new MenuSettings { MovementModel = "mOmEnTuM", ThirdPersonCamera = "on" };
            var plan = OfflineLaunch.Preview(momentum, "fixture", GameMode.Battle, Hunter.Spire, 0, 0, 0);
            Check(plan.MatchRules.MovementModel == MovementModel.Momentum && plan.MatchRules.ThirdPersonCamera, "offline preview preserves model and independent camera choice");
            state = State(); GameSettings.ApplyMatchRules(momentum, state);
            Check(state.MovementModel == MovementModel.Momentum && state.ThirdPersonCamera, "offline saved model applies explicitly");
            var quake = new MenuSettings { MovementModel = "Quake3", ThirdPersonCamera = "on" };
            var quakePlan = OfflineLaunch.Preview(quake, "fixture", GameMode.Battle, Hunter.Samus, 0, 0, 0);
            Check(quakePlan.MatchRules.MovementModel == MovementModel.Quake3, "normal offline preview selects Quake III");
            state = State(); GameSettings.ApplyMatchRules(quake, state);
            Check(state.MovementModel == MovementModel.Quake3 && state.ThirdPersonCamera, "saved Quake III applies without developer opt-in");
            Check(OfflineLaunch.Preview(quake, "fixture", GameMode.SinglePlayer, Hunter.Samus, 0, 0, 0)
                .MatchRules.MovementModel == MovementModel.Native && quake.MovementModel == "Quake3",
                "campaign remains Classic while retaining saved practice Quake III");
            var quakeConflict = new MenuSettings { MovementModel = "Quake3", BalancedMode = "on" };
            state = State();
            Check(Refused(() => GameSettings.ApplyMatchRules(quakeConflict, state)), "saved Quake III rejects unsupported Balanced abilities");
            Unchanged(state);
            var historic = new MenuSettings { MovementModel = "Native", BalancedMode = "on", Fiesta = "on" };
            GameSettings.ApplyMatchRules(historic, State());
            Check(true, "new raw-saved guard does not newly reject historical Native modifier preferences");

            var original = RmlUiOfflineRules.Capture(momentum);
            var changed = original with { Flags = (ushort)(original.Flags ^ 1) };
            var target = new MenuSettings(); original.CopyTo(target);
            Check(target.MovementModel == momentum.MovementModel, "hidden model survives offline rule copy");
            Check(!RmlUiOfflineRules.TryCommit(target, original, changed,
                _ => throw new InvalidOperationException("fixture persistence failure"), out _), "failed persistence rolls back");
            Check(RmlUiOfflineRules.Capture(target) == original && target.MovementModel == momentum.MovementModel, "rollback retains exact hidden model");
            Check(RmlUiOfflineRules.TryCommit(target, original, changed, _ => { }, out _)
                && target.MovementModel == momentum.MovementModel, "unrelated saved rule edit retains hidden model");
            var editor = new RmlUiOfflineRuleEditor(GameMode.Battle, RmlUiOfflineRules.Capture(conflict));
            Check(!editor.Validate("7", "7:00", "1:00"), "offline rule validation sees hidden Momentum and Balanced conflict");
            var retained = plan.MatchRules with { PointGoal = 12 };
            Check(retained.MovementModel == MovementModel.Momentum, "record-based lobby draft edits retain model");
            Console.WriteLine($"MOMENTUM OFFLINE RULES PASS {_checks} assertions; pure configuration, no legacy Avalonia UI execution");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine("MOMENTUM OFFLINE RULES FAIL " + error); return 1; }
    }
}
