using System;
using System.Collections.Generic;
namespace MphRead.Mods.Render.Hud;

/// <summary>Detached editor document. Drag callers capture once and commit once.</summary>
public sealed class HudStudioHistory
{
    private readonly List<string> _undo = new(), _redo = new();
    public HudProfile Draft { get; private set; }
    public bool CanUndo => _undo.Count != 0;
    public bool CanRedo => _redo.Count != 0;
    public HudStudioHistory(HudProfile original) => Draft = original.DeepClone();
    public string Capture() => HudProfileStore.Serialize(Draft);
    public void Commit(string before)
    {
        Draft.Validate();
        if (before == Capture()) return;
        _undo.Add(before); if (_undo.Count > 150) _undo.RemoveAt(0); _redo.Clear();
    }
    public void Edit(Action<HudProfile> change)
    { string before = Capture(); change(Draft); Commit(before); }
    public void Replace(HudProfile profile)
    { string before = Capture(); Draft = profile.DeepClone(); Commit(before); }
    public void Undo()
    {
        if (!CanUndo) return;
        _redo.Add(Capture()); Draft = HudProfileStore.Parse(_undo[^1]); _undo.RemoveAt(_undo.Count - 1);
    }
    public void Redo()
    {
        if (!CanRedo) return;
        _undo.Add(Capture()); Draft = HudProfileStore.Parse(_redo[^1]); _redo.RemoveAt(_redo.Count - 1);
    }
}
