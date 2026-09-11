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
    private readonly StackPanel _historyLines;
    private readonly PrimeStatusChip _unreadChip;
    private readonly StackPanel _content;
    private readonly TextBlock _usageHint;
    private readonly Action<string> _setDraft;
    private readonly Action<string> _send;
    private readonly Action<bool> _setEditing;
    private readonly Action<double> _setScrollOffset;
    private readonly Action _markRead;
    private double _initialScrollOffset;
    private bool _scrollPositionKnown;
    private Guid? _localSessionId;
    private bool _editing;
    private bool _normalizing;

    public LobbyChatPanel(IReadOnlyList<LobbyChatEntry> history, string draft,
        Action<string> setDraft, Action<string> send,
        Action<bool>? setEditing = null,
        double scrollOffset = 0,
        bool scrollPositionKnown = false,
        Action<double>? setScrollOffset = null,
        int unreadCount = 0,
        Action? markRead = null,
        Guid? localSessionId = null)
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
        _localSessionId = localSessionId;

        Padding = new Thickness(14);
        Classes.Add("prime-section-panel");
        PrimeAccessibility.SetName(this, "Lobby chat");
        PrimeAccessibility.SetDescription(this,
            "Recent lobby messages and a bounded message editor.");
        _content = new StackPanel { Spacing = 8 };
        StackPanel content = _content;
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        header.Children.Add(new TextBlock { Text = "LOBBY CHAT", Classes = { "prime-heading" } });
        _unreadChip = new PrimeStatusChip("") { IsVisible = false };
        header.Children.Add(_unreadChip);
        Grid.SetColumn(_unreadChip, 1);
        UpdateUnreadChip(unreadCount);
        content.Children.Add(header);

        _historyLines = new StackPanel { Spacing = 5 };
        RebuildHistory(history);
        _historyScroll = new ScrollViewer
        {
            Content = _historyLines,
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
        _usageHint = new TextBlock
        {
            Text = "256 UTF-8 bytes maximum · Enter sends · Escape exits editing",
            Classes = { "prime-muted" }
        };
        content.Children.Add(_usageHint);
        Child = content;
    }

    public TextBox DraftEditor => _draft;

    /// <summary>
    /// Update only the authoritative chat region of an existing lobby view.
    /// The editor, its focus, and the bounded local draft are intentionally
    /// retained while the message history is replaced from the latest
    /// snapshot.
    /// </summary>
    internal void Update(IReadOnlyList<LobbyChatEntry> history, string draft,
        double scrollOffset, bool scrollPositionKnown, int unreadCount,
        Guid? localSessionId)
    {
        ArgumentNullException.ThrowIfNull(history);
        bool preserveEditor = _editing || _draft.IsFocused;
        double currentOffset = _historyScroll.Offset.Y;
        _localSessionId = localSessionId;
        RebuildHistory(history);
        UpdateUnreadChip(unreadCount);

        _initialScrollOffset = scrollPositionKnown
            ? double.IsFinite(scrollOffset) ? Math.Max(0, scrollOffset) : 0
            : Math.Max(0, currentOffset);
        _scrollPositionKnown = scrollPositionKnown;

        if (!preserveEditor)
        {
            string bounded = Utf8TextLimit.Truncate(draft, 256);
            if (!StringComparer.Ordinal.Equals(_draft.Text, bounded))
            {
                _normalizing = true;
                _draft.Text = bounded;
                _draft.CaretIndex = bounded.Length;
                _normalizing = false;
            }
        }

        Avalonia.Threading.Dispatcher.UIThread.Post(RestoreHistoryScroll);
    }

    internal double HistoryMaxHeight
    {
        get => _historyScroll.MaxHeight;
        set => _historyScroll.MaxHeight = Math.Max(28, value);
    }

    internal bool CompactChrome
    {
        set
        {
            // Wide lobby columns have a fixed command rail beside chat. Keep
            // the editor's touch target intact while tightening only the
            // surrounding chrome enough to keep the whole panel in view.
            Padding = value ? new Thickness(8, 4) : new Thickness(14);
            Margin = value ? default : new Thickness(0, 0, 0, 12);
            _content.Spacing = value ? 4 : 8;
            _usageHint.IsVisible = !value;
        }
    }

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

    private void RebuildHistory(IReadOnlyList<LobbyChatEntry> history)
    {
        _historyLines.Children.Clear();
        foreach (LobbyChatEntry entry in history.TakeLast(HistoryLimit))
            _historyLines.Children.Add(CreateHistoryLine(entry));
        if (_historyLines.Children.Count == 0)
            _historyLines.Children.Add(new TextBlock
            {
                Text = "No messages yet.",
                Classes = { "prime-muted" }
            });
    }

    private Control CreateHistoryLine(LobbyChatEntry entry)
    {
        bool system = entry.SessionId == Guid.Empty;
        bool local = !system && _localSessionId == entry.SessionId;
        var line = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            ColumnSpacing = 6
        };
        var name = new TextBlock
        {
            Text = $"{entry.DisplayName}:",
            Classes = { system ? "prime-muted" : "prime-body" },
            Foreground = system ? GuiTheme.TextDimBrush
                : local ? GuiTheme.BrandBrush : GuiTheme.TechBrush
        };
        name.Classes.Add(system ? "prime-chat-system"
            : local ? "prime-chat-local-name" : "prime-chat-peer-name");
        var message = new TextBlock
        {
            Text = entry.Text,
            TextWrapping = TextWrapping.Wrap,
            Classes = { system ? "prime-muted" : "prime-body" }
        };
        if (system) message.Classes.Add("prime-chat-system");
        line.Children.Add(name);
        line.Children.Add(message);
        Grid.SetColumn(message, 1);
        return line;
    }

    private void UpdateUnreadChip(int unreadCount)
    {
        int bounded = Math.Clamp(unreadCount, 0, HistoryLimit);
        _unreadChip.IsVisible = bounded > 0;
        _unreadChip.Text = bounded > 0 ? $"{bounded} new" : "";
        if (bounded > 0)
            PrimeAccessibility.SetStatus(_unreadChip,
                $"{bounded} new lobby messages");
    }

    private void SendDraft() => _send(_draft.Text ?? "");
}
