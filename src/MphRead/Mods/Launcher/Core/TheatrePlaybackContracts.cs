#if !MPHREAD_SERVER
using System;

namespace MphRead.Mods.Launcher.Core;

// Values also define the bounded native ReplayAction argument. Append; never reorder.
public enum TheatrePlaybackAction
{
    TogglePause, JumpBack, JumpForward, Restart, Step, NextRate, NextCamera,
    PreviousPlayer, NextPlayer, Seek, Studio, Back, Fullscreen,
    Bookmarks, BookmarkPrevious, BookmarkNext, BookmarkAdd, BookmarkJump,
    BookmarkRemove, BookmarkConfirmRemove, BookmarkCancel, BookmarkRefresh,
    Ranges, MarkIn, MarkOut, RangeSaveHighlight, RangeCreateVirtual, RangeRefresh, RangeCancel,
    RangeSaveMaterialized, RangeCancelMaterialized,
    CameraEditor, CameraRefresh, CameraFree, CameraFovLess, CameraFovMore, CameraRollLess, CameraRollMore,
    CameraPrevious, CameraNext, CameraPreview, CameraAdd, CameraUpdate, CameraRemove,
    CameraConfirm, CameraCancelConfirm, CameraClose, CameraTrack,
    ExportOpen, ExportRefresh, ExportPreset, ExportResolution, ExportFps, ExportGameHud, ExportReplayOverlay,
    ExportEncoder, ExportSource, ExportCamera, ExportQueue, ExportPrevious, ExportNext, ExportCancelActive,
    ExportRetry, ExportClearPending, ExportConfirm, ExportCancelConfirm, ExportClose, ExportOpenOutput,
    EditorOpen, EditorRefresh, EditorTab, EditorPlayer, EditorEventFilter, EditorPrevious, EditorNext,
    EditorSeek, EditorSelectRange, EditorAddSelectionToReel, EditorAddHighlightToReel,
    EditorAddTopHighlights, EditorMoveUp, EditorMoveDown, EditorTrim, EditorCycleCamera,
    EditorRemove, EditorClearReel, EditorRemoveNamedHighlight, EditorConfirm, EditorCancel,
    EditorClose, EditorPreviousShot, EditorShotAtPlayhead, EditorNextShot,
    EditorToggleAnalyticsHud, EditorToggleNetworkHud, EditorToggleCombatHud, EditorToggleShotRays,
    CameraToolsOpen, CameraToolsRefresh, CameraToolsPrevious, CameraToolsNext, CameraToolsToggleSelection, CameraToolsRangeSelection, CameraToolsSelectAll, CameraToolsClearSelection, CameraToolsMove, CameraToolsCopy, CameraToolsPaste, CameraToolsDuplicate, CameraToolsSnapEvent, CameraToolsSnapKill, CameraToolsSnapBookmark, CameraToolsInterpolation, CameraToolsEase, CameraToolsLookAt, CameraToolsRemoveSelected, CameraToolsConstantSpeed, CameraToolsCollision, CameraToolsDirector, CameraToolsPreview, CameraToolsConfirm, CameraToolsCancel, CameraToolsClose, CameraToolsNearest,
    TimelineOpen, TimelineRefresh, TimelineZoomIn, TimelineZoomOut, TimelinePanLeft, TimelinePanRight, TimelineCenter, TimelineReset, TimelineCancelDrag, TimelineClose, TimelineActivate,
    SpatialOpen, SpatialAnalyze, SpatialKind, SpatialPlayer, SpatialRange, SpatialCancel, SpatialClose,
    StudioToolsOpen, StudioToolsRun, StudioToolsOperation, StudioToolsGameEvents, StudioToolsCombatFeedback, StudioToolsCancel, StudioToolsClose, CameraToolsTransform, CameraToolsHandles, FourViews, StudioToolsPreview, StudioToolsStopPreview, CameraToolsCurveKey, SpatialWorld, StudioCueEvent, StudioCueBus, StudioCuePrevious, StudioCueNext, StudioCueAdd, StudioCueUpdate, StudioCueRemove,
    StudioAudioEnabled = 256, StudioAudioSave = 257, CameraToolsSave = 307, CameraToolsUndo, CameraToolsRedo, CameraToolsDiscard,
    CameraToolsOrientationCurve, CameraToolsOrientationEase, CameraToolsFovCurve, CameraToolsFovEase, CameraToolsRollCurve, CameraToolsRollEase
}

public sealed record TheatrePlaybackSnapshot(Guid Lifetime, long Revision, bool Active, uint Frame,
    uint Duration, string State, float Rate, bool PlayAction, string Camera, string Error)
{
    public bool ExportRendering { get; init; }
    public string Movement { get; init; } = "Unknown";
}

/// <summary>The current launcher retains Studio routing and process ownership.</summary>
public interface ITheatrePlaybackBackend
{
    bool OpenStudio(string path, out string? error);
}
#endif
