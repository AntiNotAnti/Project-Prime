using System;
using MphRead.Mods.Network;
using OpenTK.Mathematics;
using Xunit;

namespace MphRead.Tests
{
    public sealed class ProjectilePolicyTests
    {
        private static BeamMechanics Ordinary => new(BeamType.PowerBeam, BeamType.PowerBeam, false, false, 0, 1, 2);
        [Theory]
        [InlineData(false, false, LagCompensationMode.None, LagCompensationMode.None)]
        [InlineData(false, true, LagCompensationMode.None, LagCompensationMode.None)]
        [InlineData(true, false, LagCompensationMode.None, LagCompensationMode.HistoricalTrace)]
        [InlineData(true, true, LagCompensationMode.ProjectileCatchUp, LagCompensationMode.HistoricalTrace)]
        public void ImmutableServerOptionsControlBothTimingModes(bool lag, bool catchUp,
            LagCompensationMode projectileMode, LagCompensationMode traceMode)
        {
            var combat = new ServerCombat(lag, catchUp);
            Assert.Equal(lag, combat.LagCompEnabled);
            Assert.Equal(lag && catchUp, combat.ProjectileCatchUpEnabled);
            using var scope = combat.Enter(100);
            combat.SetCommand(0, new(1, 1, 90, 0, 0, Vector3.UnitZ, InputCommand.NoWeapon), 150);
            CombatActor actor = new(0, 1, 1);
            var projectile = combat.CaptureShot(actor, Ordinary);
            var trace = combat.CaptureShot(actor, Ordinary with { Beam = BeamType.Imperialist, BeamKind = BeamType.Imperialist, Speed = 100 });
            Assert.Equal(projectileMode, projectile.Mode);
            Assert.Equal(traceMode, trace.Mode);
            Assert.Equal(projectileMode == LagCompensationMode.None ? 0u : 10u, projectile.RewindTicks);
            Assert.Equal(traceMode == LagCompensationMode.None ? 0u : 10u, trace.RewindTicks);
            Assert.Equal(2, combat.ShotsConsidered);
        }
        [Fact]
        public void PolicyRejectsSpecialOrInvalidPhysicsWithoutGuessingFromWeaponName()
        {
            foreach (var mechanics in new[]
            {
                Ordinary with { Continuous = true }, Ordinary with { InstantArea = true }, Ordinary with { Homing = 0.01f },
                Ordinary with { Speed = 0 }, Ordinary with { Speed = Single.NaN },
                Ordinary with { Lifespan = 0 }, Ordinary with { Lifespan = Single.PositiveInfinity },
                Ordinary with { BeamKind = BeamType.Imperialist }
            }) Assert.Equal(LagCompensationMode.None, LagCompensationPolicy.GetMode(mechanics));
            Assert.Equal(15u, LagCompensationPolicy.MaxProjectileFastForwardTicks);
            Assert.Equal(32, LagCompensationHistory.Capacity);
        }
    }
}
