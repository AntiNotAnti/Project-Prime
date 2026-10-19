using System.Globalization;
using MphRead.Mods.Launcher;

internal static class EditorControllerAcceptance
{
    internal static void Run(Action<bool, string> check)
    {
        using var owner = new MemoryEditor();
        var controller = new NativeStudioEditorController(owner);
        check(controller.Current().Rows.Count == 8 && controller.Current().PageCount == 3 && owner.Commands == 0,
            "native editor reads borrowed immutable hierarchy without creating/editing a document");
        controller.Page(1); controller.Current(); controller.Select(0);
        check(owner.Selected == owner.Rows[8].Id, "native hierarchy page resolves frozen exact object identity");
        controller.Current(); controller.Page(1); check(controller.Current().Rows.Count == 1, "native hierarchy retains final partial page");
        int before = owner.Commands;
        check(!controller.Select(1) && owner.Commands == before, "empty hierarchy slot cannot become another object");
        controller.Page(-1); controller.Current(); controller.Select(0); controller.Current();
        owner.ExternalChange(); controller.Current();
        check(!controller.Apply(NativeStudioEditorOperation.Move, "1", "2", "3") && owner.Commands == before,
            "inspector draft stays pinned across refresh and rejects an external document revision");
        controller.Select(0); controller.Current();
        check(controller.Apply(NativeStudioEditorOperation.Move, "1.25", "-2", "3e1")
            && owner.LastValues.X == 1.25f && owner.LastValues.Y == -2 && owner.LastValues.Z == 30,
            "explicit reselect pins new revision and bounded numeric Move reaches one existing-owner command");
        foreach (string invalid in new[] { "NaN", "Infinity", "-Infinity", "1e100", "1000001", "1,5", "", new string('1', 65) })
        {
            before = owner.Commands;
            check(!controller.Apply(NativeStudioEditorOperation.Move, invalid, "0", "0") && owner.Commands == before,
                "invalid numeric draft never reaches writer: " + (invalid.Length > 20 ? "oversized" : invalid));
        }
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            controller.Current();
            check(controller.Apply(NativeStudioEditorOperation.Rotate, "12.5") && owner.LastValues.X == 12.5f,
                "numeric parsing remains invariant under comma-decimal locale");
        }
        finally { CultureInfo.CurrentCulture = culture; }
        foreach (string invalid in new[] { "0", "-1", "0.0009", "1001" })
        { before = owner.Commands; check(!controller.Apply(NativeStudioEditorOperation.Scale, invalid) && owner.Commands == before, "scale bounds reject " + invalid); }
        before = owner.Commands;
        check(!controller.Apply(NativeStudioEditorOperation.Rotate, "3601") && owner.Commands == before, "rotation exceeds supported finite angle bound");
        controller.Current(); check(controller.Apply(NativeStudioEditorOperation.Scale, "2"), "valid uniform scale delegates once");
        controller.Current(); owner.ReplaceLease();
        before = owner.Commands;
        check(!controller.Apply(NativeStudioEditorOperation.Undo) && owner.Commands == before, "retired presentation lease cannot affect replacement owner");
        controller.Current(); controller.Select(0); controller.Current();
        check(controller.Apply(NativeStudioEditorOperation.Undo) && owner.LastOperation == NativeStudioEditorOperation.Undo,
            "current undo uses retained owner command interface");
        controller.Current(); check(controller.Apply(NativeStudioEditorOperation.Redo), "redo delegates through same retained history owner");
        controller.Current(); before = owner.Commands;
        check(!controller.Apply(NativeStudioEditorOperation.Save, path: "") && owner.Commands == before, "empty save draft cannot reach existing save writer");
        check(!controller.Apply((NativeStudioEditorOperation)999) && owner.Commands == before, "unassigned editor command is rejected before dispatch");
        check(controller.Apply(NativeStudioEditorOperation.Save, path: "fixture.json") && owner.LastValues.Path == "fixture.json", "typed save path delegates unchanged to existing owner");
        owner.PreserveAllowed = false;
        check(!controller.Close() && !controller.Closed && !owner.Retired, "failed final recovery preservation retains live editor lease");
        owner.PreserveAllowed = true;
        check(controller.Close() && controller.Closed && owner.Retired, "successful owner preservation releases presentation lease");
        before = owner.Commands;
        check(!controller.Apply(NativeStudioEditorOperation.Move, "1", "0", "0") && owner.Commands == before, "closed native editor cannot issue further edits");
        bool threadRejected = Task.Run(() => { try { controller.Current(); return false; } catch (InvalidOperationException) { return true; } }).GetAwaiter().GetResult();
        check(threadRejected, "native editor controller refuses commands on foreign thread");
    }
    private sealed class MemoryEditor : INativeStudioEditorSession
    {
        public NativeStudioObjectRow[] Rows = Enumerable.Range(0, 17).Select(i => new NativeStudioObjectRow(Guid.NewGuid(), "OBJECT " + i, false)).ToArray();
        private Guid _lease = Guid.NewGuid();
        private long _revision;
        private ulong _state;
        public Guid Selected;
        public int Commands;
        public bool PreserveAllowed = true, Retired;
        public NativeStudioEditorValues LastValues;
        public NativeStudioEditorOperation LastOperation;
        private NativeStudioEditTicket Ticket => new(_lease, _state, _revision, Selected);
        public NativeStudioEditorSnapshot Capture() => new(_revision, Ticket, !Retired, "FAKE PROJECT", "fixture.json", true, true, true,
            Selected.ToString(), Array.AsReadOnly(Rows), "MEMORY ONLY");
        public NativeStudioResult Select(NativeStudioEditTicket ticket, Guid id)
        { if (Retired || ticket != Ticket || !Rows.Any(r => r.Id == id)) return new(false, "STALE"); Selected = id; _revision++; return new(true, "SELECTED"); }
        public NativeStudioResult Execute(NativeStudioEditTicket ticket, NativeStudioEditorOperation operation, NativeStudioEditorValues values)
        { if (Retired || ticket != Ticket) return new(false, "STALE"); Commands++; LastValues = values; LastOperation = operation; _state++; _revision++; return new(true, "COMMAND"); }
        public void ExternalChange() { _state++; _revision++; }
        public void ReplaceLease() { _lease = Guid.NewGuid(); _revision++; }
        public NativeStudioResult TryClose() { if (!PreserveAllowed) return new(false, "PRESERVE FAILED"); Retired = true; return new(true, "PRESERVED"); }
        public void Dispose() => Retired = true;
    }
}
