using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using MphRead.Mods.Network;
using MphRead.Mods.Launcher.Core;

namespace MphRead.Mods.Launcher.Gui
{
    /// <summary>Shared lobby and live-match owner controls. The server validates every request.</summary>
    internal sealed class BotManagementView : StackPanel
    {
        internal static readonly string[] Difficulties = { "EASY", "NORMAL", "HARD", "INSANE" };
        private readonly ComboBox _hunter = new() { ItemsSource = new[] { "Samus", "Kanden", "Trace", "Sylux", "Noxus", "Spire", "Weavel", "Random" }, SelectedIndex = 7 };
        private readonly ComboBox _level = new() { ItemsSource = Difficulties, SelectedIndex = 1 };
        private readonly ComboBox _suit = new() { ItemsSource = new[] { "Suit 1", "Suit 2", "Suit 3", "Suit 4" }, SelectedIndex = 0 };
        private readonly ComboBox _team = new() { ItemsSource = new[] { "Auto team", "Team A", "Team B", "Team C", "Team D" }, SelectedIndex = 0 };
        private readonly ComboBox _target = new();
        private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Foreground = HubTheme.TextDimBrush };
        private readonly StackPanel _controls = new() { Spacing = 5 };
        private readonly PrimeButton _add, _update, _remove;
        private Hunter[] _hunters = Enumerable.Range(0, 7).Select(i => (Hunter)i).Append(Hunter.Random).ToArray();
        private bool? _lowTier;
        private byte[] _slots = Array.Empty<byte>();
        private string _rosterKey = "";
        private readonly LobbySessionController? _lobby;

        internal BotManagementView(LobbySessionController? lobby = null)
        {
            _lobby = lobby;
            Spacing = 5;
            Children.Add(new TextBlock { Text = "BOTS · PRACTICE MATCH\nHunter License progression disabled when bots are used.",
                TextWrapping = TextWrapping.Wrap, Foreground = HubTheme.WarmBrush });
            foreach (var (label, control) in new (string, Control)[] {
                ("Hunter", _hunter), ("Difficulty", _level), ("Suit", _suit), ("Team", _team), ("Existing bot", _target) })
            {
                _controls.Children.Add(new TextBlock { Text = label, Foreground = HubTheme.TextDimBrush });
                _controls.Children.Add(control);
            }
            _target.SelectionChanged += (_, _) =>
            {
                if (_target.SelectedIndex < 0 || _target.SelectedIndex >= _slots.Length) return;
                int slot = _slots[_target.SelectedIndex];
                _hunter.SelectedIndex = Math.Max(0, Array.IndexOf(_hunters, NetSession.SlotHunter[slot]));
                _level.SelectedIndex = NetSession.SlotBotLevel[slot];
                _suit.SelectedIndex = PlayerColors.Choice[slot];
                _team.SelectedIndex = NetSession.SlotTeamIndex[slot] + 1;
            };
            _add = new PrimeButton("ADD BOT", () => Send(LobbyCommandType.AddBot), compact: true);
            _update = new PrimeButton("UPDATE SELECTED BOT", () => Send(LobbyCommandType.UpdateBot), compact: true);
            _remove = new PrimeButton("REMOVE SELECTED BOT", () => Send(LobbyCommandType.RemoveBot), compact: true);
            _controls.Children.Add(_add); _controls.Children.Add(_update); _controls.Children.Add(_remove);
            Children.Add(_controls); Children.Add(_status);
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            timer.Tick += (_, _) => Refresh();
            AttachedToVisualTree += (_, _) => { Refresh(); timer.Start(); };
            DetachedFromVisualTree += (_, _) => timer.Stop();
        }
        private void Send(LobbyCommandType type)
        {
            byte target = _target.SelectedIndex >= 0 && _target.SelectedIndex < _slots.Length ? _slots[_target.SelectedIndex] : (byte)255;
            Hunter hunter = _hunters[Math.Clamp(_hunter.SelectedIndex, 0, _hunters.Length - 1)];
            if (_lobby is { } lobby)
            {
                LobbyIntentKind kind = type switch
                {
                    LobbyCommandType.AddBot => LobbyIntentKind.AddBot,
                    LobbyCommandType.UpdateBot => LobbyIntentKind.UpdateBot,
                    _ => LobbyIntentKind.RemoveBot
                };
                LobbyActionResult result = lobby.Dispatch(lobby.Intent(kind) with
                {
                    TargetSlot = target, Team = (sbyte)(_team.SelectedIndex - 1), Hunter = hunter,
                    Color = (byte)_suit.SelectedIndex, BotLevel = (byte)_level.SelectedIndex
                });
                if (!result.Accepted)
                {
                    Refresh();
                    _status.Text = result.Message;
                    return;
                }
            }
            else
            {
                // In-match pause controls remain on the engine's existing command path.
                NetSession.SendLobbyCommand(type, target, (sbyte)(_team.SelectedIndex - 1),
                    hunter: (byte)hunter, color: (byte)_suit.SelectedIndex, botLevel: (byte)_level.SelectedIndex);
            }
            Refresh();
        }
        private void Refresh()
        {
            bool lowTier = NetSession.ActiveMatchDefinition?.LowTier == true;
            if (_lowTier != lowTier)
            {
                Hunter chosen = _hunters[Math.Clamp(_hunter.SelectedIndex, 0, _hunters.Length - 1)];
                _hunters = Multiplayer.HunterRules.Pool(lowTier).Append(Hunter.Random).ToArray();
                _hunter.ItemsSource = _hunters.Select(h => h.ToString()).ToArray();
                _hunter.SelectedIndex = Math.Max(0, Array.IndexOf(_hunters, chosen));
                _lowTier = lowTier;
            }
            var roster = NetSession.LobbyRoster();
            string key = string.Join("|", Enumerable.Range(0, roster.Count).Where(roster.IsBot)
                .Select(i => $"{roster.Slots[i]}:{roster.Generations[i]}"));
            if (key != _rosterKey)
            {
                byte selected = _target.SelectedIndex >= 0 && _target.SelectedIndex < _slots.Length ? _slots[_target.SelectedIndex] : (byte)255;
                var entries = Enumerable.Range(0, roster.Count).Where(roster.IsBot).ToArray();
                _slots = entries.Select(i => roster.Slots[i]).ToArray();
                _target.ItemsSource = entries.Select(i => $"{roster.Names[i]} · {Difficulties[roster.BotLevels[i]]} · slot {roster.Slots[i] + 1}").ToArray();
                _target.SelectedIndex = _slots.Length == 0 ? -1 : Math.Max(0, Array.IndexOf(_slots, selected));
                _rosterKey = key;
            }
            _controls.IsEnabled = NetSession.LocalIsLobbyOwner && !NetSession.LobbyCommandPending
                && (NetSession.IsInLobby || NetSession.IsPlaying);
            _add.IsEnabled = roster.Count < (NetSession.ServerSession?.MaxPlayers ?? 8);
            _update.IsEnabled = _remove.IsEnabled = _slots.Length > 0;
            _status.Text = NetSession.LobbyMessage;
        }
    }
}
