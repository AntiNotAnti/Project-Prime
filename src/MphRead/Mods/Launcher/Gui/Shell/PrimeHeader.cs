#if MPHREAD_AVALONIA
using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
namespace MphRead.Mods.Launcher.Gui
{
    internal sealed class PrimeHeader : Border
    {
        private readonly Dictionary<PrimeRoute, PrimeTabButton> _tabs = new();
        private readonly TextBlock _status = PrimeChrome.Text("LOCAL SYSTEM", 10, PrimeTheme.GreenBrush, true);
        private readonly PrimeButton _profile;
        private readonly PrimeButton _lobby;
        public PrimeHeader(Action<PrimeRoute> navigate)
        {
            Height = PrimeMetrics.HeaderHeight;
            Background = PrimeTheme.BackgroundDeepBrush;
            BorderBrush = PrimeTheme.BorderBrush;
            BorderThickness = new Thickness(0, 0, 0, 1);
            Padding = new Thickness(24, 12);
            var identityCopy = PrimeChrome.Stack(PrimeChrome.Title("PROJECT PRIME"), _status);
            identityCopy.Spacing = 4;
            var nav = new Grid { ColumnDefinitions = new("*,1.2*,*,*,*"), ColumnSpacing = 3 };
            int index = 0;
            foreach (var route in PrimeRouter.Tabs)
            {
                string label = route switch
                {
                    PrimeRoute.Play => "PLAY",
                    PrimeRoute.Community => "COMMUNITY",
                    PrimeRoute.Studio => "STUDIO",
                    PrimeRoute.Offline => "OFFLINE",
                    PrimeRoute.Settings => "SETTINGS",
                    _ => route.ToString().ToUpperInvariant()
                };
                var button = new PrimeTabButton(label, () => navigate(route));
                ControllerNav.Identify(button, "prime.nav." + route);
                Grid.SetColumn(button, index++); nav.Children.Add(button); _tabs.Add(route, button);
            }
            _profile = new PrimeButton("PLAYER", () => navigate(PrimeRoute.HunterLicense), compact: true);
            ControllerNav.Identify(_profile, "prime.profile");
            _lobby = new PrimeButton("LOBBY ACTIVE", () => navigate(PrimeRoute.Lobby), compact: true)
                { IsVisible = false, MinHeight = 22, Height = 22 };
            ControllerNav.Identify(_lobby, "prime.lobby");
            var session = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
            session.Children.Add(_profile); session.Children.Add(_lobby);
            Child = PrimeChrome.Columns("230,*,185", identityCopy, nav, session);
        }
        public void SetRoute(PrimeRoute route)
        {
            foreach (var (key, tab) in _tabs) tab.Selected = key == (route == PrimeRoute.Lobby ? PrimeRoute.Play : route);
        }
        public void Refresh(PrimeGlobalState state)
        {
            _status.Text = state.GameFilesReady ? "● LOCAL SYSTEM READY" : "● GAME FILES REQUIRED";
            _profile.Label = state.PlayerName.ToUpperInvariant() + " // " + state.Hunter.ToUpperInvariant();
            _profile.MinHeight = state.LobbyActive ? 32 : PrimeMetrics.TabHeight;
            _profile.Height = _profile.MinHeight;
            _lobby.IsVisible = state.LobbyActive;
            _lobby.Label = $"● LOBBY ACTIVE // {state.LobbyPlayerCount}";
        }
    }
    internal sealed class PrimeFooter : Border
    {
        private readonly TextBlock _hints = PrimeChrome.Text("", 10, data: true);
        private string? _statusOverride;
        public void SetStatus(string text) { _statusOverride = text; Version.Label = text; }
        public readonly PrimeButton Version = new("BUILD // LOCAL");
        public PrimeFooter(Action update)
        {
            Height = PrimeMetrics.FooterHeight;
            Background = PrimeTheme.BackgroundDeepBrush;
            BorderBrush = PrimeTheme.BorderBrush; BorderThickness = new Thickness(0, 1, 0, 0);
            Padding = new Thickness(24, 2);
            _hints.VerticalAlignment = VerticalAlignment.Center;
            Version.MinHeight = 32;
            Version.Click += (_, _) => update();
            Child = PrimeChrome.Columns("*,Auto", _hints, Version);
        }
        public void Refresh(PrimeGlobalState state)
        {
            _hints.Text = Mods.Input.InputSourceTracker.Current == Mods.Input.InputSource.Gamepad
                ? $"{Mods.Input.InputPrompt.For(Mods.Input.UiAction.Accept).Glyph} SELECT  //  {Mods.Input.InputPrompt.For(Mods.Input.UiAction.PreviousTab).Glyph} / {Mods.Input.InputPrompt.For(Mods.Input.UiAction.NextTab).Glyph} TABS  //  {Mods.Input.InputPrompt.For(Mods.Input.UiAction.Back).Glyph} BACK"
                : "[ENTER] SELECT   //   [Q / E] SWITCH TAB   //   [ESC] BACK";
            Version.Label = _statusOverride ?? (state.UpdateAvailable ? "UPDATE AVAILABLE // INSTALL" : "SIM: 60 HZ  //  BUILD " + state.BuildVersion);
        }
    }
}
#endif
