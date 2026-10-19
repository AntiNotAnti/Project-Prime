// This CPU fixture source-links the editor and annotation/reel owners only.
// Optional spatial, audio and comparison engines are covered by their own fixtures;
// These counted seams verify dispatch lifetime only, without claiming an actual
// audio device, detached world or comparison player.
namespace MphRead.Mods.Launcher.Core
{
    public interface ITheatreSpatialBackend { }
    public interface ITheatreStudioToolsBackend { }
}

namespace MphRead.Mods.Launcher.RmlUi.Render
{
    internal sealed class EditorComparisonProbe
    {
        internal int Polls, StopCalls, ReviewRetires;
        internal bool Active;
    }

    internal static partial class RmlUiOpenGlSmoke
    {
        internal static EditorComparisonProbe ComparisonProbe(MphRead.Mods.Launcher.Core.ITheatreEditorBackend backend)
            => ((CurrentReplayEditor)backend).Comparison;

        private sealed partial class CurrentReplayEditor
        {
            internal readonly EditorComparisonProbe Comparison = new();
            private void PollWorldComparison() { }
            private void PollComparisonPreview() => Comparison.Polls++;
            private void StopStudioAudioPreview() { }
            private void RetireWorldComparison() => Comparison.ReviewRetires++;
            private void StopComparisonPreview() { Comparison.StopCalls++; Comparison.Active=false; }
        }
    }
}
