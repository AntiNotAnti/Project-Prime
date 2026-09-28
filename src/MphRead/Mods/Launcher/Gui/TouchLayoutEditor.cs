using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using MphRead.Mods.Input;

namespace MphRead.Mods.Launcher.Gui
{
    /// <summary>
    /// Phone-layout preview/editor for the Android touch overlay.
    ///
    /// It deliberately mirrors the HUD Studio interaction: tap a control to
    /// select it, drag to move it, pinch to resize it, and use the inspector
    /// rows around the canvas for precise selection and scaling.
    /// </summary>
    internal sealed class TouchLayoutEditor : Control
    {
        private readonly Dictionary<TouchControl, TouchButtonLayout> _layout;
        private readonly HashSet<TouchControl> _enabled = new();
        private readonly Dictionary<IPointer, Point> _touches = new();

        private TouchControl _selected = TouchControl.Shoot;
        private IPointer? _dragPointer;
        private bool _dragging;
        private double _pinchDistance;
        private float _pinchScale = 1f;
        private float _buttonScale;
        private float _stickScale;

        public event Action? SelectionChanged;
        public event Action? Changed;

        public TouchLayoutEditor()
        {
            _layout = TouchSettings.CopyLayout();
            foreach ((TouchControl control, _) in TouchSettings.Order)
            {
                if (TouchSettings.IsEnabled(control))
                {
                    _enabled.Add(control);
                }
            }
            _buttonScale = TouchSettings.ButtonScale;
            _stickScale = TouchSettings.StickScale;
            Height = 320;
            MinHeight = 240;
            Focusable = true;
        }

        public TouchControl Selected => _selected;

        public float ButtonScale
        {
            get => _buttonScale;
            set
            {
                _buttonScale = Math.Clamp(value,
                    TouchSettings.MinButtonScale, TouchSettings.MaxButtonScale);
                InvalidateVisual();
            }
        }

        public float StickScale
        {
            get => _stickScale;
            set
            {
                _stickScale = Math.Clamp(value,
                    TouchSettings.MinStickScale, TouchSettings.MaxStickScale);
                InvalidateVisual();
            }
        }

        public float SelectedScale => LayoutOf(_selected).Scale;

        public Dictionary<TouchControl, TouchButtonLayout> CopyLayout()
        {
            return new Dictionary<TouchControl, TouchButtonLayout>(_layout);
        }

        public void Select(TouchControl control)
        {
            if (_selected == control)
            {
                return;
            }
            _selected = control;
            SelectionChanged?.Invoke();
            InvalidateVisual();
        }

        public void SetEnabled(TouchControl control, bool enabled)
        {
            if (enabled)
            {
                _enabled.Add(control);
            }
            else
            {
                _enabled.Remove(control);
            }
            InvalidateVisual();
        }

        public void SetSelectedScale(float scale)
        {
            TouchButtonLayout value = LayoutOf(_selected);
            SetLayout(_selected, value.X, value.Y, scale);
        }

        public void ResetSelected()
        {
            if (_layout.Remove(_selected))
            {
                Changed?.Invoke();
                SelectionChanged?.Invoke();
                InvalidateVisual();
            }
        }

        public void ResetAll()
        {
            if (_layout.Count == 0)
            {
                return;
            }
            _layout.Clear();
            Changed?.Invoke();
            SelectionChanged?.Invoke();
            InvalidateVisual();
        }

        private Rect Surface
        {
            get
            {
                double maxWidth = Math.Max(1, Bounds.Width - 12);
                double maxHeight = Math.Max(1, Bounds.Height - 12);
                double width = Math.Min(maxWidth, maxHeight * 16.0 / 9.0);
                double height = width * 9.0 / 16.0;
                if (height > maxHeight)
                {
                    height = maxHeight;
                    width = height * 16.0 / 9.0;
                }
                return new Rect(
                    (Bounds.Width - width) / 2,
                    (Bounds.Height - height) / 2,
                    width, height);
            }
        }

        private TouchButtonLayout LayoutOf(TouchControl control)
        {
            if (_layout.TryGetValue(control, out TouchButtonLayout custom))
            {
                return custom;
            }
            TouchButtonGeometry baseline = TouchSettings.DefaultGeometry(control, 1920, 1080);
            return new TouchButtonLayout(
                baseline.X / 1920f,
                baseline.Y / 1080f,
                1f);
        }

        private TouchButtonGeometry GeometryOf(TouchControl control)
        {
            Rect surface = Surface;
            return TouchSettings.Geometry(control,
                (float)surface.Width, (float)surface.Height,
                _layout, _buttonScale);
        }

        private Point CentreOf(TouchControl control)
        {
            Rect surface = Surface;
            TouchButtonGeometry geometry = GeometryOf(control);
            return new Point(surface.X + geometry.X, surface.Y + geometry.Y);
        }

        private void SetLayout(TouchControl control, float x, float y, float scale)
        {
            _layout[control] = new TouchButtonLayout(
                Math.Clamp(x, 0f, 1f),
                Math.Clamp(y, 0f, 1f),
                Math.Clamp(scale,
                    TouchSettings.MinIndividualScale,
                    TouchSettings.MaxIndividualScale));
            Changed?.Invoke();
            InvalidateVisual();
        }

        private static IBrush ButtonFill(bool enabled, bool selected)
        {
            if (selected)
            {
                return new SolidColorBrush(Color.FromArgb(145, 41, 197, 255));
            }
            return enabled
                ? new SolidColorBrush(Color.FromArgb(105, 30, 47, 62))
                : new SolidColorBrush(Color.FromArgb(38, 30, 47, 62));
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);
            Rect surface = Surface;
            if (surface.Width <= 0 || surface.Height <= 0)
            {
                return;
            }

            context.FillRectangle(new SolidColorBrush(Color.Parse("#0D141D")), surface);
            context.DrawRectangle(null,
                new Pen(new SolidColorBrush(Color.Parse("#405064")), 1), surface);

            // The floating movement stick can appear anywhere on the left half.
            // Show its reachable half and a sample stick without pretending the
            // runtime stick has a fixed position.
            Rect left = new Rect(surface.X, surface.Y, surface.Width / 2, surface.Height);
            context.FillRectangle(new SolidColorBrush(Color.FromArgb(20, 70, 120, 160)), left);
            context.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(80, 120, 140, 160)), 1),
                new Point(surface.Center.X, surface.Top),
                new Point(surface.Center.X, surface.Bottom));

            double stickRadius = surface.Height * 0.15 * _stickScale;
            Point stick = new Point(surface.X + surface.Width * 0.16,
                surface.Bottom - surface.Height * 0.22);
            context.DrawEllipse(new SolidColorBrush(Color.FromArgb(35, 138, 147, 166)),
                new Pen(new SolidColorBrush(Color.FromArgb(90, 138, 147, 166)), 1),
                stick, stickRadius, stickRadius);
            DrawLabel(context, "FLOATING STICK", new Point(stick.X, stick.Y),
                Math.Max(8, stickRadius * 0.16), Brushes.LightGray);

            foreach ((TouchControl control, _) in TouchSettings.Order)
            {
                TouchButtonGeometry geometry = GeometryOf(control);
                Point centre = new Point(surface.X + geometry.X, surface.Y + geometry.Y);
                bool enabled = _enabled.Contains(control);
                bool selected = control == _selected;
                using (context.PushOpacity(enabled ? 1 : 0.38))
                {
                    IBrush fill = ButtonFill(enabled, selected);
                    IBrush edge = selected ? Brushes.Cyan
                        : enabled ? Brushes.LightSlateGray : Brushes.DimGray;
                    context.DrawEllipse(fill, new Pen(edge, selected ? 2 : 1),
                        centre, geometry.Radius, geometry.Radius);
                    DrawLabel(context, TouchSettings.ShortLabel(control), centre,
                        Math.Max(7, geometry.Radius * 0.31),
                        selected ? Brushes.White : Brushes.LightGray);
                }
            }

            string hint = $"{TouchSettings.LabelOf(_selected)}  •  drag to move  •  pinch to resize";
            var text = new FormattedText(hint, CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, new Typeface("monospace"), 11, Brushes.White);
            context.FillRectangle(new SolidColorBrush(Color.FromArgb(175, 0, 0, 0)),
                new Rect(surface.X + 6, surface.Bottom - text.Height - 10,
                    Math.Min(surface.Width - 12, text.Width + 12), text.Height + 6));
            context.DrawText(text, new Point(surface.X + 12, surface.Bottom - text.Height - 7));
        }

        private static void DrawLabel(DrawingContext context, string label,
            Point centre, double size, IBrush brush)
        {
            var text = new FormattedText(label, CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, new Typeface("monospace"), size, brush);
            context.DrawText(text, new Point(
                centre.X - text.Width / 2,
                centre.Y - text.Height / 2));
        }

        private TouchControl? Hit(Point point)
        {
            Rect surface = Surface;
            for (int i = TouchSettings.Order.Length - 1; i >= 0; i--)
            {
                TouchControl control = TouchSettings.Order[i].Control;
                TouchButtonGeometry geometry = GeometryOf(control);
                double dx = point.X - (surface.X + geometry.X);
                double dy = point.Y - (surface.Y + geometry.Y);
                double radius = geometry.Radius + 7;
                if (dx * dx + dy * dy <= radius * radius)
                {
                    return control;
                }
            }
            return null;
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            Focus();
            Point point = e.GetPosition(this);
            if (!Surface.Contains(point))
            {
                return;
            }

            if (e.Pointer.Type == PointerType.Touch)
            {
                _touches[e.Pointer] = point;
                if (_touches.Count == 2)
                {
                    _pinchDistance = TouchDistance();
                    _pinchScale = SelectedScale;
                    _dragging = false;
                    e.Pointer.Capture(this);
                    e.Handled = true;
                    return;
                }
            }

            TouchControl? hit = Hit(point);
            if (hit.HasValue)
            {
                Select(hit.Value);
                _dragPointer = e.Pointer;
                _dragging = true;
                e.Pointer.Capture(this);
                e.Handled = true;
            }
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            if (_touches.ContainsKey(e.Pointer))
            {
                _touches[e.Pointer] = e.GetPosition(this);
            }
            if (_touches.Count >= 2 && _pinchDistance > 0)
            {
                double distance = TouchDistance();
                SetSelectedScale(_pinchScale * (float)(distance / _pinchDistance));
                SelectionChanged?.Invoke();
                e.Handled = true;
                return;
            }
            if (!_dragging || _dragPointer != e.Pointer)
            {
                return;
            }

            Rect surface = Surface;
            Point point = e.GetPosition(this);
            TouchButtonLayout current = LayoutOf(_selected);
            float x = (float)((point.X - surface.X) / Math.Max(1, surface.Width));
            float y = (float)((point.Y - surface.Y) / Math.Max(1, surface.Height));
            SetLayout(_selected, x, y, current.Scale);
            e.Handled = true;
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            _touches.Remove(e.Pointer);
            if (_dragPointer == e.Pointer)
            {
                _dragging = false;
                _dragPointer = null;
            }
            if (_touches.Count < 2)
            {
                _pinchDistance = 0;
            }
            e.Pointer.Capture(null);
        }

        protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
        {
            base.OnPointerCaptureLost(e);
            _touches.Remove(e.Pointer);
            if (_dragPointer == e.Pointer)
            {
                _dragPointer = null;
                _dragging = false;
            }
            if (_touches.Count < 2)
            {
                _pinchDistance = 0;
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Key == Key.R)
            {
                ResetSelected();
                e.Handled = true;
                return;
            }
            if (e.Key is not (Key.Left or Key.Right or Key.Up or Key.Down))
            {
                return;
            }

            Rect surface = Surface;
            TouchButtonGeometry geometry = GeometryOf(_selected);
            float step = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 10f : 2f;
            float x = geometry.X
                + (e.Key == Key.Left ? -step : e.Key == Key.Right ? step : 0);
            float y = geometry.Y
                + (e.Key == Key.Up ? -step : e.Key == Key.Down ? step : 0);
            TouchButtonLayout current = LayoutOf(_selected);
            SetLayout(_selected,
                x / Math.Max(1f, (float)surface.Width),
                y / Math.Max(1f, (float)surface.Height),
                current.Scale);
            e.Handled = true;
        }

        private double TouchDistance()
        {
            Point first = default;
            bool found = false;
            foreach (Point point in _touches.Values)
            {
                if (!found)
                {
                    first = point;
                    found = true;
                }
                else
                {
                    double dx = point.X - first.X;
                    double dy = point.Y - first.Y;
                    return Math.Sqrt(dx * dx + dy * dy);
                }
            }
            return 0;
        }
    }
}
