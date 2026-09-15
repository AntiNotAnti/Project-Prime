using System;
using System.IO;
using System.Reflection;
using MphRead.Entities;
using MphRead.Mods.Network;
using OpenTK.Mathematics;
using Xunit;

namespace MphRead.Tests;

[Collection("Match baseline globals")]
public sealed class GuardianIntegrationTests
{
    [Trait("RequiresGameContent", "true")]
    [Fact]
    public void GuardianLoadsPsychoBitAndFiresAttributedPowerBeam()
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

            PlayerEntity guardian = simulation.Scene.Players[0];
            guardian.ServerActivate(0x47, Hunter.Guardian, team: 0);
            Assert.Equal("Guardian_lod0", guardian._bipedModelLods[0].Model.Name);
            Assert.Equal("PsychoBit", guardian._altModel.Model.Name);
            Assert.Equal(5, guardian._altModel.Model.Recolors.Count);
            Assert.Equal(0, guardian.Values.AltFormStrafe);
            Assert.True(guardian.UsesStrafeAltMovement);

            guardian.ModStartFormSwitch();
            Assert.Equal(CameraType.Third2, guardian.CameraType);

            PlayerEntity samus = simulation.Scene.Players[1];
            samus.ServerActivate(0x48, Hunter.Samus, team: 1);
            Assert.False(samus.UsesStrafeAltMovement);
            samus.ModStartFormSwitch();
            Assert.Equal(CameraType.Third1, samus.CameraType);

            guardian.ModForceForm(altForm: true);
            SetPrivateField(guardian, "_altAttackTime",
                (ushort)SimTicks.From30HzFrames(guardian.Values.AltAttackStartup));
            MethodInfo fire = typeof(PlayerEntity).GetMethod(
                "FireGuardianPsychoBitBeam",
                BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(typeof(PlayerEntity).FullName,
                    "FireGuardianPsychoBitBeam");

            fire.Invoke(guardian, null);

            BeamProjectileEntity beam = Assert.Single(guardian.EquipInfo.Beams,
                value => value.Lifespan > 0);
            Assert.Equal(BeamType.PowerBeam, beam.Beam);
            Assert.True(beam.Flags.TestFlag(BeamFlags.FromAlt));
            Assert.True(beam.CombatShot.SourceAltForm);
            Assert.True(beam.CombatShot.Affinity);
            Assert.Equal(guardian.Values.AltAttackDamage, beam.Damage);
            Assert.Equal(guardian.Values.AltAttackDamage, beam.HeadshotDamage);
        }
        finally
        {
            Read.ServerMode = previousServerMode;
        }
    }

    [Trait("RequiresGameContent", "true")]
    [Fact]
    public void GuardianAltAttackReplicatesAsGenericProtocol25State()
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
            authority.ServerActivate(0x47, Hunter.Guardian, team: 0);
            authority.ModForceForm(altForm: true);
            authority.Flags2 |= PlayerFlags2.AltAttack;
            SnapshotPlayer state = authority.CaptureServerState();
            Assert.True(state.Flags.TestFlag(SnapshotPlayerFlags.AltAttack));

            state.Slot = 1;
            state.ConnectionId = 0x48;
            PlayerEntity replica = simulation.Scene.Players[1];
            replica.ClientActivate(state);
            replica.ApplyServerState(state, newLife: true);
            Assert.Equal(Hunter.Guardian, replica.Hunter);
            Assert.True(replica.IsAltForm);
            Assert.Equal(new AltActionState(AltActionPhase.Active, 0),
                replica.PresentedAltAction);
            Assert.False(replica.Flags2.TestFlag(PlayerFlags2.AltAttack));
            Assert.Equal((int)PsychoBitAltAnim.Beam,
                replica._altModel.AnimInfo.Index[0]);
        }
        finally
        {
            Read.ServerMode = previousServerMode;
        }
    }

    [Trait("RequiresGameContent", "true")]
    [Fact]
    public void GuardianBotChargesAndFiresThroughAuthoritativeInputPipeline()
    {
        bool previousServerMode = Read.ServerMode;
        try
        {
            using var saved = ServerContent.PreserveContext("AMHE1");
            ServerContent.Open(FindAmhe1(), "AMHE1");
            using var simulation = new ServerSimulation(new MatchRules(
                MatchMode.Battle, "MP1 SANCTORUS"));
            Scene scene = simulation.Scene;

            PlayerEntity guardian = scene.Players[0];
            PlayerEntity target = scene.Players[1];
            guardian.ServerActivate(0x71, Hunter.Guardian, team: 0,
                botSkill: (int)BotDifficulty.Expert);
            target.ServerActivate(0x72, Hunter.Samus, team: 1);
            scene.Players.ActiveCount = 2;
            scene.Match.Phase = MatchPhase.Playing;
            guardian.ModForceForm(altForm: true);

            Vector3 facing = VectorMath.NormalizeHorizontalOr(
                guardian.FacingVector, -Vector3.UnitZ);
            target.Reposition(guardian.Position + facing * 2,
                -facing, guardian.NodeRef);

            int firstBeamTick = -1;
            BeamProjectileEntity? fired = null;
            const int maxTicks = 360;
            for (int tick = 0; tick < maxTicks; tick++)
            {
                scene.StepHeadlessFrame(advanceMatch: false);
                foreach (BeamProjectileEntity beam in guardian.EquipInfo.Beams)
                {
                    if (beam.Lifespan > 0 && beam.Flags.TestFlag(BeamFlags.FromAlt))
                    {
                        firstBeamTick = tick;
                        fired = beam;
                        break;
                    }
                }
                if (fired != null)
                {
                    break;
                }
            }

            Assert.True(firstBeamTick >= 0 && firstBeamTick < maxTicks,
                $"guardianAlt={guardian.IsAltForm} health={guardian.Health} "
                + $"targetHealth={target.Health} aiFlags={guardian.AiData.Flags2} "
                + $"altAttackDown={guardian.Controls.AltAttack.IsDown}");
            Assert.NotNull(fired);
            Assert.Equal(BeamType.PowerBeam, fired!.Beam);
            Assert.True(fired.Flags.TestFlag(BeamFlags.FromAlt));
            Assert.True(fired.CombatShot.SourceAltForm,
                $"tick={firstBeamTick} guardianAlt={guardian.IsAltForm} "
                + $"guardianFlags={guardian.Flags1} shotActor={fired.CombatShot.Actor}");
            Assert.True(fired.CombatShot.Affinity);
            Assert.Equal(guardian.ServerCombatIdentity, fired.CombatShot.Actor);
            Assert.InRange(firstBeamTick,
                SimTicks.From30HzFrames(guardian.Values.AltAttackStartup),
                maxTicks - 1);
            Assert.True(guardian._altAttackCooldown > 0);
        }
        finally
        {
            Read.ServerMode = previousServerMode;
        }
    }

    [Trait("RequiresGameContent", "true")]
    [Fact]
    public void GuardianBotAcquiresAndAttacksAVisibleOpponent()
    {
        bool previousServerMode = Read.ServerMode;
        try
        {
            using var saved = ServerContent.PreserveContext("AMHE1");
            ServerContent.Open(FindAmhe1(), "AMHE1");
            using var simulation = new ServerSimulation(new MatchRules(
                MatchMode.Battle, "MP1 SANCTORUS"));
            Scene scene = simulation.Scene;

            PlayerEntity guardian = scene.Players[0];
            PlayerEntity target = scene.Players[1];
            guardian.ServerActivate(0x81, Hunter.Guardian, team: 0,
                botSkill: (int)BotDifficulty.Expert);
            target.ServerActivate(0x82, Hunter.Samus, team: 1);
            scene.Players.ActiveCount = 2;
            scene.Match.Phase = MatchPhase.Playing;

            Vector3 facing = VectorMath.NormalizeHorizontalOr(
                guardian.FacingVector, -Vector3.UnitZ);
            target.Reposition(guardian.Position + facing * 2,
                -facing, guardian.NodeRef);

            BeamProjectileEntity? fired = null;
            const int maxTicks = 600;
            for (int tick = 0; tick < maxTicks && fired == null; tick++)
            {
                scene.StepHeadlessFrame(advanceMatch: false);
                foreach (BeamProjectileEntity beam in guardian.EquipInfo.Beams)
                {
                    if (beam.Lifespan > 0)
                    {
                        fired = beam;
                        break;
                    }
                }
            }

            Assert.NotNull(fired);
            Assert.Equal(guardian.ServerCombatIdentity, fired!.CombatShot.Actor);
        }
        finally
        {
            Read.ServerMode = previousServerMode;
        }
    }

    private static void SetPrivateField<T>(PlayerEntity player, string name,
        T value)
        => (typeof(PlayerEntity).GetField(name,
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(typeof(PlayerEntity).FullName,
                name)).SetValue(player, value);

    private static string FindAmhe1()
    {
        string? configured = Environment.GetEnvironmentVariable(
            "GAME_DATA_DIRECTORY");
        string[] starts = configured is null
            ? new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory }
            : new[] { configured, Directory.GetCurrentDirectory(),
                AppContext.BaseDirectory };
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
        throw new DirectoryNotFoundException(
            "AMHE1 extracted content was not found.");
    }
}
