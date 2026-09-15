using System;
using System.IO;
using MphRead.Entities;
using MphRead.Mods.Network;
using OpenTK.Mathematics;
using Xunit;

namespace MphRead.Tests;

[Collection("Match baseline globals")]
public sealed class SpawnFacingRegressionTests
{
    [Trait("RequiresGameContent", "true")]
    [Fact]
    public void RespawnReplacesPriorLifeNetworkAimBeforeSameFrameInputProcessing()
    {
        using IDisposable content = ServerContent.PreserveContext("AMHE1");
        ServerContent.Open(FindAmhe1(), "AMHE1");
        using var simulation = new ServerSimulation(
            new MatchRules(MatchMode.Battle, "AD2 ALINOS PERCH"));
        Scene scene = simulation.Scene;
        scene.Match.Phase = MatchPhase.Playing;
        PlayerEntity player = scene.Players[0];
        player.ServerActivate(0x5100, Hunter.Samus, -1);
        PlayerSpawnEntity? spawn = null;
        foreach (PlayerSpawnEntity candidate in scene.GetPlayerSpawnEntities())
        {
            spawn = candidate;
            break;
        }
        Assert.NotNull(spawn);
        Vector3 authoredFacing = spawn.FacingVector;

        // The command was accepted while this life was still dead. Spawn owns
        // the life transition and must prevent its cached aim from replacing
        // the authored facing later in the same simulation step.
        player.ApplyNetworkInput(new InputCommand(1, 1, 1,
            InputButtons.None, InputButtons.None, -authoredFacing,
            InputCommand.NoWeapon));
        player.Spawn(spawn.Position, authoredFacing, spawn.UpVector,
            spawn.NodeRef, respawn: true);

        player.Process();

        Assert.True(Vector3.Dot(player.ModGunVector, authoredFacing) > 0.999f);
        Assert.True(Vector3.Dot(player.FacingVector, authoredFacing) > 0.999f);
        Assert.True(Vector3.Dot(player.CameraInfo.Facing, authoredFacing) > 0.999f);
    }

    private static string FindAmhe1()
    {
        DirectoryInfo? directory = new(Directory.GetCurrentDirectory());
        while (directory != null)
        {
            string candidate = Path.Combine(directory.FullName, "AMHE1");
            if (File.Exists(Path.Combine(candidate, "_bin", "arm9.bin")))
                return candidate;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("AMHE1 extracted content was not found.");
    }
}
