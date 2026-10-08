#if MPHREAD_RMLUI_POC
using System;
using System.Collections.Generic;
using System.Linq;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Training;

namespace MphRead.Mods.Launcher.RmlUi.Pages.Offline;

/// <summary>Document bindings and explicit input submission; OfflineController owns all gameplay decisions.</summary>
public sealed class OfflinePagePresenter : IDisposable
{
    private static readonly string[] RuleNames =
    {
        "Octolith auto reset", "Friendly fire", "Affinity weapons", "Enhanced Hunters", "Shadow freeze",
        "Spawn protection (3s)", "Fiesta", "Insta-Gib", "Low Tier", "No Imp", "Balanced Mode", "Hunter radar"
    };
    private readonly RmlUiHost _host;
    private readonly RmlUiPageManager _pages;
    private readonly OfflineController _controller;
    private RmlUiDocumentToken _page, _rules, _training, _arena;
    private long _presentedRevision = -1;
    private long _bindingRevision;
    private string _arenaSearch = "";
    private int _arenaOffset, _arenaCount;
    private OfflineArenaSnapshot[] _arenaRows = Array.Empty<OfflineArenaSnapshot>();
    private bool _disposed, _trainingOnly;
    public RmlUiDocumentToken Document => _page;
    public OfflineController Controller => _controller;

    public OfflinePagePresenter(RmlUiHost host, RmlUiPageManager pages, OfflineController controller)
        => (_host, _pages, _controller) = (host, pages, controller);

    public void Open()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _page = _pages.OpenPage(new("offline", "pages/offline/setup.rml", "offline_arena"));
        _presentedRevision = -1;
        Refresh();
        PresentActivity();
    }

    private void PresentActivity()
    {
        _host.SetBool(_page,"visible:offline_match_panel",!_trainingOnly);
        _host.SetBool(_page,"visible:offline_training_panel",_trainingOnly);
        _host.SetText(_page,"offline_heading",_trainingOnly?"AIM LAB":"OFFLINE BATTLE");
        _host.SetText(_page,"offline_description",_trainingOnly?"Choose a drill and refine your aim and movement.":"Build a local match with bots and custom rules.");
    }

    public bool FocusTraining()
    {
        if (_disposed || _page == default || _pages.Page != _page || _pages.Top != _page) return false;
        _trainingOnly = true;
        PresentActivity();
        _host.Update();
        return _host.FocusDocument(_page, "offline_training_start");
    }

    public bool HandleAction(in RmlUiIntent intent)
    {
        if (_disposed || _page == default || _pages.Page != _page || !Owns(intent.Kind) || !_pages.Accept(intent)) return false;
        switch (intent.Kind)
        {
            case RmlUiIntentKind.OfflineChoice: _controller.Cycle((OfflineChoice)intent.Argument); break;
            case RmlUiIntentKind.OfflineRuleToggle: _controller.ToggleRule((OfflineRuleToggle)intent.Argument); break;
            case RmlUiIntentKind.OfflineTrainingToggle: _controller.ToggleTraining((OfflineTrainingToggle)intent.Argument); break;
            case RmlUiIntentKind.OfflineDamage: _controller.CycleDamage(); break;
            case RmlUiIntentKind.OfflineLaunchMatch: _controller.RequestMatch(); break;
            case RmlUiIntentKind.OfflineLaunchTraining: _controller.RequestTraining(); break;
            case RmlUiIntentKind.OfflineOpenArena:
                _arenaOffset = 0; _arenaSearch = "";
                _arena = _pages.OpenModal(new("offline-arena", "pages/offline/arena.rml", "offline_arena_search"));
                _host.SetField(_arena, "offline_arena_search", "");
                _presentedRevision = -1;
                break;
            case RmlUiIntentKind.OfflineSelectArena:
                if (_arena != default && intent.Argument >= 0 && intent.Argument < _arenaRows.Length
                    && _controller.SelectArena(_arenaRows[intent.Argument].Key).Accepted && _pages.CloseModal()) _arena = default;
                break;
            case RmlUiIntentKind.OfflineArenaPage:
                _arenaOffset = Math.Clamp(_arenaOffset + intent.Argument * 16, 0, Math.Max(0, (_arenaCount - 1) / 16 * 16));
                _presentedRevision = -1;
                break;
            case RmlUiIntentKind.OfflineArenaSearch:
                if (_arena != default) _arenaSearch = _host.ReadField(_arena, "offline_arena_search").Trim();
                _arenaOffset = 0; _presentedRevision = -1;
                break;
            case RmlUiIntentKind.OfflineCancelArena:
                if (_pages.CloseModal()) _arena = default;
                break;
            case RmlUiIntentKind.OfflineOpenRules:
                _controller.OpenRules();
                try
                {
                    OfflineSnapshot snapshot = _controller.Snapshot();
                    string focus = snapshot.TimeGoalVisible ? "offline_time_goal" : snapshot.PointGoalVisible ? "offline_point_goal"
                        : snapshot.TimeLimitVisible ? "offline_time_limit" : "offline_rules_apply";
                    _rules = _pages.OpenModal(new("offline-rules", "pages/offline/rules.rml", focus));
                    OfflineRulesDraft draft = snapshot.Rules;
                    _host.SetField(_rules, "offline_point_goal", draft.PointGoal);
                    _host.SetField(_rules, "offline_time_limit", draft.TimeLimit);
                    _host.SetField(_rules, "offline_time_goal", draft.TimeGoal);
                }
                catch { _controller.CancelRules(); throw; }
                break;
            case RmlUiIntentKind.OfflineCancelRules:
                if (_pages.CloseModal()) { _rules = default; _controller.CancelRules(); }
                break;
            case RmlUiIntentKind.OfflineApplyRules:
                if (_rules != default)
                {
                    _controller.SetRuleFields(_host.ReadField(_rules, "offline_point_goal"),
                        _host.ReadField(_rules, "offline_time_limit"), _host.ReadField(_rules, "offline_time_goal"));
                    if (_controller.ApplyRules().Accepted && _pages.CloseModal()) _rules = default;
                }
                break;
            case RmlUiIntentKind.OfflineOpenTrainingOptions:
                _controller.OpenTrainingOptions();
                try { _training = _pages.OpenModal(new("offline-training", "pages/offline/training.rml", "offline_training_difficulty")); }
                catch { _controller.CloseTrainingOptions(); throw; }
                break;
            case RmlUiIntentKind.OfflineCloseTrainingOptions:
                if (_pages.CloseModal()) { _training = default; _controller.CloseTrainingOptions(); }
                break;
        }
        Refresh();
        return true;
    }

    public void Refresh()
    {
        if (_disposed || _page == default || !_host.IsAlive(_page) || _pages.Page != _page) return;
        // Back and page/device retirement can dismiss a sheet outside its own button.
        if (_rules != default && !_host.IsAlive(_rules)) { _rules = default; _controller.CancelRules(); }
        if (_training != default && !_host.IsAlive(_training)) { _training = default; _controller.CloseTrainingOptions(); }
        if (_arena != default && !_host.IsAlive(_arena)) _arena = default;
        _controller.Refresh();
        OfflineSnapshot s = _controller.Snapshot();
        if (_presentedRevision == s.Revision) return;
        long revision = ++_bindingRevision;
        var bindings = new Dictionary<string, RmlUiBindingValue>();
        Text(bindings, "offline_arena_value", s.ArenaLabel);
        Text(bindings, "offline_mode_value", s.GameTypeLabel);
        Text(bindings, "offline_matchup_value", s.MatchupLabel);
        Text(bindings, "offline_bots_value", s.Bots + " bots");
        Text(bindings, "offline_skill_value", s.BotDifficultyLabel);
        Text(bindings, "offline_hunter_value", s.Hunter.ToString());
        Text(bindings, "offline_suit_value", (s.Suit + 1).ToString());
        Text(bindings, "offline_summary", s.GameTypeLabel + " // " + s.MatchupLabel + " // " + s.Bots + " bots // " + s.ArenaLabel);
        Text(bindings, "offline_availability", s.Availability);
        Text(bindings, "offline_arena_error", s.ArenaError);
        Text(bindings, "offline_error", s.Error);
        Bool(bindings, "disabled:offline_start", !s.CanLaunchMatch);
        Bool(bindings, "disabled:offline_training_start", !s.CanLaunchTraining);
        Bool(bindings, "disabled:offline_matchup", !s.MatchupEditable);
        Bool(bindings, "disabled:offline_arena", s.Arenas.IsEmpty);
        Text(bindings, "offline_training_hunter_value", s.TrainingHunter.ToString());
        Text(bindings, "offline_drill_value", Display(s.Training.Drill));
        Text(bindings, "offline_weapon_value", Display(s.Training.Weapon));
        Text(bindings, "offline_duration_value", s.Training.DurationSeconds + " seconds");
        Text(bindings, "offline_targets_value", s.Training.TargetCount.ToString());
        Text(bindings, "offline_movement_value", Display(s.Training.Movement));
        Bool(bindings, "disabled:offline_weapon", s.Training.Drill == AimTrainerDrill.ImperialistPrecision);
        Bool(bindings, "disabled:offline_targets", s.Training.Drill == AimTrainerDrill.TimedFlick);
        _pages.Present(_page, revision, bindings);
        if (_rules != default && _host.IsAlive(_rules)) PresentRules(s, revision);
        if (_training != default && _host.IsAlive(_training)) PresentTraining(s, revision);
        if (_arena != default && _host.IsAlive(_arena)) PresentArena(s, revision);
        _presentedRevision = s.Revision;
    }

    public bool TryTakeLaunch(out LaunchPlan plan)
    {
        if (_disposed || _page == default || !_host.IsAlive(_page) || _pages.Page != _page)
        {
            if (!_disposed) _controller.CancelPendingLaunch();
            plan = default; return false;
        }
        return _controller.TryTakeLaunch(out plan);
    }
    public void ReportLaunchFailure(string reason) { _controller.ReportLaunchFailure(reason); Refresh(); }
    public void Dispose()
    {
        if (_disposed) return;
        if (_pages.Page == _page && !_pages.ClosePage()) throw new InvalidOperationException("The Offline page could not be closed.");
        _controller.CancelPendingLaunch();
        _controller.CancelRules();
        _controller.CloseTrainingOptions();
        _page = _rules = _training = _arena = default;
        _disposed = true;
    }

    private void PresentRules(OfflineSnapshot s, long revision)
    {
        var b = new Dictionary<string, RmlUiBindingValue>();
        OfflineRulesDraft r = s.Rules;
        bool[] values = { r.AutoReset, r.FriendlyFire, r.AffinityWeapons, r.EnhancedHunters, r.ShadowFreeze,
            r.SpawnProtection, r.Fiesta, r.InstaGib, r.LowTier, r.NoImperialist, r.BalancedMode, r.HunterRadar };
        for (int i = 0; i < values.Length; i++) Text(b, "offline_rule_" + i, RuleNames[i] + ": " + On(values[i]));
        Text(b, "offline_damage_value", "Damage: " + r.DamageLevel);
        Text(b, "offline_rules_error", s.Error);
        Bool(b, "visible:offline_point_goal_row", s.PointGoalVisible);
        Bool(b, "visible:offline_time_limit_row", s.TimeLimitVisible);
        Bool(b, "visible:offline_time_goal_row", s.TimeGoalVisible);
        Bool(b, "visible:offline_rule_0", s.AutoResetVisible);
        Bool(b, "visible:offline_rule_1", s.FriendlyFireVisible);
        Bool(b, "visible:offline_chamber_help", s.Mode == GameMode.OneInTheChamber);
        _pages.Present(_rules, revision, b);
    }

    private void PresentTraining(OfflineSnapshot s, long revision)
    {
        var b = new Dictionary<string, RmlUiBindingValue>();
        var t = s.Training;
        Text(b, "offline_training_difficulty_value", t.Difficulty.ToString());
        Text(b, "offline_training_distance_value", t.Distance.ToString());
        Text(b, "offline_training_scope_value", t.Scope.ToString());
        Text(b, "offline_training_toggle_0", "Headshots only: " + On(t.HeadshotsOnly));
        Text(b, "offline_training_toggle_1", "Infinite ammo: " + On(t.InfiniteAmmo));
        Text(b, "offline_training_toggle_2", "Refill ammo on hit: " + On(t.ReloadOnHit));
        Text(b, "offline_training_toggle_3", "Imperialist click skips reload: " + On(!t.NormalImperialistReload));
        Text(b, "offline_training_toggle_4", "Fixed seed: " + On(t.FixedSeed));
        Bool(b, "disabled:offline_training_toggle_0", t.Drill == AimTrainerDrill.HeadshotPrecision);
        _pages.Present(_training, revision, b);
    }

    private void PresentArena(OfflineSnapshot s, long revision)
    {
        var b = new Dictionary<string, RmlUiBindingValue>();
        var filtered = s.Arenas.Where(a => _arenaSearch.Length == 0 || a.Label.Contains(_arenaSearch, StringComparison.OrdinalIgnoreCase)
            || a.Key.Contains(_arenaSearch, StringComparison.OrdinalIgnoreCase)).ToArray();
        _arenaCount = filtered.Length;
        _arenaOffset = Math.Min(_arenaOffset, Math.Max(0, (_arenaCount - 1) / 16 * 16));
        _arenaRows = filtered.Skip(_arenaOffset).Take(16).ToArray();
        Text(b, "offline_arena_page", _arenaCount == 0 ? "No arenas match this search."
            : $"{_arenaOffset + 1}–{_arenaOffset + _arenaRows.Length} of {_arenaCount} arenas // {s.GameTypeLabel} // {s.Bots + 1} combatants");
        Text(b, "offline_arena_picker_error", s.Error);
        for (int i = 0; i < 16; i++)
        {
            bool exists = i < _arenaRows.Length;
            Bool(b, "visible:offline_arena_row_" + i, exists);
            Bool(b, "disabled:offline_arena_pick_" + i, !exists || !_arenaRows[i].Compatible);
            Text(b, "offline_arena_name_" + i, exists ? _arenaRows[i].Label : "");
            Text(b, "offline_arena_reason_" + i, exists ? _arenaRows[i].Reason : "");
        }
        Bool(b, "disabled:offline_arena_previous", _arenaOffset == 0);
        Bool(b, "disabled:offline_arena_next", _arenaOffset + 16 >= _arenaCount);
        _pages.Present(_arena, revision, b);
    }

    private static string Display<T>(T value) where T : struct, Enum => TrainingLabels.Display(value.ToString());
    private static string On(bool value) => value ? "On" : "Off";
    private static void Text(Dictionary<string, RmlUiBindingValue> b, string id, string value) => b.Add(id, RmlUiBindingValue.FromText(value));
    private static void Bool(Dictionary<string, RmlUiBindingValue> b, string id, bool value) => b.Add(id, RmlUiBindingValue.FromBoolean(value));
    private static bool Owns(RmlUiIntentKind kind) => kind is RmlUiIntentKind.OfflineChoice or RmlUiIntentKind.OfflineRuleToggle
        or RmlUiIntentKind.OfflineTrainingToggle or RmlUiIntentKind.OfflineDamage or RmlUiIntentKind.OfflineLaunchMatch
        or RmlUiIntentKind.OfflineOpenRules or RmlUiIntentKind.OfflineCancelRules or RmlUiIntentKind.OfflineApplyRules
        or RmlUiIntentKind.OfflineLaunchTraining or RmlUiIntentKind.OfflineOpenTrainingOptions or RmlUiIntentKind.OfflineCloseTrainingOptions
        or RmlUiIntentKind.OfflineOpenArena or RmlUiIntentKind.OfflineSelectArena or RmlUiIntentKind.OfflineArenaPage
        or RmlUiIntentKind.OfflineArenaSearch or RmlUiIntentKind.OfflineCancelArena;
}
#endif
