using System;
using System.Threading;

namespace MphRead.Mods.Launcher.RmlUi.Core;

public readonly record struct RmlUiOfflineRulesChange(GameMode Mode,
    RmlUiOfflineRules Expected, RmlUiOfflineRules Next)
{
    public bool Validate(out string error)
    {
        error = "INVALID LOCAL MATCH RULES";
        if (!Enum.IsDefined(Mode) || (Next.Flags & ~8191) != 0
            || Next.DamageLevel is not ("low" or "medium" or "high")) return false;
        var editor = new RmlUiOfflineRuleEditor(Mode, Next);
        if (!editor.Validate(Next.PointGoal, Next.TimeLimit, Next.TimeGoal))
        { error = editor.Error; return false; }
        if (editor.Draft != Next) return false;
        error = ""; return true;
    }
}

public sealed record RmlUiOfflineRulesSaveResult(RmlUiOfflineRulesChange Change,
    bool Success, string Status, RmlUiOfflineRules? Current);

/// <summary>Only immutable copied values cross the UI/EGL boundary.</summary>
public sealed class RmlUiOfflineRulesCache
{
    private sealed record Snapshot(RmlUiOfflineRules Value);
    private Snapshot? _snapshot;
    public RmlUiOfflineRulesCache(RmlUiOfflineRules initial) => _snapshot = new(initial);
    public RmlUiOfflineRules? Read() => Volatile.Read(ref _snapshot)?.Value;
    public void Publish(RmlUiOfflineRules? current)
        => Volatile.Write(ref _snapshot, current is { } rules ? new Snapshot(rules) : null);
}
