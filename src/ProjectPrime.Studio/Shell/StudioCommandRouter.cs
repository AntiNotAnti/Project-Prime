using System.Windows.Input;

namespace ProjectPrime.Studio.Shell;

public enum StudioCommand { OpenMap, OpenReplay, OpenClip, NewMapWorkspace, NewReplayWorkspace, Save, SaveAs, Close, Undo, Redo, FrameAll, ToggleWireframe, ReplayPlayPause, ReplayMarkIn, MapValidate, MapBuild, RestoreLayout, ClearRecent, GlobalSearch, ConfigureHotkeys, MapPlaytest }
public sealed record StudioCommandContext(IStudioDocument? Document);

public sealed class StudioCommandRouter
{
    private readonly Dictionary<StudioCommand, RoutedCommand> _commands = [];
    private readonly Func<StudioCommandContext> _context;
    public StudioCommandRouter(Func<StudioCommandContext> context) => _context = context;
    public event Action<Exception>? Failed;
    public ICommand this[StudioCommand command] => _commands[command];
    public IReadOnlyList<StudioCommand> AvailableCommands => _commands.Keys.ToArray();
    public void Register(StudioCommand command, Func<StudioCommandContext, Task> execute, Func<StudioCommandContext, bool>? canExecute = null)
        => _commands[command] = new(execute, canExecute ?? (_ => true), _context, ex => Failed?.Invoke(ex));
    public void Refresh() { foreach (RoutedCommand command in _commands.Values) command.Refresh(); }
    public Task ExecuteAsync(StudioCommand command) => _commands[command].RunAsync();
    private sealed class RoutedCommand(Func<StudioCommandContext, Task> execute, Func<StudioCommandContext, bool> enabled,
        Func<StudioCommandContext> context, Action<Exception> failed) : ICommand
    {
        private bool _running;
        public event EventHandler? CanExecuteChanged;
        public bool CanExecute(object? parameter) => !_running && enabled(context());
        public async void Execute(object? parameter) { try { await RunAsync(); } catch (Exception ex) { failed(ex); } }
        public async Task RunAsync()
        {
            if (!CanExecute(null)) return;
            _running = true;
            Refresh();
            try { await execute(context()); }
            finally { _running = false; Refresh(); }
        }
        public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
