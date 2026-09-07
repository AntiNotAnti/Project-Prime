using System;
using System.Collections.Generic;
using Avalonia.Input;

namespace MphRead.Mods.UI.Navigation;

public enum UiFocusDirection
{
    Up,
    Down,
    Left,
    Right
}

/// <summary>Explicit, testable directional focus graph used by keyboard and controller input.</summary>
public sealed class UiFocusNavigationPolicy
{
    private readonly Dictionary<(string Key, UiFocusDirection Direction), string> _edges = [];

    public void Connect(string from, UiFocusDirection direction, string to)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(from);
        ArgumentException.ThrowIfNullOrWhiteSpace(to);
        _edges[(from, direction)] = to;
    }

    public void ConnectVertical(IReadOnlyList<string> keys, bool wrap = false)
        => ConnectAxis(keys, UiFocusDirection.Up, UiFocusDirection.Down, wrap);

    public void ConnectHorizontal(IReadOnlyList<string> keys, bool wrap = false)
        => ConnectAxis(keys, UiFocusDirection.Left, UiFocusDirection.Right, wrap);

    public bool TryMove(string from, UiFocusDirection direction, out string target)
        => _edges.TryGetValue((from, direction), out target!);

    public static bool TryDirection(Key key, out UiFocusDirection direction)
    {
        direction = key switch
        {
            Key.Up => UiFocusDirection.Up,
            Key.Down => UiFocusDirection.Down,
            Key.Left => UiFocusDirection.Left,
            Key.Right => UiFocusDirection.Right,
            _ => default
        };
        return key is Key.Up or Key.Down or Key.Left or Key.Right;
    }

    private void ConnectAxis(IReadOnlyList<string> keys, UiFocusDirection previous,
        UiFocusDirection next, bool wrap)
    {
        for (int i = 0; i < keys.Count; i++)
        {
            if (i > 0) Connect(keys[i], previous, keys[i - 1]);
            else if (wrap && keys.Count > 1) Connect(keys[i], previous, keys[^1]);

            if (i + 1 < keys.Count) Connect(keys[i], next, keys[i + 1]);
            else if (wrap && keys.Count > 1) Connect(keys[i], next, keys[0]);
        }
    }
}
