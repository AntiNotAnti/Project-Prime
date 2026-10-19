using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using MphRead.Mods.Launcher;

internal static class StudioModelingControllerChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var owner = new Modeling();
        var controls = new NativeStudioModelingController(owner);
        check(controls.Rows.Count == 8 && controls.Offset == 0 && controls.Pages == 3 && !controls.HasPrevious,
            "modeling starts with at most eight component rows");
        controls.Previous(); controls.Next();
        check(controls.Offset == 8 && controls.Rows[0].Index == 8 && controls.Rows.Count == 8,
            "modeling next page advances exactly eight frozen rows");
        controls.Next(); controls.Next();
        check(controls.Offset == 16 && controls.Rows.Count == 1 && !controls.HasNext,
            "modeling final component page is bounded and cannot advance");
        controls.Previous();
        check(controls.Offset == 8 && controls.Rows[0].Index == 8, "modeling previous page reverses one page");
        owner.Count = 2; owner.Change(); controls.Refresh();
        check(controls.Offset == 0 && controls.Rows.Count == 2 && controls.Pages == 1,
            "modeling refresh clamps paging after the component collection shrinks");
        owner.Count = 0; owner.Change(); controls.Refresh();
        int selects = owner.SelectCalls;
        check(controls.Rows.Count == 0 && controls.Pages == 1 && !controls.SelectComponent(0)
            && !controls.SelectComponent(-1) && !controls.SelectComponent(8) && owner.SelectCalls == selects,
            "empty and invalid modeling slots never reach the backend");

        owner = new Modeling(); controls = new(owner); controls.Next();
        var paintedTicket = controls.Snapshot.Ticket;
        var paintedRow = controls.Rows[1];
        int captures = owner.Captures;
        owner.RowShift = 100; owner.Change();
        check(!controls.SelectComponent(1) && owner.SelectCalls == 1 && owner.LastTicket == paintedTicket
            && owner.LastComponent == paintedRow && owner.Captures == captures
            && controls.Snapshot.Ticket == paintedTicket && controls.Rows[1] == paintedRow,
            "delayed modeling click submits the last painted row and old ticket without capturing a replacement");
        check(controls.Status == "STALE MODELING TICKET", "backend stale-ticket rejection reaches the modeling status");

        owner = new Modeling(); controls = new(owner);
        controls.SetConfigurationDraft("2", "15", ".25", "1", "2", "3");
        paintedTicket = controls.Snapshot.Ticket;
        owner.Change(); controls.Refresh();
        check(!controls.ApplyConfiguration() && owner.LastTicket == paintedTicket
            && controls.GridSnapDraft == "2" && controls.CursorZDraft == "3",
            "configuration drafts retain old tickets and text after external owner refresh and rejection");

        owner = new Modeling(); controls = new(owner);
        controls.SetTransformDraft("4", "5", "6", "45", "2");
        paintedTicket = controls.Snapshot.Ticket;
        owner.Change(); controls.Refresh();
        check(!controls.ApplyTransform() && owner.LastTicket == paintedTicket
            && controls.TransformXDraft == "4" && controls.TransformScaleDraft == "2",
            "transform drafts cannot silently retarget a new owner revision");

        owner = new Modeling(); controls = new(owner);
        controls.SetUvDraft("-2", "3", "4", "5", "32", "90");
        paintedTicket = controls.Snapshot.Ticket;
        owner.Change(); controls.Refresh();
        check(!controls.ApplyUv() && owner.LastTicket == paintedTicket
            && controls.UvScaleUDraft == "-2" && controls.UvDensityDraft == "32",
            "UV drafts retain their original ticket and values after a stale backend response");

        owner = new Modeling(); controls = new(owner);
        controls.SetConfigurationDraft("2", "15", "1", "1", "2", "3");
        controls.SetTransformDraft("4", "5", "6", "45", "2");
        controls.SetUvDraft("-2", "3", "4", "5", "32", "90");
        paintedTicket = controls.Snapshot.Ticket;
        owner.Change(); controls.Refresh();
        var resetTicket = controls.Snapshot.Ticket;
        controls.ResetUvDraft();
        check(controls.UvScaleUDraft == "1" && controls.UvScaleVDraft == "1" && controls.UvAngleDraft == "0"
            && controls.UvOffsetUDraft == "0" && controls.UvOffsetVDraft == "0" && controls.UvDensityDraft == "16"
            && controls.GridSnapDraft == "2" && controls.CursorZDraft == "3"
            && controls.TransformXDraft == "4" && controls.TransformScaleDraft == "2"
            && !controls.ApplyConfiguration() && owner.LastTicket == paintedTicket
            && !controls.ApplyTransform() && owner.LastTicket == paintedTicket
            && controls.ApplyUv() && owner.LastTicket == resetTicket,
            "reset UV draft restores neutral UV values and current ticket while preserving other draft text and frozen tickets");

        owner = new Modeling(); controls = new(owner);
        check(controls.TransformScaleDraft == "1" && controls.TransformAngleDraft == "0"
            && controls.UvScaleUDraft == "1" && controls.UvScaleVDraft == "1" && controls.UvDensityDraft == "16",
            "numeric modeling drafts use neutral transform and UV defaults");
        var priorCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            controls.SetConfigurationDraft("1.25", "180", "1000", "-1000000", "1000000", "0");
            check(controls.ApplyConfiguration() && owner.LastGrid == 1.25f && owner.LastAngle == 180
                && owner.LastScale == 1000 && owner.LastCursor == new Vector3(-1000000, 1000000, 0),
                "configuration dot decimals and inclusive bounds are invariant under a comma-decimal culture");
            int calls = owner.ConfigureCalls;
            controls.SetConfigurationDraft("1,25", "0", "0", "0", "0", "0");
            check(!controls.ApplyConfiguration() && owner.ConfigureCalls == calls,
                "configuration comma decimals are rejected before backend calls");
            controls.ResetDrafts();
            check(controls.GridSnapDraft == "1.25", "configuration drafts format with invariant dot decimals");
        }
        finally { CultureInfo.CurrentCulture = priorCulture; }

        foreach (string invalid in new[] { "NaN", "Infinity", "-Infinity", "1e50", "1000001", "", new string('1', 65) })
        {
            owner = new Modeling(); controls = new(owner);
            controls.SetConfigurationDraft("10", "45", "1", "1", "2", invalid);
            check(!controls.ApplyConfiguration() && owner.ConfigureCalls == 0,
                "invalid final configuration field is atomic: " + (invalid.Length > 64 ? "overlength" : invalid));
            controls.SetTransformDraft("10", "20", invalid, "30", "2");
            check(!controls.ApplyTransform() && owner.ExecuteCalls == 0,
                "invalid transform field applies none of the valid draft values");
            controls.SetUvDraft("1", "2", "3", invalid, "16");
            check(!controls.ApplyUv() && owner.ExecuteCalls == 0,
                "invalid UV field applies none of the valid draft values");
        }

        owner = new Modeling(); controls = new(owner);
        foreach (var draft in new[] {
            new[] { "-1", "0", "0", "0", "0", "0" },
            new[] { "100001", "0", "0", "0", "0", "0" },
            new[] { "0", "181", "0", "0", "0", "0" },
            new[] { "0", "0", "1001", "0", "0", "0" },
            new[] { "0", "0", "0", "-1000001", "0", "0" } })
        {
            controls.SetConfigurationDraft(draft[0], draft[1], draft[2], draft[3], draft[4], draft[5]);
            check(!controls.ApplyConfiguration() && owner.ConfigureCalls == 0, "out-of-range configuration never reaches the backend");
        }
        check(!controls.Configure(gridSnap: float.NaN) && !controls.Configure(angleSnap: float.PositiveInfinity)
            && !controls.Configure(scaleSnap: -1) && !controls.Configure(cursor: new Vector3(0, 0, 1000001))
            && !controls.Configure(mode: (NativeStudioElementMode)999) && owner.ConfigureCalls == 0,
            "typed modeling configuration validates finite bounds and enum values before backend calls");

        controls.SetTransformDraft("0", "0", "0", "3601", "1");
        check(!controls.ApplyTransform() && owner.ExecuteCalls == 0, "numeric transform rejects excessive rotation");
        foreach (string invalidScale in new[] { "0", "-1", ".0009", "1001", "NaN" })
        {
            controls.SetTransformDraft("0", "0", "0", "0", invalidScale);
            check(!controls.ApplyTransform() && owner.ExecuteCalls == 0, "numeric transform enforces a finite positive scale");
        }
        controls.SetTransformDraft("-1000000", "1000000", "0", "-3600", ".001");
        check(controls.ApplyTransform() && owner.LastOperation == NativeStudioModelingOperation.ApplyTransform
            && owner.LastValues?.X == -1000000 && owner.LastValues.Angle == -3600 && owner.LastValues.Scale == .001f,
            "numeric transform applies inclusive boundary values in one backend call");

        owner = new Modeling(); controls = new(owner);
        foreach (string invalidScale in new[] { "0", ".0009", "-1001", "Infinity" })
        {
            controls.SetUvDraft(invalidScale, "1", "0", "0", "16");
            check(!controls.ApplyUv() && owner.ExecuteCalls == 0, "UV rejects zero, tiny, excessive, or nonfinite scale");
        }
        controls.SetUvDraft("1", "1", "0", "0", "65537");
        check(!controls.MatchDensity() && owner.ExecuteCalls == 0, "UV density enforces the backend maximum");
        controls.SetUvDraft("-1000", ".001", "-1000000", "1000000", "65536", "3600");
        check(controls.ApplyUv() && owner.LastValues?.Scale == -1000 && owner.LastValues.ScaleV == .001f
            && owner.LastValues.Density == 65536 && owner.LastValues.Angle == 3600,
            "UV supports signed scale and all inclusive finite boundary values atomically");
        int executions = owner.ExecuteCalls;
        check(!controls.Execute(NativeStudioModelingOperation.ApplyTransform, new(Scale: -1))
            && !controls.Execute(NativeStudioModelingOperation.ApplyUv, new(OffsetV: float.NaN))
            && !controls.Execute((NativeStudioModelingOperation)999) && owner.ExecuteCalls == executions,
            "typed execution wrappers reject invalid operations and numeric payloads");

        owner = new Modeling(); controls = new(owner); owner.Alive = false; owner.Change(); controls.Refresh();
        check(!controls.Snapshot.Alive && !controls.SelectComponent(0) && !controls.Configure(local: true)
            && !controls.ApplyTransform() && owner.SelectCalls == 0 && owner.ConfigureCalls == 0 && owner.ExecuteCalls == 0
            && !string.IsNullOrWhiteSpace(controls.Status), "retired modeling owner is fenced with a human status");
        controls = new NativeStudioModelingController(new object());
        check(!controls.Snapshot.Alive && controls.Rows.Count == 0 && !controls.Configure(local: true)
            && !controls.Execute(NativeStudioModelingOperation.SelectAll) && !string.IsNullOrWhiteSpace(controls.Status),
            "older sessions without the optional modeling interface remain unavailable with a human status");
        bool wrongThread = false;
        Task.Run(() => { try { controls.Refresh(); } catch (InvalidOperationException) { wrongThread = true; } }).GetAwaiter().GetResult();
        check(wrongThread, "modeling controller refuses foreign-thread commands");
    }

    private sealed class Modeling : INativeStudioEditorModeling
    {
        private NativeStudioModelingTicket _ticket = new(new(Guid.NewGuid(), 0, 0, Guid.Empty), 0,
            Guid.NewGuid(), "topology-0", "elements-0");
        private float _grid, _angle, _scale;
        private Vector3 _cursor;
        internal bool Alive = true;
        internal int Count = 17, RowShift, Captures, SelectCalls, ConfigureCalls, ExecuteCalls;
        internal NativeStudioModelingTicket LastTicket;
        internal NativeStudioComponentRow? LastComponent;
        internal NativeStudioModelingOperation LastOperation;
        internal NativeStudioModelingValues? LastValues;
        internal float? LastGrid, LastAngle, LastScale;
        internal Vector3? LastCursor;
        internal void Change() => _ticket = _ticket with {
            Owner = _ticket.Owner with { State = _ticket.Owner.State + 1, Revision = _ticket.Owner.Revision + 1 },
            ConfigurationRevision = _ticket.ConfigurationRevision + 1 };
        public NativeStudioModelingSnapshot CaptureModeling()
        {
            Captures++;
            return new(_ticket.Owner.Revision, _ticket, Alive, NativeStudioElementMode.Face, NativeStudioTransformTool.Move,
                NativeStudioAxes.Free, NativeStudioSelectionMode.Replace, NativeStudioPivot.Center, false,
                _grid, _angle, _scale, _cursor,
                Array.AsReadOnly(Enumerable.Range(0, Count).Select(i => new NativeStudioComponentRow(i + RowShift,
                    i + RowShift, -1, "Face " + (i + RowShift), false)).ToArray()), "NO SELECTION", false, false,
                Alive ? "MODEL AVAILABLE" : "MODELING OWNER RETIRED");
        }
        private bool Current(NativeStudioModelingTicket ticket) => Alive && ticket == _ticket;
        public NativeStudioResult SelectComponent(NativeStudioModelingTicket ticket, NativeStudioComponentRow component)
        {
            SelectCalls++; LastTicket = ticket; LastComponent = component;
            if (!Current(ticket) || component.Index < RowShift || component.Index >= RowShift + Count)
                return new(false, "STALE MODELING TICKET");
            Change(); return new(true, "COMPONENT SELECTED");
        }
        public NativeStudioResult ConfigureModeling(NativeStudioModelingTicket ticket, NativeStudioElementMode? mode = null,
            NativeStudioTransformTool? tool = null, NativeStudioAxes? axes = null, bool? local = null,
            NativeStudioSelectionMode? selection = null, NativeStudioPivot? pivot = null, float? gridSnap = null,
            float? angleSnap = null, float? scaleSnap = null, Vector3? cursor = null)
        {
            ConfigureCalls++; LastTicket = ticket;
            if (!Current(ticket)) return new(false, "STALE MODELING TICKET");
            LastGrid = gridSnap; LastAngle = angleSnap; LastScale = scaleSnap; LastCursor = cursor;
            _grid = gridSnap ?? _grid; _angle = angleSnap ?? _angle; _scale = scaleSnap ?? _scale; _cursor = cursor ?? _cursor;
            Change(); return new(true, "CONFIGURATION APPLIED");
        }
        public NativeStudioResult ExecuteModeling(NativeStudioModelingTicket ticket, NativeStudioModelingOperation operation,
            NativeStudioModelingValues values)
        {
            ExecuteCalls++; LastTicket = ticket; LastOperation = operation; LastValues = values;
            if (!Current(ticket)) return new(false, "STALE MODELING TICKET");
            Change(); return new(true, "MODELING APPLIED");
        }
    }
}
