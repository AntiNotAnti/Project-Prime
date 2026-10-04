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
        private Grid? _transitionLayer;

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

            if (old == null || ReferenceEquals(old, next) || PrimeMotion.Reduced)
            {
                Content = next;
                PrimeMotion.Enter(next);
            }
            else
            {
                // A control can only have one Avalonia parent. Detach the current
                // workspace from the ContentPresenter before both views are placed
                // in the temporary transition layer.
                if (ReferenceEquals(Content, old))
                    Content = null;
                var layers = new Grid();
                layers.Children.Add(old);
                layers.Children.Add(next);
                _transitionLayer = layers;
                Content = layers;
                Mods.DebugLog.Line("ui-motion", $"route {_active} transition started");
                _transition = PrimeMotion.Page(old, next, () =>
                {
                    if (!ReferenceEquals(_current, next)
                        || !ReferenceEquals(_transitionLayer, layers))
                        return;

                    // Both cached workspaces must be released from the temporary
                    // transition parent before it is abandoned. Leaving the outgoing
                    // page parented to this orphaned Grid makes the next visit to that
                    // route throw when Avalonia tries to parent it again.
                    layers.Children.Clear();
                    Content = next;
                    _transitionLayer = null;
                    _transition = null;
                    Mods.DebugLog.Line("ui-motion", $"route {_active} transition completed");
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

            if (_transitionLayer is { } layers)
            {
                // Release every cached page, not just the current one. A cancelled
                // transition owns both the incoming and outgoing workspace.
                layers.Children.Clear();
                _transitionLayer = null;
            }

            if (_current != null && !ReferenceEquals(Content, _current))
                Content = _current;

            if (transition != null)
                Mods.DebugLog.Line("ui-motion", "route transition cancelled cleanly");
        }
    }
}
#endif
