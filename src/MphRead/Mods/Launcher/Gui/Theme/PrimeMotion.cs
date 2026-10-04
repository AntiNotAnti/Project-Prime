#if MPHREAD_AVALONIA
using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace MphRead.Mods.Launcher.Gui
{
    /// <summary>
    /// Short-lived motion primitives for the Prime shell.
    ///
    /// The desktop launcher is an off-screen Avalonia surface, so every visible
    /// animation frame is a full Skia raster/upload. All shell motion therefore
    /// goes through Deck.NextFrame, stays finite, and respects Reduce Motion.
    /// </summary>
    internal static class PrimeMotion
    {
        public const double PressSeconds = 0.075;
        public const double ButtonSeconds = 0.12;
        public const double PageSeconds = 0.22;
        public const double ModalInSeconds = 0.24;
        public const double ModalOutSeconds = 0.16;
        public const double ScrimSeconds = 0.18;
        private const double PageInDistance = 28;
        private const double PageStartOpacity = 0.48;

        public static bool Reduced => Deck.Still || LauncherPrefs.ReduceMotion;

        public static double Settle(double progress) =>
            Deck.Bezier(Math.Clamp(progress, 0, 1), 0.3, 0.8, 0.4, 1);

        public static double Spring(double progress) =>
            Deck.Spring(Math.Clamp(progress, 0, 1));

        public static PrimeMotionHandle Tween(Control owner, double seconds,
            Action<double> apply, Action? completed = null, Action? cancelled = null,
            Func<double, double>? easing = null)
        {
            if (Reduced || seconds <= 0)
            {
                apply(1);
                completed?.Invoke();
                return PrimeMotionHandle.Completed();
            }

            var clock = new Stopwatch();
            PrimeMotionHandle? handle = null;
            EventHandler<VisualTreeAttachmentEventArgs>? attached = null;
            bool started = false;

            void StopWaitingForAttach()
            {
                if (attached == null) return;
                owner.AttachedToVisualTree -= attached;
                attached = null;
            }

            handle = new PrimeMotionHandle(finish =>
            {
                StopWaitingForAttach();
                if (finish)
                {
                    apply(1);
                    completed?.Invoke();
                }
                else
                {
                    cancelled?.Invoke();
                }
            });

            void Step()
            {
                if (handle == null || !handle.IsActive)
                    return;
                if (Reduced)
                {
                    handle.Cancel(finish: true);
                    return;
                }
                if (TopLevel.GetTopLevel(owner) == null)
                {
                    // Once an animation has started, losing the visual tree means
                    // its owner was removed. Finish cleanly rather than keeping a
                    // hidden surface alive waiting for frames it can no longer draw.
                    handle.Cancel(finish: true);
                    return;
                }

                double raw = Math.Clamp(clock.Elapsed.TotalSeconds / seconds, 0, 1);
                apply((easing ?? Settle)(raw));
                if (raw >= 1)
                {
                    if (handle.TryComplete())
                        completed?.Invoke();
                    return;
                }
                Deck.NextFrame(owner, Step);
            }

            void Start()
            {
                if (started || handle == null || !handle.IsActive) return;
                started = true;
                StopWaitingForAttach();
                clock.Restart();
                Deck.NextFrame(owner, Step);
            }

            if (TopLevel.GetTopLevel(owner) != null)
            {
                Start();
            }
            else
            {
                // ContentControl/Panel parenting happens before Avalonia has put
                // the new visual under a TopLevel. The old code scheduled a frame
                // immediately, saw no TopLevel on that first frame, and snapped
                // straight to the final pose. Start the clock on attachment instead.
                attached = (_, _) => Start();
                owner.AttachedToVisualTree += attached;
            }

            return handle;
        }

        public static PrimeMotionHandle Enter(Control control, double lift = 10, double seconds = 0.18)
        {
            double opacity = control.Opacity;
            var original = control.RenderTransform;
            var move = new TranslateTransform(0, lift);
            control.Opacity = 0;
            control.RenderTransform = move;

            void Restore()
            {
                control.Opacity = opacity;
                if (ReferenceEquals(control.RenderTransform, move))
                    control.RenderTransform = original;
            }

            return Tween(control, seconds, p =>
            {
                control.Opacity = opacity * p;
                move.Y = lift * (1 - p);
            }, Restore, Restore);
        }

        public static PrimeMotionHandle Page(Control incoming, Action? completed = null)
        {
            double opacity = incoming.Opacity;
            var original = incoming.RenderTransform;
            var move = new TranslateTransform(PageInDistance, 0);
            incoming.RenderTransform = move;
            incoming.Opacity = opacity * PageStartOpacity;

            void Restore()
            {
                incoming.Opacity = opacity;
                if (ReferenceEquals(incoming.RenderTransform, move))
                    incoming.RenderTransform = original;
            }

            return Tween(incoming, PageSeconds, p =>
            {
                incoming.Opacity = opacity
                    * (PageStartOpacity + (1 - PageStartOpacity) * p);
                move.X = PageInDistance * (1 - p);
            }, () =>
            {
                Restore();
                completed?.Invoke();
            }, Restore);
        }

        public static PrimeMotionHandle ModalIn(Control frame, Action? completed = null)
        {
            double opacity = frame.Opacity;
            var original = frame.RenderTransform;
            var scale = new ScaleTransform(0.94, 0.94);
            var move = new TranslateTransform(0, 22);
            var group = new TransformGroup();
            group.Children.Add(scale);
            group.Children.Add(move);
            frame.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
            frame.RenderTransform = group;
            frame.Opacity = 0;

            void Restore()
            {
                frame.Opacity = opacity;
                if (ReferenceEquals(frame.RenderTransform, group))
                    frame.RenderTransform = original;
            }

            return Tween(frame, ModalInSeconds, p =>
            {
                double spring = Spring(p);
                double settle = Settle(p);
                double s = 0.94 + (1 - 0.94) * spring;
                scale.ScaleX = s;
                scale.ScaleY = s;
                move.Y = 22 * (1 - spring);
                frame.Opacity = opacity * settle;
            }, () =>
            {
                Restore();
                completed?.Invoke();
            }, Restore, progress => progress);
        }

        public static PrimeMotionHandle ModalOut(Control frame, Action? completed = null)
        {
            double opacity = frame.Opacity;
            var original = frame.RenderTransform;
            var scale = new ScaleTransform(1, 1);
            var move = new TranslateTransform();
            var group = new TransformGroup();
            group.Children.Add(scale);
            group.Children.Add(move);
            frame.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
            frame.RenderTransform = group;

            void Restore()
            {
                frame.Opacity = opacity;
                if (ReferenceEquals(frame.RenderTransform, group))
                    frame.RenderTransform = original;
            }

            return Tween(frame, ModalOutSeconds, p =>
            {
                double s = 1 - 0.015 * p;
                scale.ScaleX = s;
                scale.ScaleY = s;
                move.Y = 8 * p;
                frame.Opacity = opacity * (1 - p);
            }, () =>
            {
                completed?.Invoke();
                Restore();
            }, Restore);
        }

        public static PrimeMotionHandle Fade(Control control, double from, double to,
            double seconds = ScrimSeconds, Action? completed = null)
        {
            control.Opacity = from;
            return Tween(control, seconds,
                p => control.Opacity = from + (to - from) * p,
                completed: completed);
        }
    }

    internal sealed class PrimeMotionHandle
    {
        private Action<bool>? _cancel;

        internal PrimeMotionHandle(Action<bool> cancel) => _cancel = cancel;
        private PrimeMotionHandle() { }

        public bool IsActive => _cancel != null;

        public void Cancel(bool finish = false)
        {
            Action<bool>? cancel = _cancel;
            _cancel = null;
            cancel?.Invoke(finish);
        }

        internal bool TryComplete()
        {
            if (_cancel == null)
                return false;
            _cancel = null;
            return true;
        }

        internal static PrimeMotionHandle Completed() => new();
    }
}
#endif
