using MphRead;
using MphRead.Effects;
using OpenTK.Mathematics;
using Xunit;
namespace MphRead.Tests;
public class ParticleInterpolationTests
{
    [Fact]
    public void IndependentSpriteUsesCompletedEffectStepsWithoutAdvancingParticle()
    {
        var history = new ParticlePoseHistory();
        var owner = new EffectElementEntry(new MphRead.MatchRandom());
        var particle = new EffectParticle { Owner = owner, Position = Vector3.Zero, Lifespan = 5 };
        owner.Particles.Add(particle);
        history.Capture(new[] { owner }, 1, 0);
        particle.Position = new Vector3(2, 0, 0);
        history.Capture(new[] { owner }, 2, 0);
        Assert.Equal(1, history.Resolve(particle, .5f, true).M41);
        Assert.Equal(1.5f, history.Resolve(particle, .75f, true).M41);
        Assert.Equal(2, particle.Position.X);
        Assert.Equal(5, particle.Lifespan);
        Assert.Equal(2, history.Resolve(particle, .5f, false).M41);
        history.Remove(particle); // pool reuse, even within the same effect tick
        particle.Position = new Vector3(3, 0, 0);
        history.Capture(new[] { owner }, 3, 0);
        Assert.Equal(3, history.Resolve(particle, 0, true).M41);
    }
    [Theory]
    [InlineData(EffElemFlags.UseTransform)]
    [InlineData(EffElemFlags.UseMesh)]
    public void OwnerAttachmentsAndMeshEffectsKeepCurrentPose(EffElemFlags flags)
    {
        var history = new ParticlePoseHistory();
        var owner = new EffectElementEntry(new MphRead.MatchRandom()) { Flags = flags };
        var particle = new EffectParticle { Owner = owner };
        owner.Particles.Add(particle);
        history.Capture(new[] { owner }, 1, 0);
        particle.Position = Vector3.UnitX;
        history.Capture(new[] { owner }, 2, 0);
        Assert.Equal(1, history.Resolve(particle, 0, true).M41);
    }
}
