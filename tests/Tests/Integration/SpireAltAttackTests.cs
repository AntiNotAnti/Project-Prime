using System;
using System.IO;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Mods.Network;
using OpenTK.Mathematics;
using Xunit;

namespace MphRead.Tests;

[Collection("Match baseline globals")]
public sealed class SpireAltAttackTests
{
    [Fact]
    public void SpireAltAttackRocksFollowCpuMovementWithoutRendering()
    {
        bool previousServerMode = Read.ServerMode;
        try
        {
            using var saved = ServerContent.PreserveContext("AMHE1");
            ServerContent.Open(FindAmhe1(), "AMHE1");
            using var simulation = new ServerSimulation(new RotationEntry
            {
                RoomKey = "MP1 SANCTORUS",
                Mode = GameMode.Battle
            });

            PlayerEntity player = PlayerEntity.Players[0];
            player.ServerActivate(0x51, Hunter.Spire, team: 0);
            player.Flags2 |= PlayerFlags2.AltAttack;
            player._altModel.SetAnimation((int)SpireAltAnim.Attack, AnimFlags.NoLoop | AnimFlags.Paused);

            Assert.True(player.Process());
            Vector3 beforeLeft = player._spireRockPosL;
            Vector3 beforeRight = player._spireRockPosR;
            Vector3 beforeOffset = beforeLeft - beforeRight;
            Vector3 beforeMovePosition = player.Position;

            Vector3 delta = new(0.75f, 0, 0.5f);
            player.Reposition(beforeMovePosition + delta, player.FacingVector, player.NodeRef);
            player.PrevPosition = player.Position;
            player.Speed = Vector3.Zero;
            player.Acceleration = Vector3.Zero;

            Assert.True(player.Process());
            Vector3 actualDelta = player.Position - beforeMovePosition;
            Vector3 leftDelta = player._spireRockPosL - beforeLeft;
            Vector3 rightDelta = player._spireRockPosR - beforeRight;
            Vector3 afterOffset = player._spireRockPosL - player._spireRockPosR;

            Assert.Equal(delta.X, actualDelta.X, precision: 4);
            Assert.Equal(delta.Z, actualDelta.Z, precision: 4);
            Assert.Equal(actualDelta.X, leftDelta.X, precision: 4);
            Assert.Equal(actualDelta.Y, leftDelta.Y, precision: 4);
            Assert.Equal(actualDelta.Z, leftDelta.Z, precision: 4);
            Assert.Equal(actualDelta.X, rightDelta.X, precision: 4);
            Assert.Equal(actualDelta.Y, rightDelta.Y, precision: 4);
            Assert.Equal(actualDelta.Z, rightDelta.Z, precision: 4);
            Assert.Equal(beforeOffset.X, afterOffset.X, precision: 4);
            Assert.Equal(beforeOffset.Y, afterOffset.Y, precision: 4);
            Assert.Equal(beforeOffset.Z, afterOffset.Z, precision: 4);
        }
        finally
        {
            Read.ServerMode = previousServerMode;
        }
    }

    private static string FindAmhe1()
    {
        string? configured = Environment.GetEnvironmentVariable("GAME_DATA_DIRECTORY");
        string[] starts = configured is null
            ? new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory }
            : new[] { configured, Directory.GetCurrentDirectory(), AppContext.BaseDirectory };
        foreach (string start in starts)
        {
            DirectoryInfo? directory = new(Path.GetFullPath(start));
            while (directory != null)
            {
                string candidate = Path.Combine(directory.FullName, "AMHE1");
                if (File.Exists(Path.Combine(candidate, "_bin", "arm9.bin"))
                    && Directory.Exists(Path.Combine(candidate, "models"))
                    && Directory.Exists(Path.Combine(candidate, "levels")))
                {
                    return candidate;
                }
                directory = directory.Parent;
            }
        }

        throw new DirectoryNotFoundException("AMHE1 extracted content was not found.");
    }
}
