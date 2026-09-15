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
    [Fact]
    public void OwnerAttachmentUsesCompletedTransformsWithoutMutatingOwner()
    {
        var history = new ParticlePoseHistory();
        var owner = new EffectElementEntry(new MphRead.MatchRandom())
        {
            Flags = EffElemFlags.UseTransform,
            Transform = Matrix4.CreateTranslation(0, 0, 0)
        };
        var particle = new EffectParticle { Owner = owner };
        owner.Particles.Add(particle);
        history.Capture(new[] { owner }, 1, 0);
        owner.Transform = Matrix4.CreateTranslation(2, 0, 0);
        history.Capture(new[] { owner }, 2, 0);
        Assert.Equal(1, history.ResolveOwner(owner, .5f, true).M41);
        Assert.Equal(2, owner.Transform.M41);
        Assert.Equal(2, history.ResolveOwner(owner, .5f, false).M41);
        Assert.Equal(2, history.ResolveOwner(owner, .5f, true,
            presentationResolved: true).M41);
    }

    [Fact]
    public void AttachedMeshDrawUsesResolvedOwnerTransform()
    {
        var owner = new EffectElementEntry(new MphRead.MatchRandom())
        {
            Flags = EffElemFlags.UseTransform,
            Transform = Matrix4.CreateTranslation(4, 0, 0)
        };
        var particle = new EffectParticle
        {
            Owner = owner,
            Position = Vector3.UnitX,
            Alpha = 1,
            Scale = 1,
            SetVecsId = 5,
            DrawId = 7
        };
        Matrix4 resolvedOwner = Matrix4.CreateTranslation(2, 0, 0);

        particle.InvokeSetVecsFunc(Matrix4.Identity);
        particle.InvokeDrawFunc(1, resolvedOwner);

        Assert.True(particle.DrawNode);
        Assert.Equal(3, particle.NodeTransform.M41);
        Assert.Equal(4, owner.Transform.M41);
    }

    [Fact]
    public void MeshEffectWithoutOwnerTransformKeepsCurrentParticlePose()
    {
        var history = new ParticlePoseHistory();
        var owner = new EffectElementEntry(new MphRead.MatchRandom())
        {
            Flags = EffElemFlags.UseMesh
        };
        var particle = new EffectParticle { Owner = owner, Position = Vector3.UnitX };
        owner.Particles.Add(particle);
        history.Capture(new[] { owner }, 1, 0);
        Assert.Equal(1, history.Resolve(particle, 0, true).M41);
    }
}
