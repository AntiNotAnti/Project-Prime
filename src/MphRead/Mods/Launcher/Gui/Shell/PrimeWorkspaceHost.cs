#if MPHREAD_AVALONIA
using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Threading;

namespace MphRead.Mods.Launcher.Gui
{
    internal interface IPrimeWorkspace
    {
        void OnActivated();
        void OnDeactivated();
        void Refresh();
    }

    internal sealed class PrimeWorkspaceHost : ContentControl, IDisposable
    {
        private readonly Dictionary<PrimeRoute, Control> _cache = new();
        private readonly Dictionary<PrimeRoute, Control?> _focus = new();
        private readonly Func<PrimeRoute, Control> _create;
        private PrimeRoute? _active;
        private Control? _current;
        private PrimeMotionHandle? _transition;

        public Func<bool>? HasOverlay { get; set; }

        public PrimeWorkspaceHost(Func<PrimeRoute, Control> create)
        {
            _create = create;
            ClipToBounds = true;
        }

        public Control Get(PrimeRoute route)
        {
            if (!_cache.TryGetValue(route, out var view))
                _cache[route] = view = _create(route);
            return view;
        }

        public Control? TryGet(PrimeRoute route) => _cache.GetValueOrDefault(route);
        public void Set(PrimeRoute route, Control view) => _cache[route] = view;

        public void Remove(PrimeRoute route)
        {
            CancelTransition();
            if (_cache.Remove(route, out var view))
            {
                if (ReferenceEquals(view, _current))
                {
                    _current = null;
                    Content = null;
                }
                if (view is IDisposable disposable) disposable.Dispose();
            }
            _focus.Remove(route);
        }

        public void Dispose()
        {
            CancelTransition();
            foreach (var view in _cache.Values)
                (view as IDisposable)?.Dispose();
            _cache.Clear();
            _focus.Clear();
            _current = null;
            Content = null;
        }

        public void Show(PrimeRoute route)
        {
            CancelTransition();
            Control? old = _current;

            if (_active is { } previous && old != null)
            {
                _focus[previous] = FocusNavigator.Focused(old);
                (old as IPrimeWorkspace)?.OnDeactivated();
            }

            Control next = Get(route);
            _active = route;
            _current = next;
            (next as IPrimeWorkspace)?.OnActivated();

            // Swap the workspace immediately. The old cross-fade kept two full
            // cached pages alive in one temporary Grid and made the outgoing page
            // visibly tear/re-layout before disappearing. It also created a whole
            // class of single-parent lifecycle hazards. Animate only the incoming
            // page after the content swap: the transition is cleaner, cheaper and
            // cannot expose stale pixels from the previous destination.
            Content = next;

            if (old == null || ReferenceEquals(old, next) || PrimeMotion.Reduced)
            {
                PrimeMotion.Enter(next);
            }
            else
            {
                Mods.DebugLog.Line("ui-motion", $"route {route} incoming transition started");
                _transition = PrimeMotion.Page(next, () =>
                {
                    if (!ReferenceEquals(_current, next)) return;
                    _transition = null;
                    Mods.DebugLog.Line("ui-motion", $"route {route} incoming transition completed");
                });
            }

            RestoreFocus(route, next);
        }

        private void RestoreFocus(PrimeRoute route, Control next)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (_active != route || HasOverlay?.Invoke() == true) return;
                if (_focus.TryGetValue(route, out var focused)
                    && focused is { IsEffectivelyVisible: true, IsEffectivelyEnabled: true })
                    FocusNavigator.Focus(focused);
                else
                    FocusNavigator.Ensure(next);
            }, DispatcherPriority.Background);
        }

        private void CancelTransition()
        {
            PrimeMotionHandle? transition = _transition;
            _transition = null;
            transition?.Cancel();
            if (transition != null)
                Mods.DebugLog.Line("ui-motion", "incoming route transition cancelled cleanly");
        }
    }
}
#endif
