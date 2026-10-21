using System.Collections.Immutable;
using System.Reflection;
using MphRead;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.RmlUi.Core;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Multiplayer;
using MphRead.Mods.Network;

static class OfflineBotChecks
{
    static void Check(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException(label);
        Console.WriteLine("OFFLINE BOT PASS " + label);
    }

    public static void Run()
    {
        var draft = new RmlUiOfflineDraft(new[] { "MP1 SANCTORUS" }, Hunter.Samus, 0, 2, 1);
        var initial = draft.Current;
        Check(draft.HasBots && draft.Bot.Hunter == Hunter.Random, "random roster defaults");
        foreach (int choice in new[] { 17, 18, 19, 20, 22 })
        {
            var before = draft.Current;
            Check(draft.Change(choice) && draft.Change(choice, true)
                && draft.Current.BotRoster.SequenceEqual(before.BotRoster), "choice reverses " + choice);
            Check(RmlUiIntentRegistry.IsValid(RmlUiIntentKind.OfflineChoice, choice), "native intent admitted " + choice);
        }
        Check(draft.Change(18) && draft.Change(19) && draft.Change(20) && draft.Change(22)
            && draft.Current.BotRoster[1] == initial.BotRoster[1]
            && initial.BotRoster[0].Hunter == Hunter.Random, "edits isolate a bot and preserve captured launch request");
        draft.Change(17);
        Check(draft.Bot == initial.BotRoster[1], "selection reads the second bot");
        draft.Change(4);
        Check(draft.Current.BotRoster.All(bot => bot.Level == draft.Current.BotLevel), "shared difficulty resets roster");
        Check(!draft.Change(21) && !draft.Change(23), "team edits unavailable in FFA");
        draft.Change(2);
        Check(draft.Teams && draft.Change(21) && draft.Change(23)
            && draft.Bot.Team == 0 && draft.Current.LocalTeam == 1, "team match admits explicit assignments");
        draft.SetLowTier(true);
        for (int i = 0; i < 12; i++)
        {
            draft.Change(18);
            Check(draft.Bot.Hunter == Hunter.Random || HunterRules.Allowed(draft.Bot.Hunter, true), "low-tier hunter pool");
        }
        while (draft.Current.Bots > 0) draft.Change(3, true);
        Check(!draft.Change(18) && !draft.Change(17), "empty roster cannot edit phantom bots");
        draft.Change(3);
        Check(draft.SelectedBot == 0 && draft.Valid, "restored bot selection stays in bounds");

        var roster = ImmutableArray.Create(new OfflineBotSettings(Hunter.Trace, 3, 2, 1, 40),
            new OfflineBotSettings(Hunter.Spire, 2, 0, 0, 10));
        var settings = new MenuSettings();
        var plan = OfflineLaunch.Preview(settings, "MP1 SANCTORUS", GameMode.BattleTeams,
            Hunter.Samus, 0, 2, 1, botRoster: roster, localTeam: 1);
        Check(plan.BotRoster.SequenceEqual(roster) && plan.LocalTeam == 1, "launch carries complete roster");
        foreach (var invalid in new[] { roster.SetItem(0, roster[0] with { Suit = 4 }),
            roster.SetItem(0, roster[0] with { Hunter = (Hunter)99 }),
            roster.SetItem(0, roster[0] with { Handicap = 51 }),
            roster.SetItem(0, roster[0] with { Level = -1 }),
            roster.SetItem(0, roster[0] with { Team = 2 }), roster.RemoveAt(0) })
        {
            bool rejected = false;
            try { OfflineLaunch.Preview(settings, "MP1 SANCTORUS", GameMode.Battle, Hunter.Samus,
                0, 2, 1, botRoster: invalid); }
            catch (ArgumentException) { rejected = true; }
            Check(rejected, "invalid bot settings rejected before persistence");
        }

        var scene = new Scene(default, null!, null!, _ => { }, () => { }, initializeRuntime: false);
        SeedTurrets(scene);
        var populate = typeof(MatchStart).GetMethod("AddLocalPlayers", BindingFlags.NonPublic | BindingFlags.Static)!;
        populate.Invoke(null, new object[] { scene, plan, true });
        Check(scene.Players.PlayerCount == 3 && scene.Players.Items[1].Hunter == Hunter.Trace
            && scene.Players.Items[1].Recolor == 3 && scene.Players.Items[1].BotLevel == 2
            && scene.Players.Items[1].TeamIndex == 1 && NetSession.SlotDamageReduction[1] == 40
            && scene.Players.Items[2].Hunter == Hunter.Spire && scene.Players.Items[2].BotLevel == 0
            && scene.Players.Items[2].TeamIndex == 0 && scene.Players.Items[0].TeamIndex == 1,
            "actual offline population applies both bot profiles and player team");
        var nextScene = new Scene(default, null!, null!, _ => { }, () => { }, initializeRuntime: false);
        SeedTurrets(nextScene);
        populate.Invoke(null, new object[] { nextScene,
            new LaunchPlan { Hunter = Hunter.Samus, Bots = 0 }, false });
        Check(NetSession.SlotDamageReduction.All(value => value == 0), "next match clears previous handicaps");
    }

    static void SeedTurrets(Scene scene)
    {
        // Keep this CPU check independent of cartridge models. Population is
        // real; precreated turret holders bypass only their model-loading step.
        var field = typeof(MphRead.Entities.PlayerEntity).GetField("_halfturret", BindingFlags.NonPublic | BindingFlags.Instance)!;
        foreach (var player in scene.Players.Items)
            field.SetValue(player, new MphRead.Entities.HalfturretEntity(player, scene));
    }
}
