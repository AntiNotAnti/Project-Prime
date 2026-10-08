#if MPHREAD_RMLUI_POC
using System.Runtime.InteropServices;
using MphRead;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Pages.Offline;
using MphRead.Mods.Network;
using MphRead.Mods.Training;


internal static class NativeOfflineCheck
{
    public static void Run(string library, string assets)
    {
        int count = 0;
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); count++; Console.WriteLine("PASS " + name); }
        string root = Path.Combine(Path.GetTempPath(), "prime-offline-assets-" + Guid.NewGuid().ToString("N"));
        void Copy(string from, string to)
        {
            foreach (string path in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
            {
                string target = Path.Combine(to, Path.GetRelativePath(from, path)); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(path, target, true);
            }
        }
        Copy(Path.GetFullPath(assets), root);
        nint module = NativeLibrary.Load(Path.GetFullPath(library));
        NativeLibrary.SetDllImportResolver(typeof(RmlUiHost).Assembly, (name, _, _) => name == "ProjectPrime.RmlUi.Native" ? module : 0);
        foreach (var viewport in new[] { (1280, 720, 1f), (1440, 900, 1f), (1920, 1080, 1f), (3024, 1646, 2f), (2560, 1080, 1f), (1024, 768, 1f), (800, 450, 1f), (1280, 720, 2f) })
        {
        using var host = new RmlUiHost();
        Check(host.Initialize(viewport.Item1, viewport.Item2, viewport.Item3, root, RmlUiRenderBackend.DrawList), "actual native DrawList host initializes");
        using var manager = new RmlUiPageManager(host);
        LauncherPrefs.LastHunter = Hunter.Samus; LauncherPrefs.Training = AimTrainerDefinition.Default;
        var backend = new NativeBackend(); using var controller = new OfflineController(new MenuSettings(), new[] { "first", "second" }, backend);
        using var presenter = new OfflinePagePresenter(host, manager, controller);
        presenter.Open(); host.Update();
        var page = presenter.Document;
        Check(host.IsAlive(page) && manager.Page == page, "actual Offline page opens through shared shell");
        Check(host.TryGetElementBounds(page, "offline_page", out var pageX, out var pageY, out var pageW, out var pageH)
            && host.TryGetElementBounds(page, "stage", out var stageX, out var stageY, out var stageW, out var stageH)
            && pageW >= viewport.Item1 * .98f && pageH > 0 && pageH <= stageH + 1,
            "scroll surface keeps full width and exact shell height");
        Check(host.TryGetElementBounds(page, "offline_columns", out var cx, out var cy, out var cw, out var ch)
            && cw >= viewport.Item1 * .82f && cx + cw <= viewport.Item1 + 1 && ch > 0,
            "column content has positive viewport-bounded width");
        bool matchBounds = host.TryGetElementBounds(page, "offline_match_panel", out var ax, out var ay, out var aw, out var ah);
        bool trainingBounds = host.TryGetElementBounds(page, "offline_training_panel", out var bx, out var by, out var bw, out var bh);
        Check(matchBounds && trainingBounds
            && aw >= viewport.Item1 * .35f && bw >= viewport.Item1 * .35f
            && (Math.Abs(ay - by) < 1 ? ax + aw <= bx : ay + ah <= by),
            "responsive match/training panels are wide and do not overlap");
        foreach (var control in new[] { ("offline_arena", ax, aw), ("offline_mode", ax, aw), ("offline_start", ax, aw), ("offline_training_start", bx, bw) })
            Check(host.TryGetElementBounds(page, control.Item1, out var x, out _, out var width, out var height)
                && width >= control.Item3 * .8f && height >= 30 * viewport.Item3
                && x >= control.Item2 && x + width <= control.Item2 + control.Item3,
                "wide readable control inside panel: " + control.Item1);
        Check(host.TryGetElementBounds(page, "offline_eyebrow", out _, out _, out var eyebrowW, out var eyebrowH)
            && eyebrowW >= viewport.Item1 * .8f && eyebrowH <= 14 * viewport.Item3,
            "eyebrow stays on one readable line");
        foreach (string area in new[] { "", "training_", "adventure_" })
        {
            string heading = "offline_" + area + "heading", description = "offline_" + area + "description";
            Check(host.TryGetElementBounds(page, heading, out _, out var hy, out _, out var hh)
                && host.TryGetElementBounds(page, description, out _, out var dy, out _, out _) && dy >= hy + hh + 4,
                "block typography separates " + heading + " and description");
        }
        void Press(string id, RmlUiIntentKind expected)
        {
            Check(host.FocusDocument(manager.Top, id), "focus " + id);
            host.Input.Key(2, true); host.Input.Key(2, false); host.Update();
            Check(host.TryTakeIntent(out var action) && action.Kind == expected && action.Document == manager.Top, "native typed action " + id);
            Check(presenter.HandleAction(action), "presenter consumes " + id); host.Update();
        }
        Check(presenter.FocusTraining(), "Change Drill focuses actual training launch control"); host.Update();
        Check(host.FocusedElement() == "offline_training_start", "Change Drill preserves native training focus");
        Check(host.FocusDocument(page, "offline_arena"), "return to setup focus for actual pointer flow"); host.Update();
        Check(host.TryGetElementBounds(page, "offline_arena", out var px, out var py, out var pwidth, out var pheight), "real arena pointer target bounds");
        host.Input.PointerButton(0, px + pwidth / 2, py + pheight / 2, true);
        host.Input.PointerButton(0, px + pwidth / 2, py + pheight / 2, false); host.Update();
        Check(host.TryTakeIntent(out var pointer) && pointer.Kind == RmlUiIntentKind.OfflineOpenArena, "actual pointer reaches arena button instead of full-page scrollbar");
        Check(presenter.HandleAction(pointer), "presenter consumes actual pointer arena action"); host.Update();
        host.SetField(manager.Top, "offline_arena_search", "second");
        Press("offline_arena_search_apply", RmlUiIntentKind.OfflineArenaSearch);
        Press("offline_arena_pick_0", RmlUiIntentKind.OfflineSelectArena);
        Check(controller.Snapshot().ArenaKey == "second", "DOM arena control changes real local draft");
        Press("offline_rules_open", RmlUiIntentKind.OfflineOpenRules);
        var rules = manager.Top;
        Check(manager.ModalCount == 1 && host.ReadField(rules, "offline_point_goal") == "7", "rules field seeded from persisted settings once");
        Check(!presenter.FocusTraining() && host.CurrentInputDocument == rules, "Change Drill cannot steal focus from active modal");
        host.SetField(rules, "offline_point_goal", "26");
        Press("offline_rule_2", RmlUiIntentKind.OfflineRuleToggle);
        Check(host.ReadField(rules, "offline_point_goal") == "26", "snapshot does not overwrite live point goal edit");
        host.SetField(rules, "offline_time_limit", "bad");
        Press("offline_rules_apply", RmlUiIntentKind.OfflineApplyRules);
        Check(manager.Top == rules && controller.Snapshot().RulesOpen && backend.Commits == 0, "invalid rules stay in native sheet without persistence");
        host.SetField(rules, "offline_time_limit", "9:00");
        Press("offline_rules_apply", RmlUiIntentKind.OfflineApplyRules);
        Check(manager.ModalCount == 0 && !host.IsAlive(rules) && backend.Commits == 1 && backend.Applies == 1, "valid rules commit and retire actual modal");
        Check(!presenter.HandleAction(new(RmlUiIntentKind.OfflineRuleToggle, 2, rules, 9999)), "retired rule document cannot mutate draft");
        Press("offline_training_options", RmlUiIntentKind.OfflineOpenTrainingOptions);
        Press("offline_training_difficulty", RmlUiIntentKind.OfflineChoice);
        Press("offline_training_toggle_4", RmlUiIntentKind.OfflineTrainingToggle);
        Press("offline_training_done", RmlUiIntentKind.OfflineCloseTrainingOptions);
        Check(controller.Snapshot().Training.FixedSeed && controller.Snapshot().Training.Difficulty == TrainingDifficulty.Advanced, "actual training sheet edits authoritative definition");
        Press("offline_training_start", RmlUiIntentKind.OfflineLaunchTraining);
        Check(presenter.TryTakeLaunch(out var plan) && plan.Kind == LaunchKind.AimTrainer && plan.Training!.Value.FixedSeed, "actual native launch hands off authoritative plan");
        Check(!host.FocusDocument(page, "offline_training_start") && !controller.RequestTraining().Accepted, "launch controls disabled while issued handoff remains pending");
        presenter.ReportLaunchFailure("retry"); host.Update();
        Press("offline_rules_open", RmlUiIntentKind.OfflineOpenRules);
        Check(manager.Back(), "shared Back closes rules modal"); presenter.Refresh();
        Check(!controller.Snapshot().RulesOpen, "Back discards controller rule draft");
        host.TryGetElementBounds(page, "offline_training_start", out _, out var oldLowerY, out _, out var lowerHeight);
        if (oldLowerY + lowerHeight > pageY + pageH)
        {
            host.Input.PointerMoved(viewport.Item1 * .8, pageY + pageH / 2); host.Input.PointerWheel(-20); host.Update();
            Check(host.TryGetElementBounds(page, "offline_training_start", out _, out var newLowerY, out _, out _) && newLowerY < oldLowerY,
                "actual pointer wheel scrolls tall content within the shell");
        }
        presenter.Dispose();
        Check(!presenter.FocusTraining(), "Change Drill rejects retired Offline route");
        Check(!host.IsAlive(page) && manager.Page == default && !presenter.TryTakeLaunch(out _), "route retirement closes document and cancels launch ownership");
        host.Shutdown();
        }
        Directory.Delete(root, true); NativeLibrary.Free(module);
        Console.WriteLine($"Offline actual native integration: {count} assertions passed.");
    }
}

sealed class NativeBackend : IOfflineBackend
{
    public int Commits, Applies;
    public bool CanLaunch(out string reason) { reason = ""; return true; }
    public bool ValidateArena(string room, GameMode mode, int participants, out string reason) { reason = ""; return true; }
    public bool ValidateRules(MatchDefinition match, out string reason) => MphRead.Mods.Multiplayer.MatchModifierRules.Validate(match, out reason);
    public void CommitSettings(MenuSettings settings) => Commits++;
    public void ApplySettings(MenuSettings settings) => Applies++;
    public LaunchPlan CreateMatch(MenuSettings settings, string room, GameMode mode, Hunter hunter, int suit, int bots, int level) => new() { Kind = LaunchKind.Offline, RoomKey = room, Mode = mode, Hunter = hunter, Bots = bots, BotLevel = level };
    public LaunchPlan CreateTraining(AimTrainerDefinition training, Hunter hunter, int suit) => new() { Kind = LaunchKind.AimTrainer, Training = training, Hunter = hunter };
    public uint NewTrainingSeed() => 123;
}

#endif
