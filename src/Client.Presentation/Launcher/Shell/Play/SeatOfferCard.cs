using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using ProjectPrime.Server.Shared;
using MphRead.Mods.Launcher.Theme;
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
    private bool _expired;
    private int _lastCountdownSeconds = -1;

    public SeatOfferCard(LobbyQueueOffer offer, Action accept, Action decline)
    {
        ArgumentNullException.ThrowIfNull(offer);
        ArgumentNullException.ThrowIfNull(accept);
        ArgumentNullException.ThrowIfNull(decline);

        _expiresAt = offer.ExpiresAt;
        Padding = new Thickness(16);
        Classes.Add("prime-card");
        BorderBrush = GuiTheme.BrandBrush;
        BorderThickness = new Thickness(3, 1, 1, 1);
        PrimeAccessibility.SetName(this, "Player seat offer");
        PrimeAccessibility.SetDescription(this,
            "A player seat is available. Accept or decline it before it expires.");

        _countdown = new TextBlock { Classes = { "prime-title" } };
        PrimeAccessibility.SetName(_countdown, "Seat offer countdown");
        _accept = OfferButton("Accept", () => RunOnce(accept), primary: true);
        _decline = OfferButton("Decline", () => RunOnce(decline));
        PrimeAccessibility.SetName(_accept, "Accept player seat");
        PrimeAccessibility.SetDescription(_accept, "Accept this one-time player seat offer.");
        PrimeAccessibility.SetName(_decline, "Decline player seat offer");
        PrimeAccessibility.SetDescription(_decline, "Decline this one-time player seat offer.");

        var actions = new WrapPanel { Orientation = Orientation.Horizontal };
        actions.Children.Add(_accept);
        actions.Children.Add(_decline);
        Child = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = "PLAYER SLOT AVAILABLE", Classes = { "prime-kicker" } },
                new TextBlock { Text = "A player seat is ready for you. Choose once before the offer expires.",
                    Classes = { "prime-body" }, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                _countdown,
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
        button.MinWidth = text == "Accept" ? 120 : 100;
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
            if (!_expired)
            {
                _expired = true;
                _countdown.Text = FormatCountdown(0);
                PrimeAccessibility.SetStatus(_countdown, "Seat offer expired", PrimeStatusKind.Warning);
            }
            _accept.IsEnabled = false;
            _decline.IsEnabled = false;
            _timer.Stop();
            return;
        }

        int seconds = Math.Max(1, (int)Math.Ceiling(remaining.TotalSeconds));
        if (seconds == _lastCountdownSeconds) return;
        _lastCountdownSeconds = seconds;
        _countdown.Text = FormatCountdown(seconds);
        PrimeAccessibility.SetStatus(_countdown, _countdown.Text);
    }

    internal static string FormatCountdown(int seconds)
        => seconds <= 0
            ? "Offer expired"
            : $"Accept within {seconds / 60:00}:{seconds % 60:00}";
}
