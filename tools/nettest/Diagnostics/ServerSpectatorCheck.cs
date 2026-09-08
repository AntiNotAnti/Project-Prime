using System;
using MphRead.Entities;
using MphRead.Formats;
using OpenTK.Mathematics;

namespace MphRead.Mods.Network
{
    /// <summary>Content-backed participation and objective-release regression.</summary>
    public static class ServerSpectatorCheck
    {
        public static int Run(string data, string version, string room)
        {
            try
            {
                ServerContent.Open(data, version);
                foreach (GameMode mode in new[] { GameMode.Battle, GameMode.Capture, GameMode.Nodes, GameMode.PrimeHunter })
                {
                    Scene scene = Scene.CreateHeadless();
                    try { Check(scene, room, mode); }
                    finally { scene.CloseHeadless(); }
                }
                Console.WriteLine("[spectatorcheck] participation, no kill award, objective release, delayed rejoin PASS");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("[spectatorcheck] " + ex);
                return 1;
            }
        }

        private static void Check(Scene scene, string room, GameMode mode)
        {
            scene.LoadServerRoom(room, mode, players: 2);
            PlayerEntity player = scene.Players[0], opponent = scene.Players[1];
            player.ServerActivate(100, Hunter.Samus, 0);
            opponent.ServerActivate(200, Hunter.Kanden, 1);
            uint previousLife = player.ServerCombatIdentity.Life;
            Vector3 opponentSpawn = opponent.Position;
            OctolithFlagEntity? carried = null;
            if (mode == GameMode.Capture)
            {
                foreach (OctolithFlagEntity flag in scene.GetOctolithFlagEntities())
                {
                    player.Position = flag.Position.AddY(-1.25f);
                    player.PrevPosition = player.Position - Vector3.UnitX * 2;
                    opponent.Position = opponent.PrevPosition = flag.Position + new Vector3(8, 0, 0);
                    flag.Process();
                    if (flag.Carrier == player) { carried = flag; break; }
                }
                Require(carried != null, "Capture probe could not pick up an objective.");
            }
            NodeDefenseEntity? captured = null;
            if (mode == GameMode.Nodes)
            {
                scene.StepHeadlessFrame(advanceMatch: false);
                foreach (NodeDefenseEntity node in scene.GetNodeDefenseEntities())
                {
                    CollisionVolume volume = node.Volume;
                    Vector3 center = volume.Type switch
                    {
                        VolumeType.Sphere => volume.SpherePosition,
                        VolumeType.Cylinder => volume.CylinderPosition + volume.CylinderVector * volume.CylinderDot / 2,
                        _ => volume.BoxPosition + (volume.BoxVector1 * volume.BoxDot1
                            + volume.BoxVector2 * volume.BoxDot2 + volume.BoxVector3 * volume.BoxDot3) / 2
                    };
                    Vector3 old = player.Position;
                    player.Position = center - (player.Volume.SpherePosition - old);
                    player.ModRefreshNodeRef(old);
                    opponent.Position = center + Vector3.UnitX * 20;
                    for (int tick = 0; tick < 602; tick++) { node.Process(); }
                    if (node.CapturedPlayer == player) { captured = node; break; }
                }
                Require(captured != null, "Nodes probe could not capture an objective.");
            }
            opponent.Position = opponent.PrevPosition = opponentSpawn;
            opponent.Speed = Vector3.Zero;
            scene.Match.Players[0].Points = -3;
            scene.Match.Players[0].Kills = 2;
            scene.Match.Players[0].Deaths = 1;
            scene.Match.Players[0].Suicides = 1;
            if (mode == GameMode.PrimeHunter) { scene.Match.PrimeHunter = 0; }
            ulong startFrame = scene.LiveFrames;
            player.ServerSetSpectating(true);
            Require(carried == null || carried.Carrier != player, "Spectator did not release its carrier immediately.");
            Require(captured == null || captured.CapturedPlayer != player, "Spectator did not release its node immediately.");
            Vector3 heldPosition = player.Position;
            int opponentHealth = opponent.Health;
            for (int tick = 0; tick < 600; tick++)
            {
                player.ServerSetSpectating(true); // repeated held intent is idempotent
                scene.StepHeadlessFrame(advanceMatch: false);
                Require(player.Health == 0 && player.Position == heldPosition
                    && !player.LoadFlags.TestFlag(LoadFlags.Active)
                    && player.Flags2.TestFlag(PlayerFlags2.Spectating), "Spectator re-entered simulation.");
            }
            Require(scene.LiveFrames - startFrame == 600, "Match did not continue while spectating.");
            Require(opponent.Health > 0 && opponentHealth > 0, "Other participant did not remain in play.");
            Require(carried == null || carried.Carrier != player, "Spectator retained an objective.");
            Require(captured == null || captured.CapturedPlayer != player, "Spectator retained node ownership.");
            Require(mode != GameMode.PrimeHunter || scene.Match.PrimeHunter != 0, "Spectator remained prime hunter.");
            Require(scene.Match.Players[0].Points == -3 && scene.Match.Players[0].Kills == 2
                && scene.Match.Players[0].Deaths == 1 && scene.Match.Players[0].Suicides == 1,
                "Participation changed combat scores.");
            player.ServerSetSpectating(false);
            for (int tick = 1; tick < PlayerEntity.RespawnTime; tick++)
            {
                player.ServerSetSpectating(false);
                scene.StepHeadlessFrame(advanceMatch: false);
                Require(player.Health == 0, "Rejoin bypassed the respawn delay.");
            }
            scene.StepHeadlessFrame(advanceMatch: false);
            Require(player.Health > 0 && player.LoadFlags.TestFlag(LoadFlags.Spawned)
                && !player.Flags2.TestFlag(PlayerFlags2.Spectating)
                && player.ServerCombatIdentity.Life != previousLife, "Rejoin did not create a fresh server-selected life.");
            Require(scene.Match.Players[0].Points == -3 && scene.Match.Players[0].Kills == 2 && scene.Match.Players[0].Deaths == 1,
                "Rejoin reset or improved scores.");
            Console.WriteLine($"[spectatorcheck] {mode}: 600 spectator ticks, rejoin after {PlayerEntity.RespawnTime} ticks PASS");
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) { throw new ProgramException(message); }
        }
    }
}
