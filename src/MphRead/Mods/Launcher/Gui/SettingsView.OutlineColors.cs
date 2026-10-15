#if MPHREAD_AVALONIA
using System;
using Avalonia.Threading;
using MphRead.Mods.Render;

namespace MphRead.Mods.Launcher.Gui
{
    internal sealed partial class SettingsView
    {
        private OutlineColorSettings? _outlineDraft;
        private OutlineColorSettings? _outlinePreviewOriginal;
        private DispatcherTimer? _outlinePreviewTimer;
        private OutlineColorsView? _outlineEditor;
        private int _outlinePreviewSeconds;

        private void OpenOutlineColors()
        {
            object? previous = Content;
            _outlineEditor = new OutlineColorsView(
                _outlineDraft ?? OutlineColorPalette.CopyCurrent(),
                chosen =>
                {
                    CancelOutlineColorPreview();
                    Content = previous;
                    _outlineEditor = null;
                    if (chosen != null)
                    {
                        chosen.Validate();
                        _outlineDraft = chosen;
                    }
                    UpdateDraftStatus();
                }, PreviewOutlineColors);
            Content = _outlineEditor;
        }

        private void PreviewOutlineColors(OutlineColorSettings draft)
        {
            // Only display data changes; do not mutate persisted launcher
            // settings until Apply. Re-preview retains the original snapshot.
            _outlinePreviewOriginal ??= OutlineColorPalette.CopyCurrent();
            OutlineColorPalette.Publish(draft);
            _outlinePreviewSeconds = 60;
            if (_outlinePreviewTimer == null)
            {
                _outlinePreviewTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(1)
                };
                _outlinePreviewTimer.Tick += (_, _) =>
                {
                    _outlinePreviewSeconds--;
                    if (_outlinePreviewSeconds <= 0)
                        CancelOutlineColorPreview();
                    else
                        _outlineEditor?.SetCountdown(_outlinePreviewSeconds);
                };
            }
            _outlineEditor?.SetCountdown(_outlinePreviewSeconds);
            _outlinePreviewTimer.Stop();
            _outlinePreviewTimer.Start();
        }

        private void CancelOutlineColorPreview()
        {
            _outlinePreviewTimer?.Stop();
            _outlinePreviewSeconds = 0;
            if (_outlinePreviewOriginal != null)
            {
                OutlineColorPalette.Publish(_outlinePreviewOriginal);
                _outlinePreviewOriginal = null;
            }
            _outlineEditor?.SetCountdown(0);
        }
    }
}
#endif
