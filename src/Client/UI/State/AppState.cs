using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MphRead.Mods.UI.State;

public sealed class AppState : INotifyPropertyChanged
{
    private string _playerName = "Guest";
    private bool _signedIn;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string PlayerName
    {
        get => _playerName;
        set => Set(ref _playerName, string.IsNullOrWhiteSpace(value) ? "Guest" : value.Trim());
    }

    public bool SignedIn
    {
        get => _signedIn;
        set => Set(ref _signedIn, value);
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
