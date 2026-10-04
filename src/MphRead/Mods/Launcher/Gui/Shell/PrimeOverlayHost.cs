#if MPHREAD_AVALONIA
using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace MphRead.Mods.Launcher.Gui
{
    internal enum PrimeModalSize { Small, Medium, Large, Wide, FullscreenWorkspace }

    internal sealed class PrimeOverlayHost : Panel
    {
        private sealed class Entry
        {
            public required Border Frame { get; init; }
            public Action? Cancel { get; init; }
            public Control? Focus { get; init; }
            public PrimeMotionHandle? Motion { get; set; }
        }

        private readonly List<Entry> _stack = new();
        private readonly Border _scrim;
        private PrimeMotionHandle? _scrimMotion;

        public bool IsOpen => _stack.Count > 0;

        public PrimeOverlayHost()
        {
            IsVisible = false;
            IsHitTestVisible = false;
            Background = Brushes.Transparent;
            SetValue(ControllerNav.ModalProperty, false);
            KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.Cycle);
            _scrim = new Border
            {
                Background = GuiTheme.ScrimBrush,
                Opacity = 0,
                IsVisible = false,
                IsHitTestVisible = false,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };
            Children.Add(_scrim);
        }

        public void Show(Control view, PrimeModalSize size = PrimeModalSize.Large,
            Action? cancel = null, bool fitContent = false)
        {
            var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Control;
            double width = size switch
            {
                PrimeModalSize.Small => 480,
                PrimeModalSize.Medium => 680,
                PrimeModalSize.Large => 960,
                PrimeModalSize.Wide => 1180,
                _ => double.PositiveInfinity
            };

            var frame = new Border
            {
                Child = view,
                Background = PrimeTheme.BackgroundBrush,
                BorderBrush = PrimeTheme.BorderBrightBrush,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(24),
                MaxWidth = width,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Padding = fitContent ? new Thickness(20) : new Thickness(0),
                VerticalAlignment = fitContent || size == PrimeModalSize.Small || size == PrimeModalSize.Medium
                    ? VerticalAlignment.Center : VerticalAlignment.Stretch,
                MaxHeight = size == PrimeModalSize.Small ? 420 : double.PositiveInfinity
            };

            bool first = !IsOpen;
            if (!first)
            {
                Entry previous = _stack[^1];
                previous.Motion?.Cancel(true);
                previous.Frame.IsVisible = false;
            }

            var entry = new Entry { Frame = frame, Cancel = cancel, Focus = focused };
            _stack.Add(entry);
            IsVisible = true;
            IsHitTestVisible = true;
            SetValue(ControllerNav.ModalProperty, true);
            Children.Add(frame);

            if (first)
            {
                _scrim.IsVisible = true;
                _scrimMotion?.Cancel();
                _scrimMotion = PrimeMotion.Fade(_scrim, _scrim.Opacity, 1);
            }
            else
            {
                _scrim.Opacity = 1;
            }

            entry.Motion = PrimeMotion.ModalIn(frame);
            Dispatcher.UIThread.Post(() =>
            {
                if (IsOpen && ReferenceEquals(_stack[^1], entry))
                    FocusNavigator.Ensure(this);
            }, DispatcherPriority.Background);
        }

        public void Close(Control view)
        {
            if (IsOpen && ReferenceEquals(_stack[^1].Frame.Child, view))
                Close();
        }

        public void Close()
        {
            if (!IsOpen) return;

            Entry entry = _stack[^1];
            _stack.RemoveAt(_stack.Count - 1);
            entry.Motion?.Cancel(true);
            entry.Frame.IsHitTestVisible = false;

            Entry? revealed = _stack.Count > 0 ? _stack[^1] : null;
            if (revealed != null)
            {
                revealed.Frame.IsVisible = true;
                revealed.Frame.IsHitTestVisible = true;
            }

            bool last = _stack.Count == 0;
            if (last)
            {
                IsHitTestVisible = false;
                SetValue(ControllerNav.ModalProperty, false);
                _scrimMotion?.Cancel();
                _scrimMotion = PrimeMotion.Fade(_scrim, _scrim.Opacity, 0,
                    PrimeMotion.ModalOutSeconds, () =>
                    {
                        if (!IsOpen) _scrim.IsVisible = false;
                    });
            }

            entry.Motion = PrimeMotion.ModalOut(entry.Frame, () =>
            {
                Children.Remove(entry.Frame);
                if (!IsOpen)
                {
                    IsVisible = false;
                    _scrim.IsVisible = false;
                }
            });

            if (entry.Focus is { IsEffectivelyVisible: true, IsEffectivelyEnabled: true })
            {
                Dispatcher.UIThread.Post(() => entry.Focus.Focus(), DispatcherPriority.Background);
            }
            else if (revealed != null)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (IsOpen && ReferenceEquals(_stack[^1], revealed))
                        FocusNavigator.Ensure(revealed.Frame);
                }, DispatcherPriority.Background);
            }
        }

        public void Back()
        {
            if (!IsOpen) return;
            var cancel = _stack[^1].Cancel;
            if (cancel != null) cancel();
            else Close();
        }

        public void Clear()
        {
            _scrimMotion?.Cancel();
            _scrimMotion = null;
            foreach (Entry entry in _stack)
                entry.Motion?.Cancel();
            _stack.Clear();
            Children.Clear();
            _scrim.Opacity = 0;
            _scrim.IsVisible = false;
            Children.Add(_scrim);
            IsVisible = false;
            IsHitTestVisible = false;
            SetValue(ControllerNav.ModalProperty, false);
        }
    }
}
#endif
