#if !MPHREAD_SERVER
using System;
using System.Collections.Generic;
using System.Globalization;
using MphRead.Mods.Launcher.RmlUi.Host;

namespace MphRead.Mods.Launcher.RmlUi.Pages.License;

/// <summary>Testable text/visibility projection shared by the native page and acceptance fixture.</summary>
public static class NativeLicenseOverviewBindings
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public static IReadOnlyDictionary<string, RmlUiBindingValue> Create(
        NativeLicenseOverviewController.Snapshot state, int cardTab = 0)
    {
        var data = new Dictionary<string, RmlUiBindingValue>(StringComparer.Ordinal);
        void Text(string id, string value) => data[id] = RmlUiBindingValue.FromText(value);
        void Bool(string id, bool value) => data[id] = RmlUiBindingValue.FromBoolean(value);
        NativeLicenseOverview o = state.Overview;
        NativeLicenseCareerStats? s = o.Career;
        string Amount(long count) => o.Verified ? count.ToString("N0", Invariant) : "NOT VERIFIED";
        string Detail(long? count) => o.Verified && count.HasValue ? Amount(count.Value) : "NOT VERIFIED";
        cardTab = Math.Clamp(cardTab, 0, 4);
        Text("native_license_points", o.Verified ? o.RatingPoints.ToString("N0", Invariant) : "UNVERIFIED");
        string Card(long? value) => !o.Verified ? "SYNC REQUIRED"
            : value.HasValue ? Amount(value.Value) : "UNAVAILABLE";
        string Metric(string value) => o.Verified ? value : "SYNC REQUIRED";
        var rows = cardTab switch
        {
            1 => new[] { ("KILLS", Card(o.Kills)), ("DEATHS", Card(o.Deaths)), ("ASSISTS", Card(o.Assists)) },
            2 => new[] { ("HEADSHOT KILLS", Card(s?.Headshots)), ("BEST KILL STREAK", Card(s?.LongestKillStreak)), ("K/D RATIO", Metric(Ratio(o.Kills, o.Deaths))) },
            3 => new[] { ("OCTOLITH SCORES", Card(s?.OctolithScores)), ("NODES CAPTURED", Card(s?.NodesCaptured)), ("KILLS AS PRIME", Card(s?.KillsAsPrime)) },
            4 => new[] { ("PLAY TIME", !o.Verified ? "SYNC REQUIRED" : s == null ? "UNAVAILABLE" : Duration(s.PlayedTicks)),
                ("AVERAGE MATCH", !o.Verified ? "SYNC REQUIRED" : s == null ? "UNAVAILABLE" : Duration(o.MatchesPlayed == 0 ? 0 : s.PlayedTicks / o.MatchesPlayed)),
                ("TOTAL MATCHES", Card(o.MatchesPlayed)) },
            _ => new[] { ("WINS", Card(o.Wins)), ("MATCHES", Card(o.MatchesPlayed)), ("WIN RATIO", Metric(Percent(o.Wins, o.MatchesPlayed))) }
        };
        for (int i = 0; i < 3; i++)
        {
            Text("native_license_card_title" + i, rows[i].Item1);
            Text("native_license_card_value" + i, rows[i].Item2);
        }
        for (int i = 0; i < 5; i++) Bool("class:native_license_card_tab" + i + ":selected", i == cardTab);
        Text("native_license_status", state.Status);
        Text("native_license_name", o.Verified ? o.DisplayName : "LOCAL HUNTER");
        Text("native_license_hunter", "FAVORITE HUNTER // " + (o.Verified ? o.FavoriteHunter : "UNKNOWN"));
        Text("native_license_visibility", "PUBLIC VISIBILITY // " + (o.Verified ? o.PublicVisibility : "UNKNOWN"));
        Text("native_license_hide_profile", o.Verified && o.PublicVisibility == "PRIVATE"
            ? "SAVE PRIVATE PREFERENCE" : "HIDE PUBLIC PROFILE");
        Bool("disabled:native_license_hide_profile", !state.CanHideProfile);
        Bool("disabled:native_license_show_profile", state.Busy || !o.Verified
            || o.PrivacyLease <= 0 || o.PublicVisibility != "PRIVATE");
        Bool("visible:native_license_publish_confirmation", state.ConfirmPublic);
        Text("native_license_privacy_status", state.SavingPrivacy
            ? state.Status
            : state.Status.StartsWith("PRIVATE PREFERENCE VERIFIED", StringComparison.Ordinal)
                || state.Status.StartsWith("PUBLIC PREFERENCE VERIFIED", StringComparison.Ordinal)
                ? state.Status
            : !o.Verified || o.PublicVisibility is not ("PUBLIC" or "PRIVATE")
                ? "VISIBILITY NOT VERIFIED // SYNC LICENSE"
                : o.PublicVisibility == "PRIVATE"
                    ? "CURRENT STATUS // PRIVATE — SAVE TO PERSIST YOUR OPT-OUT"
                    : "CURRENT STATUS // PUBLIC — YOUR HUNTER NAME AND CAREER ARE VISIBLE");
        bool ranked = o.Verified && HunterLicenseStarRank.TryCreate(o.RatingPoints, o.RatingTierNumber, out _);
        HunterLicenseStarRank.TryCreate(o.RatingPoints, o.RatingTierNumber, out var rank);
        int displayTier = DisplayStarTier(o);
        bool startingStar = displayTier == 1 && !ranked;
        Text("native_license_rank", !o.Verified ? "RANK // UNVERIFIED" : ranked
            ? $"{rank.Title} // {rank.Tier} {(rank.Tier == 1 ? "STAR" : "STARS")}" : startingStar ? "BOUNTY HUNTER // 1 STARTING STAR" : o.RatingTierNumber == null ? "RANK // UNRANKED" : "RANK // UNAVAILABLE");
        Text("native_license_rank_progress", !o.Verified ? "SYNC LICENSE TO VERIFY STAR RANK"
            : ranked ? rank.NextRankPoints is int next
                ? $"NEXT // {rank.NextTitle} · {next.ToString("N0", Invariant)} RP · {rank.PointsToNextRank!.Value.ToString("N0", Invariant)} TO GO"
                : "MAX RANK // LEGENDARY HUNTER"
            : startingStar ? "UNRANKED // NO ELIGIBLE RANKED RESULT · STARTING STAR"
            : o.RatingTierNumber == null ? "COMPLETE AN ELIGIBLE RANKED MATCH TO EARN YOUR STAR RANK"
            : "RANK DATA UNAVAILABLE // SYNC LICENSE");
        Bool("visible:native_license_star_row", displayTier > 0);
        Text("native_license_stars_fallback", displayTier > 0 ? String.Join(" ", new string('★', displayTier).ToCharArray()) : "");
        for (int i = 1; i <= 5; i++) Bool("visible:native_license_star" + i, i <= displayTier);
        string[] colors = { "red", "bronze", "silver", "gold", "black" };
        for (int i = 0; i < colors.Length; i++)
            Bool("class:native_license_star_row:rank-" + colors[i], displayTier == i + 1);
        Text("native_license_matches", Amount(o.MatchesPlayed));
        Text("native_license_wins", Amount(o.Wins));
        Text("native_license_losses", Amount(o.Losses));
        Text("native_license_kills", Amount(o.Kills));
        Text("native_license_deaths", Amount(o.Deaths));
        Text("native_license_assists", Amount(o.Assists));
        Text("native_license_win_rate", Metric(Percent(o.Wins, o.MatchesPlayed)));
        Text("native_license_kd", Metric(Ratio(o.Kills, o.Deaths)));
        Text("native_license_play_time", !o.Verified ? "SYNC REQUIRED" : s == null ? "UNAVAILABLE" : Duration(s.PlayedTicks));
        Text("native_license_ties", Card(s?.Ties));
        Text("native_license_recent", o.Verified
            ? "RECENT MATCHES RETURNED // " + o.RecentMatches.ToString(Invariant)
            : "RECENT MATCHES // NOT VERIFIED");

        foreach (NativeLicenseFace face in Enum.GetValues<NativeLicenseFace>())
        {
            string name = face.ToString().ToLowerInvariant();
            Bool("class:native_license_tab_" + name + ":selected", state.Face == face);
            Bool("visible:native_license_face_" + name, state.Face == face);
        }
        Bool("disabled:native_license_refresh", state.Busy);
        Bool("disabled:native_license_cancel_refresh", !state.Busy);
        NativeLicenseAccountInfo? account = o.Verified ? o.Account : null;
        bool accountReady = !state.Busy && account != null && o.PrivacyLease > 0;
        Text("native_license_account_state", account == null ? "ACCOUNT NOT VERIFIED // SYNC LICENSE"
            : account.Secure ? "LINKED SIGN-IN IDENTITY // VERIFY YOUR RECOVERY METHOD"
            : "GUEST LICENSE // LINK AN IDENTITY TO PRESERVE ACCESS");
        Text("native_license_account_methods", account == null ? "SIGN-IN METHODS // NOT VERIFIED"
            : "SIGN-IN METHODS // " + String.Join(" / ", new[]
            {
                account.EmailLinked ? "EMAIL LINKED" : "NO EMAIL LINKED",
                account.GoogleLinked ? "GOOGLE LINKED" : "GOOGLE UNLINKED",
                account.GithubLinked ? "GITHUB LINKED" : "GITHUB UNLINKED",
                account.DiscordLinked ? "DISCORD LINKED" : "DISCORD UNLINKED"
            }));
        foreach (string id in new[] { "email", "password", "confirmation", "send_verification" })
            Bool("disabled:native_license_" + id, !accountReady);
        Bool("disabled:native_license_finish_password", !accountReady || account?.EmailLinked != true || account?.Secure != true);
        Bool("disabled:native_license_recover", !accountReady || account?.Secure != false || o.MatchesPlayed != 0);
        Bool("disabled:native_license_link_google", !accountReady || account?.GoogleLinked == true);
        Bool("disabled:native_license_link_github", !accountReady || account?.GithubLinked == true);
        Bool("disabled:native_license_link_discord", !accountReady || account?.DiscordLinked == true);
        Text("native_license_recovery_hint", account?.Secure == true ? "USE YOUR LINKED IDENTITY ON ANOTHER DEVICE TO RECOVER THIS LICENSE."
            : o.Verified && o.MatchesPlayed > 0 ? "THIS GUEST HAS CAREER HISTORY. SECURE IT BEFORE SWITCHING IDENTITIES."
            : "RECOVERY SWITCHES AN EMPTY GUEST TO AN EXISTING SECURED LICENSE. BOTH PASSWORD FIELDS MUST MATCH.");
        Bool("disabled:native_license_previous_page", state.Busy || !o.Verified || state.Page == 0);
        Bool("disabled:native_license_next_page", state.Busy || !o.Verified || state.Page + 1 >= state.Pages);

        Text("native_license_metrics", String.Join("\n", new[]
        {
            "MATCHES // " + Amount(o.MatchesPlayed), "WINS // " + Amount(o.Wins),
            "LOSSES // " + Amount(o.Losses), "TIES // " + Detail(s?.Ties),
            "WIN RATE // " + (o.Verified ? Percent(o.Wins, o.MatchesPlayed) : "NOT VERIFIED"),
            "KILLS // " + Amount(o.Kills), "DEATHS // " + Amount(o.Deaths),
            "ASSISTS // " + Amount(o.Assists), "HEADSHOTS // " + Detail(s?.Headshots),
            "K/D // " + (o.Verified ? Ratio(o.Kills, o.Deaths) : "NOT VERIFIED"),
            "DAMAGE // " + Detail(s?.Damage), "BEST KILL STREAK // " + Detail(s?.LongestKillStreak),
            "CURRENT WIN STREAK // " + Detail(s?.CurrentWinStreak),
            "LONGEST WIN STREAK // " + Detail(s?.LongestWinStreak),
            "OCTOLITH SCORES // " + Detail(s?.OctolithScores),
            "NODES CAPTURED // " + Detail(s?.NodesCaptured), "KILLS AS PRIME // " + Detail(s?.KillsAsPrime),
            "PLAY TIME // " + (o.Verified && s != null ? Duration(s.PlayedTicks) : "NOT VERIFIED"),
            "AVG / MATCH // " + (o.Verified && s != null
                ? Duration(o.MatchesPlayed == 0 ? 0 : s.PlayedTicks / o.MatchesPlayed) : "NOT VERIFIED")
        }));

        Text("native_license_history_page", o.Verified
            ? $"PAGE {state.Page + 1} / {state.Pages} // {state.Total} RECENT MATCHES RETURNED (UP TO 25)"
            : "MATCH HISTORY // NOT VERIFIED");
        Text("native_license_history_empty", !o.Verified ? "SYNC YOUR LICENSE TO VERIFY MATCH HISTORY."
            : state.Total == 0 ? "NO ACCEPTED MATCHES RETURNED." : "");
        for (int i = 0; i < NativeLicenseOverviewController.PageSize; i++)
        {
            NativeLicenseMatch? row = o.Verified && i < state.Rows.Count ? state.Rows[i] : null;
            Bool("visible:native_license_match_" + i, row != null);
            string result = row == null ? "" : row.Tied ? "TIE" : row.Won ? "WIN" : "LOSS";
            Text("native_license_match_label_" + i, row == null ? ""
                : $"{result} // {row.Room} // {row.Mode}");
            Text("native_license_match_detail_" + i, row == null ? ""
                : $"{row.Kills:N0} K / {row.Deaths:N0} D / {row.Assists:N0} A // {row.Damage:N0} DAMAGE // "
                    + Duration(row.PlayedTicks) + " // " + (row.Eligible && row.CareerEligible ? "CAREER" : "UNRANKED")
                    + " // " + (row.PlayedAt?.ToUniversalTime().ToString("yyyy-MM-dd HH:mm 'UTC'", Invariant) ?? "DATE UNAVAILABLE"));
        }

        var milestones = new (string Name, string Goal, bool? Earned)[]
        {
            ("FIRST HUNT", "Complete 1 career match", o.MatchesPlayed >= 1),
            ("FIRST VICTORY", "Win 1 career match", o.Wins >= 1),
            ("CENTURION", "Reach 100 career kills", o.Kills >= 100),
            ("HEADHUNTER", "Land 50 career headshot kills", s == null ? null : s.Headshots >= 50),
            ("ACE", "Win 25 career matches", o.Wins >= 25),
            ("VETERAN", "Complete 50 career matches", o.MatchesPlayed >= 50),
            ("LEGACY", "Complete 250 career matches", o.MatchesPlayed >= 250)
        };
        for (int i = 0; i < milestones.Length; i++)
        {
            var m = milestones[i];
            Text("native_license_achievement_" + i, m.Name + " // " + m.Goal + " // "
                + (!o.Verified || !m.Earned.HasValue ? "NOT VERIFIED" : m.Earned.Value ? "UNLOCKED" : "LOCKED"));
        }
        double games = Math.Max(1, o.MatchesPlayed);
        string PerMatch(long value) => o.Verified ? (value / games).ToString("0.00", Invariant) : "NOT VERIFIED";
        Text("native_license_comparison_metrics", String.Join("\n", new[]
        {
            "KILLS / MATCH // " + PerMatch(o.Kills), "DEATHS / MATCH // " + PerMatch(o.Deaths),
            "ASSISTS / MATCH // " + PerMatch(o.Assists), "DAMAGE / MATCH // " + (s != null ? PerMatch(s.Damage) : "NOT VERIFIED"),
            "WIN RATE // " + (o.Verified ? Percent(o.Wins, o.MatchesPlayed) : "NOT VERIFIED")
        }));
        return data;
    }

    // Presentation only: preserve the absent server tier and zero RP for unrated accounts.
    // Contradictory numeric facts and unverified snapshots must never acquire a star.
    public static int DisplayStarTier(NativeLicenseOverview overview)
    {
        if (!overview.Verified) return 0;
        if (HunterLicenseStarRank.TryCreate(overview.RatingPoints, overview.RatingTierNumber, out var rank))
            return rank.Tier;
        return overview.RatingPoints == 0 && overview.RatingTierNumber == null ? 1 : 0;
    }

    private static string Percent(long value, long total)
        => total == 0 ? "0.0%" : ((double)value / total).ToString("P1", Invariant);

    private static string Ratio(long kills, long deaths)
        => deaths == 0 ? kills > 0 ? "∞" : "0.00" : ((double)kills / deaths).ToString("0.00", Invariant);

    // Integer arithmetic also handles large server counters without TimeSpan overflow.
    private static string Duration(long ticks)
    {
        long seconds = Math.Max(0, ticks) / 60;
        return seconds >= 3600 ? $"{seconds / 3600}h {seconds / 60 % 60:00}m"
            : $"{seconds / 60}m {seconds % 60:00}s";
    }
}
#endif
