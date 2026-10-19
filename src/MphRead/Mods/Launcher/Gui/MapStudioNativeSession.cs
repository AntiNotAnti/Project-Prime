using System;
using System.IO;
using System.Linq;
using System.Numerics;
using MphRead.Mods.MapEditor;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.Launcher.Gui;

internal sealed partial class MapStudioScreen
{
    private bool _visualAttached, _ownerRunning, _disposedOwner;
    private NativeStudioEditorSession? _nativeEditor;
    private void EnsureEditorOwnerRunning()
    {
        if (_ownerRunning || _disposedOwner) return;
        // A detached legacy owner may still have its last atomic write in flight.
        // Retire and await it before resuming this same owner's writer.
        _autosave.Dispose(); _autosave.Completion.GetAwaiter().GetResult();
        _ownerRunning = true; _detached = false; _autosave = new(); _idle.Start();
        try { RefreshSourceWatch(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { _status.Text = "Source watch unavailable: " + error.Message; }
    }
    private void SuspendEditorOwner()
    {
        if (_nativeEditor != null || _nativeChange != null || _visualAttached || !_ownerRunning) return;
        _ownerRunning = false; _detached = true; _editorGeneration++; _idle.Stop(); _work?.Cancel();
        _autosave.Dispose(); _sourceWatch?.Dispose(); _sourceWatch = null; DisposePreviewCaches();
    }
    internal bool TryAcquireNativeEditor(out NativeStudioEditorSession? session, out string error)
    {
        session = null;
        if (_disposedOwner || _nativeEditor != null || _poppedOut || _work != null || _document == null || _viewport == null
            || _sheet != null || _modal.IsVisible)
        { error = "OPEN A PROJECT AND FINISH THE CURRENT EDITOR OPERATION BEFORE NATIVE EDITING"; return false; }
        EnsureEditorOwnerRunning();
        foreach (var view in _views) view.CancelInteraction();
        _viewport.NativePresentationLeased = true;
        _nativeEditor = session = new(this, _document, _viewport, _editorGeneration);
        error = ""; return true;
    }

    /// <summary>Exclusive presentation lease; the retained screen remains the only writer.</summary>
    internal sealed partial class NativeStudioEditorSession : INativeStudioEditorSession, INativeStudioEditorViewport
    {
        private readonly MapStudioScreen _owner;
        private readonly MapDocument _document;
        private readonly MapViewport _viewport;
        private readonly long _generation;
        private readonly int _thread = Environment.CurrentManagedThreadId;
        private readonly Guid _lease = Guid.NewGuid();
        private long _revision = 1;
        private bool _disposed;
        private bool _recoveryPreserved;
        private bool _renderUnavailable;
        private MapViewportLayout _layout;
        private NativeStudioEditorSnapshot? _snapshot;
        internal NativeStudioEditorSession(MapStudioScreen owner, MapDocument document, MapViewport viewport, long generation)
        {
            _owner = owner; _document = document; _viewport = viewport; _generation = generation;
            document.Invalidated += Invalidated;
        }
        private bool Alive => !_disposed && !_owner._disposedOwner && _owner._nativeEditor == this
            && _owner._editorGeneration == _generation && _owner._document == _document && _owner._viewport == _viewport;
        private NativeStudioEditTicket Ticket => new(_lease, _document.CurrentStateId.Value, _revision, _document.ActiveObjectId);
        private void CheckThread()
        { if (Environment.CurrentManagedThreadId != _thread) throw new InvalidOperationException("Studio lease belongs to its editor/render owner thread."); }
        private void Invalidated(MapDocumentChange change) { AssetIoOwnerInvalidated(); BuildOwnerInvalidated(); _revision++; _snapshot = null; }
        public NativeStudioEditorSnapshot Capture()
        {
            CheckThread(); PumpBuild();
            if (!Alive) return new(_revision, Ticket, false, "EDITOR LEASE RETIRED", "", false, false, false, "", Array.Empty<NativeStudioObjectRow>(), "EDITOR LEASE RETIRED");
            if (_snapshot != null) return _snapshot;
            var selected = MapObjects.Find(_document.Project.Definition, _document.ActiveObjectId);
            string selection = selected == null ? "NO OBJECT SELECTED" : selected + " // POSITION " + string.Join(", ", selected.Position.Select(n => n.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)));
            return _snapshot = new(_revision, Ticket, true, _document.Project.Definition.Name,
                _document.FilePath ?? _owner._path.Text ?? "", _document.IsDirty, _document.History.CanUndo, _document.History.CanRedo,
                selection, Array.AsReadOnly(MapObjects.All(_document.Project.Definition).Select(o =>
                    new NativeStudioObjectRow(o.Id, o.ToString(), o.Value is MapGeometry { Locked: true })).ToArray()),
                _renderUnavailable ? "VIEWPORT RENDERER UNAVAILABLE // DOCUMENT AND UNDO HISTORY RETAINED" : "NATIVE OBJECT AND COMPONENT EDITOR // BUILD & TEST FOR VALIDATION, PACKAGING AND PLAYTEST", _owner._work != null);
        }
        private bool Current(NativeStudioEditTicket ticket) => Alive && !_owner._detached && _owner._work == null
            && ticket == Ticket;
        public NativeStudioResult Select(NativeStudioEditTicket ticket, Guid objectId)
        {
            CheckThread(); if (!Current(ticket) || MapObjects.Find(_document.Project.Definition, objectId) == null) return Stale();
            if(_document.ActiveObjectId!=objectId)_viewport.ClearSubSelection();
            _document.Selection.Clear(); _document.Selection.Add(objectId); _document.ActiveObjectId = objectId;
            _document.SelectionChanged(); _owner.RefreshHierarchy(); _owner.ShowInspectorPage(_owner._inspectorPage, false);
            return new(true, "OBJECT SELECTED // TRANSFORM DRAFT NOW TARGETS THIS DOCUMENT REVISION");
        }
        public NativeStudioResult Execute(NativeStudioEditTicket ticket, NativeStudioEditorOperation operation, NativeStudioEditorValues values)
        {
            CheckThread(); if (!Current(ticket)) return Stale();
            try
            {
                switch (operation)
                {
                    case NativeStudioEditorOperation.Undo: _viewport.CancelInteraction(); _document.History.Undo(); break;
                    case NativeStudioEditorOperation.Redo: _viewport.CancelInteraction(); _document.History.Redo(); break;
                    case NativeStudioEditorOperation.Save:
                        string path = Path.GetFullPath(values.Path);
                        if (!path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) return new(false, "SAVE PATH MUST END IN .JSON");
                        bool saved = _owner.SaveTo(path);
                        _revision++; _snapshot = null;
                        return new(saved, _owner._status.Text ?? "SAVE COMPLETE");
                    case NativeStudioEditorOperation.Move:
                    case NativeStudioEditorOperation.Rotate:
                    case NativeStudioEditorOperation.Scale:
                        if (!float.IsFinite(values.X) || !float.IsFinite(values.Y) || !float.IsFinite(values.Z)
                            || Math.Abs(values.X) > 1000000 || Math.Abs(values.Y) > 1000000 || Math.Abs(values.Z) > 1000000
                            || (operation == NativeStudioEditorOperation.Rotate && Math.Abs(values.X) > 3600)
                            || (operation == NativeStudioEditorOperation.Scale && (values.X < .001f || values.X > 1000)))
                            return new(false, "TRANSFORM VALUES ARE OUTSIDE SUPPORTED FINITE BOUNDS");
                        if (ticket.Selection == Guid.Empty || _document.Selection.Count != 1 || !_document.Selection.Contains(ticket.Selection)) return new(false, "SELECT ONE OBJECT BEFORE APPLYING A TRANSFORM");
                        var item = MapObjects.Find(_document.Project.Definition, ticket.Selection);
                        if (item?.Value is MapGeometry { Locked: true }) return new(false, "SELECTED OBJECT IS LOCKED");
                        if (operation == NativeStudioEditorOperation.Rotate && item?.Value is not MapGeometry and not MapSpawn)
                            return new(false, "Y ROTATION REQUIRES GEOMETRY OR A PLAYER SPAWN");
                        if (operation == NativeStudioEditorOperation.Scale && item?.Value is not MapGeometry and not MapBrush)
                            return new(false, "SCALING REQUIRES GEOMETRY OR A BOX");
                        // One existing History command per explicit Apply, with no cross-click transaction merge.
                        _document.TransformSelection(new[] { ticket.Selection }, operation.ToString(),
                            operation == NativeStudioEditorOperation.Move ? new(values.X, values.Y, values.Z) : Vector3.Zero,
                            operation == NativeStudioEditorOperation.Rotate ? values.X : 0,
                            operation == NativeStudioEditorOperation.Scale ? values.X : 1, false);
                        break;
                    case NativeStudioEditorOperation.FrameAll: _viewport.FrameAll(); break;
                    case NativeStudioEditorOperation.FrameSelection: _viewport.FrameSelection(); break;
                    case NativeStudioEditorOperation.Perspective: _viewport.SetView("Perspective"); break;
                    case NativeStudioEditorOperation.Top: _viewport.SetView("Top"); break;
                    case NativeStudioEditorOperation.Front: _viewport.SetView("Front"); break;
                    case NativeStudioEditorOperation.Side: _viewport.SetView("Side"); break;
                    default: return new(false, "UNKNOWN EDITOR COMMAND");
                }
                _snapshot = null;
                return new(true, operation + " // EXISTING DOCUMENT AND UNDO HISTORY RETAINED");
            }
            catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException)
            { return new(false, "EDITOR COMMAND COULD NOT COMPLETE // CURRENT DOCUMENT RETAINED"); }
        }
        public bool DrawViewport(int x, int y, int width, int height, int targetWidth, int targetHeight)
        {
            CheckThread();
            if (!Alive || width <= 0 || height <= 0 || x < 0 || y < 0 || x + (long)width > targetWidth || y + (long)height > targetHeight) { RecordModelingDraw(false,targetWidth,targetHeight); return false; }
            _layout = new(width, height, 1, x, y);
#if MPHREAD_SHELL
            bool drawn = _viewport.DrawNative(_layout, targetWidth, targetHeight);
#else
            bool drawn = false;
#endif
            if (_renderUnavailable == drawn) { _renderUnavailable = !drawn; _snapshot = null; _revision++; }
            RecordModelingDraw(drawn,targetWidth,targetHeight);
            return drawn;
        }
        public NativeStudioResult Pick(NativeStudioEditTicket ticket, double x, double y)
        {
            CheckThread();
            if (!Current(ticket) || !_modelDrawValid || _viewport.CameraPosition!=_modelDrawCamera || _viewport.CameraTarget!=_modelDrawTarget
                || (_viewport.View=="Perspective")!=_modelDrawPerspective || !_layout.IsValid || !double.IsFinite(x) || !double.IsFinite(y)
                || x < 0 || y < 0 || x >= _layout.Width || y >= _layout.Height) return Stale();
            var id = MapViewportPicking.Pick(_viewport.BuildRenderFrame(_layout), x, y);
            if (id != Guid.Empty) return Select(ticket, id);
            _document.Selection.Clear(); _document.ActiveObjectId = Guid.Empty; _document.SelectionChanged();
            _owner.RefreshHierarchy(); _owner.ShowInspectorPage(_owner._inspectorPage, false);
            return new(true, "SELECTION CLEARED");
        }
        public void Camera(double deltaX, double deltaY, bool orbit, bool pan)
        { CheckThread(); if (Alive && double.IsFinite(deltaX) && double.IsFinite(deltaY)) _viewport.NativeCameraMove(deltaX, deltaY, orbit, pan); }
        public void Zoom(double steps) { CheckThread(); if (Alive && double.IsFinite(steps)) _viewport.NativeZoom(steps); }
        public void CancelInput() { CheckThread(); if (Alive) CancelModeling(); }
        public NativeStudioResult TryClose()
        {
            CheckThread(); if (_disposed) return new(true, "EDITOR LEASE ALREADY RELEASED");
            if(!TryLeaveBuild())return new(false,"CANCELING BUILD WORK // EDITOR REMAINS OPEN UNTIL THE WORKER RETIRES");
            if(!TryLeaveAssetIo())return new(false,"CANCELING ASSET WORK // EDITOR REMAINS OPEN UNTIL THE BOUNDED WORKER RETIRES");
            if (Alive && !_owner._visualAttached && !_owner.SaveRecovery()) return new(false, "RECOVERY COULD NOT BE PRESERVED // EDITOR REMAINS OPEN");
            _recoveryPreserved = true;
            Dispose(); return new(true, "NATIVE PRESENTATION RELEASED // RETAINED DOCUMENT AND HISTORY AVAILABLE");
        }
        public void Dispose()
        {
            CheckThread(); if (_disposed) return;
            RetireBuild(true);
            RetireAssetIo(true);
            if (!_recoveryPreserved && !_owner._visualAttached && _owner._document == _document && _document.IsDirty)
                _owner.SaveRecovery();
            ReleaseModeling();
            _disposed = true; _document.Invalidated -= Invalidated; _viewport.CancelInteraction(); _viewport.NativePresentationLeased = false;
            if (_owner._nativeEditor == this) _owner._nativeEditor = null;
            // The same viewport renderer is returned to legacy ownership if visible,
            // otherwise released on its current render owner before suspension.
#if MPHREAD_SHELL
            if (!_owner._visualAttached) _viewport.ReleaseRenderer();
#endif
            _owner.SuspendEditorOwner(); _snapshot = null;
        }
        private static NativeStudioResult Stale() => new(false, "EDITOR OR SELECTION CHANGED // RESELECT THE OBJECT BEFORE APPLYING");
    }
}
