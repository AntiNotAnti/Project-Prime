using System;
using System.IO;
using System.Reflection;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Mods.Network;
using OpenTK.Mathematics;
using Xunit;

namespace MphRead.Tests;

[Collection("Match baseline globals")]
public sealed class SpireAltAttackTests
{
    [Trait("RequiresGameContent", "true")]
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

            PlayerEntity player = simulation.Scene.Players[0];
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

    [Trait("RequiresGameContent", "true")]
    [Fact]
    public void SpireAltAttackStateReplicatesToRemotePresentation()
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

            PlayerEntity authority = simulation.Scene.Players[0];
            authority.ServerActivate(0x51, Hunter.Spire, team: 0);
            authority.ModForceForm(altForm: true);
            authority.Flags2 |= PlayerFlags2.AltAttack;
            SnapshotPlayer state = authority.CaptureServerState();

            Assert.True((state.Flags & SnapshotPlayerFlags.SpireAltAttack) != 0);
            authority.Health = 0;
            Assert.False((authority.CaptureServerState().Flags
                & SnapshotPlayerFlags.SpireAltAttack) != 0);

            state.Slot = 1;
            state.ConnectionId = 0x52;
            PlayerEntity replica = simulation.Scene.Players[1];
            replica.ClientActivate(state);
            replica.ApplyServerState(state, newLife: true);
            Assert.True(replica.Flags2.TestFlag(PlayerFlags2.AltAttack));
            Assert.Equal(replica.Position, replica._spireRockPosL);
            Assert.Equal(replica.Position, replica._spireRockPosR);

            state.Flags &= ~SnapshotPlayerFlags.SpireAltAttack;
            replica.ApplyServerState(state, newLife: false);
            Assert.False(replica.Flags2.TestFlag(PlayerFlags2.AltAttack));
        }
        finally
        {
            Read.ServerMode = previousServerMode;
        }
    }

    [Trait("RequiresGameContent", "true")]
    [Fact]
    public void PowerupTimersAndCloakingFlagReplicateAndClearWithSnapshotState()
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

            PlayerEntity authority = simulation.Scene.Players[0];
            authority.ServerActivate(0x51, Hunter.Samus, team: 0);
            authority._doubleDmgTimer = 123;
            authority._cloakTimer = 234;
            SetPrivateField(authority, "_deathaltTimer", (ushort)345);
            authority.Flags2 |= PlayerFlags2.Cloaking;
            SnapshotPlayer state = authority.CaptureServerState();

            Assert.Equal((ushort)123, state.DoubleDamageTicks);
            Assert.Equal((ushort)234, state.CloakTicks);
            Assert.Equal((ushort)345, state.DeathaltTicks);
            Assert.True((state.Flags & SnapshotPlayerFlags.Cloaking) != 0);

            state.Slot = 1;
            state.ConnectionId = 0x52;
            PlayerEntity replica = simulation.Scene.Players[1];
            replica.ClientActivate(state);
            replica.ApplyServerState(state, newLife: true);
            Assert.Equal((ushort)123, replica._doubleDmgTimer);
            Assert.Equal((ushort)234, replica._cloakTimer);
            Assert.Equal((ushort)345, GetPrivateField<ushort>(replica,
                "_deathaltTimer"));
            Assert.True(replica.Flags2.TestFlag(PlayerFlags2.Cloaking));
            Assert.True(replica.Process());
            // Headless validation intentionally does not allocate presentation
            // effects, but the same remote Process path advances the timer and
            // attempts effect 181 in a rendered replica scene.
            Assert.Equal((ushort)344, GetPrivateField<ushort>(replica,
                "_deathaltTimer"));

            state.DoubleDamageTicks = 0;
            state.CloakTicks = 0;
            state.DeathaltTicks = 0;
            state.Flags &= ~SnapshotPlayerFlags.Cloaking;
            replica.ApplyServerState(state, newLife: false);
            Assert.Equal((ushort)0, replica._doubleDmgTimer);
            Assert.Equal((ushort)0, replica._cloakTimer);
            Assert.Equal((ushort)0, GetPrivateField<ushort>(replica,
                "_deathaltTimer"));
            Assert.Null(GetPrivateField<object?>(replica,
                "_deathaltEffect"));
            Assert.False(replica.Flags2.TestFlag(PlayerFlags2.Cloaking));
        }
        finally
        {
            Read.ServerMode = previousServerMode;
        }
    }

    [Trait("RequiresGameContent", "true")]
    [Fact]
    public void WeavelReplicaFormSyncPreservesAuthoritativeOwnerHealth()
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

            PlayerEntity authority = simulation.Scene.Players[0];
            authority.ServerActivate(0x51, Hunter.Weavel, team: 0);
            authority.Health = 60;
            authority.ModForceForm(altForm: true);
            SnapshotPlayer state = authority.CaptureServerState();
            Assert.Equal(30, state.Health);

            state.Slot = 1;
            state.ConnectionId = 0x52;
            PlayerEntity replica = simulation.Scene.Players[1];
            replica.ClientActivate(state);
            replica.ApplyServerState(state, newLife: true);

            Assert.Equal(state.Health, replica.Health);
            Assert.True(replica.Flags2.TestFlag(PlayerFlags2.Halfturret));

            state.Flags &= ~(SnapshotPlayerFlags.AltForm
                | SnapshotPlayerFlags.SpireAltAttack);
            replica.ApplyServerState(state, newLife: false);

            Assert.Equal(state.Health, replica.Health);
            Assert.False(replica.Flags2.TestFlag(PlayerFlags2.Halfturret));
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

    private static T GetPrivateField<T>(PlayerEntity player, string name)
    {
        FieldInfo field = typeof(PlayerEntity).GetField(name,
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(typeof(PlayerEntity).FullName, name);
        return (T)field.GetValue(player)!;
    }

    private static void SetPrivateField<T>(PlayerEntity player, string name, T value)
        => (typeof(PlayerEntity).GetField(name,
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(typeof(PlayerEntity).FullName, name))
            .SetValue(player, value);
}
