using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using MphRead.Mods.Launcher.Theme;

namespace MphRead.Mods.Launcher.Gui
{
    /// <summary>
    /// Optional pen-only swipe diagnostic. It measures logical UI distance
    /// using the same quarter-degree sensitivity scale as direct stylus aim;
    /// it does not claim gameplay input or persist calibration state.
    /// </summary>
    internal sealed class StylusTurnPreview : Control
    {
        private readonly Func<float> _sensitivity;
        private IPointer? _pointer;
        private Point _origin;
        private Point _current;
        private float? _degrees;

        public StylusTurnPreview(Func<float> sensitivity)
        {
            _sensitivity = sensitivity;
            Height = 54;
        }

        internal static float MeasureDegrees(Vector delta, float sensitivity)
        {
            sensitivity = float.IsFinite(sensitivity)
                ? Math.Clamp(sensitivity, .01f, 10f) : 1f;
            double length = Math.Sqrt(delta.X * delta.X + delta.Y * delta.Y);
            return double.IsFinite(length)
                ? (float)(length * sensitivity / 4f) : 0;
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            if (e.Pointer.Type != PointerType.Pen)
            {
                base.OnPointerPressed(e);
                return;
            }
            _pointer = e.Pointer;
            _origin = _current = e.GetPosition(this);
            _degrees = null;
            e.Pointer.Capture(this);
            e.Handled = true;
            InvalidateVisual();
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            if (ReferenceEquals(_pointer, e.Pointer))
            {
                _current = e.GetPosition(this);
                _degrees = MeasureDegrees(_current - _origin, _sensitivity());
                e.Handled = true;
                InvalidateVisual();
            }
            base.OnPointerMoved(e);
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            if (ReferenceEquals(_pointer, e.Pointer))
            {
                _current = e.GetPosition(this);
                _degrees = MeasureDegrees(_current - _origin, _sensitivity());
                e.Pointer.Capture(null);
                _pointer = null;
                e.Handled = true;
                InvalidateVisual();
            }
            base.OnPointerReleased(e);
        }

        public override void Render(DrawingContext context)
        {
            context.DrawRectangle(GuiTheme.PanelBrush,
                new Pen(GuiTheme.EdgeBrush, 1), new Rect(Bounds.Size));
            var title = new FormattedText("STYLUS TURN PREVIEW",
                CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                GuiTheme.Face(bold: true), 11, GuiTheme.TextDimBrush);
            context.DrawText(title, new Point(10, 7));
            string value = _pointer != null
                ? $"Stylus turn: {_degrees.GetValueOrDefault():0.#}°"
                : _degrees.HasValue
                    ? $"Stylus turn: {_degrees.Value:0.#}° — drag again to remeasure"
                    : "Drag the pen across this row to measure logical turn";
            var result = new FormattedText(value, CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, GuiTheme.Face(bold: false), 13,
                _degrees.HasValue ? GuiTheme.TextBrush : GuiTheme.TextDimBrush);
            context.DrawText(result, new Point(10, 27));
        }
    }

    /// <summary>A small upper-case heading over a group of rows.</summary>
    internal sealed class Caption : Control
    {
        private readonly string _text;

        public Caption(string text)
        {
            _text = text;
            Height = 26;
        }

        /// <summary>
        /// Its own width when nothing constrains it, for the same reason
        /// <see cref="MenuEntry.MeasureOverride"/> has one: in a row, a
        /// control that measures to nothing is drawn on top of its neighbours.
        /// </summary>
        protected override Size MeasureOverride(Size availableSize)
        {
            Size size = base.MeasureOverride(availableSize);
            return new Size(Math.Min(Label().Width + 8, availableSize.Width), size.Height);
        }

        private FormattedText Label()
        {
            return new FormattedText(_text.ToUpperInvariant(), CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, GuiTheme.Face(bold: true), 11,
                GuiTheme.TextDimBrush);
        }

        public override void Render(DrawingContext context)
        {
            FormattedText text = Label();
            context.DrawText(text, new Point(0, Bounds.Height - text.Height - 4));
            double y = Bounds.Height - 2;
            context.DrawLine(new Pen(GuiTheme.EdgeBrush, 1),
                new Point(0, y), new Point(Bounds.Width, y));
        }
    }

    /// <summary>
    /// One setting with a fixed set of answers: a label, the current answer,
    /// and an arrow on each side.
    ///
    /// Cycling rather than a drop-down because every list here is short and a
    /// combo box is the one control WinForms would not draw dark -- which is
    /// the reason the original exists. Keeping the same shape here is not
    /// obligation but consistency: the two screens are one product.
    /// </summary>
    internal sealed class ChoiceRow : Control, IControllerNavigable
    {
        private readonly string _label;
        private IReadOnlyList<string> _options;
        private readonly double _measuredLabelWidth;
        private double _valueColumn;
        private int _index;
        private bool _leftHot;
        private bool _rightHot;

        public event EventHandler? Changed;

        static ChoiceRow()
        {
            AffectsRender<ChoiceRow>(IsEnabledProperty);
        }

        public int Index
        {
            get => _index;
            set
            {
                int clamped = _options.Count == 0 ? 0 : Math.Clamp(value, 0, _options.Count - 1);
                if (clamped != _index)
                {
                    _index = clamped;
                    AutomationProperties.SetItemStatus(this, Value);
                    InvalidateVisual();
                    Changed?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        public string Value => _options.Count == 0 ? "" : _options[_index];

        public ChoiceRow(string label, IReadOnlyList<string> options, int index = 0)
        {
            _label = label;
            _options = options;
            _measuredLabelWidth = PrimeLegacyControlVisuals.MeasureLabel(label);
            _valueColumn = PrimeLegacyControlVisuals.MeasureValueColumn(options);
            _index = options.Count == 0 ? 0 : Math.Clamp(index, 0, options.Count - 1);
            Height = PrimeLegacyControlVisuals.RowHeight;
            Focusable = true;
            Cursor = new Cursor(StandardCursorType.Hand);
            PrimeAccessibility.SetName(this, label);
            PrimeAccessibility.SetDescription(this,
                $"{label}. Use the left and right arrows to choose an option.");
            AutomationProperties.SetItemStatus(this, Value);
        }

        /// <summary>Replace the options in place, e.g. after a room list changes.</summary>
        public void SetItems(IReadOnlyList<string> options, int index = 0)
        {
            _options = options;
            _valueColumn = PrimeLegacyControlVisuals.MeasureValueColumn(options);
            _index = options.Count == 0 ? 0 : Math.Clamp(index, 0, options.Count - 1);
            AutomationProperties.SetItemStatus(this, Value);
            InvalidateVisual();
        }

        /// <summary>
        /// Width of the arrow buttons, and of the column the value is drawn
        /// in between them.
        /// </summary>
        private const double ArrowWidth = 28;
        private const double ArrowGap = 8;

        /// <summary>
        /// Drawn in a square at the right-hand end of the row, past the
        /// forward arrow, when the answer is a thing better shown than named
        /// -- a crosshair, at the size and shape the row has just picked.
        /// Everything else in the row shifts left to make room for it.
        /// </summary>
        public Action<DrawingContext, Rect>? Preview
        {
            get => _preview;
            set
            {
                _preview = value;
                // Room for the picture. A crosshair at its largest is 36
                // points across, and a row of the ordinary height cannot show
                // that without shrinking it -- which would defeat a preview
                // whose job is partly to answer "how big is Big".
                Height = value == null ? PrimeLegacyControlVisuals.RowHeight : 48;
                InvalidateVisual();
            }
        }

        private Action<DrawingContext, Rect>? _preview;

        private const double PreviewWidth = 52;

        private double PreviewRoom => _preview == null ? 0 : PreviewWidth;

        /// <summary>
        /// Both arrows sit still.
        ///
        /// The back arrow used to be placed from the width of the *value* --
        /// it slid left to make room for a long room name and back again for a
        /// short one. Which meant it moved as you stepped: the button jumped
        /// out from under the pointer between one map and the next, so a
        /// second click landed on the row instead of the arrow and stepped
        /// forward again. Picking a map by clicking became a thing you had to
        /// re-aim for every time.
        ///
        /// So the value gets a column of its own, of a fixed width, and is
        /// trimmed to it. The arrows never move, and the row is still one
        /// height on every card.
        /// </summary>
        private Rect LeftArrow
        {
            get
            {
                double rightX = Math.Max(0, Bounds.Width - PreviewRoom - ArrowWidth);
                double preferred = Math.Max(_measuredLabelWidth
                    + PrimeLegacyControlVisuals.LabelInset,
                    rightX - ArrowGap - _valueColumn - ArrowWidth);
                double maximum = Math.Max(0, rightX - ArrowGap - ArrowWidth);
                // The value column is measured from its actual options and
                // yields first on compact screens. The label is clipped to the
                // remaining space in Render, so the arrows never overlap text.
                double x = Math.Clamp(preferred, 0, maximum);
                return new Rect(x, 0, Math.Min(ArrowWidth,
                    Math.Max(0, Bounds.Width - x)), Bounds.Height);
            }
        }

        private Rect RightArrow
        {
            get
            {
                double x = Math.Max(0, Bounds.Width - PreviewRoom - ArrowWidth);
                return new Rect(x, 0,
                    Math.Min(ArrowWidth, Math.Max(0, Bounds.Width - x)),
                    Bounds.Height);
            }
        }

        internal Rect RenderedLeftArrow => LeftArrow;
        internal Rect RenderedRightArrow => RightArrow;
        internal double RenderedValueWidth
            => Math.Max(0, RightArrow.X - LeftArrow.Right - ArrowGap);

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            Point p = e.GetPosition(this);
            bool left = LeftArrow.Contains(p);
            bool right = RightArrow.Contains(p);
            if (left != _leftHot || right != _rightHot)
            {
                _leftHot = left;
                _rightHot = right;
                InvalidateVisual();
            }
            base.OnPointerMoved(e);
        }

        protected override void OnPointerExited(PointerEventArgs e)
        {
            _leftHot = _rightHot = false;
            InvalidateVisual();
            base.OnPointerExited(e);
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            if (!IsEnabled)
            {
                return;
            }
            Focus();
            Point p = e.GetPosition(this);
            // Anywhere that is not the back arrow steps forward, so the row can
            // be poked at without aiming.
            if (LeftArrow.Contains(p))
            {
                Step(-1);
            }
            else
            {
                Step(1);
            }
            base.OnPointerPressed(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (!IsEnabled)
            {
                base.OnKeyDown(e);
                return;
            }
            if (e.Key == Key.Left)
            {
                Step(-1);
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Right || e.Key == Key.Enter || e.Key == Key.Space)
            {
                Step(1);
                e.Handled = true;
                return;
            }
            base.OnKeyDown(e);
        }

        private void Step(int direction)
        {
            if (!IsEnabled || _options.Count == 0)
            {
                return;
            }
            // Wrapping, because the lists are short and running off the end of
            // one is more annoying than useful.
            _index = (_index + direction + _options.Count) % _options.Count;
            AutomationProperties.SetItemStatus(this, Value);
            InvalidateVisual();
            Changed?.Invoke(this, EventArgs.Empty);
        }

        void IControllerNavigable.ControllerActivate() => Step(1);
        void IControllerNavigable.ControllerAdjust(int direction) => Step(direction);

        public override void Render(DrawingContext context)
        {
            Rect body = new(0, 0, Bounds.Width, Bounds.Height);
            PrimeLegacyControlVisuals.DrawHitSurface(context, body);
            PrimeLegacyControlVisuals.DrawFocusedSurface(context, body, IsFocused,
                IsEnabled);
            IBrush text = IsEnabled ? PrimeLegacyControlVisuals.MutedTextBrush
                : PrimeLegacyControlVisuals.DisabledTextBrush;
            var label = new FormattedText(_label, CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, GuiTheme.Face(false), 13, text);
            Rect left = LeftArrow;
            using (context.PushClip(new Rect(0, 0,
                Math.Max(0, left.X - PrimeLegacyControlVisuals.LabelValueGap),
                Bounds.Height)))
            {
                context.DrawText(label, new Point(PrimeLegacyControlVisuals.LabelInset,
                    (Bounds.Height - label.Height) / 2));
            }

            var value = new FormattedText(Value, CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, GuiTheme.Face(true), 13,
                IsEnabled ? PrimeLegacyControlVisuals.TextBrush : text);
            double room = RenderedValueWidth;
            if (value.Width > room)
            {
                value.MaxTextWidth = Math.Max(1, room);
                value.Trimming = TextTrimming.CharacterEllipsis;
            }
            double centre = (left.Right + RightArrow.X) / 2;
            context.DrawText(value, new Point(centre - value.Width / 2,
                (Bounds.Height - value.Height) / 2));

            Arrow(context, left, pointsLeft: true, IsEnabled && _leftHot);
            Arrow(context, RightArrow, pointsLeft: false, IsEnabled && _rightHot);
            if (_preview != null)
            {
                const double inset = 3;
                double previewX = Math.Max(0, Bounds.Width - PreviewWidth + inset);
                _preview(context, new Rect(previewX, inset,
                    Math.Max(0, Math.Min(PreviewWidth - inset * 2,
                        Bounds.Width - previewX - inset)),
                    Math.Max(0, Bounds.Height - inset * 2)));
            }
            PrimeLegacyControlVisuals.DrawFocusMarker(context, body, IsFocused,
                IsEnabled);
        }

        private static void Arrow(DrawingContext context, Rect area, bool pointsLeft, bool hot)
        {
            double cx = area.X + area.Width / 2;
            double cy = area.Y + area.Height / 2;
            const double w = 4.5;
            const double h = 6;
            var geometry = new StreamGeometry();
            using (StreamGeometryContext sink = geometry.Open())
            {
                if (pointsLeft)
                {
                    sink.BeginFigure(new Point(cx + w, cy - h), true);
                    sink.LineTo(new Point(cx - w, cy));
                    sink.LineTo(new Point(cx + w, cy + h));
                }
                else
                {
                    sink.BeginFigure(new Point(cx - w, cy - h), true);
                    sink.LineTo(new Point(cx + w, cy));
                    sink.LineTo(new Point(cx - w, cy + h));
                }
                sink.EndFigure(true);
            }
            context.DrawGeometry(hot ? GuiTheme.AccentBrush : GuiTheme.TextDimBrush,
                null, geometry);
        }
    }

    /// <summary>One setting that is on or off.</summary>
    internal sealed class ToggleRow : Control, IControllerNavigable
    {
        private readonly string _label;
        private bool _on;

        public event EventHandler? Changed;

        static ToggleRow()
        {
            AffectsRender<ToggleRow>(IsEnabledProperty);
        }

        public bool On
        {
            get => _on;
            set
            {
                if (_on != value)
                {
                    _on = value;
                    AutomationProperties.SetItemStatus(this, value ? "On" : "Off");
                    InvalidateVisual();
                    Changed?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        public ToggleRow(string label, bool on)
        {
            _label = label;
            _on = on;
            Height = PrimeLegacyControlVisuals.RowHeight;
            Focusable = true;
            Cursor = new Cursor(StandardCursorType.Hand);
            PrimeAccessibility.SetName(this, label);
            PrimeAccessibility.SetDescription(this, $"{label}. Toggle on or off.");
            AutomationProperties.SetItemStatus(this, on ? "On" : "Off");
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            if (!IsEnabled)
            {
                return;
            }
            Focus();
            On = !On;
            base.OnPointerPressed(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (!IsEnabled)
            {
                base.OnKeyDown(e);
                return;
            }
            if (e.Key == Key.Enter || e.Key == Key.Space
                || e.Key == Key.Left || e.Key == Key.Right)
            {
                On = !On;
                e.Handled = true;
                return;
            }
            base.OnKeyDown(e);
        }

        void IControllerNavigable.ControllerActivate()
        {
            if (IsEnabled) On = !On;
        }
        void IControllerNavigable.ControllerAdjust(int direction)
        {
            if (IsEnabled) On = !On;
        }

        public override void Render(DrawingContext context)
        {
            Rect body = new(0, 0, Bounds.Width, Bounds.Height);
            PrimeLegacyControlVisuals.DrawHitSurface(context, body);
            PrimeLegacyControlVisuals.DrawFocusedSurface(context, body, IsFocused,
                IsEnabled);
            IBrush text = IsEnabled ? PrimeLegacyControlVisuals.MutedTextBrush
                : PrimeLegacyControlVisuals.DisabledTextBrush;
            var label = new FormattedText(_label, CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, GuiTheme.Face(false), 13, text);
            context.DrawText(label, new Point(PrimeLegacyControlVisuals.LabelInset,
                (Bounds.Height - label.Height) / 2));

            const double w = 40;
            const double h = 20;
            double trackWidth = Math.Min(w, Math.Max(0, Bounds.Width));
            var track = new Rect(Math.Max(0, Bounds.Width - trackWidth - 4),
                (Bounds.Height - h) / 2, trackWidth, h);
            IBrush trackBrush = !IsEnabled
                ? PrimeLegacyControlVisuals.DisabledTrackBrush
                : _on ? PrimeLegacyControlVisuals.AccentBrush(GuiTheme.Accent)
                : PrimeLegacyControlVisuals.EdgeBrush;
            context.DrawRectangle(trackBrush, null,
                new RoundedRect(track, (float)(h / 2)));
            double knobRadius = Math.Max(1, h / 2 - 3);
            double knob = _on ? track.Right - h / 2 : track.X + h / 2;
            IBrush knobBrush = !IsEnabled ? PrimeLegacyControlVisuals.DisabledKnobBrush
                : _on ? PrimeLegacyControlVisuals.OnBrush
                : PrimeLegacyControlVisuals.MutedTextBrush;
            context.DrawEllipse(knobBrush, null,
                new Point(knob, track.Y + h / 2), knobRadius, knobRadius);
            PrimeLegacyControlVisuals.DrawFocusMarker(context, body, IsFocused,
                IsEnabled);
        }
    }

    /// <summary>A label and something to type in.</summary>
    internal sealed class FieldRow : Panel
    {
        public TextBox Box { get; }

        /// <summary>Raised for user edits as well as an explicit draft update.</summary>
        public event EventHandler? Changed;

        public string Value
        {
            get => Box.Text ?? "";
            set => Box.Text = value;
        }

        public FieldRow(string label, string value, double boxWidth = 150)
        {
            Height = PrimeLegacyControlVisuals.FieldHeight;
            PrimeAccessibility.SetName(this, label);
            PrimeAccessibility.SetDescription(this, $"{label}. Enter a value.");
            var caption = new TextBlock
            {
                Text = label,
                FontFamily = GuiTheme.Display,
                FontSize = 13,
                Foreground = GuiTheme.TextDimBrush,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(4, 0, 0, 0)
            };
            // Colours are left to the Fluent dark theme rather than set here:
            // the text box's template paints from its own theme resources and
            // ignores a Background put on the control, so setting one would
            // look like it was doing something while the theme decided.
            Box = new TextBox
            {
                Text = value,
                Width = boxWidth,
                FontFamily = GuiTheme.Display,
                FontSize = 13,
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 4, 8, 4),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            PrimeAccessibility.SetName(Box, label);
            PrimeAccessibility.SetDescription(Box, "Enter a value.");
            Box.TextChanged += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
            Children.Add(caption);
            Children.Add(Box);
        }
    }

    /// <summary>A line of explanation, wrapped, under a group of rows.</summary>
    internal sealed class Note : TextBlock
    {
        public Note(string text, Color? color = null)
        {
            Text = text;
            FontFamily = GuiTheme.Display;
            FontSize = 12;
            Foreground = new SolidColorBrush(color ?? GuiTheme.TextDim);
            TextWrapping = TextWrapping.Wrap;
            Margin = new Thickness(4, 4, 4, 4);
        }
    }
}
