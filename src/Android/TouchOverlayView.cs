using System;
using Android.Content;
using Android.Graphics;
using Android.Views;
using MphRead.Mods.Input;

namespace MphRead.Droid
{
    /// <summary>
    /// The controls a thumb sees, drawn over the game.
    ///
    /// A plain Android view rather than anything in GL: it is a dozen circles
    /// that change when they are touched, and putting them through the engine's
    /// renderer would mean HUD geometry, a second projection and a redraw every
    /// frame for something that changes when a finger moves. Here it redraws
    /// only when the touch state changes, and the game underneath is untouched.
    ///
    /// Its other job is to be the only thing receiving touches: it covers the
    /// surface, so every finger arrives here and is handed to
    /// <see cref="TouchControls"/>, which is where the meaning is.
    ///
    /// The palette is the launcher's (<c>GuiTheme</c>), by value -- the same
    /// reason that file gives for repeating LauncherTheme's numbers.
    /// </summary>
    internal sealed class TouchOverlayView : View
    {
        private readonly TouchControls _controls;
        private readonly PointerInputRouter _router;
        private readonly Paint _fill = new Paint(PaintFlags.AntiAlias);
        private readonly Paint _stroke = new Paint(PaintFlags.AntiAlias);
        private readonly Paint _text = new Paint(PaintFlags.AntiAlias);

        private static readonly Color _edge = Color.Argb(150, 38, 46, 60);
        private static readonly Color _panel = Color.Argb(70, 26, 31, 41);
        private static readonly Color _accent = Color.Argb(210, 41, 197, 255);
        private static readonly Color _accentFill = Color.Argb(90, 41, 197, 255);
        private static readonly Color _label = Color.Argb(190, 138, 147, 166);

        public TouchOverlayView(Context context, TouchControls controls,
            StylusInput stylus) : base(context)
        {
            _controls = controls;
            _router = new PointerInputRouter(controls, stylus);
            // FIRE becoming SCAN is decided by the game thread, not by a
            // touch, so it has to ask for the repaint. PostInvalidate is the
            // one that may be called from off the UI thread.
            controls.Invalidated = PostInvalidate;
            SetWillNotDraw(false);
            _stroke.SetStyle(Paint.Style.Stroke);
            _fill.SetStyle(Paint.Style.Fill);
            _text.SetStyle(Paint.Style.Fill);
            _text.TextAlign = Paint.Align.Center;
        }

        protected override void OnSizeChanged(int w, int h, int oldw, int oldh)
        {
            base.OnSizeChanged(w, h, oldw, oldh);
            _controls.Layout(w, h, Resources?.DisplayMetrics?.Density ?? 1f);
            Invalidate();
        }

        /// <summary>
        /// Lay the controls out again for the size this view already has, and
        /// repaint.
        ///
        /// For the rotation that does not resize anything -- see
        /// <c>MainActivity.OnDisplayChanged</c>. <see cref="OnSizeChanged"/>
        /// is the only other thing that calls <c>Layout</c>, and it does not
        /// fire when a phone is turned end for end.
        /// </summary>
        public void Refresh()
        {
            if (Width > 0 && Height > 0)
            {
                _controls.Layout(Width, Height, Resources?.DisplayMetrics?.Density ?? 1f);
            }
            RequestLayout();
            Invalidate();
        }

        public void CancelInput() => _router.Cancel();

        protected override void OnDraw(Canvas canvas)
        {
            base.OnDraw(canvas);
            if (_controls.PadDriving)
            {
                // A pad is being held: nothing is drawn, and the view stays
                // where it is rather than going away. It is still the only
                // thing receiving touches, and the first one brings the
                // controls back -- see TouchControls.PointerDown.
                return;
            }
            float unit = Math.Max(1f, Height / 100f);
            _stroke.StrokeWidth = Math.Max(2f, unit * 0.22f);
            foreach (TouchButton button in _controls.Buttons)
            {
                if (!button.Visible)
                {
                    continue;
                }
                bool held = _controls.IsHeld(button.Action);
                _fill.Color = held ? _accentFill : _panel;
                _stroke.Color = held ? _accent : _edge;
                canvas.DrawCircle(button.CentreX, button.CentreY, button.Radius, _fill);
                canvas.DrawCircle(button.CentreX, button.CentreY, button.Radius, _stroke);
                _text.Color = held ? _accent : _label;
                _text.TextSize = button.Radius * 0.42f;
                canvas.DrawText(button.Label, button.CentreX,
                    button.CentreY + _text.TextSize * 0.35f, _text);
            }
            if (_controls.StickActive)
            {
                _stroke.Color = _edge;
                _fill.Color = _panel;
                canvas.DrawCircle(_controls.StickX, _controls.StickY, _controls.StickRadius, _fill);
                canvas.DrawCircle(_controls.StickX, _controls.StickY, _controls.StickRadius, _stroke);
                _fill.Color = _accentFill;
                _stroke.Color = _accent;
                canvas.DrawCircle(_controls.StickKnobX, _controls.StickKnobY,
                    _controls.StickKnobRadius, _fill);
                canvas.DrawCircle(_controls.StickKnobX, _controls.StickKnobY,
                    _controls.StickKnobRadius, _stroke);
            }
        }

        public override bool OnTouchEvent(MotionEvent? e)
        {
            if (e == null)
            {
                return false;
            }
            switch (e.ActionMasked)
            {
            case MotionEventActions.Down:
            case MotionEventActions.PointerDown:
                {
                    int index = e.ActionIndex;
                    _router.PointerDown(Sample(e, index));
                }
                break;
            case MotionEventActions.Move:
                // Android batches high-report-rate samples. Preserve their
                // chronological order first, then pointer order within time.
                for (int history = 0; history < e.HistorySize; history++)
                {
                    for (int i = 0; i < e.PointerCount; i++)
                    {
                        _router.PointerMove(Sample(e, i, history));
                    }
                }
                for (int i = 0; i < e.PointerCount; i++)
                {
                    _router.PointerMove(Sample(e, i));
                }
                break;
            case MotionEventActions.Up:
            case MotionEventActions.PointerUp:
                _router.PointerUp(Sample(e, e.ActionIndex));
                break;
            case MotionEventActions.Cancel:
                _router.Cancel();
                break;
            default:
                return false;
            }
            Invalidate();
            return true;
        }

        public override bool OnHoverEvent(MotionEvent? e)
        {
            if (e == null) return false;
            if (e.ActionMasked == MotionEventActions.HoverExit)
            {
                for (int i = 0; i < e.PointerCount; i++)
                    _router.PointerProximityExit(Sample(e, i));
                return true;
            }
            return base.OnHoverEvent(e);
        }

        private static PointerSample Sample(MotionEvent e, int index,
            int history = -1)
        {
            float x = history >= 0 ? e.GetHistoricalX(index, history) : e.GetX(index);
            float y = history >= 0 ? e.GetHistoricalY(index, history) : e.GetY(index);
            float pressure = history >= 0
                ? e.GetHistoricalPressure(index, history) : e.GetPressure(index);
            long time = history >= 0 ? e.GetHistoricalEventTime(history) : e.EventTime;
            PointerToolKind tool = e.GetToolType(index) switch
            {
                MotionEventToolType.Finger => PointerToolKind.Finger,
                MotionEventToolType.Stylus => PointerToolKind.Stylus,
                MotionEventToolType.Eraser => PointerToolKind.Eraser,
                MotionEventToolType.Mouse => PointerToolKind.Mouse,
                _ => PointerToolKind.Unknown
            };
            int nativeButtons = (int)e.ButtonState;
            StylusButtons buttons = StylusButtons.None;
            if ((nativeButtons & 32) != 0) buttons |= StylusButtons.Primary;
            if ((nativeButtons & 64) != 0) buttons |= StylusButtons.Secondary;
            return new PointerSample(e.GetPointerId(index), tool, x, y,
                pressure, buttons, time);
        }
    }
}
