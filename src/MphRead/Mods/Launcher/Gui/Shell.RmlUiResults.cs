#if MPHREAD_RMLUI_POC && !ANDROID
using System;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.RmlUi.Pages.InGame;
using MphRead.Mods.Training;

namespace MphRead.Mods.Launcher.Gui;

internal static partial class Shell
{
    private static MatchResultsPagePresenter? _nativeResults;
    private static MatchResultsController? _nativeResultsController;
    private static AimResultsPagePresenter? _nativeAimResults;
    private static AimResultsController? _nativeAimResultsController;
    private static AimTrainerSession? _nativeTrainingResults;

    private static void RetireNativeResults()
    {
        RetireNativeOwner(ref _nativeResults);
        RetireNativeOwner(ref _nativeResultsController);
        RetireNativeOwner(ref _nativeAimResults);
        RetireNativeOwner(ref _nativeAimResultsController);
        _nativeTrainingResults = null;
        EndScreen.PanelUp = false;
        EndScreen.NativeReportLeft = EndScreen.NativeReportTop = 0;
    }

    private static bool TickNativeEndPanel()
    {
        if (!RmlUiPrototype.Active || RmlUiPrototype.Pages is not { } composition) return false;
        bool want = _window?.HasScene == true && !_matchLoading && EndScreen.PanelAvailable
            && _nativePause == null && _nativeSettings == null && _nativePlayback == null && _nativeAimResults == null;
        if (want && _nativeResults == null)
        {
            RetireNativePages(); composition.Suspend();
            _nativeResultsController = new();
            _nativeResults = new(RmlUiPrototype.Runtime, composition.Manager, _nativeResultsController);
            _nativeResults.Open();
            // Resolve the native panel before the first scoreboard draw.
            RmlUiPrototype.Runtime.Update();
            EndScreen.PanelUp = true;
            WireNativePages(); RmlUiPrototype.ShowGameplayMenu();
        }
        else if (!want && _nativeResults != null)
        {
            RetireNativeResults();
            if (!HasNativePage) RmlUiPrototype.Hide();
        }
        _nativeResults?.Refresh();
        if (_nativeResults?.Active == true && _window != null
            && RmlUiPrototype.Runtime.TryGetElementBounds(_nativeResults.Document, "results_panel", out float x, out float y, out _, out _))
        {
            EndScreen.NativeReportLeft = x / Math.Max(1, _window.FramebufferSize.X);
            EndScreen.NativeReportTop = y / Math.Max(1, _window.FramebufferSize.Y);
        }
        DrainNativeResultEffects();
        return true;
    }

    private static bool OpenNativeAimResults(AimTrainerSession session)
    {
        if (!RmlUiPrototype.Active || RmlUiPrototype.Pages is not { } composition) return false;
        RetireNativePages(); composition.Suspend();
        _nativeTrainingResults = session;
        _nativeAimResultsController = new(AimResultsReport.From(session));
        _nativeAimResults = new(RmlUiPrototype.Runtime, composition.Manager, _nativeAimResultsController);
        _nativeAimResults.Open();
        WireNativePages(); RmlUiPrototype.ShowGameplayMenu();
        session.ResultsShown = true;
        return true;
    }

    private static void DrainNativeResultEffects()
    {
        if (_nativeResults?.TryTakeEffect(out var effect) == true)
        {
            if (effect == MatchResultsEffect.Close) OpenNativePause();
            else if (effect == MatchResultsEffect.Rematch)
            { RetireNativePages(); RmlUiPrototype.Hide(); }
        }
        if (_nativeAimResults?.TryTakeAction(out var action) == true && _nativeTrainingResults is { } training)
        {
            var definition = training.Definition; var plan = training.Plan;
            RetireNativePages(); RmlUiPrototype.Hide();
            if (action == AimResultsAction.Retry)
            { _endMatch = true; _pending = AimTrainerLaunch.Create(definition.Retry(), plan.Hunter, LauncherPrefs.LastColor); }
            else
            { _focusTrainingOnReturn = action == AimResultsAction.ChangeDrill; RequestEndMatch(); }
        }
    }
}
#endif
