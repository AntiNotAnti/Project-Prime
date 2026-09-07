using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MphRead.Mods.UI.State;

public enum SessionPhase
{
    Offline,
    Connecting,
    Lobby,
    Match,
    PostMatch,
    Reconnecting,
    Failed
}

public sealed class SessionViewState : INotifyPropertyChanged
{
    private SessionPhase _phase;
    private string _statusLabel = "Offline";
    private string? _detail;

    public event PropertyChangedEventHandler? PropertyChanged;

    public SessionPhase Phase
    {
        get => _phase;
        set => Set(ref _phase, value);
    }

    public string StatusLabel
    {
        get => _statusLabel;
        set => Set(ref _statusLabel, string.IsNullOrWhiteSpace(value) ? "Unknown" : value.Trim());
    }

    public string? Detail
    {
        get => _detail;
        set => Set(ref _detail, value);
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
