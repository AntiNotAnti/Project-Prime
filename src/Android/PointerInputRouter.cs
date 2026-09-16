using System.Collections.Generic;
using MphRead.Mods.Input;

namespace MphRead.Droid
{
    /// <summary>Routes neutral samples while retaining per-pointer capture.</summary>
    internal sealed class PointerInputRouter
    {
        private enum Route
        {
            Touch,
            Stylus,
            BottomScreen,
            BottomScreenAim,
            BottomScreenSuppressed,
            TouchControlSuppressedAim
        }
        private readonly TouchControls _touch;
        private readonly StylusInput _stylus;
        private readonly Dictionary<long, Route> _routes = new();
        private long _bottomInteractionEpoch;

        public PointerInputRouter(TouchControls touch, StylusInput stylus)
        {
            _touch = touch;
            _stylus = stylus;
        }

        public void PointerDown(in PointerSample sample)
        {
            lock (_routes) PointerDownCore(sample);
        }

        private void PointerDownCore(in PointerSample sample)
        {
            ReconcileBottomScreenEpoch();
            // The scene-owned DS panel gets first refusal. A claimed contact
            // must never also become aim or a touch-control action.
            NativeBottomScreenPointerRoute bottomRoute
                = NativeBottomScreenPlatformBridge.RoutePointerDown(sample);
            if (bottomRoute == NativeBottomScreenPointerRoute.Aim)
            {
                _routes[sample.Id] = Route.BottomScreenAim;
                _bottomInteractionEpoch
                    = NativeBottomScreenPlatformBridge.InteractionEpoch;
                if (_stylus.PointerDown(
                    NativeBottomScreenPlatformBridge.MapAimSample(sample)))
                {
                    _touch.AimSuppressed = true;
                }
                else
                {
                    NativeBottomScreenPlatformBridge.CancelPointer(sample.Id);
                    _routes[sample.Id] = Route.BottomScreenSuppressed;
                    RefreshAimSuppression();
                }
                return;
            }
            if (bottomRoute == NativeBottomScreenPointerRoute.Control)
            {
                _routes[sample.Id] = Route.BottomScreen;
                _bottomInteractionEpoch
                    = NativeBottomScreenPlatformBridge.InteractionEpoch;
                return;
            }
            if (bottomRoute == NativeBottomScreenPointerRoute.Suppressed)
            {
                _routes[sample.Id] = Route.BottomScreenSuppressed;
                _bottomInteractionEpoch
                    = NativeBottomScreenPlatformBridge.InteractionEpoch;
                return;
            }
            bool pen = sample.Tool is PointerToolKind.Stylus or PointerToolKind.Eraser;
            bool finger = sample.Tool == PointerToolKind.Finger;
            bool visibleControl = _touch.HitsVisibleControl(sample.X, sample.Y);
            bool trueDsSurface
                = NativeBottomScreenPlatformBridge.SuppressGlobalStylus;
            if (trueDsSurface && pen)
            {
                _routes[sample.Id] = Route.BottomScreenSuppressed;
                _bottomInteractionEpoch
                    = NativeBottomScreenPlatformBridge.InteractionEpoch;
                return;
            }
            if (trueDsSurface && finger)
            {
                _bottomInteractionEpoch
                    = NativeBottomScreenPlatformBridge.InteractionEpoch;
                if (!visibleControl)
                {
                    _routes[sample.Id] = Route.BottomScreenSuppressed;
                    return;
                }
                _routes[sample.Id] = Route.TouchControlSuppressedAim;
                _touch.AimSuppressed = true;
                if (TryAndroidPointerId(sample.Id, out int controlPointerId))
                {
                    _touch.PointerDown(controlPointerId, sample.X, sample.Y,
                        sample.Timestamp);
                }
                return;
            }
            // Mouse/unknown intentionally retain the existing touch/UI path;
            // only an identified pen may claim the dedicated stylus path.
            Route route = pen && !visibleControl
                ? Route.Stylus : Route.Touch;
            _routes[sample.Id] = route;
            if (route == Route.Stylus)
            {
                if (_stylus.PointerDown(sample))
                {
                    _touch.AimSuppressed = true;
                }
                else
                {
                    _routes.Remove(sample.Id);
                }
            }
            else
            {
                if (TryAndroidPointerId(sample.Id, out int pointerId))
                    _touch.PointerDown(pointerId, sample.X, sample.Y,
                        sample.Timestamp);
            }
        }

        public void PointerMove(in PointerSample sample)
        {
            lock (_routes) PointerMoveCore(sample);
        }

        private void PointerMoveCore(in PointerSample sample)
        {
            ReconcileBottomScreenEpoch();
            if (!_routes.TryGetValue(sample.Id, out Route route)) return;
            if (route == Route.BottomScreen)
            {
                NativeBottomScreenPlatformBridge.TryPointerMove(sample);
            }
            else if (route == Route.BottomScreenAim)
            {
                if (NativeBottomScreenPlatformBridge.TryPointerMove(sample))
                    _stylus.PointerMove(
                        NativeBottomScreenPlatformBridge.MapAimSample(sample));
                else
                {
                    _stylus.Cancel();
                    _routes.Remove(sample.Id);
                    RefreshAimSuppression();
                }
            }
            else if (route == Route.Stylus) _stylus.PointerMove(sample);
            else if ((route == Route.Touch
                    || route == Route.TouchControlSuppressedAim)
                && TryAndroidPointerId(sample.Id, out int pointerId))
                _touch.PointerMove(pointerId, sample.X, sample.Y, sample.Timestamp);
        }

        /// <summary>
        /// Deliver a stationary pen button/axis sample. It is deliberately a
        /// separate path from PointerMove so a barrel-button event cannot
        /// create a look delta or reset the contact anchor.
        /// </summary>
        public void PointerButton(in PointerSample sample)
        {
            lock (_routes) PointerButtonCore(sample);
        }

        private void PointerButtonCore(in PointerSample sample)
        {
            ReconcileBottomScreenEpoch();
            if (sample.Tool is not (PointerToolKind.Stylus
                or PointerToolKind.Eraser)) return;
            if (!_routes.TryGetValue(sample.Id, out Route route))
            {
                // Hover button events can arrive without a preceding hover
                // callback. Track them as stylus state only; StylusBindings
                // requires contact before exposing a gameplay action.
                _routes[sample.Id] = Route.Stylus;
                route = Route.Stylus;
            }
            if (route == Route.BottomScreen)
            {
                NativeBottomScreenPlatformBridge.TryPointerMove(sample);
            }
            else if (route == Route.BottomScreenAim)
            {
                if (!NativeBottomScreenPlatformBridge.TryPointerMove(sample))
                {
                    _stylus.Cancel();
                    _routes.Remove(sample.Id);
                    RefreshAimSuppression();
                    return;
                }
                PointerSample mapped = NativeBottomScreenPlatformBridge
                    .MapAimSample(sample) with
                {
                    Pressure = sample.Pressure,
                    Buttons = sample.Buttons
                };
                _stylus.UpdateButtonState(mapped);
            }
            else if (route == Route.Stylus) _stylus.UpdateButtonState(sample);
        }

        /// <summary>Track pen hover without granting look ownership.</summary>
        public void PointerProximityMove(in PointerSample sample)
        {
            lock (_routes) PointerProximityMoveCore(sample);
        }

        private void PointerProximityMoveCore(in PointerSample sample)
        {
            ReconcileBottomScreenEpoch();
            if (sample.Tool is not (PointerToolKind.Stylus
                or PointerToolKind.Eraser)) return;
            _routes[sample.Id] = Route.Stylus;
            _stylus.PointerProximityMove(sample);
        }

        public void PointerUp(in PointerSample sample)
        {
            lock (_routes) PointerUpCore(sample);
        }

        private void PointerUpCore(in PointerSample sample)
        {
            ReconcileBottomScreenEpoch();
            if (!_routes.TryGetValue(sample.Id, out Route route)) return;
            if (route == Route.BottomScreen)
            {
                NativeBottomScreenPlatformBridge.TryPointerUp(sample);
                _routes.Remove(sample.Id);
            }
            else if (route == Route.BottomScreenAim)
            {
                if (NativeBottomScreenPlatformBridge.TryPointerUp(sample))
                {
                    _stylus.PointerUp(
                        NativeBottomScreenPlatformBridge.MapAimSample(sample));
                }
                else _stylus.Cancel();
                _routes.Remove(sample.Id);
                RefreshAimSuppression();
            }
            else if (route == Route.BottomScreenSuppressed)
            {
                NativeBottomScreenPlatformBridge.CancelPointer(sample.Id);
                _routes.Remove(sample.Id);
            }
            else if (route == Route.Stylus)
            {
                _stylus.PointerUp(sample);
                // Keep the route while the pen remains in proximity so
                // hover/button events after a normal lift still update state.
                if (!_stylus.InProximity) _routes.Remove(sample.Id);
                RefreshAimSuppression();
            }
            else
            {
                _routes.Remove(sample.Id);
                if (TryAndroidPointerId(sample.Id, out int pointerId))
                    _touch.PointerUp(pointerId, sample.Timestamp);
                RefreshAimSuppression();
            }
        }

        public void PointerProximityExit(in PointerSample sample)
        {
            lock (_routes) PointerProximityExitCore(sample);
        }

        private void PointerProximityExitCore(in PointerSample sample)
        {
            ReconcileBottomScreenEpoch();
            if (_routes.TryGetValue(sample.Id, out Route route)
                && (route == Route.BottomScreen
                    || route == Route.BottomScreenSuppressed))
            {
                NativeBottomScreenPlatformBridge.CancelPointer(sample.Id);
                _routes.Remove(sample.Id);
            }
            else if (_routes.TryGetValue(sample.Id, out route)
                && route == Route.BottomScreenAim)
            {
                NativeBottomScreenPlatformBridge.CancelPointer(sample.Id);
                _stylus.Cancel();
                _routes.Remove(sample.Id);
                RefreshAimSuppression();
            }
            else if (_routes.TryGetValue(sample.Id, out route)
                && route == Route.Stylus)
            {
                _stylus.PointerProximityExit(sample);
                _routes.Remove(sample.Id);
                RefreshAimSuppression();
            }
        }

        public void Cancel()
        {
            lock (_routes) CancelCore();
        }

        private void CancelCore()
        {
            _routes.Clear();
            NativeBottomScreenPlatformBridge.Cancel();
            _stylus.Cancel();
            _touch.AimSuppressed = false;
            _touch.ReleaseEverything();
        }

        /// <summary>
        /// Discards platform-side captures when the scene invalidates its
        /// bottom-screen interaction state between native input callbacks.
        /// </summary>
        public void ReconcileBottomScreenEpoch()
        {
            lock (_routes) ReconcileBottomScreenEpochCore();
        }

        private void ReconcileBottomScreenEpochCore()
        {
            long current = NativeBottomScreenPlatformBridge.InteractionEpoch;
            if (_bottomInteractionEpoch == current) return;

            List<long>? stale = null;
            bool cancelStylus = false;
            foreach ((long id, Route route) in _routes)
            {
                if (route is not (Route.BottomScreen
                    or Route.BottomScreenAim
                    or Route.BottomScreenSuppressed
                    or Route.TouchControlSuppressedAim)) continue;
                stale ??= new List<long>();
                stale.Add(id);
                cancelStylus |= route == Route.BottomScreenAim;
            }
            if (stale != null)
            {
                foreach (long id in stale)
                {
                    if (_routes.Remove(id, out Route route))
                    {
                        if (route == Route.TouchControlSuppressedAim
                            && TryAndroidPointerId(id, out int pointerId))
                        {
                            _touch.PointerUp(pointerId);
                        }
                        else NativeBottomScreenPlatformBridge.CancelPointer(id);
                    }
                }
                if (cancelStylus) _stylus.Cancel();
                RefreshAimSuppression();
            }
            _bottomInteractionEpoch
                = NativeBottomScreenPlatformBridge.InteractionEpoch;
        }

        private void RefreshAimSuppression()
        {
            bool suppressed = _stylus.Active;
            if (!suppressed)
            {
                foreach (Route route in _routes.Values)
                {
                    if (route is Route.BottomScreenAim
                        or Route.TouchControlSuppressedAim)
                    {
                        suppressed = true;
                        break;
                    }
                }
            }
            _touch.AimSuppressed = suppressed;
        }

        private static bool TryAndroidPointerId(long id, out int pointerId)
        {
            if (id < int.MinValue || id > int.MaxValue)
            {
                pointerId = default;
                return false;
            }
            pointerId = (int)id;
            return true;
        }
    }
}
