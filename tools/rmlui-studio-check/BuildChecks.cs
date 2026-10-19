using MphRead.Mods.Launcher;
using MphRead.Mods.MapGen;

internal static class BuildChecks
{
    internal static void Run(Action<bool, string> check)
    {
        using var session = new Session();
        var controller = new NativeStudioBuildController(session);
        var first = controller.Current();
        check(first.Revision == controller.Current().Revision, "unchanged build capture retains presentation revision");
        check(first.Rows.Count == 8 && first.Budgets.Count == 8 && first.DiagnosticPageCount == 3
            && first.BudgetPageCount == 2 && session.Begins == 0, "build panel reads all paged results without starting work");
        session.Diagnostics[0] = new("CHANGED", MapDiagnosticSeverity.Error, "changed");
        check(first.Rows[0].Code == "D0" && first.Owner.Diagnostics[0].Code == "D0", "build presentation copies diagnostic authority");
        bool immutable = false;
        try { ((IList<MapBudget>)first.Budgets)[0] = new("changed", 0); }
        catch (NotSupportedException) { immutable = true; }
        check(immutable, "build budget rows are immutable");
        check(!controller.ProblemsPage(-1) && !controller.BudgetsPage(0) && !controller.SelectProblem(8), "build page and slot bounds reject invalid dispatch");
        check(controller.ProblemsPage(1) && controller.SelectProblem(2) && session.LastIndex == 10,
            "build problem slot dispatches absolute frozen page index");
        session.Ticket = session.Ticket with { Selection = Guid.NewGuid() };
        check(controller.Current().DiagnosticPage == 1 && controller.SelectProblem(3) && session.LastIndex == 11,
            "ordinary editor selection changes preserve result diagnostic page and identity");
        check(controller.ProblemsPage(1) && controller.Current().Rows.Count == 1 && !controller.SelectProblem(1)
            && !controller.ProblemsPage(1), "last diagnostic page cannot dispatch an old row");
        check(controller.BudgetsPage(1) && controller.Current().Budgets.Count == 1 && !controller.BudgetsPage(1),
            "budget paging retains final partial page");
        Guid shown = controller.Current().Owner.Operation;
        session.NewOperation();
        int calls = session.Selections;
        check(!controller.SelectProblem(0) && session.LastOperation == shown && session.Selections == calls + 1,
            "unrefreshed click carries shown operation and cannot select replacement result");
        check(controller.Current().DiagnosticPage == 0 && controller.Current().BudgetPage == 0,
            "new operation resets result pages");
        session.Ticket = session.Ticket with { Revision = 42, Selection = Guid.NewGuid() };
        check(controller.Begin(NativeStudioBuildKind.Package, "review.ppmap") && session.LastTicket == session.Ticket
            && session.LastKind == NativeStudioBuildKind.Package && session.Destination == "review.ppmap",
            "begin captures current session ticket and forwards package destination unchanged");
        check(!controller.Begin(NativeStudioBuildKind.Validate) && session.Begins == 1,
            "busy build rejects duplicate command before owner dispatch");
        controller.Current(); controller.Cancel();
        check(session.Cancels == 1 && session.Build.Stage == NativeStudioBuildStage.Canceling
            && !controller.Begin(NativeStudioBuildKind.Audit), "cancel targets current operation and canceling remains busy");
        controller.Clear(); check(session.Clears == 0, "busy work cannot be cleared");
        session.Build = session.Build with { Stage = NativeStudioBuildStage.ReadyPackage, ResultCurrent = true };
        controller.Current();
        check(controller.ConfirmPackage() && session.Confirms == 1 && session.LastOperation == session.Build.Operation,
            "review confirmation targets exact shown package operation");
        session.Build = session.Build with { Stage = NativeStudioBuildStage.Complete, Status = "PACKAGE COMPLETE" };
        check(controller.Current().Notice == "PACKAGE COMPLETE", "owner completion replaces obsolete command notice");
        session.Build = session.Build with { ResultCurrent = false };
        controller.Current(); check(!controller.ConfirmPackage() && session.Confirms == 1, "stale package result cannot confirm");
        controller.Clear(); check(session.Clears == 1, "completed result clear reaches retained owner");
        foreach (var kind in Enum.GetValues<NativeStudioBuildKind>())
        {
            session.Build = session.Build with { Stage = NativeStudioBuildStage.Idle };
            check(controller.Begin(kind) && session.LastKind == kind, "build kind delegates through retained owner: " + kind);
        }
        int starts = session.Begins;
        check(!controller.Begin((NativeStudioBuildKind)999) && session.Begins == starts, "unknown build command never dispatches");
        bool rejected = Task.Run(() => { try { controller.Current(); return false; } catch (InvalidOperationException) { return true; } }).GetAwaiter().GetResult();
        check(rejected, "build controller rejects foreign thread access");
        using var basic = new BasicSession();
        var unavailable = new NativeStudioBuildController(basic);
        check(!unavailable.Begin(NativeStudioBuildKind.Runtime) && !unavailable.ConfirmPackage()
            && unavailable.Current().Rows.Count == 0, "optional build capability degrades to inert empty presentation");
    }
    private class BasicSession : INativeStudioEditorSession
    {
        public NativeStudioEditTicket Ticket = new(Guid.NewGuid(), 1, 1, Guid.Empty);
        public NativeStudioEditorSnapshot Capture() => new(1, Ticket, true, "fixture", "", false, false, false, "", Array.Empty<NativeStudioObjectRow>(), "READY");
        public NativeStudioResult Select(NativeStudioEditTicket ticket, Guid objectId) => new(true, "SELECTED");
        public NativeStudioResult Execute(NativeStudioEditTicket ticket, NativeStudioEditorOperation operation, NativeStudioEditorValues values) => new(true, "DONE");
        public NativeStudioResult TryClose() => new(true, "CLOSED");
        public void Dispose() { }
    }
    private sealed class Session : BasicSession, INativeStudioEditorBuild
    {
        public readonly MapDiagnostic[] Diagnostics = Enumerable.Range(0, 17).Select(i => new MapDiagnostic("D" + i, MapDiagnosticSeverity.Error, "Problem " + i, Guid.NewGuid())).ToArray();
        public NativeStudioBuildSnapshot Build;
        public int Begins, Confirms, Cancels, Clears, Selections, LastIndex;
        public Guid LastOperation;
        public NativeStudioEditTicket LastTicket;
        public NativeStudioBuildKind LastKind;
        public string Destination = "";
        public Session() => Build = new(1, Guid.NewGuid(), Ticket, NativeStudioBuildKind.Validate, NativeStudioBuildStage.Complete,
            "DONE", "", "fingerprint", Diagnostics, Enumerable.Range(0, 9).Select(i => new MapBudget("B" + i, i, 100)).ToArray(), true);
        public void NewOperation() => Build = Build with { Operation = Guid.NewGuid() };
        public NativeStudioBuildSnapshot CaptureBuild() => Build;
        public NativeStudioResult BeginBuild(NativeStudioEditTicket ticket, NativeStudioBuildKind kind, string destination = "")
        {
            Begins++; LastTicket = ticket; LastKind = kind; Destination = destination;
            Build = Build with { Operation = Guid.NewGuid(), Stage = NativeStudioBuildStage.Running, Ticket = ticket };
            return new(true, "RUNNING");
        }
        public NativeStudioResult ConfirmBuildPackage(Guid operation)
        { LastOperation = operation; Confirms++; return new(operation == Build.Operation, "CONFIRMED"); }
        public void CancelBuild(Guid operation)
        { LastOperation = operation; Cancels++; if (operation == Build.Operation) Build = Build with { Stage = NativeStudioBuildStage.Canceling }; }
        public void ClearBuild(Guid operation) { LastOperation = operation; Clears++; }
        public NativeStudioResult SelectBuildProblem(Guid operation, int absoluteIndex)
        { Selections++; LastOperation = operation; LastIndex = absoluteIndex; return new(operation == Build.Operation, "PROBLEM"); }
    }
}

namespace MphRead
{
    // Standalone linked-source checks do not load the game entry-point assembly.
    public class ProgramException : Exception
    { public ProgramException(string message) : base(message) { } }
}
