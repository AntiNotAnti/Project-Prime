using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using FruityPrime.Server.Shared;
using AvaloniaButton = Avalonia.Controls.Button;

namespace MphRead.Mods.Launcher.Gui;

/// <summary>Bounded authoritative lobby chat with a draft that belongs to the user.</summary>
internal sealed class LobbyChatPanel : Border
{
    private const int HistoryLimit = 32;
    private readonly TextBox _draft;
    private readonly Action<string> _setDraft;
    private readonly Action<string> _send;
    private bool _normalizing;

    public LobbyChatPanel(IReadOnlyList<LobbyChatEntry> history, string draft,
        Action<string> setDraft, Action<string> send)
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(setDraft);
        ArgumentNullException.ThrowIfNull(send);
        _setDraft = setDraft;
        _send = send;

        Padding = new Thickness(14);
        Classes.Add("prime-section-panel");
        var content = new StackPanel { Spacing = 8 };
        content.Children.Add(new TextBlock { Text = "LOBBY CHAT", Classes = { "prime-heading" } });

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
        content.Children.Add(new ScrollViewer
        {
            Content = lines,
            MaxHeight = 210,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        });

        _draft = new TextBox
        {
            Text = Utf8TextLimit.Truncate(draft, 256),
            Watermark = "Message…",
            MinHeight = 44,
            AcceptsReturn = false,
            MaxLength = 256,
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
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
        input.Children.Add(sendButton);
        Grid.SetColumn(sendButton, 1);
        content.Children.Add(input);
        content.Children.Add(new TextBlock { Text = "256 UTF-8 bytes maximum · Enter sends · Escape clears focus", Classes = { "prime-muted" } });
        Child = content;
    }

    public TextBox DraftEditor => _draft;

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
            _draft.Text = "";
            _draft.CaretIndex = 0;
        }
    }

    private void SendDraft() => _send(_draft.Text ?? "");
}
