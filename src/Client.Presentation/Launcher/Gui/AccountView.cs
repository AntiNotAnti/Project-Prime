using System;
using System.Linq;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using MphRead.Formats;
using MphRead.Identity;
using MphRead.Mods.Accounts;
using MphRead.Mods.Launcher.Presentation;
using MphRead.Mods.Launcher.Theme;

namespace MphRead.Mods.Launcher.Gui;

internal sealed class AccountView : UserControl
{
    private readonly CancellationTokenSource _cancel = new();
    private readonly StackPanel _actions = new() { Spacing = 8 };
    private readonly TextBox _backend = new() { Watermark = "https://accounts.example.com" };
    private readonly TextBox _email = new() { Watermark = "Email" };
    private readonly TextBox _password = new() { Watermark = "Password", PasswordChar = '●' };
    private readonly TextBox _name = new() { Watermark = "Display name (1–16 characters)" };
    private readonly TextBox _playerId = new() { Watermark = "Player ID from registration" };
    private readonly TextBox _code = new() { Watermark = "Email confirmation code" };
    private readonly ComboBox _hunter = new() { ItemsSource = PlayableHunterCatalog.All.Select(hunter => hunter.ToString()).ToArray(), SelectedIndex = 0 };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Foreground = GuiTheme.TextDimBrush };
    private readonly TextBlock _validation = new()
    {
        TextWrapping = TextWrapping.Wrap,
        Foreground = GuiTheme.ErrorBrush,
        IsVisible = false
    };
    private readonly TextBlock _license = new() { TextWrapping = TextWrapping.Wrap, Foreground = GuiTheme.TextBrush };
    private readonly TextBlock _career = new() { TextWrapping = TextWrapping.Wrap, Foreground = GuiTheme.TextBrush };
    private readonly TextBlock _history = new() { TextWrapping = TextWrapping.Wrap, Foreground = GuiTheme.TextBrush };
    private readonly TextBlock _board = new() { TextWrapping = TextWrapping.Wrap, Foreground = GuiTheme.TextBrush };
    private static readonly string[] BoardMetrics = { "kills", "wins", "kd", "rp", "winPercentage", "headshots", "octolithScores", "nodesCaptured", "killsAsPrime" };
    private readonly ComboBox _boardMetric = new() { ItemsSource = new[] { "Kills", "Wins", "K/D", "Ranking Points", "Win percentage", "Headshots", "Octolith scores", "Nodes captured", "Kills as Prime" }, SelectedIndex = 0 };
    private readonly ComboBox _boardHunter = new() { ItemsSource = new[] { "All Hunters" }.Concat(PlayableHunterCatalog.All.Select(hunter => hunter.ToString())).ToArray(), SelectedIndex = 0 };
    private long? _historyCursor;
    private string? _boardCursor;
    public event EventHandler? Closed;

    public AccountView()
    {
        _backend.Text = LauncherPrefs.BackendAddress;
        _name.Text = LauncherPrefs.PlayerName;
        Background = GuiTheme.PanelBrush;
        var stack = new StackPanel { Spacing = 12, Margin = new Thickness(20), MaxWidth = 600, HorizontalAlignment = HorizontalAlignment.Stretch };
        stack.Children.Add(PrimeControlFactory.PageHeading("Hunter license", "ACCOUNT",
            "Keep your profile and official results with you across devices."));
        stack.Children.Add(new TextBlock { Text = "Sign in to your account service to keep your Hunter license across devices. Guest play remains available.", TextWrapping = TextWrapping.Wrap, Foreground = GuiTheme.TextDimBrush });
        var advanced = new StackPanel { Spacing = 8 };
        AddField(advanced, "Account service", _backend);
        stack.Children.Add(new Expander
        {
            Header = "Advanced",
            IsExpanded = false,
            Content = advanced
        });
        AddField(_actions, "Email", _email);
        AddField(_actions, "Password", _password);
        var passwordToggle = new MenuEntry("Show password", titleSize: 13);
        passwordToggle.Click += (_, _) =>
        {
            _password.RevealPassword = !_password.RevealPassword;
            passwordToggle.Title = _password.RevealPassword
                ? "Hide password" : "Show password";
        };
        _actions.Children.Add(passwordToggle);
        AddField(_actions, "Display name", _name);
        MenuEntry signIn = Action("Sign in", async session =>
        {
            ResetQueryState();
            string password = _password.Text ?? "";
            _password.Text = "";
            await session.SignInAsync(_email.Text?.Trim() ?? "", password, _cancel.Token);
            _playerId.Text = session.Identity!.PlayerId.ToString();
            await RefreshLicense(session);
            _status.Text = session.Identity.EmailEligibleForOfficialPlay ? "Signed in." : "Signed in. Confirm your email before official play.";
        });
        signIn.Primary = true;
        _actions.Children.Add(signIn);
        _actions.Children.Add(Action("Continue as guest", async session =>
        {
            // Explicit guest selection must clear a previously restored
            // account identity so Node admission uses the anonymous route.
            await session.SignOutAsync(_cancel.Token);
            _status.Text = "Online service configured. Guest play is ready from Host or Join.";
        }));
        _actions.Children.Add(Action("Create account", async session =>
        {
            string password = _password.Text ?? "";
            _password.Text = "";
            AccountRegistration result = await session.RegisterAsync(_email.Text?.Trim() ?? "", password, _name.Text?.Trim() ?? "", _cancel.Token);
            _playerId.Text = result.PlayerId.ToString();
            _status.Text = result.ConfirmationRequired ? "Account created. Enter the code sent to your email, then sign in." : "Account created. You can sign in now.";
        }));
        AddField(_actions, "Player ID", _playerId);
        AddField(_actions, "Confirmation code", _code);
        _actions.Children.Add(Action("Confirm email", async session =>
        {
            await session.ConfirmEmailAsync(PlayerId.Parse(_playerId.Text ?? ""), _code.Text?.Trim() ?? "", _cancel.Token);
            _code.Text = "";
            _status.Text = "Email confirmed. Sign in to refresh your eligibility.";
        }));
        _actions.Children.Add(Action("Resend confirmation", async session =>
        {
            await session.ResendConfirmationAsync(_email.Text?.Trim() ?? "", _cancel.Token);
            _status.Text = "If confirmation is needed, a new code will be sent.";
        }));
        AddField(_actions, "Favorite hunter", _hunter);
        _actions.Children.Add(Action("Save profile", async session =>
        {
            await session.UpdateProfileAsync(_name.Text?.Trim() ?? "", _hunter.SelectedIndex, _cancel.Token);
            await RefreshLicense(session);
            _status.Text = "Profile saved.";
        }));
        _actions.Children.Add(Action("Refresh hunter license", RefreshLicense));
        _actions.Children.Add(_career);
        _actions.Children.Add(Action("Recent matches", session => RefreshHistory(session, older: false)));
        _actions.Children.Add(Action("Older matches", session => RefreshHistory(session, older: true)));
        _actions.Children.Add(_history);
        AddField(_actions, "Official leaderboard", _boardMetric);
        AddField(_actions, "Hunter filter", _boardHunter);
        _boardMetric.SelectionChanged += (_, _) => { _boardCursor = null; _board.Text = ""; };
        _boardHunter.SelectionChanged += (_, _) => { _boardCursor = null; _board.Text = ""; };
        _actions.Children.Add(Action("Show leaderboard", session => RefreshBoard(session, next: false)));
        _actions.Children.Add(Action("Next leaderboard page", session => RefreshBoard(session, next: true)));
        _actions.Children.Add(_board);
        _actions.Children.Add(Action("Sign out", async session =>
        {
            await session.SignOutAsync(_cancel.Token);
            _password.Text = "";
            _playerId.Text = "";
            _license.Text = "";
            _career.Text = "";
            ResetQueryState();
            _status.Text = "Signed out. Account credentials were cleared from memory.";
        }));
        stack.Children.Add(_actions);
        stack.Children.Add(_validation);
        stack.Children.Add(_license);
        stack.Children.Add(_status);
        var close = new MenuEntry("Back", titleSize: 15);
        close.Click += (_, _) => { _cancel.Cancel(); _password.Text = ""; Closed?.Invoke(this, EventArgs.Empty); };
        stack.Children.Add(close);
        PrimeTechFrame frame = PrimeControlFactory.TechFrame(stack);
        frame.Classes.Add("prime-account-frame");
        PrimeAccessibility.SetName(frame, "Hunter license account");
        PrimeAccessibility.SetDescription(frame,
            "Sign in, manage your Hunter profile, and review official results.");
        Content = new ScrollViewer { Content = frame };
        DetachedFromVisualTree += (_, _) => { _cancel.Cancel(); _password.Text = ""; };
    }

    private static void AddField(StackPanel parent, string label, Control control)
    {
        parent.Children.Add(new TextBlock { Text = label, Foreground = GuiTheme.TextDimBrush });
        parent.Children.Add(control);
    }

    private void ResetQueryState()
    {
        _historyCursor = null;
        _boardCursor = null;
        _history.Text = "";
        _board.Text = "";
    }

    private MenuEntry Action(string title, Func<AccountSession, Task> action)
    {
        var entry = new MenuEntry(title, titleSize: 15);
        entry.Click += async (_, _) =>
        {
            if (title is "Sign in" or "Create account"
                && !ValidateCredentials(title == "Create account"))
                return;
            _actions.IsEnabled = false;
            _status.Text = "Working…";
            try
            {
                if (!Uri.TryCreate(_backend.Text?.Trim(), UriKind.Absolute, out Uri? backend) || !AccountSession.IsAllowedBackend(backend))
                    throw new ArgumentException("Enter an HTTPS account service URL. Local testing may use HTTP on loopback.");
                AccountSession session = AccountSessions.Configure(backend);
                LauncherPrefs.BackendAddress = session.Backend.AbsoluteUri;
                LauncherPrefs.Save();
                await action(session);
            }
            catch (OperationCanceledException) { _status.Text = "Cancelled."; }
            catch (Exception error)
            {
                _status.Text = PrimeRoutePresentation.PlayerFacingNetworkError(error.Message,
                    "The online service could not complete that request. Try again.");
            }
            finally { _actions.IsEnabled = true; }
        };
        return entry;
    }

    private bool ValidateCredentials(bool registration)
    {
        string email = _email.Text?.Trim() ?? "";
        int separator = email.IndexOf('@');
        if (separator <= 0 || separator == email.Length - 1)
            return ShowValidation("Enter a valid email address.");
        if (String.IsNullOrWhiteSpace(_password.Text))
            return ShowValidation("Enter your password.");
        if (registration)
        {
            string name = _name.Text?.Trim() ?? "";
            if (name.Length is < 1 or > 16)
                return ShowValidation("Use 1–16 characters for your display name.");
        }
        _validation.IsVisible = false;
        return true;
    }

    private bool ShowValidation(string message)
    {
        _validation.Text = message;
        _validation.IsVisible = true;
        PrimeAccessibility.SetStatus(_validation, message, PrimeStatusKind.Error);
        return false;
    }

    private async Task RefreshLicense(AccountSession session)
    {
        PlayerId id = session.Identity?.PlayerId ?? throw new InvalidOperationException("Sign in to view your Hunter License.");
        HunterLicense license = await session.GetLicenseAsync(id, _cancel.Token);
        _name.Text = license.DisplayName;
        _hunter.SelectedIndex = license.FavoriteHunter is int favorite
            && favorite >= 0 && favorite < PlayableHunterCatalog.Count
            ? favorite : 0;
        _license.Text = $"{license.DisplayName}\nPlayer ID: {license.PlayerId}\nJoined {license.JoinedAt:yyyy-MM-dd}";
        CareerSummary career = await session.GetCareerAsync(id, _cancel.Token);
        CareerTotals totals = career.Totals;
        long losses = totals.Losses ?? Math.Max(0, totals.Matches - totals.Wins - totals.Ties);
        string kd = totals.KillDeathRatio is { } ratio ? ratio.ToString("0.00", CultureInfo.InvariantCulture)
            : totals.Deaths == 0 ? "—" : ((double)totals.Kills / totals.Deaths).ToString("0.00", CultureInfo.InvariantCulture);
        string winRate = totals.WinRatio is { } winRatio ? winRatio.ToString("P1", CultureInfo.InvariantCulture)
            : totals.Matches == 0 ? "—" : ((double)totals.Wins / totals.Matches).ToString("P1", CultureInfo.InvariantCulture);
        _career.Text = $"Official career\n{totals.Matches} matches · {totals.Wins} wins · {losses} losses · {totals.Ties} ties\n"
            + $"Win rate {winRate} · K/D {kd}\nKills {totals.Kills} · Deaths {totals.Deaths} · Assists {totals.Assists}\n"
            + $"Damage {totals.Damage} · Headshot kills {Known(totals.HeadshotKills)}\nBiped kills {Known(totals.BipedKills)} · Alt-form kills {Known(totals.AltFormKills)}\n"
            + $"Longest kill streak {Known(totals.LongestKillStreak)} · Longest win streak {Known(totals.LongestWinStreak)}\n"
            + $"Played {totals.PlayedTicks / 3600.0:0.0} minutes\nMost-played Hunter: {Choice(career.MostPlayedHunter, "hunter")}\n"
            + $"Favorite map: {Choice(career.FavoriteMap, "map")} · Favorite mode: {Choice(career.FavoriteMode, "mode")}\nFavorite weapon: {Choice(career.FavoriteWeapon, "weapon")}\n"
            + $"Best map: {Choice(career.BestMap, "map")} · Best Hunter: {Choice(career.BestHunter, "hunter")} (minimum {career.BestMinimumMatches} matches)\n"
            + (career.RatingStatus == "policyPending" ? "Ranking Points are not available yet." : "");
        _status.Text = "Hunter License updated.";
    }

    private static string Known(long? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "—";
    private static string Choice(CareerChoice? value, string dimension = "map")
        => PrimeGameText.CareerChoiceLabel(value, dimension);

    private async Task RefreshHistory(AccountSession session, bool older)
    {
        PlayerId id = session.Identity?.PlayerId ?? throw new InvalidOperationException("Sign in to view your match history.");
        if (older && _historyCursor == null) { _status.Text = "No older page is available. Choose Recent matches to start."; return; }
        MatchHistoryPage page = await session.GetHistoryAsync(id, older ? _historyCursor : null, _cancel.Token);
        _historyCursor = page.NextCursor;
        _history.Text = page.Entries.Length == 0 ? "No recorded matches." : string.Join("\n\n", page.Entries.Select(match =>
            $"{match.EndedAt:yyyy-MM-dd HH:mm} UTC · {match.RoomKey} · {match.Mode}\n"
            + $"{(match.Won ? "Win" : match.Tied ? "Tie" : "Loss")} · {match.Kills}/{match.Deaths}/{match.Assists} K/D/A · {match.Damage} damage\n"
            + $"{match.TrustClass} · {match.Outcome}"));
        _status.Text = _historyCursor.HasValue ? "Match history loaded. Older matches are available." : "Match history loaded.";
    }

    private async Task RefreshBoard(AccountSession session, bool next)
    {
        if (next && _boardCursor == null) { _status.Text = "No next page is available. Choose Show leaderboard to start."; return; }
        string metric = BoardMetrics[Math.Clamp(_boardMetric.SelectedIndex, 0, BoardMetrics.Length - 1)];
        Hunter? hunter = _boardHunter.SelectedIndex > 0 ? (Hunter)(_boardHunter.SelectedIndex - 1) : null;
        LeaderboardPage page = await session.GetLeaderboardAsync(metric, next ? _boardCursor : null, _cancel.Token, hunter);
        _boardCursor = page.NextCursor;
        _board.Text = page.RatingStatus == "policyPending" ? "Ranking Points are not available yet."
            : page.Entries.Length == 0 ? "No eligible players on this board."
            : string.Join("\n", page.Entries.Select(player => $"{player.DisplayName} · {player.Score.ToString(metric == "winPercentage" ? "P1" : "0.##", CultureInfo.InvariantCulture)} · {player.Matches} matches"));
        _status.Text = metric == "kd" ? "K/D board requires at least 10 matches and one death."
            : metric == "winPercentage" ? "Win percentage requires at least 10 matches." : "Official leaderboard loaded.";
    }
}
