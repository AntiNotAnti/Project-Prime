using System.Collections.Concurrent;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.RmlUi.Pages.License;

internal static class StarRankAcceptance
{
    internal static void Run()
    {
        int[] floors = { 0, 40, 140, 390, 750 };
        int[] ceilings = { 39, 139, 389, 749, 850 };
        string[] models = { "redstar", "bronzestar", "silverstar", "goldstar", "blackstar" };
        string[] colors = { "red", "bronze", "silver", "gold", "black" };
        string[] titles = { "BOUNTY HUNTER", "SUPER HUNTER", "ELITE HUNTER", "MASTER HUNTER", "LEGENDARY HUNTER" };
        for (int tier = 1; tier <= 5; tier++)
        foreach (int points in new[] { floors[tier - 1], ceilings[tier - 1] })
        {
            Check(HunterLicenseStarRank.TryCreate(points, tier, out var rank), "boundary accepted " + points);
            Check(rank.Tier == tier && rank.Model == models[tier - 1]
                && rank.Title == titles[tier - 1],
                "boundary retains canonical tier, model and uppercase title " + points);
            Check(rank.NextRankPoints == (tier < 5 ? floors[tier] : (int?)null)
                && rank.PointsToNextRank == (tier < 5 ? floors[tier] - points : (int?)null)
                && rank.NextTitle == (tier < 5 ? titles[tier] : "")
                && rank.ProgressPercent == (tier < 5 ? (points - floors[tier - 1]) * 100 / (floors[tier] - floors[tier - 1]) : 100),
                "next-rank values are bounded and top rank has no fabricated successor " + points);
            var bindings = NativeLicenseOverviewBindings.Create(Snapshot(Record(points, tier)));
            Check(bindings["native_license_rank"].Text == rank.Title + " // " + tier + (tier == 1 ? " STAR" : " STARS")
                && bindings["visible:native_license_star_row"].Boolean
                && bindings["native_license_rank_progress"].Text == (tier < 5
                    ? $"NEXT // {titles[tier]} · {floors[tier]:N0} RP · {floors[tier] - points:N0} TO GO"
                    : "MAX RANK // LEGENDARY HUNTER"),
                "verified numeric facts project rank and progress " + points);
            for (int star = 1; star <= 5; star++)
                Check(bindings["visible:native_license_star" + star].Boolean == (star <= tier),
                    "exact visible star count " + tier + "/" + star);
            foreach (string color in colors)
                Check(bindings["class:native_license_star_row:rank-" + color].Boolean == (color == colors[tier - 1]),
                    "exact rank class " + tier + "/" + color);
        }
        foreach (var invalid in new (int Points, int? Tier)[]
        { (-1,1), (851,5), (0,0), (850,6), (40,1), (39,2), (140,2), (390,3), (750,4), (750,null) })
        {
            Check(!HunterLicenseStarRank.TryCreate(invalid.Points, invalid.Tier, out _), "malformed or unrated helper fails closed " + invalid);
            AssertHidden(Record(invalid.Points, invalid.Tier), invalid.Tier == null
                ? "RANK // UNRANKED" : "RANK // UNAVAILABLE");
        }
        var unrated = Record(0, null) with { DisplayName = "Jarrett", MatchesPlayed = 89, Wins = 10 };
        var starting = NativeLicenseOverviewBindings.Create(Snapshot(unrated));
        Check(!HunterLicenseStarRank.TryCreate(unrated.RatingPoints, unrated.RatingTierNumber, out _)
            && unrated.RatingTierNumber == null && unrated.RatingPoints == 0,
            "starting presentation preserves null server tier and zero points without a ranked result");
        Check(NativeLicenseOverviewBindings.DisplayStarTier(unrated) == 1
            && starting["native_license_rank"].Text == "BOUNTY HUNTER // 1 STARTING STAR"
            && starting["native_license_rank_progress"].Text == "UNRANKED // NO ELIGIBLE RANKED RESULT · STARTING STAR"
            && starting["native_license_points"].Text == "0"
            && starting["native_license_matches"].Text == "89" && starting["native_license_wins"].Text == "10",
            "verified unrated career gets the original starting presentation and truthful server facts");
        Check(starting["visible:native_license_star_row"].Boolean
            && starting["native_license_stars_fallback"].Text == "★"
            && starting["class:native_license_star_row:rank-red"].Boolean
            && Enumerable.Range(1,5).All(star => starting["visible:native_license_star" + star].Boolean == (star == 1)),
            "starting license displays exactly one red star with readable fallback");
        Check(NativeLicenseOverviewBindings.DisplayStarTier(Record(0, 1)) == 1
            && NativeLicenseOverviewBindings.Create(Snapshot(Record(0, 1)))["native_license_rank"].Text == "BOUNTY HUNTER // 1 STAR",
            "server tier one retains earned rank wording distinct from starting presentation");
        AssertHidden(unrated with { Verified = false }, "RANK // UNVERIFIED");
        AssertHidden(Record(750,5) with { Verified = false }, "RANK // UNVERIFIED");
        RetiredAccount();
    }

    private static void RetiredAccount()
    {
        var reader = new Reader();
        using var controller = new NativeLicenseOverviewController(reader);
        controller.Refresh();
        if (!reader.Reads.TryTake(out var request, TimeSpan.FromSeconds(3)))
            throw new TimeoutException("Star-rank reader was not dispatched");
        request.SetResult(Record(750,5));
        bool loaded = false;
        for (int attempt = 0; attempt < 300; attempt++)
        {
            if (controller.Tick().Overview.Verified) { loaded = true; break; }
            Thread.Sleep(10);
        }
        Check(loaded && NativeLicenseOverviewBindings.Create(controller.Current())["visible:native_license_star5"].Boolean,
            "current account first displays its verified black stars");
        reader.Current = false;
        var retired = controller.Current();
        Check(!retired.Overview.Verified, "account retirement synchronously clears verified rank authority");
        AssertHidden(retired.Overview, "RANK // UNVERIFIED");
        Check(!NativeLicenseOverviewBindings.Create(retired)["visible:native_license_star_row"].Boolean,
            "retired account controller snapshot cannot repaint stale stars");
    }

    private static NativeLicenseOverview Record(int points, int? tier)
        => new(true, "SYNTHETIC RANK", "SAMUS", "", "PRIVATE", points,
            "IGNORED LEGACY TEXT", 0,0,0,0,0,0,0) { RatingTierNumber = tier, PrivacyLease = 1 };
    private static NativeLicenseOverviewController.Snapshot Snapshot(NativeLicenseOverview overview)
        => new(1,false,overview,"",NativeLicenseFace.Overview,0,1,0,
            Array.Empty<NativeLicenseMatch>(),false,false);
    private static void AssertHidden(NativeLicenseOverview overview, string expected)
    {
        var bindings = NativeLicenseOverviewBindings.Create(Snapshot(overview));
        Check(bindings["native_license_rank"].Text == expected, "rank state is explicit: " + expected);
        Check(NativeLicenseOverviewBindings.DisplayStarTier(overview) == 0
            && !bindings["visible:native_license_star_row"].Boolean
            && Enumerable.Range(1,5).All(star => !bindings["visible:native_license_star" + star].Boolean)
            && new[] { "red", "bronze", "silver", "gold", "black" }.All(color => !bindings["class:native_license_star_row:rank-" + color].Boolean),
            "unranked, malformed and unverified projections contain no stale star visibility or class");
    }
    private static void Check(bool condition, string label)
    { if (!condition) throw new InvalidOperationException(label); Console.WriteLine("RML LICENSE STAR PASS " + label); }
    private sealed class Reader : INativeLicenseOverviewReader
    {
        internal bool Current = true;
        internal readonly BlockingCollection<TaskCompletionSource<NativeLicenseOverview>> Reads = new();
        public bool IsCurrent(NativeLicenseOverview overview) => Current;
        public Task<NativeLicenseOverview> LoadAsync(CancellationToken cancellationToken)
        {
            var completion = new TaskCompletionSource<NativeLicenseOverview>(TaskCreationOptions.RunContinuationsAsynchronously);
            Reads.Add(completion); return completion.Task;
        }
    }
}
