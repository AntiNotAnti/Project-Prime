using System;
using System.Collections.Immutable;
using MphRead.Entities;
using MphRead.Hud;
using OpenTK.Mathematics;

namespace MphRead;

public enum BroadcastHudMode { Full, Minimal, Off }

public readonly record struct BroadcastHudModel(BroadcastHudMode Mode,
    ImmutableArray<string> Lines)
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

    public BroadcastHudModel Compose(ObservationContext context, BroadcastFocus focus,
        SpectatorCameraMode cameraMode, float fieldOfView, float speedScale)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (Mode == BroadcastHudMode.Off) return new(Mode, []);
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
        lines.Add($"{team0}  {clock}  {team1}    {state}");
        if (focus.Kind == BroadcastFocusKind.Player
            && context.TryGetPlayer(focus.Id, out ObservationPlayer player))
        {
            string role = player.IsPrime ? " PRIME" : player.CarriesObjective ? " CARRIER" : "";
            lines.Add($"{player.Name}  {player.Hunter}{role}  HP {player.Health}");
            if (Mode == BroadcastHudMode.Full)
            {
                lines.Add($"{player.Weapon}  AMMO {player.AmmoUa}/{player.AmmoMissiles}");
                lines.Add($"K/D/A {player.Kills}/{player.Deaths}/{player.Assists}  SCORE {player.Points}  {cameraMode}  FOV {fieldOfView:0}  {speedScale:0.##}x");
            }
        }
        else if (focus.Kind == BroadcastFocusKind.Objective
            && context.TryGetObjective(focus.Id, out ObservationObjective objective))
        {
            string status = objective.Contested ? "CONTESTED"
                : objective.HasCarrier ? $"CARRIED BY P{objective.CarrierSlot + 1}"
                : objective.AtBase ? "AT BASE" : $"TEAM {objective.Team}";
            lines.Add($"{objective.Kind}  {status}");
            if (Mode == BroadcastHudMode.Full)
                lines.Add($"FOV {fieldOfView:0}  CAMERA {speedScale:0.##}x");
        }
        else lines.Add("NO ACTIVE TARGET");
        if (Mode == BroadcastHudMode.Full)
        {
            if (!context.CombatFeedback.IsEmpty)
                lines.Add(context.CombatFeedback[^1].Text);
            if (!context.Awards.IsEmpty)
            {
                MatchAward award = context.Awards[^1];
                string subject = context.TryGetPlayer(award.Subject.Slot,
                    out ObservationPlayer awardedPlayer)
                    ? awardedPlayer.Name : $"P{award.Subject.Slot + 1}";
                lines.Add($"{AwardLabel(award.Kind)} · {subject}");
            }
        }
        return new(Mode, lines.ToImmutable());
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
