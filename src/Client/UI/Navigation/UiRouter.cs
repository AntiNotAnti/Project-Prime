using System;
using System.Collections.Generic;

namespace MphRead.Mods.UI.Navigation;

/// <summary>Single owner for route history, modal history, and focus restoration.</summary>
public sealed class UiRouter
{
    private readonly List<UiNavigationState> _backStack = [];
    private readonly List<UiModalState> _modalStack = [];
    private readonly Dictionary<UiRoute, string> _routeFocus = [];
    private UiNavigationState _current;

    public UiRouter(UiRoute initialRoute = UiRoute.Home)
    {
        _current = new UiNavigationState(initialRoute);
    }

    public event EventHandler<UiNavigationChangedEventArgs>? Changed;

    public UiNavigationState Current => _current;
    public UiRoute CurrentRoute => _current.Route;
    public int BackCount => _backStack.Count;
    public int ModalCount => _modalStack.Count;
    public UiModalState? CurrentModal => _modalStack.Count == 0 ? null : _modalStack[^1];
    public bool CanGoBack => _modalStack.Count > 0 || _backStack.Count > 0;

    public void Navigate(UiRoute route, object? parameter = null, string? sourceFocusKey = null)
    {
        RememberFocus(_current.Route, sourceFocusKey);
        if (_current.Route == route && Equals(_current.Parameter, parameter))
        {
            return;
        }

        _backStack.Add(_current with { FocusKey = sourceFocusKey ?? _current.FocusKey });
        _current = new UiNavigationState(route, parameter, FocusFor(route));
        Raise(UiNavigationChangeKind.Navigate, _current.FocusKey);
    }

    public void Replace(UiRoute route, object? parameter = null)
    {
        _current = new UiNavigationState(route, parameter, FocusFor(route));
        Raise(UiNavigationChangeKind.Replace, _current.FocusKey);
    }

    public bool GoBack()
    {
        if (_modalStack.Count > 0)
        {
            return CloseModal();
        }
        if (_backStack.Count == 0)
        {
            return false;
        }

        UiNavigationState destination = _backStack[^1];
        _backStack.RemoveAt(_backStack.Count - 1);
        _current = destination;
        Raise(UiNavigationChangeKind.Back, destination.FocusKey ?? FocusFor(destination.Route));
        return true;
    }

    public void OpenModal(string id, object? content = null, string? returnFocusKey = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        var modal = new UiModalState(id, content, returnFocusKey);
        _modalStack.Add(modal);
        Raise(UiNavigationChangeKind.ModalOpened, null);
    }

    public bool CloseModal()
    {
        if (_modalStack.Count == 0)
        {
            return false;
        }
        UiModalState modal = _modalStack[^1];
        _modalStack.RemoveAt(_modalStack.Count - 1);
        Raise(UiNavigationChangeKind.ModalClosed, modal.ReturnFocusKey ?? FocusFor(_current.Route));
        return true;
    }

    public void RememberFocus(UiRoute route, string? focusKey)
    {
        if (!string.IsNullOrWhiteSpace(focusKey))
        {
            _routeFocus[route] = focusKey;
        }
    }

    public string? FocusFor(UiRoute route)
        => _routeFocus.TryGetValue(route, out string? key) ? key : null;

    public void Reset(UiRoute route = UiRoute.Home)
    {
        _backStack.Clear();
        _modalStack.Clear();
        _current = new UiNavigationState(route, null, FocusFor(route));
        Raise(UiNavigationChangeKind.Replace, _current.FocusKey);
    }

    private void Raise(UiNavigationChangeKind kind, string? focusKey)
        => Changed?.Invoke(this, new UiNavigationChangedEventArgs(kind, _current,
            CurrentModal, focusKey));
}
