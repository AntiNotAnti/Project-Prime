using System;
using System.Collections.Generic;
namespace MphRead.Mods.Launcher.Gui
{
    internal enum PrimeRoute { News, Play, HunterLicense, Theatre, Forge, Offline, Settings, Lobby, Community }

    // Navigation policy contains no rendering or session ownership.
    internal sealed class PrimeRouter
    {
        public static readonly PrimeRoute[] Tabs = { PrimeRoute.News, PrimeRoute.Play,
            PrimeRoute.Community, PrimeRoute.HunterLicense, PrimeRoute.Theatre,
            PrimeRoute.Forge, PrimeRoute.Offline, PrimeRoute.Settings };
        private readonly List<PrimeRoute> _history = new();
        private readonly Dictionary<PrimeRoute, Action> _routeActions = new();
        public PrimeRoute Current { get; private set; } = PrimeRoute.News;
        public PrimeRoute? Previous => _history.Count == 0 ? null : _history[^1];
        public Func<PrimeRoute, bool>? CanNavigate { get; set; }
        // Registered external actions also run for an already active route,
        // after guards and before any workspace/history mutation.
        public void RegisterRouteAction(PrimeRoute route, Action action)
            => _routeActions[route] = action ?? throw new ArgumentNullException(nameof(action));
        public bool IsWorkspaceNavigation { get; private set; }
        public event Action<PrimeRoute>? Changed;
        public bool Navigate(PrimeRoute route) => NavigateCore(route, handleAction: true);
        // Explicit embedded tools and legacy entry points retain the ordinary
        // cached workspace without invoking the external route action.
        public bool NavigateWorkspace(PrimeRoute route) => NavigateCore(route, handleAction: false);
        private bool NavigateCore(PrimeRoute route, bool handleAction)
        {
            Action? action = null;
            if (handleAction) _routeActions.TryGetValue(route, out action);
            if (route == Current && action == null) return true;
            if (!CanEnter(route, workspace: !handleAction)) return false;
            if (action != null) { action(); return false; }
            _history.Add(Current);
            if (_history.Count > 64) _history.RemoveAt(0);
            Current = route;
            Changed?.Invoke(route);
            return true;
        }
        public bool Back()
        {
            if (_history.Count == 0) return false;
            var route = _history[^1];
            if (!CanEnter(route, workspace: true)) return true;
            _history.RemoveAt(_history.Count - 1);
            Current = route;
            Changed?.Invoke(route);
            return true;
        }
        private bool CanEnter(PrimeRoute route, bool workspace)
        {
            bool previous = IsWorkspaceNavigation;
            IsWorkspaceNavigation = workspace;
            try { return CanNavigate?.Invoke(route) != false; }
            finally { IsWorkspaceNavigation = previous; }
        }
        public void Forget(PrimeRoute route) => _history.RemoveAll(r => r == route);
        public void NextRoute() => Step(1);
        public void PreviousRoute() => Step(-1);
        private void Step(int direction)
        {
            int index = Array.IndexOf(Tabs, Current == PrimeRoute.Lobby ? PrimeRoute.Play : Current);
            Navigate(Tabs[(index + direction + Tabs.Length) % Tabs.Length]);
        }
    }
}
