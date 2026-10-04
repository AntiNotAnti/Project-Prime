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
                _transition = PrimeMotion.Page(old, next, () =>
                {
                    if (!ReferenceEquals(_current, next)) return;
                    if (_transitionLayer != null)
                        _transitionLayer.Children.Remove(next);
                    Content = next;
                    _transitionLayer = null;
                    _transition = null;
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
            _transition?.Cancel();
            _transition = null;
            if (_current != null && _transitionLayer != null)
                _transitionLayer.Children.Remove(_current);
            _transitionLayer = null;
            if (_current != null && !ReferenceEquals(Content, _current))
                Content = _current;
        }
    }
}
#endif
