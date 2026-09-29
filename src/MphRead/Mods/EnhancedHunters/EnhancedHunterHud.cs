using MphRead.Entities;
using MphRead.Formats;
using MphRead.Hud;
using OpenTK.Mathematics;

namespace MphRead.Mods.EnhancedHunters;
internal static class EnhancedHunterHud
{
    private static readonly string[] Pips = ["[---]", "[|--]", "[||-]", "[|||]"];
    internal static void Queue(PlayerEntity player)
    {
        if (!EnhancedHunters.Enabled(player) || !EnhancedHunters.Alive(player)) return;
        var s = player.EnhancedState;
        string text = player.Hunter switch
        {
            Hunter.Samus when player.CurrentWeapon == BeamType.Missile => "SEEKER " + Pips[System.Math.Min(3, (int)s.ValueA)],
            Hunter.Kanden when s.TargetSlot != byte.MaxValue => "LIGHTNING ROD " + Pips[System.Math.Min(3, (int)s.ValueA)],
            Hunter.Trace when s.GhostFrames > 0 => $"GHOST STEP {s.GhostFrames / 60f:0.0}",
            Hunter.Trace when s.TargetSlot != byte.MaxValue => (s.Flags & 1) != 0 ? "< PERFECT MARK >" : "< PREDATOR MARK >",
            Hunter.Sylux when s.Flags != 0 => $"TETHER {s.ValueA}%",
            Hunter.Noxus when s.Flags != 0 => "BRITTLE " + Pips[System.Math.Min(3, (int)s.ValueB)],
            Hunter.Noxus when s.ValueA > 0 => "FROST " + Pips[System.Math.Min(3, (int)s.ValueA)],
            Hunter.Spire => $"MAGMA {player.OwningScene.EnhancedWorld.Count(player, EnhancedZoneType.MagmaPool)}/2  BOUNCE {s.ValueB}",
            Hunter.Weavel when player.IsAltForm => $"SIEGE {s.ValueB}" + (s.TimerB > 0 ? $"  OVERCLOCK {s.TimerB / 60f:0.0}" : ""),
            Hunter.Weavel => "SIEGE " + Pips[System.Math.Min(3, (int)s.ValueA)],
            _ => ""
        };
        var target = EnhancedHunters.Target(player);
        if (target != null && EnhancedHunters.Visible(player, target))
        {
            var scene = player.OwningScene;
            Vector3 position = target.Position.AddY(.7f);
            if (Matrix.Vec3MultMtx4(position, scene.ViewMatrix).Z < -1)
            {
                Matrix.ProjectPosition(position, scene.ViewMatrix, scene.PerspectiveMatrix, out Vector2 point);
                if (point.X > 0 && point.X < 1 && point.Y > 0 && point.Y < 1)
                {
                    string icon = player.Hunter switch
                    {
                        Hunter.Samus => "[   ]", Hunter.Kanden => "+ +",
                        Hunter.Trace => (s.Flags & 1) != 0 ? "<< >>" : "< >",
                        Hunter.Sylux => "~ ~", Hunter.Noxus => s.Flags != 0 ? "** **" : "* *", _ => ""
                    };
                    if (icon.Length > 0) player.QueueHudMessage(point.X * 256, point.Y * 192, 1 / 60f, 0, icon, dialogHide: true);
                }
            }
        }
        // Use the HUD's logical coordinates so the status stays near the bottom at any resolution.
        if (text.Length != 0) player.QueueHudMessage(128, 176, Align.Center, 232, 6,
            new ColorRgba(0x3FEF), 1, 1 / 60f, 0, text, dialogHide: true);
    }
}
