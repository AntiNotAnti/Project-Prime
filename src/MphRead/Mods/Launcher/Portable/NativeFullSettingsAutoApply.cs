#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using System.Diagnostics;

namespace MphRead.Mods.Launcher;

internal sealed partial class NativeFullSettingsController
{
    private long _editRevision, _failedAutoApplyRevision = -1, _autoApplyChangedAt, _previewedRevision = -1;
    internal bool AutoApply { get; set; }
    internal bool AutoApplyPreviewHeld => AutoApply && Dirty && _previewedRevision==_editRevision;
    internal bool AutoApplyPending => AutoApply && Dirty && !AutoApplyPreviewHeld;
    internal bool AutoApplyFailed { get; private set; }
    internal bool AutoApplyBlocked => SettingsPreview.Active || Busy || Capturing || BindingConflict || _calibration != null
        || Confirmation.Length != 0 || ConfirmingVideo;

    private void MarkAutoApplyEdited()
    {
        _editRevision++;
        _autoApplyChangedAt = Stopwatch.GetTimestamp();
        AutoApplyFailed = false;
    }

    internal void TickAutoApply(bool inputActive = true)
    {
        if (!AutoApply || !inputActive || _disposed || Closed || !Dirty || AutoApplyBlocked
            || _failedAutoApplyRevision == _editRevision || _previewedRevision == _editRevision) return;
        if (Stopwatch.GetElapsedTime(_autoApplyChangedAt).TotalMilliseconds < 400) return;
        FlushAutoApply();
    }

    // Navigation and completed gestures flush through the same atomic commit as
    // the quiet-time save. Errors retain editable values and never busy-retry.
    internal bool FlushAutoApply()
    {
        if (!AutoApply || !Dirty) return true;
        if (_disposed || Closed || AutoApplyBlocked) return false;
        bool saved = Apply();
        if (ConfirmingVideo) return false;
        AutoApplyFailed = !saved;
        if (!saved) _failedAutoApplyRevision = _editRevision;
        else { _failedAutoApplyRevision = -1; Status = "Settings saved automatically."; }
        return saved;
    }

    private bool ReadyForOperation(string manualMessage)
    {
        if (!Dirty) return true;
        if (AutoApply) return FlushAutoApply();
        Status = manualMessage;
        return false;
    }

    internal void ResumeAfterPreview()
    {
        _autoApplyChangedAt=Stopwatch.GetTimestamp();_previewedRevision=_editRevision;
        if(AutoApply&&Dirty&&!AutoApplyFailed)Status="Preview ended. Edit a value or use Back to save changes.";
    }

    private void ResetAutoApplyState()
    {
        AutoApplyFailed = false;
        _failedAutoApplyRevision = -1;_previewedRevision=-1;
        _autoApplyChangedAt = Stopwatch.GetTimestamp();
    }
}
#endif
