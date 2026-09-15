using System;
using System.Collections.Generic;
using MphRead.Mods.Network;

namespace MphRead.Mods.Launcher
{
    /// <summary>
    /// Turning the player's choice of hunter into one the game can load.
    ///
    /// <see cref="Hunter.Random"/> is a *menu* entry, not a character:
    /// <c>Metadata.HunterModels</c> has an entry for every playable Hunter and
    /// none for the selector sentinel. Nothing used to roll it into a real
    /// one, so picking Random and starting a match threw
    /// <see cref="System.Collections.Generic.KeyNotFoundException"/> the
    /// moment the player entity was created -- on every platform, and reported
    /// on Android as the raw resource name because a trimmed build has no
    /// message strings.
    ///
    /// Rolled where the choice is used rather than where it is made, so the
    /// preference stays "Random" and gives a different hunter next time.
    /// </summary>
    public static class Hunters
    {
        /// <summary>The immutable playable-Hunter catalog size.</summary>
        public static int Playable => PlayableHunterCatalog.Count;

        /// <summary>
        /// The roll, held for the length of one launch.
        ///
        /// It has to be held, because a launch asks more than once. The Node
        /// handoff announces the hunter before the launch plan carrying it is
        /// built, so two independent rolls would put this player on the roster
        /// as one hunter and draw them as another -- on their own screen and
        /// on everybody else's.
        /// </summary>
        private static Hunter _rolled = Hunter.Random;

        public static Hunter Resolve(Hunter hunter)
        {
            if (hunter != Hunter.Random)
            {
                return hunter;
            }
            if (_rolled == Hunter.Random)
            {
                _rolled = PlayableHunterCatalog.FromIndex(
                    Random.Shared.Next(Playable));
            }
            return _rolled;
        }

        /// <summary>
        /// Forget the roll, so the next match gets a different hunter. Called
        /// by each front screen as it opens -- "Random" that gives the same
        /// hunter every match for the life of the process is not random.
        /// </summary>
        public static void Reroll() => _rolled = Hunter.Random;
    }

    public enum LaunchKind
    {
        None = 0,
        Online = 1,
        Replay = 5
    }

    /// <summary>
    /// What a front screen decided, carried back to the code that starts the
    /// match.
    ///
    /// Lives here rather than beside <c>HomeForm</c> because there are two
    /// front screens now -- the WinForms one on Windows and
    /// <c>TextLauncher</c> everywhere else -- and this is the whole of
    /// what they have to agree on. <c>MatchStart</c> is the other half:
    /// it takes one of these and does the same thing with it whichever screen
    /// produced it, so the two launchers cannot drift into starting subtly
    /// different matches.
    /// </summary>
    public readonly struct LaunchPlan
    {
        public LaunchKind Kind { get; init; }

        public void Validate()
        {
            if (Kind is not (LaunchKind.Online or LaunchKind.Replay))
            {
                throw new ArgumentException("Only Node-admitted online and replay sessions are supported.", nameof(Kind));
            }
        }


        /// <summary>
        /// The hunter to play, already rolled if the player asked for a random
        /// one. Resolved on the way in rather than by each of the four places
        /// that read it -- see <see cref="Hunters"/> for what happens when it
        /// is not resolved at all.
        /// </summary>
        public Hunter Hunter
        {
            get => _hunter;
            init => _hunter = Hunters.Resolve(value);
        }

        private readonly Hunter _hunter;
        public string RoomKey { get; init; }
        public GameMode Mode { get; init; }
        public int Bots { get; init; }
        public int BotLevel { get; init; }
        public int Port { get; init; }
        public string PlayerName { get; init; }

        /// <summary>Replay only: the recorded file to play back.</summary>
        public string ReplayPath { get; init; }
        /// <summary>Replay only: one clip or an ordered highlight reel.</summary>
        public IReadOnlyList<ReplayHighlight>? ReplayHighlights { get; init; }
        /// <summary>
        /// Replay only: an optional indexed event/manual-preview position.
        /// The shared launch path applies this through the normal bounded seek.
        /// Highlight launches leave this null and own their range via
        /// <see cref="ReplayHighlights"/>.
        /// </summary>
        public uint? ReplayStartFrame { get; init; }
        /// <summary>
        /// Replay only: Clip Out for a user-authored bounded range. This must
        /// be paired with <see cref="ReplayStartFrame"/>.
        /// </summary>
        public uint? ReplayEndFrame { get; init; }
        /// <summary>Replay only: optional exact actor focus for a bounded range.</summary>
        public CombatActor? ReplayFocus { get; init; }
    }
}
