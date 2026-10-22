#if PROJECTPRIME_RMLUI_ANDROID_EXPERIMENT
using System;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.RmlUi.Core;
using MphRead.Mods.Network;
using MphRead.Mods.Render;

namespace MphRead.Droid;

internal sealed partial class GameView
{
    private RmlUiAndroidLauncherSession? _nativeLauncher => _loop.NativeLauncher;
    internal Guid NativeLauncherLifetime => _nativeLauncher?.Lifetime ?? Guid.Empty;
    internal void RequestNativeLauncherBack() => _nativeLauncher?.RequestBack();
    internal bool NativeLauncherOpen(Guid lifetime) => _nativeLauncher?.Opened == true && _nativeLauncher.Lifetime == lifetime;
    internal bool NativeLauncherCanManageSaves(Guid lifetime) => _nativeLauncher?.CanManageSaves(lifetime) == true;
    internal bool NativeLauncherCurrent(RmlUiAndroidLauncherRequest<RmlUiAndroidLocalLaunchRequest> witness)
        => _nativeLauncher?.IsCurrent(witness) == true;
    internal bool AcceptNativeLauncher(RmlUiAndroidLauncherRequest<RmlUiAndroidLocalLaunchRequest> witness, LaunchPlan plan)
    {
        if (_nativeLauncher?.TryAccept(witness, plan) != true) return false;
        _loop.WakeNativeLauncher(); return true;
    }
    internal RmlUiAndroidLocalWitness? NativeLocalState => _loop.NativeLocalState;
    internal bool NativeLocalCurrent(RmlUiAndroidLocalWitness witness) => _loop.NativeLocalCurrent(witness);
    internal bool TryReturnNativeHome(RmlUiAndroidLocalWitness witness) => _loop.TryReturnNativeHome(witness);
    internal bool AcknowledgeNativeHome(RmlUiAndroidLocalWitness witness, RmlUiAndroidLauncherOptions options)
        => _loop.AcknowledgeNativeHome(witness, options);
    internal Task NativeStopped => _loop.NativeStopped;

    private sealed partial class RenderLoop
    {
        private RmlUiAndroidLauncherSession? _nativeLauncher;
        private readonly Func<RmlUiAndroidLauncherOptions, RmlUiAndroidLauncherSession>? _nativeLauncherFactory;
        private readonly RmlUiAndroidLocalCycle<RmlUiAndroidLauncherOptions>? _nativeCycle;
        private RmlUiAndroidLocalWitness? _nativeLoading;
        private long _nativeSceneGeneration;
        private bool _nativeCyclicOffline;
        private int _nativeFocused = 1;
        private readonly TaskCompletionSource _nativeStopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal RmlUiAndroidLauncherSession? NativeLauncher => Volatile.Read(ref _nativeLauncher);
        internal RmlUiAndroidLocalWitness? NativeLocalState => _nativeCycle?.Current;
        internal bool NativeLocalCurrent(RmlUiAndroidLocalWitness witness) => _nativeCycle?.IsCurrent(witness) == true;
        internal bool NativeTransitionOwnsInput => _nativeCycle?.Current.Phase is RmlUiAndroidLocalPhase.Loading or RmlUiAndroidLocalPhase.ReturnRequested
            or RmlUiAndroidLocalPhase.Retiring or RmlUiAndroidLocalPhase.AwaitingHomeAck or RmlUiAndroidLocalPhase.HomeAccepted;
        internal Task NativeStopped => _nativeStopped.Task;
        internal void WakeNativeLauncher() { lock (_lock) Monitor.PulseAll(_lock); }
        internal void CancelNativeLoad() { RequestStop(); _onEnd(); }
        internal void SetNativeFocused(bool focused)
        {
            Volatile.Write(ref _nativeFocused, focused ? 1 : 0);
            NativePause?.SetFocused(focused); NativeLauncher?.SetFocused(focused);
        }
        internal bool TryReturnNativeHome(RmlUiAndroidLocalWitness witness)
        {
            if (_nativeCycle?.TryRequestReturn(witness) != true) return false;
            _nativePause?.SuspendInput(); _nativeLauncher?.SuspendInput();
            WakeNativeLauncher(); return true;
        }
        internal bool AcknowledgeNativeHome(RmlUiAndroidLocalWitness witness, RmlUiAndroidLauncherOptions options)
        {
            if (_nativeCycle?.TryAcknowledgeHome(witness, options) != true) return false;
            WakeNativeLauncher(); return true;
        }
        private bool ProcessNativeLocalMode()
        {
            var phase = _nativeCycle!.Current.Phase;
            if (phase == RmlUiAndroidLocalPhase.ReturnRequested && Scene is { } ending)
                End(ending);
            phase = _nativeCycle.Current.Phase;
            if (phase is RmlUiAndroidLocalPhase.Faulted or RmlUiAndroidLocalPhase.Stopped) return false;
            if (_nativeCycle.TryTakeHome(out var options))
            {
                var fresh = _nativeLauncherFactory!(options!);
                Volatile.Write(ref _nativeLauncher, fresh);
                fresh.SetFocused(Volatile.Read(ref _nativeFocused) != 0);
                if (_nativeCycle.Current.Phase == RmlUiAndroidLocalPhase.Stopped) fresh.RequestStop();
            }
            if (_nativeCycle.Current.Phase == RmlUiAndroidLocalPhase.Home) return DrawNativeLauncherFrame();
            // No scene, simulation, network pump, or old Build delegate while
            // the exact UI reset/return acknowledgement is outstanding.
            _clock.Start(); RequestFrameRate(); ApplySwapInterval(); WaitForTick();
            GL.ClearColor(new OpenTK.Mathematics.Color4(10 / 255f, 12 / 255f, 16 / 255f, 1));
            GL.Clear(OpenTK.Graphics.OpenGL.ClearBufferMask.ColorBufferBit);
            if (!Android.Opengl.EGL14.EglSwapBuffers(_display, _eglSurface))
                HandleFailedPresent(Android.Opengl.EGL14.EglGetError());
            return true;
        }
        private bool DrawNativeLauncherFrame()
        {
            var launcher = _nativeLauncher;
            if (launcher == null) return false;
            if (launcher.TryTakePlan(out LaunchPlan plan, out var request))
            {
                if (_nativeCycle!.Current.Phase == RmlUiAndroidLocalPhase.Stopped) return false;
                if (plan.Kind is not (LaunchKind.Offline or LaunchKind.Adventure)
                    || !GameFiles.Ready || NetSession.Active || DemoPlayback.IsActive || MatchStart.HasPendingCommit
                    || !request.MatchesPlan(plan, LauncherPrefs.LastColor)
                    || !request.IsCurrent(RmlUiAndroidLocalLaunchRequest.ReadSlot, () => MatchStart.RecentShipDock)
                    || !_nativeCycle.TryBeginMatch(out var loading))
                    throw new InvalidOperationException("The local cycle cannot admit this match.");
                _nativeLoading = loading; _nativeCyclicOffline = true; _ended = false;
                // Each match gets an independently fenced Pause route. A
                // retired route can never resume into a newer scene.
                _nativePause?.DisposeOwner();
                Volatile.Write(ref _nativePause, _nativePauseFactory());
                _nativePause?.SetFocused(Volatile.Read(ref _nativeFocused) != 0);
                if (_nativeCycle.Current.Phase == RmlUiAndroidLocalPhase.Stopped)
                { _nativePause?.RequestStop(); return false; }
                _build = (input, size) => AndroidMatch.Build(input, size, plan,
                    () => TryReturnNativeHome(loading), candidate => Scene = candidate);
                _clock.Restart(); _framePacer.Reset(0); FrameTiming.Reset();
                BuildScene(); return true;
            }
            _clock.Start(); RequestFrameRate(); ApplySwapInterval(); WaitForTick();
            GL.ClearColor(new OpenTK.Mathematics.Color4(10 / 255f, 12 / 255f, 16 / 255f, 1));
            GL.Clear(OpenTK.Graphics.OpenGL.ClearBufferMask.ColorBufferBit);
            if (!launcher.Render(_size.X, _size.Y, Volatile.Read(ref _nativeDensity))) return false;
            if (!Android.Opengl.EGL14.EglSwapBuffers(_display, _eglSurface))
                HandleFailedPresent(Android.Opengl.EGL14.EglGetError());
            return true;
        }
        private bool TryEndNativeLocalScene(Scene scene)
        {
            bool stopping; lock (_lock) stopping = _stopping;
            if (!_nativeCyclicOffline || _nativeCycle == null || stopping || NetSession.Active || DemoPlayback.IsActive
                || scene.LifetimeGeneration != _nativeSceneGeneration) return false;
            var current = _nativeCycle.Current;
            if (current.Phase == RmlUiAndroidLocalPhase.Match) _nativeCycle.TryRequestReturn(current);
            bool returned = _nativeCycle.TryRetire(() => RetireNativeScene(scene), out var witness);
            Scene = null; _nativeCyclicOffline = false; _nativeLoading = null;
            _build = (_, _) => throw new InvalidOperationException("No current local plan has been admitted.");
            if (returned)
            {
                _ended = false;
                _nativeLauncher!.NotifySceneRetired(witness);
            }
            else
            {
                _ended = true;
                _onError("The local match could not retire safely. Returning to the existing launcher.");
                RequestStop();
            }
            return true;
        }
        private bool RetireNativeScene(Scene scene)
        {
            var context = new CleanupContext(this);
            void Failure(Exception error) => Console.WriteLine("[android/rmlui] local retirement: " + error.Message);
            return RmlUiAndroidCleanupContext.Run(context, valid => RmlUiAndroidLocalRetirement.Run(
                new LocalRetirement(this, scene, context, valid), Failure), Failure);
        }
        private sealed class CleanupContext(RenderLoop loop) : IRmlUiAndroidCleanupContext
        {
            private Android.Opengl.EGLSurface? _pbuffer;
            public bool CurrentMatchesOwner => loop._context != null
                && Android.Opengl.EGL14.EglGetCurrentContext()?.Equals(loop._context) == true;
            public bool HasForeignCurrentContext
            {
                get
                {
                    var current = Android.Opengl.EGL14.EglGetCurrentContext();
                    return current != null && !current.Equals(Android.Opengl.EGL14.EglNoContext) && !CurrentMatchesOwner;
                }
            }
            public bool TryBindSurfaceless() => loop._display != null && loop._context != null
                && Android.Opengl.EGL14.EglMakeCurrent(loop._display, Android.Opengl.EGL14.EglNoSurface,
                    Android.Opengl.EGL14.EglNoSurface, loop._context);
            public bool TryCreatePbuffer()
            {
                if (loop._display == null || loop._config == null) return false;
                _pbuffer = Android.Opengl.EGL14.EglCreatePbufferSurface(loop._display, loop._config,
                    new[] { Android.Opengl.EGL14.EglWidth, 1, Android.Opengl.EGL14.EglHeight, 1, Android.Opengl.EGL14.EglNone }, 0);
                if (_pbuffer == null || _pbuffer.Equals(Android.Opengl.EGL14.EglNoSurface)) { _pbuffer = null; return false; }
                return true;
            }
            public bool TryBindPbuffer() => _pbuffer != null && loop._display != null && loop._context != null
                && Android.Opengl.EGL14.EglMakeCurrent(loop._display, _pbuffer, _pbuffer, loop._context);
            public void UnbindTemporary()
            {
                if (loop._display != null && !Android.Opengl.EGL14.EglMakeCurrent(loop._display,
                    Android.Opengl.EGL14.EglNoSurface, Android.Opengl.EGL14.EglNoSurface, Android.Opengl.EGL14.EglNoContext))
                    throw new InvalidOperationException("The temporary cleanup binding could not be released.");
            }
            public void DestroyPbuffer()
            {
                var surface = _pbuffer; _pbuffer = null;
                if (surface != null && loop._display != null && !Android.Opengl.EGL14.EglDestroySurface(loop._display, surface))
                    throw new InvalidOperationException("The temporary cleanup surface could not be destroyed.");
            }
        }
        private sealed class LocalRetirement(RenderLoop loop, Scene scene, CleanupContext context, bool contextValid) : IRmlUiAndroidLocalRetirement
        {
            private PostMatchCommitContext? _commit;
            public void ReleaseInput()
            {
                loop._nativePause?.SuspendInput(); loop._nativeLauncher?.SuspendInput();
                loop._input.ReleaseAll(); loop._controls.ReleaseEverything();
                scene.ModSetLateAim(0, 0);
                Mods.Input.GamepadInput.InvalidatePresentationAim(); Mods.Input.NativeTouchHost.Publish(false);
            }
            public bool RetireNative()
            {
                loop._nativePause?.DisposeOwner();
                return loop._nativeLauncher?.DisposeOwner() != false && RmlUiAndroidPauseNativeLease.AvailableForNewOwner;
            }
            public void CaptureCommit() => _commit = MatchStart.PrepareBeforeTeardown(scene.GameState);
            public void CleanupScene() => scene.DoCleanup();
            public void UnloadScene()
            {
                if (!contextValid || !context.CurrentMatchesOwner) throw new InvalidOperationException("Scene GL release requires its exact existing context.");
                scene.UnloadGl();
            }
            public void Commit()
            {
                // Active room transitions preserve the established capture
                // point after DoCleanup has cancelled that transition.
                _commit ??= MatchStart.PrepareAfterMatch(scene.GameState);
                MatchStart.AfterMatch(_commit);
            }
            public bool ReleaseForeground() => _commit != null && ForegroundSceneBinding.TryRelease(_commit);
            public void ResetPresentation()
            {
                loop._spectatorRequest = 0;
                loop._dialogClickDown = loop._menuWasHeld = loop._spectateCycleHeld = loop._spectateViewHeld = false;
                loop._replayPlayHeld = loop._replayBackHeld = loop._replayForwardHeld = loop._replayPausedShown = false;
                loop._missileWasHeld = loop._chatWasHeld = loop._keyboardShown = false;
                loop._uiPixels = Array.Empty<byte>(); loop._uiVersion = 0;
                loop._presentationInputRevision = Mods.Input.GamepadContexts.Revision;
                loop._onSoftKeyboard(false); AndroidUiOverlay.Visible = false;
                Mods.PauseMenu.Reset(); Mods.SpectatorMode.Reset();
                loop._clock.Restart(); loop._framePacer.Reset(0); FrameTiming.Reset();
            }
        }
        private bool RetireTerminalNativeScene(Scene scene)
        {
            if (_nativeCycle == null) return false;
            _nativeCycle.Fault();
            try
            {
                RetireNativeScene(scene);
            }
            finally
            {
                Scene = null; _nativeCyclicOffline = false; _nativeLoading = null;
                _build = (_, _) => throw new InvalidOperationException("No current local plan has been admitted.");
            }
            return true;
        }
    }
}
#endif
