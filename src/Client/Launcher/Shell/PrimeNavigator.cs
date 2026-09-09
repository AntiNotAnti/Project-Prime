using System;
using System.Collections.Generic;

namespace MphRead.Mods.Launcher.Gui;

public enum PrimeNavigationChangeKind
{
    Push,
    RootReplace,
    Back,
    Reset
}
public sealed record PrimeNavigationChangedEventArgs(PrimeNavigationChangeKind Kind,
    PrimeRoute Route, IReadOnlyList<PrimeRoute> History);

/// <summary>
/// Small, bounded route owner for the shell. Root selections clear history;
/// internal navigation can push a finite number of return routes.
/// </summary>
public sealed class PrimeNavigator
{
    private readonly List<PrimeRoute> _history = new();
    private readonly int _maximumHistory;
    private PrimeRoute _current;

    public PrimeNavigator(PrimeRoute initialRoute = PrimeRoute.Gateway, int maximumHistory = 24)
    {
        if (maximumHistory is < 1 or > 128)
            throw new ArgumentOutOfRangeException(nameof(maximumHistory));
        if (initialRoute != PrimeRoute.Gateway && !PrimeRouteInfo.IsAuthenticated(initialRoute))
            throw new ArgumentOutOfRangeException(nameof(initialRoute));
        _maximumHistory = maximumHistory;
        _current = initialRoute;
    }

    public PrimeRoute CurrentRoute => _current;
    public int HistoryCount => _history.Count;
    public bool CanGoBack => _history.Count > 0;
    public IReadOnlyList<PrimeRoute> History => _history.ToArray();

    public event EventHandler<PrimeNavigationChangedEventArgs>? Changed;

    /// <summary>Push a route for an internal screen transition.</summary>
    public void Navigate(PrimeRoute route)
    {
        ValidateRoute(route);
        if (_current == route) return;
        PushHistory(_current);
        _current = route;
        Raise(PrimeNavigationChangeKind.Push);
    }

    /// <summary>
    /// Select a top-level destination. It is a new root, so B/Escape cannot
    /// walk through every previous top-level selection.
    /// </summary>
    public void NavigateRoot(PrimeRoute route)
    {
        ValidateRoute(route);
        if (_current == route && _history.Count == 0) return;
        _history.Clear();
        _current = route;
        Raise(PrimeNavigationChangeKind.RootReplace);
    }

    public bool GoBack()
    {
        if (_history.Count == 0) return false;
        _current = _history[^1];
        _history.RemoveAt(_history.Count - 1);
        Raise(PrimeNavigationChangeKind.Back);
        return true;
    }

    public void Reset(PrimeRoute route = PrimeRoute.Gateway)
    {
        ValidateRoute(route);
        _history.Clear();
        _current = route;
        Raise(PrimeNavigationChangeKind.Reset);
    }

    private void PushHistory(PrimeRoute route)
    {
        if (_history.Count == _maximumHistory)
            _history.RemoveAt(0);
        _history.Add(route);
    }

    private static void ValidateRoute(PrimeRoute route)
    {
        if (route != PrimeRoute.Gateway && !PrimeRouteInfo.IsAuthenticated(route))
            throw new ArgumentOutOfRangeException(nameof(route));
    }

    private void Raise(PrimeNavigationChangeKind kind)
        => Changed?.Invoke(this, new PrimeNavigationChangedEventArgs(kind, _current, History));
}
