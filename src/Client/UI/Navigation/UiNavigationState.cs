namespace MphRead.Mods.UI.Navigation;

public readonly record struct UiNavigationState(UiRoute Route, object? Parameter = null,
    string? FocusKey = null);

public readonly record struct UiModalState(string Id, object? Content = null,
    string? ReturnFocusKey = null);

public enum UiNavigationChangeKind
{
    Navigate,
    Replace,
    Back,
    ModalOpened,
    ModalClosed
}

public sealed class UiNavigationChangedEventArgs : System.EventArgs
{
    public UiNavigationChangedEventArgs(UiNavigationChangeKind kind, UiNavigationState state,
        UiModalState? modal, string? focusKey)
    {
        Kind = kind;
        State = state;
        Modal = modal;
        FocusKey = focusKey;
    }

    public UiNavigationChangeKind Kind { get; }
    public UiNavigationState State { get; }
    public UiModalState? Modal { get; }
    public string? FocusKey { get; }
}
