using System;
using System.Collections.Generic;
using System.Globalization;
using MphRead.Mods.Network;
using MphRead.Mods.Multiplayer;

namespace MphRead.Mods.Launcher.RmlUi.Core;

/// <summary>
/// An immutable copy of the session identity and rule state. Never publish an
/// edit against a newer revision, match, or authority epoch.
/// </summary>
public readonly record struct RmlUiRuleDraft(ushort MatchId, ulong AuthorityEpoch,
    ushort SessionRevision, MatchDefinition Match, LobbyRuleFlags Flags);

/// <summary>
/// A bounded local draft for the native rule sheet.
/// The authoritative session only changes after UpdateMatch is validated
/// and accepted by the server. No direct network, toolkit or scene calls.
/// </summary>
public sealed class RmlUiRuleEditor
{
    public const int ChoiceCount = Host.RmlUiIntentRegistry.LobbyRuleChoiceCount;
    public RmlUiRuleDraft Source { get; }
    public RmlUiRuleDraft Draft { get; private set; }
    public bool Dirty => Source.Match != Draft.Match || Source.Flags != Draft.Flags;
    public string MovementLabel => Movement.MovementModelRules.DisplayName(Draft.Match.MovementModel);

    public RmlUiRuleEditor(RmlUiRuleDraft source)
    {
        if (source.MatchId == 0 || source.AuthorityEpoch == 0
            || source.Match.RoomKey == null)
            throw new ArgumentException("A live authority session is required.", nameof(source));
        Source = Draft = source;
    }

    public bool Toggle(int index)
    {
        if (index is < 0 or >= ChoiceCount) return false;
        var match = Draft.Match;
        var flags = Draft.Flags;
        switch (index)
        {
            case 0: flags ^= LobbyRuleFlags.RequireReady; break;
            case 1: flags ^= LobbyRuleFlags.AllowJoinInProgress; break;
            case 2: flags ^= LobbyRuleFlags.LockTeams; break;
            case 3:
                match = match with { HideOpponentHealth = !match.HideOpponentHealth };
                flags = Set(flags, LobbyRuleFlags.HideOpponentHealth, match.HideOpponentHealth);
                break;
            case 4: match = match with { DisablePowerups = !match.DisablePowerups }; break;
            case 5: match = match with { SpawnProtection = !match.SpawnProtection }; break;
            case 6: match = match with { AffinityWeapons = !match.AffinityWeapons }; break;
            case 7: match = match with { FriendlyFire = !match.FriendlyFire }; break;
            case 8: match = match with { BalancedMode = !match.BalancedMode }; break;
            case 9: match = match with { InstaGib = !match.InstaGib }; break;
            case 10: match = match with { LowTier = !match.LowTier }; break;
            case 11: match = match with { NoImperialist = !match.NoImperialist }; break;
            case 12: match = match with { Fiesta = !match.Fiesta }; break;
            case 13: match = match with { VanillaDuelResources = !match.VanillaDuelResources }; break;
            case 14: match = match with { ShadowFreeze = !match.ShadowFreeze }; break;
            case 15: match = match with { OctolithAutoReset = !match.OctolithAutoReset }; break;
            case 16: match = match with { ThirdPersonCamera = !match.ThirdPersonCamera }; break;
            case 17:
                if (!Movement.MovementModelRules.IsDefined(match.MovementModel)) return false;
                match = match with { MovementModel = Movement.MovementModelRules.Next(match.MovementModel) };
                break;
        }
        Draft = Draft with { Match = match, Flags = flags };
        return true;
    }

    public bool IsOn(int index)
    {
        MatchDefinition match = Draft.Match;
        LobbyRuleFlags flags = Draft.Flags;
        return index switch
        {
            0 => flags.HasFlag(LobbyRuleFlags.RequireReady),
            1 => flags.HasFlag(LobbyRuleFlags.AllowJoinInProgress),
            2 => flags.HasFlag(LobbyRuleFlags.LockTeams),
            3 => !match.HideOpponentHealth,
            4 => !match.DisablePowerups,
            5 => match.SpawnProtection,
            6 => match.AffinityWeapons,
            7 => match.FriendlyFire,
            8 => match.BalancedMode,
            9 => match.InstaGib,
            10 => match.LowTier,
            11 => match.NoImperialist,
            12 => match.Fiesta,
            13 => match.VanillaDuelResources,
            14 => match.ShadowFreeze,
            15 => match.OctolithAutoReset,
            16 => match.ThirdPersonCamera,
            17 => Movement.MovementModelRules.IsDefined(match.MovementModel)
                && match.MovementModel != Movement.MovementModel.Native,
            _ => false
        };
    }

    public bool NextMode()
    {
        int current = MatchTypeCatalog.BaseModeIndex(Draft.Match.Mode);
        MatchTypeDefinition next = MatchTypeCatalog.GameTypes[(current + 1) % MatchTypeCatalog.GameTypes.Length];
        MatchFormat format = MatchTypeCatalog.NormalizeFormat(next, Draft.Match.Format);
        GameMode mode = next.Resolve(format);
        var match = Draft.Match with
        {
            Mode = mode,
            Format = format,
            TimeLimitSeconds = mode == GameMode.OneInTheChamber ? (ushort)0 : (ushort)420,
            PointGoal = MatchGoalRules.DefaultValue(mode),
            VanillaDuelResources = Draft.Match.VanillaDuelResources
                && format == MatchFormat.OneVsOne && mode == GameMode.BattleTeams,
            OctolithAutoReset = Draft.Match.OctolithAutoReset && MatchModifierRules.UsesOctolith(mode)
        };
        Draft = Draft with { Match = match };
        return true;
    }

    public bool NextFormat()
    {
        MatchTypeDefinition type = MatchTypeCatalog.GameTypes[
            MatchTypeCatalog.BaseModeIndex(Draft.Match.Mode)];
        int index = MatchTypeCatalog.MatchupIndex(Draft.Match.Format);
        // Custom has its own team layout editor; no synthesized invalid layout.
        for (int i = 1; i < MatchTypeCatalog.Matchups.Length; i++)
        {
            MatchFormat next = MatchTypeCatalog.Matchups[
                (index + i) % MatchTypeCatalog.Matchups.Length].Format;
            if (next == MatchFormat.Custom) continue;
            next = MatchTypeCatalog.NormalizeFormat(type, next);
            GameMode mode = type.Resolve(next);
            if (next == Draft.Match.Format && mode == Draft.Match.Mode) continue;
            var match = Draft.Match with
            {
                Format = next,
                Mode = mode,
                VanillaDuelResources = Draft.Match.VanillaDuelResources
                    && next == MatchFormat.OneVsOne && mode == GameMode.BattleTeams
            };
            if (LobbyRules.ValidateDefinition(match, out _) != LobbyResultCode.Ok)
                continue;
            Draft = Draft with { Match = match };
            return true;
        }
        return false;
    }

    public bool NextMap(IReadOnlyList<string> builtInArenas)
    {
        ArgumentNullException.ThrowIfNull(builtInArenas);
        if (builtInArenas.Count == 0) return false;
        int current = -1;
        for (int i = 0; i < builtInArenas.Count; i++)
            if (String.Equals(builtInArenas[i], Draft.Match.RoomKey,
                StringComparison.OrdinalIgnoreCase)) { current = i; break; }
        for (int step = 1; step <= builtInArenas.Count; step++)
        {
            string candidate = builtInArenas[(current + step) % builtInArenas.Count];
            if (String.IsNullOrWhiteSpace(candidate) || candidate == Draft.Match.RoomKey)
                continue;
            try
            {
                NetworkMapIdentity identity = NetworkMapIdentity.ForRoom(candidate);
                Draft = Draft with
                {
                    Match = Draft.Match with { RoomKey = candidate, MapIdentity = identity }
                };
                return true;
            }
            catch (ArgumentException) { /* Skip invalid room names. */ }
        }
        return false;
    }

    /// <summary>
    /// Stage an installed map inside the same optimistic rules draft. Server
    /// session identity/revision and immutable package hash are checked again
    /// by SubmitRules and the authoritative server before any change takes effect.
    /// </summary>
    public bool TrySelectMap(string room, out string reason)
    {
        reason = "";
        if (String.IsNullOrWhiteSpace(room))
        {
            reason = "Choose an installed map.";
            return false;
        }
        try
        {
            NetworkMapIdentity identity = NetworkMapIdentity.ForRoom(room);
            var candidate = Draft.Match with { RoomKey = room, MapIdentity = identity };
            if (LobbyRules.ValidateDefinition(candidate, out reason) != LobbyResultCode.Ok)
                return false;
            Draft = Draft with { Match = candidate };
            return true;
        }
        catch (Exception error) when (error is ArgumentException or System.IO.InvalidDataException)
        {
            reason = error.Message;
            return false;
        }
    }

    public bool TrySetLimits(string time, string goal, out string reason)
    {
        reason = "";
        time = time?.Trim() ?? "";
        goal = goal?.Trim() ?? "";
        ushort seconds;
        if (time.Contains(':'))
        {
            string[] parts = time.Split(':');
            if (parts.Length != 2
                || !UInt16.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out ushort mins)
                || !UInt16.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out ushort secs)
                || secs >= 60 || mins * 60u + secs > ushort.MaxValue)
            {
                reason = "Enter a time limit in M:SS format.";
                return false;
            }
            seconds = (ushort)(mins * 60 + secs);
        }
        else if (UInt16.TryParse(time, NumberStyles.None, CultureInfo.InvariantCulture, out ushort minutes)
                 && minutes * 60u <= ushort.MaxValue)
            seconds = (ushort)(minutes * 60);
        else
        {
            reason = "Enter a time limit as minutes or M:SS.";
            return false;
        }

        if (!UInt16.TryParse(goal, NumberStyles.None, CultureInfo.InvariantCulture, out ushort target))
        {
            reason = "Enter a valid score, lives, or control-time goal.";
            return false;
        }

        GameMode mode = Draft.Match.Mode;
        if (mode == GameMode.OneInTheChamber) { seconds = 0; target = 2; }
        Draft = Draft with { Match = Draft.Match with
            { TimeLimitSeconds = seconds, PointGoal = target } };
        return true;
    }

    public bool Validate(out string reason)
    {
        if (LobbyRules.ValidateDefinition(Draft.Match, out reason) != LobbyResultCode.Ok)
            return false;
        // The server will also recheck the latest roster, map package identity
        // and owner/revision fence before accepting the UpdateMatch command.
        return true;
    }

    public bool SameSession(in RmlUiRuleDraft current) =>
        current.MatchId == Source.MatchId
        && current.AuthorityEpoch == Source.AuthorityEpoch
        && current.SessionRevision == Source.SessionRevision;

    public string TimeInput() =>
        (Draft.Match.TimeLimitSeconds / 60).ToString(CultureInfo.InvariantCulture)
        + ":" + (Draft.Match.TimeLimitSeconds % 60).ToString("00", CultureInfo.InvariantCulture);

    public string GoalInput() => Draft.Match.PointGoal.ToString(CultureInfo.InvariantCulture);

    private static LobbyRuleFlags Set(LobbyRuleFlags flags, LobbyRuleFlags bit, bool enabled)
        => enabled ? flags | bit : flags & ~bit;
}
