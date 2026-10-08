using System;
using MphRead.Mods.Launcher.Core;
namespace MphRead.Mods.Launcher.Gui
{
    internal enum PrimeRoute { News, Play, HunterLicense, Theatre, Forge, Offline, Settings, Lobby }

    // Navigation policy contains no rendering or session ownership.
    internal sealed class PrimeRouter : IDisposable
    {
        public static readonly PrimeRoute[] Tabs = { PrimeRoute.News, PrimeRoute.Play,
            PrimeRoute.HunterLicense, PrimeRoute.Theatre, PrimeRoute.Forge, PrimeRoute.Offline, PrimeRoute.Settings };
        // Temporary legacy facade: history and page lifetimes have one toolkit-free owner.
        public LauncherRouter ApplicationRouter { get; }
        public PrimeRoute Current => FromCore(ApplicationRouter.Current.Page);
        public PrimeRoute? Previous => ApplicationRouter.Previous is { } route ? FromCore(route.Page) : null;
        public Func<PrimeRoute, bool>? CanNavigate { get; set; }
        public Func<bool>? NavigationGuardEnabled { get; set; }
        public event Action<PrimeRoute>? Changed;
        private PrimeRoute _last;
        private readonly bool _ownsRouter;
        private readonly Func<LauncherRoute, bool> _guard;
        public PrimeRouter(LauncherRouter? applicationRouter = null)
        {
            ApplicationRouter = applicationRouter ?? new();
            _ownsRouter = applicationRouter == null;
            _last = Current;
            _guard = route => NavigationGuardEnabled?.Invoke() == false
                || CanNavigate?.Invoke(FromCore(route.Page)) != false;
            ApplicationRouter.CanNavigate = _guard;
            ApplicationRouter.Changed += OnApplicationChanged;
        }
        private void OnApplicationChanged(LauncherNavigationSnapshot snapshot)
        {
            // An earlier subscriber can redirect synchronously and publish a
            // newer page before this invocation resumes. Never show the retired page.
            if (snapshot.Lifetime.Id != ApplicationRouter.Lifetime.Id) return;
            PrimeRoute route = FromCore(snapshot.Current.Page);
            if (route == _last) return;
            _last = route;
            Changed?.Invoke(route);
        }
        public bool Navigate(PrimeRoute route)
        {
            var outcome = ApplicationRouter.Navigate(new(ToCore(route)));
            return outcome is LauncherNavigationOutcome.Changed or LauncherNavigationOutcome.Unchanged;
        }
        public bool Back()
        {
            return ApplicationRouter.Back() != LauncherNavigationOutcome.ExitConfirmationRequired;
        }
        public void Forget(PrimeRoute route) => ApplicationRouter.Forget(ToCore(route));
        public void NextRoute() => Step(1);
        public void PreviousRoute() => Step(-1);
        private void Step(int direction)
        {
            int index = Array.IndexOf(Tabs, Current == PrimeRoute.Lobby ? PrimeRoute.Play : Current);
            Navigate(Tabs[(index + direction + Tabs.Length) % Tabs.Length]);
        }
        public void Dispose()
        {
            Changed = null;
            ApplicationRouter.Changed -= OnApplicationChanged;
            if (ApplicationRouter.CanNavigate == _guard) ApplicationRouter.CanNavigate = null;
            if (_ownsRouter) ApplicationRouter.Dispose();
        }

        internal static LauncherPage ToCore(PrimeRoute route) => route switch
        {
            PrimeRoute.News => LauncherPage.Home, PrimeRoute.Play => LauncherPage.Play,
            PrimeRoute.HunterLicense => LauncherPage.License, PrimeRoute.Theatre => LauncherPage.Theatre,
            PrimeRoute.Forge => LauncherPage.StudioLaunch, PrimeRoute.Offline => LauncherPage.Offline,
            PrimeRoute.Settings => LauncherPage.Settings, PrimeRoute.Lobby => LauncherPage.Lobby,
            _ => throw new ArgumentOutOfRangeException(nameof(route))
        };
        internal static PrimeRoute FromCore(LauncherPage page) => page switch
        {
            LauncherPage.Home or LauncherPage.News => PrimeRoute.News, LauncherPage.Play or LauncherPage.Social => PrimeRoute.Play,
            LauncherPage.License or LauncherPage.Hunters => PrimeRoute.HunterLicense,
            LauncherPage.Theatre => PrimeRoute.Theatre,
            LauncherPage.StudioLaunch or LauncherPage.Community => PrimeRoute.Forge,
            LauncherPage.Offline or LauncherPage.Adventure => PrimeRoute.Offline,
            LauncherPage.Settings => PrimeRoute.Settings, LauncherPage.Lobby => PrimeRoute.Lobby,
            _ => throw new ArgumentException("This route has no legacy workspace.", nameof(page))
        };
    }
}
