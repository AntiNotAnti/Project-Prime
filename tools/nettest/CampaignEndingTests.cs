using System;
using System.IO;
using System.Reflection;
using MphRead.Mods.Campaign;
using MphRead.Mods.Launcher;

namespace MphRead.NetTest;

internal static class CampaignEndingTests
{
    internal static int Run()
    {
        string previousDirectory = Directory.GetCurrentDirectory();
        byte previousSlot = Menu.SaveSlot;
        SaveWhen previousRequest = Menu.NeededSave;
        string root = Path.Combine(Path.GetTempPath(),
            "prime-camp04-endings-" + Guid.NewGuid().ToString("N"));
        int count = 0;
        void Check(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException("CAMP-04 ENDING FAIL " + label);
            count++;
            Console.WriteLine("CAMP-04 ENDING PASS " + label);
        }
        SceneGameState CreateState(StorySave save, int roomId, CampaignDeparture kind)
        {
            var state = new SceneGameState(new ScenePlayerRegistry())
            {
                LastCampaignDeparture = kind,
                LastCampaignDepartureRoomId = roomId
            };
            typeof(SceneGameState).GetProperty(nameof(SceneGameState.StorySave),
                BindingFlags.Public | BindingFlags.Instance)!.SetValue(state, save);
            return state;
        }
        try
        {
            Directory.CreateDirectory(root);
            Directory.SetCurrentDirectory(root);
            StorySave story = PostMatchSaveTests.Fixture();
            story.CheckpointRoomId = 91;
            story.FoundOctoliths = 0xFF;
            var bad = CampaignEndingSummary.Capture(1, 91, story, _ => 73);
            var good = CampaignEndingSummary.Capture(1, 92, story, _ => 89);
            Check(bad is { Kind: CampaignEndingKind.Incomplete, CompletionPercent: 73 },
                "Gorea 1 generates original incomplete ending variant");
            Check(good is { Kind: CampaignEndingKind.Complete, CompletionPercent: 89 },
                "Gorea 2 generates complete ending variant");
            Check(good?.Octoliths == 8 && good?.Deaths == 13 && good?.HunterKills == 11,
                "ending result carries immutable saved gameplay facts");
            Check(CampaignEndingSummary.Capture(1, 28, story) == null
                && CampaignEndingSummary.Capture(0, 92, story) == null
                && CampaignEndingSummary.Capture(1, 92, null) == null,
                "non-endings, invalid slot, and no save cannot trigger credits");
            var uncertain = CampaignEndingSummary.Capture(1, 92, story,
                _ => throw new IOException("unavailable logbook"));
            Check(uncertain?.CompletionPercent == null,
                "missing completion assets never fabricate a percentage");
            Check(CampaignEndingSummary.Capture(1, 92, story, _ => 105)?.CompletionPercent == null,
                "out-of-bounds completion values are refused");

            Menu.SaveSlot = 1;
            Menu.NeededSave = SaveWhen.Always;
            MatchStart.ClearRecentCampaignEnding();
            var pending = MatchStart.PrepareAfterMatch(
                CreateState(story, 92, CampaignDeparture.Ending));
            Check(MatchStart.RecentCampaignEnding == null && pending.Ending is
                { Kind: CampaignEndingKind.Complete, SaveCommitted: false },
                "ending visible only as a value snapshot before commit");
            MatchStart.AfterMatch(pending);
            Check(MatchStart.RecentCampaignEnding is
                { Kind: CampaignEndingKind.Complete, Slot: 1, SaveCommitted: true }
                && File.Exists(Path.Combine("Savedata", "save001.json")),
                "successful native save publishes complete ending result");
            Check(MatchStart.RecentShipDock == null,
                "a Gorea ending never exposes the ship fast-travel ticket");

            Menu.NeededSave = SaveWhen.Never;
            var unsaved = MatchStart.PrepareAfterMatch(
                CreateState(story, 91, CampaignDeparture.Ending));
            MatchStart.AfterMatch(unsaved);
            Check(MatchStart.RecentCampaignEnding is
                { Kind: CampaignEndingKind.Incomplete, SaveCommitted: false },
                "Never save policy still presents the ending without claiming a save");

            Menu.NeededSave = SaveWhen.Never;
            var ordinary = MatchStart.PrepareAfterMatch(
                CreateState(story, 45, CampaignDeparture.Quit));
            MatchStart.AfterMatch(ordinary);
            Check(MatchStart.RecentCampaignEnding == null,
                "ordinary quit cannot re-show a prior ending");

            Menu.NeededSave = SaveWhen.Never;
            var dock = MatchStart.PrepareAfterMatch(
                CreateState(story, 45, CampaignDeparture.ReturnedToShip));
            MatchStart.AfterMatch(dock);
            Check(MatchStart.RecentCampaignEnding == null,
                "gunship entry never misclassified as Gorea credits");
            Console.WriteLine($"CAMP-04 ENDING PASS total={count}: native save handoff, no assets/GL");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine("CAMP-04 ENDING FAIL " + ex);
            return 1;
        }
        finally
        {
            MatchStart.ClearRecentCampaignEnding();
            MatchStart.ClearRecentShipDock();
            Menu.SaveSlot = previousSlot;
            Menu.NeededSave = previousRequest;
            Directory.SetCurrentDirectory(previousDirectory);
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
