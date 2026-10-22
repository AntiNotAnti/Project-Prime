using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Campaign;

namespace MphRead.NetTest;

internal static class CampaignMovieTests
{
    internal static int Run()
    {
        int checks = 0;
        void Check(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException("CAMP-04 MOVIE FAIL " + label);
            checks++;
            Console.WriteLine("CAMP-04 MOVIE PASS " + label);
        }
        try
        {
            int[] landingRooms = { 27, 45, 65, 77, 89 };
            Movie[] arrivals = { Movie.AlinosLanding, Movie.CALanding, Movie.VDOLanding,
                Movie.ArcterraLanding, Movie.OublietteLanding };
            Movie[] departures = { Movie.AlinosTakeoff, Movie.CATakeoff, Movie.VDOTakeoff,
                Movie.ArcterraTakeoff, Movie.None };
            for (int i = 0; i < landingRooms.Length; i++)
            {
                Check(CampaignMovieCatalog.Landing(landingRooms[i]) == arrivals[i],
                    "landing film room " + landingRooms[i]);
                Check(CampaignMovieCatalog.Takeoff(landingRooms[i]) == departures[i],
                    "takeoff film room " + landingRooms[i]);
            }
            Check(CampaignMovieCatalog.Landing(91) == Movie.None
                && CampaignMovieCatalog.Takeoff(91) == Movie.None, "Gorea rooms have no planet film");
            Check(CampaignMovieCatalog.Ending(91) == Movie.BadEndingPart1
                && CampaignMovieCatalog.Ending(92) == Movie.GoodEnding
                && CampaignMovieCatalog.Ending(28) == Movie.None, "good and bad ending IDs");
            Check(!CampaignMovieCatalog.TryFiles(Movie.None, "root", out _, out _)
                && !CampaignMovieCatalog.TryFiles((Movie)13, "root", out _, out _)
                && !CampaignMovieCatalog.TryFiles(Movie.CALanding, "", out _, out _),
                "reserved, None and empty movie roots safely rejected");
            Check(CampaignMovieCatalog.TryFiles(Movie.AlinosLanding, "root",
                out string singleTop, out string? singleBottom)
                && singleTop.EndsWith("04.vx", StringComparison.OrdinalIgnoreCase)
                && singleBottom == null, "single-screen movie file maps to local ROM");
            Check(CampaignMovieCatalog.TryFiles(Movie.StoryIntro, "root",
                out string dualTop, out string? dualBottom)
                && dualTop.EndsWith("02_top.vx", StringComparison.OrdinalIgnoreCase)
                && dualBottom?.EndsWith("02_bot.vx", StringComparison.OrdinalIgnoreCase) == true,
                "dual-screen movie demands both local VX files");
            Check(!CampaignMovieCatalog.HasFiles(Movie.StoryIntro, "root", p => p == dualTop),
                "missing bottom-screen VX never begins a movie");
            Check(CampaignMovieCatalog.HasFiles(Movie.StoryIntro, "root",
                p => p == dualTop || p == dualBottom),
                "both ROM streams satisfy preflight without actual assets");
            Check(CampaignMovieCatalog.HasFiles(Movie.AlinosLanding, "root", p => p == singleTop),
                "single-screen movie needs only its one stream");

            Check(CampaignMovieCatalog.WaitForWarmup(Task.CompletedTask, () => 4, TimeSpan.Zero),
                "four queued frames count as ready");
            Check(CampaignMovieCatalog.WaitForWarmup(Task.CompletedTask, () => 2, TimeSpan.Zero),
                "valid short finished movie can start with fewer than four frames");
            Check(!CampaignMovieCatalog.WaitForWarmup(Task.CompletedTask, () => 0, TimeSpan.Zero),
                "finished decoder with no frames refuses start");
            var missing = new TaskCompletionSource<bool>();
            Check(!CampaignMovieCatalog.WaitForWarmup(missing.Task, () => 0, TimeSpan.Zero),
                "unresponsive decoder never blocks scene owner forever");
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            Check(!CampaignMovieCatalog.WaitForWarmup(missing.Task, () => 0,
                TimeSpan.FromSeconds(2), cancelled.Token), "canceled movie stops immediately");
            Check(!CampaignMovieCatalog.WaitForWarmup(Task.FromException(
                new InvalidDataException("corrupt VX")), () => 0, TimeSpan.Zero),
                "faulted decoder can't masquerade as a video");
            Console.WriteLine($"CAMP-04 MOVIE PASS total={checks} no licensed assets or GL");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine("CAMP-04 MOVIE FAIL " + ex);
            return 1;
        }
    }
}
