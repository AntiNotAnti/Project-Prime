using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MphRead.Identity;
using MphRead.Mods;
using MphRead.Mods.Accounts;
using MphRead.Mods.Input;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.Gui;
using MphRead.Mods.Network;
using ProjectPrime.Server.Shared;
using Xunit;

namespace MphRead.Tests.Client;

[Collection(AvaloniaUiCollection.Name)]
public sealed class PrimeTitleScreenTests
{
    [Fact]
    public void LifecycleIsMonotonicAndDismissesOnlyFromReady()
    {
        var lifecycle = new PrimeTitleScreenLifecycle();

        Assert.Equal(PrimeTitleScreenPhase.Loading, lifecycle.Phase);
        Assert.False(lifecycle.BeginDismissal());
        Assert.True(lifecycle.MarkReady());
        Assert.False(lifecycle.MarkReady());
        Assert.True(lifecycle.BeginDismissal());
        Assert.False(lifecycle.BeginDismissal());
        Assert.True(lifecycle.CompleteDismissal());
        Assert.False(lifecycle.IsBlocking);
        Assert.False(lifecycle.HideForTeardown());
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new PrimeTitleScreenLifecycle(PrimeTitleScreenPhase.Dismissing));
    }

    [Fact]
    public void InputGateRequiresReleasedKeyboardEdgeAndPhysicalControllerButton()
    {
        var input = new PrimeTitleInputState();
        Assert.True(input.PressKey(Key.Space));
        Assert.False(input.PressKey(Key.Space));
        input.ReleaseKey(Key.Space);
        Assert.True(input.PressKey(Key.Space));

        input.SeedController(new GamepadState
        {
            Connected = true,
            Buttons = GamepadButtons.A
        });
        Assert.Equal(GamepadButtons.None, input.ObserveController(new GamepadState
        {
            Connected = true,
            LeftTrigger = 1,
            RightTrigger = 1,
            LeftX = 1,
            Buttons = GamepadButtons.A
        }));
        Assert.Equal(GamepadButtons.None, input.ObserveController(new GamepadState
        {
            Connected = false
        }));
        Assert.Equal(GamepadButtons.None, input.ObserveController(new GamepadState
        {
            Connected = true,
            Buttons = GamepadButtons.X
        }));
        Assert.Equal(GamepadButtons.None, input.ObserveController(new GamepadState
        {
            Connected = true,
            LeftTrigger = 1,
            RightX = 1,
            Buttons = GamepadButtons.None
        }));
        Assert.Equal(GamepadButtons.B, input.ObserveController(new GamepadState
        {
            Connected = true,
            Buttons = GamepadButtons.B
        }));
    }

    [AvaloniaFact]
    public void ViewLoadsCanonicalResourceAndUsesResponsiveStretchAndExactPrompts()
    {
        using var view = new PrimeTitleScreenView(PrimeTitleScreenPhase.Ready,
            reducedMotion: true, allowBlink: false);
        Assert.True(view.HasBackgroundImage);
        Assert.Equal(new Thickness(24, 24, 24, 54), view.PromptMargin);

        view.Measure(new Size(1600, 900));
        view.Arrange(new Rect(0, 0, 1600, 900));
        Assert.Equal(Stretch.UniformToFill, view.BackgroundStretch);
        view.Arrange(new Rect(0, 0, 560, 800));
        Assert.Equal(Stretch.Uniform, view.BackgroundStretch);

        view.SetInputPrompt(PrimeInputDevice.KeyboardMouse, ControllerFamily.Generic);
        Assert.Equal("PRESS ANY KEY TO CONTINUE", view.Prompt);
        view.SetInputPrompt(PrimeInputDevice.Gamepad, ControllerFamily.PlayStation);
        Assert.Equal("PRESS ANY BUTTON TO CONTINUE", view.Prompt);
        view.SetInputPrompt(PrimeInputDevice.Touch, ControllerFamily.Generic);
        Assert.Equal("TAP TO CONTINUE", view.Prompt);
        Assert.False(view.BlinkRunning);
    }

    [AvaloniaFact]
    public void ViewFallbackIsBrandedAndBlinkRunsOnlyWhileAttached()
    {
        using var fallback = new PrimeTitleScreenView(PrimeTitleScreenPhase.Ready,
            reducedMotion: false, allowBlink: true,
            openBackground: () => throw new FileNotFoundException());
        Assert.False(fallback.HasBackgroundImage);
        Assert.False(fallback.BlinkRunning);

        var window = new Window { Width = 940, Height = 560, Content = fallback };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            Assert.True(fallback.BlinkRunning);
            Assert.Contains(fallback.GetVisualDescendants().OfType<TextBlock>(),
                text => text.Text == "PROJECT PRIME");
            Assert.Single(fallback.GetVisualDescendants().OfType<PrimeBrandMark>());
            Assert.Empty(fallback.GetVisualDescendants().OfType<PrimeDiamond>());
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
        Assert.False(fallback.BlinkRunning);
    }

    [AvaloniaFact]
    public void LoadingAbsorbsHeldKeyThenDismissesAfterReleaseAndRepress()
    {
        PrimeShellView shell = PrimeShellView.CreateTitleCapture(new MenuSettings(),
            Array.Empty<string>(), new(PrimeTitleScreenPhase.Loading,
                ReducedMotion: true));
        var window = new Window { Width = 940, Height = 560, Content = shell };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            Grid shellGrid = shell.GetVisualDescendants().OfType<Grid>()
                .Single(grid => grid.Name == "ShellGrid");
            Assert.False(shellGrid.IsEnabled);
            Assert.True(shell.HandleTitleKeyDown(Key.Space));
            Assert.Equal(PrimeTitleScreenPhase.Loading, shell.TitlePhase);

            shell.MarkTitleReady();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(shell.TitleScreen,
                window.FocusManager?.GetFocusedElement());
            Assert.True(shell.HandleTitleKeyDown(Key.Space));
            Assert.Equal(PrimeTitleScreenPhase.Ready, shell.TitlePhase);

            Assert.True(shell.HandleTitleKeyUp(Key.Space));
            Assert.True(shell.HandleTitleKeyDown(Key.Space));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(PrimeTitleScreenPhase.Hidden, shell.TitlePhase);
            Assert.True(shellGrid.IsEnabled);

            // The continue press stays captured until its matching release.
            Assert.True(shell.HandleTitleKeyDown(Key.Space));
            Assert.True(shell.HandleTitleKeyUp(Key.Space));
            Assert.False(shell.HandleTitleKeyDown(Key.Enter));
            Assert.False(shell.HandleTitleKeyUp(Key.Enter));
            Assert.False(shell.HandleTitlePointerPressed(PointerType.Mouse));
            Assert.False(shell.HandleTitlePointerReleased());
        }
        finally
        {
            window.Close();
            shell.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    [AvaloniaFact]
    public void BackResetAndReactivationNeverBypassOrReshowTitle()
    {
        PrimeShellView shell = PrimeShellView.CreateTitleCapture(new MenuSettings(),
            Array.Empty<string>(), new(PrimeTitleScreenPhase.Ready,
                ReducedMotion: true));
        PrimeRoute route = shell.CurrentRoute;
        Assert.True(shell.GoBack());
        Assert.Equal(route, shell.CurrentRoute);
        Assert.Equal(PrimeTitleScreenPhase.Ready, shell.TitlePhase);

        shell.HandleTitleKeyDown(Key.Enter);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(PrimeTitleScreenPhase.Hidden, shell.TitlePhase);
        shell.HandleTitleKeyUp(Key.Enter);
        shell.Reset();
        shell.Deactivate();
        shell.Activate();
        Assert.Equal(PrimeTitleScreenPhase.Hidden, shell.TitlePhase);
        shell.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    [AvaloniaTheory]
    [InlineData(true, PrimeRoute.Play)]
    [InlineData(false, PrimeRoute.Gateway)]
    public void InitialRestoreSelectsTheUnderlyingRouteBeforeTitleBecomesReady(
        bool restored, PrimeRoute expectedRoute)
    {
        PrimeShellView shell = new(new MenuSettings(), Array.Empty<string>(),
            restoreOnActivate: true, ignoreGameFileGate: true, captureMode: true,
            titleCaptureState: new(PrimeTitleScreenPhase.Loading,
                ReducedMotion: true),
            restoreSession: _ => Task.FromResult(restored));
        Assert.Equal(PrimeTitleScreenPhase.Loading, shell.TitlePhase);
        var window = new Window { Width = 940, Height = 560, Content = shell };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(expectedRoute, shell.CurrentRoute);
            Assert.Equal(PrimeTitleScreenPhase.Ready, shell.TitlePhase);
            Assert.Equal(restored ? PrimeStartupRestoreState.Restored
                : PrimeStartupRestoreState.NotRestored,
                shell.StartupRestoreState);
            Assert.NotNull(shell.TitleScreen);

            shell.HandleTitleKeyDown(Key.Enter);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(PrimeTitleScreenPhase.Hidden, shell.TitlePhase);
            Assert.Equal(expectedRoute, shell.CurrentRoute);
        }
        finally
        {
            window.Close();
            shell.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    [AvaloniaFact]
    public void FailedInitialRestoreStillMakesGatewayTitleReady()
    {
        PrimeShellView shell = new(new MenuSettings(), Array.Empty<string>(),
            restoreOnActivate: true, ignoreGameFileGate: true, captureMode: true,
            titleCaptureState: new(PrimeTitleScreenPhase.Loading,
                ReducedMotion: true),
            restoreSession: _ => Task.FromException<bool>(
                new IOException("capture restore failure")));
        var window = new Window { Width = 940, Height = 560, Content = shell };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(PrimeRoute.Gateway, shell.CurrentRoute);
            Assert.Equal(PrimeTitleScreenPhase.Ready, shell.TitlePhase);
            Assert.Equal(PrimeStartupRestoreState.NotRestored,
                shell.StartupRestoreState);
            Assert.NotNull(shell.TitleScreen);
        }
        finally
        {
            window.Close();
            shell.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    [AvaloniaFact]
    public void PendingRestoreDoesNotBecomeReadyWhenTheShellReactivates()
    {
        var restore = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        PrimeShellView shell = new(new MenuSettings(), Array.Empty<string>(),
            restoreOnActivate: true, ignoreGameFileGate: true, captureMode: true,
            titleCaptureState: new(PrimeTitleScreenPhase.Loading,
                ReducedMotion: true),
            restoreSession: token => restore.Task.WaitAsync(token));
        var window = new Window { Width = 940, Height = 560, Content = shell };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(PrimeTitleScreenPhase.Loading, shell.TitlePhase);

            window.Content = null;
            Dispatcher.UIThread.RunJobs();
            window.Content = shell;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(PrimeTitleScreenPhase.Loading, shell.TitlePhase);

            restore.SetResult(false);
            Assert.True(SpinWait.SpinUntil(() =>
            {
                Dispatcher.UIThread.RunJobs();
                return shell.TitlePhase == PrimeTitleScreenPhase.Ready;
            }, TimeSpan.FromSeconds(2)));
            Assert.Equal(PrimeRoute.Gateway, shell.CurrentRoute);
        }
        finally
        {
            restore.TrySetResult(false);
            window.Content = null;
            window.Close();
            shell.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    [AvaloniaFact]
    public void RestoreBudgetMakesTitleReadyWithoutCompletingRestore()
    {
        var restore = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var delay = new ControlledDelay();
        PrimeShellView shell = new(new MenuSettings(), Array.Empty<string>(),
            restoreOnActivate: true, ignoreGameFileGate: true, captureMode: true,
            titleCaptureState: new(PrimeTitleScreenPhase.Loading,
                ReducedMotion: true),
            restoreSession: _ => restore.Task,
            startupDelay: delay.WaitAsync);
        var window = new Window { Width = 940, Height = 560, Content = shell };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(PrimeStartupRestoreState.Pending,
                shell.StartupRestoreState);
            Assert.Equal(PrimeTitleScreenPhase.Loading, shell.TitlePhase);
            Assert.Equal(PrimeShellView.StartupRestoreUiBudget, delay.Requested);

            delay.Complete();
            Assert.True(SpinWait.SpinUntil(() =>
            {
                Dispatcher.UIThread.RunJobs();
                return shell.TitlePhase == PrimeTitleScreenPhase.Ready;
            }, TimeSpan.FromSeconds(2)));
            Assert.Equal(PrimeStartupRestoreState.Pending,
                shell.StartupRestoreState);
            Assert.Equal(PrimeRoute.Gateway, shell.CurrentRoute);
        }
        finally
        {
            restore.TrySetResult(false);
            window.Content = null;
            window.Close();
            shell.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    [AvaloniaFact]
    public void LateRestoreBeforeContinueStillSelectsPlay()
    {
        var restore = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var delay = new ControlledDelay();
        PrimeShellView shell = new(new MenuSettings(), Array.Empty<string>(),
            restoreOnActivate: true, ignoreGameFileGate: true, captureMode: true,
            titleCaptureState: new(PrimeTitleScreenPhase.Loading,
                ReducedMotion: true), restoreSession: _ => restore.Task,
            startupDelay: delay.WaitAsync);
        var window = new Window { Width = 940, Height = 560, Content = shell };
        window.Show();
        try
        {
            delay.Complete();
            Assert.True(SpinWait.SpinUntil(() =>
            {
                Dispatcher.UIThread.RunJobs();
                return shell.TitlePhase == PrimeTitleScreenPhase.Ready;
            }, TimeSpan.FromSeconds(2)));

            restore.SetResult(true);
            Assert.True(SpinWait.SpinUntil(() =>
            {
                Dispatcher.UIThread.RunJobs();
                return shell.StartupRestoreState
                    == PrimeStartupRestoreState.Restored;
            }, TimeSpan.FromSeconds(2)));
            Assert.Equal(PrimeRoute.Play, shell.CurrentRoute);

            shell.HandleTitleKeyDown(Key.Enter);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(PrimeTitleScreenPhase.Hidden, shell.TitlePhase);
            Assert.Equal(PrimeRoute.Play, shell.CurrentRoute);
        }
        finally
        {
            window.Content = null;
            window.Close();
            shell.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    [AvaloniaFact]
    public void ContinueAfterBudgetAbandonsLateInjectedRestoreAndEntersGateway()
    {
        var restore = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var delay = new ControlledDelay();
        PrimeShellView shell = new(new MenuSettings(), Array.Empty<string>(),
            restoreOnActivate: true, ignoreGameFileGate: true, captureMode: true,
            titleCaptureState: new(PrimeTitleScreenPhase.Loading,
                ReducedMotion: true), restoreSession: _ => restore.Task,
            startupDelay: delay.WaitAsync);
        var window = new Window { Width = 940, Height = 560, Content = shell };
        window.Show();
        try
        {
            delay.Complete();
            Assert.True(SpinWait.SpinUntil(() =>
            {
                Dispatcher.UIThread.RunJobs();
                return shell.TitlePhase == PrimeTitleScreenPhase.Ready;
            }, TimeSpan.FromSeconds(2)));

            shell.HandleTitleKeyDown(Key.Enter);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(PrimeStartupRestoreState.Abandoned,
                shell.StartupRestoreState);
            Assert.Equal(PrimeRoute.Gateway, shell.CurrentRoute);

            restore.SetResult(true);
            shell.StartupRestoreTask.GetAwaiter().GetResult();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(PrimeStartupRestoreState.Abandoned,
                shell.StartupRestoreState);
            Assert.Equal(PrimeRoute.Gateway, shell.CurrentRoute);
        }
        finally
        {
            restore.TrySetResult(false);
            window.Content = null;
            window.Close();
            shell.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    [AvaloniaFact]
    public void ContinueWinsAtGatewayIdentityCommitBarrier()
    {
        var account = new DeferredRestoreAccount();
        var entered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        PrimeShellView shell = new(new MenuSettings(), Array.Empty<string>(),
            restoreOnActivate: true, ignoreGameFileGate: true, captureMode: true,
            titleCaptureState: new(PrimeTitleScreenPhase.Loading,
                ReducedMotion: true),
            startupDelay: (_, _) => Task.CompletedTask,
            gatewayFactory: state => new GatewayController(state,
                _ => Task.FromResult<IPrimeGatewayAccount>(account), () =>
                {
                    entered.TrySetResult();
                    release.Wait();
                }));
        int identityEvents = 0;
        shell.Gateway.IdentityChanged += (_, _) => identityEvents++;
        var window = new Window { Width = 940, Height = 560, Content = shell };
        window.Show();
        try
        {
            Assert.True(SpinWait.SpinUntil(() =>
            {
                Dispatcher.UIThread.RunJobs();
                return shell.TitlePhase == PrimeTitleScreenPhase.Ready;
            }, TimeSpan.FromSeconds(2)));
            account.CompleteLicense();
            entered.Task.Wait(TimeSpan.FromSeconds(2));

            shell.HandleTitleKeyDown(Key.Enter);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(PrimeStartupRestoreState.Abandoned,
                shell.StartupRestoreState);
            Assert.Equal(PrimeRoute.Gateway, shell.CurrentRoute);
            release.Set();

            Assert.True(SpinWait.SpinUntil(() =>
            {
                Dispatcher.UIThread.RunJobs();
                return shell.StartupRestoreTask.IsCompleted;
            }, TimeSpan.FromSeconds(2)));
            Assert.False(shell.Gateway.State.SignedIn);
            Assert.Equal(0, identityEvents);
        }
        finally
        {
            release.Set();
            window.Content = null;
            window.Close();
            shell.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    [AvaloniaFact]
    public void DisposalAbandonsPendingStartupRestoreWithoutWaitingForSources()
    {
        var restore = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var delay = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        PrimeShellView shell = new(new MenuSettings(), Array.Empty<string>(),
            restoreOnActivate: true, ignoreGameFileGate: true, captureMode: true,
            titleCaptureState: new(PrimeTitleScreenPhase.Loading,
                ReducedMotion: true), restoreSession: _ => restore.Task,
            startupDelay: (_, _) => delay.Task);
        var window = new Window { Width = 940, Height = 560, Content = shell };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        window.Content = null;
        window.Close();
        shell.DisposeAsync().AsTask().GetAwaiter().GetResult();

        Assert.Equal(PrimeStartupRestoreState.Abandoned,
            shell.StartupRestoreState);
        Assert.Equal(PrimeTitleScreenPhase.Hidden, shell.TitlePhase);
        Assert.True(shell.ShellInputEnabled);
        restore.TrySetResult(true);
        delay.TrySetResult();
    }

    [AvaloniaFact]
    public void HeldContinueRepeatsAreQuarantinedBeforeTheFocusedChild()
    {
        PrimeShellView shell = PrimeShellView.CreateTitleCapture(new MenuSettings(),
            Array.Empty<string>(), new(PrimeTitleScreenPhase.Ready,
                ReducedMotion: true));
        var window = new Window { Width = 940, Height = 560, Content = shell };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            shell.HandleTitleKeyDown(Key.Enter);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(PrimeTitleScreenPhase.Hidden, shell.TitlePhase);

            Avalonia.Controls.Button target = shell.GetVisualDescendants()
                .OfType<Avalonia.Controls.Button>().First();
            target.Focus();
            int received = 0;
            target.KeyDown += (_, _) => received++;

            var repeated = new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.Enter
            };
            target.RaiseEvent(repeated);
            Assert.True(repeated.Handled);
            Assert.Equal(0, received);

            target.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyUpEvent,
                Key = Key.Enter
            });
            var fresh = new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.Space
            };
            target.RaiseEvent(fresh);
            Assert.False(fresh.Handled);
            Assert.Equal(1, received);
        }
        finally
        {
            window.Close();
            shell.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    [AvaloniaFact]
    public void ShellUsesPhysicalGamepadEdgesAndClassifiesPointerPrompts()
    {
        PrimeShellView shell = PrimeShellView.CreateTitleCapture(new MenuSettings(),
            Array.Empty<string>(), new(PrimeTitleScreenPhase.Loading,
                ReducedMotion: true));

        Assert.True(shell.HandleTitlePointerPressed(PointerType.Mouse));
        Assert.Equal("PRESS ANY KEY TO CONTINUE", shell.TitleScreen?.Prompt);
        shell.HandleTitlePointerReleased();
        Assert.True(shell.HandleTitlePointerPressed(PointerType.Touch));
        Assert.Equal("TAP TO CONTINUE", shell.TitleScreen?.Prompt);
        shell.HandleTitlePointerReleased();
        Assert.True(shell.HandleTitlePointerPressed(PointerType.Pen));
        Assert.Equal("TAP TO CONTINUE", shell.TitleScreen?.Prompt);
        shell.HandleTitlePointerReleased();

        shell.MarkTitleReady();
        shell.HandleTitleControllerState(new GamepadState
        {
            Connected = true,
            Buttons = GamepadButtons.A,
            Family = ControllerFamily.Xbox
        });
        Assert.Equal(PrimeTitleScreenPhase.Ready, shell.TitlePhase);
        Assert.Equal("PRESS ANY BUTTON TO CONTINUE", shell.TitleScreen?.Prompt);

        shell.HandleTitleControllerState(new GamepadState
        {
            Connected = true,
            LeftTrigger = 1,
            RightTrigger = 1,
            RightX = 1,
            Family = ControllerFamily.Xbox
        });
        Assert.Equal(PrimeTitleScreenPhase.Ready, shell.TitlePhase);
        shell.HandleTitleControllerState(new GamepadState
        {
            Connected = true,
            Buttons = GamepadButtons.B,
            Family = ControllerFamily.Xbox
        });
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(PrimeTitleScreenPhase.Hidden, shell.TitlePhase);

        shell.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    [AvaloniaFact]
    public void DisposalDuringFadeCannotLeaveShellInputLocked()
    {
        PrimeShellView shell = PrimeShellView.CreateTitleCapture(new MenuSettings(),
            Array.Empty<string>(), new(PrimeTitleScreenPhase.Ready));
        shell.HandleTitleKeyDown(Key.Enter);
        Assert.Equal(PrimeTitleScreenPhase.Dismissing, shell.TitlePhase);

        shell.DisposeAsync().AsTask().GetAwaiter().GetResult();

        Assert.Equal(PrimeTitleScreenPhase.Hidden, shell.TitlePhase);
        Assert.True(shell.ShellInputEnabled);
    }

    [AvaloniaFact]
    public void DetachDuringFadeCompletesWithoutFaultingOrLeavingInputLocked()
    {
        PrimeShellView shell = PrimeShellView.CreateTitleCapture(new MenuSettings(),
            Array.Empty<string>(), new(PrimeTitleScreenPhase.Ready));
        var window = new Window { Width = 940, Height = 560, Content = shell };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            shell.HandleTitleKeyDown(Key.Enter);
            Assert.Equal(PrimeTitleScreenPhase.Dismissing, shell.TitlePhase);
            Task dismissal = shell.TitleDismissalTask;

            window.Content = null;
            Dispatcher.UIThread.RunJobs();
            dismissal.GetAwaiter().GetResult();

            Assert.Equal(PrimeTitleScreenPhase.Hidden, shell.TitlePhase);
            Assert.True(shell.ShellInputEnabled);
        }
        finally
        {
            window.Content = null;
            window.Close();
            shell.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    [AvaloniaFact]
    public void SeatOfferIsDeferredUntilTitleHasFullyDismissed()
    {
        PrimeShellCaptureState state = SeatOfferState();
        PrimeShellView shell = PrimeShellView.CreateTitleCapture(new MenuSettings(),
            new[] { "MP3 PROVING GROUND" }, new(PrimeTitleScreenPhase.Ready,
                ReducedMotion: true), state);
        var window = new Window { Width = 940, Height = 560, Content = shell };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            Assert.False(shell.SeatOfferOverlayVisible);
            shell.HandleTitleKeyDown(Key.Enter);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(PrimeTitleScreenPhase.Hidden, shell.TitlePhase);
            Assert.True(shell.SeatOfferOverlayVisible);
            Assert.Single(shell.GetVisualDescendants().OfType<SeatOfferCard>());
        }
        finally
        {
            window.Close();
            shell.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private static PrimeShellCaptureState SeatOfferState()
    {
        var player = new PlayerId(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        Guid sessionId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var offer = new LobbyQueueOffer(
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            DateTimeOffset.UtcNow.AddMinutes(1), LobbySeatPolicy.ImmediateSeat);
        var waitlist = new LobbyWaitlistSnapshot(1,
            ImmutableArray.Create(new LobbyQueueEntrySummary(1, "Capture Preview",
                LobbyQueueEntryState.SeatOffered, 1)), true,
            LobbyQueueEntryState.SeatOffered, 1, offer);
        var member = new LobbyMember(sessionId, player.Value, "Capture Preview",
            Hunter.Samus, 0, Ready: false, Observer: true);
        var lobby = new LobbySnapshot(
            Guid.Parse("44444444-4444-4444-4444-444444444444"), "Offer Room",
            LobbyVisibility.Public, Guid.Parse("55555555-5555-5555-5555-555555555555"),
            LobbyPhase.Open, 2, 8, 16, ImmutableArray.Create(member),
            ImmutableArray<LobbyChatEntry>.Empty, "MP3 PROVING GROUND",
            MatchMode.Battle, Waitlist: waitlist);
        var session = new NodeSessionSnapshot(sessionId, player.Value, "Capture Preview",
            Guid.Parse("66666666-6666-6666-6666-666666666666"), new string('A', 43));
        var node = new NodeControlClient.ViewState(Session: session, Lobby: lobby);
        return new PrimeShellCaptureState(
            Play: new PlayState(PlayPhase.Lobby, Array.Empty<NodeListing>(), node,
                Hunter.Samus, lobby.Name, Loading: false, Revision: lobby.Revision),
            Identity: PrimeShellCaptureIdentity.SignedIn);
    }

    private sealed class ControlledDelay
    {
        private readonly TaskCompletionSource _completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TimeSpan Requested { get; private set; }

        public Task WaitAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            Requested = delay;
            return _completion.Task.WaitAsync(cancellationToken);
        }

        public void Complete() => _completion.TrySetResult();
    }

    private sealed class DeferredRestoreAccount : IPrimeGatewayAccount
    {
        private readonly PlayerId _playerId = new(Guid.Parse(
            "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));
        private readonly TaskCompletionSource<HunterLicense> _license = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public Uri Backend => new("https://backend.test/");
        public bool IsSignedIn => true;
        public AccountIdentity? Identity => new(_playerId, true, true);

        public Task<bool> RestoreAsync(CancellationToken cancellationToken)
            => Task.FromResult(true);

        public Task<HunterLicense> GetLicenseAsync(PlayerId playerId,
            CancellationToken cancellationToken) => _license.Task;

        public void CompleteLicense() => _license.TrySetResult(new HunterLicense(
            _playerId, "Pilot", 0, DateTimeOffset.UnixEpoch));

        public Task SignInAsync(string email, string password,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AccountRegistration> RegisterAsync(string email,
            string password, string displayName,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task ConfirmEmailAsync(PlayerId playerId, string code,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task ResendConfirmationAsync(string email,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task SignOutAsync(CancellationToken cancellationToken)
            => throw new NotSupportedException();
        public Task UpdateProfileAsync(string displayName, int favoriteHunter,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
