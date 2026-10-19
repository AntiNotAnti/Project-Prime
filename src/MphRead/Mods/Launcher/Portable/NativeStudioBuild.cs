using System;
using System.Collections.Generic;
using System.Linq;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.Launcher;

public enum NativeStudioBuildKind { Validate, Runtime, Package, PlayCamera, PlaySelection, PlayTeamA, PlayTeamB, Navigation, Audit }
public enum NativeStudioBuildStage { Idle, Running, Canceling, ReadyPackage, Complete, Failed }
public sealed record NativeStudioBuildSnapshot(long Revision, Guid Operation, NativeStudioEditTicket Ticket,
    NativeStudioBuildKind Kind, NativeStudioBuildStage Stage, string Status, string Destination, string Fingerprint,
    IReadOnlyList<MapDiagnostic> Diagnostics, IReadOnlyList<MapBudget> Budgets, bool ResultCurrent)
{
    public bool Busy => Stage is NativeStudioBuildStage.Running or NativeStudioBuildStage.Canceling;
}
public interface INativeStudioEditorBuild
{
    NativeStudioBuildSnapshot CaptureBuild();
    NativeStudioResult BeginBuild(NativeStudioEditTicket ticket, NativeStudioBuildKind kind, string destination = "");
    NativeStudioResult ConfirmBuildPackage(Guid operation);
    void CancelBuild(Guid operation);
    void ClearBuild(Guid operation);
    NativeStudioResult SelectBuildProblem(Guid operation, int absoluteIndex);
}

/// <summary>Owner-thread presentation of retained build work and its frozen diagnostic identities.</summary>
public sealed class NativeStudioBuildController
{
    public const int PageSize = 8;
    private readonly INativeStudioEditorSession _session;
    private readonly INativeStudioEditorBuild? _build;
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private NativeStudioBuildSnapshot _shown;
    private int _diagnosticPage, _budgetPage;
    private long _revision;
    private string _notice = "";
    public NativeStudioBuildController(INativeStudioEditorSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _build = session as INativeStudioEditorBuild;
        _shown = Freeze(_build?.CaptureBuild() ?? new(0, Guid.Empty, session.Capture().Ticket,
            NativeStudioBuildKind.Validate, NativeStudioBuildStage.Idle, "BUILD UNAVAILABLE", "", "",
            Array.Empty<MapDiagnostic>(), Array.Empty<MapBudget>(), false));
    }
    public sealed record Snapshot(long Revision, NativeStudioBuildSnapshot Owner, int DiagnosticPage,
        int DiagnosticPageCount, int BudgetPage, int BudgetPageCount, IReadOnlyList<MapDiagnostic> Rows,
        IReadOnlyList<MapBudget> Budgets, string Notice);
    public Snapshot Current()
    {
        Check();
        if (_build != null)
        {
            var current = _build.CaptureBuild();
            if (current.Operation != _shown.Operation) _diagnosticPage = _budgetPage = 0;
            if ((current with { Diagnostics = _shown.Diagnostics, Budgets = _shown.Budgets }) != _shown
                || !current.Diagnostics.SequenceEqual(_shown.Diagnostics) || !current.Budgets.SequenceEqual(_shown.Budgets))
            { _shown = Freeze(current); _notice = ""; _revision++; }
        }
        _diagnosticPage = Math.Min(_diagnosticPage, Pages(_shown.Diagnostics.Count) - 1);
        _budgetPage = Math.Min(_budgetPage, Pages(_shown.Budgets.Count) - 1);
        return new(_revision, _shown, _diagnosticPage, Pages(_shown.Diagnostics.Count), _budgetPage,
            Pages(_shown.Budgets.Count), Array.AsReadOnly(_shown.Diagnostics.Skip(_diagnosticPage * PageSize).Take(PageSize).ToArray()),
            Array.AsReadOnly(_shown.Budgets.Skip(_budgetPage * PageSize).Take(PageSize).ToArray()),
            _notice.Length > 0 ? _notice : _shown.Status);
    }
    public bool Begin(NativeStudioBuildKind kind, string destination = "")
    {
        Check();
        if (_build == null || !Enum.IsDefined(kind) || _build.CaptureBuild().Busy) return false;
        var current = _session.Capture();
        return current.Alive && Result(_build.BeginBuild(current.Ticket, kind, destination));
    }
    public bool ConfirmPackage()
    {
        Check();
        return _build != null && _shown.Stage == NativeStudioBuildStage.ReadyPackage && _shown.ResultCurrent
            && !_build.CaptureBuild().Busy && Result(_build.ConfirmBuildPackage(_shown.Operation));
    }
    public void Cancel()
    { Check(); if (_build != null && _shown.Busy) _build.CancelBuild(_shown.Operation); }
    public void Clear()
    { Check(); if (_build != null && !_shown.Busy && !_build.CaptureBuild().Busy) _build.ClearBuild(_shown.Operation); }
    public bool ProblemsPage(int direction)
    { Check(); return Page(ref _diagnosticPage, _shown.Diagnostics.Count, direction); }
    public bool BudgetsPage(int direction)
    { Check(); return Page(ref _budgetPage, _shown.Budgets.Count, direction); }
    public bool SelectProblem(int slot)
    {
        Check(); int index = _diagnosticPage * PageSize + slot;
        return _build != null && !_shown.Busy && slot >= 0 && slot < PageSize && index < _shown.Diagnostics.Count
            && Result(_build.SelectBuildProblem(_shown.Operation, index));
    }
    private static NativeStudioBuildSnapshot Freeze(NativeStudioBuildSnapshot value) => value with
    { Diagnostics = Array.AsReadOnly(value.Diagnostics.ToArray()), Budgets = Array.AsReadOnly(value.Budgets.ToArray()) };
    private static int Pages(int count) => Math.Max(1, (count + PageSize - 1) / PageSize);
    private bool Page(ref int page, int count, int direction)
    { int next = page + direction; if (direction is not (-1 or 1) || next < 0 || next >= Pages(count)) return false; page = next; _revision++; return true; }
    private bool Result(NativeStudioResult result) { _notice = result.Message; _revision++; return result.Success; }
    private void Check()
    { if (Environment.CurrentManagedThreadId != _thread) throw new InvalidOperationException("Native Studio build requires its owner thread."); }
}
