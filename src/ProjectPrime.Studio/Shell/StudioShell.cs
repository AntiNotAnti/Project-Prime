using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ProjectPrime.Studio.Map;
using ProjectPrime.Studio.Replay;

namespace ProjectPrime.Studio.Shell;

public sealed class StudioShell : UserControl
{
    private readonly StudioDocumentHost _documents;
    private readonly StudioRecentDocuments _recent;
    private readonly StudioCommandRouter _commands;
    private readonly Func<StudioRecentDocument, Task> _openRecent;
    private readonly Func<IStudioDocument, Task> _close;
    private readonly Func<Task> _restore;
    private readonly bool _hasPreviousSession;
    private bool _refreshing;
    private readonly TabControl _tabs = new() { Name = "StudioDocumentTabs" };
    public StudioShell(StudioDocumentHost documents, StudioRecentDocuments recent, StudioCommandRouter commands,
        Func<StudioRecentDocument, Task> openRecent, Func<IStudioDocument, Task> close, Func<Task> restore, bool hasPreviousSession)
    {
        (_documents, _recent, _commands, _openRecent, _close, _restore, _hasPreviousSession) = (documents, recent, commands, openRecent, close, restore, hasPreviousSession);
        Refresh();
    }
    public void Refresh()
    {
        _refreshing = true;
        try
        {
            if (_documents.ActiveDocument is null) { Content = CreateHome(); return; }
            foreach (TabItem tab in _tabs.Items.OfType<TabItem>().ToArray())
                if (tab.Tag is IStudioDocument existing && !_documents.Documents.Contains(existing)) { tab.Content=null; _tabs.Items.Remove(tab); }
            foreach (IStudioDocument document in _documents.Documents)
            {
                if (_tabs.Items.OfType<TabItem>().FirstOrDefault(item => ReferenceEquals(item.Tag,document)) is { } existing)
                {
                    Control? current=document is MapStudioDocument mapDocument ? mapDocument.Host : document is ReplayStudioDocument replayDocument ? replayDocument.Host : null;
                    if(current is not null && !ReferenceEquals(existing.Content,current)) {existing.Content=null;existing.Content=current;}
                    if (existing.Header is StackPanel heading && heading.Children[0] is TextBlock title) title.Text=document.Title + (document.Dirty ? " •" : "");
                    if (ReferenceEquals(document,_documents.ActiveDocument)) _tabs.SelectedItem=existing;
                    continue;
                }
                StackPanel header = new() { Orientation = Orientation.Horizontal, Spacing = 10 };
                header.Children.Add(new TextBlock { Text = document.Title + (document.Dirty ? " •" : ""), FontSize = 13, VerticalAlignment = VerticalAlignment.Center });
                Button close = new() { Content = "×", Padding = new Thickness(4, 0), MinHeight = 22, Height = 22 };
                ToolTip.SetTip(close, "Close document");
                close.Click += async (_, _) => await _close(document);
                header.Children.Add(close);
                TabItem tab = new() { Header = header, Padding = new Thickness(8, 4), Tag = document, Content = document is MapStudioDocument map ? map.Host : document is ReplayStudioDocument replay ? replay.Host : document.Kind == StudioDocumentKind.Map ? new MapStudioWorkspace(document) : new ReplayStudioWorkspace(document) };
                _tabs.Items.Add(tab);
                if (ReferenceEquals(document, _documents.ActiveDocument)) _tabs.SelectedItem = tab;
            }
            _tabs.SelectionChanged -= SelectTab;
            _tabs.SelectionChanged += SelectTab;
            Content = _tabs;
        }
        finally { _refreshing = false; }
    }
    private void SelectTab(object? sender, SelectionChangedEventArgs e)
    {
        if (!_refreshing && _tabs.SelectedItem is TabItem { Tag: IStudioDocument document } && !ReferenceEquals(document,_documents.ActiveDocument)) _documents.Select(document);
    }
    private Control CreateHome()
    {
        StackPanel body = new() { MaxWidth = 850, Margin = new Thickness(35), Spacing = 22 };
        body.Children.Add(new TextBlock { Text = "PROJECT PRIME", FontSize = 12, Foreground = new SolidColorBrush(Color.Parse("#79C9EF")), FontWeight = FontWeight.Bold });
        body.Children.Add(new TextBlock { Name = "StudioHomeHeading", Text = "Studio", FontSize = 40, FontWeight = FontWeight.SemiBold });
        body.Children.Add(new TextBlock { Text = "A dedicated desktop home for maps and replays.", FontSize = 18, TextWrapping = TextWrapping.Wrap });
        body.Children.Add(new TextBlock { Text = "Create and edit map projects with the shared creator tools. Open replay files in their dedicated workspace.", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightGray, LineHeight = 22 });
        StackPanel actions = new() { Orientation = Orientation.Horizontal, Spacing = 12 };
        actions.Children.Add(Action("New map", StudioCommand.NewMapWorkspace));
        actions.Children.Add(Action("Replay workspace", StudioCommand.NewReplayWorkspace));
        body.Children.Add(actions);
        StackPanel files = new() { Orientation = Orientation.Horizontal, Spacing = 12 };
        files.Children.Add(Action("Open map…", StudioCommand.OpenMap));
        files.Children.Add(Action("Open replay…", StudioCommand.OpenReplay));
        files.Children.Add(Action("Open clip…", StudioCommand.OpenClip));
        body.Children.Add(files);
        if (_hasPreviousSession)
        {
            Button restore = new() { Content = "Restore previous session", HorizontalAlignment = HorizontalAlignment.Left };
            restore.Click += async (_, _) => await _restore();
            body.Children.Add(restore);
        }
        body.Children.Add(new TextBlock { Text = "Recent documents", FontSize = 18, FontWeight = FontWeight.SemiBold });
        if (_recent.Items.Count == 0) body.Children.Add(new TextBlock { Text = "Opened source files will appear here.", Foreground = Brushes.LightGray });
        foreach (StudioRecentDocument item in _recent.Items.Take(8))
        {
            StackPanel label = new() { Spacing = 4 };
            label.Children.Add(new TextBlock { Text = $"{item.Kind.ToString().ToUpperInvariant()}   {System.IO.Path.GetFileName(item.Path)}", FontWeight = FontWeight.SemiBold });
            label.Children.Add(new TextBlock { Text = item.Path, FontSize = 11, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightGray });
            Button button = new() { Content = label, HorizontalContentAlignment = HorizontalAlignment.Left, HorizontalAlignment = HorizontalAlignment.Stretch };
            button.Click += async (_, _) => await _openRecent(item);
            body.Children.Add(button);
        }
        return new ScrollViewer { Content = body, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
    }
    private Button Action(string label, StudioCommand command) => new() { Content = label, Command = _commands[command] };
}
