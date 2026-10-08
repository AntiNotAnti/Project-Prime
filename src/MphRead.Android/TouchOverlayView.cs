using System;
using Android.Content;
using Android.Graphics;
using Android.Views;

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
#if MPHREAD_RMLUI_ANDROID
        internal Func<bool>? NativeVisible;
        internal Func<MotionEvent, bool>? NativeTouch;
        internal Func<MotionEvent, bool>? NativeHover;
#endif
        private readonly Paint _fill = new Paint(PaintFlags.AntiAlias);
        private readonly Paint _stroke = new Paint(PaintFlags.AntiAlias);
        private readonly Paint _text = new Paint(PaintFlags.AntiAlias);

        private static readonly Color _edge = Color.Argb(150, 38, 46, 60);
        private static readonly Color _panel = Color.Argb(70, 26, 31, 41);
        private static readonly Color _accent = Color.Argb(210, 41, 197, 255);
        private static readonly Color _accentFill = Color.Argb(90, 41, 197, 255);
        private static readonly Color _label = Color.Argb(190, 138, 147, 166);

        public TouchOverlayView(Context context, TouchControls controls) : base(context)
        {
            _controls = controls;
            // FIRE becoming SCAN is decided by the game thread, not by a
            // touch, so it has to ask for the repaint. PostInvalidate is the
            // one that may be called from off the UI thread.
            controls.Invalidated = InvalidateNextFrame;
            SetWillNotDraw(false);
            _stroke.SetStyle(Paint.Style.Stroke);
            _fill.SetStyle(Paint.Style.Fill);
            _text.SetStyle(Paint.Style.Fill);
            _text.TextAlign = Paint.Align.Center;
        }

        private void InvalidateNextFrame()
        {
            // MotionEvent can arrive much faster than the display. Ask Android
            // for at most one redraw on the next display pulse instead of
            // invalidating the Canvas once per digitizer sample.
            PostInvalidateOnAnimation();
        }

        protected override void OnSizeChanged(int w, int h, int oldw, int oldh)
        {
            base.OnSizeChanged(w, h, oldw, oldh);
            _controls.Layout(w, h, Resources?.DisplayMetrics?.Density ?? 1f);
            InvalidateNextFrame();
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
            InvalidateNextFrame();
        }

        protected override void OnDraw(Canvas canvas)
        {
            base.OnDraw(canvas);
#if MPHREAD_RMLUI_ANDROID
            if (NativeVisible?.Invoke() == true) return;
#endif
#if MPHREAD_AVALONIA
            if (AndroidUiSurface.Current?.Visible == true)
            {
                // A screen is drawn into the frame under this one and every
                // touch belongs to it -- see OnTouchEvent. Buttons a finger
                // cannot reach are worse than no buttons.
                return;
            }
#endif
            if (MphRead.Mods.Chat.ChatBox.Composing)
            {
                // Chat owns the glass while the soft keyboard is up. Reset the
                // Paint alpha first because normal gameplay may have applied a
                // reduced touch-overlay opacity on the previous frame.
                float density = Resources?.DisplayMetrics?.Density ?? 1f;
                float cell = Width / 4f;
                _fill.Color = _panel;
                _fill.Alpha = _panel.A;
                canvas.DrawRect(0, 0, Width, 56 * density, _fill);
                _text.Color = _label;
                _text.Alpha = _label.A;
                _text.TextSize = 14 * density;
                string[] labels =
                {
                    MphRead.Mods.Chat.ChatBox.HistoryOpen ? "CLOSE HISTORY" : "HISTORY",
                    "NEWEST",
                    "SEND",
                    "CANCEL"
                };
                for (int i = 0; i < labels.Length; i++)
                {
                    canvas.DrawText(labels[i], cell * (i + .5f),
                        _text.TextSize * 2, _text);
                }
                return;
            }
            // Controller activity must not change presentation. Enabled touch
            // targets remain visually stable at the opacity the player chose,
            // while the overlay continues to receive touches in mixed
            // controller/touch play.
            float opacity = MphRead.Mods.Input.TouchSettings.OverlayOpacity;
            if (opacity <= 0f)
            {
                // Drawing nothing does not disable hit testing: OnTouchEvent
                // still hands every contact to TouchControls.
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
                _fill.Alpha = ScaledAlpha(_fill.Color, opacity);
                _stroke.Color = held ? _accent : _edge;
                _stroke.Alpha = ScaledAlpha(_stroke.Color, opacity);
                canvas.DrawCircle(button.CentreX, button.CentreY, button.Radius, _fill);
                canvas.DrawCircle(button.CentreX, button.CentreY, button.Radius, _stroke);
                _text.Color = held ? _accent : _label;
                _text.Alpha = ScaledAlpha(_text.Color, opacity);
                _text.TextSize = button.Radius * 0.42f;
                canvas.DrawText(button.Label, button.CentreX,
                    button.CentreY + _text.TextSize * 0.35f, _text);
            }
            if (_controls.StickActive)
            {
                _stroke.Color = _edge;
                _stroke.Alpha = ScaledAlpha(_stroke.Color, opacity);
                _fill.Color = _panel;
                _fill.Alpha = ScaledAlpha(_fill.Color, opacity);
                canvas.DrawCircle(_controls.StickX, _controls.StickY, _controls.StickRadius, _fill);
                canvas.DrawCircle(_controls.StickX, _controls.StickY, _controls.StickRadius, _stroke);
                _fill.Color = _accentFill;
                _fill.Alpha = ScaledAlpha(_fill.Color, opacity);
                _stroke.Color = _accent;
                _stroke.Alpha = ScaledAlpha(_stroke.Color, opacity);
                canvas.DrawCircle(_controls.StickKnobX, _controls.StickKnobY,
                    _controls.StickKnobRadius, _fill);
                canvas.DrawCircle(_controls.StickKnobX, _controls.StickKnobY,
                    _controls.StickKnobRadius, _stroke);
            }
        }

        private static int ScaledAlpha(Color color, float opacity)
        {
            return Math.Clamp((int)MathF.Round(color.A * opacity), 0, 255);
        }

        private float _historyTouchY;
        private int _historyPointer = -1;

        public override bool OnTouchEvent(MotionEvent? e)
        {
#if MPHREAD_RMLUI_ANDROID
            if (e != null && NativeVisible?.Invoke() == true) { NativeTouch?.Invoke(e); return true; }
#endif
            if (e == null)
            {
                return false;
            }
#if MPHREAD_AVALONIA
            if (AndroidUiSurface.Current is AndroidUiSurface surface && surface.Visible)
            {
                // The results panel is the one moment in a match when nobody
                // is aiming, so while it is up every touch is the screen's.
                // The desktop says the same thing by releasing the cursor for
                // as long as the panel is drawn.
                return HandUp(surface, e);
            }
#endif
            if (MphRead.Mods.Chat.ChatBox.Composing)
            {
                _controls.ReleaseEverything();
                if (e.ActionMasked == MotionEventActions.Down)
                {
                    _historyTouchY = e.GetY();
                    _historyPointer = e.GetPointerId(0);
                    if (_historyTouchY < 56 * (Resources?.DisplayMetrics?.Density ?? 1f))
                    {
                        switch (Math.Clamp(
                            (int)(e.GetX() / Math.Max(1, Width / 4f)), 0, 3))
                        {
                            case 0:
                                MphRead.Mods.Chat.ChatBox.ToggleHistory();
                                break;
                            case 1:
                                MphRead.Mods.Chat.ChatBox.JumpHistoryNewest();
                                break;
                            case 2:
                                MphRead.Mods.Chat.ChatBox.Submit();
                                break;
                            case 3:
                                MphRead.Mods.Chat.ChatBox.Cancel();
                                break;
                        }
                        _historyPointer = -1;
                    }
                }
                else if (e.ActionMasked == MotionEventActions.Move
                    && _historyPointer >= 0)
                {
                    int index = e.FindPointerIndex(_historyPointer);
                    if (index >= 0)
                    {
                        float step = 20 * (Resources?.DisplayMetrics?.Density ?? 1f);
                        int lines = (int)((e.GetY(index) - _historyTouchY) / step);
                        MphRead.Mods.Chat.ChatBox.ScrollHistory(lines);
                        _historyTouchY += lines * step;
                    }
                }
                else if (e.ActionMasked is MotionEventActions.Up
                    or MotionEventActions.Cancel)
                {
                    _historyPointer = -1;
                }
                InvalidateNextFrame();
                return true;
            }
            bool redraw = false;
            switch (e.ActionMasked)
            {
            case MotionEventActions.Down:
            case MotionEventActions.PointerDown:
                {
                    int index = e.ActionIndex;
                    _controls.PointerDown(e.GetPointerId(index), e.GetX(index), e.GetY(index));
                    redraw = true;
                }
                break;
            case MotionEventActions.Move:
                redraw = DispatchMoveSamples(e);
                break;
            case MotionEventActions.Up:
            case MotionEventActions.PointerUp:
                // An UP can carry the last bit of movement too. Feed it before
                // releasing ownership so a short final flick is not lost.
                redraw = DispatchMoveSamples(e);
                _controls.PointerUp(e.GetPointerId(e.ActionIndex));
                redraw = true;
                break;
            case MotionEventActions.Cancel:
                _controls.ReleaseEverything();
                redraw = true;
                break;
            default:
                return false;
            }
            if (redraw)
            {
                InvalidateNextFrame();
            }
            return true;
        }
#if MPHREAD_RMLUI_ANDROID
        protected override bool DispatchHoverEvent(MotionEvent? e) => e != null && NativeVisible?.Invoke() == true
            && NativeHover?.Invoke(e) == true || base.DispatchHoverEvent(e);
#endif

        // A real device commonly batches several high-rate digitizer samples
        // into one UI-thread MotionEvent. Replaying the whole history keeps the
        // distance, velocity and gesture timing intact. For presentation, the
        // batch is spread over at most 20 ms, centred on delivery: half is
        // immediately eligible and half is allowed to land on the next
        // high-refresh frame(s). The 60 Hz simulation still consumes the exact
        // complete delta without this presentation timing.
        private const long AimReplayWindowMs = 20;

        private bool DispatchMoveSamples(MotionEvent e)
        {
            int history = e.HistorySize;
            long deliveredAt = Environment.TickCount64;
            long firstEventTime = history > 0
                ? e.GetHistoricalEventTime(0)
                : e.EventTime;
            long eventSpan = Math.Max(0, e.EventTime - firstEventTime);
            long replayWindow = Math.Min(eventSpan, AimReplayWindowMs);
            long replayStart = deliveredAt - replayWindow / 2;
            bool redraw = false;

            for (int h = 0; h < history; h++)
            {
                long sourceTime = e.GetHistoricalEventTime(h);
                long presentAt = PresentationTime(
                    sourceTime, firstEventTime, eventSpan, replayStart, replayWindow);
                for (int i = 0; i < e.PointerCount; i++)
                {
                    redraw |= _controls.PointerMove(
                        e.GetPointerId(i),
                        e.GetHistoricalX(i, h),
                        e.GetHistoricalY(i, h),
                        sourceTime,
                        presentAt);
                }
            }

            long currentPresentAt = history > 0
                ? replayStart + replayWindow
                : deliveredAt;
            for (int i = 0; i < e.PointerCount; i++)
            {
                redraw |= _controls.PointerMove(
                    e.GetPointerId(i),
                    e.GetX(i),
                    e.GetY(i),
                    e.EventTime,
                    currentPresentAt);
            }
            return redraw;
        }

        private static long PresentationTime(long sourceTime, long firstEventTime,
            long eventSpan, long replayStart, long replayWindow)
        {
            if (eventSpan <= 0 || replayWindow <= 0)
            {
                return replayStart;
            }
            double progress = (sourceTime - firstEventTime) / (double)eventSpan;
            return replayStart + (long)Math.Round(progress * replayWindow);
        }

        /// <summary>
        /// One finger, as the pointer the toolkit would have been given by a
        /// windowing system. The first only: these screens are a menu, and a
        /// second finger on a menu is a finger resting on the glass.
        /// </summary>
#if MPHREAD_AVALONIA
        private static bool HandUp(AndroidUiSurface surface, MotionEvent e)
        {
            if (e.ActionMasked == MotionEventActions.PointerDown
                || e.ActionMasked == MotionEventActions.PointerUp)
            {
                return true;
            }
            float x = e.GetX(0);
            float y = e.GetY(0);
            switch (e.ActionMasked)
            {
            case MotionEventActions.Down:
                // No preparatory move: a touch contact carries its own
                // position and TouchBegin is the first thing Avalonia may
                // hear about this id. An update ahead of it is an update for
                // a contact that does not exist yet, and the press that
                // followed landed wherever that resolved to.
                surface.TouchDown(x, y);
                break;
            case MotionEventActions.Move:
                surface.TouchMove(x, y);
                break;
            case MotionEventActions.Up:
                surface.TouchUp(x, y);
                break;
            case MotionEventActions.Cancel:
                surface.TouchUp(x, y);
                break;
            default:
                return false;
            }
            return true;
        }
#endif
    }
}
