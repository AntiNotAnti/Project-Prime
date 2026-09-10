using System.Diagnostics;
using MphRead.Hud;
using MphRead.Mods.Network;

namespace MphRead.Entities
{
    public partial class PlayerPresentation
    {
        private MatchResult? _shownResult;
        private PostMatchPresentation? _resultPresentation;
        private int _resultPage;
        private long _nextResultPage;
        private bool _replayRecaps;
        private int _recapPage;
        private int ResultPageCount => 3 + Presentation.CombatFeedback.Recaps.Count;
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
        public bool ResultsAvailable => HasPostMatchResult || IntermissionVoteControls.Available;
        public bool ReplayRecapsAvailable => ReplayPlayback.IsModern;
        public void NavigateResults(int direction, bool toggleRecaps = false)
        {
            if (HasPostMatchResult)
            {
                _resultPage = (_resultPage + direction + ResultPageCount) % ResultPageCount;
                _nextResultPage = Stopwatch.GetTimestamp() + Stopwatch.Frequency * 6;
            }
            else if (ReplayPlayback.IsModern)
            {
                if (toggleRecaps) _replayRecaps = !_replayRecaps;
                int count = Presentation.CombatFeedback.Recaps.Count;
                if (count > 0) _recapPage = (_recapPage + direction + count) % count;
            }
        }
        private bool HasPostMatchResult => _player._scene.Match.Result != null
            && _player._scene.Match.Phase is MatchPhase.Ending or MatchPhase.Intermission;
        private void ApplyPostMatchInput()
        {
            if (Bindings.NextWeapon.IsPressed) _resultPage = (_resultPage + 1) % ResultPageCount;
            else if (Bindings.PrevWeapon.IsPressed) _resultPage = (_resultPage + ResultPageCount - 1) % ResultPageCount;
            else return;
            _nextResultPage = Stopwatch.GetTimestamp() + Stopwatch.Frequency * 6;
        }
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
        private bool DrawPostMatchResults()
        {
            if (IntermissionVoteControls.Draw(this)) return true;
            if (!HasPostMatchResult)
            {
                if (_replayRecaps && ReplayPlayback.IsModern)
                {
                    if (Presentation.CombatFeedback.Recaps.Count > 0) DrawRetainedRecap(_recapPage, "Recap toggle: close / cycle: lives");
                    else DrawText2D(128, 92, Align.Center, 0, "No received life recaps yet", scale: .7f);
                    return true;
                }
                return false;
            }
            MatchResult result = _player._scene.Match.Result!;
            long now = Stopwatch.GetTimestamp();
            if (!ReferenceEquals(_shownResult, result))
            {
                _shownResult = result;
                _resultPresentation = new PostMatchPresentation(result);
                _resultPage = 0;
                _nextResultPage = now + Stopwatch.Frequency * 4;
            }
            if (now >= _nextResultPage)
            {
                _resultPage = (_resultPage + 1) % ResultPageCount;
                _nextResultPage = now + Stopwatch.Frequency * 4;
            }
            if (_resultPage >= ResultPageCount) _resultPage = 0;
            if (_resultPage >= 3)
            { DrawRetainedRecap(_resultPage - 3, "Weapon cycle: pages / auto rotate"); return true; }
            DrawText2D(128, 40, Align.Center, 0, _resultPresentation!.Headers[_resultPage], scale: .7f);
            string[] lines = _resultPresentation.Pages[_resultPage];
            for (int i = 0; i < lines.Length; i++)
                DrawText2D(8, 57 + i * 12, Align.Left, 0, lines[i], maxLength: 50, scale: .6f);
            DrawText2D(128, 164, Align.Center, 0, "Weapon cycle: pages / auto rotate", scale: .6f);
            return true;
        }
    }
}
