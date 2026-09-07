using System;

namespace MphRead;

/// <summary>
/// Mutable lobby-owned reference to validated rules. MatchRules remains immutable;
/// replacing this reference is the only draft mutation.
/// </summary>
public sealed class LobbyRulesDraft
{
    private MatchRules _rules;

    public MatchRules Current => _rules;
    public LobbySelection Selection => new(_rules.RoomKey, _rules.Mode);

    public LobbyRulesDraft(MatchRules rules)
    {
        _rules = Validate(rules);
    }

    internal bool Replace(MatchRules rules)
    {
        rules = Validate(rules);
        if (rules == _rules) return false;
        _rules = rules;
        return true;
    }

    internal bool Select(LobbySelection selection)
        => Replace(_rules.With(mode: selection.Mode, roomKey: selection.MapKey));

    public MatchRules Freeze()
    {
        MatchLifecycle.ValidateRules(_rules);
        return _rules;
    }

    private static MatchRules Validate(MatchRules? rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        MatchLifecycle.ValidateRules(rules);
        _ = new LobbySelection(rules.RoomKey, rules.Mode);
        return rules;
    }
}
