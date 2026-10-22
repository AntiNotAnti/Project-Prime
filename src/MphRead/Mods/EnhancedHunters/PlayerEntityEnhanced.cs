using MphRead.Mods.EnhancedHunters;
namespace MphRead.Entities;
public partial class PlayerEntity
{
    public EnhancedHunterState EnhancedState { get; } = new();
    private byte _enhancedFeedbackValue, _enhancedFeedbackFlags;
    internal void ModEnhancedFeedback()
    {
        var cue = EnhancedHunterEffects.Cue(this, _enhancedFeedbackValue, _enhancedFeedbackFlags);
        _enhancedFeedbackValue = EnhancedState.ValueA; _enhancedFeedbackFlags = EnhancedState.Flags;
        if (cue.HasValue && IsMainPlayer && _scene.Services.AllowsPresentationSideEffects) _soundSource.PlaySfx(cue.Value);
    }
    internal void ModEnhancedBeforeShot()
    {
        if (!EnhancedHunters.Enabled(this)) return;
        EnhancedState.StationaryShot = Hunter == Hunter.Trace && _cloakTimer >= 60 && _hSpeedMag < .05f;
        EnhancedState.GhostFrames = 0;
    }
    internal void ModEnhancedGhost()
    {
        if (EnhancedHunters.Enabled(this) && Hunter == Hunter.Trace && EnhancedState.GhostFrames > 0)
            _targetAlpha = System.Math.Min(_targetAlpha, 5 / 31f);
    }
}
