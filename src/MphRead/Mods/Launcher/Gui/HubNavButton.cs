#if MPHREAD_AVALONIA
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace MphRead.Mods.Launcher.Gui
{
    /// <summary>
    /// Flat, controller-focusable navigation used by the FPS hub.
    ///
    /// Interaction motion is intentionally short-lived. The desktop shell
    /// rasterises the Avalonia surface into the game window, so hover/focus
    /// feedback may animate briefly but must never become an idle redraw loop.
    /// </summary>
    internal class HubNavButton : ContentControl
    {
        private readonly Border _frame;
        private readonly Border _rail;
        private readonly TextBlock _label;
        private readonly IBrush _accent;
        private readonly IBrush _restBackground;
        private readonly IBrush _hotBackground;
        private readonly ScaleTransform _scale = new(1, 1);
        private readonly TranslateTransform _shift = new();
        private PrimeMotionHandle? _poseMotion;
        private readonly bool _primary;
        private bool _tactical, _tab;

        protected void UseTacticalStyle(bool tab = false, bool compact = false)
        {
            _tactical = true;
            _tab = tab;
            _rail.IsVisible = false;
            _label.FontFamily = PrimeTypography.Label;
            _label.FontWeight = FontWeight.SemiBold;
            _label.FontSize = compact ? 14 : 16;
            _label.HorizontalAlignment = HorizontalAlignment.Center;
            _label.VerticalAlignment = VerticalAlignment.Center;
            _label.TextWrapping = TextWrapping.Wrap;
            _label.TextAlignment = TextAlignment.Center;
            _frame.Padding = compact ? new Thickness(4, 2) : new Thickness(10, 8);
            if (compact) MinHeight = 28;
            RefreshVisual();
        }

        private bool _pointer;
        private bool _pressed;
        private bool _selected;
        private readonly Tap _tap = new();

        public event EventHandler? Click;
        public string Label
        {
            get => _label.Text ?? "";
            set => _label.Text = value;
        }

        /// <summary>
        /// Persistently emphasize this destination while its page is active.
        /// Hover/focus remain separate so selection does not look pressed.
        /// </summary>
        public bool Selected
        {
            get => _selected;
            set
            {
                if (_selected == value) return;
                _selected = value;
                RefreshVisual();
            }
        }

        public HubNavButton(string label, string detail = "", bool primary = false,
            bool compact = false, Color? accent = null)
        {
            _primary = primary;
            Color tint = accent ?? HubTheme.Accent;
            _accent = new SolidColorBrush(tint);
            _restBackground = primary
                ? HubTheme.AccentPanel(tint, 34)
                : HubTheme.PanelBrush;
            _hotBackground = HubTheme.AccentPanel(tint, 70);

            Focusable = true;
            Cursor = new Cursor(StandardCursorType.Hand);
            MinHeight = compact ? 42 : 54;
            HorizontalContentAlignment = HorizontalAlignment.Stretch;
            VerticalContentAlignment = VerticalAlignment.Stretch;

            _rail = new Border
            {
                Width = compact ? 2 : 3,
                Background = _accent,
                Opacity = primary ? 1 : 0.35,
                Margin = new Thickness(0, 0, compact ? 7 : 11, 0)
            };

            _label = new TextBlock
            {
                Text = label,
                FontFamily = HubTheme.Ui,
                FontWeight = FontWeight.SemiBold,
                FontSize = compact ? PrimeTypography.BodySmall : PrimeTypography.HeadingSmall,
                Foreground = primary ? _accent : HubTheme.TextBrush,
                VerticalAlignment = VerticalAlignment.Center
            };

            var detailText = new TextBlock
            {
                Text = detail,
                FontFamily = HubTheme.Ui,
                FontSize = 10,
                Foreground = HubTheme.TextDimBrush,
                TextWrapping = TextWrapping.Wrap,
                IsVisible = !compact && detail.Length > 0,
                Margin = new Thickness(0, 2, 0, 0)
            };

            var text = new StackPanel { Spacing = 0 };
            text.Children.Add(_label);
            text.Children.Add(detailText);

            var body = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
            body.Children.Add(_rail);
            Grid.SetColumn(text, 1);
            body.Children.Add(text);

            var transforms = new TransformGroup();
            transforms.Children.Add(_scale);
            transforms.Children.Add(_shift);
            _frame = new Border
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Padding = compact ? new Thickness(8, 7) : new Thickness(12, 9),
                Background = _restBackground,
                BorderBrush = primary ? _accent : HubTheme.EdgeBrush,
                BorderThickness = new Thickness(1),
                Child = body,
                RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
                RenderTransform = transforms
            };
            Content = _frame;
        }

        protected override void OnPointerEntered(PointerEventArgs e)
        {
            _pointer = true;
            RefreshVisual();
            base.OnPointerEntered(e);
        }

        protected override void OnPointerExited(PointerEventArgs e)
        {
            _pointer = false;
            RefreshVisual();
            base.OnPointerExited(e);
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            if (_tap.Down && _tap.Moved(e, this))
            {
                _pressed = false;
                RefreshVisual();
            }
            base.OnPointerMoved(e);
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            if (!IsEffectivelyEnabled)
            {
                base.OnPointerPressed(e);
                return;
            }
            _tap.Press(e, this);
            _pressed = true;
            Focus();
            e.Pointer.Capture(this);
            e.Handled = true;
            RefreshVisual();
            base.OnPointerPressed(e);
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            bool click = IsEffectivelyEnabled && _tap.Release(e, this);
            _pressed = false;
            RefreshVisual();
            if (click)
            {
                e.Handled = true;
                Click?.Invoke(this, EventArgs.Empty);
            }
            base.OnPointerReleased(e);
        }

        protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
        {
            _tap.Cancel();
            _pressed = false;
            RefreshVisual();
            base.OnPointerCaptureLost(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (IsEffectivelyEnabled && (e.Key == Key.Enter || e.Key == Key.Space))
            {
                _pressed = true;
                RefreshVisual();
                e.Handled = true;
                Click?.Invoke(this, EventArgs.Empty);
                return;
            }
            base.OnKeyDown(e);
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            if (_pressed && (e.Key == Key.Enter || e.Key == Key.Space))
            {
                _pressed = false;
                RefreshVisual();
                e.Handled = true;
            }
            base.OnKeyUp(e);
        }

        protected override void OnGotFocus(FocusChangedEventArgs e)
        {
            RefreshVisual();
            base.OnGotFocus(e);
        }

        protected override void OnLostFocus(FocusChangedEventArgs e)
        {
            _pressed = false;
            RefreshVisual();
            base.OnLostFocus(e);
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == IsEnabledProperty)
                RefreshVisual();
        }

        private void RefreshVisual()
        {
            if (_frame == null) return;
            bool enabled = IsEnabled && IsEffectivelyEnabled;
            bool hot = (_pointer || IsFocused) && enabled;

            if (_tactical)
            {
                bool emphasized = hot || _selected;
                _frame.Background = _primary
                    ? (_pressed ? PrimeTheme.AccentDeepBrush : hot ? PrimeTheme.GlowBrush : PrimeTheme.PrimaryBrush)
                    : emphasized ? PrimeTheme.PanelHighlightBrush : _tab ? Brushes.Transparent : PrimeTheme.PanelRaisedBrush;
                _frame.BorderBrush = emphasized ? PrimeTheme.GlowBrush : _tab ? Brushes.Transparent : PrimeTheme.BorderBrush;
                _frame.BorderThickness = _tab && !IsFocused
                    ? new Thickness(0, 0, 0, _selected ? 2 : 0)
                    : new Thickness(1);
                _label.Foreground = _primary
                    ? (hot && !_pressed ? PrimeTheme.BackgroundDeepBrush : Brushes.White)
                    : emphasized ? PrimeTheme.HighlightBrush : PrimeTheme.TextBrush;

                AnimatePose(
                    _pressed ? 0.985 : hot ? 1.012 : 1,
                    hot && !_tab ? 1.5 : 0,
                    _pressed ? 0.80 : enabled ? 1 : 0.48,
                    emphasized || _primary ? 1 : 0.35);
                return;
            }

            bool selected = hot || _selected;
            _frame.Background = selected ? _hotBackground : _restBackground;
            _frame.BorderBrush = selected || _primary ? _accent : HubTheme.EdgeBrush;
            _label.Foreground = selected || _primary ? _accent : HubTheme.TextBrush;
            AnimatePose(
                _pressed ? 0.985 : hot ? 1.01 : 1,
                hot ? 3 : 0,
                _pressed ? 0.80 : enabled ? 1 : 0.48,
                selected || _primary ? 1 : 0.35);
        }

        private void AnimatePose(double scale, double shift, double opacity, double railOpacity)
        {
            _poseMotion?.Cancel();
            double fromScale = _scale.ScaleX;
            double fromShift = _shift.X;
            double fromOpacity = _frame.Opacity;
            double fromRail = _rail.Opacity;
            double seconds = _pressed ? PrimeMotion.PressSeconds : PrimeMotion.ButtonSeconds;

            _poseMotion = PrimeMotion.Tween(this, seconds, progress =>
            {
                double spring = PrimeMotion.Spring(progress);
                double settle = PrimeMotion.Settle(progress);
                double value = fromScale + (scale - fromScale) * spring;
                _scale.ScaleX = value;
                _scale.ScaleY = value;
                _shift.X = fromShift + (shift - fromShift) * spring;
                _frame.Opacity = fromOpacity + (opacity - fromOpacity) * settle;
                _rail.Opacity = fromRail + (railOpacity - fromRail) * settle;
            }, easing: progress => progress);
        }
    }
}
#endif
