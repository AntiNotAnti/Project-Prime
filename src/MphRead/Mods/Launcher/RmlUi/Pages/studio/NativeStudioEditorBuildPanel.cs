#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using MphRead.Mods.Launcher.RmlUi.Host;

namespace MphRead.Mods.Launcher.RmlUi.Pages.Studio;

internal sealed partial class NativeStudioEditorPage
{
    private NativeStudioBuildController? _buildControls;
    private NativeStudioBuildController.Snapshot? _buildView;
    private long _buildPresented = -1;
    private bool _buildOpen, _buildDestinationInitialized;
    private NativeStudioBuildController BuildControls => _buildControls ??= new(_session);
    private bool HandleBuildIntent(int action)
    {
        if (action == 223) { _buildOpen = !_buildOpen; _buildPresented = -1; return true; }
        if (!_buildOpen || _session is not INativeStudioEditorBuild) return false;
        var controls = BuildControls;
        switch (action)
        {
            case 200: controls.Begin(NativeStudioBuildKind.Validate); break;
            case 201: controls.Begin(NativeStudioBuildKind.Runtime); break;
            case 202:
                if (_buildView?.Owner.Stage == NativeStudioBuildStage.ReadyPackage) controls.ConfirmPackage();
                else controls.Begin(NativeStudioBuildKind.Package, _host.ReadField(_page, "native_build_destination", 32767));
                break;
            case 203: controls.Begin(NativeStudioBuildKind.PlayCamera); break;
            case 204: controls.Begin(NativeStudioBuildKind.PlaySelection); break;
            case 205: controls.Begin(NativeStudioBuildKind.Navigation); break;
            case 206: controls.Begin(NativeStudioBuildKind.Audit); break;
            case 207: controls.Cancel(); break;
            case 208: controls.ProblemsPage(-1); break;
            case 209: controls.ProblemsPage(1); break;
            case 210: controls.BudgetsPage(-1); break;
            case 211: controls.BudgetsPage(1); break;
            case 220: controls.Clear(); break;
            case 221: controls.Begin(NativeStudioBuildKind.PlayTeamA); break;
            case 222: controls.Begin(NativeStudioBuildKind.PlayTeamB); break;
            default:
                if (action is < 212 or > 219) return false;
                if (controls.SelectProblem(action - 212)) { _controller.AcceptSelection(); _details.Reset(); }
                break;
        }
        return true;
    }
    private void RefreshBuild()
    {
        _buildView = BuildControls.Current();
        if (!_buildDestinationInitialized)
        {
            string path = _session.Capture().Path;
            if (!String.IsNullOrWhiteSpace(path))
            {
                try { _host.SetField(_page, "native_build_destination", Path.ChangeExtension(path, ".ppmap")); }
                catch (ArgumentException) { }
            }
            _buildDestinationInitialized = true;
        }
    }
    private void BindBuild(Dictionary<string, RmlUiBindingValue> bindings)
    {
        if (_buildView is not { } view) return;
        var owner = view.Owner;
        bool available = _session is INativeStudioEditorBuild, busy = owner.Busy;
        bool reviewed = owner.Stage == NativeStudioBuildStage.ReadyPackage;
        bindings["visible:native_build_panel"] = RmlUiBindingValue.FromBoolean(_buildOpen);
        bindings["disabled:native_build_toggle"] = RmlUiBindingValue.FromBoolean(!available);
        bindings["class:native_build_toggle:selected"] = RmlUiBindingValue.FromBoolean(_buildOpen);
        bindings["native_build_status"] = RmlUiBindingValue.FromText((busy ? "BUSY // " : "") + owner.Status);
        bindings["native_build_notice"] = RmlUiBindingValue.FromText(view.Notice == owner.Status ? "" : view.Notice);
        bindings["native_build_current"] = RmlUiBindingValue.FromText(owner.Stage == NativeStudioBuildStage.Idle ? "NO BUILD RESULT"
            : busy ? "WORKING ON CAPTURED PROJECT"
            : owner.ResultCurrent ? "RESULT MATCHES CURRENT PROJECT" : "PROJECT CHANGED // RESULT RETAINED FOR REFERENCE");
        bindings["native_build_review"] = RmlUiBindingValue.FromText(owner.Destination.Length == 0 ? ""
            : "PACKAGE DESTINATION // " + owner.Destination + (reviewed ? "\nReview this exact path before confirming export." : ""));
        bindings["native_build_fingerprint"] = RmlUiBindingValue.FromText(owner.Fingerprint.Length == 0 ? "" : "CONTENT // " + owner.Fingerprint);
        bindings["native_build_package"] = RmlUiBindingValue.FromText(reviewed ? "CONFIRM REVIEWED PACKAGE EXPORT" : "REVIEW PACKAGE EXPORT");
        foreach (string name in new[] { "validate", "runtime", "play_camera", "play_selection", "play_team_a", "play_team_b", "navigation", "audit" })
            bindings["disabled:native_build_" + name] = RmlUiBindingValue.FromBoolean(!available || busy || reviewed);
        bindings["disabled:native_build_package"] = RmlUiBindingValue.FromBoolean(!available || busy || reviewed && !owner.ResultCurrent);
        bindings["disabled:native_build_destination"] = RmlUiBindingValue.FromBoolean(!available || busy || reviewed);
        bindings["disabled:native_build_cancel"] = RmlUiBindingValue.FromBoolean(!busy || owner.Stage == NativeStudioBuildStage.Canceling);
        bindings["disabled:native_build_clear"] = RmlUiBindingValue.FromBoolean(busy || owner.Stage == NativeStudioBuildStage.Idle);
        bindings["native_build_problems_page"] = RmlUiBindingValue.FromText($"PROBLEMS {view.DiagnosticPage + 1} / {view.DiagnosticPageCount} // {owner.Diagnostics.Count} TOTAL");
        bindings["native_build_budgets_page"] = RmlUiBindingValue.FromText($"BUDGETS {view.BudgetPage + 1} / {view.BudgetPageCount} // {owner.Budgets.Count} TOTAL");
        bindings["disabled:native_build_problems_previous"] = RmlUiBindingValue.FromBoolean(busy || view.DiagnosticPage == 0);
        bindings["disabled:native_build_problems_next"] = RmlUiBindingValue.FromBoolean(busy || view.DiagnosticPage + 1 >= view.DiagnosticPageCount);
        bindings["disabled:native_build_budgets_previous"] = RmlUiBindingValue.FromBoolean(busy || view.BudgetPage == 0);
        bindings["disabled:native_build_budgets_next"] = RmlUiBindingValue.FromBoolean(busy || view.BudgetPage + 1 >= view.BudgetPageCount);
        for (int i = 0; i < NativeStudioBuildController.PageSize; i++)
        {
            var row = i < view.Rows.Count ? view.Rows[i] : null;
            bindings["visible:native_build_problem" + i] = RmlUiBindingValue.FromBoolean(row != null);
            bindings["native_build_problem" + i] = RmlUiBindingValue.FromText(row == null ? "" : $"{row.Severity} // {row.Code}\n{row.Message}");
            bindings["disabled:native_build_problem" + i] = RmlUiBindingValue.FromBoolean(busy || !owner.ResultCurrent || row?.ObjectId is not { } id || id == Guid.Empty);
            var budget = i < view.Budgets.Count ? view.Budgets[i] : null;
            bindings["visible:native_build_budget" + i] = RmlUiBindingValue.FromBoolean(budget != null);
            bindings["native_build_budget" + i] = RmlUiBindingValue.FromText(budget == null ? "" : budget.Name + " // "
                + budget.Used.ToString("N0", CultureInfo.InvariantCulture) + (budget.Limit is { } limit ? " / " + limit.ToString("N0", CultureInfo.InvariantCulture) : ""));
        }
        // The retained owner refuses edits during work; communicate that fence in the native controls too.
        foreach (string id in new[] { "native_editor_undo", "native_editor_redo", "native_editor_save", "native_editor_publish", "native_editor_legacy",
            "native_editor_move", "native_editor_rotate", "native_editor_scale", "native_editor_path", "native_editor_move_x", "native_editor_move_y", "native_editor_move_z", "native_editor_angle", "native_editor_factor" })
            if (busy) bindings["disabled:" + id] = RmlUiBindingValue.FromBoolean(true);
        // Restore inputs which have no other capability-specific binding when work retires.
        if (!busy) foreach (string id in new[] { "native_editor_save", "native_editor_publish", "native_editor_legacy", "native_editor_path",
            "native_editor_move_x", "native_editor_move_y", "native_editor_move_z", "native_editor_angle", "native_editor_factor" })
            bindings["disabled:" + id] = RmlUiBindingValue.FromBoolean(false);
        _buildPresented = view.Revision;
    }
}
#endif
