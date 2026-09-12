using MphRead.Hud;
using MphRead.Mods.Network;

namespace MphRead.Entities
{
    public partial class PlayerPresentation
    {
        private bool _replayRecaps;
        private int _recapPage;
        private bool ApplyRecapNavigation()
        {
            if (!ReplayPlayback.IsModern) { _replayRecaps = false; return false; }
            if (Bindings.RecapHistory.IsPressed) _replayRecaps = !_replayRecaps;
            int count = Presentation.CombatFeedback.Recaps.Count;
            if (!_replayRecaps || count == 0) return false;
            _recapPage = System.Math.Clamp(_recapPage, 0, count - 1);
            if (Bindings.NextWeapon.IsPressed) _recapPage = (_recapPage + 1) % count;
            else if (Bindings.PrevWeapon.IsPressed) _recapPage = (_recapPage + count - 1) % count;
            return true;
        }
        public bool RecapViewOpen => _replayRecaps && ReplayPlayback.IsModern;
        public bool ResultsAvailable => IntermissionVoteControls.Available;
        public bool ReplayRecapsAvailable => ReplayPlayback.IsModern;
        public void NavigateResults(int direction, bool toggleRecaps = false)
        {
            if (ReplayPlayback.IsModern)
            {
                if (toggleRecaps) _replayRecaps = !_replayRecaps;
                int count = Presentation.CombatFeedback.Recaps.Count;
                if (count > 0) _recapPage = (_recapPage + direction + count) % count;
            }
        }
        private bool HasPostMatchResult => _player._scene.Match.Result != null
            && _player._scene.Match.Phase is MatchPhase.Ending or MatchPhase.Intermission;
        private void DrawRetainedRecap(int index, string footer)
        {
            var archive = Presentation.CombatFeedback.Recaps;
            if (archive.Count == 0) return;
            index = System.Math.Clamp(index, 0, archive.Count - 1);
            var life = archive[index];
            DrawText2D(128, 40, Align.Center, 0, $"LIFE {life.Life.Life} RECAP {index + 1}/{archive.Count}", scale: .7f);
            DrawText2D(128, 52, Align.Center, 0, life.Heading, maxLength: 44, scale: .7f);
            DrawText2D(128, 64, Align.Center, 0, life.Final, maxLength: 44, scale: .65f);
            for (int i = 0; i < life.Count; i++)
                DrawText2D(8, 78 + i * 9, Align.Left, 0, life[i].Text, maxLength: 50, scale: .6f);
            DrawText2D(128, 164, Align.Center, 0, footer, scale: .6f);
        }
        private bool DrawIntermissionPresentation()
        {
            if (IntermissionVoteControls.Draw(this)) return true;
            if (!HasPostMatchResult && _replayRecaps && ReplayPlayback.IsModern)
            {
                if (Presentation.CombatFeedback.Recaps.Count > 0) DrawRetainedRecap(_recapPage, "Recap toggle: close / cycle: lives");
                else DrawText2D(128, 92, Align.Center, 0, "No received life recaps yet", scale: .7f);
                return true;
            }
            // Match results are presented by the launcher-owned after-match
            // report. Drawing a second paged result over the completed scene
            // only duplicates that report during the handoff.
            return false;
        }
    }
}
