using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using FruityPrime.Server.Shared;
using AvaloniaButton = Avalonia.Controls.Button;

namespace MphRead.Mods.Launcher.Gui;

/// <summary>
/// Displays one authoritative seat offer and its real expiration time.  The
/// timer only runs while this card is attached, so the shell never gains a
/// continuous animation loop just for decoration.
/// </summary>
internal sealed class SeatOfferCard : Border
{
    private readonly DateTimeOffset _expiresAt;
    private readonly TextBlock _countdown;
    private readonly AvaloniaButton _accept;
    private readonly AvaloniaButton _decline;
    private readonly DispatcherTimer _timer;
    private bool _actionTaken;

    public SeatOfferCard(LobbyQueueOffer offer, Action accept, Action decline)
    {
        ArgumentNullException.ThrowIfNull(offer);
        ArgumentNullException.ThrowIfNull(accept);
        ArgumentNullException.ThrowIfNull(decline);

        _expiresAt = offer.ExpiresAt;
        Padding = new Thickness(16);
        Classes.Add("prime-card");
        BorderBrush = GuiTheme.WarmBrush;
        BorderThickness = new Thickness(3, 1, 1, 1);

        _countdown = new TextBlock { Classes = { "prime-title" } };
        _accept = OfferButton("Accept seat", () => RunOnce(accept), primary: true);
        _decline = OfferButton("Decline", () => RunOnce(decline));

        var actions = new WrapPanel { Orientation = Orientation.Horizontal };
        actions.Children.Add(_accept);
        actions.Children.Add(_decline);
        Child = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = "PLAYER SLOT AVAILABLE", Classes = { "prime-kicker" } },
                new TextBlock { Text = "A server seat is reserved for you. Choose once before the offer expires.",
                    Classes = { "prime-body" }, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                new TextBlock { Text = $"Offer identity · {offer.OfferId}", Classes = { "prime-muted" } },
                _countdown,
                new TextBlock { Text = $"Seat policy · {FormatPolicy(offer.Policy)}", Classes = { "prime-muted" } },
                actions
            }
        };

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _timer.Tick += (_, _) => UpdateCountdown();
        AttachedToVisualTree += (_, _) =>
        {
            UpdateCountdown();
            if (!_actionTaken) _timer.Start();
        };
        DetachedFromVisualTree += (_, _) => _timer.Stop();
        UpdateCountdown();
    }

    private static AvaloniaButton OfferButton(string text, Action action, bool primary = false)
    {
        AvaloniaButton button = PrimeControlFactory.Button(text, action, primary: primary);
        button.MinHeight = 44;
        button.MinWidth = text == "Accept seat" ? 120 : 100;
        return button;
    }

    private void RunOnce(Action action)
    {
        if (_actionTaken || DateTimeOffset.UtcNow >= _expiresAt) return;
        _actionTaken = true;
        _accept.IsEnabled = false;
        _decline.IsEnabled = false;
        _timer.Stop();
        action();
    }

    private void UpdateCountdown()
    {
        TimeSpan remaining = _expiresAt - DateTimeOffset.UtcNow;
        if (remaining <= TimeSpan.Zero)
        {
            _countdown.Text = "Offer expired";
            _accept.IsEnabled = false;
            _decline.IsEnabled = false;
            _timer.Stop();
            return;
        }

        int seconds = Math.Max(1, (int)Math.Ceiling(remaining.TotalSeconds));
        _countdown.Text = $"Accept within {seconds / 60}:{seconds % 60:00}";
    }

    private static string FormatPolicy(LobbySeatPolicy policy) => policy switch
    {
        LobbySeatPolicy.ImmediateSeat => "Immediate seat",
        LobbySeatPolicy.NextMatchSeat => "Next match seat",
        LobbySeatPolicy.ObserverUntilNextMatch => "Observer until next match",
        _ => policy.ToString()
    };
}
