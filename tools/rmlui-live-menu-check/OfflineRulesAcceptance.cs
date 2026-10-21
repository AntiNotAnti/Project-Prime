using System;
using MphRead;
using MphRead.Mods.Launcher.RmlUi.Core;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Multiplayer;

internal static class OfflineRulesAcceptance
{
    internal static void Run()
    {
        var saved = new MenuSettings { ThirdPersonCamera = "on" };
        var cameraRules = RmlUiOfflineRules.Capture(saved);
        Check(cameraRules.Enabled(12), "saved third-person camera setting restores as a local rule");
        (cameraRules with { Flags = (ushort)(cameraRules.Flags & ~(1 << 12)) }).CopyTo(saved);
        Check(saved.ThirdPersonCamera == "off", "camera rule off is persisted independently");
        var services = new Stub();
        using var controller = new RmlUiMenuController(services);
        Check(controller.Dispatch(RmlUiIntentKind.Navigate, (int)RmlUiRouteArgument.Offline)
            && controller.Dispatch(RmlUiIntentKind.OfflineOpenRules)
            && Flag("offline_rules_open") && controller.OfflineRuleInputs == services.OfflineRules,
            "native local rules open from the existing settings snapshot");
        Check(controller.Dispatch(RmlUiIntentKind.OfflineRuleToggle, 12)
            && controller.OfflineRuleInputs?.Enabled(12) == true,
            "third-person camera is an independent local match rule");
        Check(controller.Dispatch(RmlUiIntentKind.OfflineRuleToggle, 6)
            && controller.Dispatch(RmlUiIntentKind.OfflineDamage)
            && controller.OfflineRuleInputs?.Enabled(6) == true
            && services.OfflineRulesSaveAttempts == 0
            && services.OfflineRules?.Enabled(6) == false,
            "native rule and damage edits remain private until explicit Apply");
        Check(!controller.Dispatch(RmlUiIntentKind.OfflineChoice, 1)
            && !controller.Dispatch(RmlUiIntentKind.OfflineLaunchMatch)
            && !controller.Dispatch(RmlUiIntentKind.Navigate, (int)RmlUiRouteArgument.Home)
            && !controller.Dispatch(RmlUiIntentKind.OfflineRuleToggle, 5),
            "foreground local rules block background navigation/start and retired Chamber toggle");
        Check(controller.Dispatch(RmlUiIntentKind.OfflineCancelRules)
            && !Flag("offline_rules_open") && controller.OfflineRuleInputs == null
            && services.OfflineRulesSaves == 0 && services.OfflineRules?.Enabled(6) == false,
            "Cancel retires the local rule draft without persistence");

        controller.Dispatch(RmlUiIntentKind.OfflineOpenRules);
        Check(!controller.Dispatch(RmlUiIntentKind.OfflineApplyRules,
                offlinePoints: "12", offlineLimit: "7:65", offlineGoal: "3:00")
            && Flag("offline_rules_open") && Text("offline_rules_error").Contains("DURATIONS")
            && services.OfflineRulesSaveAttempts == 0,
            "malformed durations keep rules open and never reach persistence");
        controller.Dispatch(RmlUiIntentKind.OfflineRuleToggle, 6); // Fiesta
        controller.Dispatch(RmlUiIntentKind.OfflineRuleToggle, 10); // Balanced
        Check(!controller.Dispatch(RmlUiIntentKind.OfflineApplyRules,
                offlinePoints: "12", offlineLimit: "8:20", offlineGoal: "3:00")
            && Flag("offline_rules_open") && Text("offline_rules_error").Contains("Balanced Mode")
            && services.OfflineRulesSaveAttempts == 0,
            "invalid modifier combinations remain local with the current validator's reason");
        controller.Dispatch(RmlUiIntentKind.OfflineRuleToggle, 6); // Remove Fiesta.
        controller.Dispatch(RmlUiIntentKind.OfflineRuleToggle, 8); // Low Tier.
        services.FailOfflineRulesSave = true;
        Check(!controller.Dispatch(RmlUiIntentKind.OfflineApplyRules,
                offlinePoints: "12", offlineLimit: "8:20", offlineGoal: "3:00")
            && Flag("offline_rules_open") && Text("offline_rules_error").Contains("SIMULATED SAVE FAILURE")
            && services.OfflineRulesSaves == 0 && controller.OfflineRuleInputs?.PointGoal == "12",
            "failed settings write retains the editable draft and actionable error");
        services.FailOfflineRulesSave = false;
        Check(controller.Dispatch(RmlUiIntentKind.OfflineApplyRules,
                offlinePoints: "12", offlineLimit: "8:20", offlineGoal: "3:00")
            && !Flag("offline_rules_open") && services.OfflineRulesSaves == 1
            && services.OfflineRules?.Enabled(8) == true
            && services.OfflineRules?.TimeLimit == "8:20"
            && !controller.Dispatch(RmlUiIntentKind.OfflineApplyRules,
                offlinePoints: "12", offlineLimit: "8:20", offlineGoal: "3:00")
            && services.OfflineRulesSaves == 1,
            "successful retry commits once and a retired editor cannot save twice");
        bool lowTierOnly = true;
        for (int i = 0; i < 12; i++)
        {
            string hunterName = Text("offline_hunter_value");
            lowTierOnly &= Enum.TryParse<Hunter>(hunterName, true, out var hunter)
                && HunterRules.Allowed(hunter, true);
            lowTierOnly &= controller.Dispatch(RmlUiIntentKind.OfflineChoice, 5);
        }
        Check(lowTierOnly, "committed Low Tier sanitizes the current Hunter and restricts later cycling");
        Check(controller.Dispatch(RmlUiIntentKind.OfflineOpenRules)
            && controller.OfflineRuleInputs == services.OfflineRules,
            "reopening local rules rehydrates the committed snapshot");
        controller.Dispatch(RmlUiIntentKind.OfflineCancelRules);
        services.OfflineRules = null;
        Check(!controller.Dispatch(RmlUiIntentKind.OfflineOpenRules)
            && !Flag("offline_rules_open"), "unavailable settings service cannot manufacture a rules draft");
        MovementBindings();

        bool Flag(string name) => controller.Tick().Bindings.TryGetValue(name, out var value) && value.Boolean;
        string Text(string name) => controller.Tick().Bindings.TryGetValue(name, out var value) ? value.Text : "";
    }

    private static void MovementBindings()
    {
        var services = new Stub { BuiltInMaps = new[] { "MP1 SANCTORUS", "MP2 HARVESTER" } };
        using var controller = new RmlUiMenuController(services);
        string Text(string name) => controller.Tick().Bindings.TryGetValue(name, out var value) ? value.Text : "";
        bool Flag(string name) => controller.Tick().Bindings.TryGetValue(name, out var value) && value.Boolean;
        Check(controller.Dispatch(RmlUiIntentKind.Navigate, (int)RmlUiRouteArgument.Offline)
            && controller.Dispatch(RmlUiIntentKind.OfflineOpenRules)
            && Flag("offline_movement_visible") && Text("offline_rule13_label") == "MOVEMENT // CLASSIC",
            "offline native movement binding restores Classic default in visible multiplayer choice");
        Check(controller.Dispatch(RmlUiIntentKind.OfflineRuleToggle, 13)
            && Text("offline_rule13_label") == "MOVEMENT // MOMENTUM"
            && controller.Dispatch(RmlUiIntentKind.OfflineRuleToggle, 13)
            && Text("offline_rule13_label") == "MOVEMENT // QUAKE III"
            && controller.Dispatch(RmlUiIntentKind.OfflineRuleToggle, 13)
            && Text("offline_rule13_label") == "MOVEMENT // TRAVERSAL"
            && controller.OfflineRuleInputs?.MovementModel == "Traversal"
            && controller.Dispatch(RmlUiIntentKind.OfflineRuleToggle, 13)
            && Text("offline_rule13_label") == "MOVEMENT // PARKOUR"
            && controller.OfflineRuleInputs?.MovementModel == "Parkour"
            && controller.Dispatch(RmlUiIntentKind.OfflineRuleToggle, 13)
            && Text("offline_rule13_label") == "MOVEMENT // CLASSIC"
            && controller.OfflineRuleInputs?.MovementModel == "Native"
            && controller.Dispatch(RmlUiIntentKind.OfflineRuleToggle, 13)
            && controller.Dispatch(RmlUiIntentKind.OfflineRuleToggle, 12)
            && controller.Dispatch(RmlUiIntentKind.OfflineDamage)
            && controller.OfflineRuleInputs?.MovementModel == "Momentum"
            && services.OfflineRules?.MovementModel == "Native" && services.OfflineRulesSaveAttempts == 0,
            "the five-way movement cycle including Parkour and unrelated draft edits stay private before Apply");
        Check(controller.Dispatch(RmlUiIntentKind.OfflineCancelRules)
            && services.OfflineRules?.MovementModel == "Native", "Cancel preserves authoritative saved Classic");
        controller.Dispatch(RmlUiIntentKind.OfflineOpenRules);
        controller.Dispatch(RmlUiIntentKind.OfflineRuleToggle, 13);
        controller.Dispatch(RmlUiIntentKind.OfflineRuleToggle, 10);
        Check(!controller.Dispatch(RmlUiIntentKind.OfflineApplyRules, offlinePoints: "7", offlineLimit: "7:00", offlineGoal: "3:00")
            && Text("offline_rules_error").Contains("Momentum") && services.OfflineRulesSaveAttempts == 0
            && controller.OfflineRuleInputs?.MovementModel == "Momentum",
            "incompatible Balanced Momentum shows validator error and reaches no persistence");
        controller.Dispatch(RmlUiIntentKind.OfflineRuleToggle, 10);
        Check(controller.Dispatch(RmlUiIntentKind.OfflineApplyRules, offlinePoints: "7", offlineLimit: "7:00", offlineGoal: "3:00")
            && services.OfflineRules?.MovementModel == "Momentum" && services.OfflineRulesSaves == 1,
            "explicit corrected Momentum selection persists once");
        Check(controller.Dispatch(RmlUiIntentKind.OfflineChoice, 0)
            && controller.Dispatch(RmlUiIntentKind.OfflineChoice, 1)
            && controller.Dispatch(RmlUiIntentKind.OfflineChoice, 2)
            && controller.Dispatch(RmlUiIntentKind.OfflineOpenRules)
            && Text("offline_rule13_label") == "MOVEMENT // MOMENTUM",
            "map mode and format edits preserve persisted movement on reopening");
        controller.Dispatch(RmlUiIntentKind.OfflineCancelRules);
        services.OfflineRules = services.OfflineRules!.Value with { MovementModel = "unknown" };
        int attempts = services.OfflineRulesSaveAttempts;
        Check(controller.Dispatch(RmlUiIntentKind.OfflineOpenRules)
            && Text("offline_rule13_label") == "MOVEMENT // UNSUPPORTED"
            && !controller.Dispatch(RmlUiIntentKind.OfflineApplyRules, offlinePoints: "7", offlineLimit: "7:00", offlineGoal: "3:00")
            && services.OfflineRulesSaveAttempts == attempts && services.OfflineRules?.MovementModel == "unknown",
            "unknown saved model displays Unsupported and fails Apply without automatic repair");
        Check(controller.Dispatch(RmlUiIntentKind.OfflineRuleToggle, 13)
            && Text("offline_rule13_label") == "MOVEMENT // CLASSIC"
            && controller.Dispatch(RmlUiIntentKind.OfflineApplyRules, offlinePoints: "7", offlineLimit: "7:00", offlineGoal: "3:00")
            && services.OfflineRules?.MovementModel == "Native",
            "explicit movement click repairs unknown saved value to Classic before saving");
        services.OfflineRules = services.OfflineRules!.Value with { MovementModel = "Momentum" };
        using var reloaded = new RmlUiMenuController(services);
        Check(reloaded.Dispatch(RmlUiIntentKind.Navigate, (int)RmlUiRouteArgument.Offline)
            && reloaded.Dispatch(RmlUiIntentKind.OfflineOpenRules)
            && reloaded.Tick().Bindings.TryGetValue("offline_rule13_label", out var label)
            && label.Text == "MOVEMENT // MOMENTUM", "controller reload rehydrates saved movement display");
    }

    private static void Check(bool pass, string message)
    {
        if (!pass) throw new InvalidOperationException(message);
        Console.WriteLine("RML OFFLINE RULES PASS " + message);
    }
}
