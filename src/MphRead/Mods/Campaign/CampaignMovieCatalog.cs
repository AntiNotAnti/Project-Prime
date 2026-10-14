using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace MphRead.Mods.Campaign
{
    /// <summary>
    /// The original cartridge movie IDs and the input boundary for Project
    /// Prime's existing scene movie player. No decoder, GL, audio or packaged
    /// game assets are introduced by this campaign adapter.
    /// </summary>
    public static class CampaignMovieCatalog
    {
        public static Movie Landing(int roomId) => roomId switch
        {
            27 => Movie.AlinosLanding,
            45 => Movie.CALanding,
            65 => Movie.VDOLanding,
            77 => Movie.ArcterraLanding,
            89 => Movie.OublietteLanding,
            _ => Movie.None
        };

        public static Movie Takeoff(int roomId) => roomId switch
        {
            27 => Movie.AlinosTakeoff,
            45 => Movie.CATakeoff,
            65 => Movie.VDOTakeoff,
            77 => Movie.ArcterraTakeoff,
            _ => Movie.None
        };

        public static Movie Ending(int finalRoomId) => finalRoomId switch
        {
            CampaignNavigation.GoreaFirstRoomId => Movie.BadEndingPart1,
            CampaignNavigation.GoreaSecondRoomId => Movie.GoodEnding,
            _ => Movie.None
        };

        /// <summary>
        /// Get the movie's locally extracted VX files. An unavailable,
        /// reserved or invalid movie is not an exception and must not strand
        /// an in-progress campaign on a white fade. File contents are never
        /// shipped; paths are references into the user's own extracted ROM.
        /// </summary>
        public static bool TryFiles(Movie movie, string? extractedRoot,
            out string top, out string? bottom)
        {
            top = "";
            bottom = null;
            int index = (int)movie;
            if (String.IsNullOrWhiteSpace(extractedRoot)
                || index < 0 || index >= Metadata.MovieFiles.Length)
                return false;
            Metadata.MovieInfo? info = Metadata.MovieFiles[index];
            if (info == null || String.IsNullOrWhiteSpace(info.TopScreenPath)) return false;
            top = Paths.Combine(extractedRoot, info.TopScreenPath);
            if (!String.IsNullOrWhiteSpace(info.BottomScreenPath))
                bottom = Paths.Combine(extractedRoot, info.BottomScreenPath);
            return true;
        }

        public static bool HasFiles(Movie movie, string? extractedRoot,
            Func<string, bool>? exists = null)
        {
            exists ??= File.Exists;
            return TryFiles(movie, extractedRoot, out string top, out string? bottom)
                && exists(top) && (bottom == null || exists(bottom));
        }

        /// <summary>
        /// The old player spun forever until four decoded frames arrived.
        /// A missing or corrupt movie could freeze the GL owner thread. A
        /// short valid movie may produce fewer than four total frames. Bound
        /// startup while retaining the existing streaming decoder.
        /// </summary>
        public static bool WaitForWarmup(Task decoding, Func<int> queuedFrames,
            TimeSpan deadline, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(decoding);
            ArgumentNullException.ThrowIfNull(queuedFrames);
            if (deadline < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(deadline));
            Stopwatch watch = Stopwatch.StartNew();
            do
            {
                int queued = queuedFrames();
                if (decoding.IsFaulted || decoding.IsCanceled || token.IsCancellationRequested)
                    return false;
                if (queued >= 4 || (queued > 0 && decoding.IsCompletedSuccessfully))
                    return true;
                if (decoding.IsCompleted) return false;
                if (watch.Elapsed >= deadline) return false;
                Thread.Sleep(5);
            }
            while (true);
        }
    }
}
