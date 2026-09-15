using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Mods.Network;
using MphRead.Sound;
using OpenTK.Mathematics;
using Xunit;

namespace MphRead.Tests;

[Collection("Match baseline globals")]
public sealed class SpireAltAttackTests
{
    [Trait("RequiresGameContent", "true")]
    [Fact]
    public void ClientActivationPreparesReplacementHunterModelsForPresentation()
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
            IScenePresentation presentation
                = DispatchProxy.Create<IScenePresentation, RecordingPresentationProxy>();
            var recording = (RecordingPresentationProxy)(object)presentation;
            simulation.Scene.Presentation = presentation;

            PlayerEntity authority = simulation.Scene.Players[0];
            authority.ServerActivate(0x51, Hunter.Kanden, team: 0);
            SnapshotPlayer state = authority.CaptureServerState();
            state.Slot = 1;
            state.ConnectionId = 0x52;
            PlayerEntity replica = simulation.Scene.Players[1];

            replica.ClientActivate(state);

            Assert.Contains(replica, recording.InitializedEntities);
            Assert.Equal(Hunter.Kanden, replica.Hunter);
            Assert.Contains("Kanden_lod0", recording.InitializedModelNames);
        }
        finally
        {
            Read.ServerMode = previousServerMode;
        }
    }

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
            state.AltAction = new AltActionState(AltActionPhase.Active, 8);

            Assert.True((state.Flags & SnapshotPlayerFlags.AltAttack) != 0);
            authority.Health = 0;
            Assert.False((authority.CaptureServerState().Flags
                & SnapshotPlayerFlags.AltAttack) != 0);

            state.Slot = 1;
            state.ConnectionId = 0x52;
            PlayerEntity replica = simulation.Scene.Players[1];
            replica.ClientActivate(state);
            replica.ApplyServerState(state, newLife: true);
            Assert.Equal(new AltActionState(AltActionPhase.Active, 8),
                replica.PresentedAltAction);
            Assert.False(replica.Flags2.TestFlag(PlayerFlags2.AltAttack));
            Assert.Equal((int)SpireAltAnim.Attack,
                replica._altModel.AnimInfo.Index[0]);
            Assert.Equal(replica.Position, replica._spireRockPosL);
            Assert.Equal(replica.Position, replica._spireRockPosR);
            Assert.Equal(PlayerEntity.ResolveAltActionPresentationFrame(
                    state.AltAction, replica._altModel.AnimInfo.FrameCount[0]),
                replica._altModel.AnimInfo.Frame[0]);

            uint epoch = replica.PresentationPoseEpoch;
            state.AltAction = new AltActionState(AltActionPhase.Active, 2);
            replica.ApplyServerState(state, newLife: false);
            Assert.True(replica.PresentationPoseEpoch > epoch);
            Assert.Equal(PlayerEntity.ResolveAltActionPresentationFrame(
                    state.AltAction, replica._altModel.AnimInfo.FrameCount[0]),
                replica._altModel.AnimInfo.Frame[0]);

            state.Flags &= ~SnapshotPlayerFlags.AltAttack;
            state.AltAction = AltActionState.None;
            replica.ApplyServerState(state, newLife: false);
            Assert.False(replica.Flags2.TestFlag(PlayerFlags2.AltAttack));
            Assert.Equal(AltActionState.None, replica.PresentedAltAction);
        }
        finally
        {
            Read.ServerMode = previousServerMode;
        }
    }

    [Trait("RequiresGameContent", "true")]
    [Fact]
    public void NoxusReplicaBootstrapsAndTearsDownPresentationAudio()
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
            authority.ServerActivate(0x61, Hunter.Noxus, team: 0);
            authority.ModForceForm(altForm: true);
            SnapshotPlayer state = authority.CaptureServerState();
            state.Flags |= SnapshotPlayerFlags.AltAttack;
            state.AltAction = new AltActionState(AltActionPhase.Charging,
                ushort.MaxValue);
            state.Slot = 1;
            state.ConnectionId = 0x62;
            PlayerEntity replica = simulation.Scene.Players[1];
            replica.ClientActivate(state);

            var requests = new List<AudioRequest>();
            simulation.Scene.Audio.Requested += requests.Add;
            replica.ApplyServerState(state, newLife: true);

            Assert.Contains(requests, request => request.Kind == AudioRequestKind.Play
                && request.Id == (int)SfxId.NOX_TOP_ATTACK2 && request.Loop);
            Assert.DoesNotContain(requests, request => request.Kind == AudioRequestKind.Play
                && request.Id == (int)SfxId.NOX_TOP_ATTACK1);

            requests.Clear();
            state.Flags &= ~SnapshotPlayerFlags.AltAttack;
            state.AltAction = AltActionState.None;
            replica.ApplyServerState(state, newLife: false);
            Assert.Contains(requests, request => request.Kind
                == AudioRequestKind.StopSourceSound
                && request.Id == (int)SfxId.NOX_TOP_ATTACK2);
            Assert.Contains(requests, request => request.Kind == AudioRequestKind.Play
                && request.Id == (int)SfxId.NOX_TOP_ATTACK3);

            // If packet loss hides the None phase, Active -> Charging is still
            // a new action and must retire/bootstrap its presentation audio.
            requests.Clear();
            state.Flags |= SnapshotPlayerFlags.AltAttack;
            state.AltAction = new AltActionState(AltActionPhase.Active, 4);
            replica.ApplyServerState(state, newLife: false);
            requests.Clear();
            state.AltAction = new AltActionState(AltActionPhase.Charging, 0);
            replica.ApplyServerState(state, newLife: false);
            Assert.Contains(requests, request => request.Kind
                == AudioRequestKind.StopSourceSound
                && request.Id == (int)SfxId.NOX_TOP_ATTACK2);

            requests.Clear();
            state.AltAction = new AltActionState(AltActionPhase.Charging,
                ushort.MaxValue);
            replica.ApplyServerState(state, newLife: false);
            Assert.Contains(requests, request => request.Kind == AudioRequestKind.Play
                && request.Id == (int)SfxId.NOX_TOP_ATTACK2 && request.Loop);

            requests.Clear();
            state.Flags |= SnapshotPlayerFlags.Spectating;
            replica.ApplyServerState(state, newLife: false);
            Assert.Contains(requests, request => request.Kind
                == AudioRequestKind.StopSourceSound
                && request.Id == (int)SfxId.NOX_TOP_ATTACK2);
            Assert.DoesNotContain(requests, request => request.Kind == AudioRequestKind.Play
                && request.Id == (int)SfxId.NOX_TOP_ATTACK3);
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
                | SnapshotPlayerFlags.AltAttack);
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

    private class RecordingPresentationProxy : DispatchProxy
    {
        public List<EntityBase> InitializedEntities { get; } = new();
        public List<string> InitializedModelNames { get; } = new();

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == nameof(IScenePresentation.InitEntity)
                && args is [EntityBase entity])
            {
                InitializedEntities.Add(entity);
                foreach (ModelInstance model in entity.GetModels())
                {
                    InitializedModelNames.Add(model.Model.Name);
                }
            }
            if (targetMethod?.ReturnType == null
                || targetMethod.ReturnType == typeof(void))
            {
                return null;
            }
            return targetMethod.ReturnType.IsValueType
                ? Activator.CreateInstance(targetMethod.ReturnType)
                : null;
        }
    }
}
