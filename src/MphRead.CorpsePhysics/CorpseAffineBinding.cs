using System.Numerics;

namespace MphRead.CorpsePhysics;

// A corpse-owned copy of the actual composed skin pose. It contains no engine,
// model, node, palette, asset, actor or authority reference. Collision frames
// are rigid; residual native scale/shear and bone-to-body offsets stay affine.
// Capture each LOD independently. A layout match never proves equal bind poses.
public sealed class CorpseAffineBinding
{
    public const int MaximumNodes = 128;
    private readonly Matrix4x4[] _offsets;
    private readonly byte[] _bodies;
    public int NodeCount => _offsets.Length;
    private CorpseAffineBinding(Matrix4x4[] offsets, byte[] bodies)
    { _offsets = offsets; _bodies = bodies; }

    public static bool TryCapture(ReadOnlySpan<Matrix4x4> composedNodes,
        ReadOnlySpan<byte> bodyForNode, ReadOnlySpan<CorpsePose> initialBodies,
        out CorpseAffineBinding? binding)
    {
        binding = null;
        if (composedNodes.Length is < 1 or > MaximumNodes || bodyForNode.Length != composedNodes.Length
            || initialBodies.Length != CorpseRig.PartCount) return false;
        foreach (var pose in initialBodies) if (!pose.IsValid) return false;
        Span<Matrix4x4> offsets = stackalloc Matrix4x4[composedNodes.Length];
        for (int n = 0; n < composedNodes.Length; n++)
        {
            byte part = bodyForNode[n]; var node = composedNodes[n];
            if (part >= CorpseRig.PartCount || !Affine(node)) return false;
            var inverse = Matrix4x4.CreateFromQuaternion(
                Quaternion.Conjugate(Quaternion.Normalize(initialBodies[part].Orientation)));
            // Subtract world origins before rotation instead of multiplying
            // two large cancelling translations. Keep the complete linear part.
            node.Translation -= initialBodies[part].Position;
            var offset = node * inverse;
            if (!Affine(offset) || offset.Translation.LengthSquared() > 16) return false;
            offsets[n] = offset;
        }
        binding = new(offsets.ToArray(), bodyForNode.ToArray());
        return true;
    }

    // Immutable binding + per-call bounded stack scratch permits concurrent
    // readers. Validate the entire result before touching caller-owned storage.
    public bool TrySample(ReadOnlySpan<CorpsePose> bodies, Span<Matrix4x4> destination)
    {
        if (bodies.Length != CorpseRig.PartCount || destination.Length < NodeCount) return false;
        foreach (var pose in bodies) if (!pose.IsValid) return false;
        Span<Matrix4x4> frames = stackalloc Matrix4x4[CorpseRig.PartCount];
        for(int i=0;i<frames.Length;i++)
        {
            frames[i]=Matrix4x4.CreateFromQuaternion(Quaternion.Normalize(bodies[i].Orientation));
            frames[i].Translation=bodies[i].Position;
        }
        Span<Matrix4x4> next = stackalloc Matrix4x4[NodeCount];
        for (int n = 0; n < NodeCount; n++)
        {
            var matrix = _offsets[n] * frames[_bodies[n]];
            if (!Affine(matrix)) return false;
            next[n] = matrix;
        }
        next.CopyTo(destination);
        return true;
    }

    private static bool Affine(Matrix4x4 m)
        => m.M14 == 0 && m.M24 == 0 && m.M34 == 0 && m.M44 == 1
        && float.IsFinite(m.M11) && float.IsFinite(m.M12) && float.IsFinite(m.M13)
        && float.IsFinite(m.M21) && float.IsFinite(m.M22) && float.IsFinite(m.M23)
        && float.IsFinite(m.M31) && float.IsFinite(m.M32) && float.IsFinite(m.M33)
        && Limits.Position(m.Translation)
        && new Vector3(m.M11, m.M12, m.M13).LengthSquared() <= 64
        && new Vector3(m.M21, m.M22, m.M23).LengthSquared() <= 64
        && new Vector3(m.M31, m.M32, m.M33).LengthSquared() <= 64;
}
