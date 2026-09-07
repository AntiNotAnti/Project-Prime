using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MphRead.Mods.UI.State;

public enum UiLoadState
{
    Idle,
    Loading,
    Ready,
    Empty,
    Offline,
    Failed
}

public sealed class AsyncScreenState : INotifyPropertyChanged
{
    private UiLoadState _state;
    private string _message = string.Empty;
    private bool _canRetry;

    public event PropertyChangedEventHandler? PropertyChanged;

    public UiLoadState State { get => _state; private set => Set(ref _state, value); }
    public string Message { get => _message; private set => Set(ref _message, value); }
    public bool CanRetry { get => _canRetry; private set => Set(ref _canRetry, value); }

    public void Loading(string message = "Loading…") => Set(UiLoadState.Loading, message, false);
    public void Ready() => Set(UiLoadState.Ready, string.Empty, false);
    public void Empty(string message) => Set(UiLoadState.Empty, message, true);
    public void Offline(string message = "This service is offline.") => Set(UiLoadState.Offline, message, true);
    public void Failed(string message) => Set(UiLoadState.Failed, message, true);

    public static string FriendlyFailure(Exception error, string operation) => error switch
    {
        OperationCanceledException => $"{operation} was canceled.",
        System.Net.Http.HttpRequestException => $"{operation} is unavailable. Check your connection and try again.",
        TimeoutException => $"{operation} took too long. Try again.",
        _ => $"{operation} could not be completed. Try again."
    };

    private void Set(UiLoadState state, string message, bool canRetry)
    {
        State = state;
        Message = message;
        CanRetry = canRetry;
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
