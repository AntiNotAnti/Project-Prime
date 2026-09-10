using System.Collections.Generic;
using MphRead.Mods.Input;

namespace MphRead.Droid
{
    /// <summary>Routes neutral samples while retaining per-pointer capture.</summary>
    internal sealed class PointerInputRouter
    {
        private enum Route { Touch, Stylus }
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
            if (route == Route.Stylus) _stylus.PointerMove(sample);
            else _touch.PointerMove(sample.Id, sample.X, sample.Y, sample.Timestamp);
        }

        public void PointerUp(in PointerSample sample)
        {
            if (!_routes.Remove(sample.Id, out Route route)) return;
            if (route == Route.Stylus)
            {
                _stylus.PointerUp(sample);
                _touch.AimSuppressed = _stylus.Active;
            }
            else _touch.PointerUp(sample.Id, sample.Timestamp);
        }

        public void PointerProximityExit(in PointerSample sample)
        {
            if (_routes.TryGetValue(sample.Id, out Route route)
                && route == Route.Stylus)
            {
                PointerUp(sample);
            }
        }

        public void Cancel()
        {
            _routes.Clear();
            _stylus.Cancel();
            _touch.AimSuppressed = false;
            _touch.ReleaseEverything();
        }
    }
}
