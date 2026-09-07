using System;
using MphRead.Entities;

namespace MphRead.Mods.Network
{
    /// <summary>
    /// Keeps the local match aligned with the server's.
    ///
    /// MatchFlow.StartMatch hardcodes MatchTime (7 minutes for Battle) with
    /// no notion of a server, so a client joining a round already 3 minutes
    /// old started its own 7-minute clock. Correct offline, wrong the moment
    /// a server owns the match: the server's remaining time is the truth,
    /// and every client must show it.
    ///
    /// Applied continuously rather than once at load: the server rotates
    /// maps and restarts its clock, and a client that only synced at join
    /// would drift away again at the next rotation.
    /// </summary>
    public static class NetMatchSync
    {
        private static string _lastRoom = "";
        private static bool _everSynced;

        public static void Reset()
        {
            _lastRoom = "";
            _everSynced = false;
        }

        /// <summary>True once the server's clock has been adopted at least once.</summary>
        public static bool Synced => _everSynced;

        /// <summary>Seconds of difference last observed, for diagnostics.</summary>
        public static float LastDrift { get; private set; }

        public static void Apply(Scene scene)
        {
            if (!NetSession.Active || NetSession.ServerMatch == null)
            {
                return;
            }
            MatchStatePacket state = NetSession.ServerMatch.Value;
            if (state.RoomKey.Length == 0)
            {
                return;
            }
            // The point goal decides when a match ends, so it belongs to the
            // server for the same reason the clock does: clients that
            // disagreed about it would stop playing at different moments.
            // Applied whether or not the clock is, because the results
            // sequence below is exactly when the clock must be left alone.
            MatchRules rules = scene.Match.Rules;
            bool updateGoal = state.PointGoal > 0 && rules.LegacyPointGoal != state.PointGoal;
            // Same-team damage is a server rule. Retain the immutable rules
            // object when neither replicated setting has changed.
            if (updateGoal || rules.FriendlyFire != state.FriendlyFire)
            {
                scene.Match.ApplyRules(rules.With(
                    startingLives: updateGoal && rules.IsSurvival ? state.PointGoal : null,
                    scoreGoal: updateGoal && !rules.IsSurvival ? state.PointGoal : null,
                    friendlyFire: state.FriendlyFire));
            }
            // Not while the match is ending. MatchTime is the countdown the
            // results sequence itself runs on -- three seconds of the winner's
            // camera, then five of the scoreboard -- so adopting the server's
            // figure here put the old map's remaining time back on top of it
            // every frame and the sequence never finished.
            if (NetMatchEnd.InIntermission(scene))
            {
                _lastRoom = state.RoomKey;
                return;
            }
            // A time limit of zero means the server runs without one; leave
            // the local clock alone rather than freezing it at zero.
            if (state.TimeRemaining <= 0 && state.TimeElapsed <= 0)
            {
                return;
            }

            bool newMatch = state.RoomKey != _lastRoom;
            _lastRoom = state.RoomKey;

            LastDrift = scene.Match.MatchTime - state.TimeRemaining;
            // Snap on a new match or when clearly out of step; small
            // differences are packet latency and correcting them every frame
            // would make the on-screen timer stutter.
            if (newMatch || !_everSynced || Math.Abs(LastDrift) > 1.5f)
            {
                scene.Match.MatchTime = state.TimeRemaining;
                if (!_everSynced || newMatch)
                {
                    Console.WriteLine($"[net] match clock synced to server: "
                        + $"{state.TimeRemaining:0} s remaining on {state.RoomKey}");
                }
                _everSynced = true;
            }
        }
    }
}
