using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.RmlUi.Core;
using MphRead.Mods.Launcher.RmlUi.Host;

internal static class HomeRailAcceptance
{
    internal static void Run()
    {
        var services = new Stub();
        using var controller = new RmlUiMenuController(services);
        var empty = controller.Tick();
        var projected = RmlUiMenuPageBindings.Project(RmlUiMenuPage.Home, empty.Bindings);
        Check(projected["visible:home_social_rail"].Boolean && projected["visible:home_social_empty"].Boolean
            && !projected["visible:home_friend0"].Boolean
            && projected["home_social_empty"].Text.Contains("UNAVAILABLE", StringComparison.Ordinal),
            "Friends panel remains visible with honest unavailable state and no stale people");
        var news = new BundledNewsProvider().Read().Skip(1).Take(2).ToArray();
        for (int i = 0; i < 2; i++)
            Check(projected["visible:home_news" + i].Boolean
                && projected["home_news" + i + "_title"].Text == news[i].Title
                && projected["home_news" + i + "_category"].Text == news[i].Category,
                "additional Community Updates line uses existing authored news source " + i);
        services.SocialOverride = new(true, true, Array.Empty<string>(), 0);
        controller.Dispatch(RmlUiIntentKind.PlayCancel);
        var noFriends = controller.Tick();
        Check(noFriends.Bindings["home_social_rail_visible"].Boolean && noFriends.Bindings["home_social_empty_visible"].Boolean
            && noFriends.Bindings["home_social_empty_text"].Text.StartsWith("NO FRIENDS YET", StringComparison.Ordinal),
            "verified empty Friends explains how to find people instead of hiding the section");
        services.SocialOverride = new(true, true, new[] { "Actual Friend" }, 2)
        {
            PresenceVerified = true, SharingPresence = 1,
            FriendActivities = new[] { "IN MATCH" }, FriendJoinable = new[] { false },
            InvitationsVerified = true, IncomingInvitations = 3
        };
        controller.Dispatch(RmlUiIntentKind.PlayCancel);
        var populated = controller.Tick().Bindings;
        Check(populated["home_friend0_visible"].Boolean && populated["home_friend0_name"].Text == "Actual Friend"
            && populated["home_friend0_activity"].Text == "IN MATCH" && populated["home_invites_count"].Text == "3"
            && populated["home_requests_count"].Text == "2" && populated["home_party_count"].Text == "1 / 4",
            "Friends shows verified presence and request/invite counts independently from four party seats");
        services.SocialOverride = services.SocialOverride with { Connected = false };
        controller.Dispatch(RmlUiIntentKind.PlayCancel);
        var disconnected = controller.Tick().Bindings;
        Check(disconnected["home_social_rail_visible"].Boolean && !disconnected["home_friend0_visible"].Boolean
            && disconnected["home_friend0_name"].Text == "" && disconnected["home_invites_count"].Text == "NOT SYNCED",
            "disconnect clears previous friend data while keeping accessible Social entry");
    }
    private static void Check(bool pass, string label)
    { if (!pass) throw new InvalidOperationException(label); Console.WriteLine("HOME RAIL PASS " + label); }
}
