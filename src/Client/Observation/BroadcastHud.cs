using System;
using System.Collections.Immutable;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Hud;
using OpenTK.Mathematics;

namespace MphRead;

public enum BroadcastHudMode { Full, Minimal, Off }

public readonly record struct BroadcastScoreboard(int Team0Score, int Team1Score,
    string Clock, string MatchState);

public readonly record struct BroadcastPlayerCard(string Name, Hunter Hunter,
    bool IsPrime, bool CarriesObjective, int Health, BeamType Weapon, int AmmoUa,
    int AmmoMissiles, int Kills, int Deaths, int Assists, int Points);

public readonly record struct BroadcastObjectiveCard(ObservationObjectiveKind Kind,
    bool Contested, bool HasCarrier, int CarrierSlot, bool AtBase, int Team);

public readonly record struct BroadcastKillFeed(string Text);

public readonly record struct BroadcastAwardBanner(string Text);

public readonly record struct BroadcastHudModel(BroadcastHudMode Mode,
    BroadcastScoreboard? Scoreboard, BroadcastPlayerCard? PlayerCard,
    BroadcastObjectiveCard? ObjectiveCard, BroadcastKillFeed? KillFeed,
    BroadcastAwardBanner? AwardBanner, ImmutableArray<string> Lines)
{
    public bool Visible => Mode != BroadcastHudMode.Off && !Lines.IsEmpty;
}

/// <summary>Observer-only match presentation. It contains no network/debug diagnostics.</summary>
public sealed class BroadcastHud
{
    public BroadcastHudMode Mode { get; private set; } = BroadcastHudMode.Full;

    public void SetMode(BroadcastHudMode mode)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        Mode = mode;
    }

    public BroadcastHudMode CycleMode()
    {
        Mode = (BroadcastHudMode)(((int)Mode + 1) % 3);
        return Mode;
    }

    public BroadcastHudModel Compose(ObservationContext context, BroadcastFocus focus)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (Mode == BroadcastHudMode.Off)
            return new(Mode, null, null, null, null, null, []);
        var lines = ImmutableArray.CreateBuilder<string>(Mode == BroadcastHudMode.Full ? 4 : 2);
        string state = context.IsOvertime ? "OVERTIME" : context.IsMatchPoint ? "MATCH POINT" : context.Phase.ToString();
        string clock = context.MatchTimeSeconds < 0 ? "--:--"
            : $"{(int)context.MatchTimeSeconds / 60:00}:{(int)context.MatchTimeSeconds % 60:00}";
        int team0 = 0, team1 = 0;
        foreach (ObservationPlayer value in context.Players)
        {
            if (value.Team == 0) team0 += value.Points;
            else if (value.Team == 1) team1 += value.Points;
        }
        var scoreboard = new BroadcastScoreboard(team0, team1, clock, state);
        BroadcastPlayerCard? playerCard = null;
        BroadcastObjectiveCard? objectiveCard = null;
        BroadcastKillFeed? killFeed = null;
        BroadcastAwardBanner? awardBanner = null;
        lines.Add($"{team0}  {clock}  {team1}    {state}");
        if (focus.Kind == BroadcastFocusKind.Player
            && context.TryGetPlayer(focus.Id, out ObservationPlayer player))
        {
            playerCard = new(player.Name, player.Hunter, player.IsPrime,
                player.CarriesObjective, player.Health, player.Weapon, player.AmmoUa,
                player.AmmoMissiles, player.Kills, player.Deaths, player.Assists,
                player.Points);
            string role = player.IsPrime ? " PRIME" : player.CarriesObjective ? " CARRIER" : "";
            lines.Add($"{player.Name}  {player.Hunter}{role}  HP {player.Health}");
            if (Mode == BroadcastHudMode.Full)
            {
                lines.Add($"{player.Weapon}  AMMO {player.AmmoUa}/{player.AmmoMissiles}");
                lines.Add($"K/D/A {player.Kills}/{player.Deaths}/{player.Assists}  SCORE {player.Points}");
            }
        }
        else if (focus.Kind == BroadcastFocusKind.Objective
            && context.TryGetObjective(focus.Id, out ObservationObjective objective))
        {
            objectiveCard = new(objective.Kind, objective.Contested,
                objective.HasCarrier, objective.CarrierSlot, objective.AtBase,
                objective.Team);
            string status = objective.Contested ? "CONTESTED"
                : objective.HasCarrier ? $"CARRIED BY P{objective.CarrierSlot + 1}"
                : objective.AtBase ? "AT BASE" : $"TEAM {objective.Team}";
            lines.Add($"{objective.Kind}  {status}");
        }
        else lines.Add("NO ACTIVE TARGET");
        if (Mode == BroadcastHudMode.Full)
        {
            if (!context.CombatFeedback.IsEmpty)
            {
                killFeed = new(context.CombatFeedback[^1].Text);
                lines.Add(killFeed.Value.Text);
            }
            if (!context.Awards.IsEmpty)
            {
                MatchAward award = context.Awards[^1];
                string subject = context.TryGetPlayer(award.Subject,
                    out ObservationPlayer awardedPlayer)
                    ? awardedPlayer.Name : "UNKNOWN";
                awardBanner = new($"{AwardLabel(award.Kind)} · {subject}");
                lines.Add(awardBanner.Value.Text);
            }
        }
        return new(Mode, scoreboard, playerCard, objectiveCard, killFeed,
            awardBanner, lines.ToImmutable());
    }

    private static string AwardLabel(MatchAwardKind kind) => kind switch
    {
        MatchAwardKind.FirstHunt => "FIRST HUNT",
        MatchAwardKind.DoubleKill => "DOUBLE KILL",
        MatchAwardKind.TripleKill => "TRIPLE KILL",
        MatchAwardKind.PrimeSlayer => "PRIME SLAYER",
        _ => kind.ToString().ToUpperInvariant()
    };

    internal void Draw(ScenePresentation presentation, in BroadcastHudModel model)
    {
        if (!model.Visible) return;
        PlayerPresentation? hud = null;
        foreach (PlayerEntity player in presentation.World.Players)
        {
            if (!player.LoadFlags.TestFlag(LoadFlags.Active)) continue;
            hud = player.GetPresentation();
            break;
        }
        if (hud == null) return;
        int height = model.Mode == BroadcastHudMode.Minimal ? 22 : 11 + model.Lines.Length * 9;
        presentation.DrawHudFlatBox(5, 4, 251, height, new Vector4(0, 0, 0, .7f));
        for (int index = 0; index < model.Lines.Length; index++)
            hud.DrawText2D(128, 8 + index * 9, Align.Center, 0, model.Lines[index],
                maxLength: 48, scale: index == 0 ? .55f : .5f);
    }
}
