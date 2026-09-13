using System;
using System.Reflection;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using MphRead.Entities;
using MphRead.Mods;
using MphRead.Mods.Input;
using MphRead.Mods.Launcher.Theme;

namespace MphRead.Mods.Launcher.Gui
{
    /// <summary>
    /// One rebindable control: what it does on the left, what it is bound to on
    /// the right, click and press to change it.
    ///
    /// The awkward part is the same one the WinForms row had: this window is a
    /// toolkit's and the game is GLFW's, and their key enumerations agree only
    /// about printable ASCII -- Escape is 27 in one and 256 in the other. The
    /// map below covers everything a person is likely to bind; anything
    /// unmapped is refused rather than bound to whatever key happens to share
    /// its number.
    /// </summary>
    internal sealed class KeyRow : Control
    {
        private readonly string _label;
        private readonly Func<Keybind> _binding;
        private readonly Action<ButtonType, PrimeKey, PrimeMouseButton> _rebind;
        private readonly double _labelWidth;
        private bool _listening;
        private bool _hot;

        public event EventHandler? Rebound;

        /// <summary>Stable presentation value used by the Settings draft tracker.</summary>
        internal string CurrentValue => InputSettings.Describe(_binding());

        public KeyRow(PropertyInfo property, double labelWidth = 160)
        {
            _label = InputSettings.ActionName(property);
            _binding = () => InputSettings.Bind(property);
            _rebind = (type, key, button) => InputSettings.Rebind(property, type, key, button);
            _labelWidth = labelWidth;
            Height = PrimeTouchTargets.MinimumDip;
            Focusable = true;
            Cursor = new Cursor(StandardCursorType.Hand);
            PrimeAccessibility.SetName(this, _label);
            UpdateAccessibleValue();
        }

        /// <summary>
        /// Build a key row backed by a setting that is not a
        /// <see cref="ClientPlayerBindings"/> property. Chat is handled by
        /// the shell before player input, so it intentionally uses this path
        /// and never becomes a fake gameplay binding.
        /// </summary>
        public KeyRow(string label, Func<Keybind> binding,
            Action<ButtonType, PrimeKey, PrimeMouseButton> rebind, double labelWidth = 160)
        {
            _label = label;
            _binding = binding;
            _rebind = rebind;
            _labelWidth = labelWidth;
            Height = PrimeTouchTargets.MinimumDip;
            Focusable = true;
            Cursor = new Cursor(StandardCursorType.Hand);
            PrimeAccessibility.SetName(this, _label);
            UpdateAccessibleValue();
        }

        private Rect Box => new(_labelWidth, 2,
            Math.Max(60, Bounds.Width - _labelWidth - 4), Bounds.Height - 4);

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            Focus();
            PointerPointProperties properties = e.GetCurrentPoint(this).Properties;
            if (!_listening)
            {
                if (Box.Contains(e.GetPosition(this)))
                {
                    _listening = true;
                    InvalidateVisual();
                }
                e.Handled = true;
                base.OnPointerPressed(e);
                return;
            }
            // Already listening: this press is the new binding.
            PrimeMouseButton? button = properties.PointerUpdateKind switch
            {
                PointerUpdateKind.LeftButtonPressed => PrimeMouseButton.Left,
                PointerUpdateKind.RightButtonPressed => PrimeMouseButton.Right,
                PointerUpdateKind.MiddleButtonPressed => PrimeMouseButton.Middle,
                PointerUpdateKind.XButton1Pressed => PrimeMouseButton.Button4,
                PointerUpdateKind.XButton2Pressed => PrimeMouseButton.Button5,
                _ => null
            };
            if (button != null)
            {
                _rebind(ButtonType.Mouse, PrimeKey.Unknown, button.Value);
                Done();
            }
            e.Handled = true;
            base.OnPointerPressed(e);
        }

        protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
        {
            if (_listening && e.Delta.Y != 0)
            {
                _rebind(
                    e.Delta.Y > 0 ? ButtonType.ScrollUp : ButtonType.ScrollDown,
                    PrimeKey.Unknown, PrimeMouseButton.Left);
                Done();
                e.Handled = true;
            }
            base.OnPointerWheelChanged(e);
        }

        protected override void OnPointerEntered(PointerEventArgs e)
        {
            _hot = true;
            InvalidateVisual();
            base.OnPointerEntered(e);
        }

        protected override void OnPointerExited(PointerEventArgs e)
        {
            _hot = false;
            InvalidateVisual();
            base.OnPointerExited(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (!_listening)
            {
                if (e.Key == Key.Enter || e.Key == Key.Space)
                {
                    _listening = true;
                    InvalidateVisual();
                    e.Handled = true;
                }
                base.OnKeyDown(e);
                return;
            }
            // While listening every key belongs to this row, including the ones
            // the window would otherwise spend on moving the focus or closing.
            e.Handled = true;
            if (e.Key == Key.Escape)
            {
                Done();
                return;
            }
            if (e.Key == Key.Back || e.Key == Key.Delete)
            {
                _rebind(ButtonType.Key, PrimeKey.Unknown, PrimeMouseButton.Left);
                Done();
                return;
            }
            PrimeKey? key = Translate(e.Key);
            if (key != null)
            {
                _rebind(ButtonType.Key, key.Value, PrimeMouseButton.Left);
                Done();
            }
        }

        private void Done()
        {
            _listening = false;
            UpdateAccessibleValue();
            InvalidateVisual();
            Rebound?.Invoke(this, EventArgs.Empty);
        }

        private void UpdateAccessibleValue()
            => AutomationProperties.SetItemStatus(this,
                InputSettings.Describe(_binding()));

        protected override void OnLostFocus(Avalonia.Interactivity.RoutedEventArgs e)
        {
            _listening = false;
            InvalidateVisual();
            base.OnLostFocus(e);
        }

        protected override void OnGotFocus(GotFocusEventArgs e)
        {
            InvalidateVisual();
            base.OnGotFocus(e);
        }

        /// <summary>The toolkit's key to the one the game's input layer speaks.</summary>
        private static PrimeKey? Translate(Key key)
        {
            if (key >= Key.A && key <= Key.Z)
            {
                return PrimeKey.A + (key - Key.A);
            }
            if (key >= Key.D0 && key <= Key.D9)
            {
                return PrimeKey.D0 + (key - Key.D0);
            }
            if (key >= Key.NumPad0 && key <= Key.NumPad9)
            {
                return PrimeKey.KeyPad0 + (key - Key.NumPad0);
            }
            if (key >= Key.F1 && key <= Key.F12)
            {
                return PrimeKey.F1 + (key - Key.F1);
            }
            return key switch
            {
                Key.Space => PrimeKey.Space,
                Key.Tab => PrimeKey.Tab,
                Key.Enter => PrimeKey.Enter,
                Key.LeftShift => PrimeKey.LeftShift,
                Key.RightShift => PrimeKey.RightShift,
                Key.LeftCtrl => PrimeKey.LeftControl,
                Key.RightCtrl => PrimeKey.RightControl,
                Key.LeftAlt => PrimeKey.LeftAlt,
                Key.RightAlt => PrimeKey.RightAlt,
                Key.Left => PrimeKey.Left,
                Key.Right => PrimeKey.Right,
                Key.Up => PrimeKey.Up,
                Key.Down => PrimeKey.Down,
                Key.Insert => PrimeKey.Insert,
                Key.Home => PrimeKey.Home,
                Key.End => PrimeKey.End,
                Key.PageUp => PrimeKey.PageUp,
                Key.PageDown => PrimeKey.PageDown,
                Key.CapsLock => PrimeKey.CapsLock,
                Key.OemMinus => PrimeKey.Minus,
                Key.OemPlus => PrimeKey.Equal,
                Key.OemOpenBrackets => PrimeKey.LeftBracket,
                Key.OemCloseBrackets => PrimeKey.RightBracket,
                Key.OemSemicolon => PrimeKey.Semicolon,
                Key.OemQuotes => PrimeKey.Apostrophe,
                Key.OemComma => PrimeKey.Comma,
                Key.OemPeriod => PrimeKey.Period,
                Key.OemQuestion => PrimeKey.Slash,
                Key.OemBackslash or Key.OemPipe => PrimeKey.Backslash,
                Key.OemTilde => PrimeKey.GraveAccent,
                Key.Add => PrimeKey.KeyPadAdd,
                Key.Subtract => PrimeKey.KeyPadSubtract,
                Key.Multiply => PrimeKey.KeyPadMultiply,
                Key.Divide => PrimeKey.KeyPadDivide,
                _ => null
            };
        }

        public override void Render(DrawingContext context)
        {
            // Avalonia can schedule one final render after a tab is detached,
            // when layout has already collapsed the row to a zero-sized box.
            if (Bounds.Width <= 0 || Bounds.Height <= 0)
            {
                return;
            }

            // See MenuEntry.Render: hit testing follows the drawing.
            context.FillRectangle(Brushes.Transparent,
                new Rect(0, 0, Bounds.Width, Bounds.Height));
            FormattedText label = TrackedText.Make(_label, 12,
                bold: true, GuiTheme.TextBrush);
            context.DrawText(label, new Point(4, (Bounds.Height - label.Height) / 2));

            Rect box = Box;
            context.DrawRectangle(GuiTheme.PanelLightBrush,
                new Pen(new SolidColorBrush(_listening ? GuiTheme.Warm
                    : IsFocused || _hot ? GuiTheme.Accent : GuiTheme.Edge), 1),
                new RoundedRect(box, 4));

            string text = _listening
                ? "press a key, a mouse button or the wheel"
                : InputSettings.Describe(_binding());
            FormattedText value = TrackedText.Make(text, 12, bold: true,
                new SolidColorBrush(_listening ? GuiTheme.Warm : GuiTheme.Text));
            // Never wider than the box: a binding nobody has heard of should
            // not push its own frame off the row.
            value.MaxTextWidth = Math.Max(20, box.Width - 12);
            value.MaxTextHeight = Math.Max(1, box.Height);
            value.Trimming = TextTrimming.CharacterEllipsis;
            context.DrawText(value, new Point(box.X + (box.Width - value.Width) / 2,
                box.Y + (box.Height - value.Height) / 2));
        }
    }
}
