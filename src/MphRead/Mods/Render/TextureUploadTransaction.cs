using System;
using System.Collections.Generic;
using System.Linq;

namespace MphRead.Mods.Render;

/// <summary>Roll back only admissions made by a failed owner-thread model compile.</summary>
internal sealed class TextureUploadTransaction : IDisposable
{
    private readonly Dictionary<string, int> _bindings;
    private readonly HashSet<int> _pins;
    private readonly HashSet<int> _existing;
    private readonly HashSet<int> _existingPins;
    private readonly Action<int> _release;
    private bool _committed;
    internal TextureUploadTransaction(Dictionary<string, int> bindings, HashSet<int> pins, Action<int> release)
    {
        _bindings = bindings;
        _pins = pins;
        _existing = bindings.Values.ToHashSet();
        _existingPins = new(pins);
        _release = release;
    }
    internal void Commit() => _committed = true;
    public void Dispose()
    {
        if (_committed) return;
        _committed = true;
        int[] added = _bindings.Values.Where(binding => !_existing.Contains(binding)).Distinct().ToArray();
        foreach (string key in _bindings.Where(pair => !_existing.Contains(pair.Value)).Select(pair => pair.Key).ToArray())
            _bindings.Remove(key);
        _pins.IntersectWith(_existingPins);
        foreach (int binding in added) _release(binding);
    }
}

internal sealed class CharacterTextureAdmissionException : InvalidOperationException
{
    internal CharacterTextureAdmissionException() : base("Embedded character albedo could not be admitted to the texture budget.") { }
}
