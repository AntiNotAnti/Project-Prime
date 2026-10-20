using System.Numerics;
using BepuPhysics;
using BepuPhysics.Collidables;
using BepuPhysics.CollisionDetection;
using BepuPhysics.Constraints;
using BepuUtilities;

namespace MphRead.CorpsePhysics;

internal struct CorpseContacts : INarrowPhaseCallbacks
{
    public void Initialize(Simulation simulation) { }
    public bool AllowContactGeneration(int worker, CollidableReference a, CollidableReference b, ref float margin)
        => a.Mobility == CollidableMobility.Dynamic && b.Mobility == CollidableMobility.Static;
    public bool AllowContactGeneration(int worker, CollidablePair pair, int childA, int childB) => true;
    public bool ConfigureContactManifold<T>(int worker, CollidablePair pair, ref T manifold, out PairMaterialProperties material)
        where T : unmanaged, IContactManifold<T>
    {
        material = new(.8f, 2, new SpringSettings(20, 1)); return true;
    }
    public bool ConfigureContactManifold(int worker, CollidablePair pair, int childA, int childB, ref ConvexContactManifold manifold) => true;
    public void Dispose() { }
}
internal struct CorpseGravity : IPoseIntegratorCallbacks
{
    private Vector<float> _gravity, _linearDamping, _angularDamping;
    public AngularIntegrationMode AngularIntegrationMode => AngularIntegrationMode.Nonconserving;
    public bool AllowSubstepsForUnconstrainedBodies => false;
    public bool IntegrateVelocityForKinematics => false;
    public void Initialize(Simulation simulation) { }
    public void PrepareForIntegration(float dt)
    {
        _gravity = new(-9.81f * dt); _linearDamping = new(MathF.Pow(.35f, dt)); _angularDamping = new(MathF.Pow(.1f, dt));
    }
    public void IntegrateVelocity(Vector<int> indices, Vector3Wide position, QuaternionWide orientation,
        BodyInertiaWide inertia, Vector<int> mask, int worker, Vector<float> dt, ref BodyVelocityWide velocity)
    {
        velocity.Linear.X *= _linearDamping; velocity.Linear.Y = (velocity.Linear.Y + _gravity) * _linearDamping;
        velocity.Linear.Z *= _linearDamping; velocity.Angular.X *= _angularDamping;
        velocity.Angular.Y *= _angularDamping; velocity.Angular.Z *= _angularDamping;
    }
}
