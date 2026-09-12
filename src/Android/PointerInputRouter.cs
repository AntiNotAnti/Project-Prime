using System.Collections.Generic;
using MphRead.Mods.Input;

namespace MphRead.Droid
{
    /// <summary>Routes neutral samples while retaining per-pointer capture.</summary>
    internal sealed class PointerInputRouter
    {
        private enum Route { Touch, Stylus, BottomScreen }
        private readonly TouchControls _touch;
        private readonly StylusInput _stylus;
        private readonly Dictionary<int, Route> _routes = new();

        public PointerInputRouter(TouchControls touch, StylusInput stylus)
        {
            _touch = touch;
            _stylus = stylus;
        }

        public void PointerDown(in PointerSample sample)
        {
            // The scene-owned DS panel gets first refusal. A claimed contact
            // must never also become aim or a touch-control action.
            if (NativeBottomScreenPlatformBridge.TryPointerDown(sample))
            {
                _routes[sample.Id] = Route.BottomScreen;
                return;
            }
            bool pen = sample.Tool is PointerToolKind.Stylus or PointerToolKind.Eraser;
            // Mouse/unknown intentionally retain the existing touch/UI path;
            // only an identified pen may claim the dedicated stylus path.
            Route route = pen && !_touch.HitsVisibleControl(sample.X, sample.Y)
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
                _touch.PointerDown(sample.Id, sample.X, sample.Y, sample.Timestamp);
            }
        }

        public void PointerMove(in PointerSample sample)
        {
            if (!_routes.TryGetValue(sample.Id, out Route route)) return;
            if (route == Route.BottomScreen)
            {
                NativeBottomScreenPlatformBridge.TryPointerMove(sample);
            }
            else if (route == Route.Stylus) _stylus.PointerMove(sample);
            else _touch.PointerMove(sample.Id, sample.X, sample.Y, sample.Timestamp);
        }

        /// <summary>
        /// Deliver a stationary pen button/axis sample. It is deliberately a
        /// separate path from PointerMove so a barrel-button event cannot
        /// create a look delta or reset the contact anchor.
        /// </summary>
        public void PointerButton(in PointerSample sample)
        {
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
            else if (route == Route.Stylus) _stylus.UpdateButtonState(sample);
        }

        /// <summary>Track pen hover without granting look ownership.</summary>
        public void PointerProximityMove(in PointerSample sample)
        {
            if (sample.Tool is not (PointerToolKind.Stylus
                or PointerToolKind.Eraser)) return;
            _routes[sample.Id] = Route.Stylus;
            _stylus.PointerProximityMove(sample);
        }

        public void PointerUp(in PointerSample sample)
        {
            if (!_routes.TryGetValue(sample.Id, out Route route)) return;
            if (route == Route.BottomScreen)
            {
                NativeBottomScreenPlatformBridge.TryPointerUp(sample);
                _routes.Remove(sample.Id);
            }
            else if (route == Route.Stylus)
            {
                _stylus.PointerUp(sample);
                _touch.AimSuppressed = _stylus.Active;
                // Keep the route while the pen remains in proximity so
                // hover/button events after a normal lift still update state.
                if (!_stylus.InProximity) _routes.Remove(sample.Id);
            }
            else
            {
                _routes.Remove(sample.Id);
                _touch.PointerUp(sample.Id, sample.Timestamp);
            }
        }

        public void PointerProximityExit(in PointerSample sample)
        {
            if (_routes.TryGetValue(sample.Id, out Route route)
                && route == Route.BottomScreen)
            {
                NativeBottomScreenPlatformBridge.CancelPointer(sample.Id);
                _routes.Remove(sample.Id);
            }
            else if (_routes.TryGetValue(sample.Id, out route)
                && route == Route.Stylus)
            {
                _stylus.PointerProximityExit(sample);
                _routes.Remove(sample.Id);
                _touch.AimSuppressed = _stylus.Active;
            }
        }

        public void Cancel()
        {
            _routes.Clear();
            NativeBottomScreenPlatformBridge.Cancel();
            _stylus.Cancel();
            _touch.AimSuppressed = false;
            _touch.ReleaseEverything();
        }
    }
}
