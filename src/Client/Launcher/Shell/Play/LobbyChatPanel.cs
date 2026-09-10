using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using ProjectPrime.Server.Shared;
using MphRead.Mods.Launcher.Theme;
using AvaloniaButton = Avalonia.Controls.Button;

namespace MphRead.Mods.Launcher.Gui;

/// <summary>Bounded authoritative lobby chat with a draft that belongs to the user.</summary>
internal sealed class LobbyChatPanel : Border
{
    private const int HistoryLimit = PlayPresentationState.ChatHistoryLimit;
    private readonly TextBox _draft;
    private readonly ScrollViewer _historyScroll;
    private readonly Action<string> _setDraft;
    private readonly Action<string> _send;
    private readonly Action<bool> _setEditing;
    private readonly Action<double> _setScrollOffset;
    private readonly Action _markRead;
    private readonly double _initialScrollOffset;
    private readonly bool _scrollPositionKnown;
    private bool _editing;
    private bool _normalizing;

    public LobbyChatPanel(IReadOnlyList<LobbyChatEntry> history, string draft,
        Action<string> setDraft, Action<string> send,
        Action<bool>? setEditing = null,
        double scrollOffset = 0,
        bool scrollPositionKnown = false,
        Action<double>? setScrollOffset = null,
        int unreadCount = 0,
        Action? markRead = null)
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(setDraft);
        ArgumentNullException.ThrowIfNull(send);
        _setDraft = setDraft;
        _send = send;
        _setEditing = setEditing ?? (_ => { });
        _setScrollOffset = setScrollOffset ?? (_ => { });
        _markRead = markRead ?? (() => { });
        _initialScrollOffset = double.IsFinite(scrollOffset) ? Math.Max(0, scrollOffset) : 0;
        _scrollPositionKnown = scrollPositionKnown;

        Padding = new Thickness(14);
        Classes.Add("prime-section-panel");
        PrimeAccessibility.SetName(this, "Lobby chat");
        PrimeAccessibility.SetDescription(this,
            "Recent lobby messages and a bounded message editor.");
        var content = new StackPanel { Spacing = 8 };
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        header.Children.Add(new TextBlock { Text = "LOBBY CHAT", Classes = { "prime-heading" } });
        if (unreadCount > 0)
        {
            int boundedUnread = Math.Clamp(unreadCount, 1, HistoryLimit);
            var unread = new PrimeStatusChip($"{boundedUnread} new", GuiTheme.AccentBrush);
            PrimeAccessibility.SetStatus(unread, $"{boundedUnread} new lobby messages");
            header.Children.Add(unread);
            Grid.SetColumn(unread, 1);
        }
        content.Children.Add(header);

        var lines = new StackPanel { Spacing = 5 };
        IEnumerable<LobbyChatEntry> visible = history.TakeLast(HistoryLimit);
        foreach (LobbyChatEntry entry in visible)
        {
            var line = new TextBlock
            {
                Text = $"{entry.DisplayName}: {entry.Text}",
                TextWrapping = TextWrapping.Wrap,
                Classes = { "prime-body" }
            };
            lines.Children.Add(line);
        }
        if (lines.Children.Count == 0)
            lines.Children.Add(new TextBlock { Text = "No messages yet.", Classes = { "prime-muted" } });
        _historyScroll = new ScrollViewer
        {
            Content = lines,
            MaxHeight = 210,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        _historyScroll.ScrollChanged += (_, _) =>
        {
            _setScrollOffset(_historyScroll.Offset.Y);
            if (IsAtEnd()) _markRead();
        };
        _historyScroll.AttachedToVisualTree += (_, _) =>
            Avalonia.Threading.Dispatcher.UIThread.Post(RestoreHistoryScroll);
        content.Children.Add(_historyScroll);

        _draft = new TextBox
        {
            Text = Utf8TextLimit.Truncate(draft, 256),
            Watermark = "Message…",
            MinHeight = 44,
            AcceptsReturn = false,
            MaxLength = 256,
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        PrimeAccessibility.SetName(_draft, "Lobby message");
        PrimeAccessibility.SetDescription(_draft,
            "Message, limited to 256 UTF-8 bytes. Enter sends it.");
        _draft.GotFocus += (_, _) => BeginEditing();
        _draft.LostFocus += (_, _) => EndEditing();
        _draft.TextChanged += (_, _) =>
        {
            if (_normalizing) return;
            string value = _draft.Text ?? "";
            string bounded = Utf8TextLimit.Truncate(value, 256);
            if (!StringComparer.Ordinal.Equals(value, bounded))
            {
                _normalizing = true;
                _draft.Text = bounded;
                _draft.CaretIndex = bounded.Length;
                _normalizing = false;
            }
            _setDraft(bounded);
        };
        _draft.KeyDown += DraftKeyDown;

        var input = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
        input.Children.Add(_draft);
        AvaloniaButton sendButton = PrimeControlFactory.Button("Send", SendDraft, primary: true);
        sendButton.MinHeight = 44;
        sendButton.MinWidth = 90;
        PrimeAccessibility.SetName(sendButton, "Send lobby message");
        PrimeAccessibility.SetDescription(sendButton, "Send the drafted lobby message.");
        input.Children.Add(sendButton);
        Grid.SetColumn(sendButton, 1);
        content.Children.Add(input);
        content.Children.Add(new TextBlock
        {
            Text = "256 UTF-8 bytes maximum · Enter sends · Escape exits editing",
            Classes = { "prime-muted" }
        });
        Child = content;
    }

    public TextBox DraftEditor => _draft;

    /// <summary>Exit text editing without discarding a typed draft.</summary>
    public bool TryExitEditing()
    {
        if (!_editing && !_draft.IsFocused) return false;
        EndEditing();
        TopLevel.GetTopLevel(_draft)?.FocusManager?.ClearFocus();
        return true;
    }

    private void DraftKeyDown(object? sender, KeyEventArgs args)
    {
        if (args.Key == Key.Enter)
        {
            args.Handled = true;
            SendDraft();
        }
        else if (args.Key == Key.Escape)
        {
            args.Handled = true;
            TryExitEditing();
        }
    }

    private void BeginEditing()
    {
        if (_editing) return;
        _editing = true;
        _setEditing(true);
        _markRead();
    }

    private void EndEditing()
    {
        if (!_editing) return;
        _editing = false;
        _setEditing(false);
    }

    private bool IsAtEnd()
        => _historyScroll.Extent.Height <= _historyScroll.Viewport.Height + 1
            || _historyScroll.Offset.Y >= _historyScroll.Extent.Height
                - _historyScroll.Viewport.Height - 2;

    private void RestoreHistoryScroll()
    {
        if (_scrollPositionKnown)
        {
            double maximum = Math.Max(0, _historyScroll.Extent.Height - _historyScroll.Viewport.Height);
            _historyScroll.Offset = new Vector(0, Math.Clamp(_initialScrollOffset, 0, maximum));
        }
        else
        {
            _historyScroll.ScrollToEnd();
        }
        if (IsAtEnd()) _markRead();
    }

    private void SendDraft() => _send(_draft.Text ?? "");
}
