using System.Numerics;

namespace MphRead.CorpsePhysics;

public static class ReferenceScene
{
    // Original primitive mannequin calibrated to measured Samus reference
    // landmarks. No game mesh, animation, texture, muzzle or collision asset.
    // Right wrist, hands and soles are authored virtual endpoints.
    public static CorpseRig Samus()
    {
        Vector3 pelvis = new(0, 1.184f, -.039f), shoulders = new(0, 1.515f, -.045f);
        Vector3 lh = new(-.107f, 1.095f, -.026f), lk = new(-.107f, .627f, -.064f), la = new(-.107f, .115f, 0);
        Vector3 rh = new(.107f, 1.095f, -.026f), rk = new(.107f, .627f, -.064f), ra = new(.107f, .115f, 0);
        Vector3 ls = new(-.295f, 1.515f, -.045f), le = new(-.530f, 1.508f, -.034f), lw = new(-.811f, 1.497f, -.049f);
        Vector3 rs = new(.295f, 1.515f, -.045f), re = new(.530f, 1.508f, -.034f), rw = new(.811f, 1.497f, -.049f);
        var parts = new CorpsePart[CorpseRig.PartCount];
        CorpsePart Box(Vector3 at, Vector3 size, float mass, int parent, Vector3 joint, float swing, float twist)
            => new(new(at, Quaternion.Identity), size, mass, parent, joint, swing, twist);
        CorpsePart Limb(Vector3 from, Vector3 to, float width, float mass, int parent, float swing = 1, float twist = .3f)
            => new(new((from + to) * .5f, Frame(to - from)), new(width, (to - from).Length(), width), mass, parent, from, swing, twist);
        CorpsePart Foot(Vector3 ankle, int parent)
            => new(new(ankle + new Vector3(0, -.055f, -.055f), Frame(-Vector3.UnitZ)), new(.13f, .24f, .12f), .75f, parent, ankle, .45f, .2f);
        parts[0] = Box(pelvis, new(.29f, .22f, .22f), 3, -1, pelvis, .5f, .2f);
        parts[1] = Limb(pelvis, shoulders, .32f, 4, 0, .45f, .25f);
        parts[2] = Box(new(0, 1.704f, -.064f), new(.20f), 1, 1, new(0, 1.664f, -.064f), .65f, .4f);
        parts[3] = Limb(ls, le, .10f, .8f, 1, 1.3f, .45f);
        parts[4] = Limb(le, lw, .09f, .7f, 3, 1.2f, .25f);
        parts[5] = Limb(lw, lw + Vector3.Normalize(lw - le) * .12f, .07f, .5f, 4, .45f, .2f);
        parts[6] = Limb(rs, re, .10f, .8f, 1, 1.3f, .45f);
        parts[7] = Limb(re, rw, .09f, .7f, 6, 1.2f, .25f);
        parts[8] = Limb(rw, rw + Vector3.Normalize(rw - re) * .12f, .07f, .5f, 7, .45f, .2f);
        parts[9] = Limb(lh, lk, .14f, 2, 0, 1.1f, .3f);
        parts[10] = Limb(lk, la, .13f, 1.5f, 9, 1.2f, .2f);
        parts[11] = Foot(la, 10);
        parts[12] = Limb(rh, rk, .14f, 2, 0, 1.1f, .3f);
        parts[13] = Limb(rk, ra, .13f, 1.5f, 12, 1.2f, .2f);
        parts[14] = Foot(ra, 13);
        return new("Samus reference primitives v1", parts);
    }
    public static Quaternion Frame(Vector3 longitudinal)
    {
        Vector3 y = Vector3.Normalize(longitudinal);
        Vector3 z = MathF.Abs(Vector3.Dot(y, Vector3.UnitZ)) > .95f ? Vector3.UnitY : Vector3.UnitZ;
        z = Vector3.Normalize(z - y * Vector3.Dot(y, z)); Vector3 x = Vector3.Normalize(Vector3.Cross(y, z));
        z = Vector3.Cross(x, y);
        return Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(new(
            x.X, x.Y, x.Z, 0, y.X, y.Y, y.Z, 0, z.X, z.Y, z.Z, 0, 0, 0, 0, 1)));
    }
    public static CorpseStaticWorld Floor(bool varied = false)
    {
        var triangles = new List<StaticTriangle>();
        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        { triangles.Add(new(a, b, c)); triangles.Add(new(a, c, d)); }
        Quad(new(-8, 0, -8), new(-8, 0, 8), new(8, 0, 8), new(8, 0, -8));
        if (varied)
        {
            Quad(new(0, 0, 1), new(0, 0, 5), new(4, .8f, 5), new(4, .8f, 1));
            for (int i = 0; i < 3; i++)
            {
                float a = 1 + i, b = a + 1, h = (i + 1) * .2f;
                Quad(new(a, h, -5), new(a, h, -2), new(b, h, -2), new(b, h, -5));
                Quad(new(a, 0, -5), new(a, h, -5), new(b, h, -5), new(b, 0, -5));
                Quad(new(b, 0, -2), new(b, h, -2), new(a, h, -2), new(a, 0, -2));
                Quad(new(a, 0, -2), new(a, h, -2), new(a, h, -5), new(a, 0, -5));
                Quad(new(b, 0, -5), new(b, h, -5), new(b, h, -2), new(b, 0, -2));
            }
        }
        return new(triangles.ToArray());
    }
}
